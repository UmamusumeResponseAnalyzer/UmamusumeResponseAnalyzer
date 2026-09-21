using i18n = UmamusumeResponseAnalyzer.Localization.TerminalGui;
using System.Collections.Immutable;
using System.Drawing;
using Terminal.Gui.Drawing;
using Terminal.Gui.Text;
using Terminal.Gui.ViewBase;

namespace UmamusumeResponseAnalyzer.TerminalGui;

internal sealed class NotificationOverlayView : View
{
    const int MaxVisibleNotifications = 4;
    const int OverflowHeight = 3;

    ImmutableArray<OverlayLine> lines = [];

    internal NotificationOverlayView()
    {
        CanFocus = false;
        Enabled = false;
        Visible = false;
        ViewportSettings =
            ViewportSettingsFlags.Transparent |
            ViewportSettingsFlags.TransparentMouse;
    }

    internal void UpdateSnapshot(NotificationOverlaySnapshot snapshot)
    {
        var width = PopupWidth(snapshot.AvailableBounds.Width);
        var maxHeight = Math.Max(0, snapshot.AvailableBounds.Height - 1);
        var rendered = width == 0
            ? []
            : BuildLines(snapshot.Notifications, width, maxHeight, snapshot.Now);
        if (rendered.IsDefaultOrEmpty)
        {
            lines = [];
            Frame = Rectangle.Empty;
            Visible = false;
            return;
        }

        lines = rendered;
        Frame = new(
            snapshot.AvailableBounds.Right - width - 1,
            snapshot.AvailableBounds.Top + 1,
            width,
            lines.Length);
        Visible = true;
        SetNeedsDraw();
    }

    protected override bool OnDrawingContent(DrawContext? context)
    {
        var normal = GetAttributeForRole(VisualRole.Normal);
        for (var y = 0; y < lines.Length; y++)
        {
            SetAttribute(lines[y].Severity is { } severity
                ? SeverityAttribute(severity)
                : normal);
            AddStr(0, y, lines[y].Text);
        }
        context?.AddDrawnRectangle(ViewportToScreen());
        return true;
    }

    static int PopupWidth(int availableWidth)
        => availableWidth < 70 ? 0 : Math.Clamp(availableWidth / 3, 34, 46);

    static ImmutableArray<OverlayLine> BuildLines(
        ImmutableArray<NotificationOverlayItem> notifications,
        int width,
        int maxHeight,
        DateTimeOffset now)
    {
        if (notifications.IsDefaultOrEmpty || maxHeight < OverflowHeight)
            return [];

        var cards = notifications
            .Take(MaxVisibleNotifications)
            .Select(notification => BuildCard(notification, width, now))
            .ToArray();
        var displayed = 0;
        var cardHeight = 0;
        while (displayed < cards.Length &&
               cardHeight + cards[displayed].Length <= maxHeight)
        {
            cardHeight += cards[displayed].Length;
            displayed++;
        }

        var remaining = notifications.Length - displayed;
        if (remaining > 0)
        {
            while (displayed > 0 && cardHeight + OverflowHeight > maxHeight)
            {
                displayed--;
                cardHeight -= cards[displayed].Length;
            }
            remaining = notifications.Length - displayed;
        }

        var result = ImmutableArray.CreateBuilder<OverlayLine>(
            cardHeight + (remaining > 0 ? OverflowHeight : 0));
        for (var i = 0; i < displayed; i++)
            result.AddRange(cards[i]);
        if (remaining > 0)
        {
            result.Add(new(Top(width)));
            result.Add(new(Content(string.Format(i18n.Notification_Remaining, remaining), width)));
            result.Add(new(Bottom(width)));
        }
        return result.ToImmutable();
    }

    static ImmutableArray<OverlayLine> BuildCard(
        NotificationOverlayItem notification,
        int width,
        DateTimeOffset now)
    {
        var result = ImmutableArray.CreateBuilder<OverlayLine>();
        result.Add(new(Top(width)));
        result.Add(new(Content(SeverityText(notification.Severity), width), notification.Severity));
        foreach (var line in notification.Text.ReplaceLineEndings("\n").Split('\n'))
            result.Add(new(Content(line, width)));
        result.Add(new(Bottom(width, Countdown(notification.ExpiresAt, now))));
        return result.ToImmutable();
    }

    static Terminal.Gui.Drawing.Attribute SeverityAttribute(UiSeverity severity)
        => new(
            severity switch
            {
                UiSeverity.Trace => StandardColor.Gray,
                UiSeverity.Info => StandardColor.Cyan,
                UiSeverity.Success => StandardColor.BrightGreen,
                UiSeverity.Warning => StandardColor.BrightYellow,
                UiSeverity.Error => StandardColor.BrightRed,
                _ => throw new ArgumentOutOfRangeException(nameof(severity), severity, null)
            },
            Terminal.Gui.Drawing.Color.None);

    static string SeverityText(UiSeverity severity)
        => severity switch
        {
            UiSeverity.Trace => i18n.Severity_Trace,
            UiSeverity.Info => i18n.Severity_Info,
            UiSeverity.Success => i18n.Severity_Success,
            UiSeverity.Warning => i18n.Severity_Warning,
            UiSeverity.Error => i18n.Severity_Error,
            _ => throw new ArgumentOutOfRangeException(nameof(severity), severity, null)
        };

    static string Countdown(DateTimeOffset expiresAt, DateTimeOffset now)
    {
        var remaining = expiresAt - now;
        var seconds = remaining <= TimeSpan.Zero
            ? 0
            : Math.Max(1, (long)Math.Ceiling(remaining.TotalSeconds));
        return string.Format(i18n.Notification_Countdown, seconds);
    }

    static string Top(int width) => FrameBorder('┌', '┐', width);

    static string Bottom(int width, string? label = null)
    {
        if (string.IsNullOrEmpty(label))
            return FrameBorder('└', '┘', width);

        label = $" {label} ";
        var dashes = width - 2 - label.GetColumns();
        return dashes < 2
            ? FrameBorder('└', '┘', width)
            : $"└{new string('─', dashes)}{label}┘";
    }

    static string FrameBorder(char left, char right, int width)
        => $"{left}{new string('─', width - 2)}{right}";

    static string Content(string text, int width)
        => $"│ {TextFormatter.ClipOrPad(text, width - 4)} │";

    readonly record struct OverlayLine(string Text, UiSeverity? Severity = null);
}

internal readonly record struct NotificationOverlayItem(
    string Text,
    UiSeverity Severity,
    DateTimeOffset ExpiresAt);

internal sealed record NotificationOverlaySnapshot(
    ImmutableArray<NotificationOverlayItem> Notifications,
    DateTimeOffset Now,
    Rectangle AvailableBounds);
