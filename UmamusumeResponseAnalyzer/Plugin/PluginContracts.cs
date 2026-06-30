using Gallop.Endpoints;
using Spectre.Console;
using UmamusumeResponseAnalyzer.LiveDisplay;
using HttpMethod = WatsonWebserver.Core.HttpMethod;

namespace UmamusumeResponseAnalyzer.Plugin;

public interface IPluginContext
{
    ILiveDisplayOutput LiveDisplay { get; }
    IPluginHostEvents Events { get; }
}

public interface IPluginHostEvents
{
    IDisposable OnStarted(Func<CancellationToken, ValueTask> handler);

    IDisposable OnStarted(Func<Task> handler)
        => OnStarted(_ => new ValueTask(handler()));
}

public interface IPlugin
{
    string Name { get; }
    string Author { get; }
    Version Version => GetType().Assembly.GetName().Version ?? new(0, 0, 0);
    string[] Targets { get; }

    string DataDirectory => Path.Combine("PluginData", Name);
    string SettingsFilePath => Path.Combine(DataDirectory, "settings.yaml");

    void Initialize() { }

    void Initialize(ILiveDisplayOutput liveDisplay)
    {
        Initialize();
    }

    void Initialize(IPluginContext context)
    {
        Initialize(context.LiveDisplay);
    }

    void Dispose() { }

    void ConfigPrompt() { }

    Task ConfigPromptAsync()
    {
        ConfigPrompt();
        return Task.CompletedTask;
    }

    Task UpdatePlugin(ProgressContext ctx);
}

public enum AnalyzerKind
{
    Request,
    Response,
}

public enum AnalyzerPayloadKind
{
    Dto,
    RawMessagePack,
}

[AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
public abstract class AnalyzerAttribute(
    Type endpointType,
    AnalyzerKind kind,
    AnalyzerPayloadKind payloadKind,
    int priority = 0) : Attribute
{
    public Type EndpointType { get; } = endpointType;
    public AnalyzerKind Kind { get; } = kind;
    public AnalyzerPayloadKind PayloadKind { get; } = payloadKind;
    public int Priority { get; } = priority;
}

[AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
public sealed class RequestAnalyzerAttribute<TEndpoint>(int priority = 0)
    : AnalyzerAttribute(typeof(TEndpoint), AnalyzerKind.Request, AnalyzerPayloadKind.Dto, priority)
    where TEndpoint : IGameEndpoint;

[AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
public sealed class ResponseAnalyzerAttribute<TEndpoint>(int priority = 0)
    : AnalyzerAttribute(typeof(TEndpoint), AnalyzerKind.Response, AnalyzerPayloadKind.Dto, priority)
    where TEndpoint : IGameEndpoint;

[AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
public sealed class RawRequestAnalyzerAttribute<TEndpoint>(int priority = 0)
    : AnalyzerAttribute(typeof(TEndpoint), AnalyzerKind.Request, AnalyzerPayloadKind.RawMessagePack, priority)
    where TEndpoint : IGameEndpoint;

[AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
public sealed class RawResponseAnalyzerAttribute<TEndpoint>(int priority = 0)
    : AnalyzerAttribute(typeof(TEndpoint), AnalyzerKind.Response, AnalyzerPayloadKind.RawMessagePack, priority)
    where TEndpoint : IGameEndpoint;

[AttributeUsage(AttributeTargets.Method, Inherited = false)]
public sealed class RouteAttribute(HttpMethod method, string path) : Attribute
{
    public HttpMethod Method { get; } = method;
    public string Path { get; } = path;
}

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Property | AttributeTargets.Field, Inherited = false)]
public sealed class PluginSettingAttribute : Attribute;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Property, Inherited = false)]
public sealed class PluginDescriptionAttribute(string description) : Attribute
{
    public string Description { get; } = description;
}

[AttributeUsage(AttributeTargets.Assembly, Inherited = false)]
public sealed class LoadInHostContextAttribute : Attribute;

[AttributeUsage(AttributeTargets.Assembly, Inherited = false, AllowMultiple = true)]
public sealed class SharedContextWithAttribute(params string[] pluginNames) : Attribute
{
    public string[] PluginNames { get; } = pluginNames;
}
