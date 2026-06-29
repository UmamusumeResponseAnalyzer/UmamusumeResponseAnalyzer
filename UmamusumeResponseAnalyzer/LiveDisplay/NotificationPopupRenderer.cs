namespace UmamusumeResponseAnalyzer.LiveDisplay
{
    // 右上角 notification popup 的文本行构建 + 倒计时刷新节流。
    //
    // 无可变 state 所有权——notifications/keyboardPopup 由 UiHost 持有，每次渲染时以参数传入。
    // 仅 lastPopupCountdownSecond 是 renderer 自己的"上次刷新秒"追踪，避免每帧重画秒级倒计时。
    internal sealed class NotificationPopupRenderer
    {
        const int MaxPopupNotifications = 4;

        long lastPopupCountdownSecond = -1;

        public static int GetPopupWidth(int windowWidth)
        {
            if (windowWidth < 70)
                return 0;

            return Math.Clamp(windowWidth / 3, 34, 46);
        }

        // 是否需要因倒计时秒数变化而触发一次重绘。notifications/keyboardPopup 为只读参数。
        public bool ShouldRefreshPopupCountdown(
            IReadOnlyList<LiveDisplayNotification> notifications,
            KeyboardPopup? keyboardPopup,
            DateTimeOffset now)
        {
            var hasKeyboardCountdown = keyboardPopup?.ExpiresAt > now;
            if (notifications.Count == 0 && !hasKeyboardCountdown)
            {
                lastPopupCountdownSecond = -1;
                return false;
            }

            var second = now.ToUnixTimeSeconds();
            if (second == lastPopupCountdownSecond)
                return false;

            lastPopupCountdownSecond = second;
            return true;
        }

        // 构建 notification popup 的所有文本行。labelResolver 把 workspace 转成显示标签。
        public List<string> BuildLines(
            IReadOnlyList<LiveDisplayNotification> activeNotifications,
            int popupWidth,
            int maxHeight,
            DateTimeOffset now,
            Func<LiveDisplayWorkspace, string> labelResolver)
        {
            const int CardHeight = 5;
            const int CompactHeight = 3;
            var lines = new List<string>();
            var displayed = 0;
            var displayLimit = Math.Min(activeNotifications.Count, MaxPopupNotifications);
            for (var i = 0; i < displayLimit; i++)
            {
                if (lines.Count + CardHeight > maxHeight)
                    break;

                lines.AddRange(BuildCardLines(activeNotifications[i], popupWidth, now, labelResolver));
                displayed++;
            }

            var remaining = activeNotifications.Count - displayed;
            if (remaining > 0 && lines.Count + CompactHeight <= maxHeight)
            {
                lines.AddRange(BuildCompactLines($"还有 {remaining} 条通知", popupWidth));
            }

            return lines;
        }

        static IEnumerable<string> BuildCardLines(
            LiveDisplayNotification notification,
            int popupWidth,
            DateTimeOffset now,
            Func<LiveDisplayWorkspace, string> labelResolver)
        {
            yield return PopupFrame.Top(popupWidth);
            yield return BuildContentLine($"{SeverityText(notification.Severity)} {notification.PluginId}", popupWidth);
            yield return BuildContentLine(notification.Text, popupWidth);
            yield return BuildContentLine(notification.Workspace is null ? "全局通知" : labelResolver(notification.Workspace), popupWidth);
            yield return PopupFrame.BottomWithRightLabel(popupWidth, PopupCountdown.Format(notification.ExpiresAt, now));
        }

        static IEnumerable<string> BuildCompactLines(string text, int popupWidth)
        {
            yield return PopupFrame.Top(popupWidth);
            yield return BuildContentLine(text, popupWidth);
            yield return PopupFrame.Bottom(popupWidth);
        }

        static string BuildContentLine(string text, int popupWidth)
        {
            return "│ " + CellText.FitToCellWidth(text, popupWidth - 4) + " │";
        }

        static string SeverityText(LiveDisplaySeverity severity) => severity switch
        {
            LiveDisplaySeverity.Trace => "TRACE",
            LiveDisplaySeverity.Info => "INFO ",
            LiveDisplaySeverity.Success => "OK   ",
            LiveDisplaySeverity.Warning => "WARN ",
            LiveDisplaySeverity.Error => "ERR  ",
            _ => "INFO "
        };
    }
}
