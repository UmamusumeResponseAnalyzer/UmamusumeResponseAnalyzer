using Gallop.Endpoints;
using Terminal.Gui.App;

namespace UmamusumeResponseAnalyzer.Plugin;

public interface IPluginContext
{
    IApplication Application { get; }
    IPluginAnalyzerRegistry Analyzers { get; }
    bool IsPluginAvailable(string internalName);
    void ReportBackgroundFailure(Exception error);
}

public interface IPlugin : IAsyncDisposable
{
    void Initialize(IPluginContext context);

    ValueTask StartAsync(CancellationToken cancellationToken = default)
        => ValueTask.CompletedTask;

    ValueTask IAsyncDisposable.DisposeAsync() => ValueTask.CompletedTask;

    Task ConfigPromptAsync(IApplication application, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

}

public enum AnalyzerKind
{
    Request,
    Response,
}

public enum EndpointPatternKind
{
    Exact,
    Wildcard,
    Regex,
}

public readonly record struct EndpointPattern
{
    EndpointPattern(EndpointPatternKind kind, string pattern)
    {
        Kind = kind;
        Pattern = pattern;
    }

    public EndpointPatternKind Kind { get; }
    public string Pattern { get; }

    public static EndpointPattern Exact(string path)
        => Create(EndpointPatternKind.Exact, path);

    public static EndpointPattern Wildcard(string pattern)
        => Create(EndpointPatternKind.Wildcard, pattern);

    public static EndpointPattern Regex(string pattern)
        => Create(EndpointPatternKind.Regex, pattern);

    static EndpointPattern Create(EndpointPatternKind kind, string pattern)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pattern);
        return new(kind, pattern);
    }
}

public readonly record struct AnalyzerInvocation<TPayload>(
    GameEndpointDescriptor Endpoint,
    TPayload Payload,
    GameHttpHeaders Headers);

public sealed record GameHttpHeaders(
    string? Sid,
    string? AppVer,
    string? ResVer,
    string? ViewerId,
    string? Device,
    string? DeviceSubtype);

public interface IPluginAnalyzerRegistry
{
    void Register<TPayload>(
        AnalyzerKind kind,
        IReadOnlyList<EndpointPattern> patterns,
        Func<AnalyzerInvocation<TPayload>, ValueTask> handler,
        int priority = 0);
}

[AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
public abstract class AnalyzerAttribute : Attribute
{
    protected AnalyzerAttribute(Type endpointType, AnalyzerKind kind, int priority = 0)
    {
        EndpointType = endpointType;
        Kind = kind;
        Priority = priority;
    }

    public Type EndpointType { get; }
    public AnalyzerKind Kind { get; }
    public int Priority { get; }
}

[AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
public sealed class RequestAnalyzerAttribute<TEndpoint>(int priority = 0)
    : AnalyzerAttribute(typeof(TEndpoint), AnalyzerKind.Request, priority)
    where TEndpoint : IGameEndpoint;

[AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
public sealed class ResponseAnalyzerAttribute<TEndpoint>(int priority = 0)
    : AnalyzerAttribute(typeof(TEndpoint), AnalyzerKind.Response, priority)
    where TEndpoint : IGameEndpoint;
