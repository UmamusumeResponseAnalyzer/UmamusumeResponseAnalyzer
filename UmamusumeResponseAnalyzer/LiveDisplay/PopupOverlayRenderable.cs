using Spectre.Console;
using Spectre.Console.Rendering;

namespace UmamusumeResponseAnalyzer.LiveDisplay
{
    abstract class PopupOverlayRenderable : IRenderable
    {
        readonly IRenderable content;
        readonly Func<RenderOptions, IReadOnlyList<IReadOnlyList<Segment>>> buildOverlayLines;
        readonly int overlayWidth;

        protected PopupOverlayRenderable(
            IRenderable content,
            IReadOnlyList<IReadOnlyList<Segment>> overlayLines,
            int overlayWidth) : this(content, _ => overlayLines, overlayWidth)
        {
        }

        protected PopupOverlayRenderable(
            IRenderable content,
            Func<RenderOptions, IReadOnlyList<IReadOnlyList<Segment>>> buildOverlayLines,
            int overlayWidth)
        {
            this.content = content;
            this.buildOverlayLines = buildOverlayLines;
            this.overlayWidth = overlayWidth;
        }

        protected readonly record struct OverlayPlacement(int Left, int Top);
        protected int OverlayWidth => overlayWidth;

        public Measurement Measure(RenderOptions options, int maxWidth) => content.Measure(options, maxWidth);

        public IEnumerable<Segment> Render(RenderOptions options, int maxWidth)
        {
            var overlayLines = buildOverlayLines(options);
            if (overlayLines.Count == 0 ||
                overlayWidth <= 0 ||
                !TryGetPlacement(maxWidth, options.Height, overlayLines.Count, out var placement) ||
                placement.Left < 0 ||
                placement.Top < 0 ||
                placement.Left + overlayWidth > maxWidth)
            {
                foreach (var segment in content.Render(options, maxWidth))
                    yield return segment;
                yield break;
            }

            var overlayBottom = placement.Top + overlayLines.Count;
            var overlayRight = placement.Left + overlayWidth;
            var baseLines = Segment.SplitLines(content.Render(options, maxWidth), maxWidth, options.Height);
            var lineCount = Math.Max(baseLines.Count, overlayBottom);
            if (options.Height.HasValue)
                lineCount = Math.Min(lineCount, options.Height.Value);

            for (var lineIndex = 0; lineIndex < lineCount; lineIndex++)
            {
                IEnumerable<Segment> line = lineIndex < baseLines.Count
                    ? baseLines[lineIndex]
                    : [];

                if (lineIndex >= placement.Top && lineIndex < overlayBottom)
                {
                    var popupLine = overlayLines[lineIndex - placement.Top];
                    var left = CellText.SliceSegments(line, 0, placement.Left).ToArray();
                    foreach (var segment in left)
                        yield return segment;

                    var leftWidth = Segment.CellCount(left);
                    if (leftWidth < placement.Left)
                        yield return Segment.Padding(placement.Left - leftWidth);

                    foreach (var segment in popupLine)
                        yield return segment;

                    var right = CellText.SliceSegments(line, overlayRight, maxWidth - overlayRight).ToArray();
                    foreach (var segment in right)
                        yield return segment;
                }
                else
                {
                    foreach (var segment in line)
                        yield return segment;
                }

                if (lineIndex < lineCount - 1)
                    yield return Segment.LineBreak;
            }
        }

        protected abstract bool TryGetPlacement(int maxWidth, int? maxHeight, int overlayHeight, out OverlayPlacement placement);

        protected static IReadOnlyList<IReadOnlyList<Segment>> PlainLines(IEnumerable<string> lines)
        {
            return lines.Select(PlainLine).ToArray();
        }

        protected static IReadOnlyList<Segment> PlainLine(string line)
        {
            return [new Segment(line)];
        }

        protected static string FitToCellWidth(string value, int width)
        {
            return CellText.FitToCellWidth(value, width);
        }

        protected static string TrimToCellWidth(string value, int maxWidth)
        {
            return CellText.TrimToCellWidth(value, maxWidth);
        }

        protected static IEnumerable<Segment> FitSegmentsToCellWidth(IEnumerable<Segment> segments, int width)
        {
            if (width <= 0)
                yield break;

            var trimmed = CellText.SliceSegments(segments, 0, width).ToArray();
            foreach (var segment in trimmed)
                yield return segment;

            var cellWidth = Segment.CellCount(trimmed);
            if (cellWidth < width)
                yield return Segment.Padding(width - cellWidth);
        }

    }
}
