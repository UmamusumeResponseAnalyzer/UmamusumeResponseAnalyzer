using System.Text;
using Terminal.Gui.Configuration;
using Terminal.Gui.Drawing;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;
using Attribute = Terminal.Gui.Drawing.Attribute;

namespace UmamusumeResponseAnalyzer.TerminalGui;

/// <summary>
/// Host look: sections are separated by heading rules instead of boxes, a single blue accent marks
/// focus and selection, and floating surfaces sit on one raised tone. The ground and body text stay on
/// the terminal's own colors so plugin content drawn with <see cref="Color.None"/> blends in.
/// </summary>
internal static class UraTheme
{
    internal static readonly Color Ground = Color.None;
    internal static readonly Color Text = Color.None;
    internal static readonly Color Ink = new(0xd6, 0xd8, 0xdc);
    internal static readonly Color Muted = new(0x8b, 0x91, 0x9a);
    internal static readonly Color Rule = new(0x2c, 0x31, 0x38);
    internal static readonly Color Surface = new(0x1a, 0x1d, 0x22);
    internal static readonly Color Sunken = new(0x10, 0x12, 0x15);
    internal static readonly Color Hover = new(0x26, 0x2b, 0x33);
    internal static readonly Color Accent = new(0x6c, 0xb4, 0xff);
    internal static readonly Color Selection = new(0x1e, 0x35, 0x50);
    internal static readonly Color OnSelection = new(0xe8, 0xf2, 0xff);
    internal static readonly Color OnActiveTab = new(0x9f, 0xd0, 0xff);
    internal static readonly Color InactiveSelection = new(0x22, 0x26, 0x2c);
    internal static readonly Color Success = new(0x5c, 0xc9, 0x8c);
    internal static readonly Color Warning = new(0xe6, 0xb4, 0x50);
    internal static readonly Color Error = new(0xff, 0x7b, 0x72);

    /// <summary>Ground-level views: workspaces, the startup dashboard and full-screen menus.</summary>
    internal static readonly Scheme Base = new()
    {
        Normal = new Attribute(Text, Ground),
        HotNormal = new Attribute(Text, Ground, TextStyle.Underline),
        Focus = new Attribute(OnSelection, Selection),
        HotFocus = new Attribute(OnSelection, Selection, TextStyle.Underline),
        Active = new Attribute(Ink, InactiveSelection),
        HotActive = new Attribute(Ink, InactiveSelection, TextStyle.Underline),
        Highlight = new Attribute(Ink, Hover),
        Editable = new Attribute(Ink, Surface),
        ReadOnly = new Attribute(Muted, Surface),
        Disabled = new Attribute(Muted, Ground)
    };

    /// <summary>Menu pages share the ground; the current item is bold on the selection fill.</summary>
    internal static readonly Scheme Menu = new(Base)
    {
        Focus = new Attribute(OnSelection, Selection, TextStyle.Bold),
        HotFocus = new Attribute(OnSelection, Selection, TextStyle.Bold | TextStyle.Underline)
    };

    /// <summary>Floating surfaces: dialogs, the command line, the taskbar and popups.</summary>
    internal static readonly Scheme Raised = new()
    {
        Normal = new Attribute(Ink, Surface),
        HotNormal = new Attribute(Ink, Surface, TextStyle.Underline),
        Focus = new Attribute(OnSelection, Selection),
        HotFocus = new Attribute(OnSelection, Selection, TextStyle.Underline),
        Active = new Attribute(Ink, Hover),
        HotActive = new Attribute(Ink, Hover, TextStyle.Underline),
        Highlight = new Attribute(Ink, Hover),
        Editable = new Attribute(Ink, Surface),
        ReadOnly = new Attribute(Muted, Surface),
        Disabled = new Attribute(Muted, Surface)
    };

    /// <summary>Dialog bodies are raised; their text fields sink back to the ground tone.</summary>
    internal static readonly Scheme Dialog = new(Raised)
    {
        Editable = new Attribute(Ink, Sunken),
        ReadOnly = new Attribute(Muted, Sunken)
    };

    /// <summary>Border lines of raised surfaces.</summary>
    internal static readonly Scheme RaisedRule = new(new Attribute(Rule, Surface));

    internal static void Apply()
    {
        SchemeManager.AddScheme("Base", Base);
        SchemeManager.AddScheme("Accent", Base);
        SchemeManager.AddScheme("Menu", Menu);
        SchemeManager.AddScheme("Dialog", Dialog);

        Terminal.Gui.Views.Dialog.DefaultShadow = ShadowStyles.None;
        Terminal.Gui.Views.Dialog.DefaultBorderStyle = LineStyle.Single;
        MessageBox.DefaultBorderStyle = LineStyle.Single;
        FrameView.DefaultBorderStyle = LineStyle.Single;
        Button.DefaultShadow = ShadowStyles.None;
        Glyphs.LeftBracket = new Rune('[');
        Glyphs.RightBracket = new Rune(']');
    }

    internal static Color SeverityColor(UiSeverity severity) => severity switch
    {
        UiSeverity.Trace => Muted,
        UiSeverity.Info => Accent,
        UiSeverity.Success => Success,
        UiSeverity.Warning => Warning,
        UiSeverity.Error => Error,
        _ => throw new ArgumentOutOfRangeException(nameof(severity), severity, null)
    };

    /// <summary>Draws a dialog's or popup's border lines in the rule tone instead of its text color.</summary>
    internal static void UseRuleBorder(View view)
        => view.Border.GetOrCreateView().SetScheme(RaisedRule);
}
