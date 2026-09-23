using i18n = UmamusumeResponseAnalyzer.Localization.TerminalGui;
using System.Collections.Concurrent;
using Terminal.Gui.Drivers;
using Terminal.Gui.Input;
using Terminal.Gui.Views;
using UmamusumeResponseAnalyzer.TerminalGui;
using Xunit;

namespace UmamusumeResponseAnalyzer.Tests;

[Collection("PluginRuntime")]
public sealed class WorkspaceLifecycleTests(PluginRuntimeFixture fixture) : IDisposable
{
    readonly TerminalGuiTestApp terminal = fixture.Terminal;
    readonly UiHost host = fixture.Host;
    readonly List<Workspace> ownedWorkspaces = [];

    public void Dispose()
    {
        foreach (var workspace in ownedWorkspaces.AsEnumerable().Reverse())
            workspace.Remove();
        host.FlushAsync().WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
    }

    [Fact]
    public async Task MixedCaseConcurrentCreateReturnsOneCanonicalReference()
    {
        var title = $"Concurrent-{Guid.NewGuid():N}";
        var spellings = new[]
        {
            title,
            title.ToUpperInvariant(),
            title.ToLowerInvariant()
        };
        var workspaces = await Task.WhenAll(Enumerable.Range(0, 32)
            .Select(index => Task.Run(() => Workspace.Create(spellings[index % spellings.Length]))));
        var canonical = Own(workspaces[0]);

        Assert.All(workspaces, workspace => Assert.Same(canonical, workspace));
        Assert.Contains(canonical.Title, spellings, StringComparer.Ordinal);
        await host.FlushAsync();
    }

    [Fact]
    public async Task ConcurrentPanelWritersYieldToFinalAdmissionSentinel()
    {
        await terminal.ResizeAsync(120, 30);
        var workspace = Own(Workspace.Create($"Panels-{Guid.NewGuid():N}"));
        const string prefix = "concurrent-panel-";

        await Task.WhenAll(Enumerable.Range(0, 32).Select(index => Task.Run(() =>
            workspace.SetPanel(
                "main",
                "main",
                WorkspaceContent.Text($"{prefix}{index}"),
                fullBleed: true,
                switchToWorkspace: false))));

        var sentinel = $"final-panel-{Guid.NewGuid():N}";
        workspace.SetPanel(
            "main",
            "main",
            WorkspaceContent.Text(sentinel),
            fullBleed: true,
            switchToWorkspace: true);
        await host.FlushAsync();

        await terminal.WaitForScreenAsync(sentinel);
        var screen = await terminal.CaptureScreenAsync();
        Assert.Contains(sentinel, screen, StringComparison.Ordinal);
        Assert.DoesNotContain(prefix, screen, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GlobalLogsRenderInAdmissionOrderWithSource()
    {
        await terminal.ResizeAsync(120, 30);
        var bootstrap = host.Bootstrap;
        var run = Guid.NewGuid().ToString("N")[..8];
        var expected = Enumerable.Range(0, 4)
            .Select(index => $"L{run}-{index}")
            .ToArray();

        bootstrap.Workspace.SwitchTo();
        for (var index = 0; index < expected.Length; index++)
            TerminalUi.Log("Admission", expected[index]);
        await host.FlushAsync();

        await terminal.WaitForScreenAsync(expected[^1]);
        var screen = await terminal.CaptureScreenAsync();
        var positions = expected
            .Select(text => screen.IndexOf(text, StringComparison.Ordinal))
            .ToArray();
        Assert.All(positions, position => Assert.True(position >= 0));
        Assert.True(positions.SequenceEqual(positions.Order()));
        Assert.Contains("[Admission]", screen, StringComparison.Ordinal);
    }

    Workspace Own(Workspace workspace)
    {
        ownedWorkspaces.Add(workspace);
        return workspace;
    }
}

public sealed class WorkspaceLifecycleProcessTests
{
    [Fact]
    public async Task FirstVisibleFrameContainsBootstrapDashboardBeforeOrdinaryEmptyState()
    {
        const string scenario = "workspace-bootstrap-first-frame";
        const string result = "workspace-bootstrap-first-frame-ok";
        if (TerminalUiLifecycleChildProcess.IsChild(scenario))
        {
            using var terminal = new TerminalGuiTestApp(width: 120, height: 36);
            var host = TerminalUiLifecycleChildProcess.InitializeHost(
                terminal,
                CancellationToken.None);
            var frames = new ConcurrentQueue<string>();

            void CaptureFrame(object? sender, EventArgs e)
            {
                if (terminal.Application.TopRunnableView is null)
                    return;
                frames.Enqueue((terminal.Application.Driver
                    ?? throw new InvalidOperationException(
                        "Terminal.Gui driver was not initialized."))
                    .ToString());
            }

            terminal.RunOnOwnerThread(() =>
                terminal.Application.LayoutAndDrawComplete += CaptureFrame);
            var run = await terminal.StartAsync(host);
            try
            {
                await terminal.InvokeAsync(() =>
                    terminal.Application.LayoutAndDrawComplete -= CaptureFrame);
                var visibleFrames = frames.ToArray();
                Assert.NotEmpty(visibleFrames);
                var firstFrame = visibleFrames[0];
                Assert.Contains(Localization.LaunchMenu.I18N_Environment, firstFrame, StringComparison.Ordinal);
                Assert.Contains(Localization.LaunchMenu.I18N_InitializationResults, firstFrame, StringComparison.Ordinal);
                Assert.Contains(Localization.LaunchMenu.I18N_PluginSummary, firstFrame, StringComparison.Ordinal);
                Assert.Contains(Localization.LaunchMenu.I18N_RecentLogs, firstFrame, StringComparison.Ordinal);
                Assert.All(visibleFrames, frame => Assert.DoesNotContain(
                    string.Format(i18n.Workspace_NoOutput, i18n.Workspace_BootstrapTitle),
                    frame,
                    StringComparison.Ordinal));

                var emptyWorkspace = Workspace.Create("Ordinary empty workspace");
                emptyWorkspace.SwitchTo();
                await host.FlushAsync();
                await terminal.WaitForScreenAsync(
                    string.Format(i18n.Workspace_NoOutput, "Ordinary empty workspace"));

                TerminalUiLifecycleChildProcess.WriteResult(result);
            }
            finally
            {
                await terminal.InvokeAsync(() =>
                    terminal.Application.LayoutAndDrawComplete -= CaptureFrame);
                await terminal.StopAsync(host, run);
            }
            return;
        }

        Assert.Equal(
            result,
            await TerminalUiLifecycleProcessTests.RunChildAsync(
                scenario,
                typeof(WorkspaceLifecycleProcessTests),
                nameof(FirstVisibleFrameContainsBootstrapDashboardBeforeOrdinaryEmptyState)));
    }

    [Fact]
    public async Task RemoveClearsOwnedOutputAndHotkeyWithoutTouchingSurvivor()
    {
        const string scenario = "workspace-owned-output-removal";
        const string result = "workspace-owned-output-removal-ok";
        if (TerminalUiLifecycleChildProcess.IsChild(scenario))
        {
            using var terminal = new TerminalGuiTestApp();
            var host = TerminalUiLifecycleChildProcess.InitializeHost(
                terminal,
                CancellationToken.None);
            var run = await terminal.StartAsync(host);
            try
            {
                await terminal.ResizeAsync(120, 30);
                var doomed = Workspace.Create($"Doomed-{Guid.NewGuid():N}");
                var survivor = Workspace.Create($"Survivor-{Guid.NewGuid():N}");
                var doomedAlias = Workspace.Create(doomed.Title.ToUpperInvariant());
                var survivorAlias = Workspace.Create(survivor.Title.ToUpperInvariant());
                var doomedPanel = $"doomed-panel-{Guid.NewGuid():N}";
                var panelDisposed = new DisposeSignal();
                var survivorPanel = $"survivor-panel-{Guid.NewGuid():N}";
                var notificationToken = Guid.NewGuid().ToString("N")[..8];
                var doomedNotification1 = $"D{notificationToken}-1";
                var doomedNotification2 = $"D{notificationToken}-2";
                var survivorNotification = $"S{notificationToken}";
                var modifiers = ConsoleModifiers.Control | ConsoleModifiers.Alt | ConsoleModifiers.Shift;
                var workspaceKey = new Key(
                    KeyCode.F12 | KeyCode.CtrlMask | KeyCode.AltMask | KeyCode.ShiftMask);
                var notificationKey = new Key(
                    KeyCode.F11 | KeyCode.CtrlMask | KeyCode.AltMask | KeyCode.ShiftMask);
                var notificationHits = 0;

                Assert.Same(doomed, doomedAlias);
                Assert.Same(survivor, survivorAlias);
                doomed.SetPanel(
                    "main",
                    "main",
                    new WorkspaceContent(() => new TrackingView(panelDisposed) { Text = doomedPanel }),
                    fullBleed: true);
                survivorAlias.SetPanel(
                    "main",
                    "main",
                    WorkspaceContent.Text(survivorPanel),
                    fullBleed: true,
                    switchToWorkspace: false);
                doomedAlias.BindHotkey(ConsoleKey.F12, modifiers);
                doomed.Notify(
                    doomedNotification1,
                    UiSeverity.Warning,
                    TimeSpan.FromMinutes(1),
                    new UiShortcut(ConsoleKey.F11, () =>
                    {
                        notificationHits++;
                        return Task.CompletedTask;
                    }, modifiers));
                doomedAlias.Notify(doomedNotification2, UiSeverity.Info, TimeSpan.FromMinutes(1));
                survivorAlias.Notify(survivorNotification, UiSeverity.Success, TimeSpan.FromMinutes(1));
                await host.FlushAsync();
                await terminal.RedrawAsync();

                await terminal.WaitForScreenAsync(doomedPanel);
                await terminal.WaitForScreenAsync(doomedNotification1);
                await terminal.WaitForScreenAsync(doomedNotification2);
                var doomedScreen = await terminal.CaptureScreenAsync();
                Assert.Contains(doomedNotification1, doomedScreen, StringComparison.Ordinal);
                Assert.Contains(doomedNotification2, doomedScreen, StringComparison.Ordinal);
                Assert.DoesNotContain(survivorNotification, doomedScreen, StringComparison.Ordinal);
                Assert.True(await HotkeyManager.HandleKeyAsync(notificationKey));
                Assert.Equal(1, notificationHits);

                survivor.SwitchTo();
                await host.FlushAsync();
                await terminal.InjectAsync(workspaceKey);
                await terminal.WaitForAsync(() => ReferenceEquals(Workspace.Current, doomed));
                await host.FlushAsync();
                Assert.Same(doomed, Workspace.Current);

                survivor.SwitchTo();
                doomed.Remove();
                await host.FlushAsync();
                await terminal.RedrawAsync();
                await terminal.WaitForAsync(() => panelDisposed.Disposed);
                Assert.False(await HotkeyManager.HandleKeyAsync(workspaceKey));
                Assert.False(await HotkeyManager.HandleKeyAsync(notificationKey));
                Assert.Equal(1, notificationHits);
                Assert.Same(survivor, Workspace.Current);
                await terminal.WaitForScreenAsync(survivorPanel);
                await terminal.WaitForScreenAsync(survivorNotification);

                var survivorScreen = await terminal.CaptureScreenAsync();
                Assert.DoesNotContain(doomedNotification1, survivorScreen, StringComparison.Ordinal);
                Assert.DoesNotContain(doomedNotification2, survivorScreen, StringComparison.Ordinal);
                Assert.Contains(survivorNotification, survivorScreen, StringComparison.Ordinal);
                Assert.Contains(survivorPanel, survivorScreen, StringComparison.Ordinal);

                TerminalUiLifecycleChildProcess.WriteResult(result);
            }
            finally
            {
                await terminal.StopAsync(host, run);
            }
            return;
        }

        Assert.Equal(
            result,
            await TerminalUiLifecycleProcessTests.RunChildAsync(
                scenario,
                typeof(WorkspaceLifecycleProcessTests),
                nameof(RemoveClearsOwnedOutputAndHotkeyWithoutTouchingSurvivor)));
    }

    [Fact]
    public async Task ProtectedBootstrapIsCanonicalAndHostRemainsUsableAfterRejectedRemoval()
    {
        const string scenario = "workspace-protected-bootstrap";
        const string result = "workspace-protected-bootstrap-ok";
        if (TerminalUiLifecycleChildProcess.IsChild(scenario))
        {
            using var terminal = new TerminalGuiTestApp();
            var host = TerminalUiLifecycleChildProcess.InitializeHost(
                terminal,
                CancellationToken.None);
            var run = await terminal.StartAsync(host);
            try
            {
                var bootstrap = host.Bootstrap.Workspace;
                Assert.Equal(Workspace.BootstrapTitle, Workspace.Current.Title);
                Assert.Same(bootstrap, Workspace.Current);
                Assert.Same(bootstrap, Workspace.Create("启动"));
                var error = Assert.Throws<InvalidOperationException>(bootstrap.Remove);
                Assert.Equal(string.Format(i18n.Workspace_BootstrapCannotRemove, Workspace.BootstrapTitle), error.Message);
                Assert.Same(bootstrap, Workspace.Current);
                Assert.False(bootstrap.IsRemoved);

                var workspace = Workspace.Create("Navigable Workspace");
                workspace.SetPanel(
                    "main",
                    "main",
                    WorkspaceContent.Text(string.Join(
                        Environment.NewLine,
                        Enumerable.Range(1, 40).Select(index => $"line-{index:00}"))),
                    fullBleed: true);
                await host.FlushAsync();
                await terminal.WaitForScreenAsync("line-40");

                Assert.True(await ((IUiInputSink)host)
                    .TryHandleWorkspaceCommandAsync(Command.Start));
                await terminal.RedrawAsync();
                await terminal.WaitForScreenAsync("line-01");

                workspace.Remove();
                Assert.Same(bootstrap, Workspace.Current);
                await host.FlushAsync();
                await terminal.WaitForScreenAsync(Localization.LaunchMenu.I18N_Environment);
                var screen = await terminal.CaptureScreenAsync();
                Assert.Contains(Localization.LaunchMenu.I18N_InitializationResults, screen, StringComparison.Ordinal);
                Assert.Contains(Localization.LaunchMenu.I18N_PluginSummary, screen, StringComparison.Ordinal);
                Assert.Contains(Localization.LaunchMenu.I18N_RecentLogs, screen, StringComparison.Ordinal);
                Assert.DoesNotContain("当前没有 workspace。", screen, StringComparison.Ordinal);

                TerminalUiLifecycleChildProcess.WriteResult(result);
            }
            finally
            {
                await terminal.StopAsync(host, run);
            }
            return;
        }

        Assert.Equal(
            result,
            await TerminalUiLifecycleProcessTests.RunChildAsync(
                scenario,
                typeof(WorkspaceLifecycleProcessTests),
                nameof(ProtectedBootstrapIsCanonicalAndHostRemainsUsableAfterRejectedRemoval)));
    }

    sealed class DisposeSignal
    {
        public volatile bool Disposed;
    }

    sealed class TrackingView(DisposeSignal signal) : Label
    {
        protected override void Dispose(bool disposing)
        {
            if (disposing)
                signal.Disposed = true;
            base.Dispose(disposing);
        }
    }
}
