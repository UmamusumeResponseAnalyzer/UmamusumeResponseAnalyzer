using i18n = UmamusumeResponseAnalyzer.Localization.TerminalGui;
using System.Drawing;
using System.Runtime.CompilerServices;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;
using UmamusumeResponseAnalyzer.TerminalGui;
using Xunit;

namespace UmamusumeResponseAnalyzer.Tests;

[Collection("HotkeyManager")]
public sealed class WorkspaceViewTests
{
    [Theory]
    [InlineData("en-US", "Startup")]
    [InlineData("zh-CN", "启动")]
    [InlineData("ja-JP", "起動")]
    public async Task TaskbarLocalizesBootstrapAndPersistsStableTitles(string culture, string displayTitle)
    {
        var originalCulture = i18n.Culture;
        i18n.Culture = System.Globalization.CultureInfo.GetCultureInfo(culture);
        try
        {
            using var terminal = new TerminalGuiTestApp(width: 60, height: 10);
            var bootstrap = new Workspace(Workspace.BootstrapTitle);
            var other = new Workspace("Other");
            Workspace? switched = null;
            string[]? saved = null;
            using var taskbar = new WorkspaceTaskbarView(
                bootstrap, () => false, workspace => switched = workspace,
                [other.Title, Workspace.BootstrapTitle], order => saved = [.. order]);
            taskbar.Refresh([bootstrap, other], bootstrap);
            using var window = WindowWith(taskbar.BottomEdgeTrigger, taskbar);
            window.MouseEvent += (_, mouse) => taskbar.HandleMousePosition(mouse);
            var run = await StartAsync(terminal, window);
            try
            {
                await terminal.MoveMouseAsync(new Point(0, 9));
                await terminal.WaitForScreenAsync(displayTitle);
                var screen = await terminal.CaptureScreenAsync();
                Assert.True(screen.IndexOf("Other", StringComparison.Ordinal) < screen.IndexOf(displayTitle, StringComparison.Ordinal));
                if (culture != "zh-CN")
                    Assert.DoesNotContain("启动", screen, StringComparison.Ordinal);
                var bootstrapPoint = FindText(screen, displayTitle);
                await terminal.ClickAsync(bootstrapPoint);
                await terminal.WaitForAsync(() => ReferenceEquals(switched, bootstrap));

                var otherPoint = FindText(screen, "Other");
                var target = otherPoint with { X = otherPoint.X - 2 };
                await terminal.InjectAsync(MouseAt(terminal, bootstrapPoint, MouseFlags.LeftButtonPressed));
                await terminal.InjectAsync(MouseAt(terminal, target, MouseFlags.LeftButtonPressed | MouseFlags.PositionReport));
                await terminal.InjectAsync(MouseAt(terminal, target, MouseFlags.LeftButtonReleased));
                await terminal.WaitForAsync(() => saved is not null);
                Assert.NotNull(saved);
                Assert.Equal(["启动", "Other"], saved);
            }
            finally
            {
                await StopAsync(terminal, run);
            }
        }
        finally
        {
            i18n.Culture = originalCulture;
        }
    }

    [Theory]
    [InlineData("", 3, 1)]
    [InlineData("\r\n\r\n", 3, 3)]
    [InlineData("a\nb\n", 3, 3)]
    [InlineData("abc", 3, 1)]
    [InlineData("abcd", 3, 2)]
    [InlineData("ab cd ef", 4, 2)]
    [InlineData("马娘A", 3, 2)]
    [InlineData("😀😀A", 3, 2)]
    [InlineData("\u0301\u0301", 1, 1)]
    [InlineData("\u0301马", 1, 2)]
    [InlineData("马\u0301", 1, 2)]
    public void TextPanelHeightPreservesHardWrapping(string text, int width, int expectedHeight)
    {
        var workspace = new Workspace("Text height");
        using var viewport = new WorkspaceViewport(workspace) { Frame = new(0, 0, width, 1) };
        var view = new View { Text = text, Height = 0 };
        viewport.SetPanel(Panel(workspace, "text", "Text", new(() => view), 1, fullBleed: true));

        viewport.Reconcile();

        Assert.Equal(expectedHeight, Assert.IsType<DimAbsolute>(view.Height).Size);
    }

    [Fact]
    public async Task ReconcileRealizesOnlyFinalContentAndReusesItsView()
    {
        using var terminal = new TerminalGuiTestApp(width: 50, height: 12);
        using var viewport = new WorkspaceViewport(new Workspace(Workspace.BootstrapTitle));
        var workspace = new Workspace("Model batch");
        var inactiveWorkspace = new Workspace("Inactive");
        Button? finalView = null;
        var finalContent = new WorkspaceContent(() =>
        {
            if (finalView is not null)
                return new Button { Text = "RECREATED" };

            finalView = new Button { Text = "FINAL-CONTENT" };
            return finalView;
        });
        viewport.SetActiveWorkspace(workspace);
        viewport.SetPanel(Panel(
            workspace,
            "global",
            "Discarded one",
            new(() =>
            {
                throw new InvalidOperationException("Discarded content was realized.");
            }),
            1));
        viewport.SetPanel(Panel(
            workspace,
            "global",
            "Discarded two",
            new(() =>
            {
                throw new InvalidOperationException("Discarded content was realized.");
            }),
            2));
        viewport.SetPanel(Panel(workspace, "global", "Final title", finalContent, 3));
        viewport.Reconcile();

        using var window = WindowWith(viewport);
        var run = await StartAsync(terminal, window);
        try
        {
            var screen = await terminal.CaptureScreenAsync();
            Assert.Contains("Final title", screen);
            Assert.Contains("FINAL-CONTENT", screen);
            Assert.DoesNotContain("DISCARDED", screen);

            await terminal.InvokeAsync(() =>
            {
                finalView!.Text = "PERSISTED-STATE";
                finalView.SetFocus();
                viewport.SetPanel(Panel(workspace, "global", "Renamed title", finalContent, 4));
                viewport.SetPanel(Panel(
                    inactiveWorkspace,
                    "inactive",
                    "Inactive title",
                    new(() => throw new InvalidOperationException("Inactive content was realized.")),
                    1));
                viewport.Reconcile();
            });
            await terminal.RedrawAsync();
            screen = await terminal.CaptureScreenAsync();

            Assert.Contains("Renamed title", screen);
            Assert.Contains("PERSISTED-STATE", screen);
            Assert.DoesNotContain("RECREATED", screen);
            Assert.Same(
                finalView,
                await terminal.InvokeAsync(() => window.MostFocused));
        }
        finally
        {
            await StopAsync(terminal, run);
        }
    }

    [Fact]
    public async Task SelectionUsesReferenceGenerationKeyOrderAndLatestFullBleed()
    {
        using var terminal = new TerminalGuiTestApp(width: 48, height: 10);
        using var viewport = new WorkspaceViewport(new Workspace(Workspace.BootstrapTitle));
        var oldGeneration = new Workspace("Shared title");
        var newGeneration = new Workspace("shared TITLE");
        viewport.SetPanel(Panel(
            oldGeneration,
            "ghost",
            "Ghost title",
            WorkspaceContent.Text("OLD-GENERATION"),
            1));
        viewport.SetPanel(Panel(
            newGeneration,
            "z-global",
            "Second title",
            WorkspaceContent.Text("SECOND-GLOBAL"),
            2));
        viewport.SetPanel(Panel(
            newGeneration,
            "a-global",
            "First title",
            WorkspaceContent.Text("FIRST-GLOBAL"),
            3));
        viewport.SetActiveWorkspace(oldGeneration);
        viewport.RemoveWorkspace(oldGeneration, newGeneration);
        viewport.Reconcile();

        using var window = WindowWith(viewport);
        var run = await StartAsync(terminal, window);
        try
        {
            var screen = await terminal.CaptureScreenAsync();
            Assert.DoesNotContain("OLD-GENERATION", screen);
            Assert.Contains("FIRST-GLOBAL", screen);
            Assert.Contains("SECOND-GLOBAL", screen);
            var firstTitle = screen.IndexOf("First title", StringComparison.Ordinal);
            var secondTitle = screen.IndexOf("Second title", StringComparison.Ordinal);
            Assert.True(firstTitle >= 0);
            Assert.True(secondTitle >= 0);
            Assert.True(firstTitle < secondTitle);
            Assert.DoesNotContain("Plugin -", screen);

            await terminal.InvokeAsync(() =>
            {
                viewport.SetPanel(Panel(
                    newGeneration,
                    "full-new",
                    "New full bleed",
                    new(() => new Label
                    {
                        Text = "LATEST-FULL-BLEED",
                        Height = Dim.Fill()
                    }),
                    11,
                    fullBleed: true));
                viewport.SetPanel(Panel(
                    newGeneration,
                    "full-old",
                    "Old full bleed",
                    WorkspaceContent.Text("OLDER-FULL-BLEED"),
                    10,
                    fullBleed: true));
                viewport.Reconcile();
            });
            await terminal.RedrawAsync();
            screen = await terminal.CaptureScreenAsync();

            Assert.Contains("LATEST-FULL-BLEED", screen);
            Assert.DoesNotContain("OLDER-FULL-BLEED", screen);
            Assert.DoesNotContain("FIRST-GLOBAL", screen);

            await terminal.InvokeAsync(() =>
            {
                viewport.SetPanel(Panel(
                    newGeneration,
                    "full-explicit",
                    "Explicit full bleed",
                    new(() => new Label
                    {
                        Text = string.Join(
                            Environment.NewLine,
                            Enumerable.Range(1, 20).Select(index => $"explicit-{index:00}")),
                        Height = 20
                    }),
                    12,
                    fullBleed: true));
                viewport.Reconcile();
                viewport.SetFocus();
            });
            await terminal.RedrawAsync();
            Assert.Contains("explicit-20", await terminal.CaptureScreenAsync());

            await terminal.InjectAsync(Key.Home);
            await terminal.RedrawAsync();
            Assert.Contains("explicit-01", await terminal.CaptureScreenAsync());
        }
        finally
        {
            await StopAsync(terminal, run);
        }
    }

    [Theory]
    [InlineData(ReleaseAction.Replace)]
    [InlineData(ReleaseAction.RemovePanel)]
    [InlineData(ReleaseAction.RemoveWorkspace)]
    [InlineData(ReleaseAction.Dispose)]
    public void ReleasedPanelsDoNotKeepFactoryOrViewAlive(ReleaseAction action)
    {
        var tracked = CreateReleasedPanel(action);

        Assert.True(tracked.Signal.Disposed);
        Collect();

        Assert.False(tracked.FactoryOwner.IsAlive);
        Assert.False(tracked.View.IsAlive);
        GC.KeepAlive(tracked.Viewport);
    }

    [Theory]
    [InlineData("replace")]
    [InlineData("remove-panel")]
    [InlineData("invalidate")]
    [InlineData("switch")]
    [InlineData("remove-workspace")]
    [InlineData("full-bleed")]
    [InlineData("dispose")]
    public async Task PanelTransitionsReleaseSliderBeforeDetachAndRestoreMouseRouting(string transition)
    {
        using var terminal = new TerminalGuiTestApp();
        var workspace = new Workspace("Mouse capture");
        var other = new Workspace("Other");
        using var viewport = new WorkspaceViewport(workspace);
        View? panel = null;
        Button? focused = null;
        ScrollSlider? slider = null;
        var content = new WorkspaceContent(() =>
        {
            panel = new View { Height = 12 };
            focused = new Button { Text = "FOCUS" };
            slider = new ScrollSlider { X = 2, Y = 2, Width = 1, Height = 5 };
            panel.Add(focused, slider);
            return panel;
        });
        viewport.SetPanel(Panel(workspace, "main", "Main", content, 1));
        viewport.Reconcile();
        var originalPanel = panel!;
        var originalSlider = slider!;
        var originalFocus = focused!;
        var receiver = new Button { Text = "RECEIVER", X = 50, Y = 1, MousePositionTracking = true };
        var entered = false;
        var wheeled = false;
        receiver.MouseEnter += (_, _) => entered = true;
        receiver.MouseEvent += (_, mouse) => wheeled |= mouse.Flags == MouseFlags.WheeledDown;
        receiver.Accepted += (_, _) => receiver.Text = "CLICKED";
        using var window = WindowWith(viewport, receiver);
        var run = await StartAsync(terminal, window);
        var mouse = terminal.Application.Mouse;
        var releasedWhileAttached = false;
        var panelDisposing = false;
        originalPanel.Disposing += (_, _) => panelDisposing = true;
        void Released(object? sender, ViewEventArgs args)
        {
            if (ReferenceEquals(args.View, originalSlider))
                releasedWhileAttached = ReferenceEquals(originalSlider.App, terminal.Application) &&
                    originalPanel.SuperView is not null && !panelDisposing;
        }
        mouse.UnGrabbedMouse += Released;
        try
        {
            var pressPoint = await terminal.InvokeAsync(() =>
            {
                originalFocus.SetFocus();
                return originalSlider.FrameToScreen().Location;
            });
            await terminal.InjectAsync(MouseAt(terminal, pressPoint, MouseFlags.LeftButtonPressed));
            await terminal.WaitForAsync(() => mouse.IsGrabbed(originalSlider));
            await terminal.InvokeAsync(() =>
            {
                Assert.Same(originalFocus, window.MostFocused);
                switch (transition)
                {
                    case "replace":
                        viewport.SetPanel(Panel(workspace, "main", "New", WorkspaceContent.Text("new"), 2));
                        break;
                    case "remove-panel":
                        viewport.RemovePanel(workspace, "main");
                        break;
                    case "invalidate":
                        viewport.InvalidatePanel(workspace, "main");
                        break;
                    case "switch":
                        viewport.SetActiveWorkspace(other);
                        break;
                    case "remove-workspace":
                        viewport.RemoveWorkspace(workspace, other);
                        break;
                    case "full-bleed":
                        viewport.SetPanel(Panel(workspace, "full", "Full", WorkspaceContent.Text("full"), 2, fullBleed: true));
                        break;
                    case "dispose":
                        viewport.Dispose();
                        window.Remove(viewport);
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(transition));
                }
                if (transition != "dispose")
                    viewport.Reconcile();
                Assert.True(releasedWhileAttached);
                Assert.False(mouse.IsGrabbed());
            });

            await terminal.InjectAsync(MouseAt(terminal, pressPoint, MouseFlags.LeftButtonReleased));
            await terminal.MoveMouseAsync(new Point(79, 23));
            var receiverPoint = await terminal.InvokeAsync(() => receiver.FrameToScreen().Location);
            await terminal.MoveMouseAsync(receiverPoint);
            await terminal.WaitForAsync(() => entered);
            await terminal.InjectAsync(MouseAt(terminal, receiverPoint, MouseFlags.WheeledDown));
            await terminal.WaitForAsync(() => wheeled);
            await terminal.ClickAsync(receiverPoint);
            await terminal.WaitForScreenAsync("CLICKED");
        }
        finally
        {
            mouse.UnGrabbedMouse -= Released;
            await StopAsync(terminal, run);
        }
    }

    [Theory]
    [InlineData("panel")]
    [InlineData("wrapper")]
    [InlineData("margin")]
    [InlineData("border-child")]
    [InlineData("padding-child")]
    public async Task RebuildReleasesCaptureInPanelAndLayoutAdornmentTrees(string owner)
    {
        using var terminal = new TerminalGuiTestApp();
        var workspace = new Workspace("Adornment capture");
        using var viewport = new WorkspaceViewport(workspace);
        var panel = new View { Height = 10 };
        var content = new WorkspaceContent(() => panel);
        viewport.SetPanel(Panel(workspace, "main", "Main", content, 1));
        viewport.Reconcile();
        using var window = WindowWith(viewport);
        var run = await StartAsync(terminal, window);
        try
        {
            await terminal.InvokeAsync(() =>
            {
                var captured = owner switch
                {
                    "panel" => panel,
                    "wrapper" => panel.SuperView!,
                    "margin" => panel.Margin.GetOrCreateView(),
                    "border-child" => panel.SuperView!.Border.GetOrCreateView(),
                    "padding-child" => panel.Padding.GetOrCreateView(),
                    _ => throw new ArgumentOutOfRangeException(nameof(owner))
                };
                if (owner.EndsWith("-child", StringComparison.Ordinal))
                {
                    var child = new View();
                    captured.Add(child);
                    captured = child;
                }
                var mouse = terminal.Application.Mouse;
                mouse.GrabMouse(captured);
                Assert.True(mouse.IsGrabbed(captured));
                viewport.SetPanel(Panel(workspace, "main", "Renamed", content, 2));
                viewport.Reconcile();
                Assert.False(mouse.IsGrabbed());
            });
        }
        finally
        {
            await StopAsync(terminal, run);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancelledCaptureReleaseKeepsHierarchyIntactAndAllowsRetry(bool dispose)
    {
        using var terminal = new TerminalGuiTestApp();
        var workspace = new Workspace("Cancelled release");
        using var viewport = new WorkspaceViewport(workspace);
        var panel = new View { Height = 10 };
        var child = new View();
        panel.Add(child);
        viewport.SetPanel(Panel(workspace, "main", "Main", new(() => panel), 1));
        viewport.Reconcile();
        using var window = WindowWith(viewport);
        var run = await StartAsync(terminal, window);
        try
        {
            await terminal.InvokeAsync(() =>
            {
                var mouse = terminal.Application.Mouse;
                var parent = panel.SuperView;
                var layoutViews = viewport.SubViews.ToArray();
                var disposing = false;
                panel.Disposing += (_, _) => disposing = true;
                mouse.GrabMouse(child);
                void Cancel(object? sender, GrabMouseEventArgs args) => args.Cancel = true;
                mouse.UnGrabbingMouse += Cancel;
                if (!dispose)
                    viewport.RemovePanel(workspace, "main");
                Action change = dispose ? viewport.Dispose : viewport.Reconcile;
                try
                {
                    var error = Assert.Throws<InvalidOperationException>(change);
                    Assert.Equal(string.Format(i18n.Workspace_MouseCaptureReleaseFailed, workspace.Title), error.Message);
                    Assert.Same(parent, panel.SuperView);
                    Assert.Equal(layoutViews, viewport.SubViews);
                    Assert.Same(terminal.Application, child.App);
                    Assert.False(disposing);
                    Assert.True(mouse.IsGrabbed(child));
                }
                finally
                {
                    mouse.UnGrabbingMouse -= Cancel;
                }
                change();
                Assert.False(mouse.IsGrabbed());
                Assert.True(disposing);
                if (dispose)
                    window.Remove(viewport);
            });
        }
        finally
        {
            await StopAsync(terminal, run);
        }
    }

    [Fact]
    public async Task RedrawScrollResizeAndUnrelatedCapturesRemainIndependent()
    {
        using var terminal = new TerminalGuiTestApp();
        var workspace = new Workspace("Keep capture");
        using var viewport = new WorkspaceViewport(workspace);
        var child = new View();
        var panel = new View { Height = 60 };
        panel.Add(child);
        var content = new WorkspaceContent(() => panel);
        viewport.SetPanel(Panel(workspace, "main", "Main", content, 1));
        viewport.Reconcile();
        var external = new View { X = 50, Width = 5, Height = 5 };
        using var window = WindowWith(viewport, external);
        var run = await StartAsync(terminal, window);
        try
        {
            await terminal.InvokeAsync(() => terminal.Application.Mouse.GrabMouse(child));
            await terminal.RedrawAsync();
            await terminal.ResizeAsync(90, 30);
            await terminal.InvokeAsync(() =>
            {
                var mouse = terminal.Application.Mouse;
                viewport.Reconcile();
                viewport.Navigate(Command.Start);
                Assert.True(mouse.IsGrabbed(child));
                mouse.UngrabMouse();
                mouse.GrabMouse(external);
                viewport.SetPanel(Panel(workspace, "main", "Renamed", content, 2));
                viewport.Reconcile();
                Assert.True(mouse.IsGrabbed(external));
                mouse.UngrabMouse();

                using var dialog = new Dialog { Width = 30, Height = 10 };
                var dialogChild = new View();
                dialog.Add(dialogChild);
                var dialogSession = terminal.Application.Begin(dialog)!;
                try
                {
                    mouse.GrabMouse(dialogChild);
                    viewport.SetPanel(Panel(workspace, "main", "Behind modal", content, 3));
                    viewport.Reconcile();
                    Assert.True(mouse.IsGrabbed(dialogChild));
                }
                finally
                {
                    terminal.Application.End(dialogSession);
                }
                mouse.GrabMouse(external);
                viewport.Dispose();
                Assert.True(mouse.IsGrabbed(external));
                mouse.UngrabMouse();
                window.Remove(viewport);
            });
        }
        finally
        {
            await StopAsync(terminal, run);
        }
    }

    [Fact]
    public void FactoriesRejectAttachedSharedAndReleasedViews()
    {
        using (var owner = new View())
        using (var viewport = new WorkspaceViewport(new Workspace(Workspace.BootstrapTitle)))
        {
            var workspace = new Workspace("Attached");
            var attached = new View();
            owner.Add(attached);
            viewport.SetActiveWorkspace(workspace);
            viewport.SetPanel(Panel(
                workspace,
                "attached",
                "Attached",
                new(() => attached),
                1,
                fullBleed: true));

            Assert.Throws<InvalidOperationException>(viewport.Reconcile);
        }

        using (var viewport = new WorkspaceViewport(new Workspace(Workspace.BootstrapTitle)))
        {
            var workspace = new Workspace("Shared");
            var shared = new View();
            viewport.SetActiveWorkspace(workspace);
            viewport.SetPanel(Panel(workspace, "a", "A", new(() => shared), 1));
            viewport.SetPanel(Panel(workspace, "b", "B", new(() => shared), 2));

            Assert.Throws<InvalidOperationException>(viewport.Reconcile);
        }

        using (var viewport = new WorkspaceViewport(new Workspace(Workspace.BootstrapTitle)))
        {
            var workspace = new Workspace("Released");
            var signal = new DisposeSignal();
            var released = new TrackingView(signal) { Text = "released" };
            viewport.SetActiveWorkspace(workspace);
            viewport.SetPanel(Panel(
                workspace,
                "panel",
                "First",
                new(() => released),
                1,
                fullBleed: true));
            viewport.Reconcile();
            viewport.SetPanel(Panel(
                workspace,
                "panel",
                "Replacement",
                WorkspaceContent.Text("replacement"),
                2,
                fullBleed: true));
            viewport.Reconcile();
            Assert.True(signal.Disposed);
            viewport.SetPanel(Panel(
                workspace,
                "panel",
                "Released again",
                new(() => released),
                3,
                fullBleed: true));

            Assert.Throws<InvalidOperationException>(viewport.Reconcile);
        }
    }

    [Fact]
    public void DisposeContinuesAfterOwnedViewThrows()
    {
        var first = new DisposeSignal();
        var second = new DisposeSignal();
        var viewport = new WorkspaceViewport(new Workspace(Workspace.BootstrapTitle));
        var workspace = new Workspace("Dispose");
        viewport.SetActiveWorkspace(workspace);
        viewport.SetPanel(Panel(
            workspace,
            "a",
            "Throwing",
            new(() => new ThrowingDisposeView(first)),
            1));
        viewport.SetPanel(Panel(
            workspace,
            "b",
            "Tracking",
            new(() => new TrackingView(second)),
            2));
        viewport.Reconcile();

        Assert.NotNull(Record.Exception(viewport.Dispose));
        Assert.True(first.Disposed);
        Assert.True(second.Disposed);
    }

    [Fact]
    public async Task ViewportStartsAtBottomNavigatesAndClampsAfterContentShrinks()
    {
        using var terminal = new TerminalGuiTestApp(width: 32, height: 8);
        using var viewport = new WorkspaceViewport(new Workspace(Workspace.BootstrapTitle));
        var workspace = new Workspace("Scroll");
        viewport.SetActiveWorkspace(workspace);
        viewport.SetPanel(Panel(
            workspace,
            "log",
            "Long output",
            WorkspaceContent.Text(string.Join(
                Environment.NewLine,
                Enumerable.Range(1, 50).Select(index => $"line-{index:00}"))),
            1));
        viewport.Reconcile();

        using var window = WindowWith(viewport);
        var run = await StartAsync(terminal, window);
        try
        {
            await terminal.InvokeAsync(viewport.SetFocus);
            var screen = await terminal.CaptureScreenAsync();
            Assert.Contains("line-50", screen);
            Assert.DoesNotContain("line-01", screen);

            await terminal.InjectAsync(Key.Home);
            await terminal.RedrawAsync();
            Assert.Contains("line-01", await terminal.CaptureScreenAsync());

            await terminal.InjectAsync(Key.PageDown);
            await terminal.InjectAsync(Key.End);
            await terminal.RedrawAsync();
            Assert.Contains("line-50", await terminal.CaptureScreenAsync());

            await terminal.InjectAsync(Key.CursorUp);
            await terminal.InjectAsync(Key.CursorDown);
            await terminal.ResizeAsync(14, 6);
            await terminal.RedrawAsync();
            Assert.Contains("line-50", await terminal.CaptureScreenAsync());

            Button? focusTarget = null;
            await terminal.InvokeAsync(() =>
            {
                viewport.Navigate(Command.Start);
                viewport.SetPanel(Panel(
                    workspace,
                    "log",
                    "Short output",
                    new(() =>
                    {
                        var root = new View { Width = Dim.Fill(), Height = Dim.Fill() };
                        root.Add(
                            new Label { Text = "Short body", Width = Dim.Fill(), Height = 1 },
                            focusTarget = new Button { Text = "Focus target", Y = 2 });
                        return root;
                    }),
                    2,
                    fullBleed: true));
                viewport.Reconcile();
            });
            await terminal.RedrawAsync();
            screen = await terminal.CaptureScreenAsync();

            Assert.Contains("Short body", screen);
            Assert.DoesNotContain("line-", screen);
            await terminal.InvokeAsync(() => focusTarget!.SetFocus());
            Assert.Same(
                focusTarget,
                await terminal.InvokeAsync(() => window.MostFocused));
        }
        finally
        {
            await StopAsync(terminal, run);
        }
    }

    [Fact]
    public async Task TaskbarHoverClickDragResizeAndViewportStayIndependent()
    {
        using var terminal = new TerminalGuiTestApp(width: 48, height: 12);
        using var viewport = new WorkspaceViewport(new Workspace(Workspace.BootstrapTitle));
        var alpha = new Workspace("Alpha");
        var beta = new Workspace("Beta");
        var gamma = new Workspace("Gamma");
        var workspaceAction = false;
        Workspace? switched = null;
        string[]? saved = null;
        var snapshot = new[] { alpha, beta, gamma };
        WorkspaceTaskbarView? taskbar = null;
        taskbar = new(
            alpha,
            () => false,
            workspace =>
            {
                switched = workspace;
                taskbar!.Refresh(snapshot, workspace);
            },
            [],
            order => saved = [.. order]);
        using (taskbar)
        {
            viewport.SetActiveWorkspace(alpha);
            viewport.SetPanel(Panel(
                alpha,
                "main",
                "Main",
                new(() =>
                {
                    var root = new View { Width = Dim.Fill(), Height = Dim.Fill() };
                    var action = new Button
                    {
                        Text = "GO",
                        X = 0,
                        Y = Pos.AnchorEnd(2)
                    };
                    action.Accepted += (_, _) => workspaceAction = true;
                    root.Add(
                        new Label { Text = "VIEWPORT-BODY" },
                        action);
                    return root;
                }),
                1,
                fullBleed: true));
            viewport.Reconcile();
            taskbar.Refresh(snapshot, alpha);

            using var window = WindowWith(viewport, taskbar.BottomEdgeTrigger, taskbar);
            window.MouseEvent += (_, mouse) => taskbar.HandleMousePosition(mouse);
            var run = await StartAsync(terminal, window);
            try
            {
                await terminal.MoveMouseAsync(new Point(0, 11));
                await terminal.WaitForScreenAsync("Alpha");
                var screen = await terminal.CaptureScreenAsync();
                Assert.Contains("VIEWPORT-BODY", screen);
                Assert.Contains("Alpha", screen);
                Assert.Contains("Beta", screen);
                Assert.Contains("Gamma", screen);

                await terminal.ClickAsync(FindText(screen, "GO"));
                await terminal.WaitForAsync(() => workspaceAction);

                await terminal.MoveMouseAsync(new Point(0, 11));
                await terminal.WaitForScreenAsync("Beta");
                screen = await terminal.CaptureScreenAsync();
                var betaPoint = FindText(screen, "Beta");
                var beforeHover = await terminal.CaptureAttributeAsync(betaPoint);
                await terminal.MoveMouseAsync(betaPoint);
                await terminal.RedrawAsync();
                var afterHover = await terminal.CaptureAttributeAsync(betaPoint);
                Assert.NotEqual(beforeHover, afterHover);

                var alphaHoverPoint = FindText(
                    await terminal.CaptureScreenAsync(),
                    "Alpha");
                await terminal.MoveMouseAsync(alphaHoverPoint);
                await terminal.RedrawAsync();
                Assert.Contains("Gamma", await terminal.CaptureScreenAsync());

                await terminal.MoveMouseAsync(betaPoint);
                var bottomBlank = betaPoint with { Y = 11 };
                await terminal.MoveMouseAsync(bottomBlank);
                await terminal.WaitForAsync(async () =>
                    !(await terminal.CaptureScreenAsync()).Contains(
                        "Gamma",
                        StringComparison.Ordinal));

                await terminal.MoveMouseAsync(bottomBlank);
                await terminal.RedrawAsync();
                Assert.DoesNotContain("Gamma", await terminal.CaptureScreenAsync());

                await terminal.MoveMouseAsync(Point.Empty);
                await terminal.MoveMouseAsync(bottomBlank);
                await terminal.WaitForScreenAsync("Gamma");
                screen = await terminal.CaptureScreenAsync();
                betaPoint = FindText(screen, "Beta");

                await terminal.ClickAsync(betaPoint);
                await terminal.WaitForAsync(() => ReferenceEquals(switched, beta));

                screen = await terminal.CaptureScreenAsync();
                var gammaPoint = FindText(screen, "Gamma");
                var alphaPoint = FindText(screen, "Alpha");
                var dragTarget = alphaPoint with
                {
                    X = Math.Max(0, alphaPoint.X - alpha.Title.Length / 2)
                };
                await terminal.InjectAsync(MouseAt(
                    terminal,
                    gammaPoint,
                    MouseFlags.LeftButtonPressed));
                await terminal.InjectAsync(MouseAt(
                    terminal,
                    dragTarget,
                    MouseFlags.LeftButtonPressed | MouseFlags.PositionReport));
                await terminal.InvokeAsync(() =>
                {
                    Assert.True(terminal.Application.Mouse.IsGrabbed());
                    viewport.SetPanel(Panel(alpha, "extra", "Extra", WorkspaceContent.Text("extra"), 2));
                    viewport.Reconcile();
                    Assert.True(terminal.Application.Mouse.IsGrabbed());
                });
                await terminal.InjectAsync(MouseAt(
                    terminal,
                    dragTarget,
                    MouseFlags.LeftButtonReleased));
                await terminal.WaitForAsync(() => saved is not null);

                Assert.Equal(["Gamma", "Alpha", "Beta"], saved!);
                Assert.Same(beta, switched);

                await terminal.ResizeAsync(16, 8);
                await terminal.MoveMouseAsync(Point.Empty);
                await terminal.MoveMouseAsync(new Point(0, 7));
                await terminal.RedrawAsync();
                screen = await terminal.CaptureScreenAsync();
                Assert.Contains("VIEWPORT-BODY", screen);
                Assert.Contains("…", screen);
            }
            finally
            {
                await StopAsync(terminal, run);
            }
        }
    }

    [Fact]
    public async Task BootstrapDashboardRendersCoreStateAndExplicitLogText()
    {
        using var terminal = new TerminalGuiTestApp(width: 120, height: 36);
        const string logText = "[Plugin] bootstrap-log";
        using var dashboard = new BootstrapDashboardView(
            [("版本", "M1-test"), ("工作目录", "K:\\repo")],
            [new("插件初始化", UiSeverity.Success, "初始化完成")],
            [new("ExamplePlugin", "1.2.3", "OK", "初始化完成")],
            [
                .. Enumerable.Range(1, 40)
                    .Select(index => new BootstrapLogRow("INFO", $"older-log-{index:00}")),
                new("WARN", logText)
            ]);
        using var window = WindowWith(dashboard);
        var run = await StartAsync(terminal, window);
        try
        {
            var screen = await terminal.CaptureScreenAsync();

            Assert.Contains(Localization.LaunchMenu.I18N_Environment, screen);
            Assert.Contains(Localization.LaunchMenu.I18N_InitializationResults, screen);
            Assert.Contains(Localization.LaunchMenu.I18N_PluginSummary, screen);
            Assert.Contains(Localization.LaunchMenu.I18N_RecentLogs, screen);
            Assert.Contains("M1-test", screen);
            Assert.Contains("ExamplePlugin", screen);
            Assert.Contains($"WARN {logText}", screen);
            Assert.Throws<ArgumentOutOfRangeException>(
                () => BootstrapWorkspace.SeverityLabel((UiSeverity)int.MaxValue));
        }
        finally
        {
            await StopAsync(terminal, run);
        }
    }

    static WorkspacePanel Panel(
        Workspace workspace,
        string key,
        string title,
        WorkspaceContent content,
        long sequence,
        bool fullBleed = false)
        => new(workspace, key, title, content, sequence, fullBleed);

    static Window WindowWith(params View[] views)
    {
        var window = new Window
        {
            Width = Dim.Fill(),
            Height = Dim.Fill(),
            BorderStyle = null
        };
        window.Add(views);
        return window;
    }

    static async Task<Task> StartAsync(TerminalGuiTestApp terminal, Window window)
        => await terminal.StartAsync(
            () => terminal.Application.RunAsync(window, CancellationToken.None));

    static async Task StopAsync(TerminalGuiTestApp terminal, Task run)
    {
        await terminal.InvokeAsync(() => terminal.Application.RequestStop());
        await run.WaitAsync(TimeSpan.FromSeconds(5));
    }

    static Point FindText(string screen, string text)
    {
        var lines = screen.ReplaceLineEndings("\n").Split('\n');
        for (var y = 0; y < lines.Length; y++)
        {
            var x = lines[y].IndexOf(text, StringComparison.Ordinal);
            if (x >= 0)
                return new(x + Math.Max(0, text.Length / 2), y);
        }
        throw new InvalidOperationException($"Framebuffer 中没有找到 '{text}'。");
    }

    static Mouse MouseAt(TerminalGuiTestApp terminal, Point point, MouseFlags flags)
        => new()
        {
            ScreenPosition = point,
            Flags = flags,
            Timestamp = terminal.Time.Now
        };

    [MethodImpl(MethodImplOptions.NoInlining)]
    static TrackedRelease CreateReleasedPanel(ReleaseAction action)
    {
        var viewport = new WorkspaceViewport(new Workspace(Workspace.BootstrapTitle));
        var workspace = new Workspace("Release");
        var factoryOwner = new object();
        var signal = new DisposeSignal();
        WeakReference? viewReference = null;
        var content = new WorkspaceContent(() =>
        {
            GC.KeepAlive(factoryOwner);
            var view = new TrackingView(signal) { Text = "tracked", Height = Dim.Fill() };
            viewReference = new(view);
            return view;
        });
        viewport.SetActiveWorkspace(workspace);
        viewport.SetPanel(Panel(workspace, "tracked", "Tracked", content, 1, fullBleed: true));
        viewport.Reconcile();

        switch (action)
        {
            case ReleaseAction.Replace:
                viewport.SetPanel(Panel(
                    workspace,
                    "tracked",
                    "Replacement",
                    WorkspaceContent.Text("replacement"),
                    2,
                    fullBleed: true));
                viewport.Reconcile();
                break;
            case ReleaseAction.RemovePanel:
                viewport.RemovePanel(workspace, "tracked");
                viewport.Reconcile();
                break;
            case ReleaseAction.RemoveWorkspace:
                viewport.RemoveWorkspace(
                    workspace,
                    new Workspace(Workspace.BootstrapTitle));
                viewport.Reconcile();
                break;
            case ReleaseAction.Dispose:
                viewport.Dispose();
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(action), action, null);
        }

        return new(
            viewport,
            signal,
            new WeakReference(factoryOwner),
            viewReference ?? throw new InvalidOperationException("Tracked View was not realized."));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static void Collect()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    public enum ReleaseAction
    {
        Replace,
        RemovePanel,
        RemoveWorkspace,
        Dispose
    }

    sealed record TrackedRelease(
        WorkspaceViewport Viewport,
        DisposeSignal Signal,
        WeakReference FactoryOwner,
        WeakReference View);

    sealed class DisposeSignal
    {
        public bool Disposed { get; set; }
    }

    class TrackingView(DisposeSignal signal) : View
    {
        protected override void Dispose(bool disposing)
        {
            if (disposing)
                signal.Disposed = true;
            base.Dispose(disposing);
        }
    }

    sealed class ThrowingDisposeView(DisposeSignal signal) : TrackingView(signal)
    {
        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing)
                throw new InvalidOperationException("Dispose failed after releasing the View.");
        }
    }
}
