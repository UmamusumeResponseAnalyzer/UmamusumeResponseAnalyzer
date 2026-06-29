using Spectre.Console;

namespace UmamusumeResponseAnalyzer.LiveDisplay
{
    static class PopupFrame
    {
        public static string Top(int width)
        {
            return Border('┌', '┐', width);
        }

        public static string Bottom(int width)
        {
            return Border('└', '┘', width);
        }

        public static string TopWithCenteredLabel(int width, string label)
        {
            if (string.IsNullOrEmpty(label))
                return Top(width);

            var labelWidth = label.GetCellWidth();
            var dashTotal = width - 2 - labelWidth;
            if (dashTotal < 2)
                return Top(width);

            var leftDash = dashTotal / 2;
            var rightDash = dashTotal - leftDash;
            return "┌" + new string('─', leftDash) + label + new string('─', rightDash) + "┐";
        }

        public static string BottomWithRightLabel(int width, string label)
        {
            if (string.IsNullOrEmpty(label))
                return Bottom(width);

            label = " " + label + " ";
            var labelWidth = label.GetCellWidth();
            var dashTotal = width - 2 - labelWidth;
            if (dashTotal < 2)
                return Bottom(width);

            return "└" + new string('─', dashTotal) + label + "┘";
        }

        static string Border(char left, char right, int width)
        {
            return left + new string('─', Math.Max(0, width - 2)) + right;
        }
    }
}
