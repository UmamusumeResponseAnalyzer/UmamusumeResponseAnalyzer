using Terminal.Gui.Input;

namespace UmamusumeResponseAnalyzer.TerminalGui;

internal interface IUiInputSink
{
    int PopupVisibleLineCount => 1;
    Task<bool> TryHandleWorkspaceCommandAsync(Command command);
    void ShowPopup(HotkeyPopup popup);
    void HidePopup();
}

internal sealed record HotkeyPopup(
    IReadOnlyList<HotkeyPopupLine> Lines,
    int ScrollOffset = 0,
    DateTimeOffset? ExpiresAt = null,
    HotkeyPopupSelection? Selection = null,
    IReadOnlyList<UiShortcut>? Shortcuts = null);

internal sealed record HotkeyPopupSelection(
    IReadOnlyList<int> LineIndexes,
    int SelectedIndex,
    Func<int, Task> ConfirmAsync)
{
    public int BoundedSelectedIndex => LineIndexes.Count == 0
        ? -1
        : Math.Clamp(SelectedIndex, 0, LineIndexes.Count - 1);

    public int SelectedLineIndex => BoundedSelectedIndex < 0
        ? -1
        : LineIndexes[BoundedSelectedIndex];

    public HotkeyPopupSelection Normalize()
    {
        var selectedIndex = BoundedSelectedIndex;
        return selectedIndex == SelectedIndex ? this : this with { SelectedIndex = selectedIndex };
    }
}

internal sealed record HotkeyPopupLine(string Text);
