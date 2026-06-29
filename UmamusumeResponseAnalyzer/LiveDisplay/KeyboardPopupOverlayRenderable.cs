using Spectre.Console;
using Spectre.Console.Rendering;
using UmamusumeResponseAnalyzer;

namespace UmamusumeResponseAnalyzer.LiveDisplay
{
    sealed class KeyboardPopupOverlayRenderable(
        IRenderable content,
        KeyboardPopup popup,
        int width,
        int height,
        int bottomInset,
        DateTimeOffset? now = null) : PopupOverlayRenderable(
            content,
            options => BuildLines(popup, width, EffectiveHeight(options, height), bottomInset, now ?? DateTimeOffset.Now, options),
            Math.Max(0, width))
    {
        protected override bool TryGetPlacement(int maxWidth, int? maxHeight, int overlayHeight, out OverlayPlacement placement)
        {
            var availableHeight = maxHeight ?? height;
            if (availableHeight <= bottomInset || overlayHeight <= 0)
            {
                placement = default;
                return false;
            }

            placement = new OverlayPlacement(0, availableHeight - bottomInset - overlayHeight);
            return true;
        }

        static int EffectiveHeight(RenderOptions options, int fallbackHeight) => options.Height ?? fallbackHeight;

        static List<IReadOnlyList<Segment>> BuildLines(
            KeyboardPopup popup,
            int width,
            int height,
            int bottomInset,
            DateTimeOffset now,
            RenderOptions options)
        {
            if (width < 12 || popup.Lines.Count == 0)
                return [];

            var maxVisible = Math.Max(1, height - bottomInset - 2);
            var visibleCount = Math.Min(popup.Lines.Count, maxVisible);
            var maxOffset = Math.Max(0, popup.Lines.Count - visibleCount);
            var scrollOffset = Math.Clamp(popup.ScrollOffset, 0, maxOffset);
            var scrollable = scrollOffset > 0 || scrollOffset + visibleCount < popup.Lines.Count;
            var countdown = PopupCountdown.Format(popup.ExpiresAt, now);
            var lines = new List<IReadOnlyList<Segment>>(visibleCount + 2)
            {
                PlainLine(BuildTopBorder(width, scrollOffset, visibleCount, popup.Lines.Count, scrollable))
            };

            for (var i = scrollOffset; i < scrollOffset + visibleCount; i++)
                lines.Add(BuildContentLine(popup.Lines[i], width, options));

            lines.Add(PlainLine(PopupFrame.BottomWithRightLabel(width, countdown)));
            return lines;
        }

        static string BuildTopBorder(int width, int scrollOffset, int visibleCount, int totalCount, bool scrollable)
        {
            if (!scrollable)
                return PopupFrame.Top(width);

            var label = $" ↑ {scrollOffset + 1}-{scrollOffset + visibleCount}/{totalCount} ↓ ";
            return PopupFrame.TopWithCenteredLabel(width, label);
        }

        static IReadOnlyList<Segment> BuildContentLine(KeyboardPopupLine line, int width, RenderOptions options)
        {
            var contentWidth = Math.Max(0, width - 4);
            var fittedContent = line.IsMarkup
                ? FitSegmentsToCellWidth(BuildMarkupContent(line.Text, contentWidth, options), contentWidth).ToArray()
                : [BuildTextSegment(FitToCellWidth(line.Text, contentWidth), line.Color)];
            return
            [
                new Segment("│ "),
                ..fittedContent,
                new Segment(" │")
            ];
        }

        static IReadOnlyList<Segment> BuildMarkupContent(string markup, int width, RenderOptions options)
        {
            if (width <= 0)
                return [];

            try
            {
                var segments = ((IRenderable)new Markup(markup)).Render(options, width);
                var lines = Segment.SplitLines(segments, width, 1);
                return lines.Count > 0 ? lines[0] : [];
            }
            catch
            {
                return [new Segment(TrimToCellWidth(markup, width))];
            }
        }

        static Segment BuildTextSegment(string text, ConsoleColor color)
        {
            return TryMapColor(color, out var spectreColor)
                ? new Segment(text, new Style(foreground: spectreColor))
                : new Segment(text);
        }

        static bool TryMapColor(ConsoleColor color, out Color spectreColor)
        {
            spectreColor = color switch
            {
                ConsoleColor.Red or ConsoleColor.DarkRed => Color.Red,
                ConsoleColor.Green or ConsoleColor.DarkGreen => Color.Green,
                ConsoleColor.Yellow or ConsoleColor.DarkYellow => Color.Yellow,
                ConsoleColor.Blue or ConsoleColor.DarkBlue => Color.Blue,
                ConsoleColor.Cyan or ConsoleColor.DarkCyan => Color.Aqua,
                ConsoleColor.Magenta or ConsoleColor.DarkMagenta => Color.Fuchsia,
                ConsoleColor.Gray or ConsoleColor.DarkGray => Color.Grey35,
                _ => default
            };
            return color is not (ConsoleColor.White or ConsoleColor.Black);
        }
    }
}
