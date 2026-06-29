using Spectre.Console.Rendering;

namespace UmamusumeResponseAnalyzer.LiveDisplay
{
    sealed class NotificationOverlayRenderable(
        IRenderable content,
        IReadOnlyList<string> popupLines,
        int popupWidth) : PopupOverlayRenderable(content, PlainLines(popupLines), popupWidth)
    {
        const int TopInset = 1;
        const int RightInset = 1;

        protected override bool TryGetPlacement(int maxWidth, int? maxHeight, int overlayHeight, out OverlayPlacement placement)
        {
            if (maxHeight is { } height && TopInset + overlayHeight > height)
            {
                placement = default;
                return false;
            }

            var rightInset = OverlayWidth + RightInset < maxWidth ? RightInset : 0;
            var left = maxWidth - rightInset - OverlayWidth;
            if (left <= 0)
            {
                placement = default;
                return false;
            }

            placement = new OverlayPlacement(left, TopInset);
            return true;
        }
    }
}
