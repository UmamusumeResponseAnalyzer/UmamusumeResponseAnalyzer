using System.Globalization;
using System.Text;
using Terminal.Gui.Text;

namespace UmamusumeResponseAnalyzer.TerminalGui;

/// <summary>Column-accurate text helpers: wide (CJK, emoji) graphemes count as two cells and are never split.</summary>
internal static class CellText
{
    internal const string Ellipsis = "…";

    /// <summary>The longest prefix of <paramref name="text"/> that fits in <paramref name="columns"/> cells.</summary>
    internal static string Clip(string text, int columns)
    {
        if (columns <= 0)
            return string.Empty;
        if (text.GetColumns() <= columns)
            return text;

        var builder = new StringBuilder();
        var used = 0;
        foreach (var element in Elements(text))
        {
            var width = Math.Max(0, element.GetColumns());
            if (used + width > columns)
                break;
            builder.Append(element);
            used += width;
        }
        return builder.ToString();
    }

    /// <summary>Clips to <paramref name="columns"/> cells, ending with an ellipsis when text was cut.</summary>
    internal static string Shorten(string text, int columns)
        => text.GetColumns() <= columns
            ? text
            : Clip(text, columns - Ellipsis.GetColumns()) + Ellipsis;

    /// <summary>Clips or pads with spaces so the result is exactly <paramref name="columns"/> cells wide.</summary>
    internal static string Fit(string text, int columns)
    {
        var clipped = Clip(text, columns);
        return clipped + new string(' ', Math.Max(0, columns - clipped.GetColumns()));
    }

    /// <summary>Hard-wraps each line of <paramref name="text"/> at <paramref name="columns"/> cells.</summary>
    internal static List<string> Wrap(string text, int columns)
    {
        var lines = new List<string>();
        foreach (var logicalLine in text.ReplaceLineEndings("\n").Split('\n'))
        {
            var builder = new StringBuilder();
            var used = 0;
            foreach (var element in Elements(logicalLine))
            {
                var width = Math.Max(0, element.GetColumns());
                if (used > 0 && used + width > columns)
                {
                    lines.Add(builder.ToString());
                    builder.Clear();
                    used = 0;
                }
                builder.Append(element);
                used += width;
            }
            lines.Add(builder.ToString());
        }
        return lines;
    }

    static IEnumerable<string> Elements(string text)
    {
        var enumerator = StringInfo.GetTextElementEnumerator(text);
        while (enumerator.MoveNext())
            yield return enumerator.GetTextElement();
    }
}
