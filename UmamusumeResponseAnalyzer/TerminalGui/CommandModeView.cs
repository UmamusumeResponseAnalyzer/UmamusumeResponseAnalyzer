using i18n = UmamusumeResponseAnalyzer.Localization.TerminalGui;
using Attribute = Terminal.Gui.Drawing.Attribute;
using Terminal.Gui.Drivers;
using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace UmamusumeResponseAnalyzer.TerminalGui;

/// <summary>
/// The bottom command line on the raised surface: a rule row, completion candidates, the <c>›</c>
/// input row and right-aligned key hints. Narrow or short terminals get the input row alone.
/// </summary>
internal sealed class CommandModeView : View
{
    const int CompactWidth = 24;
    // Rule, input and hint rows; candidates go between the rule and the input.
    const int FullHeight = 3;
    const int PromptX = 2;
    const int InputX = 4;

    static readonly Scheme CandidateScheme = new(new Attribute(UraTheme.Muted, UraTheme.Surface));
    static readonly Scheme PromptScheme = new(new Attribute(UraTheme.Accent, UraTheme.Surface, TextStyle.Bold));

    readonly Action<string> submitted;
    readonly Func<string, IReadOnlyList<string>> complete;
    readonly List<string> history = [];
    readonly Label candidates;
    readonly Label prompt;
    readonly TextField input;
    readonly KeyHintsView hints;
    bool compact;

    IReadOnlyList<string> completionCandidates = [];
    View? previousFocus;
    string draft = string.Empty;
    int historyIndex;
    bool settingText;

    public CommandModeView(
        Action<string> submitted,
        Func<string, IReadOnlyList<string>> complete)
    {
        this.submitted = submitted;
        this.complete = complete;

        X = 0;
        Y = Pos.AnchorEnd();
        Width = Dim.Fill();
        Height = Dim.Func(_ => DesiredHeight());
        Visible = false;
        CanFocus = true;
        TabStop = TabBehavior.TabGroup;
        SetScheme(UraTheme.Raised);

        candidates = new Label
        {
            X = InputX,
            Width = Dim.Fill(1),
            CanFocus = false
        };
        candidates.SetScheme(CandidateScheme);
        prompt = new Label
        {
            Text = "›",
            Width = 1,
            Height = 1,
            CanFocus = false
        };
        prompt.SetScheme(PromptScheme);
        input = new TextField
        {
            Height = 1,
            TabStop = TabBehavior.TabStop
        };
        input.KeyBindings.Remove(Key.Tab);
        input.KeyBindings.Remove(Key.CursorUp);
        input.KeyBindings.Remove(Key.CursorDown);
        hints = new KeyHintsView(i18n.CommandMode_Help)
        {
            X = 0,
            Width = Dim.Fill(),
            HintAlignment = Alignment.End
        };

        input.Accepting += (_, args) =>
        {
            args.Handled = true;
            Submit();
        };
        input.KeyDownNotHandled += (_, key) =>
        {
            switch (key.KeyCode)
            {
                case KeyCode.Tab:
                    Complete();
                    key.Handled = true;
                    break;
                case KeyCode.CursorUp:
                    MoveHistory(-1);
                    key.Handled = true;
                    break;
                case KeyCode.CursorDown:
                    MoveHistory(1);
                    key.Handled = true;
                    break;
                case KeyCode.Esc:
                    Close();
                    key.Handled = true;
                    break;
            }
        };
        input.TextChanged += (_, _) =>
        {
            if (settingText)
                return;

            completionCandidates = [];
            historyIndex = history.Count;
            draft = input.Text;
            SetNeedsLayout();
            SetNeedsDraw();
        };

        Add(candidates, prompt, input, hints);
    }

    internal bool IsOpen => Visible;

    internal bool Open(string initialText, View? focused)
    {
        if (Visible)
            return false;

        previousFocus = focused;
        completionCandidates = [];
        historyIndex = history.Count;
        draft = initialText;
        SetInput(initialText);
        input.ClearHistoryChanges();
        Visible = true;
        SetNeedsLayout();
        SetNeedsDraw();
        input.SetFocus();
        input.ClearAllSelection();
        input.InsertionPoint = int.MaxValue;
        return true;
    }

    internal void Close()
    {
        if (!Visible)
            return;

        var focus = previousFocus;
        previousFocus = null;
        completionCandidates = [];
        draft = string.Empty;
        historyIndex = history.Count;
        SetInput(string.Empty);
        input.ClearHistoryChanges();
        Visible = false;
        SetNeedsLayout();
        SetNeedsDraw();

        if (focus is { CanFocus: true, Enabled: true, Visible: true } && IsAttachedTo(focus, SuperView))
            focus.SetFocus();
    }

    protected override bool OnDrawingContent(DrawContext? context)
    {
        if (!compact && Viewport.Width > 0)
        {
            SetAttribute(new Attribute(UraTheme.Rule, UraTheme.Surface));
            AddStr(0, 0, new string('─', Viewport.Width));
        }
        return true;
    }

    protected override void OnSubViewLayout(LayoutEventArgs args)
    {
        compact = Frame.Width < CompactWidth || Frame.Height < FullHeight;
        var candidateLines = compact ? [] : VisibleCandidateLines();

        candidates.Visible = !compact && candidateLines.Length > 0;
        hints.Visible = !compact;
        candidates.Text = string.Join(Environment.NewLine, candidateLines);
        candidates.Y = 1;
        candidates.Height = candidateLines.Length;

        var inputY = compact ? 0 : candidateLines.Length + 1;
        prompt.X = compact ? 0 : PromptX;
        prompt.Y = inputY;
        input.X = compact ? 2 : InputX;
        input.Y = inputY;
        input.Width = Dim.Fill(compact ? 0 : 1);
        hints.Y = inputY + 1;

        base.OnSubViewLayout(args);
    }

    int DesiredHeight()
    {
        var availableHeight = SuperView?.Viewport.Height ?? 0;
        var availableWidth = SuperView?.Viewport.Width ?? 0;
        if (availableHeight < FullHeight || availableWidth < CompactWidth)
            return 1;

        return Math.Min(availableHeight, FullHeight + VisibleCandidateCount(availableHeight));
    }

    string[] VisibleCandidateLines()
    {
        var visibleCount = VisibleCandidateCount(SuperView?.Viewport.Height ?? 0);
        if (visibleCount == 0)
            return [];
        if (completionCandidates.Count <= visibleCount)
            return completionCandidates.ToArray();

        return
        [
            .. completionCandidates.Take(Math.Max(0, visibleCount - 1)),
            string.Format(i18n.CommandMode_MoreCandidates, completionCandidates.Count - Math.Max(0, visibleCount - 1))
        ];
    }

    int VisibleCandidateCount(int availableHeight)
    {
        return Math.Min(completionCandidates.Count, Math.Max(0, availableHeight - FullHeight));
    }

    void Complete()
    {
        IReadOnlyList<string> matches;
        try
        {
            matches = complete(input.Text);
        }
        catch (Exception ex)
        {
            TerminalUi.Notify("Keyboard", string.Format(i18n.CommandMode_CompletionFailed, ex.Message), UiSeverity.Error);
            TerminalUi.LogException("Keyboard", ex);
            completionCandidates = [];
            SetNeedsLayout();
            SetNeedsDraw();
            return;
        }

        completionCandidates = matches.Count > 1 ? matches.ToArray() : [];
        if (matches.Count == 1)
        {
            SetInput(matches[0]);
        }
        else if (matches.Count > 1)
        {
            var prefix = LongestCommonPrefix(matches);
            if (prefix.Length > input.Text.Length)
                SetInput(prefix);
        }

        historyIndex = history.Count;
        draft = input.Text;
        SetNeedsLayout();
        SetNeedsDraw();
    }

    void MoveHistory(int delta)
    {
        if (history.Count == 0)
            return;

        if (delta < 0)
        {
            if (historyIndex == history.Count)
                draft = input.Text;
            historyIndex = Math.Max(0, historyIndex - 1);
            SetInput(history[historyIndex]);
        }
        else if (historyIndex < history.Count)
        {
            historyIndex++;
            SetInput(historyIndex == history.Count ? draft : history[historyIndex]);
        }

        completionCandidates = [];
        SetNeedsLayout();
        SetNeedsDraw();
    }

    void Submit()
    {
        var command = input.Text;
        if (!string.IsNullOrWhiteSpace(command))
            history.Add(command);

        Close();
        if (!string.IsNullOrWhiteSpace(command))
            submitted(command);
    }

    void SetInput(string text)
    {
        settingText = true;
        try
        {
            input.Text = text;
            input.InsertionPoint = int.MaxValue;
        }
        finally
        {
            settingText = false;
        }
    }

    static string LongestCommonPrefix(IReadOnlyList<string> values)
    {
        var prefix = values[0];
        for (var i = 1; i < values.Count && prefix.Length > 0; i++)
        {
            var length = Math.Min(prefix.Length, values[i].Length);
            var index = 0;
            while (index < length && prefix[index] == values[i][index])
                index++;
            prefix = prefix[..index];
        }
        return prefix;
    }

    static bool IsAttachedTo(View view, View? ancestor)
    {
        for (View? current = view; current is not null; current = current.SuperView)
        {
            if (ReferenceEquals(current, ancestor))
                return true;
        }
        return false;
    }
}
