using Spectre.Console;
using Spectre.Console.Rendering;
using UmamusumeResponseAnalyzer;

namespace UmamusumeResponseAnalyzer.LiveDisplay
{
    sealed class KeyboardCommandInputOverlayRenderable(
        IRenderable content,
        KeyboardCommandInput input,
        int width,
        int height) : PopupOverlayRenderable(content, [BuildLine(input, width)], Math.Max(0, width))
    {
        protected override bool TryGetPlacement(int maxWidth, int? maxHeight, int overlayHeight, out OverlayPlacement placement)
        {
            var availableHeight = maxHeight ?? height;
            if (availableHeight <= 0 || overlayHeight <= 0)
            {
                placement = default;
                return false;
            }

            placement = new OverlayPlacement(0, availableHeight - 1);
            return true;
        }

        static IReadOnlyList<Segment> BuildLine(KeyboardCommandInput input, int width)
        {
            if (width <= 0)
                return [];

            const string prefix = "> ";
            const string hint = "ESC 取消";
            var prefixWidth = prefix.GetCellWidth();
            var hintWidth = hint.GetCellWidth();
            var showHint = width >= prefixWidth + hintWidth + 3;
            var inputWidth = Math.Max(0, width - prefixWidth - (showHint ? hintWidth + 1 : 0));
            var inputText = CellText.TrimToCellWidth(input.Text, inputWidth);
            var used = prefixWidth + inputText.GetCellWidth();
            var segments = new List<Segment>
            {
                new(prefix, new Style(foreground: Color.Green)),
                new(inputText, new Style(foreground: Color.White))
            };

            if (showHint)
            {
                var padding = Math.Max(1, width - used - hintWidth);
                segments.Add(Segment.Padding(padding));
                segments.Add(new Segment(hint, new Style(foreground: Color.Grey35)));
            }
            else if (used < width)
            {
                segments.Add(Segment.Padding(width - used));
            }

            return segments;
        }
    }
}
