using UmamusumeResponseAnalyzer.LocalizedLayout.Handlers;

namespace UmamusumeResponseAnalyzer.LocalizedLayout
{
    public class RecommendTerminalSize
    {
        public static TerminalSize Current
        {
            get
            {
                var culture = Thread.CurrentThread.CurrentUICulture.Name switch
                {
                    "zh-CN" => "SimplifiedChinese",
                    "en-US" => "English",
                    "ja-JP" => "Japanese"
                };
                var layout = typeof(RecommendTerminalSize).GetField(culture, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                return (TerminalSize)layout!.GetValue(null)!;
            }
        }
        public static TerminalSize SimplifiedChinese = new(110, 35);
        public static TerminalSize Japanese = new(135, 35);
        public static TerminalSize English = new(110, 35);
        public class TerminalSize
        {
            public int Width { get; }
            public int Height { get; }
            public TerminalSize(int width, int height)
            {
                Width = width;
                Height = height;
            }
        }
    }
}
