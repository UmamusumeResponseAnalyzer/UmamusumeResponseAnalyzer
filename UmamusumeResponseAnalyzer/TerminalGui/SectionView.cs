using Terminal.Gui.Drawing;
using Terminal.Gui.Text;
using Terminal.Gui.ViewBase;
using Attribute = Terminal.Gui.Drawing.Attribute;

namespace UmamusumeResponseAnalyzer.TerminalGui;

/// <summary>
/// A titled region drawn as a heading rule (<c>标题 ─────</c>) instead of a box. The heading takes the
/// first row; content goes below it from <see cref="ContentX"/>, so a section costs one row and two
/// columns, the same as a bordered frame. The heading turns accent while focus is inside the section.
/// </summary>
internal sealed class SectionView : View
{
    internal const int ContentX = 1;
    internal const int ContentY = 1;

    string heading;

    internal SectionView(string heading)
    {
        this.heading = heading;
    }

    internal string Heading
    {
        get => heading;
        set
        {
            if (heading == value)
                return;
            heading = value;
            SetNeedsDraw();
        }
    }

    /// <summary>Places <paramref name="content"/> under the heading, filling the section.</summary>
    internal static void Place(View content, int bottomGap = 0)
    {
        content.X = ContentX;
        content.Y = ContentY;
        content.Width = Dim.Fill(ContentX);
        content.Height = Dim.Fill(bottomGap);
    }

    protected override void OnHasFocusChanged(bool newHasFocus, View? previousFocusedView, View? focusedView)
    {
        base.OnHasFocusChanged(newHasFocus, previousFocusedView, focusedView);
        SetNeedsDraw();
    }

    protected override bool OnDrawingContent(DrawContext? context)
    {
        var width = Viewport.Width;
        if (width <= 0 || Viewport.Height <= 0)
            return true;

        var background = GetAttributeForRole(VisualRole.Normal).Background;
        var title = TextFormatter.ClipOrPad(heading, Math.Max(0, width - 2)).TrimEnd();
        SetAttribute(new Attribute(
            HasFocus ? UraTheme.Accent : GetAttributeForRole(VisualRole.Normal).Foreground,
            background,
            TextStyle.Bold));
        AddStr(ContentX, 0, title);

        var ruleStart = ContentX + title.GetColumns() + (title.Length == 0 ? 0 : 1);
        var ruleLength = width - 1 - ruleStart;
        if (ruleLength > 0)
        {
            SetAttribute(new Attribute(UraTheme.Rule, background));
            AddStr(ruleStart, 0, new string('─', ruleLength));
        }
        return true;
    }
}
