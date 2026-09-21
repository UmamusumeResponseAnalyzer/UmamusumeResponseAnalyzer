using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Gallop;
using EventLoggerPlugin;
using EventResponseAnalyzer;
using GamePacketCollector;
using RamenScenarioAnalyzer;
using Terminal.Gui.App;
using UmamusumeResponseAnalyzer;
using UmamusumeResponseAnalyzer.Entities;
using UmamusumeResponseAnalyzer.TerminalGui;
using UmamusumeResponseAnalyzer.Plugin;
using UiText = UmamusumeResponseAnalyzer.Localization.TerminalGui;

if (args.Contains("--self-test", StringComparer.OrdinalIgnoreCase))
{
    SelfTests.Run();
    return;
}

var corpusPath = CommandLine.ResolveCorpusPath(args);
using var ui = new WorkspaceSmokeSession();
var replay = new ReplayHarness(corpusPath, ui);
var summary = await replay.RunAsync();
summary.WriteTo(Console.Out);
if (summary.HasFailures)
    Environment.ExitCode = 1;

static class SelfTests
{
    public static void Run()
    {
        var body = new byte[] { 0x91, 0x01, 0xC0, 0xFF };
        var headerText = string.Join(
            "\r\n",
            "POST /notify/request HTTP/1.1",
            "x-hachimi-game-url: https://api.games.umamusume.jp/umamusume/single_mode_ramen/start",
            "x-hachimi-sid: sid-1",
            "x-hachimi-app-ver: 2.28.5",
            "x-hachimi-res-ver: 10020130:TQ39BvvuMvNQ",
            "x-hachimi-viewerid: 428051154",
            "x-hachimi-device: 3",
            "x-hachimi-device-subtype: 1",
            $"content-length: {body.Length}",
            "",
            "");
        foreach (var header in new[]
        {
            headerText,
            headerText.Replace("\r\n", "\n", StringComparison.Ordinal),
            headerText.Replace("HTTP/1.1\r\n", "HTTP/1.1\n\n", StringComparison.Ordinal)
        })
        {
            var message = HttpMessageEnvelope.Parse("sample.txt", Encoding.ASCII.GetBytes(header).Concat(body).ToArray());
            AssertEqual(PacketDirection.Request, message.Direction);
            AssertEqual("https://api.games.umamusume.jp/umamusume/single_mode_ramen/start", message.CanonicalUrl);
            AssertEqual("sid-1", message.Headers.Sid);
            AssertBytes(body, message.Body);
        }
        AssertThrows<FormatException>(
            () => HttpMessageEnvelope.Parse("bad-length.txt", Encoding.ASCII.GetBytes(headerText.Replace($"content-length: {body.Length}", "content-length: 999", StringComparison.Ordinal)).Concat(body).ToArray()));
        AssertThrows<FormatException>(
            () => HttpMessageEnvelope.Parse("no-separator.txt", Encoding.ASCII.GetBytes(headerText.TrimEnd('\r', '\n'))));
        var originalCulture = UiText.Culture;
        try
        {
            foreach (var cultureName in new[] { "zh-CN", "en-US", "ja-JP" })
            {
                UiText.Culture = CultureInfo.GetCultureInfo(cultureName);
                var error = $"│{BootstrapWorkspace.SeverityLabel(UiSeverity.Error)} [plugin] failure";
                var warning = $"│{BootstrapWorkspace.SeverityLabel(UiSeverity.Warning)} [plugin] warning";
                var info = $"│{BootstrapWorkspace.SeverityLabel(UiSeverity.Info)} [plugin] info";
                var errors = ReplayHarness.VisibleErrors(string.Join('\n', error, warning, info));
                AssertEqual(1, errors.Length);
                AssertEqual(error, errors[0]);
            }
        }
        finally
        {
            UiText.Culture = originalCulture;
        }
        Console.WriteLine("PASS parser and localized visible-error self-test");
    }

    static void AssertEqual<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"Expected {expected}, got {actual}.");
    }

    static void AssertBytes(byte[] expected, byte[] actual)
    {
        if (!expected.SequenceEqual(actual))
            throw new InvalidOperationException($"Expected {Convert.ToHexString(expected)}, got {Convert.ToHexString(actual)}.");
    }

    static void AssertThrows<TException>(Action action)
        where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException)
        {
            return;
        }

        throw new InvalidOperationException($"Expected {typeof(TException).Name}.");
    }
}

enum PacketDirection
{
    Request,
    Response,
}

sealed record HttpMessageEnvelope(
    PacketDirection Direction,
    string CanonicalUrl,
    GameHttpHeaders Headers,
    byte[] Body)
{
    public static HttpMessageEnvelope Parse(string sourceName, byte[] bytes)
    {
        var (headerLength, separatorLength) = FindHeaderSeparator(sourceName, bytes);
        var headerText = Encoding.UTF8.GetString(bytes, 0, headerLength);
        var lines = headerText
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n', StringSplitOptions.None);

        if (lines.Length == 0 || string.IsNullOrWhiteSpace(lines[0]))
            throw new FormatException($"{sourceName}: HTTP request line is empty.");

        var requestLine = lines[0].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (requestLine.Length < 2)
            throw new FormatException($"{sourceName}: invalid HTTP request line: {lines[0]}");

        var direction = requestLine[1] switch
        {
            "/notify/request" => PacketDirection.Request,
            "/notify/response" => PacketDirection.Response,
            var path => throw new FormatException($"{sourceName}: unsupported notify path: {path}"),
        };

        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in lines.Skip(1))
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            var colon = line.IndexOf(':');
            if (colon <= 0)
                throw new FormatException($"{sourceName}: invalid HTTP header line: {line}");

            headers[line[..colon].Trim()] = line[(colon + 1)..].Trim();
        }

        if (!headers.TryGetValue("x-hachimi-game-url", out var canonicalUrl) ||
            string.IsNullOrWhiteSpace(canonicalUrl))
        {
            throw new FormatException($"{sourceName}: missing x-hachimi-game-url header.");
        }

        var bodyOffset = headerLength + separatorLength;
        var body = bytes[bodyOffset..];
        if (!headers.TryGetValue("content-length", out var contentLengthText) ||
            !int.TryParse(contentLengthText, out var contentLength))
        {
            throw new FormatException($"{sourceName}: missing or invalid content-length header.");
        }
        if (contentLength != body.Length)
            throw new FormatException($"{sourceName}: content-length mismatch, header={contentLength}, body={body.Length}.");

        return new(
            direction,
            canonicalUrl,
            new(
                Header(headers, "x-hachimi-sid"),
                Header(headers, "x-hachimi-app-ver"),
                Header(headers, "x-hachimi-res-ver"),
                Header(headers, "x-hachimi-viewerid"),
                Header(headers, "x-hachimi-device"),
                Header(headers, "x-hachimi-device-subtype")),
            body);
    }

    static (int HeaderLength, int SeparatorLength) FindHeaderSeparator(string sourceName, byte[] bytes)
    {
        var index = bytes.AsSpan().IndexOf("\r\n\r\n"u8);
        if (index >= 0)
            return (index, 4);

        index = bytes.AsSpan().IndexOf("\n\n"u8);
        if (index >= 0)
            return (index, 2);

        throw new FormatException($"{sourceName}: HTTP header/body separator was not found.");
    }

    static string? Header(IReadOnlyDictionary<string, string> headers, string name)
        => headers.TryGetValue(name, out var value) && value.Length != 0 ? value : null;
}

static class CommandLine
{
    const string DefaultCorpusPath = @"F:\Desktop\ramen_full_game";

    public static string ResolveCorpusPath(string[] args)
    {
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i].Equals("--corpus", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 >= args.Length)
                    throw new ArgumentException("--corpus requires a path argument.");
                return args[i + 1];
            }

            if (args[i].Length == 0 || args[i][0] != '-')
                return args[i];
        }

        return DefaultCorpusPath;
    }
}

sealed class ReplayHarness(string corpusPath, WorkspaceSmokeSession ui)
{
    const string RamenCommonResponseEndpoints =
        "^/umamusume/single_mode_ramen/(?:change_short_cut|check_event|check_point|continue|exec_command|finish_claw_crane|gain_skills|race_end|race_entry|race_out|ramen_live|select_region|tasting|uraf_effect_apply)$";

    static readonly HashSet<Type> RamenCommonResponseEndpointTypes =
    [
        .. PluginManager.ExpandEndpointPatterns([EndpointPattern.Regex(RamenCommonResponseEndpoints)])
            .Select(endpoint => endpoint.EndpointType),
    ];

    readonly ReplaySummary summary = new(corpusPath);
    readonly ReplayAnalyzerDispatcher dispatcher = new();
    readonly List<LoadedReplayPlugin> plugins = [];
    CorpusFile? pendingRequestFile;
    HttpMessageEnvelope? pendingRequest;

    public async Task<ReplaySummary> RunAsync()
    {
        if (!Directory.Exists(corpusPath))
            throw new DirectoryNotFoundException($"Corpus path does not exist: {corpusPath}");

        var files = EnumerateCorpusFiles(corpusPath);
        summary.TotalFiles = files.Count;
        summary.WorkingDirectory = CreateWorkingDirectory();

        var originalCwd = Directory.GetCurrentDirectory();
        (Workspace Current, string Screen)? visibleBeforeDispose = null;
        try
        {
            Directory.SetCurrentDirectory(summary.WorkingDirectory);
            InitializeHostConfig();
            PrepareHostDatabaseFixtures(files);
            if (await Database.Initialize() != DatabaseAvailability.Ready)
                throw new InvalidOperationException("Minimal Host database fixture failed to initialize.");
            PrepareGamePacketCollectorConfig();
            await InitializePluginsAsync();

            foreach (var file in files)
                await ReplayFileAsync(file);

            FlushPendingPair();
            ReadGamePacketCollectorResult();
            visibleBeforeDispose = CaptureVisibleState();
        }
        finally
        {
            try
            {
                foreach (var loaded in plugins)
                {
                    try
                    {
                        await loaded.Context.StopAsync();
                    }
                    catch (Exception ex)
                    {
                        summary.PluginDisposeErrors.Add(
                            $"{PluginManager.InternalName(loaded.Plugin)} background: {ex.Message}");
                    }
                }

                foreach (var loaded in plugins)
                {
                    try
                    {
                        loaded.Plugin.Dispose();
                    }
                    catch (Exception ex)
                    {
                        summary.PluginDisposeErrors.Add(
                            $"{PluginManager.InternalName(loaded.Plugin)}: {ex.Message}");
                    }
                    finally
                    {
                        loaded.Context.Dispose();
                    }
                }

                if (visibleBeforeDispose is not null)
                {
                    try
                    {
                        AssertDisposedState(visibleBeforeDispose.Value);
                    }
                    catch (Exception ex)
                    {
                        summary.PluginDisposeErrors.Add($"Workspace state: {ex.Message}");
                    }
                }
            }
            finally
            {
                Directory.SetCurrentDirectory(originalCwd);
            }
        }

        return summary;
    }

    async Task ReplayFileAsync(CorpusFile file)
    {
        HttpMessageEnvelope message;
        try
        {
            message = HttpMessageEnvelope.Parse(file.Path, await File.ReadAllBytesAsync(file.Path));
            summary.CountRead(message.Direction);
        }
        catch (Exception ex)
        {
            summary.ParseErrors.Add($"{file.Name}: {ex.Message}");
            return;
        }

        TrackPair(file, message);

        var errorsBefore = VisibleErrors(ui.CaptureScreen());
        try
        {
            var invokedHandlers = await dispatcher.DispatchAsync(message);
            var errorsAfter = VisibleErrors(ui.CaptureScreen());
            if (errorsAfter.Length != 0)
            {
                var newErrors = errorsAfter.Except(errorsBefore, StringComparer.Ordinal).ToArray();
                summary.PluginExceptions.Add(
                    newErrors.Length == 0
                        ? $"{file.Name}: a visible Error state remained after analyzer dispatch; success was not observable."
                        : $"{file.Name}: analyzer dispatch published visible Error state: {string.Join(" | ", newErrors)}");
                return;
            }
            if (invokedHandlers == 0)
            {
                summary.IgnoredPackets++;
            }
            else
            {
                summary.SuccessfulDispatches++;
                summary.HandlerInvocations += invokedHandlers;
            }
        }
        catch (Exception ex)
        {
            if (IsUnrecognizedEndpoint(ex))
            {
                summary.UnrecognizedEndpoints.Add($"{file.Name}: {message.CanonicalUrl}");
                return;
            }

            summary.DispatchExceptions.Add($"{file.Name}: {ex.GetType().Name}: {ex.Message}");
        }
    }

    internal static string[] VisibleErrors(string screen)
        => [.. screen.ReplaceLineEndings("\n")
            .Split('\n')
            .Where(line => line.Contains($"{BootstrapWorkspace.SeverityLabel(UiSeverity.Error)} ", StringComparison.Ordinal))];

    void TrackPair(CorpusFile file, HttpMessageEnvelope message)
    {
        if (message.Direction == PacketDirection.Request)
        {
            if (pendingRequestFile is not null)
                summary.PairErrors.Add($"{pendingRequestFile.Name}: request was not followed by response before {file.Name}.");

            pendingRequestFile = file;
            pendingRequest = message;
            return;
        }

        if (pendingRequestFile is null || pendingRequest is null)
        {
            summary.PairErrors.Add($"{file.Name}: response without preceding request.");
            return;
        }

        summary.Pairs++;
        if (!string.Equals(pendingRequest.CanonicalUrl, message.CanonicalUrl, StringComparison.Ordinal))
        {
            summary.PairErrors.Add(
                $"{pendingRequestFile.Name} + {file.Name}: canonical URL mismatch, request={pendingRequest.CanonicalUrl}, response={message.CanonicalUrl}.");
        }

        if (!string.Equals(pendingRequest.Headers.Sid, message.Headers.Sid, StringComparison.Ordinal))
        {
            summary.PairErrors.Add(
                $"{pendingRequestFile.Name} + {file.Name}: sid mismatch, request={pendingRequest.Headers.Sid}, response={message.Headers.Sid}.");
        }

        pendingRequestFile = null;
        pendingRequest = null;
    }

    void FlushPendingPair()
    {
        if (pendingRequestFile is null)
            return;

        summary.PairErrors.Add($"{pendingRequestFile.Name}: request has no following response.");
        pendingRequestFile = null;
        pendingRequest = null;
    }

    async Task InitializePluginsAsync()
    {
        IPlugin[] candidates =
        [
            new EventLoggerPlugin.EventLoggerPlugin(),
            new EventResponseAnalyzer.EventResponseAnalyzer(),
            new GamePacketCollectorPlugin(),
            new RamenScenarioAnalyzer.RamenScenarioAnalyzer(),
        ];

        foreach (var plugin in candidates)
        {
            var context = new ReplayPluginContext(plugin, ui.Application, dispatcher);
            plugins.Add(new(plugin, context));
            try
            {
                dispatcher.RegisterAttributeAnalyzers(plugin);
                plugin.Initialize(context);
                summary.InitializedPlugins.Add(PluginManager.InternalName(plugin));
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"Plugin initialization failed: {PluginManager.InternalName(plugin)}",
                    ex);
            }
        }

        foreach (var loaded in plugins)
            await loaded.Context.StartAsync();
    }

    void PrepareGamePacketCollectorConfig()
    {
        var dataDirectory = Path.Combine("PluginData", "游戏包采集");
        Directory.CreateDirectory(dataDirectory);
        var configPath = Path.Combine(dataDirectory, "config.json");
        File.WriteAllText(
            configPath,
            JsonSerializer.Serialize(
                new
                {
                    uploadUrl = "http://127.0.0.1:9/PluginReplaySmoke/GamePackets",
                    serverRegionHint = "replay-smoke",
                    enabled = false,
                },
                new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }));
    }

    void ReadGamePacketCollectorResult()
    {
        var dataDirectory = Path.Combine(summary.WorkingDirectory, "PluginData", "游戏包采集");
        var configPath = Path.Combine(dataDirectory, "config.json");
        if (File.Exists(configPath))
        {
            using var document = JsonDocument.Parse(File.ReadAllText(configPath));
            if (document.RootElement.TryGetProperty("enabled", out var enabled))
                summary.GamePacketCollectorUploadEnabled = enabled.ValueKind == JsonValueKind.True;
        }

        var pendingDirectory = Path.Combine(dataDirectory, "pending");
        if (Directory.Exists(pendingDirectory))
            summary.GamePacketCollectorPendingPackets = Directory.GetFiles(pendingDirectory, "*.json", SearchOption.TopDirectoryOnly).Length;

        var sentDirectory = Path.Combine(dataDirectory, "sent");
        if (Directory.Exists(sentDirectory))
            summary.GamePacketCollectorSentPackets = Directory.GetFiles(sentDirectory, "*.json", SearchOption.AllDirectories).Length;
    }

    (Workspace Current, string Screen) CaptureVisibleState()
    {
        var current = Workspace.Current
            ?? throw new InvalidOperationException("Replay finished without a current Workspace.");
        if (!ReferenceEquals(current, Workspace.Create(current.Title)))
            throw new InvalidOperationException("Workspace.Create did not return the canonical current handle.");

        var screen = ui.CaptureScreen();
        if (string.IsNullOrWhiteSpace(screen))
            throw new InvalidOperationException("Replay produced no visible current Workspace in the real Host.");

        summary.VisibleFramebufferCharacters = screen.Count(character => !char.IsWhiteSpace(character));
        summary.CurrentWorkspace = current.Title;
        return (current, screen);
    }

    void AssertDisposedState((Workspace Current, string Screen) before)
    {
        if (!ReferenceEquals(Workspace.Create(before.Current.Title), before.Current))
            throw new InvalidOperationException($"Plugin Dispose removed Workspace generation '{before.Current.Title}'.");

        before.Current.SwitchTo();
        var after = ui.CaptureScreen();
        if (before.Screen.Contains("拉面杯训练", StringComparison.Ordinal)
            && after.Contains("拉面杯训练", StringComparison.Ordinal))
            throw new InvalidOperationException("Plugin Dispose left the replay panel visible.");

        summary.RetainedWorkspacesAfterDispose = 1;
    }

    static void InitializeHostConfig()
        => Config.Initialize();

    static void PrepareHostDatabaseFixtures(IReadOnlyList<CorpusFile> files)
    {
        WriteBrotliJson("events_female.br", "[]");
        WriteBrotliJson("events_male.br", "[]");
        WriteBrotliJson("names.br", BuildNameFixture(files));
        WriteBrotliJson("skill_data.br", "[]");
        WriteBrotliJson("skill_upgrade_speciality.br", "[]");
        WriteBrotliJson("talent_skill_sets.br", "{}");
        WriteBrotliJson("factor_ids.br", "{}");
        WriteBrotliJson("wins_saddle.br", "[]");
        WriteBrotliJson("succession_relation.br", "{}");
    }

    static string BuildNameFixture(IReadOnlyList<CorpusFile> files)
    {
        var supportCardIds = new HashSet<int>();
        foreach (var file in files)
        {
            var message = HttpMessageEnvelope.Parse(file.Path, File.ReadAllBytes(file.Path));
            if (message.Direction != PacketDirection.Response ||
                !Server.TryResolveEndpoint(message.CanonicalUrl, out var endpoint))
            {
                continue;
            }

            var context = new AnalyzerDispatchContext(endpoint, message.Body, message.Headers);
            SingleModeChara? chara = endpoint.Path switch
            {
                "/umamusume/single_mode_ramen/load" =>
                    ((SingleModeRamenLoadResponse)context.GetDto(typeof(SingleModeRamenLoadResponse)))
                    .data?.single_mode_load_common?.chara_info,
                _ when RamenCommonResponseEndpointTypes.Contains(endpoint.EndpointType) =>
                    ((SingleModeRamenExecCommandResponse)context.GetDto(typeof(SingleModeRamenExecCommandResponse)))
                    .data?.chara_info,
                _ => null,
            };
            if (chara?.support_card_array is null)
                continue;
            foreach (var supportCard in chara.support_card_array)
                supportCardIds.Add(supportCard.support_card_id);
        }

        List<BaseName> names =
        [
            .. supportCardIds.Order().Select(id =>
                (BaseName)new SupportCardName(id, $"fixture-{id}", $"S{id}", 0, id)),
        ];
        return Newtonsoft.Json.JsonConvert.SerializeObject(
            names,
            new Newtonsoft.Json.JsonSerializerSettings
            {
                TypeNameHandling = Newtonsoft.Json.TypeNameHandling.All,
            });
    }

    static void WriteBrotliJson(string path, string json)
    {
        using var file = File.Create(path);
        using var brotli = new BrotliStream(file, CompressionLevel.Optimal);
        using var writer = new StreamWriter(brotli, new UTF8Encoding(false));
        writer.Write(json);
    }

    static string CreateWorkingDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "ura-plugin-replay-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    static List<CorpusFile> EnumerateCorpusFiles(string path)
        => Directory
            .EnumerateFiles(path, "*.txt", SearchOption.TopDirectoryOnly)
            .Select(file => new CorpusFile(ReadSequence(Path.GetFileName(file)), Path.GetFileName(file), file))
            .OrderBy(file => file.Sequence)
            .ThenBy(file => file.Name, StringComparer.Ordinal)
            .ToList();

    static int ReadSequence(string fileName)
    {
        if (!fileName.StartsWith("[", StringComparison.Ordinal))
            throw new FormatException($"Corpus filename must start with sequence marker: {fileName}");

        var end = fileName.IndexOf(']');
        if (end <= 1 || !int.TryParse(fileName[1..end], out var sequence))
            throw new FormatException($"Corpus filename has invalid sequence marker: {fileName}");

        return sequence;
    }

    static bool IsUnrecognizedEndpoint(Exception ex)
        => ex is UnrecognizedEndpointException;
}

sealed record CorpusFile(int Sequence, string Name, string Path);

sealed class UnrecognizedEndpointException(string canonicalUrl)
    : Exception($"未识别 Gallop endpoint: {canonicalUrl}");

sealed record LoadedReplayPlugin(IPlugin Plugin, ReplayPluginContext Context);

sealed class ReplayPluginContext(
    IPlugin plugin,
    IApplication application,
    ReplayAnalyzerDispatcher dispatcher) : IPluginContext, IDisposable
{
    readonly ReplayHostEvents events = new();
    readonly CancellationTokenSource lifetime = new();
    readonly List<Task> backgroundTasks = [];

    public IApplication Application { get; } = application;
    public IPluginHostEvents Events => events;
    public IPluginAnalyzerRegistry Analyzers { get; } = new ReplayAnalyzerRegistry(plugin, dispatcher);
    public bool IsPluginAvailable(string internalName) => false;

    public void RunBackground(Func<CancellationToken, ValueTask> operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        backgroundTasks.Add(Task.Run(() => operation(lifetime.Token).AsTask()));
    }

    internal ValueTask StartAsync()
        => events.StartAsync(lifetime.Token);

    internal async ValueTask StopAsync()
    {
        lifetime.Cancel();
        try
        {
            await Task.WhenAll(backgroundTasks).WaitAsync(TimeSpan.FromSeconds(10));
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
        {
        }
    }

    public void Dispose() => lifetime.Dispose();
}

sealed class ReplayHostEvents : IPluginHostEvents
{
    readonly List<Func<CancellationToken, ValueTask>> startedHandlers = [];

    public void OnStarted(Func<CancellationToken, ValueTask> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        startedHandlers.Add(handler);
    }

    internal async ValueTask StartAsync(CancellationToken cancellationToken)
    {
        foreach (var handler in startedHandlers)
            await handler(cancellationToken);
    }
}

sealed class ReplayAnalyzerRegistry(
    IPlugin plugin,
    ReplayAnalyzerDispatcher dispatcher) : IPluginAnalyzerRegistry
{
    public void Register<TPayload>(
        AnalyzerKind kind,
        IReadOnlyList<EndpointPattern> patterns,
        Func<AnalyzerInvocation<TPayload>, ValueTask> handler,
        int priority = 0)
        => dispatcher.Add(plugin, kind, patterns, handler, priority);
}

sealed class ReplayAnalyzerDispatcher
{
    readonly List<AnalyzerRegistration> registrations = [];

    internal void RegisterAttributeAnalyzers(IPlugin plugin)
        => registrations.AddRange(PluginManager.CreateRegistrationPlan(plugin).Analyzers);

    internal void Add<TPayload>(
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
            throw new ArgumentException("Analyzer requires at least one endpoint pattern.", nameof(patterns));

        var payloadType = typeof(TPayload);
        if (payloadType != typeof(ReadOnlyMemory<byte>) &&
            !Gallop.Endpoints.GameEndpointCatalog.ByEndpointType.Values.Any(endpoint =>
                (kind == AnalyzerKind.Request ? endpoint.RequestType : endpoint.ResponseType) == payloadType))
        {
            throw new InvalidOperationException(
                $"Analyzer payload is not a concrete Host Gallop DTO for {kind}: {payloadType.FullName}");
        }

        foreach (var endpoint in PluginManager.ExpandEndpointPatterns(patterns))
        {
            registrations.Add(new(
                plugin,
                null,
                endpoint.EndpointType,
                kind,
                priority,
                payloadType == typeof(ReadOnlyMemory<byte>)
                    ? context => handler(new(
                        endpoint,
                        (TPayload)(object)context.Payload,
                        context.Headers))
                    : context => handler(new(
                        endpoint,
                        (TPayload)context.GetDto(typeof(TPayload)),
                        context.Headers)),
                $"replay programmatic {payloadType.FullName} analyzer"));
        }
    }

    internal async ValueTask<int> DispatchAsync(HttpMessageEnvelope message)
    {
        if (!Server.TryResolveEndpoint(message.CanonicalUrl, out var endpoint))
            throw new UnrecognizedEndpointException(message.CanonicalUrl);

        var kind = message.Direction == PacketDirection.Request
            ? AnalyzerKind.Request
            : AnalyzerKind.Response;
        var matched = registrations
            .Where(registration =>
                registration.Kind == kind &&
                registration.EndpointType == endpoint.EndpointType)
            .OrderBy(registration => registration.Priority)
            .ToList();
        if (matched.Count == 0)
            return 0;

        var context = new AnalyzerDispatchContext(endpoint, message.Body, message.Headers);
        foreach (var registration in matched)
            await registration.Handler(context);
        return matched.Count;
    }
}

sealed class ReplaySummary(string corpusPath)
{
    public string CorpusPath { get; } = corpusPath;
    public string WorkingDirectory { get; set; } = string.Empty;
    public int TotalFiles { get; set; }
    public int RequestMessages { get; private set; }
    public int ResponseMessages { get; private set; }
    public int Pairs { get; set; }
    public int SuccessfulDispatches { get; set; }
    public int IgnoredPackets { get; set; }
    public int HandlerInvocations { get; set; }
    public bool GamePacketCollectorUploadEnabled { get; set; }
    public int GamePacketCollectorPendingPackets { get; set; }
    public int GamePacketCollectorSentPackets { get; set; }
    public int VisibleFramebufferCharacters { get; set; }
    public int RetainedWorkspacesAfterDispose { get; set; }
    public string? CurrentWorkspace { get; set; }
    public List<string> InitializedPlugins { get; } = [];
    public List<string> ParseErrors { get; } = [];
    public List<string> PairErrors { get; } = [];
    public List<string> UnrecognizedEndpoints { get; } = [];
    public List<string> DispatchExceptions { get; } = [];
    public List<string> PluginExceptions { get; } = [];
    public List<string> PluginDisposeErrors { get; } = [];

    public bool HasFailures =>
        TotalFiles == 0 ||
        RequestMessages == 0 ||
        ResponseMessages == 0 ||
        Pairs == 0 ||
        SuccessfulDispatches == 0 ||
        HandlerInvocations == 0 ||
        ParseErrors.Count != 0 ||
        PairErrors.Count != 0 ||
        UnrecognizedEndpoints.Count != 0 ||
        DispatchExceptions.Count != 0 ||
        PluginExceptions.Count != 0 ||
        PluginDisposeErrors.Count != 0;

    public void CountRead(PacketDirection direction)
    {
        if (direction == PacketDirection.Request)
            RequestMessages++;
        else
            ResponseMessages++;
    }

    public void WriteTo(TextWriter writer)
    {
        writer.WriteLine("PluginReplaySmoke summary");
        writer.WriteLine($"Corpus: {CorpusPath}");
        writer.WriteLine($"Working directory: {WorkingDirectory}");
        writer.WriteLine($"Files: {TotalFiles}");
        writer.WriteLine($"Read: request={RequestMessages}, response={ResponseMessages}, pairs={Pairs}, pairErrors={PairErrors.Count}");
        writer.WriteLine($"Dispatch: handledPackets={SuccessfulDispatches}, ignoredPackets={IgnoredPackets}, handlerInvocations={HandlerInvocations}, unrecognizedEndpoint={UnrecognizedEndpoints.Count}, dispatchException={DispatchExceptions.Count}, pluginException={PluginExceptions.Count}");
        writer.WriteLine($"Plugins: {string.Join(", ", InitializedPlugins)}");
        writer.WriteLine($"GamePacketCollector: uploadEnabled={GamePacketCollectorUploadEnabled}, pending={GamePacketCollectorPendingPackets}, sent={GamePacketCollectorSentPackets}");
        writer.WriteLine($"Workspace: visibleFramebufferCharacters={VisibleFramebufferCharacters}, current={CurrentWorkspace}, retainedAfterDispose={RetainedWorkspacesAfterDispose}");
        WriteList(writer, "Parse errors", ParseErrors);
        WriteList(writer, "Pair errors", PairErrors);
        WriteList(writer, "Unrecognized endpoints", UnrecognizedEndpoints);
        WriteList(writer, "Dispatch exceptions", DispatchExceptions);
        WriteList(writer, "Plugin exceptions", PluginExceptions);
        WriteList(writer, "Plugin dispose errors", PluginDisposeErrors);
    }

    static void WriteList(TextWriter writer, string title, IReadOnlyList<string> values)
    {
        if (values.Count == 0)
            return;

        writer.WriteLine($"{title}:");
        foreach (var value in values.Take(20))
            writer.WriteLine($"  - {value}");
        if (values.Count > 20)
            writer.WriteLine($"  - ... {values.Count - 20} more");
    }
}
