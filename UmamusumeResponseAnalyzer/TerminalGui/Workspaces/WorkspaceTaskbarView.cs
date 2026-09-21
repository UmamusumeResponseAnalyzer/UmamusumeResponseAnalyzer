using System.ComponentModel;
using System.Drawing;
using System.Text;
using Attribute = Terminal.Gui.Drawing.Attribute;
using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.Text;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace UmamusumeResponseAnalyzer.TerminalGui;

internal sealed class WorkspaceTaskbarView : View
{
    const int DragThreshold = 2;
    const string Ellipsis = "…";

    static readonly int EllipsisWidth = Ellipsis.GetColumns();
    static readonly Scheme PopupScheme = new(new Attribute(StandardColor.White, StandardColor.RaisinBlack));
    static readonly Scheme InactiveScheme = new()
    {
        Normal = new Attribute(StandardColor.White, StandardColor.RaisinBlack),
        Highlight = new Attribute(StandardColor.White, StandardColor.DarkSlateGray)
    };
    static readonly Scheme ActiveScheme = new()
    {
        Normal = new Attribute(StandardColor.Black, StandardColor.Cyan),
        Highlight = new Attribute(StandardColor.Black, StandardColor.Cyan)
    };

    readonly Func<bool> commandModeIsOpen;
    readonly Action<Workspace> switchWorkspace;
    readonly Action<IReadOnlyList<string>> saveTitleOrder;
    readonly View bottomEdgeTrigger;
    readonly View popup;
    readonly List<(Shortcut Item, Workspace Workspace)> items = [];
    readonly List<string> savedTitleOrder;

    int[] fullTitleWidths = [];
    int[] itemMargins = [];
    int[] titleBudgets = [];
    Workspace activeWorkspace;
    Workspace? hoveredWorkspace;
    Workspace? laidOutActiveWorkspace;
    Workspace? laidOutHoveredWorkspace;
    int laidOutViewportWidth;
    bool titleLayoutValid;
    bool bottomEdgeActivationSuppressed;
    Shortcut? pressedItem;
    int pressScreenX;
    int insertionIndex;
    bool dragging;
    Rectangle[] dragFrames = [];

    public WorkspaceTaskbarView(
        Workspace activeWorkspace,
        Func<bool> commandModeIsOpen,
        Action<Workspace> switchWorkspace,
        IReadOnlyList<string> savedTitleOrder,
        Action<IReadOnlyList<string>> saveTitleOrder)
    {
        this.activeWorkspace = activeWorkspace;
        this.commandModeIsOpen = commandModeIsOpen;
        this.switchWorkspace = switchWorkspace;
        this.saveTitleOrder = saveTitleOrder;
        this.savedTitleOrder = savedTitleOrder
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        Width = Dim.Fill();
        Height = Dim.Fill();
        CanFocus = false;
        TabStop = TabBehavior.NoStop;
        ViewportSettings = ViewportSettingsFlags.Transparent | ViewportSettingsFlags.TransparentMouse;

        bottomEdgeTrigger = new View
        {
            Y = Pos.AnchorEnd(),
            Width = Dim.Fill(),
            Height = 1,
            CanFocus = false,
            TabStop = TabBehavior.NoStop,
            ViewportSettings = ViewportSettingsFlags.Transparent
        };
        bottomEdgeTrigger.MouseBindings.Clear();
        popup = new View
        {
            X = Pos.Center(),
            Y = Pos.AnchorEnd(),
            Width = Dim.Auto(DimAutoStyle.Content, maximumContentDim: Dim.Fill(2)),
            Height = 3,
            BorderStyle = LineStyle.Single,
            CanFocus = false,
            TabStop = TabBehavior.NoStop,
            Visible = false
        };
        popup.SetScheme(PopupScheme);

        bottomEdgeTrigger.MouseEnter += BottomEdgeTriggerMouseEnter;
        bottomEdgeTrigger.MouseLeave += BottomEdgeTriggerMouseLeave;
        popup.MouseLeave += PopupMouseLeave;
        Add(popup);
    }

    internal View BottomEdgeTrigger => bottomEdgeTrigger;

    internal void Refresh(IReadOnlyList<Workspace> workspaces, Workspace activeWorkspace)
    {
        var current = OrderWorkspaces(workspaces);
        if (items.Count != current.Length ||
            items.Where((entry, index) => !ReferenceEquals(entry.Workspace, current[index])).Any())
        {
            CancelInteraction(relayout: false);
            RebuildItems(current);
        }

        this.activeWorkspace = activeWorkspace;
        foreach (var (item, workspace) in items)
            item.SetScheme(ReferenceEquals(workspace, activeWorkspace) ? ActiveScheme : InactiveScheme);

        if (items.Count == 0)
        {
            Hide();
            return;
        }
        else
            bottomEdgeTrigger.Visible = !commandModeIsOpen();

        if (pressedItem is null)
            ApplyTitleLayout();
    }

    internal void CommandModeVisibilityChanged() => Hide();

    internal void HandleMousePosition(Mouse mouse)
    {
        if (!mouse.Flags.HasFlag(MouseFlags.PositionReport))
            return;

        if (!bottomEdgeTrigger.FrameToScreen().Contains(mouse.ScreenPosition))
        {
            bottomEdgeActivationSuppressed = false;
            return;
        }

        if (!bottomEdgeActivationSuppressed)
            Show();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            CancelInteraction(relayout: false);
            bottomEdgeTrigger.MouseEnter -= BottomEdgeTriggerMouseEnter;
            bottomEdgeTrigger.MouseLeave -= BottomEdgeTriggerMouseLeave;
            popup.MouseLeave -= PopupMouseLeave;
            foreach (var (item, _) in items)
                DetachItem(item);
            items.Clear();
        }
        base.Dispose(disposing);
    }

    protected override void OnViewportChanged(DrawEventArgs e)
    {
        base.OnViewportChanged(e);
        CancelInteraction(relayout: false);
        RefreshTitleMeasurements();
        titleLayoutValid = false;
        ApplyTitleLayout();
    }

    void BottomEdgeTriggerMouseEnter(object? sender, CancelEventArgs e)
    {
        if (!bottomEdgeActivationSuppressed)
            Show();
    }

    void BottomEdgeTriggerMouseLeave(object? sender, EventArgs e)
    {
        if (App?.Mouse.LastMousePosition is { } currentPosition &&
            FrameToScreen().Contains(currentPosition))
        {
            bottomEdgeActivationSuppressed = false;
        }

        if (pressedItem is not null ||
            popup.Visible &&
            App?.Mouse.LastMousePosition is { } position &&
            popup.FrameToScreen().Contains(position))
        {
            return;
        }

        Hide();
    }

    void Show()
    {
        if (commandModeIsOpen() || items.Count == 0)
            return;

        if (popup.Visible)
        {
            ApplyTitleLayout();
            return;
        }

        popup.Visible = true;
        ApplyTitleLayout();
    }

    void PopupMouseLeave(object? sender, EventArgs e)
    {
        if (pressedItem is null)
            Hide();
    }

    void Hide()
    {
        CancelInteraction(relayout: false);
        var resetTitleLayout = hoveredWorkspace is not null;
        hoveredWorkspace = null;
        popup.Visible = false;
        bottomEdgeTrigger.Height = 1;
        bottomEdgeTrigger.Visible = items.Count > 0 && !commandModeIsOpen();
        if (resetTitleLayout)
            ApplyTitleLayout();
    }

    void RebuildItems(IReadOnlyList<Workspace> workspaces)
    {
        hoveredWorkspace = null;
        foreach (var (item, _) in items)
        {
            DetachItem(item);
            popup.Remove(item);
            item.Dispose();
        }
        items.Clear();

        Shortcut? previous = null;
        foreach (var workspace in workspaces)
        {
            var item = new Shortcut(Key.Empty, workspace.DisplayTitle, () => switchWorkspace(workspace))
            {
                X = previous is null ? 0 : Pos.Right(previous),
                Y = 0,
                Height = Dim.Fill(),
                CanFocus = false,
                TabStop = TabBehavior.NoStop,
                HotKeySpecifier = (Rune)'\xffff'
            };
            if (item.CommandView is not null)
                item.CommandView.HotKeySpecifier = (Rune)'\xffff';
            item.MouseEnter += ItemMouseEnter;
            item.MouseLeave += ItemMouseLeave;
            item.MouseEvent += ItemMouseEvent;
            if (item.CommandView is not null)
                item.CommandView.MouseEvent += ItemMouseEvent;
            popup.Add(item);
            items.Add((item, workspace));
            previous = item;
        }

        RefreshTitleMeasurements();
        titleLayoutValid = false;
    }

    Workspace[] OrderWorkspaces(IEnumerable<Workspace> workspaces)
    {
        var registered = workspaces.ToArray();
        var remaining = registered.ToDictionary(
            workspace => workspace.Title,
            StringComparer.OrdinalIgnoreCase);
        var ordered = new List<Workspace>(registered.Length);

        foreach (var title in savedTitleOrder)
        {
            if (!remaining.Remove(title, out var workspace))
                continue;

            ordered.Add(workspace);
        }

        foreach (var workspace in registered)
        {
            if (remaining.Remove(workspace.Title))
                ordered.Add(workspace);
        }

        return [.. ordered];
    }

    void ApplyTitleLayout()
    {
        if (pressedItem is not null || items.Count == 0)
            return;

        if (titleLayoutValid &&
            laidOutViewportWidth == Viewport.Width &&
            ReferenceEquals(laidOutActiveWorkspace, activeWorkspace) &&
            ReferenceEquals(laidOutHoveredWorkspace, hoveredWorkspace))
        {
            return;
        }

        var available = Math.Max(0, Viewport.Width - popup.GetAdornmentsThickness().Horizontal);
        var fullWidth = 0;
        for (var index = 0; index < items.Count; index++)
        {
            titleBudgets[index] = fullTitleWidths[index];
            fullWidth = checked(fullWidth + fullTitleWidths[index] + itemMargins[index]);
        }

        if (fullWidth > available)
        {
            var ordinaryCount = 0;
            var protectedWidth = 0;
            var ordinaryMargins = 0;
            var allocatedTitleWidth = 0;
            for (var index = 0; index < items.Count; index++)
            {
                if (ReferenceEquals(items[index].Workspace, activeWorkspace) ||
                    ReferenceEquals(items[index].Workspace, hoveredWorkspace))
                {
                    protectedWidth = checked(
                        protectedWidth + fullTitleWidths[index] + itemMargins[index]);
                    continue;
                }

                ordinaryCount++;
                ordinaryMargins = checked(ordinaryMargins + itemMargins[index]);
                titleBudgets[index] = fullTitleWidths[index] == 0 ? 0 : 1;
                allocatedTitleWidth = checked(allocatedTitleWidth + titleBudgets[index]);
            }

            if (ordinaryCount > 0)
            {
                var availableTitleWidth = Math.Max(
                    0,
                    available - protectedWidth - ordinaryMargins);
                var remaining = Math.Max(0, availableTitleWidth - allocatedTitleWidth);
                while (remaining > 0)
                {
                    var allocated = false;
                    for (var index = 0; index < items.Count; index++)
                    {
                        if (ReferenceEquals(items[index].Workspace, activeWorkspace) ||
                            ReferenceEquals(items[index].Workspace, hoveredWorkspace) ||
                            titleBudgets[index] >= fullTitleWidths[index])
                        {
                            continue;
                        }

                        titleBudgets[index]++;
                        remaining--;
                        allocated = true;
                        if (remaining == 0)
                            break;
                    }

                    if (!allocated)
                        break;
                }
            }
        }

        for (var index = 0; index < items.Count; index++)
        {
            var title = items[index].Workspace.DisplayTitle;
            var visibleTitle = titleBudgets[index] >= fullTitleWidths[index]
                ? title
                : ShortenTitle(title, titleBudgets[index]);
            if (items[index].Item.Title == visibleTitle)
                continue;

            items[index].Item.Title = visibleTitle;
        }

        laidOutViewportWidth = Viewport.Width;
        laidOutActiveWorkspace = activeWorkspace;
        laidOutHoveredWorkspace = hoveredWorkspace;
        titleLayoutValid = true;
    }

    void RefreshTitleMeasurements()
    {
        if (fullTitleWidths.Length != items.Count)
        {
            fullTitleWidths = new int[items.Count];
            itemMargins = new int[items.Count];
            titleBudgets = new int[items.Count];
        }

        for (var index = 0; index < items.Count; index++)
        {
            fullTitleWidths[index] = items[index].Workspace.DisplayTitle.GetColumns();
            itemMargins[index] =
                items[index].Item.CommandView?.Margin.Thickness.Horizontal ?? 0;
        }
    }

    static string ShortenTitle(string title, int width)
    {
        if (width <= EllipsisWidth)
            return Ellipsis;

        return TextFormatter.ClipOrPad(title, width - EllipsisWidth).TrimEnd() + Ellipsis;
    }

    void ItemMouseEnter(object? sender, CancelEventArgs e)
    {
        if (pressedItem is not null || sender is not Shortcut item)
            return;

        var index = IndexOf(item);
        if (index < 0)
            return;

        hoveredWorkspace = items[index].Workspace;
        ApplyTitleLayout();
    }

    void ItemMouseLeave(object? sender, EventArgs e)
    {
        if (pressedItem is not null || sender is not Shortcut item)
            return;

        var index = IndexOf(item);
        if (index < 0 || !ReferenceEquals(hoveredWorkspace, items[index].Workspace))
            return;

        var position = App?.Mouse.LastMousePosition;
        if (position is { } point)
        {
            for (var candidate = 0; candidate < items.Count; candidate++)
            {
                if (candidate != index &&
                    items[candidate].Item.FrameToScreen().Contains(point))
                {
                    return;
                }
            }
        }

        bottomEdgeActivationSuppressed = position is { } leavePosition &&
            bottomEdgeTrigger.FrameToScreen().Contains(leavePosition);
        Hide();
    }

    void ItemMouseEvent(object? sender, Mouse mouse)
    {
        var item = ItemFor(sender);
        if (item is null || commandModeIsOpen())
            return;

        if (pressedItem is null && mouse.Flags.HasFlag(MouseFlags.LeftButtonPressed))
        {
            pressedItem = item;
            pressScreenX = mouse.ScreenPosition.X;
            insertionIndex = IndexOf(item);
            dragFrames = items.Select(entry => entry.Item.FrameToScreen()).ToArray();
            mouse.Handled = true;
            return;
        }

        if (pressedItem is null)
            return;

        if (mouse.Flags.HasFlag(MouseFlags.PositionReport) &&
            mouse.Flags.HasFlag(MouseFlags.LeftButtonPressed))
        {
            if (!dragging &&
                Math.Abs(mouse.ScreenPosition.X - pressScreenX) >= DragThreshold)
            {
                dragging = true;
                App?.Mouse.GrabMouse(pressedItem);
            }

            if (dragging)
            {
                insertionIndex = CalculateInsertionIndex(mouse.ScreenPosition.X, pressedItem);
                mouse.Handled = true;
            }
            return;
        }

        if (!mouse.Flags.HasFlag(MouseFlags.LeftButtonReleased))
            return;

        if (!dragging)
        {
            ClearInteractionState();
            ApplyTitleLayout();
            return;
        }

        var source = pressedItem;
        var sourceIndex = IndexOf(source);
        var targetIndex = insertionIndex;
        mouse.Handled = true;
        CancelInteraction(relayout: false);

        if (sourceIndex == targetIndex || sourceIndex < 0)
        {
            ApplyTitleLayout();
            return;
        }

        var reordered = items.Select(entry => entry.Workspace).ToList();
        var moved = reordered[sourceIndex];
        reordered.RemoveAt(sourceIndex);
        reordered.Insert(Math.Clamp(targetIndex, 0, reordered.Count), moved);
        PersistOrder(reordered);
        RebuildItems(reordered);
        foreach (var (shortcut, workspace) in items)
            shortcut.SetScheme(ReferenceEquals(workspace, activeWorkspace) ? ActiveScheme : InactiveScheme);
        ApplyTitleLayout();
    }

    int CalculateInsertionIndex(int screenX, Shortcut source)
    {
        var index = 0;
        for (var frameIndex = 0; frameIndex < dragFrames.Length; frameIndex++)
        {
            if (ReferenceEquals(items[frameIndex].Item, source))
                continue;

            var frame = dragFrames[frameIndex];
            if (screenX >= frame.X + frame.Width / 2)
                index++;
        }

        return index;
    }

    void PersistOrder(IReadOnlyList<Workspace> reordered)
    {
        var titles = reordered.Select(workspace => workspace.Title).ToArray();
        var visible = titles.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var merged = new List<string>(Math.Max(savedTitleOrder.Count, titles.Length));
        var currentIndex = 0;

        foreach (var savedTitle in savedTitleOrder)
        {
            if (visible.Contains(savedTitle))
                merged.Add(titles[currentIndex++]);
            else
                merged.Add(savedTitle);
        }

        while (currentIndex < titles.Length)
            merged.Add(titles[currentIndex++]);

        savedTitleOrder.Clear();
        savedTitleOrder.AddRange(merged);
        saveTitleOrder([.. savedTitleOrder]);
    }

    void CancelInteraction(bool relayout = true)
    {
        var item = pressedItem;
        ClearInteractionState();
        if (item is not null && App?.Mouse.IsGrabbed(item) == true)
            App.Mouse.UngrabMouse();
        if (relayout)
            ApplyTitleLayout();
    }

    void ClearInteractionState()
    {
        pressedItem = null;
        dragging = false;
        insertionIndex = -1;
        dragFrames = [];
    }

    void DetachItem(Shortcut item)
    {
        item.MouseEnter -= ItemMouseEnter;
        item.MouseLeave -= ItemMouseLeave;
        item.MouseEvent -= ItemMouseEvent;
        if (item.CommandView is not null)
            item.CommandView.MouseEvent -= ItemMouseEvent;
    }

    Shortcut? ItemFor(object? sender)
    {
        if (sender is Shortcut shortcut)
            return shortcut;

        for (var index = 0; index < items.Count; index++)
        {
            if (ReferenceEquals(items[index].Item.CommandView, sender))
                return items[index].Item;
        }

        return null;
    }

    int IndexOf(Shortcut item)
        => items.FindIndex(entry => ReferenceEquals(entry.Item, item));
}
