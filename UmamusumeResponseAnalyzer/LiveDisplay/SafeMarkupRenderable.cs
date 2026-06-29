using Spectre.Console;
using Spectre.Console.Rendering;

namespace UmamusumeResponseAnalyzer.LiveDisplay
{
    // Markup 解析失败时回退到纯文本的 IRenderable 装饰器。
    // 用于把插件来源的日志文本安全地塞进 Spectre 渲染管线：合法 markup 上色，
    // 非法 markup 原样显示而不抛异常。
    internal sealed class SafeMarkupRenderable(string markup, string fallbackMarkup) : IRenderable
    {
        public Measurement Measure(RenderOptions options, int maxWidth)
        {
            try
            {
                return ((IRenderable)new Markup(markup)).Measure(options, maxWidth);
            }
            catch
            {
                return ((IRenderable)new Markup(fallbackMarkup)).Measure(options, maxWidth);
            }
        }

        public IEnumerable<Segment> Render(RenderOptions options, int maxWidth)
        {
            try
            {
                return ((IRenderable)new Markup(markup)).Render(options, maxWidth).ToArray();
            }
            catch
            {
                return ((IRenderable)new Markup(fallbackMarkup)).Render(options, maxWidth).ToArray();
            }
        }
    }
}
