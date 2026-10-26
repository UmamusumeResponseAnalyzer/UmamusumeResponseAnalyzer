using Terminal.Gui.Drawing;
using Terminal.Gui.Text;
using Terminal.Gui.ViewBase;
using Attribute = Terminal.Gui.Drawing.Attribute;

namespace UmamusumeResponseAnalyzer.TerminalGui;

/// <summary>
/// One row of key hints such as <c>Tab 补全 · ↑↓ 历史</c>: each <c>·</c>-separated hint starts with its
/// key, drawn in the text color, and the rest of the hint is muted. Hints that do not fit are dropped
/// whole rather than cut.
/// </summary>
internal sealed class KeyHintsView : View
{
    const int HintGap = 3;
    const string Separator = " · ";

    readonly (string Key, string Label)[] hints;

    internal KeyHintsView(string text)
    {
        hints = text
            .Split(Separator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(hint =>
            {
                var space = hint.IndexOf(' ');
                return space < 0 ? (hint, string.Empty) : (hint[..space], hint[(space + 1)..]);
            })
            .ToArray();
        Height = 1;
        CanFocus = false;
        TabStop = TabBehavior.NoStop;
    }

    internal Alignment HintAlignment { get; init; } = Alignment.Start;

    protected override bool OnDrawingContent(DrawContext? context)
    {
        var width = Viewport.Width;
        var normal = GetAttributeForRole(VisualRole.Normal);
        var visible = new List<(string Key, string Label)>(hints.Length);
        var used = 0;
        foreach (var hint in hints)
        {
            var next = used + (visible.Count == 0 ? 0 : HintGap) + HintWidth(hint);
            if (next > width - 2)
                break;
            visible.Add(hint);
            used = next;
        }

        var x = HintAlignment == Alignment.End ? Math.Max(1, width - 1 - used) : 1;
        foreach (var (key, label) in visible)
        {
            SetAttribute(new Attribute(UraTheme.Ink, normal.Background));
            AddStr(x, 0, key);
            x += key.GetColumns();
            if (label.Length > 0)
            {
                SetAttribute(new Attribute(UraTheme.Muted, normal.Background));
                AddStr(x + 1, 0, label);
                x += 1 + label.GetColumns();
            }
            x += HintGap;
        }
        return true;
    }

    static int HintWidth((string Key, string Label) hint)
        => hint.Key.GetColumns() + (hint.Label.Length == 0 ? 0 : 1 + hint.Label.GetColumns());
}
