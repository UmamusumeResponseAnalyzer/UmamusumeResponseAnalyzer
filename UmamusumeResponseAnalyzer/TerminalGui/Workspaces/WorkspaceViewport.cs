using System.Drawing;
using System.Runtime.CompilerServices;
using System.Text;
using Terminal.Gui.Input;
using Terminal.Gui.Text;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace UmamusumeResponseAnalyzer.TerminalGui;

internal sealed class WorkspaceViewport : View
{
    static readonly object ReleasedViewMarker = new();

    readonly Dictionary<Workspace, Dictionary<string, WorkspacePanel>> panels =
        new(ReferenceEqualityComparer.Instance);
    readonly Dictionary<Workspace, Dictionary<string, RealizedPanel>> realizedPanels =
        new(ReferenceEqualityComparer.Instance);
    readonly Dictionary<Workspace, int> scrollOffsets =
        new(ReferenceEqualityComparer.Instance);
    readonly HashSet<View> ownedViews = new(ReferenceEqualityComparer.Instance);
    readonly ConditionalWeakTable<View, object> releasedViews = new();
    readonly HashSet<(Workspace Workspace, string Key)> invalidatedPanels = [];
    readonly List<View> layoutViews = [];
    readonly List<PanelLayout> panelLayouts = [];

    Workspace activeWorkspace;
    bool disposed;
    bool dirty = true;
    bool layingOut;
    bool fullBleed;
    int maxScroll;

    internal WorkspaceViewport(Workspace activeWorkspace)
    {
        this.activeWorkspace = activeWorkspace;
        Width = Dim.Fill();
        Height = Dim.Fill();
        CanFocus = true;
        TabStop = TabBehavior.TabGroup;

        AddCommand(Command.Up, () => Navigate(Command.Up));
        AddCommand(Command.Down, () => Navigate(Command.Down));
        AddCommand(Command.PageUp, () => Navigate(Command.PageUp));
        AddCommand(Command.PageDown, () => Navigate(Command.PageDown));
        AddCommand(Command.Start, () => Navigate(Command.Start));
        AddCommand(Command.End, () => Navigate(Command.End));
        KeyBindings.ReplaceCommands(Key.CursorUp, Command.Up);
        KeyBindings.ReplaceCommands(Key.CursorDown, Command.Down);
        KeyBindings.ReplaceCommands(Key.PageUp, Command.PageUp);
        KeyBindings.ReplaceCommands(Key.PageDown, Command.PageDown);
        KeyBindings.ReplaceCommands(Key.Home, Command.Start);
        KeyBindings.ReplaceCommands(Key.End, Command.End);
    }

    internal void SetPanel(WorkspacePanel panel)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (!panels.TryGetValue(panel.Workspace, out var workspacePanels))
        {
            workspacePanels = new(StringComparer.Ordinal);
            panels.Add(panel.Workspace, workspacePanels);
        }
        workspacePanels[panel.Key] = panel;
        dirty = true;
    }

    internal bool RemovePanel(Workspace workspace, string key)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (!panels.TryGetValue(workspace, out var workspacePanels) ||
            !workspacePanels.Remove(key))
        {
            return false;
        }

        if (workspacePanels.Count == 0)
            panels.Remove(workspace);
        invalidatedPanels.Remove((workspace, key));
        dirty = true;
        return true;
    }

    internal void RemoveWorkspace(Workspace workspace, Workspace replacement)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        panels.Remove(workspace);
        scrollOffsets.Remove(workspace);
        invalidatedPanels.RemoveWhere(entry => ReferenceEquals(entry.Workspace, workspace));
        if (ReferenceEquals(activeWorkspace, workspace))
            activeWorkspace = replacement;
        dirty = true;
    }

    internal void SetActiveWorkspace(Workspace workspace)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (ReferenceEquals(activeWorkspace, workspace))
            return;

        activeWorkspace = workspace;
        dirty = true;
    }

    internal void InvalidatePanel(Workspace workspace, string key)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (!panels.TryGetValue(workspace, out var workspacePanels) ||
            !workspacePanels.ContainsKey(key))
        {
            throw new InvalidOperationException(
                $"Workspace panel '{key}' is not registered.");
        }
        invalidatedPanels.Add((workspace, key));
        dirty = true;
    }

    internal void Reconcile()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (!dirty)
        {
            UpdateLayout();
            return;
        }

        ReleaseMouseCapture(includeViewport: false);
        var focused = App?.TopRunnableView?.MostFocused;
        if (focused is not null && !Contains(focused))
            focused = null;

        DetachRealizedViews();
        ClearLayoutViews();
        ReleaseObsoleteViews();
        BuildActiveLayout();
        dirty = false;
        UpdateLayout();
        SetNeedsLayout();
        SetNeedsDraw();
        if (focused is not null && Contains(focused))
            focused.SetFocus();
    }

    internal bool Navigate(Command command)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        UpdateLayout();
        var current = scrollOffsets.GetValueOrDefault(activeWorkspace);
        var next = command switch
        {
            Command.Up => Math.Min(maxScroll, current + 1),
            Command.Down => Math.Max(0, current - 1),
            Command.PageUp => Math.Min(maxScroll, current + Math.Max(1, Viewport.Height)),
            Command.PageDown => Math.Max(0, current - Math.Max(1, Viewport.Height)),
            Command.Start => maxScroll,
            Command.End => 0,
            _ => current
        };
        if (next == current)
            return false;

        scrollOffsets[activeWorkspace] = next;
        UpdateViewport();
        SetNeedsDraw();
        return true;
    }

    protected override void OnViewportChanged(DrawEventArgs e)
    {
        base.OnViewportChanged(e);
        if (!disposed && !layingOut)
            UpdateLayout();
    }

    protected override void Dispose(bool disposing)
    {
        if (!disposing)
        {
            base.Dispose(false);
            return;
        }

        List<Exception> errors = [];
        if (!disposed)
        {
            ReleaseMouseCapture(includeViewport: true);
            disposed = true;
            var views = ownedViews.ToArray();
            var wrappers = layoutViews.ToArray();

            realizedPanels.Clear();
            panels.Clear();
            scrollOffsets.Clear();
            invalidatedPanels.Clear();
            ownedViews.Clear();
            layoutViews.Clear();
            panelLayouts.Clear();
            fullBleed = false;
            maxScroll = 0;
            dirty = false;

            foreach (var view in views)
                MarkReleased(view);
            foreach (var view in views)
                ReleaseView(view, errors);
            foreach (var wrapper in wrappers)
            {
                Capture(errors, () => wrapper.SuperView?.Remove(wrapper));
                Capture(errors, wrapper.Dispose);
            }
        }

        Capture(errors, () => base.Dispose(true));
        ThrowCleanupErrors(errors);
    }

    void BuildActiveLayout()
    {
        var selected = panels.TryGetValue(activeWorkspace, out var workspacePanels)
            ? workspacePanels.Values.OrderBy(panel => panel.Key, StringComparer.Ordinal).ToArray()
            : [];
        var latestFullBleed = selected
            .Where(panel => panel.FullBleed)
            .MaxBy(panel => panel.Sequence);
        if (latestFullBleed is not null)
            selected = [latestFullBleed];
        if (selected.Length == 0)
        {
            AddMessage($"{activeWorkspace.Title} 还没有输出。");
            return;
        }

        fullBleed = selected.Length == 1 && selected[0].FullBleed;
        foreach (var panel in selected)
        {
            var realized = GetRealized(panel);
            realized.View.X = 0;
            realized.View.Y = 0;
            realized.View.Width = Dim.Fill();
            EnableFocusPath(realized.View);
            if (fullBleed)
            {
                Add(realized.View);
                panelLayouts.Add(new(realized, null));
                continue;
            }

            var frame = new FrameView
            {
                Title = panel.Title,
                X = 0,
                Width = Dim.Fill()
            };
            frame.Border.GetOrCreateView();
            frame.Add(realized.View);
            EnableFocusPath(frame);
            Add(frame);
            layoutViews.Add(frame);
            panelLayouts.Add(new(realized, frame));
        }
    }

    void AddMessage(string text)
    {
        var message = new Label
        {
            Text = text,
            X = 1,
            Y = 1,
            Width = Dim.Fill(1),
            Height = 1
        };
        Add(message);
        layoutViews.Add(message);
    }

    RealizedPanel GetRealized(WorkspacePanel panel)
    {
        if (!realizedPanels.TryGetValue(panel.Workspace, out var workspacePanels))
        {
            workspacePanels = new(StringComparer.Ordinal);
            realizedPanels.Add(panel.Workspace, workspacePanels);
        }
        if (workspacePanels.TryGetValue(panel.Key, out var existing))
            return existing;

        var view = panel.Content.CreateView();
        if (releasedViews.TryGetValue(view, out _))
        {
            throw new InvalidOperationException(
                $"Workspace panel '{panel.Key}' factory 返回了已由 viewport 释放的 View。");
        }
        if (view.SuperView is not null)
        {
            throw new InvalidOperationException(
                $"Workspace panel '{panel.Key}' factory 返回了已挂载的 View。");
        }
        if (!ownedViews.Add(view))
        {
            throw new InvalidOperationException(
                $"Workspace panel '{panel.Key}' factory 返回了已由 viewport 持有的 View。");
        }

        var realized = new RealizedPanel(
            panel.Content,
            view,
            view.Height is DimAbsolute absolute ? Math.Max(0, absolute.Size) : 0,
            view.Height is DimFill);
        workspacePanels.Add(panel.Key, realized);
        return realized;
    }

    void ReleaseObsoleteViews()
    {
        foreach (var (workspace, workspaceViews) in realizedPanels.ToArray())
        {
            panels.TryGetValue(workspace, out var workspacePanels);
            foreach (var (key, realized) in workspaceViews.ToArray())
            {
                var invalidated = invalidatedPanels.Remove((workspace, key));
                if (!invalidated &&
                    workspacePanels is not null &&
                    workspacePanels.TryGetValue(key, out var panel) &&
                    ReferenceEquals(panel.Content, realized.Content))
                {
                    continue;
                }

                workspaceViews.Remove(key);
                Release(realized);
            }
            if (workspaceViews.Count == 0)
                realizedPanels.Remove(workspace);
        }
        invalidatedPanels.Clear();
    }

    void Release(RealizedPanel realized)
    {
        var view = realized.View;
        if (!ownedViews.Remove(view))
            return;

        MarkReleased(view);
        List<Exception> errors = [];
        ReleaseView(view, errors);
        ThrowCleanupErrors(errors);
    }

    void DetachRealizedViews()
    {
        foreach (var workspacePanels in realizedPanels.Values)
        {
            foreach (var realized in workspacePanels.Values)
                realized.View.SuperView?.Remove(realized.View);
        }
    }

    void ReleaseMouseCapture(bool includeViewport)
    {
        var mouse = App?.Mouse;
        if (mouse?.IsGrabbed() is not true)
            return;

        // Terminal.Gui 2.4.17 can retain a removed view's unfocused descendant grab.
        // Check before detaching: descendants can lose their inherited App during removal.
        bool HasCapture(View view)
            => mouse.IsGrabbed(view) ||
               view.SubViews.Any(HasCapture) ||
               view.Margin?.View is { } margin && HasCapture(margin) ||
               view.Border?.View is { } border && HasCapture(border) ||
               view.Padding?.View is { } padding && HasCapture(padding);

        bool HasRemovingCapture()
            => includeViewport && HasCapture(this) ||
               ownedViews.Any(HasCapture) || layoutViews.Any(HasCapture);

        if (!HasRemovingCapture())
            return;

        mouse.UngrabMouse();
        if (HasRemovingCapture())
        {
            throw new InvalidOperationException(
                $"Workspace '{activeWorkspace.Title}' 无法解除待移除控件的鼠标捕获；已中止界面拆除。" +
                " / Mouse capture remains in the view hierarchy; detachment aborted.");
        }
    }

    void ClearLayoutViews()
    {
        foreach (var view in layoutViews)
        {
            Remove(view);
            view.Dispose();
        }
        layoutViews.Clear();
        panelLayouts.Clear();
        fullBleed = false;
    }

    void UpdateLayout()
    {
        if (layingOut)
            return;

        var size = Viewport.Size;
        if (size.Width <= 0 || size.Height <= 0)
            return;

        layingOut = true;
        try
        {
            var width = size.Width;
            var height = size.Height;
            var heights = CalculatePanelHeights(width, height);
            var contentHeight = Math.Max(height, heights.Sum());
            maxScroll = Math.Max(0, contentHeight - height);

            var y = 0;
            for (var index = 0; index < panelLayouts.Count; index++)
            {
                var layout = panelLayouts[index];
                var panelHeight = heights[index];
                if (layout.Frame is null)
                {
                    layout.Realized.View.Height = layout.Realized.FillsViewportHeight
                        ? Dim.Fill()
                        : panelHeight;
                    continue;
                }

                layout.Frame.Y = y;
                layout.Frame.Height = panelHeight;
                layout.Realized.View.Height = Math.Max(1, panelHeight - 2);
                y += panelHeight;
            }

            SetContentSize(new Size(width, contentHeight));
            UpdateViewport();
        }
        finally
        {
            layingOut = false;
        }
    }

    void UpdateViewport()
    {
        var size = Viewport.Size;
        if (size.Width <= 0 || size.Height <= 0)
            return;

        var width = size.Width;
        var height = size.Height;
        var offset = Math.Clamp(scrollOffsets.GetValueOrDefault(activeWorkspace), 0, maxScroll);
        scrollOffsets[activeWorkspace] = offset;
        Viewport = new Rectangle(0, maxScroll - offset, width, height);
    }

    int[] CalculatePanelHeights(int width, int height)
    {
        if (panelLayouts.Count == 0)
            return [];
        if (fullBleed)
        {
            var layout = panelLayouts[0];
            return layout.Realized.FillsViewportHeight
                ? [height]
                :
                [
                    Math.Max(
                        height,
                        PreferredViewHeight(
                            layout.Realized.View,
                            layout.Realized.DeclaredHeight,
                            height,
                            width))
                ];
        }

        var result = panelLayouts
            .Select(layout => Math.Max(
                3,
                PreferredViewHeight(
                    layout.Realized.View,
                    layout.Realized.DeclaredHeight,
                    Math.Max(1, height / panelLayouts.Count - 2),
                    Math.Max(1, width - 2)) + 2))
            .ToArray();
        var remaining = height - result.Sum();
        if (remaining > 0)
            result[^1] += remaining;
        return result;
    }

    static int PreferredViewHeight(View view, int declaredHeight, int fallback, int width)
    {
        var text = view.Text?.ToString();
        var textHeight = string.IsNullOrEmpty(text) ? 0 : CountWrappedLines(text, width);
        var subViewHeight = view.SubViews.Count == 0 ? 0 : view.GetHeightRequiredForSubViews();
        return Math.Max(fallback, Math.Max(declaredHeight, Math.Max(textHeight, subViewHeight)));
    }

    static int CountWrappedLines(string text, int width)
    {
        width = Math.Max(1, width);
        var count = 0;
        foreach (var logicalLine in text.ReplaceLineEndings("\n").Split('\n'))
        {
            count++;
            var hasContent = false;
            var used = 0;
            foreach (var rune in logicalLine.EnumerateRunes())
            {
                var columns = Math.Max(0, rune.GetColumns());
                if (hasContent && used + columns > width)
                {
                    count++;
                    used = 0;
                }
                hasContent = true;
                used += columns;
            }
        }
        return count;
    }

    static bool EnableFocusPath(View view)
    {
        var hasFocusableView = view.CanFocus;
        foreach (var child in view.SubViews)
            hasFocusableView |= EnableFocusPath(child);
        if (!view.CanFocus && hasFocusableView && view.SubViews.Count > 0)
        {
            view.CanFocus = true;
            view.TabStop = TabBehavior.TabGroup;
        }
        return hasFocusableView;
    }

    bool Contains(View view)
    {
        for (View? current = view; current is not null; current = current.SuperView)
        {
            if (ReferenceEquals(current, this))
                return true;
        }
        return false;
    }

    void MarkReleased(View view)
        => releasedViews.GetValue(view, static _ => ReleasedViewMarker);

    static void Capture(List<Exception> errors, Action action)
    {
        try
        {
            action();
        }
        catch (Exception exception)
        {
            errors.Add(exception);
        }
    }

    static void ReleaseView(View view, List<Exception> errors)
    {
        Capture(errors, () => view.SuperView?.Remove(view));
        Capture(errors, view.Dispose);
    }

    static void ThrowCleanupErrors(List<Exception> errors)
    {
        if (errors.Count == 1)
            throw errors[0];
        if (errors.Count > 1)
            throw new AggregateException(errors);
    }

    sealed record RealizedPanel(
        WorkspaceContent Content,
        View View,
        int DeclaredHeight,
        bool FillsViewportHeight);

    sealed record PanelLayout(
        RealizedPanel Realized,
        FrameView? Frame);
}
