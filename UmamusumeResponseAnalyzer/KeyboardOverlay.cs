namespace UmamusumeResponseAnalyzer
{
    internal interface IKeyboardOverlaySink
    {
        void ShowPopup(KeyboardPopup popup);
        void HidePopup();
        void ShowCommandInput(KeyboardCommandInput input);
        void HideCommandInput();
    }

    internal sealed record KeyboardPopup(
        IReadOnlyList<KeyboardPopupLine> Lines,
        int ScrollOffset = 0,
        DateTimeOffset? ExpiresAt = null);

    internal sealed record KeyboardPopupLine(string Text, ConsoleColor Color, bool IsMarkup);
    internal sealed record KeyboardCommandInput(string Text);

    public sealed class KeyboardHandlerContext
    {
        readonly List<KeyboardPopupLine> lines = [];

        public int LineCount => lines.Count;

        public KeyboardHandlerContext WriteLine(string text = "", ConsoleColor color = ConsoleColor.White)
        {
            lines.Add(new KeyboardPopupLine(text, color, IsMarkup: false));
            return this;
        }

        public KeyboardHandlerContext MarkupLine(string markup = "")
        {
            lines.Add(new KeyboardPopupLine(markup, ConsoleColor.White, IsMarkup: true));
            return this;
        }

        internal KeyboardPopup ToPopup(int scrollOffset = 0)
        {
            return new KeyboardPopup(lines.ToArray(), scrollOffset);
        }
    }
}
