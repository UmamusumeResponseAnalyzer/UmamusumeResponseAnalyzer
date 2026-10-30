using System.Drawing;
using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;
using UmamusumeResponseAnalyzer.TerminalGui;
using Xunit;

namespace UmamusumeResponseAnalyzer.Tests;

[Collection("PluginRuntime")]
public sealed class BootstrapMenuTests : IDisposable
{
    readonly TerminalGuiTestApp terminal = new();
    readonly UiHost host;
    readonly Task run;

    public BootstrapMenuTests(PluginRuntimeFixture fixture)
    {
        UiHost? created = null;
        terminal.RunOnOwnerThread(() => created = new UiHost(
            terminal.Application, SynchronizationContext.Current!, CancellationToken.None));
        host = created!;
        run = terminal.StartAsync(host).GetAwaiter().GetResult();
    }

    public void Dispose()
    {
        try { terminal.StopAsync(host, run).GetAwaiter().GetResult(); }
        finally { terminal.Dispose(); }
    }

    [Fact]
    public async Task PreparationActionsAndStartupKeepOneViewAndDoNotRepeatActions()
    {
        var bootstrap = host.Bootstrap;
        bootstrap.ShowPreparing();
        bootstrap.SetSettings(new("test-version", "work", "config.yaml", "127.0.0.1", 4693, "日文", "女", "source"));
        await host.FlushAsync();
        var view = await terminal.InvokeAsync(() => Descendants(terminal.Application.TopRunnableView!)
            .OfType<BootstrapView>().Single());
        var start = await terminal.InvokeAsync(() => (Button)Descendants(view).Single(v => v.Id == "bootstrap-start"));
        var options = await terminal.InvokeAsync(() => (Button)Descendants(view).Single(v => v.Id == "bootstrap-options"));
        await terminal.InvokeAsync(() =>
        {
            Assert.False(Descendants(view).Single(v => v.Id == "bootstrap-settings-toggle").Visible);
            Assert.False(Descendants(view).Single(v => v.Id == "bootstrap-details-toggle").Visible);
        });
        Assert.False(await terminal.InvokeAsync(() => start.Enabled));
        await terminal.ClickAsync(await terminal.InvokeAsync(() => start.ViewportToScreen(new Point(1, 0))));
        await terminal.InjectAsync(Key.Enter);
        Assert.False(await terminal.InvokeAsync(() => Descendants(terminal.Application.TopRunnableView!)
            .OfType<CommandModeView>().Single().IsOpen));

        var choice = bootstrap.ShowMenuAsync(TestContext.Current.CancellationToken);
        await host.FlushAsync();
        var readyScreen = await terminal.CaptureScreenAsync();
        TestContext.Current.TestOutputHelper?.WriteLine($"UiHost Bootstrap ready 80x24\n{readyScreen}");
        foreach (var label in new[] { Localization.LaunchMenu.I18N_Start, Localization.LaunchMenu.I18N_Options,
                     Localization.LaunchMenu.I18N_PluginRepository, Localization.LaunchMenu.I18N_UpdateAssets, Localization.LaunchMenu.I18N_More })
            Assert.Contains(label, readyScreen);
        var summary = await terminal.InvokeAsync(() => Descendants(view).Single(v => v.Id == "bootstrap-summary"));
        Assert.Equal(view.Viewport.Width - 4, await terminal.InvokeAsync(() => summary.Frame.Width));
        Assert.Contains(summary.Text, readyScreen);
        Assert.Same(view, await terminal.InvokeAsync(() => Descendants(terminal.Application.TopRunnableView!)
            .OfType<BootstrapView>().Single()));
        Assert.True(await terminal.InvokeAsync(() => start.Enabled));
        await terminal.InvokeAsync(() =>
        {
            Assert.True(Descendants(view).Single(v => v.Id == "bootstrap-settings-toggle").Visible);
            Assert.False(Descendants(view).Single(v => v.Id == "bootstrap-details-toggle").Visible);
            Assert.False(Descendants(view).Single(v => v.Id == "bootstrap-diagnostics-toggle").Visible);
        });
        Assert.Same(start, await terminal.InvokeAsync(() => terminal.Application.TopRunnableView!.MostFocused));
        await terminal.InjectAsync(Key.Tab);
        Assert.Same(options, await terminal.InvokeAsync(() => terminal.Application.TopRunnableView!.MostFocused));

        bootstrap.Log("test", "background-log");
        bootstrap.SetPhase(BootstrapPhaseId.Scan, BootstrapPhaseState.Succeeded, "scan-complete");
        await host.FlushAsync();
        Assert.Same(options, await terminal.InvokeAsync(() => terminal.Application.TopRunnableView!.MostFocused));
        await terminal.InjectAsync(Key.Enter);
        Assert.Equal(BootstrapAction.Options, await choice.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.False(await terminal.InvokeAsync(() => options.Enabled));
        await terminal.InjectAsync(Key.Enter);
        Assert.False(await terminal.InvokeAsync(() => Descendants(terminal.Application.TopRunnableView!)
            .OfType<CommandModeView>().Single().IsOpen));

        var next = bootstrap.ShowMenuAsync(TestContext.Current.CancellationToken);
        await host.FlushAsync();
        await terminal.ClickAsync(await terminal.InvokeAsync(() => start.ViewportToScreen(new Point(1, 0))));
        Assert.Equal(BootstrapAction.Start, await next.WaitAsync(TimeSpan.FromSeconds(5)));
        await host.FlushAsync();
        await terminal.InvokeAsync(() =>
        {
            Assert.False(Descendants(view).Single(v => v.Id == "bootstrap-settings-toggle").Visible);
            Assert.False(Descendants(view).Single(v => v.Id == "bootstrap-details-toggle").Visible);
        });
        bootstrap.ShowInformation();
        bootstrap.SetPhase(BootstrapPhaseId.Database, BootstrapPhaseState.Running, "loading-data");
        await host.FlushAsync();
        await terminal.WaitForScreenAsync("loading-data");
        Assert.False(await terminal.InvokeAsync(() => Descendants(view).Single(v => v.Id == "bootstrap-details-toggle").Visible));
        bootstrap.SetPhase(BootstrapPhaseId.Database, BootstrapPhaseState.Succeeded, "data-ready");
        bootstrap.SetListening("127.0.0.1:4693");
        await host.FlushAsync();
        await terminal.WaitForScreenAsync("127.0.0.1:4693");
        await terminal.InvokeAsync(() =>
        {
            Assert.False(Descendants(view).Single(v => v.Id == "bootstrap-settings-toggle").Visible);
            Assert.True(Descendants(view).Single(v => v.Id == "bootstrap-details-toggle").Visible);
        });
        Assert.Same(view, await terminal.InvokeAsync(() => Descendants(terminal.Application.TopRunnableView!)
            .OfType<BootstrapView>().Single()));
        Assert.All(await terminal.InvokeAsync(() => Descendants(view).OfType<Button>()
            .Where(button => new[] { "bootstrap-start", "bootstrap-options", "bootstrap-plugins", "bootstrap-update-data" }.Contains(button.Id))
            .Select(button => button.Enabled).ToArray()), Assert.False);
    }

    [Fact]
    public async Task MoreAndDetailsPreserveFocusAcrossUpdatesAndEscapeReturnsToMore()
    {
        var bootstrap = host.Bootstrap;
        var updateSource = "https://updates.example.invalid/" + new string('p', 120) + "/diagnostic-end";
        bootstrap.SetSettings(new("test-version", "work", "config.yaml", "127.0.0.1", 4693, "日文", "女", updateSource));
        using var cancellation = new CancellationTokenSource();
        var choice = bootstrap.ShowMenuAsync(cancellation.Token);
        await host.FlushAsync();
        var view = await terminal.InvokeAsync(() => Descendants(terminal.Application.TopRunnableView!).OfType<BootstrapView>().Single());
        var more = await terminal.InvokeAsync(() => Descendants(view).Single(v => v.Id == "bootstrap-more"));
        await terminal.InvokeAsync(() => more.SetFocus());
        await terminal.InjectAsync(Key.Enter);
        await terminal.WaitForScreenAsync(Localization.LaunchMenu.I18N_UpdateProgram);
        var popover = await terminal.InvokeAsync(() => Assert.IsAssignableFrom<View>(terminal.Application.Popovers!.GetActivePopover()));
        await terminal.InvokeAsync(() =>
        {
            var menu = Descendants(popover).OfType<Menu>().Single();
            Assert.Equal(LineStyle.Single, menu.BorderStyle);
            var items = menu.SubViews.OfType<MenuItem>().ToArray();
            foreach (var item in items)
            {
                Assert.Equal(0, item.Frame.Left);
                Assert.Equal(menu.Viewport.Width, item.Frame.Width);
                Assert.InRange(item.Frame.Bottom, 1, menu.Viewport.Height);
                var command = Assert.IsAssignableFrom<View>(item.CommandView);
                Assert.InRange(command.Frame.Left, 0, item.Viewport.Width);
                Assert.InRange(command.Frame.Right, 0, item.Viewport.Width);
            }
        });
        await terminal.InjectAsync(Key.CursorDown);
        var focused = await terminal.InvokeAsync(() => popover.MostFocused);
        bootstrap.Log("test", "menu-stays-open");
        await host.FlushAsync();
        Assert.Same(popover, await terminal.InvokeAsync(() => terminal.Application.Popovers!.GetActivePopover()));
        Assert.Same(focused, await terminal.InvokeAsync(() => popover.MostFocused));
        Assert.Contains(Localization.LaunchMenu.I18N_UpdateProgram, await terminal.CaptureScreenAsync());
        await terminal.InjectAsync(Key.Esc);
        Assert.Same(more, await terminal.InvokeAsync(() => terminal.Application.TopRunnableView!.MostFocused));
        Assert.False(choice.IsCompleted);

        var toggle = await terminal.InvokeAsync(() => Descendants(view).Single(v => v.Id == "bootstrap-settings-toggle"));
        await terminal.InvokeAsync(() => toggle.SetFocus());
        await terminal.InjectAsync(Key.Enter);
        await terminal.WaitForScreenAsync("日文");
        bootstrap.Log("test", "settings-stay-open");
        await host.FlushAsync();
        Assert.Same(toggle, await terminal.InvokeAsync(() => terminal.Application.TopRunnableView!.MostFocused));
        Assert.Contains("日文", await terminal.CaptureScreenAsync());

        await terminal.InvokeAsync(() => more.SetFocus());
        await terminal.InjectAsync(Key.Enter);
        await terminal.InvokeAsync(() => Descendants((View)terminal.Application.Popovers!.GetActivePopover()!)
            .OfType<MenuItem>().Single(item => item.Title == Localization.LaunchMenu.I18N_Diagnostics).SetFocus());
        await terminal.InjectAsync(Key.Enter);
        var diagnostics = await terminal.InvokeAsync(() => (ListView)Descendants(view).Single(v => v.Id == "bootstrap-diagnostics"));
        await terminal.InvokeAsync(() =>
        {
            diagnostics.SetFocus();
            diagnostics.SelectedItem = diagnostics.Source!.Count - 1;
            diagnostics.ScrollHorizontal(diagnostics.Source.MaxItemLength);
        });
        await terminal.RedrawAsync();
        await terminal.WaitForScreenAsync("diagnostic-end");
        var diagnosticPosition = await terminal.InvokeAsync(() => diagnostics.Viewport.Location);
        Assert.True(diagnosticPosition.X > 0);
        bootstrap.Log("test", "diagnostics-stay-scrolled");
        await host.FlushAsync();
        Assert.Equal(diagnosticPosition, await terminal.InvokeAsync(() => diagnostics.Viewport.Location));
        Assert.Same(diagnostics, await terminal.InvokeAsync(() => terminal.Application.TopRunnableView!.MostFocused));
        Assert.Contains("diagnostic-end", await terminal.CaptureScreenAsync());
        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => choice);
        Assert.Same(view, await terminal.InvokeAsync(() => Descendants(terminal.Application.TopRunnableView!).OfType<BootstrapView>().Single()));
    }

    [Fact]
    public async Task LogRefreshKeepsSelectionScrollAndFocusUntilUserReturnsToTail()
    {
        await terminal.ResizeAsync(38, 16);
        var bootstrap = host.Bootstrap;
        bootstrap.ShowInformation();
        bootstrap.SetListening("127.0.0.1:4693");
        for (var index = 0; index < 60; index++)
            bootstrap.Log("test", $"history-{index:00}-abcdefghijklmnopqrstuvwxyz");
        await host.FlushAsync();
        var view = await terminal.InvokeAsync(() => Descendants(terminal.Application.TopRunnableView!).OfType<BootstrapView>().Single());
        var toggle = await terminal.InvokeAsync(() => Descendants(view).Single(v => v.Id == "bootstrap-details-toggle"));
        await terminal.InvokeAsync(() => toggle.SetFocus());
        await terminal.InjectAsync(Key.Enter);
        var logs = await terminal.InvokeAsync(() => (ListView)Descendants(view).Single(v => v.Id == "bootstrap-logs"));
        var body = await terminal.InvokeAsync(() => Descendants(view).Single(v => v.Id == "bootstrap-body"));
        await terminal.InvokeAsync(() => logs.SetFocus());
        foreach (var (key, expected) in new[]
                 {
                     (Key.Home, 0), (Key.End, 59), (Key.Home, 0),
                     (Key.CursorDown, 1), (Key.CursorDown, 2), (Key.CursorUp, 1)
                 })
        {
            await terminal.InjectAsync(key);
            await terminal.WaitForScreenAsync($"history-{expected:00}-");
            await terminal.InvokeAsync(() =>
            {
                Assert.Equal(38, view.Viewport.Width);
                Assert.Equal(expected, logs.SelectedItem);
                var selectedY = logs.ViewportToScreen(new Point(0, expected - logs.Viewport.Y)).Y;
                var bodyY = body.ViewportToScreen(Point.Empty).Y;
                Assert.InRange(selectedY, bodyY, bodyY + body.Viewport.Height - 1);
            });
        }
        var selection = await terminal.InvokeAsync(() => logs.SelectedItem);
        var position = await terminal.InvokeAsync(() => logs.Viewport.Location);
        var bodyPosition = await terminal.InvokeAsync(() => body.Viewport.Location);
        bootstrap.Log("test", "new-tail");
        bootstrap.SetPhase(BootstrapPhaseId.Host, BootstrapPhaseState.Succeeded, "host-ready");
        await host.FlushAsync();
        Assert.Same(logs, await terminal.InvokeAsync(() => terminal.Application.TopRunnableView!.MostFocused));
        Assert.Equal(selection, await terminal.InvokeAsync(() => logs.SelectedItem));
        Assert.Equal(position, await terminal.InvokeAsync(() => logs.Viewport.Location));
        Assert.Equal(bodyPosition, await terminal.InvokeAsync(() => body.Viewport.Location));
        await terminal.InjectAsync(Key.End);
        await terminal.WaitForScreenAsync("new-tail");
        await terminal.InjectAsync(Key.CursorUp);
        await terminal.WaitForScreenAsync("history-59-");
        var pausedPosition = await terminal.InvokeAsync(() =>
        {
            Assert.Equal(59, logs.SelectedItem);
            Assert.True(logs.Viewport.Y + logs.Viewport.Height >= logs.Source!.Count);
            return (logs.SelectedItem, logs.Viewport.Location, body.Viewport.Location);
        });
        bootstrap.Log("test", "paused-tail");
        await host.FlushAsync();
        Assert.Same(logs, await terminal.InvokeAsync(() => terminal.Application.TopRunnableView!.MostFocused));
        Assert.Equal(pausedPosition, await terminal.InvokeAsync(() =>
            (logs.SelectedItem, logs.Viewport.Location, body.Viewport.Location)));

        await terminal.InjectAsync(Key.End);
        await terminal.WaitForScreenAsync("paused-tail");
        bootstrap.Log("test", "following-tail");
        await host.FlushAsync();
        await terminal.WaitForScreenAsync("following-tail");
        await terminal.InvokeAsync(() => Assert.Equal(logs.Source!.Count - 1, logs.SelectedItem));
    }

    static IEnumerable<View> Descendants(View view)
    {
        yield return view;
        foreach (var child in view.SubViews)
            foreach (var descendant in Descendants(child))
                yield return descendant;
    }
}
