using i18n = UmamusumeResponseAnalyzer.Localization.TerminalGui;
using System.Drawing;
using Terminal.Gui.Drawing;
using Terminal.Gui.Text;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;
using UmamusumeResponseAnalyzer.TerminalGui;
using Xunit;

namespace UmamusumeResponseAnalyzer.Tests;

[Collection("HotkeyManager")]
public sealed class NotificationOverlayViewTests
{
    [Theory]
    [InlineData("en-US", "INFO", "2s")]
    [InlineData("zh-CN", "信息", "2秒")]
    [InlineData("ja-JP", "情報", "2秒")]
    public async Task Snapshot_RendersFramebufferBoundsAttributesAndPhysicalLineOrder(
        string culture, string info, string countdown)
    {
        var originalCulture = i18n.Culture;
        i18n.Culture = System.Globalization.CultureInfo.GetCultureInfo(culture);
        try
        {
            using var terminal = new TerminalGuiTestApp(width: 90, height: 24);
            using var overlay = new NotificationOverlayView();
            var now = new DateTimeOffset(2026, 7, 31, 12, 0, 0, TimeSpan.Zero);
            var available = new Rectangle(5, 2, 80, 20);
            overlay.UpdateSnapshot(new(
                [
                    new("trace", UiSeverity.Trace, now.AddMilliseconds(1001)),
                    new("first-line\nsecond-line", UiSeverity.Info, now.AddSeconds(3)),
                    new("success", UiSeverity.Success, now.AddSeconds(4)),
                    new("warning", UiSeverity.Warning, now.AddSeconds(5))
                ],
                now,
                available));

            // Each card is its body plus the two border rows; severity and countdown ride the top border.
            Assert.Equal(new Rectangle(50, 3, 34, 13), overlay.Frame);
            Assert.Equal(1, available.Right - overlay.Frame.Right);
            Assert.True(overlay.Frame.Bottom <= available.Bottom);

            using var window = WindowWith(overlay);
            var run = await StartAsync(terminal, window);
            try
            {
                await terminal.WaitForScreenAsync("second-line");
                var screen = await terminal.CaptureScreenAsync();
                var rows = screen.ReplaceLineEndings("\n").Split('\n');
                var firstRow = Array.FindIndex(rows, row => row.Contains("first-line", StringComparison.Ordinal));
                var secondRow = Array.FindIndex(rows, row => row.Contains("second-line", StringComparison.Ordinal));
                var successRow = Array.FindIndex(rows, row => row.Contains(i18n.Severity_Success, StringComparison.Ordinal));
                Assert.Contains(i18n.Severity_Trace, screen, StringComparison.Ordinal);
                Assert.Contains(info, screen, StringComparison.Ordinal);
                Assert.Contains(i18n.Severity_Warning, screen, StringComparison.Ordinal);
                Assert.Contains(countdown, screen, StringComparison.Ordinal);
                Assert.Equal(firstRow + 1, secondRow);
                Assert.True(secondRow < successRow);

                Assert.Equal(
                    new Terminal.Gui.Drawing.Attribute(UraTheme.Rule, UraTheme.Surface),
                    await terminal.CaptureAttributeAsync(overlay.Frame.Location));
                // The severity dot follows "┌─ " on each card's top border.
                var severityAttributes = new[]
                {
                    await terminal.CaptureAttributeAsync(new(overlay.Frame.X + 3, overlay.Frame.Y)),
                    await terminal.CaptureAttributeAsync(new(overlay.Frame.X + 3, overlay.Frame.Y + 3)),
                    await terminal.CaptureAttributeAsync(new(overlay.Frame.X + 3, overlay.Frame.Y + 7)),
                    await terminal.CaptureAttributeAsync(new(overlay.Frame.X + 3, overlay.Frame.Y + 10))
                };
                Assert.Equal(
                    [UraTheme.Muted, UraTheme.Accent, UraTheme.Success, UraTheme.Warning],
                    severityAttributes.Select(attribute => attribute!.Value.Foreground));

                await terminal.InvokeAsync(() => overlay.UpdateSnapshot(new(
                    [new("error", UiSeverity.Error, now.AddSeconds(1))],
                    now,
                    available)));
                await terminal.WaitForScreenAsync(i18n.Severity_Error);
                Assert.Equal(
                    UraTheme.Error,
                    (await terminal.CaptureAttributeAsync(
                        new(overlay.Frame.X + 3, overlay.Frame.Y)))!.Value.Foreground);

                await terminal.InvokeAsync(() => overlay.UpdateSnapshot(new([], now, available)));
                await terminal.WaitForAsync(async () =>
                    !(await terminal.CaptureScreenAsync()).Contains(i18n.Severity_Error, StringComparison.Ordinal));
                Assert.False(overlay.Visible);
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

    [Fact]
    public async Task HeightPressure_UsesOverflowCardInsteadOfCroppingANotification()
    {
        using var terminal = new TerminalGuiTestApp(width: 80, height: 10);
        using var overlay = new NotificationOverlayView();
        var now = DateTimeOffset.UnixEpoch;
        overlay.UpdateSnapshot(new(
            [
                new("first", UiSeverity.Info, now.AddSeconds(10)),
                new("second", UiSeverity.Info, now.AddSeconds(10)),
                new("third", UiSeverity.Info, now.AddSeconds(10)),
                new("fourth", UiSeverity.Info, now.AddSeconds(10)),
                new("fifth", UiSeverity.Info, now.AddSeconds(10)),
                new("sixth", UiSeverity.Info, now.AddSeconds(10))
            ],
            now,
            new Rectangle(0, 0, 80, 10)));

        // Three 3-row cards would fill the 9 usable rows, so the third yields to the one-row overflow line.
        Assert.Equal(new Rectangle(45, 1, 34, 7), overlay.Frame);
        using var window = WindowWith(overlay);
        var run = await StartAsync(terminal, window);
        try
        {
            await terminal.WaitForScreenAsync(string.Format(i18n.Notification_Remaining, 4));
            var screen = await terminal.CaptureScreenAsync();
            Assert.Contains("first", screen, StringComparison.Ordinal);
            Assert.Contains("second", screen, StringComparison.Ordinal);
            Assert.DoesNotContain("third", screen, StringComparison.Ordinal);
            Assert.True(overlay.Frame.Bottom <= 10);
        }
        finally
        {
            await StopAsync(terminal, run);
        }
    }

    [Fact]
    public async Task CellWidth_WrapsBeforeWholeCjkOrEmojiRune()
    {
        using var terminal = new TerminalGuiTestApp(width: 70, height: 10);
        using var overlay = new NotificationOverlayView();
        var now = DateTimeOffset.UnixEpoch;
        var prefix = new string('a', 26);
        overlay.UpdateSnapshot(new(
            [new($"{prefix}马👨‍👩‍👧‍👦🙂tail", UiSeverity.Info, now.AddSeconds(1))],
            now,
            new Rectangle(0, 0, 70, 10)));

        using var window = WindowWith(overlay);
        var run = await StartAsync(terminal, window);
        try
        {
            await terminal.WaitForScreenAsync(prefix);
            Assert.Equal(
                $"│ {prefix}马👨‍👩‍👧‍👦 │",
                await CaptureRowAsync(terminal, overlay.Frame.X, overlay.Frame.Y + 1, overlay.Frame.Width));
            Assert.Equal(
                $"│ 🙂tail{new string(' ', 24)} │",
                await CaptureRowAsync(terminal, overlay.Frame.X, overlay.Frame.Y + 2, overlay.Frame.Width));
        }
        finally
        {
            await StopAsync(terminal, run);
        }
    }

    [Fact]
    public async Task CellWidth_WrapsWideRuneThatWouldCrossTheRightBorder()
    {
        using var terminal = new TerminalGuiTestApp(width: 70, height: 10);
        using var overlay = new NotificationOverlayView();
        var now = DateTimeOffset.UnixEpoch;
        var prefix = new string('a', 29);
        overlay.UpdateSnapshot(new(
            [new($"{prefix}马tail", UiSeverity.Info, now.AddSeconds(1))],
            now,
            new Rectangle(0, 0, 70, 10)));

        using var window = WindowWith(overlay);
        var run = await StartAsync(terminal, window);
        try
        {
            await terminal.WaitForScreenAsync(prefix);
            // The wide rune would straddle the right border, so the row pads and the rune wraps.
            Assert.Equal(
                $"│ {prefix}  │",
                await CaptureRowAsync(terminal, overlay.Frame.X, overlay.Frame.Y + 1, overlay.Frame.Width));
            Assert.Equal(
                $"│ 马tail{new string(' ', 24)} │",
                await CaptureRowAsync(terminal, overlay.Frame.X, overlay.Frame.Y + 2, overlay.Frame.Width));
        }
        finally
        {
            await StopAsync(terminal, run);
        }
    }

    [Fact]
    public async Task Card_BorderCarriesSeveritySourceAndCountdownAndCapsTheBody()
    {
        var originalCulture = i18n.Culture;
        i18n.Culture = System.Globalization.CultureInfo.GetCultureInfo("en-US");
        try
        {
            using var terminal = new TerminalGuiTestApp(width: 70, height: 12);
            using var overlay = new NotificationOverlayView();
            var now = DateTimeOffset.UnixEpoch;
            overlay.UpdateSnapshot(new(
                [new("one\ntwo\nthree\nfour\nfive", UiSeverity.Info, now.AddSeconds(1), "URA")],
                now,
                new Rectangle(0, 0, 70, 12)));

            Assert.Equal(new Rectangle(35, 1, 34, 6), overlay.Frame);
            using var window = WindowWith(overlay);
            var run = await StartAsync(terminal, window);
            try
            {
                await terminal.WaitForScreenAsync("URA");
                Assert.Equal(
                    $"┌─ ● INFO URA {new string('─', 14)} 1s ─┐",
                    await CaptureRowAsync(terminal, overlay.Frame.X, overlay.Frame.Y, overlay.Frame.Width));
                Assert.Equal(
                    $"│ four…{new string(' ', 25)} │",
                    await CaptureRowAsync(terminal, overlay.Frame.X, overlay.Frame.Y + 4, overlay.Frame.Width));
                Assert.Equal(
                    $"└{new string('─', 32)}┘",
                    await CaptureRowAsync(terminal, overlay.Frame.X, overlay.Frame.Y + 5, overlay.Frame.Width));
                Assert.DoesNotContain("five", await terminal.CaptureScreenAsync(), StringComparison.Ordinal);
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

    [Fact]
    public async Task NativeTransparency_PassesClicksToTheCoveredView()
    {
        using var terminal = new TerminalGuiTestApp(width: 80, height: 12);
        using var overlay = new NotificationOverlayView();
        var now = DateTimeOffset.UnixEpoch;
        overlay.UpdateSnapshot(new(
            [new("OverlayClick", UiSeverity.Info, now.AddSeconds(1))],
            now,
            new Rectangle(0, 0, 80, 12)));
        var hits = 0;
        var button = new Button
        {
            Text = "Underlay",
            X = 50,
            Y = 3,
            Width = 16
        };
        button.Accepting += (_, _) => hits++;

        Assert.False(overlay.CanFocus);
        Assert.False(overlay.Enabled);
        Assert.True(overlay.ViewportSettings.HasFlag(ViewportSettingsFlags.Transparent));
        Assert.True(overlay.ViewportSettings.HasFlag(ViewportSettingsFlags.TransparentMouse));

        using var window = WindowWith(button, overlay);
        var run = await StartAsync(terminal, window);
        try
        {
            await terminal.WaitForScreenAsync("OverlayClick");
            var frame = await terminal.InvokeAsync(button.FrameToScreen);
            await terminal.ClickAsync(new Point(frame.X + frame.Width / 2, frame.Y));
            await terminal.WaitForAsync(() => hits == 1);
        }
        finally
        {
            await StopAsync(terminal, run);
        }
    }

    [Fact]
    public void Bounds_HideNarrowSnapshotsAndRejectUnknownSeverity()
    {
        using var overlay = new NotificationOverlayView();
        var now = DateTimeOffset.UnixEpoch;
        overlay.UpdateSnapshot(new(
            [new("hidden", UiSeverity.Info, now.AddSeconds(1))],
            now,
            new Rectangle(0, 0, 69, 10)));
        Assert.False(overlay.Visible);
        Assert.Equal(Rectangle.Empty, overlay.Frame);

        Assert.Throws<ArgumentOutOfRangeException>(() => overlay.UpdateSnapshot(new(
            [new("invalid", (UiSeverity)int.MaxValue, now.AddSeconds(1))],
            now,
            new Rectangle(0, 0, 70, 10))));
    }

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
        await terminal.InvokeAsync(terminal.Application.RequestStop);
        await run.WaitAsync(TimeSpan.FromSeconds(5));
    }

    static Task<string> CaptureRowAsync(
        TerminalGuiTestApp terminal,
        int x,
        int y,
        int width)
        => terminal.InvokeAsync(() =>
        {
            var contents = terminal.Application.Driver!.Contents!;
            var graphemes = new List<string>();
            for (var column = x; column < x + width;)
            {
                var grapheme = contents[y, column].Grapheme.ToString();
                graphemes.Add(grapheme);
                column += Math.Max(1, grapheme.GetColumns());
            }
            return string.Concat(graphemes);
        });
}
