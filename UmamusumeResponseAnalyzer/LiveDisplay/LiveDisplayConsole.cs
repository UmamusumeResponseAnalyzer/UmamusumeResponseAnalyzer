using Spectre.Console;
using Spectre.Console.Rendering;

namespace UmamusumeResponseAnalyzer.LiveDisplay
{
    public static class LiveDisplayConsole
    {
        static readonly AsyncLocal<bool> consoleInteractionActive = new();
        static UiHost? uiHost;

        internal static LiveDisplayWorkspace? DefaultLogWorkspace { get; set; }

        public static void Bind(UiHost host)
        {
            uiHost = host;
        }

        public static void Unbind(UiHost host)
        {
            if (ReferenceEquals(uiHost, host))
            {
                uiHost = null;
                DefaultLogWorkspace = null;
            }
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
            if (host is null)
                return action();

            return host.IsRunning
                ? host.RunConsoleInteractionAsync(action)
                : RunDirectConsoleInteractionAsync(action);
        }

        public static async Task<T> RunAsync<T>(Func<Task<T>> action)
        {
            if (consoleInteractionActive.Value)
                return await action();

            var host = uiHost;
            if (host is null)
                return await action();

            if (!host.IsRunning)
            {
                using var directInteraction = EnterConsoleInteraction();
                return await action();
            }

            T result = default!;
            await host.RunConsoleInteractionAsync(async () => result = await action());
            return result;
        }

        static async Task RunDirectConsoleInteractionAsync(Func<Task> action)
        {
            using var directInteraction = EnterConsoleInteraction();
            await action();
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

        internal static void MarkupLog(string source, string markup, LiveDisplaySeverity severity = LiveDisplaySeverity.Info)
        {
            var host = uiHost;
            if (consoleInteractionActive.Value || host is null)
            {
                Run(() => AnsiConsole.MarkupLine(markup));
                return;
            }

            host.Log(new LiveDisplayLogLine(DefaultLogWorkspace, source, markup, severity, IsMarkup: true, DateTimeOffset.Now));
        }

        internal static void MarkupLog(string source, string format, LiveDisplaySeverity severity, params object[] args)
        {
            MarkupLog(source, string.Format(format, args), severity);
        }

        internal static void LogException(string source, Exception ex, LiveDisplaySeverity severity = LiveDisplaySeverity.Error)
        {
            var host = uiHost;
            if (consoleInteractionActive.Value || host is null)
            {
                WriteException(ex);
                return;
            }

            host.Log(new LiveDisplayLogLine(DefaultLogWorkspace, source, FormatExceptionLogMessage(ex), severity, IsMarkup: false, DateTimeOffset.Now));
        }

        internal static string FormatExceptionLogMessage(Exception ex)
        {
            if (TryFormatPluginInitializationFailure(ex, out var pluginInitializationMessage))
                return pluginInitializationMessage;

            var messages = new List<string>();
            AppendMessages(ex, messages);
            return messages.Count == 0 ? ex.GetType().Name : string.Join(Environment.NewLine, messages);
        }

        static bool TryFormatPluginInitializationFailure(Exception ex, out string message)
        {
            const string prefix = "插件初始化失败: plugin=";

            var outerMessage = NormalizeExceptionMessage(ex.Message);
            if (!outerMessage.StartsWith(prefix, StringComparison.Ordinal))
            {
                message = string.Empty;
                return false;
            }

            var plugin = ParsePluginName(outerMessage[prefix.Length..]);
            var rootMessages = new List<string>();
            AppendMessages(ex.InnerException, rootMessages);
            if (plugin.Length == 0 || rootMessages.Count == 0)
            {
                message = string.Empty;
                return false;
            }

            message = $"{plugin}初始化失败：{rootMessages[0]}";
            if (rootMessages.Count > 1)
                message += Environment.NewLine + string.Join(Environment.NewLine, rootMessages.Skip(1));

            return true;
        }

        static void AppendMessages(Exception? exception, List<string> messages)
        {
            if (exception is null)
                return;

            if (exception is AggregateException { InnerExceptions.Count: > 0 } aggregate)
            {
                foreach (var inner in aggregate.Flatten().InnerExceptions)
                    AppendMessages(inner, messages);
                return;
            }

            var message = NormalizeExceptionMessage(exception.Message);
            if (message.Length > 0 && !messages.Contains(message, StringComparer.Ordinal))
                messages.Add(message);

            AppendMessages(exception.InnerException, messages);
        }

        static string ParsePluginName(string message)
        {
            var plugin = message;
            var displayNameMarker = plugin.IndexOf(" (", StringComparison.Ordinal);
            if (displayNameMarker >= 0)
                plugin = plugin[..displayNameMarker];

            var fieldMarker = plugin.IndexOf(',', StringComparison.Ordinal);
            if (fieldMarker >= 0)
                plugin = plugin[..fieldMarker];

            return plugin.Trim();
        }

        static string NormalizeExceptionMessage(string message)
        {
            return RemoveConfigFileField(RemovePathField(message.Trim())).Trim();
        }

        static string RemoveConfigFileField(string message)
        {
            foreach (var marker in new[] { "配置文件:", "配置文件：" })
            {
                var index = message.IndexOf(marker, StringComparison.Ordinal);
                if (index > 0)
                    return message[..index];
            }

            return message;
        }

        static string RemovePathField(string message)
        {
            var marker = message.IndexOf(", path=", StringComparison.OrdinalIgnoreCase);
            if (marker < 0)
                return message;

            var valueStart = marker + ", path=".Length;
            var nextField = NextFieldIndex(message, valueStart);
            return nextField < 0
                ? message[..marker]
                : message[..marker] + message[nextField..];

            static int NextFieldIndex(string value, int start)
            {
                var next = -1;
                foreach (var field in new[] { ", phase=", ", type=" })
                {
                    var index = value.IndexOf(field, start, StringComparison.OrdinalIgnoreCase);
                    if (index >= 0 && (next < 0 || index < next))
                        next = index;
                }

                return next;
            }
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
            DefaultLogWorkspace = null;
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
