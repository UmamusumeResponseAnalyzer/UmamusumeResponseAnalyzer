using System.Drawing;
using System.Reflection;
using Terminal.Gui.Input;
using Terminal.Gui.Views;
using UmamusumeResponseAnalyzer.Commands;
using UmamusumeResponseAnalyzer.Plugin;
using UmamusumeResponseAnalyzer.TerminalGui;
using Xunit;

namespace UmamusumeResponseAnalyzer.Tests;

[Collection("PluginReload")]
public sealed class UiHostRenderTests : IDisposable
{
    readonly TerminalGuiTestApp terminal;
    readonly UiHost host;
    readonly List<Workspace> ownedWorkspaces = [];

    public UiHostRenderTests(PluginRuntimeFixture fixture)
    {
        terminal = fixture.Terminal;
        host = fixture.Host;
        HotkeyManager.OverlaySink = host;
    }

    public void Dispose()
    {
        foreach (var workspace in ownedWorkspaces.AsEnumerable().Reverse())
        {
            host.RemoveWorkspace(workspace);
        }
        HotkeyManager.UnregisterAll();
        host.FlushAsync().WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
    }

    [Fact]
    public void WorkspaceContent_CreatesFreshViewsAndRejectsNull()
    {
        var created = 0;
        var content = new WorkspaceContent(() =>
        {
            created++;
            return new Label { Text = $"view-{created}" };
        });

        using var first = content.CreateView();
        using var second = content.CreateView();

        Assert.IsType<Label>(first);
        Assert.IsType<Label>(second);
        Assert.NotSame(first, second);
        Assert.Equal(2, created);
        Assert.Throws<InvalidOperationException>(
            new WorkspaceContent(() => null!).CreateView);
    }

    [Fact]
    public async Task WorkspaceIdentity_IsCanonicalCaseInsensitiveAndTombstonedByReference()
    {
        var title = $"Canonical-{Guid.NewGuid():N}";
        var first = Own(host.CreateWorkspace(title));
        var same = host.CreateWorkspace(title.ToUpperInvariant());

        Assert.Same(first, same);
        Assert.Equal(title, first.Title);

        host.RemoveWorkspace(first);
        Assert.Throws<InvalidOperationException>(() => host.SetPanel(
            first,
            "late",
            "late",
            WorkspaceContent.Text("late"),
            fullBleed: true,
            switchToWorkspace: true));

        var replacement = Own(host.CreateWorkspace(title.ToLowerInvariant()));
        Assert.NotSame(first, replacement);
        Assert.Same(host.Bootstrap.Workspace, host.GetCurrentWorkspace());
        replacement.SwitchTo();
        Assert.Same(replacement, host.GetCurrentWorkspace());
        await host.FlushAsync();
    }

    [Fact]
    public async Task Flush_CompletesAfterTheAcceptedPanelIsReconciled()
    {
        var workspace = CreateWorkspace("Flush workspace");
        host.SetPanel(
            workspace,
            "main",
            "main",
            WorkspaceContent.Text("FlushVisible"),
            fullBleed: true,
            switchToWorkspace: true);

        await host.FlushAsync();

        await terminal.RedrawAsync();
        var screen = await terminal.CaptureScreenAsync();
        Assert.Contains("FlushVisible", screen, StringComparison.Ordinal);
        Assert.StartsWith("FlushVisible", screen, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PanelReplacementAndRemoval_PreservePublicReturnsAndFinalFramebuffer()
    {
        var workspace = CreateWorkspace("Panel workspace");
        host.SetPanel(
            workspace,
            "main",
            "main",
            WorkspaceContent.Text("FirstView"),
            fullBleed: true,
            switchToWorkspace: true);
        await host.FlushAsync();
        await terminal.WaitForScreenAsync("FirstView");

        host.SetPanel(
            workspace,
            "main",
            "main",
            WorkspaceContent.Text("SecondView"),
            fullBleed: true,
            switchToWorkspace: true);
        await host.FlushAsync();

        await terminal.WaitForScreenAsync("SecondView");
        Assert.DoesNotContain(
            "FirstView",
            await terminal.CaptureScreenAsync(),
            StringComparison.Ordinal);
        Assert.True(host.RemovePanel(workspace, "main"));
        Assert.False(host.RemovePanel(workspace, "main"));
        await host.FlushAsync();
        await terminal.RedrawAsync();
        Assert.DoesNotContain(
            "SecondView",
            await terminal.CaptureScreenAsync(),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Taskbar_HoverClickDragAndResizePreserveCanonicalWorkspace()
    {
        const string scenario = "taskbar-hover-click-drag-resize";
        if (!TerminalUiLifecycleChildProcess.IsChild(scenario))
        {
            Assert.Equal(
                "ok",
                await TerminalUiLifecycleProcessTests.RunChildAsync(
                    scenario,
                    typeof(UiHostRenderTests),
                    nameof(Taskbar_HoverClickDragAndResizePreserveCanonicalWorkspace)));
            return;
        }

        await terminal.ResizeAsync(80, 18);
        var first = CreateWorkspace("Taskbar First");
        var second = CreateWorkspace("Taskbar Second");
        host.SetPanel(
            first,
            "main",
            "main",
            WorkspaceContent.Text("FirstWorkspaceBody"),
            fullBleed: true,
            switchToWorkspace: true);
        host.SetPanel(
            second,
            "main",
            "main",
            WorkspaceContent.Text("SecondWorkspaceBody"),
            fullBleed: true,
            switchToWorkspace: false);
        await host.FlushAsync();

        await terminal.MoveMouseAsync(new Point(0, 17));
        var secondPoint = await WaitForTaskbarItemAsync(second.Title, first.Title);
        await terminal.ClickAsync(secondPoint);

        await terminal.WaitForScreenAsync("SecondWorkspaceBody");
        Assert.Same(second, host.GetCurrentWorkspace());

        await terminal.MoveMouseAsync(Point.Empty);
        await terminal.MoveMouseAsync(new Point(0, 17));
        var firstPoint = await WaitForTaskbarItemAsync(first.Title, second.Title);
        secondPoint = await WaitForTaskbarItemAsync(second.Title, first.Title);
        var dragTarget = firstPoint with
        {
            X = Math.Max(0, firstPoint.X - first.Title.Length / 2)
        };
        await terminal.InjectAsync(new Mouse
        {
            ScreenPosition = secondPoint,
            Flags = MouseFlags.LeftButtonPressed,
            Timestamp = terminal.Time.Now
        });
        await terminal.InjectAsync(new Mouse
        {
            ScreenPosition = dragTarget,
            Flags = MouseFlags.LeftButtonPressed | MouseFlags.PositionReport,
            Timestamp = terminal.Time.Now
        });
        await terminal.InjectAsync(new Mouse
        {
            ScreenPosition = dragTarget,
            Flags = MouseFlags.LeftButtonReleased,
            Timestamp = terminal.Time.Now
        });
        await terminal.WaitForAsync(async () =>
        {
            var line = (await terminal.CaptureScreenAsync())
                .ReplaceLineEndings("\n")
                .Split('\n')
                .FirstOrDefault(candidate =>
                    candidate.Contains(first.Title, StringComparison.Ordinal) &&
                    candidate.Contains(second.Title, StringComparison.Ordinal));
            return line is not null &&
                   line.IndexOf(second.Title, StringComparison.Ordinal) <
                   line.IndexOf(first.Title, StringComparison.Ordinal);
        });

        await terminal.ResizeAsync(100, 24);
        await terminal.RedrawAsync();
        await terminal.MoveMouseAsync(new Point(0, 23));
        await WaitForTaskbarItemAsync(second.Title, first.Title);
        Assert.Contains(
            "SecondWorkspaceBody",
            await terminal.CaptureScreenAsync(),
            StringComparison.Ordinal);
        TerminalUiLifecycleChildProcess.WriteResult("ok");
    }

    [Fact]
    public async Task Viewport_NativeHomeAndEndPreserveBottomAnchoring()
    {
        var workspace = CreateWorkspace("Scroll workspace");
        host.SetPanel(
            workspace,
            "main",
            "main",
            WorkspaceContent.Text(string.Join(
                Environment.NewLine,
                Enumerable.Range(1, 40).Select(value => $"line-{value:00}"))),
            fullBleed: true,
            switchToWorkspace: true);
        await host.FlushAsync();
        await terminal.RedrawAsync();

        Assert.Contains(
            "line-40",
            await terminal.CaptureScreenAsync(),
            StringComparison.Ordinal);

        await terminal.InjectAsync(Key.Home);
        await terminal.WaitForScreenAsync("line-01");
        await terminal.InjectAsync(Key.End);
        await terminal.WaitForScreenAsync("line-40");
    }

    [Fact]
    public async Task Notifications_FilterByWorkspaceAndKeepShortcutLifetime()
    {
        const string scenario = "workspace-notification-scope";
        if (!TerminalUiLifecycleChildProcess.IsChild(scenario))
        {
            Assert.Equal(
                "ok",
                await TerminalUiLifecycleProcessTests.RunChildAsync(
                    scenario,
                    typeof(UiHostRenderTests),
                    nameof(Notifications_FilterByWorkspaceAndKeepShortcutLifetime)));
            return;
        }

        var first = CreateWorkspace("Notification First");
        var second = CreateWorkspace("Notification Second");
        host.SetPanel(
            first,
            "main",
            "main",
            WorkspaceContent.Text("NotificationFirstBody"),
            fullBleed: true,
            switchToWorkspace: true);
        host.SetPanel(
            second,
            "main",
            "main",
            WorkspaceContent.Text("NotificationSecondBody"),
            fullBleed: true,
            switchToWorkspace: false);
        var shortcutHits = 0;
        host.Notify(
            second,
            "SecondWorkspaceNotification",
            UiSeverity.Warning,
            TimeSpan.FromMinutes(1),
            []);
        host.Notify(
            first,
            "FirstWorkspaceNotification",
            UiSeverity.Info,
            TimeSpan.FromMinutes(1),
            [new UiShortcut(ConsoleKey.F8, () =>
            {
                Interlocked.Increment(ref shortcutHits);
                return Task.CompletedTask;
            })]);
        await host.FlushAsync();

        await terminal.WaitForScreenAsync("FirstWorkspaceNotification");
        Assert.DoesNotContain(
            "SecondWorkspaceNotification",
            await terminal.CaptureScreenAsync(),
            StringComparison.Ordinal);
        await terminal.InjectAsync(Key.F8);
        await terminal.WaitForAsync(() => Volatile.Read(ref shortcutHits) == 1);

        host.SwitchWorkspace(second);
        await host.FlushAsync();
        await terminal.WaitForScreenAsync("SecondWorkspaceNotification");
        Assert.DoesNotContain(
            "FirstWorkspaceNotification",
            await terminal.CaptureScreenAsync(),
            StringComparison.Ordinal);

        TerminalUiLifecycleChildProcess.WriteResult("ok");
    }

    [Fact]
    public async Task Notification_TtlRemovesFramebufferAndShortcut()
    {
        const string scenario = "workspace-notification-ttl";
        if (!TerminalUiLifecycleChildProcess.IsChild(scenario))
        {
            Assert.Equal(
                "ok",
                await TerminalUiLifecycleProcessTests.RunChildAsync(
                    scenario,
                    typeof(UiHostRenderTests),
                    nameof(Notification_TtlRemovesFramebufferAndShortcut)));
            return;
        }

        var workspace = CreateWorkspace("TTL workspace");
        host.SetPanel(
            workspace,
            "main",
            "main",
            WorkspaceContent.Text("TtlBody"),
            fullBleed: true,
            switchToWorkspace: true);
        var hits = 0;
        host.Notify(
            workspace,
            "ExpiringNotification",
            UiSeverity.Info,
            TimeSpan.FromSeconds(2),
            [new UiShortcut(ConsoleKey.F8, () =>
            {
                Interlocked.Increment(ref hits);
                return Task.CompletedTask;
            })]);
        await host.FlushAsync();
        await terminal.WaitForScreenAsync("ExpiringNotification");

        await terminal.InjectAsync(Key.F8);
        await terminal.WaitForAsync(() => Volatile.Read(ref hits) == 1);
        await Task.Delay(TimeSpan.FromMilliseconds(2100), TestContext.Current.CancellationToken);
        terminal.Time.Advance(TimeSpan.FromSeconds(1));
        await terminal.WaitForAsync(async () =>
            !(await terminal.CaptureScreenAsync()).Contains(
                "ExpiringNotification",
                StringComparison.Ordinal));
        await terminal.InjectAsync(Key.F8);
        await Task.Delay(20, TestContext.Current.CancellationToken);

        Assert.Equal(1, Volatile.Read(ref hits));
        Assert.Contains(
            "TtlBody",
            await terminal.CaptureScreenAsync(),
            StringComparison.Ordinal);

        TerminalUiLifecycleChildProcess.WriteResult("ok");
    }

    [Fact]
    public async Task WorkspaceHotkeysCaptureAdmissionOwnerAndCleanupExactMetadata()
    {
        var removed = CreateWorkspace("Owner hotkey removed");
        var kept = CreateWorkspace("Owner hotkey kept");
        var removedOwner = new object();
        var keptOwner = new object();
        using (HotkeyManager.RegisterScope(removedOwner))
            removed.BindHotkey(ConsoleKey.F17, description: "removed owner workspace");
        using (HotkeyManager.RegisterScope(keptOwner))
            kept.BindHotkey(ConsoleKey.F18, description: "kept owner workspace");
        await host.FlushAsync();

        Assert.Same(
            removedOwner,
            HotkeyManager.Hotkeys[(ConsoleKey.F17, ConsoleModifiers.None)].Owner);
        Assert.Same(
            keptOwner,
            HotkeyManager.Hotkeys[(ConsoleKey.F18, ConsoleModifiers.None)].Owner);

        Assert.Equal(1, HotkeyManager.UnregisterByOwner(removedOwner));
        Assert.DoesNotContain(
            (ConsoleKey.F17, ConsoleModifiers.None),
            HotkeyManager.Hotkeys.Keys);
        Assert.Same(
            keptOwner,
            HotkeyManager.Hotkeys[(ConsoleKey.F18, ConsoleModifiers.None)].Owner);

        await host.HandleCommandAsync("/workspace");
        await terminal.RedrawAsync();
        var screen = await terminal.CaptureScreenAsync();
        Assert.Contains(removed.Title, screen, StringComparison.Ordinal);
        Assert.DoesNotContain($"{removed.Title} [F17]", screen, StringComparison.Ordinal);
        Assert.Contains($"{kept.Title} [F18]", screen, StringComparison.Ordinal);

        Assert.Equal(1, HotkeyManager.UnregisterByOwner(keptOwner));
    }

    [Fact]
    public async Task SameOwnerPendingCleanupDoesNotRestoreOldHotkeyAndAllowsNewBind()
    {
        var workspace = CreateWorkspace("Pending owner hotkey");
        var owner = new object();
        using (HotkeyManager.RegisterScope(owner))
            workspace.BindHotkey(ConsoleKey.F13, description: "existing owner");
        await host.FlushAsync();

        var canceled = await terminal.InvokeAsync(() =>
        {
            using (HotkeyManager.RegisterScope(owner))
                workspace.BindHotkey(ConsoleKey.F13, description: "pending owner");
            return HotkeyManager.UnregisterByOwner(owner);
        });

        Assert.Equal(2, canceled);
        await host.FlushAsync();
        Assert.DoesNotContain(
            (ConsoleKey.F13, ConsoleModifiers.None),
            HotkeyManager.Hotkeys.Keys);
        await host.HandleCommandAsync("/workspace");
        await terminal.RedrawAsync();
        Assert.DoesNotContain(
            $"{workspace.Title} [F13]",
            await terminal.CaptureScreenAsync(),
            StringComparison.Ordinal);

        await terminal.InvokeAsync(() =>
        {
            using (HotkeyManager.RegisterScope(owner))
                workspace.BindHotkey(ConsoleKey.F13, description: "reused owner");
        });
        await host.FlushAsync();

        Assert.Same(
            owner,
            HotkeyManager.Hotkeys[(ConsoleKey.F13, ConsoleModifiers.None)].Owner);
        await host.HandleCommandAsync("/workspace");
        await terminal.RedrawAsync();
        Assert.Contains(
            $"{workspace.Title} [F13]",
            await terminal.CaptureScreenAsync(),
            StringComparison.Ordinal);
        Assert.Equal(1, HotkeyManager.UnregisterByOwner(owner));
    }

    [Fact]
    public async Task WorkspaceHotkeyReplacementKeepsActualAndMetadataInSync()
    {
        var first = CreateWorkspace("Replacement first");
        var second = CreateWorkspace("Replacement second");
        var firstOwner = new object();
        using (HotkeyManager.RegisterScope(firstOwner))
            first.BindHotkey(ConsoleKey.F14, description: "first owner");
        await host.FlushAsync();
        var firstEntry = HotkeyManager.Hotkeys[
            (ConsoleKey.F14, ConsoleModifiers.None)];

        var rollbackOwner = new object();
        HotkeyManager.HotkeyEntry rollbackEntry;
        using (HotkeyManager.RegisterScope(rollbackOwner))
        {
            rollbackEntry = HotkeyManager.CaptureTracked(
                "rollback owner",
                () => Task.CompletedTask);
        }
        var displaced = HotkeyManager.RegisterTracked(
            ConsoleKey.F14,
            ConsoleModifiers.None,
            rollbackEntry);
        Assert.Equal(1, HotkeyManager.UnregisterByOwner(rollbackOwner));
        HotkeyManager.RestoreTracked(
            ConsoleKey.F14,
            ConsoleModifiers.None,
            rollbackEntry,
            displaced);
        Assert.Same(
            firstEntry,
            HotkeyManager.Hotkeys[(ConsoleKey.F14, ConsoleModifiers.None)]);

        HotkeyManager.HotkeyEntry sameOwnerPublicationEntry;
        using (HotkeyManager.RegisterScope(firstOwner))
        {
            sameOwnerPublicationEntry = HotkeyManager.CaptureTracked(
                "same owner publication rollback",
                () => Task.CompletedTask);
        }
        var sameOwnerPublicationDisplaced = HotkeyManager.RegisterTracked(
            ConsoleKey.F14,
            ConsoleModifiers.None,
            sameOwnerPublicationEntry);
        HotkeyManager.RestoreTracked(
            ConsoleKey.F14,
            ConsoleModifiers.None,
            sameOwnerPublicationEntry,
            sameOwnerPublicationDisplaced);
        Assert.Same(
            firstEntry,
            HotkeyManager.Hotkeys[(ConsoleKey.F14, ConsoleModifiers.None)]);
        await host.HandleCommandAsync("/workspace");
        await terminal.RedrawAsync();
        Assert.Contains(
            $"{first.Title} [F14]",
            await terminal.CaptureScreenAsync(),
            StringComparison.Ordinal);

        HotkeyManager.HotkeyEntry sameOwnerEntry;
        using (HotkeyManager.RegisterScope(firstOwner))
        {
            sameOwnerEntry = HotkeyManager.CaptureTracked(
                "same owner rollback",
                () => Task.CompletedTask);
        }
        var sameOwnerDisplaced = HotkeyManager.RegisterTracked(
            ConsoleKey.F14,
            ConsoleModifiers.None,
            sameOwnerEntry);
        Assert.NotNull(sameOwnerDisplaced);
        Assert.Equal(1, HotkeyManager.UnregisterByOwner(firstOwner));
        HotkeyManager.RestoreTracked(
            ConsoleKey.F14,
            ConsoleModifiers.None,
            sameOwnerEntry,
            sameOwnerDisplaced);
        Assert.DoesNotContain(
            (ConsoleKey.F14, ConsoleModifiers.None),
            HotkeyManager.Hotkeys.Keys);
        await host.HandleCommandAsync("/workspace");
        await terminal.RedrawAsync();
        Assert.DoesNotContain(
            $"{first.Title} [F14]",
            await terminal.CaptureScreenAsync(),
            StringComparison.Ordinal);

        using (HotkeyManager.RegisterScope(firstOwner))
            first.BindHotkey(ConsoleKey.F14, description: "same owner rebound");
        await host.FlushAsync();
        Assert.Same(
            firstOwner,
            HotkeyManager.Hotkeys[(ConsoleKey.F14, ConsoleModifiers.None)].Owner);

        using (HotkeyManager.RegisterScope(new InactiveHotkeyOwner()))
            second.BindHotkey(ConsoleKey.F14, description: "rejected owner");
        await host.FlushAsync();
        Assert.Same(
            firstOwner,
            HotkeyManager.Hotkeys[(ConsoleKey.F14, ConsoleModifiers.None)].Owner);

        var secondOwner = new object();
        using (HotkeyManager.RegisterScope(secondOwner))
            second.BindHotkey(ConsoleKey.F14, description: "second owner");
        await host.FlushAsync();
        Assert.Same(
            secondOwner,
            HotkeyManager.Hotkeys[(ConsoleKey.F14, ConsoleModifiers.None)].Owner);

        await host.HandleCommandAsync("/workspace");
        await terminal.RedrawAsync();
        var replacedScreen = await terminal.CaptureScreenAsync();
        Assert.DoesNotContain($"{first.Title} [F14]", replacedScreen, StringComparison.Ordinal);
        Assert.Contains($"{second.Title} [F14]", replacedScreen, StringComparison.Ordinal);

        Assert.Equal(1, HotkeyManager.UnregisterByOwner(secondOwner));
        await host.HandleCommandAsync("/workspace");
        await terminal.RedrawAsync();
        var cleanedScreen = await terminal.CaptureScreenAsync();
        Assert.DoesNotContain($"{first.Title} [F14]", cleanedScreen, StringComparison.Ordinal);
        Assert.DoesNotContain($"{second.Title} [F14]", cleanedScreen, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WorkspaceCommand_CompletesAndSelectsCanonicalWorkspace()
    {
        var first = CreateWorkspace("Command First");
        var second = CreateWorkspace("Command Second");
        first.BindHotkey(ConsoleKey.D1, ConsoleModifiers.Control);
        host.SetPanel(
            first,
            "main",
            "main",
            WorkspaceContent.Text("CommandFirstBody"),
            fullBleed: true,
            switchToWorkspace: true);
        host.SetPanel(
            second,
            "main",
            "main",
            WorkspaceContent.Text("CommandSecondBody"),
            fullBleed: true,
            switchToWorkspace: false);
        await host.FlushAsync();

        Assert.Contains(
            "/workspace switch \"Command Second\"",
            host.CompleteCommand("/workspace switch Command S"));
        await host.HandleCommandAsync("/workspace");
        await host.FlushAsync();
        await terminal.WaitForScreenAsync("Workspaces");

        await terminal.InjectAsync(Key.CursorDown);
        await terminal.InjectAsync(Key.Enter);
        await host.FlushAsync();
        await terminal.WaitForScreenAsync("CommandSecondBody");
        Assert.Same(second, host.GetCurrentWorkspace());
    }

    [Fact]
    public async Task CommandMode_SubmitAppliesFinalVisibleResult()
    {
        var first = CreateWorkspace("Input First");
        var second = CreateWorkspace("Input Second");
        Button? focusTarget = null;
        host.SetPanel(
            first,
            "main",
            "main",
            new WorkspaceContent(() =>
            {
                focusTarget = new Button { Text = "CommandFocus" };
                return focusTarget;
            }),
            fullBleed: true,
            switchToWorkspace: true);
        host.SetPanel(
            second,
            "main",
            "main",
            WorkspaceContent.Text("CommandInputResult"),
            fullBleed: true,
            switchToWorkspace: false);
        await host.FlushAsync();
        await terminal.WaitForScreenAsync("CommandFocus");
        await terminal.InvokeAsync(() => focusTarget!.SetFocus());

        await terminal.InjectAsync(new Key('/'));
        await terminal.WaitForScreenAsync("Command Mode");
        await InjectTextAsync("workspace switch Input Second");
        await terminal.InjectAsync(Key.Enter);

        await terminal.WaitForScreenAsync("CommandInputResult");
        Assert.Same(second, host.GetCurrentWorkspace());
        Assert.DoesNotContain(
            "Command Mode",
            await terminal.CaptureScreenAsync(),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task CommandResultsAlwaysLogAndOnlyMessageWithoutDisplayNotifies()
    {
        const string scenario = "command-result-notifications";
        if (!TerminalUiLifecycleChildProcess.IsChild(scenario))
        {
            Assert.Equal(
                "ok",
                await TerminalUiLifecycleProcessTests.RunChildAsync(
                    scenario,
                    typeof(UiHostRenderTests),
                    nameof(CommandResultsAlwaysLogAndOnlyMessageWithoutDisplayNotifies)));
            return;
        }

        const string messageOnly = "message-only";
        const string messageWithDisplay = "message-with-display";
        var workspace = Workspace.Create("Command result target");
        workspace.SetPanel(
            "main",
            "main",
            WorkspaceContent.Text("CommandResultTarget"),
            fullBleed: true);
        await host.FlushAsync();
        await terminal.WaitForScreenAsync("CommandResultTarget");
        List<UiLogLine> logs = [];
        void ObserveLog(UiLogLine line) => logs.Add(line);

        host.LogAdded += ObserveLog;
        try
        {
            await terminal.InvokeAsync(() => host.ApplyCommandResult(
                new HostCommands.Result(messageOnly, UiSeverity.Info)));
            await host.FlushAsync();
            await terminal.WaitForScreenAsync($"[Command] {messageOnly}");

            Assert.Contains(
                logs,
                line => line.Text == $"[Command] {messageOnly}"
                    && line.Severity == UiSeverity.Info);

            await terminal.InvokeAsync(() => host.ApplyCommandResult(new HostCommands.Result(
                messageWithDisplay,
                UiSeverity.Success,
                new HostCommands.Display(
                    "Command result display",
                    [new("command-display-item-sentinel")]))));
            await host.FlushAsync();
            await terminal.WaitForScreenAsync("command-display-item-sentinel");

            Assert.Contains(
                logs,
                line => line.Text == $"[Command] {messageWithDisplay}"
                    && line.Severity == UiSeverity.Success);
            Assert.DoesNotContain(
                messageWithDisplay,
                await terminal.CaptureScreenAsync(),
                StringComparison.Ordinal);

            await terminal.InjectAsync(Key.Esc);
            await terminal.WaitForAsync(async () =>
                !(await terminal.CaptureScreenAsync()).Contains(
                    "command-display-item-sentinel",
                    StringComparison.Ordinal));
            await Task.Delay(TimeSpan.FromMilliseconds(5100), TestContext.Current.CancellationToken);
            terminal.Time.Advance(TimeSpan.FromSeconds(1));
            await terminal.WaitForAsync(async () =>
                !(await terminal.CaptureScreenAsync()).Contains(
                    messageOnly,
                    StringComparison.Ordinal));
        }
        finally
        {
            host.LogAdded -= ObserveLog;
        }

        TerminalUiLifecycleChildProcess.WriteResult("ok");
    }

    [Fact]
    public async Task ExceptionLogsKeepContextNestedFailuresAndFullDetails()
    {
        var error = new InvalidOperationException("插件 Demo 初始化失败", new AggregateException(
            new InvalidDataException("配置文件: settings.yaml"),
            new IOException("读取失败, path=data.bin")));
        var lines = new List<UiLogLine>();
        host.LogAdded += lines.Add;
        try
        {
            TerminalUi.LogException("URA", error);
            await host.FlushAsync();
            var line = Assert.Single(lines);
            Assert.Equal(string.Join(Environment.NewLine,
                "[URA] 插件 Demo 初始化失败", "配置文件: settings.yaml", "读取失败, path=data.bin"), line.Text);
            Assert.Equal(error.ToString(), line.ExceptionDetails);
        }
        finally
        {
            host.LogAdded -= lines.Add;
        }
    }

    [Fact]
    public async Task BootstrapGlobalAndFailureLogsRemainHiddenUntilWorkspaceSwitch()
    {
        var bootstrap = host.Bootstrap;
        try
        {
            bootstrap.Workspace.SwitchTo();
            await host.FlushAsync();
            bootstrap.Log("URA", "BootstrapLog", UiSeverity.Info);
            await host.FlushAsync();
            await terminal.WaitForScreenAsync("BootstrapLog");

            var other = CreateWorkspace("Bootstrap Other");
            host.SetPanel(
                other,
                "main",
                "main",
                WorkspaceContent.Text("OtherWorkspaceBody"),
                fullBleed: true,
                switchToWorkspace: true);
            TerminalUi.Log("ABI", "global-log-sentinel", UiSeverity.Warning);
            TerminalUi.LogException(
                "URA",
                new InvalidOperationException("bootstrap-error-sentinel"));
            await host.HandleCommandAsync("/workspace switch \"unterminated");
            await host.FlushAsync();
            await terminal.WaitForScreenAsync("OtherWorkspaceBody");
            var otherScreen = await terminal.CaptureScreenAsync();
            Assert.DoesNotContain(
                "bootstrap-error-sentinel",
                otherScreen,
                StringComparison.Ordinal);
            Assert.DoesNotContain(
                "global-log-sentinel",
                otherScreen,
                StringComparison.Ordinal);

            bootstrap.Workspace.SwitchTo();
            await host.FlushAsync();
            await terminal.WaitForScreenAsync("bootstrap-error-sentinel");
            await terminal.WaitForScreenAsync("global-log-sentinel");
            var bootstrapScreen = await terminal.CaptureScreenAsync();
            Assert.Contains(
                "Quoted workspace title 缺少结束双引号。",
                bootstrapScreen,
                StringComparison.Ordinal);
        }
        finally
        {
            bootstrap.Workspace.SwitchTo();
            await host.FlushAsync();
        }
    }

    [Fact]
    public async Task RejectedBootstrapRemovalKeepsRenderingLaterGlobalDiagnostics()
    {
        var bootstrap = host.Bootstrap;
        await terminal.ResizeAsync(120, 36);
        var survivor = CreateWorkspace("Bootstrap survivor");
        host.SetPanel(
            survivor,
            "main",
            "main",
            WorkspaceContent.Text("BootstrapSurvivorBody"),
            fullBleed: true,
            switchToWorkspace: true);
        await host.FlushAsync();

        List<UiLogLine> logs = [];
        void ObserveLog(UiLogLine line) => logs.Add(line);
        host.LogAdded += ObserveLog;
        try
        {
            var error = Assert.Throws<InvalidOperationException>(bootstrap.Workspace.Remove);
            Assert.Equal("Bootstrap workspace '启动' 不能移除。", error.Message);
            TerminalUi.Log("Bootstrap", "global-after-bootstrap-rejection");
            TerminalUi.LogException(
                "Bootstrap",
                new InvalidOperationException("error-after-bootstrap-rejection"));
            bootstrap.Log("Bootstrap", "bootstrap-facade-after-rejection");
            bootstrap.SetPhase("host", "宿主", UiSeverity.Success, "删除被拒绝后仍在运行");
            await host.HandleCommandAsync("/workspace switch \"unterminated");
            await host.FlushAsync();
            await terminal.RedrawAsync();

            Assert.Same(survivor, Workspace.Current);
            var screen = await terminal.CaptureScreenAsync();
            Assert.Contains("BootstrapSurvivorBody", screen, StringComparison.Ordinal);
            Assert.DoesNotContain("after-bootstrap-rejection", screen, StringComparison.Ordinal);
            Assert.Contains(
                logs,
                line => line.Text.Contains("error-after-bootstrap-rejection", StringComparison.Ordinal));

            bootstrap.Workspace.SwitchTo();
            await host.FlushAsync();
            await terminal.WaitForScreenAsync("global-after-bootstrap-rejection");
            await terminal.WaitForScreenAsync("bootstrap-facade-after-rejection");
            await terminal.WaitForScreenAsync("error-after-bootstrap-rejection");
            screen = await terminal.CaptureScreenAsync();
            Assert.Contains("运行环境", screen, StringComparison.Ordinal);
        }
        finally
        {
            host.LogAdded -= ObserveLog;
            bootstrap.Workspace.SwitchTo();
            await host.FlushAsync();
        }
    }

    sealed class InactiveHotkeyOwner : IPlugin
    {
        public string Name => nameof(InactiveHotkeyOwner);
        public string Author => "Test";
        public string[] Targets => [];
        public void Initialize(IPluginContext context) { }
    }

    Workspace CreateWorkspace(string title)
        => Own(host.CreateWorkspace(title));

    Workspace Own(Workspace workspace)
    {
        ownedWorkspaces.Add(workspace);
        return workspace;
    }

    async Task<Point> WaitForTaskbarItemAsync(string title, string otherTitle)
    {
        Point? point = null;
        await terminal.WaitForAsync(async () =>
        {
            var lines = (await terminal.CaptureScreenAsync())
                .ReplaceLineEndings("\n")
                .Split('\n');
            for (var y = 0; y < lines.Length; y++)
            {
                if (!lines[y].Contains(otherTitle, StringComparison.Ordinal))
                    continue;

                var x = lines[y].IndexOf(title, StringComparison.Ordinal);
                if (x < 0)
                    continue;

                point = new(x + title.Length / 2, y);
                return true;
            }
            return false;
        });
        return point!.Value;
    }

    async Task InjectTextAsync(string text)
    {
        foreach (var character in text)
            await terminal.InjectAsync(new Key(character));
    }

}

public sealed class UiHostShutdownProcessTests
{
    [Fact]
    public Task PluginDisposeAndFlushCompleteBeforeHostStops()
        => RunScenarioAsync(
            "plugin-before-host-stop",
            nameof(PluginDisposeAndFlushCompleteBeforeHostStops),
            RunPluginShutdownAsync);

    [Fact]
    public Task AdmissionFencePrecedesBlockingOverlayDetach()
        => RunScenarioAsync(
            "shutdown-admission-fence",
            nameof(AdmissionFencePrecedesBlockingOverlayDetach),
            RunAdmissionFenceAsync);

    [Fact]
    public Task ShutdownHandshakeDoesNotDependOnPostOrder()
        => RunScenarioAsync(
            "shutdown-worker-before-drain",
            nameof(ShutdownHandshakeDoesNotDependOnPostOrder),
            RunWorkerBeforeDrainAsync);

    [Fact]
    public Task WindowQuitCancelsLifetimeBeforeWaitingForPluginLease()
        => RunScenarioAsync(
            "window-quit-cancels-lifetime",
            nameof(WindowQuitCancelsLifetimeBeforeWaitingForPluginLease),
            RunWindowQuitAsync);

    [Fact]
    public Task PluginDisposeObservesCancelledLifetimeWithoutActiveLease()
        => RunScenarioAsync(
            "plugin-dispose-sees-cancelled-lifetime",
            nameof(PluginDisposeObservesCancelledLifetimeWithoutActiveLease),
            RunDisposeAfterStartedAsync);

    [Fact]
    public Task CreateWindowFailureAbandonsPreRunIngress()
        => RunScenarioAsync(
            "create-window-failure",
            nameof(CreateWindowFailureAbandonsPreRunIngress),
            RunCreateWindowFailureAsync);

    [Fact]
    public Task PrimaryAndShutdownFailuresAreAggregated()
        => RunScenarioAsync(
            "primary-and-shutdown-failure",
            nameof(PrimaryAndShutdownFailuresAreAggregated),
            RunAggregatedFailureAsync);

    [Fact]
    public Task ApplyFailureAbandonsAcceptedFlushAndStopsWithoutHanging()
        => RunScenarioAsync(
            "apply-failure-abandons-flush",
            nameof(ApplyFailureAbandonsAcceptedFlushAndStopsWithoutHanging),
            RunApplyFailureAsync);

    [Fact]
    public Task ApplyFailureBeforeBatchBoundaryFailsLaterAcceptedFlush()
        => RunScenarioAsync(
            "apply-failure-before-batch-boundary",
            nameof(ApplyFailureBeforeBatchBoundaryFailsLaterAcceptedFlush),
            RunCrossBatchApplyFailureAsync);

    [Fact]
    public Task CommandPrimarySurvivesReportingFailureAndShutdown()
        => RunScenarioAsync(
            "command-primary-reporting-failure",
            nameof(CommandPrimarySurvivesReportingFailureAndShutdown),
            RunCommandPrimaryFailureAsync);

    [Fact]
    public Task PluginHotkeyPendingBeforeRunIsNotRegisteredAfterGenerationCloses()
        => RunScenarioAsync(
            "pending-plugin-hotkey-generation-close",
            nameof(PluginHotkeyPendingBeforeRunIsNotRegisteredAfterGenerationCloses),
            RunPendingPluginHotkeyAsync);

    static async Task RunScenarioAsync(
        string scenario,
        string methodName,
        Func<Task> childAction)
    {
        if (TerminalUiLifecycleChildProcess.IsChild(scenario))
        {
            await childAction();
            Assert.Contains(
                "stopped",
                Assert.Throws<InvalidOperationException>(() =>
                    TerminalUi.Log("shutdown-test", "late")).Message,
                StringComparison.Ordinal);
            TerminalUiLifecycleChildProcess.WriteResult("ok");
            return;
        }

        Assert.Equal(
            "ok",
            await TerminalUiLifecycleProcessTests.RunChildAsync(
                scenario,
                typeof(UiHostShutdownProcessTests),
                methodName));
    }

    static async Task RunPluginShutdownAsync()
    {
        using var terminal = new TerminalGuiTestApp();
        var host = TerminalUiLifecycleChildProcess.InitializeHost(terminal, CancellationToken.None);
        HotkeyManager.OverlaySink = host;
        var run = await terminal.StartAsync(host);
        var workspace = Workspace.Create("Shutdown plugin");
        var view = new ShutdownProbeView();
        host.SetPanel(
            workspace,
            "probe",
            "probe",
            new WorkspaceContent(() => view),
            fullBleed: true,
            switchToWorkspace: true);
        await host.FlushAsync();

        using var plugin = new PackagedPluginFixture(
            "ShutdownWorkspacePlugin",
            root => WorkspaceRemovalPluginSource(Path.Combine(root, "panel-removed")));

        host.RequestShutdown();
        await run.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal("removed", File.ReadAllText(Path.Combine(plugin.Root, "panel-removed")));
        Assert.True(view.DetachedAndDisposedWhileHostAccepted);
    }

    static async Task RunAdmissionFenceAsync()
    {
        using var terminal = new TerminalGuiTestApp();
        var host = TerminalUiLifecycleChildProcess.InitializeHost(terminal, CancellationToken.None);
        HotkeyManager.OverlaySink = host;
        var workspace = Workspace.Create("Shutdown fence");
        var navigation = HotkeyManager.HandleMouseWheelAsync(1, hasModifiers: false);
        Assert.False(navigation.IsCompleted);

        host.RequestShutdown();
        await terminal.WaitForAsync(() => HotkeyManager.OverlaySink is null);
        Assert.False(navigation.IsCompleted);
        Assert.Contains(
            "stopping",
            Assert.Throws<InvalidOperationException>(() => workspace.SetPanel(
                "late",
                "late",
                WorkspaceContent.Text("late"))).Message,
            StringComparison.Ordinal);

        var run = await terminal.StartAsync(host);
        await run.WaitAsync(TimeSpan.FromSeconds(5));
        await navigation.WaitAsync(TimeSpan.FromSeconds(5));
    }

    static async Task RunWorkerBeforeDrainAsync()
    {
        using var terminal = new TerminalGuiTestApp();
        var (host, ownerContext) = InitializeHostWithObservedPosts(terminal);
        HotkeyManager.OverlaySink = host;

        host.RequestShutdown();
        await ownerContext.FirstPostCompleted.WaitAsync(TimeSpan.FromSeconds(5));

        var run = await terminal.StartAsync(host);
        await run.WaitAsync(TimeSpan.FromSeconds(5));
    }

    static async Task RunWindowQuitAsync()
    {
        using var terminal = new TerminalGuiTestApp();
        using var lifetime = new CancellationTokenSource();
        var host = TerminalUiLifecycleChildProcess.InitializeHost(terminal, lifetime.Token);
        host.ShutdownStarting += lifetime.Cancel;
        HotkeyManager.OverlaySink = host;
        var run = await terminal.StartAsync(host);

        using var plugin = new PackagedPluginFixture(
            "LeaseHoldingPlugin",
            root => LeaseHoldingPluginSource(Path.Combine(root, "entered")));
        var started = PluginManager.TriggerStartedForPluginsAsync([plugin.Plugin], lifetime.Token);
        await WaitForFileAsync(Path.Combine(plugin.Root, "entered"));

        await terminal.InvokeAsync(() =>
            Assert.True(terminal.Application.TopRunnableView!.InvokeCommand(Command.Quit)));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await started);
        await run.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(lifetime.IsCancellationRequested);
    }

    static async Task RunDisposeAfterStartedAsync()
    {
        using var terminal = new TerminalGuiTestApp();
        using var lifetime = new CancellationTokenSource();
        var host = TerminalUiLifecycleChildProcess.InitializeHost(terminal, lifetime.Token);
        host.ShutdownStarting += lifetime.Cancel;
        HotkeyManager.OverlaySink = host;
        var run = await terminal.StartAsync(host);

        using var plugin = new PackagedPluginFixture(
            "CancellationObservingPlugin",
            root => CancellationObservingPluginSource(Path.Combine(root, "disposed")));
        await PluginManager.TriggerStartedForPluginsAsync([plugin.Plugin], lifetime.Token);
        Assert.False(lifetime.IsCancellationRequested);

        host.RequestShutdown();
        await run.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal("disposed", File.ReadAllText(Path.Combine(plugin.Root, "disposed")));
    }

    static async Task RunCreateWindowFailureAsync()
    {
        using var terminal = new TerminalGuiTestApp();
        var host = TerminalUiLifecycleChildProcess.InitializeHost(terminal, CancellationToken.None);
        var workspace = Workspace.Create("CreateWindow failure");
        host.SetPanel(
            workspace,
            "pending",
            "pending",
            WorkspaceContent.Text("pending"),
            fullBleed: true,
            switchToWorkspace: true);
        var flush = host.FlushAsync();
        Config.WorkspaceTaskbarTitleOrder = null!;

        var failure = await Record.ExceptionAsync(async () =>
        {
            var run = await terminal.StartAsync(host);
            await run;
        });
        var flushFailure = await Record.ExceptionAsync(async () => await flush);

        Assert.Contains("Config.WorkspaceTaskbarTitleOrder", Assert.IsType<InvalidOperationException>(failure).Message);
        Assert.Contains("accepted flush event", Assert.IsType<InvalidOperationException>(flushFailure).Message);
    }

    static async Task RunAggregatedFailureAsync()
    {
        using var terminal = new TerminalGuiTestApp();
        var host = TerminalUiLifecycleChildProcess.InitializeHost(terminal, CancellationToken.None);
        using var plugin = new PackagedPluginFixture(
            "FailingShutdownPlugin",
            _ => FailingShutdownPluginSource());
        Config.WorkspaceTaskbarTitleOrder = null!;

        var failure = await Record.ExceptionAsync(async () =>
        {
            var run = await terminal.StartAsync(host);
            await run;
        });
        var aggregate = Assert.IsType<AggregateException>(failure).Flatten();

        Assert.Contains(
            aggregate.InnerExceptions,
            exception => exception.Message.Contains(
                "Config.WorkspaceTaskbarTitleOrder",
                StringComparison.Ordinal));
        Assert.Contains(
            aggregate.InnerExceptions,
            exception => exception.ToString().Contains(
                "shutdown-cleanup-failure",
                StringComparison.Ordinal));
    }

    static async Task RunApplyFailureAsync()
    {
        using var terminal = new TerminalGuiTestApp();
        var host = TerminalUiLifecycleChildProcess.InitializeHost(terminal, CancellationToken.None);
        var workspace = Workspace.Create("Apply failure");
        var panelReconciled = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        host.SetPanel(
            workspace,
            "prefix",
            "prefix",
            new WorkspaceContent(() =>
            {
                panelReconciled.TrySetResult();
                return new Label { Text = "reconciled-prefix" };
            }),
            fullBleed: true,
            switchToWorkspace: true);
        host.LogAdded += _ => throw new InvalidOperationException("apply-log-failure");
        TerminalUi.Log("Test", "will fail");
        var flush = host.FlushAsync();
        host.RequestShutdown();

        var start = terminal.StartAsync(host);
        var flushFailure = await Record.ExceptionAsync(async () =>
            await flush.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.True(panelReconciled.Task.IsCompletedSuccessfully);
        Assert.Contains(
            "apply-log-failure",
            Assert.IsType<InvalidOperationException>(flushFailure).Message);

        var runFailure = await Record.ExceptionAsync(async () =>
        {
            var run = await start;
            await run.WaitAsync(TimeSpan.FromSeconds(5));
        });

        Assert.Contains(
            "apply-log-failure",
            Assert.IsType<InvalidOperationException>(runFailure).Message);
    }

    static async Task RunCrossBatchApplyFailureAsync()
    {
        using var terminal = new TerminalGuiTestApp();
        var host = TerminalUiLifecycleChildProcess.InitializeHost(terminal, CancellationToken.None);
        host.LogAdded += line =>
        {
            if (line.Text == "first-batch-failure")
                throw new InvalidOperationException("first-batch-failure");
        };
        host.Log("first-batch-failure", UiSeverity.Error);
        for (var index = 0; index < 255; index++)
            host.Log($"abandoned-{index}", UiSeverity.Info);
        host.Log("later-batch", UiSeverity.Info);
        var flush = host.FlushAsync();

        var run = await terminal.StartAsync(host);
        var flushFailure = await Record.ExceptionAsync(async () =>
            await flush.WaitAsync(TimeSpan.FromSeconds(5)));
        var runFailure = await Record.ExceptionAsync(async () =>
            await run.WaitAsync(TimeSpan.FromSeconds(5)));

        Assert.Contains("first-batch-failure", flushFailure?.ToString());
        Assert.Contains("first-batch-failure", runFailure?.ToString());
    }

    static async Task RunCommandPrimaryFailureAsync()
    {
        var originalCwd = Directory.GetCurrentDirectory();
        var tempDir = Path.Combine(
            Path.GetTempPath(),
            "ura-command-primary-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(tempDir, "Plugins"));
        Directory.SetCurrentDirectory(tempDir);
        try
        {
            const string pluginName = "CommandPrimaryFailure";
            PluginCompiler.CompilePackage(
                CommandPrimaryFailurePluginSource(pluginName),
                pluginName,
                Path.Combine(tempDir, "Plugins", $"{pluginName}.zip"));

            using var terminal = new TerminalGuiTestApp();
            var host = TerminalUiLifecycleChildProcess.InitializeHost(
                terminal,
                CancellationToken.None);
            PluginManager.Init();
            PluginManager.InitializeLoadedPlugins();
            var pluginType = Assert.Single(PluginManager.LoadedPlugins).GetType();
            host.LogAdded += line =>
            {
                if (line.Text == "[Command] command-primary-trigger")
                    throw new InvalidOperationException("command-primary-trigger");
            };

            var run = await terminal.StartAsync(host);
            var command = host.HandleCommandAsync($"/plugin unload {pluginName}");
            var disposeEntered = (bool)(await Task.Run(() => pluginType
                .GetMethod("WaitUntilDisposing")!
                .Invoke(null, null)))!;
            pluginType.GetMethod("ReleaseDispose")!.Invoke(null, null);
            Assert.True(disposeEntered);

            var commandFailure = await Record.ExceptionAsync(async () =>
                await command.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.False(commandFailure is TimeoutException);

            var runFailure = await Record.ExceptionAsync(async () =>
                await run.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.Equal("command-primary-trigger", runFailure?.Message);
        }
        finally
        {
            Directory.SetCurrentDirectory(originalCwd);
            try { Directory.Delete(tempDir, recursive: true); } catch { }
        }
    }

    static async Task RunPendingPluginHotkeyAsync()
    {
        using var terminal = new TerminalGuiTestApp();
        var host = TerminalUiLifecycleChildProcess.InitializeHost(terminal, CancellationToken.None);
        HotkeyManager.OverlaySink = host;
        using var plugin = new PackagedPluginFixture(
            "PendingWorkspaceHotkeyPlugin",
            _ => PendingWorkspaceHotkeyPluginSource());

        host.RequestShutdown();
        var run = await terminal.StartAsync(host);
        await run.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.DoesNotContain(
            (ConsoleKey.F19, ConsoleModifiers.None),
            HotkeyManager.Hotkeys.Keys);
    }

    static async Task WaitForFileAsync(string path)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        while (!File.Exists(path))
            await Task.Delay(10, timeout.Token);
    }

    static (UiHost Host, FirstPostSynchronizationContext OwnerContext)
        InitializeHostWithObservedPosts(TerminalGuiTestApp terminal)
    {
        var currentConfig = typeof(Config).GetProperty(
            "Current",
            BindingFlags.NonPublic | BindingFlags.Static)!;
        Assert.Null(currentConfig.GetValue(null));
        currentConfig.SetValue(null, new YamlConfig());

        UiHost? host = null;
        FirstPostSynchronizationContext? ownerContext = null;
        terminal.RunOnOwnerThread(() =>
        {
            ownerContext = new(SynchronizationContext.Current!);
            host = new(
                terminal.Application,
                ownerContext,
                CancellationToken.None);
            TerminalUi.Initialize(host);
        });
        return (host!, ownerContext!);
    }

    static string CommandPrimaryFailurePluginSource(string pluginName)
        => $$"""
            using System;
            using System.Threading;
            using UmamusumeResponseAnalyzer.Plugin;
            using UmamusumeResponseAnalyzer.TerminalGui;

            public sealed class {{pluginName}} : IPlugin
            {
                static readonly ManualResetEventSlim Disposing = new();
                static readonly ManualResetEventSlim Release = new();

                public string Name => "{{pluginName}}";
                public string Author => "Test";
                public string[] Targets => Array.Empty<string>();
                public void Initialize(IPluginContext context) { }
                public static bool WaitUntilDisposing()
                    => Disposing.Wait(TimeSpan.FromSeconds(5));
                public static void ReleaseDispose() => Release.Set();
                public void Dispose()
                {
                    Disposing.Set();
                    if (!Release.Wait(TimeSpan.FromSeconds(5)))
                        throw new TimeoutException("Dispose was not released.");
                    TerminalUi.Log("Command", "command-primary-trigger");
                }
            }
            """;

    static string WorkspaceRemovalPluginSource(string marker)
        => $$"""
            using System.IO;
            using UmamusumeResponseAnalyzer.Plugin;
            using UmamusumeResponseAnalyzer.TerminalGui;

            public sealed class Plugin : IPlugin
            {
                public void Initialize(IPluginContext context) { }

                public void Dispose()
                {
                    var removed = Workspace.Current.RemovePanel("probe");
                    File.WriteAllText(@"{{marker.Replace("\"", "\"\"")}}", removed ? "removed" : "missing");
                }
            }
            """;

    static string LeaseHoldingPluginSource(string entered)
        => $$"""
            using System;
            using System.IO;
            using System.Threading;
            using System.Threading.Tasks;
            using UmamusumeResponseAnalyzer.Plugin;

            public sealed class Plugin : IPlugin
            {
                public void Initialize(IPluginContext context)
                    => context.Events.OnStarted(async cancellationToken =>
                    {
                        File.WriteAllText(@"{{entered.Replace("\"", "\"\"")}}", "entered");
                        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                    });
            }
            """;

    static string CancellationObservingPluginSource(string disposed)
        => $$"""
            using System;
            using System.IO;
            using System.Threading;
            using System.Threading.Tasks;
            using UmamusumeResponseAnalyzer.Plugin;

            public sealed class Plugin : IPlugin
            {
                CancellationToken lifetimeToken;

                public void Initialize(IPluginContext context)
                    => context.Events.OnStarted(cancellationToken =>
                    {
                        lifetimeToken = cancellationToken;
                        return ValueTask.CompletedTask;
                    });

                public void Dispose()
                {
                    if (!lifetimeToken.IsCancellationRequested)
                        throw new InvalidOperationException("plugin lifetime was not cancelled");
                    File.WriteAllText(@"{{disposed.Replace("\"", "\"\"")}}", "disposed");
                }
            }
            """;

    static string FailingShutdownPluginSource()
        => """
            using System;
            using UmamusumeResponseAnalyzer.Plugin;

            public sealed class Plugin : IPlugin
            {
                public void Initialize(IPluginContext context) { }
                public void Dispose()
                    => throw new InvalidOperationException("shutdown-cleanup-failure");
            }
            """;

    static string PendingWorkspaceHotkeyPluginSource()
        => """
            using System;
            using UmamusumeResponseAnalyzer.Plugin;
            using UmamusumeResponseAnalyzer.TerminalGui;

            public sealed class Plugin : IPlugin
            {
                public void Initialize(IPluginContext context)
                {
                    var workspace = Workspace.Create("Pending plugin hotkey");
                    workspace.BindHotkey(ConsoleKey.F19, description: "pending plugin workspace");
                }
            }
            """;

    sealed class PackagedPluginFixture : IDisposable
    {
        public PackagedPluginFixture(string internalName, Func<string, string> source)
        {
            Root = Path.Combine(
                Path.GetTempPath(),
                $"ura-uihost-plugin-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path.Combine(Root, "Plugins"));
            var originalCwd = Directory.GetCurrentDirectory();
            try
            {
                Directory.SetCurrentDirectory(Root);
                PluginCompiler.CompilePackage(
                    source(Root),
                    internalName,
                    Path.Combine(Root, "Plugins", $"{internalName}.zip"));
                PluginManager.Init();
                PluginManager.InitializeLoadedPlugins();
                Plugin = Assert.Single(
                    PluginManager.SnapshotLoadedPlugins(),
                    plugin => PluginManager.InternalName(plugin) == internalName);
            }
            finally
            {
                Directory.SetCurrentDirectory(originalCwd);
            }
        }

        public string Root { get; }
        public IPlugin Plugin { get; }

        public void Dispose()
        {
            try { Directory.Delete(Root, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    sealed class ShutdownProbeView : Label
    {
        public bool DetachedAndDisposedWhileHostAccepted { get; private set; }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (!disposing || DetachedAndDisposedWhileHostAccepted)
                return;

            TerminalUi.Log("shutdown-test", "realized panel disposed");
            DetachedAndDisposedWhileHostAccepted = SuperView is null;
        }
    }

    sealed class FirstPostSynchronizationContext(SynchronizationContext owner)
        : SynchronizationContext
    {
        readonly TaskCompletionSource firstPostCompleted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task FirstPostCompleted => firstPostCompleted.Task;

        public override void Post(SendOrPostCallback callback, object? state)
        {
            owner.Post(_ =>
            {
                try
                {
                    callback(state);
                }
                finally
                {
                    firstPostCompleted.TrySetResult();
                }
            }, null);
        }

        public override SynchronizationContext CreateCopy() => this;
    }
}
