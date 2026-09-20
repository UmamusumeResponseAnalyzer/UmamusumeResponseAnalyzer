using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Text.Json;
using Gallop;
using Gallop.Endpoints;
using MessagePack;
using Terminal.Gui.App;
using UmamusumeResponseAnalyzer;
using UmamusumeResponseAnalyzer.Plugin;
using UmamusumeResponseAnalyzer.TerminalGui;

static class PackageLoadSmoke
{
    static readonly GameHttpHeaders Headers = new("zip-smoke-sid", "zip-smoke-app", "zip-smoke-res", "zip-smoke-viewer", null, null);

    public static void Run(TargetAssembly[] targets, WorkspaceSmokeSession ui, Func<Task> initializeDatabase)
    {
        using var directory = new TempCurrentDirectory("seven-zip-business");
        initializeDatabase().GetAwaiter().GetResult();
        Directory.CreateDirectory("Plugins");
        foreach (var target in targets)
        {
            File.Copy(PluginSmokeRunner.FindPackagePath(target), Path.Combine("Plugins", target.AssemblyName + ".zip"));
        }
        PackageLegendFixture.WriteLegendBuffCsv();
        var references = RunLoaded(targets, ui);
        for (var attempt = 0; attempt < 20 && references.Any(item => item.Reference.IsAlive); attempt++)
        {
            ui.Flush();
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            Thread.Sleep(50);
        }
        Require(references.All(item => !item.Reference.IsAlive),
            $"ZIP unload retained references: {string.Join(", ", references.Where(item => item.Reference.IsAlive).Select(item => item.Label))}.");
        Console.WriteLine("PASS ZIP business: plugins=7; drained registrations, panels and collectible contexts released");
    }

    // A separate stack frame prevents the inspection locals from rooting a collectible plugin during GC.
    [MethodImpl(MethodImplOptions.NoInlining)]
    static (string Label, WeakReference Reference)[] RunLoaded(TargetAssembly[] targets, WorkspaceSmokeSession ui)
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var uploadUrl = $"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}/GamePackets";
        var dataDirectory = Path.Combine("PluginData", "游戏包采集");
        Directory.CreateDirectory(dataDirectory);
        File.WriteAllText(Path.Combine(dataDirectory, "config.json"), JsonSerializer.Serialize(new
        {
            uploadUrl,
            enabled = true,
            serverRegionHint = "zip-smoke",
            endpointGroups = new[] { "single-mode", "gacha" }
        }));
        var upload = ReceiveUpload(listener);
        var weak = new List<(string Label, WeakReference Reference)>();
        var eventLogger = default(IPlugin);
        try
        {
            PluginManager.Init();
            PluginManager.InitializeLoadedPlugins();
            PluginManager.TriggerStartedAsync().GetAwaiter().GetResult();
            Require(PluginManager.FailedPlugins.Count == 0 && PluginManager.LoadedPlugins.Count == targets.Length,
                $"Expected seven loaded ZIP plugins; actual={PluginManager.LoadedPlugins.Count}; failed={string.Join(",", PluginManager.FailedPlugins)}");
            foreach (var target in targets)
            {
                var plugin = PluginManager.LoadedPlugins.Single(plugin => PluginManager.InternalName(plugin) == target.AssemblyName);
                if (target.AssemblyName == "EventLoggerPlugin")
                    eventLogger = plugin;
                var assembly = plugin.GetType().Assembly;
                var context = AssemblyLoadContext.GetLoadContext(assembly)!;
                var metadata = PluginManager.Metadatas[target.AssemblyName];
                Require(metadata.PackagePath == Path.GetFullPath(Path.Combine("Plugins", target.AssemblyName + ".zip")),
                    $"{target.AssemblyName} metadata did not identify the isolated fresh ZIP.");
                using var archive = ZipFile.OpenRead(PluginSmokeRunner.FindPackagePath(target));
                using var stream = archive.GetEntry(metadata.AssemblyEntry)!.Open();
                using var bytes = new MemoryStream();
                stream.CopyTo(bytes);
                bytes.Position = 0;
                using var pe = new PEReader(bytes);
                var reader = pe.GetMetadataReader();
                var mvid = reader.GetGuid(reader.GetModuleDefinition().Mvid);
                Require(context.IsCollectible && context != AssemblyLoadContext.Default && assembly.ManifestModule.ModuleVersionId == mvid,
                    $"{target.AssemblyName} did not execute the fresh ZIP main DLL in a collectible context.");
                foreach (var shared in new[] { typeof(IPlugin).Assembly, typeof(SingleModeChara).Assembly, typeof(IApplication).Assembly })
                    Require(ReferenceEquals(context.LoadFromAssemblyName(shared.GetName()), shared),
                        $"{target.AssemblyName} duplicated shared host identity {shared.GetName().Name}.");
                weak.Add(($"{target.AssemblyName} instance", new(plugin)));
                weak.Add(($"{target.AssemblyName} context", new(context)));
                Console.WriteLine($"PASS ZIP origin {target.AssemblyName}: MVID={mvid}, PackagePath={metadata.PackagePath}");
            }
            foreach (var group in PluginManager.ContextGroups)
            {
                var assemblies = PluginManager.LoadedPlugins.Where(plugin => group.Contains(PluginManager.InternalName(plugin)))
                    .Select(plugin => AssemblyLoadContext.GetLoadContext(plugin.GetType().Assembly)).Distinct().ToArray();
                Require(assemblies.Length == 1, "Soft-linked ZIP plugins did not share one load context.");
            }
            weak.AddRange(PluginManager.RequestAnalyzerMethods.Concat(PluginManager.ResponseAnalyzerMethods)
                .Select(registration => ($"{PluginManager.InternalName(registration.Plugin)} callback", new WeakReference(registration.Handler))));

            AssertCaptureUpload(upload);
            AssertLegendAndAir(ui);
            AssertEventLogger(ui);
            AssertEventChoices(ui);
            AssertRamenAndStatus(ui);
        }
        finally
        {
            PluginManager.ShutdownAsync().GetAwaiter().GetResult();
            ui.Flush();
        }
        Require(PluginManager.LoadedPlugins.Count == 0 && PluginManager.Contexts.Count == 0
            && PluginManager.RequestAnalyzerMethods.Count == 0 && PluginManager.ResponseAnalyzerMethods.Count == 0,
            "ZIP shutdown left plugin instances or analyzer registrations published.");
        Require(eventLogger!.GetType().Assembly.GetType("EventLoggerPlugin.EventLoggerDisplay", true)!
            .GetField("workspace", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null) is null,
            "EventLogger Dispose retained its workspace reference.");
        foreach (var (workspace, marker) in new[]
        {
            ("LegendScenarioAnalyzer", "心得等级"), ("RamenScenarioAnalyzer", "URAF"),
            ("EventResponseAnalyzer", "ZIP 选择事件")
        })
        {
            Workspace.Create(workspace).SwitchTo();
            Require(!ui.CaptureScreen(200, 80).Contains(marker, StringComparison.Ordinal), $"ZIP shutdown left {workspace} output mounted.");
        }
        ui.Bootstrap.SwitchTo();
        return [.. weak];
    }

    static void AssertCaptureUpload(Task<(string Method, string Json)> upload)
    {
        var endpoint = GameEndpointCatalog.ByPath.Values.First(endpoint => endpoint.Path.StartsWith("/umamusume/gacha/", StringComparison.Ordinal)
            && PluginManager.RequestAnalyzerMethods.Any(registration => registration.EndpointType == endpoint.EndpointType));
        var request = MessagePackSerializer.ConvertFromJson("{\"zipSmoke\":\"request\"}");
        var response = MessagePackSerializer.ConvertFromJson("{\"zipSmoke\":\"response\"}");
        Dispatch(AnalyzerKind.Request, endpoint, request);
        Dispatch(AnalyzerKind.Response, endpoint, response);
        var received = upload.GetAwaiter().GetResult();
        using var json = JsonDocument.Parse(received.Json);
        var root = json.RootElement;
        Require(received.Method == "PUT", "Collector did not issue PUT.");
        foreach (var (property, expected) in new[]
        {
            ("schemaVersion", "2"), ("kind", "game-packet"), ("endpointPath", endpoint.Path),
            ("group", "gacha"), ("sid", Headers.Sid), ("appVersion", Headers.AppVer),
            ("gameDataVersion", Headers.ResVer), ("viewerId", Headers.ViewerId), ("serverRegionHint", "zip-smoke")
        })
            Require(root.GetProperty(property).GetString() == expected, $"Collector PUT field {property} did not preserve its input.");
        Require(root.GetProperty("request").GetBytesFromBase64().SequenceEqual(request)
            && root.GetProperty("response").GetBytesFromBase64().SequenceEqual(response)
            && !string.IsNullOrEmpty(root.GetProperty("packetIdemKey").GetString()), "Collector PUT lost raw packet bytes or identity.");
        var pending = Path.Combine("PluginData", "游戏包采集", "pending");
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (Directory.EnumerateFiles(pending).Any() && DateTime.UtcNow < deadline)
            Thread.Sleep(10);
        Require(!Directory.EnumerateFiles(pending).Any(), "Collector did not clear successfully uploaded pending data.");
        Console.WriteLine("PASS ZIP business GamePacketCollector: real request/response capture, loopback PUT, pending cleanup");
    }

    static void AssertLegendAndAir(WorkspaceSmokeSession ui)
    {
        var response = PackageLegendFixture.CreateLegendCheckEventResponse(singleModeCharaId: 901, turn: 2);
        PrepareChara(response.data.chara_info, (int)ScenarioType.Legend);
        DispatchResponse(typeof(GameApi.SingleModeLegend.CheckEvent), response);
        AssertPanel(ui, "LegendScenarioAnalyzer", "速度", "耐力", "力量", "心得等级", "100", "110", "120");
        var air = PluginManager.LoadedPlugins.Single(plugin => PluginManager.InternalName(plugin) == "AIRedirector");
        var config = Member(air, "config");
        foreach (var scenario in new[] { "UAF", "Cook", "Mecha", "Legend" })
            Require(Equals(Member(config, scenario), false), $"AIR {scenario} unexpectedly enabled an external process.");
        var bridge = Member(air, "legendOutput");
        var target = Member(bridge, "targetId");
        Require(Equals(Member(target, "SingleModeCharaId"), 901) && Equals(Member(target, "Turn"), 2),
            "AIR ZIP callback did not update its Legend output target.");
        Require(!((System.Collections.IEnumerable)Member(air, "startedProcesses")).Cast<object>().Any(), "AIR ZIP smoke started a process.");
        var load = PackageLegendFixture.CreateLegendLoadResponse();
        PrepareChara(load.data.single_mode_load_common.chara_info, (int)ScenarioType.Legend);
        load.data.single_mode_load_common.chara_info.single_mode_chara_id = 902;
        load.data.single_mode_load_common.chara_info.speed = 321;
        DispatchResponse(typeof(GameApi.SingleModeLegend.Load), load);
        AssertPanel(ui, "LegendScenarioAnalyzer", "心得等级", "321", "110");
        target = Member(bridge, "targetId");
        Require(Equals(Member(target, "SingleModeCharaId"), 902), "AIR load callback did not advance its target identity.");
        Console.WriteLine("PASS ZIP business LegendScenarioAnalyzer: CheckEvent/Load training panels");
        Console.WriteLine("PASS ZIP business AIRedirector: Legend target 901/2 -> 902/2; process switches remain false");
    }

    static void AssertEventLogger(WorkspaceSmokeSession ui)
    {
        var response = PackageLegendFixture.CreateLegendCheckEventResponse(singleModeCharaId: 902, turn: 3);
        PrepareChara(response.data.chara_info, (int)ScenarioType.Legend);
        DispatchResponse(typeof(GameApi.SingleModeLegend.CheckEvent), response);
        var assembly = PluginManager.LoadedPlugins.Single(plugin => PluginManager.InternalName(plugin) == "EventLoggerPlugin").GetType().Assembly;
        var state = assembly.GetType("EventLoggerPlugin.EventLogger", true)!.GetProperty("Current")!.GetValue(null)!;
        Require(Equals(Member(state, "CurrentTurn"), 3) && Equals(Member(state, "Scenario"), (int)ScenarioType.Legend),
            "EventLogger ZIP did not update its current turn and scenario.");
        DispatchResponse(typeof(GameApi.SingleMode.ExecCommand), new SingleModeExecCommandResponse
        {
            data = new()
            {
                command_result = new() { result_state = 1 },
                chara_info = response.data.chara_info,
                home_info = response.data.home_info,
                unchecked_event_array = []
            }
        });
        AssertPanel(ui, "事件记录", "训练失败！", "WARN");
        ui.Bootstrap.SwitchTo();
        Require(!ui.CaptureScreen().Contains("训练失败", StringComparison.Ordinal), "EventLogger warning escaped its workspace.");
        Console.WriteLine("PASS ZIP business EventLoggerPlugin: turn/scenario state and workspace training-failure warning");
    }

    static void AssertEventChoices(WorkspaceSmokeSession ui)
    {
        var chara = PackageLegendFixture.CreateChara(singleModeCharaId: 903, turn: 4);
        PrepareChara(chara, 1);
        DispatchResponse(typeof(GameApi.SingleMode.CheckEvent), new SingleModeCheckEventResponse
        {
            data = new()
            {
                chara_info = chara,
                unchecked_event_array = [new()
                {
                    story_id = 501143706,
                    event_contents_info = new() { choice_array = [new() { select_index_info_array = [new() { select_index = 1 }] }] }
                }]
            }
        });
        AssertPanel(ui, "EventResponseAnalyzer", "ZIP 选择事件", "ZIP 选项", "速度 +10");
        Console.WriteLine("PASS ZIP business EventResponseAnalyzer: known event, option and effect panel");
    }

    static void AssertRamenAndStatus(WorkspaceSmokeSession ui)
    {
        var response = PackageRamenFixture.CreateRamenLoadResponse();
        var chara = response.data.single_mode_load_common.chara_info;
        PrepareChara(chara, 14);
        chara.single_mode_chara_id = 904;
        response.data.ramen_data_set_load = new()
        {
            selected_region_id_array = [1, 2, 3],
            reduce_base_turn_info_array = [new() { feeling_id = 1, reduce_base_turn = 4 }],
            last_tasting_info = new() { region_id = 4 },
            check_point_pt = 123,
            expected_check_point_pt = 456
        };
        DispatchResponse(typeof(GameApi.SingleModeRamen.Load), response, ui);
        AssertPanel(ui, "RamenScenarioAnalyzer", "URAF", "速度", "100", "110");
        var assembly = PluginManager.LoadedPlugins.Single(plugin => PluginManager.InternalName(plugin) == "RamenScenarioAnalyzer").GetType().Assembly;
        var state = assembly.GetType("RamenScenarioAnalyzer.RamenScenarioState", true)!.GetMethod("Snapshot")!.Invoke(null, null)!;
        Require(Equals(Member(state, "loaded"), true) && Equals(Member(state, "single_mode_chara_id"), 904)
            && Equals(Member(state, "check_point_pt"), 123)
            && ((int[])Member(state, "selected_region_id_array")).SequenceEqual([1, 2, 3]),
            "Ramen ZIP did not retain the load fixture state.");
        var output = Path.Combine("PluginData", "SendGameStatusPlugin");
        var currentText = File.ReadAllText(Path.Combine(output, "thisTurn.json"));
        using var json = JsonDocument.Parse(currentText);
        var root = json.RootElement;
        var game = root.GetProperty("baseGame");
        var ramen = root.GetProperty("ramen");
        Require(game.GetProperty("single_mode_chara_id").GetInt32() == 904
            && game.GetProperty("scenarioId").GetInt32() == 14 && game.GetProperty("source").GetString() == "load"
            && ramen.GetProperty("last_ramen").GetInt32() == 3 && ramen.GetProperty("special_feeling").GetInt32() == 2
            && ramen.GetProperty("scenario_pt").GetInt32() == 123
            && ramen.GetProperty("selected_regions").EnumerateArray().Select(value => value.GetInt32()).SequenceEqual([0, 1, 2]),
            "SendGameStatus ZIP output did not preserve character, source and Ramen state.");
        var numbered = Path.Combine(output, "game904_turn1.json");
        Require(File.ReadAllText(numbered) == currentText, "SendGameStatus numbered snapshot differs from thisTurn.json.");
        Console.WriteLine("PASS ZIP business RamenScenarioAnalyzer: state, character and URAF panel");
        Console.WriteLine("PASS ZIP business SendGameStatusPlugin: thisTurn.json + numbered snapshot, shared Ramen state");
    }

    static void PrepareChara(SingleModeChara chara, int scenario)
    {
        // The status-writer fixture requires a valid character and six evaluation slots.
        chara.state = 1;
        chara.scenario_id = scenario;
        chara.card_id = 100100;
        chara.rarity = 5;
        chara.skill_array = [];
        chara.skill_tips_array = [];
        chara.skill_upgrade_info_array = [];
        chara.chara_effect_id_array = [];
        chara.evaluation_info_array = [.. Enumerable.Range(1, 6).Select(id => new EvaluationInfo { target_id = id, evaluation = 80 })];
    }

    static void DispatchResponse(Type endpointType, object response, WorkspaceSmokeSession? ui = null)
        => Dispatch(AnalyzerKind.Response, GameEndpointCatalog.ByEndpointType[endpointType], MessagePackSerializer.Serialize(response.GetType(), response), ui);

    static void Dispatch(AnalyzerKind kind, GameEndpointDescriptor endpoint, byte[] payload, WorkspaceSmokeSession? ui = null)
    {
        using var registrations = PluginManager.SnapshotAnalyzerRegistrations(kind, endpoint.EndpointType);
        Require(registrations.Count != 0, $"No production registrations matched {kind} {endpoint.Path}.");
        var context = new AnalyzerDispatchContext(endpoint, payload, Headers);
        for (var index = 0; index < registrations.Count; index++)
        {
            var registration = registrations[index];
            using var owner = HotkeyManager.RegisterScope(registration.Plugin);
            var checkStatusWorkspace = ui is not null && PluginManager.InternalName(registration.Plugin) == "SendGameStatusPlugin";
            var workspace = Workspace.Current;
            var screen = checkStatusWorkspace ? ui!.CaptureScreen(200, 80) : null;
            registration.Handler(context).GetAwaiter().GetResult();
            if (checkStatusWorkspace)
                Require(ReferenceEquals(workspace, Workspace.Current) && screen == ui!.CaptureScreen(200, 80),
                    "SendGameStatus ZIP callback changed the workspace or framebuffer.");
        }
    }

    static void AssertPanel(WorkspaceSmokeSession ui, string title, params string[] expected)
    {
        Workspace.Create(title).SwitchTo();
        var screen = ui.CaptureScreen(200, 80);
        foreach (var token in expected)
            Require(screen.Contains(token, StringComparison.Ordinal), $"{title} panel omitted '{token}':\n{screen}");
    }

    // Inspection only: handlers are always entered through the production registration snapshot.
    static object Member(object instance, string name)
    {
        var type = instance.GetType();
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        return (type.GetProperty(name, flags)?.GetValue(instance) ?? type.GetField(name, flags)?.GetValue(instance))
            ?? throw new InvalidOperationException($"Missing observable state {type.FullName}.{name}.");
    }

    static async Task<(string Method, string Json)> ReceiveUpload(TcpListener listener)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using var client = await listener.AcceptTcpClientAsync(timeout.Token);
        using var stream = client.GetStream();
        using var reader = new StreamReader(stream, leaveOpen: true);
        var requestLine = await reader.ReadLineAsync(timeout.Token) ?? throw new IOException("Missing HTTP request line.");
        var contentLength = 0;
        while (await reader.ReadLineAsync(timeout.Token) is { Length: > 0 } header)
            if (header.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
                contentLength = int.Parse(header[15..].Trim());
        Require(contentLength > 0, "Collector PUT has no body.");
        var body = new char[contentLength];
        Require(await reader.ReadBlockAsync(body.AsMemory(), timeout.Token) == contentLength, "Truncated collector PUT body.");
        await stream.WriteAsync("HTTP/1.1 200 OK\r\nContent-Length: 0\r\nConnection: close\r\n\r\n"u8.ToArray(), timeout.Token);
        return (requestLine.Split(' ')[0], new string(body));
    }

    static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
