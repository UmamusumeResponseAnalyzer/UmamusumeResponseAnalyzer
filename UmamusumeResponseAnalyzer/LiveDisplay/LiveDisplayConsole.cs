using Spectre.Console;
using Spectre.Console.Rendering;

namespace UmamusumeResponseAnalyzer.LiveDisplay
{
    public static class LiveDisplayConsole
    {
        static readonly AsyncLocal<bool> consoleInteractionActive = new();
        static UiHost? uiHost;

        public static void Bind(UiHost host)
        {
            uiHost = host;
        }

        public static void Unbind(UiHost host)
        {
            if (ReferenceEquals(uiHost, host))
                uiHost = null;
        }

        public static void Run(Action action)
        {
            RunAsync(() =>
            {
                action();
                return Task.CompletedTask;
            }).GetAwaiter().GetResult();
        }

        public static T Run<T>(Func<T> action)
        {
            return RunAsync(() => Task.FromResult(action())).GetAwaiter().GetResult();
        }

        public static Task RunAsync(Func<Task> action)
        {
            if (consoleInteractionActive.Value)
                return action();

            var host = uiHost;
            return host is null ? action() : host.RunConsoleInteractionAsync(action);
        }

        public static async Task<T> RunAsync<T>(Func<Task<T>> action)
        {
            if (consoleInteractionActive.Value)
                return await action();

            var host = uiHost;
            if (host is null)
                return await action();

            T result = default!;
            await host.RunConsoleInteractionAsync(async () => result = await action());
            return result;
        }

        public static T Prompt<T>(IPrompt<T> prompt)
        {
            return Run(() => AnsiConsole.Prompt(prompt));
        }

        // Progress 是 Spectre 的 live rendering，与 UiHost 的 AnsiConsole.Live 互斥。
        // 走 RunAsync（console-interaction）后，live loop 已暂停并清屏，Progress 独占控制台即可。
        // 调用方负责配置 columns 并启动，例如：
        //   await LiveDisplayConsole.RunProgressAsync(p => p.Columns([...]).StartAsync(async ctx => { ... }));
        public static Task RunProgressAsync(Func<Progress, Task> action)
        {
            return RunAsync(() => action(AnsiConsole.Progress()));
        }

        public static Task<TResult> RunProgressAsync<TResult>(Func<Progress, Task<TResult>> action)
        {
            return RunAsync(() => action(AnsiConsole.Progress()));
        }

        public static void Clear()
        {
            Run(AnsiConsole.Clear);
        }

        public static void MarkupLine(string markup)
        {
            Run(() => AnsiConsole.MarkupLine(markup));
        }

        // 与 AnsiConsole.MarkupLine(string format, params object[] args) 对齐的复合格式化重载。
        public static void MarkupLine(string format, params object[] args)
        {
            Run(() => AnsiConsole.MarkupLine(format, args));
        }

        public static void WriteLine(string text)
        {
            Run(() => AnsiConsole.WriteLine(text));
        }

        public static void WriteLine()
        {
            Run(AnsiConsole.WriteLine);
        }

        // WriteLine(string format, params object[] args) 与 string.Format 等价的复合格式化重载。
        public static void WriteLine(string format, params object[] args)
        {
            Run(() => AnsiConsole.WriteLine(format, args));
        }

        // 桥接的 ReadKey。console-interaction 期间 KeyboardManager 已挂起，Console.ReadKey 仍能正常读键。
        public static ConsoleKeyInfo ReadKey(bool intercept = false)
        {
            return Run(() => Console.ReadKey(intercept));
        }

        public static string ReadLine()
        {
            return Run(() => Console.ReadLine() ?? string.Empty);
        }

        public static void Write(IRenderable renderable)
        {
            Run(() => AnsiConsole.Write(renderable));
        }

        public static void WriteException(Exception ex)
        {
            Run(() => AnsiConsole.WriteException(ex));
        }

        public static void Log(string source, string text, LiveDisplaySeverity severity = LiveDisplaySeverity.Info)
        {
            var host = uiHost;
            if (host is null)
            {
                AnsiConsole.WriteLine($"[{source}] {text}");
                return;
            }

            host.Log(new LiveDisplayLogLine(null, source, text, severity, IsMarkup: false, DateTimeOffset.Now));
        }

        public static void Notify(string source, string text, LiveDisplaySeverity severity = LiveDisplaySeverity.Info, TimeSpan? ttl = null)
        {
            var host = uiHost;
            if (host is null)
            {
                AnsiConsole.WriteLine($"[{source}] {text}");
                return;
            }

            host.Notify(new LiveDisplayNotification(null, source, text, severity, LiveDisplayNotification.ExpiresAtFromNow(severity, ttl)));
        }

        internal static IDisposable EnterConsoleInteraction()
        {
            var previous = consoleInteractionActive.Value;
            consoleInteractionActive.Value = true;
            return new ConsoleInteractionScope(previous);
        }

        internal static void UnbindForTests()
        {
            uiHost = null;
        }

        sealed class ConsoleInteractionScope(bool previous) : IDisposable
        {
            public void Dispose()
            {
                consoleInteractionActive.Value = previous;
            }
        }
    }
}
