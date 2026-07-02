namespace UmamusumeResponseAnalyzer.LiveDisplay
{
    // UiHost 的 UI 事件联合：通过 SingleReader channel 投递，渲染循环 DrainEvents 后用
    // pattern match 派发到对应状态变更。sealed record 子类是不可变载荷。
    internal abstract record UiEvent
    {
        public sealed record RegisterWorkspace(LiveDisplayWorkspace Workspace) : UiEvent;
        public sealed record SetWorkspaceShortcut(LiveDisplayWorkspace Workspace, string ShortcutText) : UiEvent;
        public sealed record SetPanel(LiveDisplayPanel Panel) : UiEvent;
        public sealed record Log(LiveDisplayLogLine Line) : UiEvent;
        public sealed record Notify(LiveDisplayNotification Notification) : UiEvent;
        public sealed record SwitchWorkspace(LiveDisplayWorkspace Workspace) : UiEvent;
        public sealed record ShowPopup(KeyboardPopup Popup) : UiEvent;
        public sealed record HidePopup : UiEvent;
        public sealed record ShowCommandInput(KeyboardCommandInput Input) : UiEvent;
        public sealed record HideCommandInput : UiEvent;
        public sealed record Shutdown : UiEvent;
    }
}
