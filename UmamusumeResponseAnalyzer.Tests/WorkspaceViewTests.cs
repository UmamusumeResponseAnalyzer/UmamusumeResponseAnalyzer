using i18n = UmamusumeResponseAnalyzer.Localization.TerminalGui;
using System.Drawing;
using System.Runtime.CompilerServices;
using Terminal.Gui.Input;
using Terminal.Gui.Text;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;
using UmamusumeResponseAnalyzer.Plugin;
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
            terminal.Application.Mouse.MouseEvent += (_, mouse) => taskbar.HandleMousePosition(mouse);
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
        var bottomEdgeAction = false;
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
                    var edgeAction = new Button { Text = "EDGE", X = 0, Y = Pos.AnchorEnd() };
                    edgeAction.Accepted += (_, _) => bottomEdgeAction = true;
                    root.Add(
                        new Label { Text = "VIEWPORT-BODY" },
                        action,
                        edgeAction);
                    return root;
                }),
                1,
                fullBleed: true));
            viewport.Reconcile();
            taskbar.Refresh(snapshot, alpha);

            using var window = WindowWith(viewport, taskbar.BottomEdgeTrigger, taskbar);
            terminal.Application.Mouse.MouseEvent += (_, mouse) => taskbar.HandleMousePosition(mouse);
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
                Assert.Equal(11, FindText(screen, "Alpha").Y);
                Assert.Equal(11, FindText(screen, "Beta").Y);
                Assert.Equal(11, FindText(screen, "Gamma").Y);

                await terminal.ClickAsync(FindText(screen, "GO"));
                await terminal.WaitForAsync(() => workspaceAction);
                await terminal.ClickAsync(FindText(await terminal.CaptureScreenAsync(), "EDGE"));
                await terminal.WaitForAsync(() => bottomEdgeAction);

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
                var bottomBlank = new Point(47, 11);
                await terminal.MoveMouseAsync(bottomBlank);
                await terminal.RedrawAsync();
                Assert.Contains("Gamma", await terminal.CaptureScreenAsync());

                var aboveBottom = bottomBlank with { Y = 10 };
                await terminal.MoveMouseAsync(aboveBottom);
                await terminal.WaitForAsync(async () =>
                    !(await terminal.CaptureScreenAsync()).Contains(
                        "Gamma",
                        StringComparison.Ordinal));

                await terminal.MoveMouseAsync(aboveBottom);
                await terminal.RedrawAsync();
                Assert.DoesNotContain("Gamma", await terminal.CaptureScreenAsync());

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
                var outsideDragTarget = dragTarget with { Y = 10 };
                await terminal.InjectAsync(MouseAt(
                    terminal,
                    outsideDragTarget,
                    MouseFlags.LeftButtonPressed | MouseFlags.PositionReport));
                await terminal.RedrawAsync();
                Assert.Contains("Gamma", await terminal.CaptureScreenAsync());
                Assert.True(await terminal.InvokeAsync(() => terminal.Application.Mouse.IsGrabbed()));
                await terminal.InjectAsync(MouseAt(
                    terminal,
                    outsideDragTarget,
                    MouseFlags.LeftButtonReleased));
                await terminal.WaitForAsync(() => saved is not null);

                Assert.Equal(["Gamma", "Alpha", "Beta"], saved!);
                Assert.Same(beta, switched);
                Assert.False(await terminal.InvokeAsync(() => terminal.Application.Mouse.IsGrabbed()));
                await terminal.RedrawAsync();
                Assert.DoesNotContain("Gamma", await terminal.CaptureScreenAsync());

                await terminal.ResizeAsync(16, 8);
                await terminal.MoveMouseAsync(Point.Empty);
                await terminal.MoveMouseAsync(new Point(0, 7));
                await terminal.RedrawAsync();
                screen = await terminal.CaptureScreenAsync();
                Assert.Contains("VIEWPORT-BODY", screen);
                Assert.Contains("…", screen);
                Assert.Equal(7, FindText(screen, "Beta").Y);
            }
            finally
            {
                await StopAsync(terminal, run);
            }
        }
    }

    [Theory]
    [InlineData(80, 24, "127.0.0.2", true, "127.0.0.2:4693")]
    [InlineData(120, 35, "0:0:0:0:0:0:0:1", true, "[::1]:4693")]
    [InlineData(80, 24, "0.0.0.0", false, "0.0.0.0:4693")]
    public async Task BootstrapKeepsDiagnosticsAndLogsFoldedAndReflowsToolbar(
        int width, int height, string listenHost, bool localAccess, string binding)
    {
        using var terminal = new TerminalGuiTestApp(width, height);
        terminal.RunOnOwnerThread(UraTheme.Apply);
        using var bootstrap = new BootstrapView(_ => { }, () => { }, () => { });
        var snapshot = new BootstrapSnapshot(
            new("M1-test", "K:\\repo", "config.yaml", listenHost, 4693, "日文", "女", "test-update-source"),
            [new(BootstrapPhaseId.Database, BootstrapPhaseState.Succeeded, "data-ready")],
            [], [new("INFO", "[Plugin] bootstrap-log")], BootstrapMode.Ready, null);
        bootstrap.Update(snapshot);
        using var window = WindowWith(bootstrap);
        var run = await StartAsync(terminal, window);
        try
        {
            var screen = await terminal.CaptureScreenAsync();
            TestContext.Current.TestOutputHelper?.WriteLine($"Bootstrap ready {width}x{height}\n{screen}");
            Assert.Contains("4693", screen);
            Assert.Contains(localAccess ? Localization.LaunchMenu.I18N_LocalAccess : Localization.LaunchMenu.I18N_DeviceAccess,
                Descendants(bootstrap).Single(view => view.Id == "bootstrap-summary").Text);
            Assert.DoesNotContain("M1-test", screen);
            Assert.DoesNotContain("test-update-source", screen);
            Assert.DoesNotContain("bootstrap-log", screen);
            Assert.DoesNotContain("data-ready", screen);
            Assert.True(Descendants(bootstrap).Single(view => view.Id == "bootstrap-settings-toggle").Visible);
            Assert.False(Descendants(bootstrap).Single(view => view.Id == "bootstrap-details-toggle").Visible);
            var toolbarButtons = Descendants(bootstrap).OfType<Button>()
                .Where(button => new[] { "bootstrap-start", "bootstrap-options", "bootstrap-plugins", "bootstrap-update-data", "bootstrap-more" }.Contains(button.Id))
                .ToArray();
            Assert.Equal(5, toolbarButtons.Length);
            Assert.All(toolbarButtons, button => Assert.Contains(button.Text, screen));
            await terminal.ResizeAsync(38, 16);
            await terminal.RedrawAsync();
            var narrowScreen = await terminal.CaptureScreenAsync();
            TestContext.Current.TestOutputHelper?.WriteLine($"Bootstrap ready 38x16\n{narrowScreen}");
            Assert.All(toolbarButtons, button => Assert.Contains(button.Text, narrowScreen));
            var body = Descendants(bootstrap).Single(view => view.Id == "bootstrap-body");
            await terminal.InvokeAsync(() => body.ScrollVertical(
                Descendants(bootstrap).Single(view => view.Id == "bootstrap-summary-detail").Frame.Y - body.Viewport.Y));
            await terminal.RedrawAsync();
            var scrolledScreen = await terminal.CaptureScreenAsync();
            TestContext.Current.TestOutputHelper?.WriteLine($"Bootstrap scrolled 38x16\n{scrolledScreen}");
            var message = new TextFormatter
            {
                Text = Localization.LaunchMenu.I18N_NoPluginsAvailable,
                WordWrap = true,
                ConstrainToWidth = body.Viewport.Width
            };
            Assert.All(message.GetLines(), line => Assert.Contains(line, scrolledScreen));
            var settingsToggle = Descendants(bootstrap).Single(view => view.Id == "bootstrap-settings-toggle");
            for (var attempt = 0; attempt < toolbarButtons.Length + 2 &&
                 !await terminal.InvokeAsync(() => ReferenceEquals(window.MostFocused, settingsToggle)); attempt++)
                await terminal.InjectAsync(Key.Tab);
            Assert.Same(settingsToggle, await terminal.InvokeAsync(() => window.MostFocused));
            Assert.Contains(Localization.LaunchMenu.I18N_StartupSettings, await terminal.CaptureScreenAsync());
            await terminal.InvokeAsync(() =>
            {
                foreach (var button in toolbarButtons)
                {
                    var origin = button.ViewportToScreen(Point.Empty);
                    Assert.InRange(origin.X, 0, 37);
                    Assert.InRange(origin.Y, 0, 15);
                    Assert.True(origin.X + button.Frame.Width <= 38);
                }
                Assert.True(toolbarButtons.Select(button => button.Frame.Y).Distinct().Count() > 1);
            });
            await terminal.ResizeAsync(width, height);
            await terminal.InjectAsync(Key.Enter);
            await terminal.WaitForScreenAsync(binding);
            await terminal.InvokeAsync(() => bootstrap.Update(snapshot with { Mode = BootstrapMode.Information, Listening = binding }));
            await terminal.RedrawAsync();
            Assert.False(Descendants(bootstrap).Single(view => view.Id == "bootstrap-settings-toggle").Visible);
            var toggle = Descendants(bootstrap).Single(view => view.Id == "bootstrap-details-toggle");
            await terminal.InvokeAsync(() => toggle.SetFocus());
            await terminal.InjectAsync(Key.Enter);
            await terminal.WaitForScreenAsync("bootstrap-log");
            Assert.Contains("data-ready", await terminal.CaptureScreenAsync());
            var detailLists = Descendants(bootstrap).Single(view => view.Id == "bootstrap-details").SubViews.OfType<ListView>();
            var emptyPluginList = Assert.Single(detailLists, list => list.Source?.Count == 0);
            Assert.False(emptyPluginList.Visible);
            Assert.Equal(0, emptyPluginList.Frame.Height);
            Assert.Throws<ArgumentOutOfRangeException>(
                () => BootstrapWorkspace.SeverityLabel((UiSeverity)int.MaxValue));
        }
        finally
        {
            await StopAsync(terminal, run);
        }
    }

    [Theory]
    [InlineData("中")]
    [InlineData("😀")]
    public async Task BootstrapLogsScrollWideCharactersToVisibleEnd(string wideText)
    {
        using var terminal = new TerminalGuiTestApp(width: 80, height: 24);
        terminal.RunOnOwnerThread(UraTheme.Apply);
        const string suffix = "ABCDEFGHIJKLMNOPQRS";
        using var bootstrap = new BootstrapView(_ => { }, () => { }, () => { });
        var snapshot = new BootstrapSnapshot(null, [], [], [new("INFO", wideText + suffix)], BootstrapMode.Information, "127.0.0.1:4693");
        bootstrap.Update(snapshot);
        var logList = (ListView)Descendants(bootstrap).Single(view => view.Id == "bootstrap-logs");
        using var window = WindowWith(bootstrap);
        var run = await StartAsync(terminal, window);
        try
        {
            await terminal.InvokeAsync(() => Descendants(bootstrap).Single(view => view.Id == "bootstrap-details-toggle").SetFocus());
            await terminal.InjectAsync(Key.Enter);
            await terminal.InvokeAsync(() =>
            {
                logList.SetFocus();
                var text = wideText + new string('A', logList.Viewport.Width - suffix.Length - 1) + suffix;
                bootstrap.Update(snapshot with { Logs = [new("INFO", text)] });
            });
            await terminal.InvokeAsync(() => logList.ScrollHorizontal("● INFO ".GetColumns()));
            await terminal.RedrawAsync();
            Assert.Contains(wideText, await terminal.CaptureScreenAsync());

            await terminal.InvokeAsync(() =>
            {
                logList.ScrollHorizontal(logList.Source!.MaxItemLength);
                Assert.Equal(
                    logList.Source.MaxItemLength - logList.Viewport.Width,
                    logList.Viewport.X);
            });
            await terminal.RedrawAsync();
            Assert.Contains(suffix, await terminal.CaptureScreenAsync());
        }
        finally
        {
            await StopAsync(terminal, run);
        }
    }

    [Fact]
    public async Task BootstrapMouseWheelScrollsBodyAndChainsAtListBoundaries()
    {
        using var terminal = new TerminalGuiTestApp(width: 80, height: 24);
        terminal.RunOnOwnerThread(UraTheme.Apply);
        using var bootstrap = new BootstrapView(_ => { }, () => { }, () => { });
        bootstrap.Update(new(null,
            Enum.GetValues<BootstrapPhaseId>().Select(id => new BootstrapPhase(id, BootstrapPhaseState.Succeeded, "ready")).ToArray(),
            Enumerable.Range(1, 6).Select(index => new BootstrapPluginRow($"plugin-{index}", $"Plugin {index}", "1.0", BootstrapPluginState.Active, "active")).ToArray(),
            Enumerable.Range(0, 40).Select(index => new BootstrapLogRow("INFO", $"wheel-log-{index:00}")).ToArray(),
            BootstrapMode.Information, "127.0.0.1:4693"));
        using var window = WindowWith(bootstrap);
        var run = await StartAsync(terminal, window);
        try
        {
            var body = Descendants(bootstrap).Single(view => view.Id == "bootstrap-body");
            var toggle = Descendants(bootstrap).Single(view => view.Id == "bootstrap-details-toggle");
            var phases = Descendants(bootstrap).Single(view => view.Id == "bootstrap-details").SubViews.OfType<ListView>().First();
            var logs = (ListView)Descendants(bootstrap).Single(view => view.Id == "bootstrap-logs");
            await terminal.InvokeAsync(() => toggle.SetFocus());
            await terminal.InjectAsync(Key.Enter);
            await terminal.RedrawAsync();

            var blankPoint = await terminal.InvokeAsync(() => body.ViewportToScreen(new Point(0, 1)));
            await terminal.InjectAsync(MouseAt(terminal, blankPoint, MouseFlags.WheeledDown));
            Assert.Equal(1, await terminal.InvokeAsync(() => body.Viewport.Y));
            await terminal.InjectAsync(MouseAt(terminal, blankPoint, MouseFlags.WheeledUp));
            Assert.Equal(0, await terminal.InvokeAsync(() => body.Viewport.Y));

            var phasePoint = await terminal.InvokeAsync(() => phases.ViewportToScreen(new Point(2, 0)));
            await terminal.InjectAsync(MouseAt(terminal, phasePoint, MouseFlags.WheeledDown));
            Assert.Equal(1, await terminal.InvokeAsync(() => body.Viewport.Y));
            Assert.Equal(0, await terminal.InvokeAsync(() => phases.Viewport.Y));
            Assert.Same(toggle, await terminal.InvokeAsync(() => window.MostFocused));

            await terminal.InvokeAsync(() => logs.SetFocus());
            await terminal.InjectAsync(Key.End);
            var bodyPosition = await terminal.InvokeAsync(() => body.Viewport.Y);
            var logPosition = await terminal.InvokeAsync(() => logs.Viewport.Y);
            var logPoint = await terminal.InvokeAsync(() => logs.ViewportToScreen(new Point(2, logs.Viewport.Height - 1)));
            await terminal.InjectAsync(MouseAt(terminal, logPoint, MouseFlags.WheeledUp));
            Assert.Equal(logPosition - 1, await terminal.InvokeAsync(() => logs.Viewport.Y));
            Assert.Equal(bodyPosition, await terminal.InvokeAsync(() => body.Viewport.Y));
            Assert.Equal(39, await terminal.InvokeAsync(() => logs.SelectedItem));

            await terminal.InjectAsync(Key.Home);
            bodyPosition = await terminal.InvokeAsync(() => body.Viewport.Y);
            Assert.True(bodyPosition > 0);
            logPoint = await terminal.InvokeAsync(() => logs.ViewportToScreen(new Point(2, 0)));
            await terminal.InjectAsync(MouseAt(terminal, logPoint, MouseFlags.WheeledUp));
            Assert.Equal(0, await terminal.InvokeAsync(() => logs.Viewport.Y));
            Assert.Equal(bodyPosition - 1, await terminal.InvokeAsync(() => body.Viewport.Y));

            await terminal.InjectAsync(Key.End);
            bodyPosition = await terminal.InvokeAsync(() => body.Viewport.Y);
            logPosition = await terminal.InvokeAsync(() => logs.Viewport.Y);
            logPoint = await terminal.InvokeAsync(() => logs.ViewportToScreen(new Point(2, logs.Viewport.Height - 1)));
            await terminal.InjectAsync(MouseAt(terminal, logPoint, MouseFlags.WheeledDown));
            Assert.Equal(logPosition, await terminal.InvokeAsync(() => logs.Viewport.Y));
            Assert.Equal(bodyPosition + 1, await terminal.InvokeAsync(() => body.Viewport.Y));
            Assert.Same(logs, await terminal.InvokeAsync(() => window.MostFocused));
        }
        finally
        {
            await StopAsync(terminal, run);
        }
    }

    [Theory]
    [InlineData(true, MouseFlags.LeftButtonClicked)]
    [InlineData(false, MouseFlags.LeftButtonClicked)]
    [InlineData(false, MouseFlags.RightButtonClicked)]
    public async Task BootstrapMouseClickKeepsItsLogRowAndTabRestoresSelectionVisibility(bool fullSequence, MouseFlags clicked)
    {
        using var terminal = new TerminalGuiTestApp(width: 120, height: 35);
        terminal.RunOnOwnerThread(UraTheme.Apply);
        using var bootstrap = new BootstrapView(_ => { }, () => { }, () => { });
        bootstrap.Update(new(null,
            Enum.GetValues<BootstrapPhaseId>().Select(id => new BootstrapPhase(id, BootstrapPhaseState.Succeeded, "ready")).ToArray(),
            Enumerable.Range(1, 6).Select(index => new BootstrapPluginRow($"plugin-{index}", $"Plugin {index}", "1.0", BootstrapPluginState.Active, "active")).ToArray(),
            Enumerable.Range(0, 40).Select(index => new BootstrapLogRow("INFO", $"click-log-{index:00}")).ToArray(),
            BootstrapMode.Information, "127.0.0.1:4693"));
        using var window = WindowWith(bootstrap);
        var run = await StartAsync(terminal, window);
        try
        {
            var body = Descendants(bootstrap).Single(view => view.Id == "bootstrap-body");
            var more = Descendants(bootstrap).Single(view => view.Id == "bootstrap-more");
            var logs = (ListView)Descendants(bootstrap).Single(view => view.Id == "bootstrap-logs");
            await terminal.InvokeAsync(() => Descendants(bootstrap).Single(view => view.Id == "bootstrap-details-toggle").SetFocus());
            await terminal.InjectAsync(Key.Enter);
            await terminal.InvokeAsync(() => logs.SetFocus());
            await terminal.InjectAsync(Key.End);
            await terminal.InvokeAsync(() => more.SetFocus());
            var point = await terminal.InvokeAsync(() => logs.ViewportToScreen(new Point(2, 0)));
            await terminal.InjectAsync(MouseAt(terminal, point, MouseFlags.WheeledUp));
            var clickedRow = await terminal.InvokeAsync(() => logs.Viewport.Y);
            var position = await terminal.InvokeAsync(() => (logs.Viewport.Location, body.Viewport.Location));
            Assert.Equal(39, await terminal.InvokeAsync(() => logs.SelectedItem));
            var sequence = fullSequence
                ? new[] { MouseFlags.LeftButtonPressed, MouseFlags.LeftButtonReleased, clicked }
                : [clicked];
            foreach (var flags in sequence)
                await terminal.InjectAsync(MouseAt(terminal, point, flags));
            Assert.Equal(clickedRow, await terminal.InvokeAsync(() => logs.SelectedItem));
            Assert.Equal(position, await terminal.InvokeAsync(() => (logs.Viewport.Location, body.Viewport.Location)));
            Assert.Same(logs, await terminal.InvokeAsync(() => window.MostFocused));

            await terminal.InvokeAsync(() => more.SetFocus());
            await terminal.InjectAsync(MouseAt(terminal, point, MouseFlags.WheeledDown));
            Assert.True(await terminal.InvokeAsync(() => logs.Viewport.Y > clickedRow));
            for (var attempt = 0; attempt < 8 && !await terminal.InvokeAsync(() => logs.HasFocus); attempt++)
                await terminal.InjectAsync(Key.Tab);
            await terminal.InvokeAsync(() =>
            {
                Assert.Same(logs, window.MostFocused);
                Assert.Equal(clickedRow, logs.SelectedItem);
                Assert.InRange(clickedRow, logs.Viewport.Y, logs.Viewport.Y + logs.Viewport.Height - 1);
                var selectedY = logs.ViewportToScreen(new Point(0, clickedRow - logs.Viewport.Y)).Y;
                var bodyTop = body.ViewportToScreen(Point.Empty).Y;
                Assert.InRange(selectedY, bodyTop, bodyTop + body.Viewport.Height - 1);
            });
            await terminal.WaitForScreenAsync($"click-log-{clickedRow:00}");
        }
        finally
        {
            await StopAsync(terminal, run);
        }
    }

    [Theory]
    [InlineData(nameof(BootstrapPhaseId.Config))]
    [InlineData(nameof(BootstrapPhaseId.Scan))]
    [InlineData(nameof(BootstrapPhaseId.Database))]
    [InlineData(nameof(BootstrapPhaseId.Listener))]
    public async Task BootstrapFailureShowsOneCauseAndKeepsSkippedStepsOutOfErrors(string failedPhase)
    {
        using var terminal = new TerminalGuiTestApp();
        terminal.RunOnOwnerThread(UraTheme.Apply);
        using var bootstrap = new BootstrapView(_ => { }, () => { }, () => { });
        var failed = Enum.Parse<BootstrapPhaseId>(failedPhase);
        bootstrap.Update(new(null,
            [new(failed, BootstrapPhaseState.Failed, "primary-failure", "original-error"),
             new(BootstrapPhaseId.StartCallbacks, BootstrapPhaseState.Skipped, "not-executed"),
             new(BootstrapPhaseId.Host, BootstrapPhaseState.Failed, "primary-failure", "original-error")],
            [], [], BootstrapMode.Information, null));
        using var window = WindowWith(bootstrap);
        var run = await StartAsync(terminal, window);
        try
        {
            var screen = await terminal.CaptureScreenAsync();
            TestContext.Current.TestOutputHelper?.WriteLine($"Bootstrap {failed} failure 80x24\n{screen}");
            Assert.Contains("primary-failure", screen);
            if (failed != BootstrapPhaseId.Database)
                Assert.Contains(BootstrapWorkspace.PhaseLabel(failed),
                    Descendants(bootstrap).Single(view => view.Id == "bootstrap-summary").Text);
            Assert.DoesNotContain("not-executed", screen);
            Assert.DoesNotContain("original-error", screen);
            Assert.False(Descendants(bootstrap).Single(view => view.Id == "bootstrap-settings-toggle").Visible);
            Assert.Equal(failed == BootstrapPhaseId.Listener, Descendants(bootstrap).Single(view => view.Id == "bootstrap-details-toggle").Visible);
            var errors = Descendants(bootstrap).Single(view => view.Id == "bootstrap-errors");
            Assert.Equal(1, ((ListView)Descendants(errors).Single(view => view.Id == "bootstrap-error-list")).Source!.Count);
            Assert.All(Descendants(bootstrap).OfType<Button>()
                .Where(button => new[] { "bootstrap-start", "bootstrap-options", "bootstrap-plugins", "bootstrap-update-data" }.Contains(button.Id)),
                button => Assert.False(button.Enabled));
        }
        finally
        {
            await StopAsync(terminal, run);
        }
    }

    [Fact]
    public async Task BootstrapResizeKeepsFocusedErrorActionVisibleWithoutMovingOnRefresh()
    {
        using var terminal = new TerminalGuiTestApp(width: 80, height: 24);
        terminal.RunOnOwnerThread(UraTheme.Apply);
        using var bootstrap = new BootstrapView(_ => { }, () => { }, () => { });
        var snapshot = new BootstrapSnapshot(null,
            [new(BootstrapPhaseId.Database, BootstrapPhaseState.Failed,
                Localization.LaunchMenu.I18N_DatabaseUnavailable, "database-error")],
            [], [], BootstrapMode.Information, null);
        bootstrap.Update(snapshot);
        using var window = WindowWith(bootstrap);
        var run = await StartAsync(terminal, window);
        try
        {
            var copy = Descendants(bootstrap).Single(view => view.Id == "bootstrap-copy-error");
            var body = Descendants(bootstrap).Single(view => view.Id == "bootstrap-body");
            await terminal.InvokeAsync(() => copy.SetFocus());
            await terminal.RedrawAsync();
            Assert.Contains(Localization.LaunchMenu.I18N_CopyError, await terminal.CaptureScreenAsync());

            await terminal.ResizeAsync(38, 16);
            await terminal.RedrawAsync();
            var screen = await terminal.CaptureScreenAsync();
            TestContext.Current.TestOutputHelper?.WriteLine($"Focused error action after resize 38x16\n{screen}");
            await terminal.InvokeAsync(() =>
            {
                Assert.Same(copy, window.MostFocused);
                var bodyTop = body.ViewportToScreen(Point.Empty).Y;
                var copyTop = copy.ViewportToScreen(Point.Empty).Y;
                Assert.InRange(copyTop, bodyTop, bodyTop + body.Viewport.Height - copy.Frame.Height);
            });
            Assert.Contains(Localization.LaunchMenu.I18N_CopyError, screen);

            await terminal.InvokeAsync(() => Assert.True(body.ScrollVertical(-1)));
            await terminal.RedrawAsync();
            var position = await terminal.InvokeAsync(() => body.Viewport.Location);
            await terminal.InvokeAsync(() => bootstrap.Update(snapshot with { Logs = [new("INFO", "background-refresh")] }));
            await terminal.RedrawAsync();
            Assert.Equal(position, await terminal.InvokeAsync(() => body.Viewport.Location));
            Assert.Same(copy, await terminal.InvokeAsync(() => window.MostFocused));
        }
        finally
        {
            await StopAsync(terminal, run);
        }
    }

    [Theory]
    [InlineData((int)PluginFailureStage.Scan, nameof(Localization.LaunchMenu.I18N_ScanFailed))]
    [InlineData((int)PluginFailureStage.Load, nameof(Localization.LaunchMenu.I18N_LoadFailed))]
    [InlineData((int)PluginFailureStage.Initialize, nameof(Localization.LaunchMenu.I18N_InitializeFailed))]
    [InlineData((int)PluginFailureStage.Start, nameof(Localization.LaunchMenu.I18N_StartCallbackFailed))]
    [InlineData((int)PluginFailureStage.Runtime, nameof(Localization.LaunchMenu.I18N_RuntimeFailed))]
    [InlineData(null, nameof(Localization.LaunchMenu.I18N_StateUnavailable))]
    public async Task BootstrapPluginFailureKeepsListenerAndOpenedErrorStableDuringRefresh(int? failureStage, string labelResource)
    {
        using var terminal = new TerminalGuiTestApp(width: 120, height: 35);
        terminal.RunOnOwnerThread(UraTheme.Apply);
        using var bootstrap = new BootstrapView(_ => { }, () => { }, () => { });
        const string originalError = "InvalidOperationException: plugin-load-failure\n at ExamplePlugin.Initialize()";
        var snapshot = new BootstrapSnapshot(null, [],
            [new("failed", "FailedPlugin", "1.0", BootstrapPluginState.Unavailable, "load-failure", originalError, (PluginFailureStage?)failureStage),
             new("started", "StartedPlugin", "1.0", BootstrapPluginState.StartFailed, "callback-failure", "callback-error")],
            [], BootstrapMode.Information, "127.0.0.1:4693");
        bootstrap.Update(snapshot);
        using var window = WindowWith(bootstrap);
        var run = await StartAsync(terminal, window);
        try
        {
            var screen = await terminal.CaptureScreenAsync();
            Assert.Contains("127.0.0.1:4693", screen);
            Assert.Contains(string.Format(Localization.LaunchMenu.I18N_PhaseFailure, "FailedPlugin",
                Localization.LaunchMenu.ResourceManager.GetString(labelResource, Localization.LaunchMenu.Culture)), screen);
            Assert.Contains(string.Format(Localization.LaunchMenu.I18N_PhaseFailure, "StartedPlugin",
                Localization.LaunchMenu.I18N_StartCallbackFailed), screen);
            Assert.DoesNotContain("load-failure", screen);
            Assert.DoesNotContain("callback-failure", screen);
            Assert.Contains(Localization.LaunchMenu.I18N_PluginUnavailable, screen);
            await terminal.InvokeAsync(() => Descendants(bootstrap).Single(view => view.Id == "bootstrap-error-list").SetFocus());
            await terminal.InjectAsync(Key.CursorDown);
            await terminal.WaitForScreenAsync(Localization.LaunchMenu.I18N_PluginStillActive);
            await terminal.InjectAsync(Key.CursorUp);
            await terminal.WaitForScreenAsync(Localization.LaunchMenu.I18N_PluginUnavailable);
            var show = Descendants(bootstrap).Single(view => view.Id == "bootstrap-show-error");
            await terminal.InvokeAsync(() => show.SetFocus());
            await terminal.InjectAsync(Key.Enter);
            await terminal.WaitForScreenAsync("ExamplePlugin.Initialize()");
            var error = (ListView)Descendants(bootstrap).Single(view => view.Id == "bootstrap-error-details");
            await terminal.InvokeAsync(() => error.SetFocus());
            await terminal.InvokeAsync(() => bootstrap.Update(snapshot with
            {
                Plugins = [new("started", "StartedPlugin", "1.0", BootstrapPluginState.StartFailed, "callback-failure", "callback-error")],
                Logs = [new("INFO", "background-refresh")]
            }));
            Assert.Equal(originalError, await terminal.InvokeAsync(() => string.Join('\n', error.Source!.ToList().Cast<string>())));
            Assert.Same(error, await terminal.InvokeAsync(() => terminal.Application.TopRunnableView!.MostFocused));
            await terminal.InjectAsync(Key.Esc);
            Assert.False(await terminal.InvokeAsync(() => error.Visible));
        }
        finally
        {
            await StopAsync(terminal, run);
        }
    }

    static IEnumerable<View> Descendants(View view)
    {
        yield return view;
        foreach (var child in view.SubViews)
            foreach (var descendant in Descendants(child))
                yield return descendant;
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
