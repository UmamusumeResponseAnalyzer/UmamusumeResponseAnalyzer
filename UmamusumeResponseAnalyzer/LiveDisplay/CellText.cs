using System.Globalization;
using System.Text;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace UmamusumeResponseAnalyzer.LiveDisplay
{
    static class CellText
    {
        public static string FitToCellWidth(string value, int width)
        {
            if (width <= 0)
                return string.Empty;

            if (IsAscii(value))
                return FitAsciiToCellWidth(value, width);

            var trimmed = TrimToCellWidth(value, width);
            var pad = Math.Max(0, width - trimmed.GetCellWidth());
            return trimmed + new string(' ', pad);
        }

        public static string TrimToCellWidth(string value, int maxWidth)
        {
            if (maxWidth <= 0)
                return string.Empty;

            if (IsAscii(value))
                return TrimAsciiToCellWidth(value, maxWidth);

            if (value.GetCellWidth() <= maxWidth)
                return value;

            if (maxWidth <= 3)
                return new string('.', maxWidth);

            var builder = new StringBuilder();
            var width = 0;
            foreach (var element in EnumerateTextElements(value))
            {
                if (width + element.CellWidth > maxWidth - 3)
                    break;

                builder.Append(value.AsSpan(element.Start, element.Length));
                width += element.CellWidth;
            }

            builder.Append("...");
            return builder.ToString();
        }

        internal static IEnumerable<Segment> SliceSegments(IEnumerable<Segment> segments, int start, int width)
        {
            if (width <= 0)
                yield break;

            var end = start + width;
            var offset = 0;
            foreach (var segment in segments)
            {
                if (segment.IsLineBreak || segment.IsControlCode)
                    continue;

                if (IsAscii(segment.Text))
                {
                    var segmentStart = offset;
                    var segmentEnd = offset + segment.Text.Length;
                    if (segmentEnd <= start)
                    {
                        offset = segmentEnd;
                        continue;
                    }

                    if (segmentStart >= end)
                        yield break;

                    var localStart = Math.Max(start, segmentStart) - segmentStart;
                    var localEnd = Math.Min(end, segmentEnd) - segmentStart;
                    var length = localEnd - localStart;
                    if (length == segment.Text.Length)
                        yield return segment;
                    else if (length > 0)
                        yield return new Segment(segment.Text.Substring(localStart, length), segment.Style);

                    offset = segmentEnd;
                    if (offset >= end)
                        yield break;
                    continue;
                }

                var segmentWidth = segment.Text.GetCellWidth();
                if (offset + segmentWidth <= start)
                {
                    offset += segmentWidth;
                    continue;
                }

                if (offset >= start && offset + segmentWidth <= end)
                {
                    yield return segment;
                    offset += segmentWidth;
                    if (offset >= end)
                        yield break;
                    continue;
                }

                var builder = new StringBuilder();
                foreach (var element in EnumerateTextElements(segment.Text))
                {
                    var nextOffset = offset + element.CellWidth;
                    if (nextOffset > start && offset < end)
                    {
                        if (offset >= start && nextOffset <= end)
                            builder.Append(segment.Text.AsSpan(element.Start, element.Length));
                        else
                            builder.Append(' ', Math.Min(nextOffset, end) - Math.Max(offset, start));
                    }

                    offset = nextOffset;
                    if (offset >= end)
                        break;
                }

                if (builder.Length > 0)
                    yield return new Segment(builder.ToString(), segment.Style);

                if (offset >= end)
                    yield break;
            }
        }

        static IEnumerable<TextElement> EnumerateTextElements(string value)
        {
            var indexes = StringInfo.ParseCombiningCharacters(value);
            for (var i = 0; i < indexes.Length; i++)
            {
                var start = indexes[i];
                var end = i + 1 < indexes.Length ? indexes[i + 1] : value.Length;
                var length = end - start;
                var text = value.Substring(start, length);
                yield return new TextElement(start, length, text.GetCellWidth());
            }
        }

        static bool IsAscii(string value)
        {
            foreach (var c in value)
            {
                if (c > 0x7F)
                    return false;
            }

            return true;
        }

        static string FitAsciiToCellWidth(string value, int width)
        {
            var trimmed = TrimAsciiToCellWidth(value, width);
            return trimmed.Length >= width
                ? trimmed
                : trimmed + new string(' ', width - trimmed.Length);
        }

        static string TrimAsciiToCellWidth(string value, int maxWidth)
        {
            if (value.Length <= maxWidth)
                return value;

            if (maxWidth <= 3)
                return new string('.', maxWidth);

            return value[..(maxWidth - 3)] + "...";
        }

        readonly record struct TextElement(int Start, int Length, int CellWidth);
    }
}
