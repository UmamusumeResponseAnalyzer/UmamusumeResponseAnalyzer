using static UmamusumeResponseAnalyzer.Localization.LaunchMenu;
using System.Drawing;
using Terminal.Gui.Input;
using UmamusumeResponseAnalyzer.TerminalGui;
using Xunit;

namespace UmamusumeResponseAnalyzer.Tests;

[Collection("PluginRuntime")]
public sealed class BootstrapMenuTests(PluginRuntimeFixture fixture)
{
    readonly TerminalGuiTestApp terminal = fixture.Terminal;
    readonly UiHost host = fixture.Host;

    [Fact]
    public async Task MenuSurvivesBackgroundUpdatesAndReusesBootstrapForInformation()
    {
        var bootstrap = host.Bootstrap;
        try
        {
            bootstrap.ShowPreparingMenu("启动菜单测试", ["开始测试", "选项测试"]);
            await terminal.WaitForScreenAsync(I18N_PreparingStartup);
            var preparingView = await terminal.InvokeAsync(() => Descendants(terminal.Application.TopRunnableView!)
                .OfType<BootstrapMenuView>().Single());
            Assert.False(await terminal.InvokeAsync(() => Descendants(preparingView).OfType<Terminal.Gui.Views.Menu>().Single().Enabled));
            await terminal.InjectAsync(Key.Enter);
            var preparingPoint = await terminal.InvokeAsync(() => Descendants(preparingView)
                .OfType<Terminal.Gui.Views.MenuItem>().First().ViewportToScreen(new Point(2, 0)));
            await terminal.ClickAsync(preparingPoint);
            bootstrap.SetSettings([("menu-setting", "preparing-value")]);
            bootstrap.SetPhase("preparing", "准备", UiSeverity.Info, "preparing-phase");
            bootstrap.Log("menu-test", "preparing-log");
            await host.FlushAsync();
            Assert.Same(preparingView, await terminal.InvokeAsync(() => Descendants(terminal.Application.TopRunnableView!)
                .OfType<BootstrapMenuView>().Single()));
            Assert.False(await terminal.InvokeAsync(() => Descendants(terminal.Application.TopRunnableView!)
                .OfType<CommandModeView>().Single().IsOpen));
            Assert.Contains(I18N_PreparingStartup, await terminal.CaptureScreenAsync());

            var menu = bootstrap.ShowMenuAsync("启动菜单测试", ["开始测试", "选项测试"], TestContext.Current.CancellationToken);
            await terminal.WaitForScreenAsync("启动菜单测试");
            await terminal.WaitForScreenAsync("选项测试");
            var menuView = await terminal.InvokeAsync(() => Descendants(terminal.Application.TopRunnableView!)
                .OfType<BootstrapMenuView>().Single());
            Assert.Equal("开始测试", await terminal.InvokeAsync(() => terminal.Application.TopRunnableView!.MostFocused?.Title));
            await terminal.InjectAsync(Key.CursorDown);
            Assert.Equal("选项测试", await terminal.InvokeAsync(() => terminal.Application.TopRunnableView!.MostFocused?.Title));
            bootstrap.Log("menu-test", "menu-background-log");
            bootstrap.SetPhase("menu-test", "menu-phase", UiSeverity.Success, "menu-background-phase");
            await host.FlushAsync();
            Assert.Same(menuView, await terminal.InvokeAsync(() => Descendants(terminal.Application.TopRunnableView!)
                .OfType<BootstrapMenuView>().Single()));
            await terminal.InjectAsync(Key.Enter);
            Assert.Equal("选项测试", await menu.WaitAsync(TimeSpan.FromSeconds(5)));
            await terminal.WaitForScreenAsync(string.Format(I18N_Running, "选项测试"));
            await terminal.InjectAsync(Key.Enter);
            await terminal.InjectAsync(Key.Enter);
            Assert.False(await terminal.InvokeAsync(() => Descendants(terminal.Application.TopRunnableView!)
                .OfType<CommandModeView>().Single().IsOpen));

            var next = bootstrap.ShowMenuAsync("继续菜单测试", ["开始测试"], TestContext.Current.CancellationToken);
            await terminal.WaitForScreenAsync("继续菜单测试");
            var point = await terminal.InvokeAsync(() =>
            {
                var item = Descendants(terminal.Application.TopRunnableView!).OfType<Terminal.Gui.Views.MenuItem>().Single();
                return item.ViewportToScreen(new Point(2, 0));
            });
            await terminal.ClickAsync(point);
            Assert.Equal("开始测试", await next.WaitAsync(TimeSpan.FromSeconds(5)));
            bootstrap.ShowInformation();
            await host.FlushAsync();
            Assert.Same(bootstrap.Workspace, Workspace.Current);
            var screen = await terminal.CaptureScreenAsync();
            Assert.Contains("preparing-log", screen);
            Assert.Contains("menu-background-log", screen);
            Assert.DoesNotContain("继续菜单测试", screen);
            Assert.Empty(await terminal.InvokeAsync(() => Descendants(terminal.Application.TopRunnableView!)
                .OfType<BootstrapMenuView>().ToArray()));
        }
        finally
        {
            bootstrap.ShowInformation();
            await host.FlushAsync();
        }
    }

    [Fact]
    public async Task MenuWaitEndsOnEscapeCancellationAndViewDisposal()
    {
        var bootstrap = host.Bootstrap;
        try
        {
            var escape = bootstrap.ShowMenuAsync("退出菜单测试", ["开始测试"], TestContext.Current.CancellationToken);
            await terminal.WaitForScreenAsync("退出菜单测试");
            await terminal.InjectAsync(Key.Esc);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => escape.WaitAsync(TimeSpan.FromSeconds(5)));

            using var cancellation = new CancellationTokenSource();
            var canceled = bootstrap.ShowMenuAsync("取消菜单测试", ["开始测试"], cancellation.Token);
            await terminal.WaitForScreenAsync("取消菜单测试");
            await cancellation.CancelAsync();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => canceled);

            var disposed = bootstrap.ShowMenuAsync("释放菜单测试", ["开始测试"], TestContext.Current.CancellationToken);
            await terminal.WaitForScreenAsync("释放菜单测试");
            host.RemovePanel(bootstrap.Workspace, "menu");
            await host.FlushAsync();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => disposed.WaitAsync(TimeSpan.FromSeconds(5)));
        }
        finally
        {
            bootstrap.ShowInformation();
            await host.FlushAsync();
        }
    }

    static IEnumerable<Terminal.Gui.ViewBase.View> Descendants(Terminal.Gui.ViewBase.View view)
    {
        yield return view;
        foreach (var child in view.SubViews)
            foreach (var descendant in Descendants(child))
                yield return descendant;
    }
}
