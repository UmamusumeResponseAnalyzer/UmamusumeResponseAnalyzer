using i18n = UmamusumeResponseAnalyzer.Localization.TerminalGui;
using System.Collections.Immutable;
using System.Drawing;
using Terminal.Gui.Drawing;
using Terminal.Gui.Text;
using Terminal.Gui.ViewBase;
using Attribute = Terminal.Gui.Drawing.Attribute;

namespace UmamusumeResponseAnalyzer.TerminalGui;

/// <summary>
/// Notification cards stacked at the top right. A card is a box in the rule tone on the raised
/// surface; its top border carries the severity, the source and the remaining seconds, and the body
/// wraps inside. Notifications that do not fit collapse into one "N more" line.
/// </summary>
internal sealed class NotificationOverlayView : View
{
    const int MaxVisibleNotifications = 4;
    const int MaxBodyLines = 4;
    const int OverflowHeight = 1;

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
        for (var y = 0; y < lines.Length; y++)
        {
            var x = lines[y].Indent;
            foreach (var span in lines[y].Spans)
            {
                SetAttribute(span.Attribute);
                AddStr(x, y, span.Text);
                x += span.Text.GetColumns();
            }
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
            result.Add(Overflow(string.Format(i18n.Notification_Remaining, remaining), width));
        return result.ToImmutable();
    }

    static ImmutableArray<OverlayLine> BuildCard(
        NotificationOverlayItem notification,
        int width,
        DateTimeOffset now)
    {
        var inner = width - 4;
        var body = CellText.Wrap(notification.Text, inner);
        if (body.Count > MaxBodyLines)
        {
            body = body.GetRange(0, MaxBodyLines);
            body[^1] = CellText.Shorten(body[^1] + CellText.Ellipsis, inner);
        }

        var result = ImmutableArray.CreateBuilder<OverlayLine>(body.Count + 2);
        result.Add(Top(notification, width, Countdown(notification.ExpiresAt, now)));
        foreach (var line in body)
            result.Add(new(0, [Rule("│ "), Body(CellText.Fit(line, inner)), Rule(" │")]));
        result.Add(new(0, [Rule($"└{new string('─', width - 2)}┘")]));
        return result.ToImmutable();
    }

    // ┌─ ● 错误 Source ───────── 9秒 ─┐
    static OverlayLine Top(NotificationOverlayItem notification, int width, string countdown)
    {
        const string Head = "┌─ ";
        const string Tail = " ─┐";
        var severity = $"● {SeverityText(notification.Severity)}";
        // Head, severity, the space before the rule, and Tail; the rule keeps at least one dash.
        var fixedColumns = Head.Length + severity.GetColumns() + 1 + Tail.Length;

        var countdownColumns = countdown.GetColumns() + 1;
        if (fixedColumns + 1 + countdownColumns > width)
        {
            countdown = string.Empty;
            countdownColumns = 0;
        }
        var sourceRoom = width - fixedColumns - countdownColumns - 2;
        var source = notification.Source is { Length: > 0 } name && sourceRoom > 0
            ? CellText.Shorten(name, sourceRoom)
            : string.Empty;
        var sourceColumns = source.Length == 0 ? 0 : source.GetColumns() + 1;
        var dashes = Math.Max(1, width - fixedColumns - countdownColumns - sourceColumns);

        var severityAttribute = new Attribute(
            UraTheme.SeverityColor(notification.Severity),
            UraTheme.Surface,
            TextStyle.Bold);
        return new(0,
        [
            Rule(Head),
            new(severity, severityAttribute),
            Muted(source.Length == 0 ? string.Empty : $" {source}"),
            Rule($" {new string('─', dashes)}"),
            Muted(countdown.Length == 0 ? string.Empty : $" {countdown}"),
            Rule(Tail)
        ]);
    }

    static OverlayLine Overflow(string text, int width)
    {
        var label = $" {CellText.Shorten(text, width - 2)} ";
        return new(width - label.GetColumns(), [Muted(label)]);
    }

    static Span Rule(string text) => new(text, new Attribute(UraTheme.Rule, UraTheme.Surface));

    static Span Body(string text) => new(text, new Attribute(UraTheme.Ink, UraTheme.Surface));

    static Span Muted(string text) => new(text, new Attribute(UraTheme.Muted, UraTheme.Surface));

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

    readonly record struct Span(string Text, Attribute Attribute);

    readonly record struct OverlayLine(int Indent, ImmutableArray<Span> Spans);
}

internal readonly record struct NotificationOverlayItem(
    string Text,
    UiSeverity Severity,
    DateTimeOffset ExpiresAt,
    string? Source = null);

internal sealed record NotificationOverlaySnapshot(
    ImmutableArray<NotificationOverlayItem> Notifications,
    DateTimeOffset Now,
    Rectangle AvailableBounds);
