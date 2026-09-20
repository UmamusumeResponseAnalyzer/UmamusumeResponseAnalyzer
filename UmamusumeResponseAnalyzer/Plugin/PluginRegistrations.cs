using Gallop.Endpoints;
using System.Collections.Immutable;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace UmamusumeResponseAnalyzer.Plugin;

internal sealed record AnalyzerRegistration(
    IPlugin Plugin,
    MethodInfo? Method,
    Type EndpointType,
    AnalyzerKind Kind,
    int Priority,
    Func<AnalyzerDispatchContext, ValueTask> Handler,
    string Source);

internal sealed record PluginRegistrationPlan(
    IReadOnlyList<AnalyzerRegistration> Analyzers,
    IReadOnlyList<Func<CancellationToken, ValueTask>> BackgroundOperations);

internal sealed class PluginScopedAnalyzerRegistry(IPlugin plugin) : IPluginAnalyzerRegistry
{
    public void Register<TPayload>(
        AnalyzerKind kind,
        IReadOnlyList<EndpointPattern> patterns,
        Func<AnalyzerInvocation<TPayload>, ValueTask> handler,
        int priority = 0)
        => PluginManager.StageProgrammaticAnalyzers(plugin, kind, patterns, handler, priority);
}

internal sealed class PluginRegistrationStage(
    IPlugin plugin,
    IEnumerable<AnalyzerRegistration>? initialAnalyzers = null) : IDisposable
{
    readonly List<AnalyzerRegistration> analyzers = initialAnalyzers?.ToList() ?? [];
    readonly List<Func<CancellationToken, ValueTask>> backgroundOperations = [];
    bool committed;
    bool disposed;

    internal IPlugin Plugin { get; } = plugin;

    internal void Add(IEnumerable<AnalyzerRegistration> registrations)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        analyzers.AddRange(registrations);
    }

    internal void AddBackground(Func<CancellationToken, ValueTask> operation)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        backgroundOperations.Add(operation);
    }

    internal void Commit()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (committed)
            throw new InvalidOperationException($"插件 registration stage 已提交: {PluginManager.InternalName(Plugin)}");

        PluginManager.CommitRegistrationStage(Plugin, analyzers, backgroundOperations);
        committed = true;
    }

    public void Dispose()
    {
        disposed = true;
        analyzers.Clear();
        backgroundOperations.Clear();
        PluginManager.EndRegistrationStage(this);
    }
}

internal static partial class PluginManager
{
    static readonly AsyncLocal<PluginRegistrationStage?> ActiveRegistrationStage = new();
    static readonly TimeSpan PatternTimeout = TimeSpan.FromMilliseconds(100);

    internal static PluginRegistrationStage BeginRegistrationStage(
        IPlugin plugin,
        bool includeAttributeAnalyzers = false)
    {
        if (ActiveRegistrationStage.Value is not null)
            throw new InvalidOperationException("插件 registration stage 不允许嵌套。");

        var stage = new PluginRegistrationStage(
            plugin,
            includeAttributeAnalyzers ? CreateAttributeRegistrations(plugin) : null);
        ActiveRegistrationStage.Value = stage;
        return stage;
    }

    internal static void EndRegistrationStage(PluginRegistrationStage stage)
    {
        if (ReferenceEquals(ActiveRegistrationStage.Value, stage))
            ActiveRegistrationStage.Value = null;
    }

    internal static void StageBackgroundOperation(
        IPlugin plugin,
        Func<CancellationToken, ValueTask> operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        RequireRegistrationStage(plugin).AddBackground(operation);
    }

    internal static void ValidateRegistrationStage(IPlugin plugin)
        => _ = RequireRegistrationStage(plugin);

    internal static void RegisterMethods(IPlugin plugin)
        => CommitAnalyzerRegistrations(CreateAttributeRegistrations(plugin));

    internal static PluginRegistrationPlan CreateRegistrationPlan(IPlugin plugin)
        => new(CreateAttributeRegistrations(plugin), []);

    static List<AnalyzerRegistration> CreateAttributeRegistrations(IPlugin plugin)
    {
        var registrations = new List<AnalyzerRegistration>();
        foreach (var method in plugin.GetType().GetMethods(
                     BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance |
                     BindingFlags.Static | BindingFlags.DeclaredOnly))
        {
            foreach (var analyzer in method.GetCustomAttributes<AnalyzerAttribute>())
                registrations.Add(CreateAttributeRegistration(plugin, method, analyzer));
        }

        return registrations;
    }

    static AnalyzerRegistration CreateAttributeRegistration(
        IPlugin plugin,
        MethodInfo method,
        AnalyzerAttribute analyzer)
    {
        if (method.ContainsGenericParameters || method.ReturnType != typeof(ValueTask))
            throw AnalyzerRegistrationException(
                plugin,
                method,
                analyzer.EndpointType,
                analyzer.Kind,
                "ValueTask analyzer(TConcreteDto payload)",
                DescribeAnalyzerSignature(method));

        var parameters = method.GetParameters();
        if (parameters.Length != 1)
            throw AnalyzerRegistrationException(
                plugin,
                method,
                analyzer.EndpointType,
                analyzer.Kind,
                "ValueTask analyzer(TConcreteDto payload)",
                DescribeAnalyzerSignature(method));

        if (!GameEndpointCatalog.ByEndpointType.TryGetValue(analyzer.EndpointType, out var endpoint))
            throw AnalyzerRegistrationException(
                plugin,
                method,
                analyzer.EndpointType,
                analyzer.Kind,
                "catalog endpoint",
                "endpoint type is not in GameEndpointCatalog");

        var payloadType = parameters[0].ParameterType;
        var expected = analyzer.Kind == AnalyzerKind.Request ? endpoint.RequestType : endpoint.ResponseType;
        if (payloadType != expected)
            throw AnalyzerRegistrationException(
                plugin,
                method,
                analyzer.EndpointType,
                analyzer.Kind,
                expected.FullName ?? expected.Name,
                payloadType.FullName ?? payloadType.Name);

        var create = typeof(PluginManager)
            .GetMethod(nameof(CreateAttributeHandler), BindingFlags.NonPublic | BindingFlags.Static)!
            .MakeGenericMethod(payloadType);
        var handler = (Func<AnalyzerDispatchContext, ValueTask>)create.Invoke(null, [plugin, method])!;
        return new(
            plugin,
            method,
            analyzer.EndpointType,
            analyzer.Kind,
            analyzer.Priority,
            handler,
            $"{method.DeclaringType?.FullName}.{method.Name} [{analyzer.GetType().Name}]");
    }

    static Func<AnalyzerDispatchContext, ValueTask> CreateAttributeHandler<TPayload>(
        IPlugin plugin,
        MethodInfo method)
    {
        var handler = method.IsStatic
            ? method.CreateDelegate<Func<TPayload, ValueTask>>()
            : method.CreateDelegate<Func<TPayload, ValueTask>>(plugin);
        return context => handler((TPayload)context.GetDto(typeof(TPayload)));
    }

    internal static IPluginAnalyzerRegistry AnalyzersFor(IPlugin plugin)
        => new PluginScopedAnalyzerRegistry(plugin);

    internal static void StageProgrammaticAnalyzers<TPayload>(
        IPlugin plugin,
        AnalyzerKind kind,
        IReadOnlyList<EndpointPattern> patterns,
        Func<AnalyzerInvocation<TPayload>, ValueTask> handler,
        int priority)
    {
        ArgumentNullException.ThrowIfNull(patterns);
        ArgumentNullException.ThrowIfNull(handler);
        if (!Enum.IsDefined(kind))
            throw new ArgumentOutOfRangeException(nameof(kind));
        if (patterns.Count == 0)
            throw new ArgumentException("Analyzer 至少需要一个 endpoint pattern。", nameof(patterns));

        var payloadType = typeof(TPayload);
        ValidateProgrammaticPayload(kind, payloadType);
        var endpoints = ExpandEndpointPatterns(patterns);
        var registrations = endpoints.Select(endpoint => new AnalyzerRegistration(
            plugin,
            null,
            endpoint.EndpointType,
            kind,
            priority,
            payloadType == typeof(ReadOnlyMemory<byte>)
                ? context => handler(new(endpoint, (TPayload)(object)context.Payload, context.Headers))
                : context => handler(new(endpoint, (TPayload)context.GetDto(payloadType), context.Headers)),
            $"programmatic {payloadType.FullName} analyzer")).ToList();

        RequireRegistrationStage(plugin).Add(registrations);
    }

    static PluginRegistrationStage RequireRegistrationStage(IPlugin plugin)
    {
        var stage = ActiveRegistrationStage.Value;
        if (stage is null || !ReferenceEquals(stage.Plugin, plugin))
            throw new InvalidOperationException(
                $"Analyzer 与 background operation 只能在 Initialize 或 Host 执行的 OnStarted 回调中注册: plugin={InternalName(plugin)}");
        return stage;
    }

    static void ValidateProgrammaticPayload(AnalyzerKind kind, Type payloadType)
    {
        if (payloadType == typeof(ReadOnlyMemory<byte>))
            return;

        if (payloadType == typeof(object) || payloadType == typeof(byte[]) || payloadType.IsInterface ||
            payloadType.IsAbstract || payloadType.ContainsGenericParameters)
            throw new InvalidOperationException($"Analyzer payload 必须是 Host 中现有的闭合具体 Gallop DTO: {payloadType.FullName}");

        var known = GameEndpointCatalog.ByEndpointType.Values.Any(endpoint =>
            (kind == AnalyzerKind.Request ? endpoint.RequestType : endpoint.ResponseType) == payloadType);
        if (!known)
            throw new InvalidOperationException(
                $"Analyzer payload 不是当前方向的 Host Gallop DTO: kind={kind}, payload={payloadType.FullName}");
    }

    internal static IReadOnlyList<GameEndpointDescriptor> ExpandEndpointPatterns(
        IReadOnlyList<EndpointPattern> patterns)
    {
        var endpoints = new Dictionary<Type, GameEndpointDescriptor>();
        foreach (var pattern in patterns)
        {
            var matcher = CreatePatternMatcher(pattern);
            var matches = GameEndpointCatalog.ByEndpointType.Values
                .Where(endpoint => matcher(endpoint.Path))
                .OrderBy(endpoint => endpoint.Path, StringComparer.Ordinal)
                .ToList();
            if (matches.Count == 0)
                throw new InvalidOperationException(
                    $"Endpoint pattern 未命中当前 catalog: kind={pattern.Kind}, pattern={pattern.Pattern}");

            foreach (var endpoint in matches)
                endpoints.TryAdd(endpoint.EndpointType, endpoint);
        }

        return endpoints.Values.OrderBy(endpoint => endpoint.Path, StringComparer.Ordinal).ToList();
    }

    static Func<string, bool> CreatePatternMatcher(EndpointPattern pattern)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pattern.Pattern);
        return pattern.Kind switch
        {
            EndpointPatternKind.Exact => CreateExactMatcher(pattern.Pattern),
            EndpointPatternKind.Wildcard => CreateWildcardMatcher(pattern.Pattern),
            EndpointPatternKind.Regex => CreateRegexMatcher(pattern.Pattern),
            _ => throw new ArgumentOutOfRangeException(nameof(pattern), pattern.Kind, "未知 endpoint pattern 类型。"),
        };
    }

    static Func<string, bool> CreateExactMatcher(string path)
    {
        ValidateCanonicalPathPattern(path, allowWildcard: false);
        return candidate => string.Equals(candidate, path, StringComparison.Ordinal);
    }

    static Func<string, bool> CreateWildcardMatcher(string pattern)
    {
        ValidateCanonicalPathPattern(pattern, allowWildcard: true);
        if (!pattern.Contains('*', StringComparison.Ordinal))
            throw new ArgumentException($"Wildcard endpoint pattern 必须包含 *: {pattern}", nameof(pattern));

        return CreateRegexMatcher(
            Regex.Escape(pattern).Replace("\\*", "[^/]*", StringComparison.Ordinal));
    }

    static void ValidateCanonicalPathPattern(string pattern, bool allowWildcard)
    {
        if (pattern[0] != '/' || pattern.Length == 1 || pattern[^1] == '/' ||
            pattern.Contains('\\', StringComparison.Ordinal) ||
            pattern.Contains('?', StringComparison.Ordinal) ||
            pattern.Contains('#', StringComparison.Ordinal))
            throw new ArgumentException($"Endpoint pattern 必须是 canonical absolute path: {pattern}", nameof(pattern));

        foreach (var segment in pattern[1..].Split('/'))
        {
            if (segment.Length == 0 || segment is "." or "..")
                throw new ArgumentException($"Endpoint pattern 包含非 canonical path segment: {pattern}", nameof(pattern));
            if (!allowWildcard && segment.Contains('*', StringComparison.Ordinal))
                throw new ArgumentException($"Exact endpoint pattern 不能包含 *: {pattern}", nameof(pattern));
            if (allowWildcard && segment.Contains("**", StringComparison.Ordinal))
                throw new ArgumentException($"Wildcard endpoint pattern 不支持相邻 **: {pattern}", nameof(pattern));
        }
    }

    static Func<string, bool> CreateRegexMatcher(string pattern)
    {
        var regex = new Regex(
            $"\\A(?:{pattern})\\z",
            RegexOptions.CultureInvariant | RegexOptions.NonBacktracking,
            PatternTimeout);
        return regex.IsMatch;
    }

    internal static void CommitRegistrationStage(
        IPlugin plugin,
        IReadOnlyList<AnalyzerRegistration> analyzers,
        IReadOnlyList<Func<CancellationToken, ValueTask>> backgroundOperations)
    {
        var generation = RequireGeneration(plugin);
        var plan = new PluginRegistrationPlan([.. analyzers], [.. backgroundOperations]);
        if (generation.TryStageRegistration(plan))
            return;

        generation.ValidateBackgroundAdmission();
        CommitAnalyzerRegistrations(plan.Analyzers);
        generation.RunBackground(plan.BackgroundOperations);
    }

    internal static void CommitPendingRegistrations(IEnumerable<IPlugin> plugins)
    {
        var pending = plugins
            .Select(plugin => (Generation: RequireGeneration(plugin), Plan: RequireGeneration(plugin).TakePendingRegistration()))
            .ToList();
        CommitAnalyzerRegistrations(pending.SelectMany(item => item.Plan.Analyzers));
        foreach (var item in pending)
            item.Generation.Open();
        foreach (var item in pending)
            item.Generation.RunBackground(item.Plan.BackgroundOperations);
    }

    internal static void CommitAnalyzerRegistrations(IEnumerable<AnalyzerRegistration> registrations)
    {
        var added = registrations.ToArray();
        if (added.Length == 0)
            return;

        var current = Runtime.ReadAnalyzers();
        Runtime.PublishAnalyzers(new(
            AppendInDispatchOrder(
                current.Request,
                added.Where(registration => registration.Kind == AnalyzerKind.Request)),
            AppendInDispatchOrder(
                current.Response,
                added.Where(registration => registration.Kind == AnalyzerKind.Response))));
    }

    internal static AnalyzerCallbackSnapshot SnapshotAnalyzerRegistrations(
        AnalyzerKind kind,
        Type endpointType)
    {
        var snapshot = Runtime.ReadAnalyzers();
        var index = kind == AnalyzerKind.Request ? snapshot.RequestByEndpoint : snapshot.ResponseByEndpoint;
        return AnalyzerCallbackSnapshot.Create(index.GetValueOrDefault(endpointType, []));
    }

    static void RemoveAnalyzerMethods(IPlugin plugin)
        => RemoveAnalyzerMethods([plugin]);

    static void RemoveAnalyzerMethods(IEnumerable<IPlugin> plugins)
    {
        var removed = plugins.ToHashSet<IPlugin>(ReferenceEqualityComparer.Instance);
        var current = Runtime.ReadAnalyzers();
        Runtime.PublishAnalyzers(new(
            current.Request.Where(registration => !removed.Contains(registration.Plugin)).ToImmutableArray(),
            current.Response.Where(registration => !removed.Contains(registration.Plugin)).ToImmutableArray()));
    }

    static ImmutableArray<AnalyzerRegistration> AppendInDispatchOrder(
        ImmutableArray<AnalyzerRegistration> current,
        IEnumerable<AnalyzerRegistration> added)
        => current
            .AddRange(added)
            .OrderBy(registration => registration.Priority)
            .ToImmutableArray();

    static void ClearAnalyzerRegistrations()
        => Runtime.PublishAnalyzers(AnalyzerRuntimeSnapshot.Empty);

    static string DescribeAnalyzerSignature(MethodInfo method)
    {
        var parameters = method.GetParameters();
        var parameterText = parameters.Length == 0
            ? "<none>"
            : string.Join(", ", parameters.Select(p => p.ParameterType.FullName ?? p.ParameterType.Name));
        var asyncStateMachine = method.GetCustomAttribute<AsyncStateMachineAttribute>() is null
            ? string.Empty
            : ", async-state-machine";
        return $"return={method.ReturnType.FullName ?? method.ReturnType.Name}, parameters=({parameterText}){asyncStateMachine}";
    }

    static InvalidOperationException AnalyzerRegistrationException(
        IPlugin plugin,
        MethodInfo method,
        Type endpointType,
        AnalyzerKind kind,
        string expected,
        string actual)
        => new(
            $"插件 analyzer 签名无效: plugin={InternalName(plugin)}, " +
            $"method={method.DeclaringType?.FullName}.{method.Name}, endpoint={endpointType.FullName}, " +
            $"kind={kind}, expected={expected}, actual={actual}");
}
