using i18n = UmamusumeResponseAnalyzer.Localization.TerminalGui;
namespace UmamusumeResponseAnalyzer.TerminalGui;

public static class TerminalUi
{
    static UiHost? uiHost;

    internal static void Initialize(UiHost host)
    {
        ArgumentNullException.ThrowIfNull(host);
        host.EnsureAvailable();
        if (Interlocked.CompareExchange(ref uiHost, host, null) is not null)
            throw new InvalidOperationException(i18n.Host_AlreadyInitialized);
    }

    internal static UiHost RequireHost()
    {
        var host = Volatile.Read(ref uiHost)
            ?? throw new InvalidOperationException(i18n.Host_NotInitialized);
        host.EnsureAvailable();
        return host;
    }

    internal static void LogException(
        string source,
        Exception ex,
        UiSeverity severity = UiSeverity.Error,
        string? details = null)
    {
        ArgumentNullException.ThrowIfNull(ex);
        RequireHost().Log(
            $"[{source}] {FormatExceptionLogMessage(ex)}",
            severity,
            details is null ? ex.ToString() : $"{ex}{Environment.NewLine}{details}");
    }

    internal static string FormatExceptionLogMessage(Exception ex)
    {
        var messages = new List<string>();
        AppendMessages(ex, messages);
        return messages.Count == 0 ? ex.GetType().Name : string.Join(Environment.NewLine, messages);
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

        var message = exception.Message.Trim();
        if (message.Length > 0 && !messages.Contains(message, StringComparer.Ordinal))
            messages.Add(message);
        AppendMessages(exception.InnerException, messages);
    }

    public static void Log(
        string source,
        string text,
        UiSeverity severity = UiSeverity.Info)
        => RequireHost().Log($"[{source}] {text}", severity);

    public static void Notify(
        string source,
        string text,
        UiSeverity severity = UiSeverity.Info,
        TimeSpan? ttl = null)
        => RequireHost().Notify(null, $"[{source}] {text}", severity, ttl, []);
}
