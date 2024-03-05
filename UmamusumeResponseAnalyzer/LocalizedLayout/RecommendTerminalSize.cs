using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UmamusumeResponseAnalyzer.LocalizedLayout
{
    public static class RecommendTerminalSize
    {
        public static TerminalSize SimplifiedChinese = new(110, 35);
        public static TerminalSize Japanese = new(135, 35);
        public static TerminalSize English = new(110, 35);
        public record TerminalSize
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
