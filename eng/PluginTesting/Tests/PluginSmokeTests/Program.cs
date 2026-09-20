using System.Drawing;
using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Gallop;
using Gallop.Endpoints;
using MessagePack;
using Newtonsoft.Json;
using Terminal.Gui.App;
using Terminal.Gui.Drivers;
using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.Text;
using Terminal.Gui.Time;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;
using UmamusumeResponseAnalyzer;
using UmamusumeResponseAnalyzer.Entities;
using UmamusumeResponseAnalyzer.TerminalGui;
using UmamusumeResponseAnalyzer.Plugin;
using EventI18n = EventResponseAnalyzer.i18n.ParseSingleModeCheckEventResponse;
using TAttribute = Terminal.Gui.Drawing.Attribute;
using TColor = Terminal.Gui.Drawing.Color;

var targets = new[]
{
    new TargetAssembly(
        "AIRedirector",
        static () => new AIRedirector.AIRedirector(),
        ExpectedManifestDependencies: ["LegendScenarioAnalyzer"]),
    new TargetAssembly(
        "EventLoggerPlugin",
        static () => new EventLoggerPlugin.EventLoggerPlugin(),
        ExpectedWorkspaceTitle: "事件记录",
        ExpectedManifestDependencies: ["LegendScenarioAnalyzer", "RamenScenarioAnalyzer"]),
    new TargetAssembly(
        "EventResponseAnalyzer",
        static () => new EventResponseAnalyzer.EventResponseAnalyzer(),
        ExpectedPanelKey: "events",
        ExpectedWorkspaceTitle: "EventResponseAnalyzer"),
    new TargetAssembly(
        "GamePacketCollector",
        static () => new GamePacketCollector.GamePacketCollectorPlugin()),
    new TargetAssembly(
        "LegendScenarioAnalyzer",
        static () => new LegendScenarioAnalyzer.LegendScenarioAnalyzer(),
        ExpectedSwitchedWorkspaceTitle: "LegendScenarioAnalyzer"),
    new TargetAssembly(
        "RamenScenarioAnalyzer",
        static () => new RamenScenarioAnalyzer.RamenScenarioAnalyzer(),
        ExpectedPanelKey: "training",
        ExpectedSwitchedWorkspaceTitle: "RamenScenarioAnalyzer"),
    new TargetAssembly(
        "SendGameStatusPlugin",
        static () => new SendGameStatusPlugin.SendGameStatusPlugin(),
        ExpectedManifestDependencies: ["EventLoggerPlugin", "RamenScenarioAnalyzer"]),
};

var failures = new List<string>();
var summaries = new List<PluginRunSummary>();
PluginSmokeRunner.ValidatePackageRoot(targets);
using var ui = new WorkspaceSmokeSession();
if (args is ["--package-load"])
{
    try
    {
        PackageLoadSmoke.Run(targets, ui, async () =>
        {
            InitializeHostConfigForSmoke();
            await InitializeSmokeDatabase([new Story
            {
                Id = 501143706,
                TriggerName = "ZIP 角色",
                Name = "ZIP 选择事件",
                Choices = [[new Choice { Option = "ZIP 选项", SuccessEffect = "速度 +10" }]]
            }]);
        });
    }
    catch (Exception exception)
    {
        Console.Error.WriteLine($"FAIL ZIP business: {exception}");
        Environment.ExitCode = 1;
    }
    return;
}
if (args.Length != 0)
    throw new ArgumentException("Usage: PluginSmokeTests [--package-load]");

AssertReleaseAnalyzersDoNotRoundTripThroughJson();
AssertGamePacketCollectorCatalog();
AssertPluginUiContract();

using var workspace = TempWorkspace.Create();
var originalCwd = Directory.GetCurrentDirectory();

try
{
    var eventResponseRunDirectory = Path.Combine(workspace.Path, "EventResponseAnalyzerExecCommand");
    Directory.CreateDirectory(eventResponseRunDirectory);
    Directory.SetCurrentDirectory(eventResponseRunDirectory);
    InitializeHostConfigForSmoke();
    AssertEventResponseAnalyzerEndpointCoverage(ui.Application);
    AssertEventResponseAnalyzerPriorityAfterLegendScenarioAnalyzer(ui.Application);
    await AssertEventResponseAnalyzerDisplayStates(ui);
    await AssertEventResponseAnalyzerHistory(ui);
    await AssertEventResponseAnalyzerLegendExecCommandRendersEventPanel(ui);
    Directory.SetCurrentDirectory(originalCwd);

    foreach (var target in targets)
    {
        var runDirectory = Path.Combine(workspace.Path, target.AssemblyName);
        Directory.CreateDirectory(runDirectory);

        Directory.SetCurrentDirectory(runDirectory);
        try
        {
            InitializeHostConfigForSmoke();
            await InitializeSmokeDatabase();
            PrepareSmokeSideEffectGuards(target);
            var summary = await PluginSmokeRunner.Run(target, ui);
            AssertSmokeSideEffectGuards(target);
            summaries.Add(summary);
            Console.WriteLine(
                $"PASS {target.AssemblyName}: plugins={summary.PluginCount}, attributeAnalyzers={summary.AttributeAnalyzerCount}, registeredAnalyzers={summary.RegisteredAnalyzerCount}");
        }
        catch (Exception ex)
        {
            failures.Add($"{target.AssemblyName}: {ex}");
            Console.Error.WriteLine($"FAIL {target.AssemblyName}");
            Console.Error.WriteLine(ex);
        }
        finally
        {
            Directory.SetCurrentDirectory(originalCwd);
        }
    }
}
finally
{
    Directory.SetCurrentDirectory(originalCwd);
}

if (failures.Count != 0)
{
    Console.Error.WriteLine();
    Console.Error.WriteLine($"FAILED plugin smoke tests: {failures.Count}");
    foreach (var failure in failures)
    {
        Console.Error.WriteLine();
        Console.Error.WriteLine(failure);
    }

    Environment.Exit(1);
}

Console.WriteLine();
Console.WriteLine(
    $"PASS source plugin smoke tests: assemblies={targets.Length}, plugins={summaries.Sum(x => x.PluginCount)}, attributeAnalyzers={summaries.Sum(x => x.AttributeAnalyzerCount)}, registeredAnalyzers={summaries.Sum(x => x.RegisteredAnalyzerCount)}");

static void AssertPluginUiContract()
{
    if (typeof(IPluginContext).GetProperty(nameof(IPluginContext.Application))?.PropertyType != typeof(IApplication))
        throw new InvalidOperationException("IPluginContext.Application must expose the Host IApplication.");

    var methods = typeof(IPlugin).GetMethods().Where(x => x.Name == nameof(IPlugin.ConfigPromptAsync)).ToArray();
    if (methods is not [{ ReturnType: var returnType }] || returnType != typeof(Task))
        throw new InvalidOperationException("IPlugin must expose exactly one Task-returning ConfigPromptAsync method.");

    var parameters = methods[0].GetParameters();
    if (parameters.Length != 2
        || parameters[0].ParameterType != typeof(IApplication)
        || parameters[1].ParameterType != typeof(CancellationToken)
        || !parameters[1].IsOptional)
    {
        throw new InvalidOperationException(
            "IPlugin.ConfigPromptAsync must accept (IApplication, CancellationToken).");
    }
}

static void AssertReleaseAnalyzersDoNotRoundTripThroughJson()
{
    var repoRoot = PluginSmokeRunner.FindRepositoryRoot();
    var analyzerSources = new[]
    {
        Path.Combine(repoRoot, "EventLoggerPlugin", "Class1.cs"),
        Path.Combine(repoRoot, "EventResponseAnalyzer", "Class1.cs"),
    };
    var forbiddenPatterns = new[]
    {
        "MessagePackSerializer.ConvertToJson",
        "JsonConvert.DeserializeObject<SingleMode",
        ".ToObject<SingleMode",
        "JObject.Parse",
    };

    foreach (var sourcePath in analyzerSources)
    {
        var source = File.ReadAllText(sourcePath);
        foreach (var pattern in forbiddenPatterns)
        {
            if (source.Contains(pattern, StringComparison.Ordinal))
                throw new InvalidOperationException(
                    $"{Path.GetRelativePath(repoRoot, sourcePath)} must consume Gallop DTOs directly; forbidden analyzer JSON bridge found: {pattern}");
        }
    }
}

static void AssertEventResponseAnalyzerEndpointCoverage(IApplication application)
{
    var plugin = new EventResponseAnalyzer.EventResponseAnalyzer();
    using var context = new RuntimePluginContext(application);
    plugin.Initialize(context);
    if (context.AnalyzerRegistry.Registrations.Count != 2)
        throw new InvalidOperationException(
            $"EventResponseAnalyzer must use exactly 2 programmatic registrations, actual={context.AnalyzerRegistry.Registrations.Count}.");

    var actual = context.AnalyzerRegistry.Registrations
        .Where(registration => registration.Kind == AnalyzerKind.Response)
        .SelectMany(registration => PluginManager.ExpandEndpointPatterns(registration.Patterns))
        .Select(endpoint => endpoint.EndpointType)
        .Distinct()
        .OrderBy(type => type.FullName, StringComparer.Ordinal)
        .ToArray();

    var expected = new[]
    {
        typeof(GameApi.SingleMode.CheckEvent),
        typeof(GameApi.SingleMode.ExecCommand),
        typeof(GameApi.SingleMode.Load),
        typeof(GameApi.SingleModeArc.CheckEvent),
        typeof(GameApi.SingleModeArc.ExecCommand),
        typeof(GameApi.SingleModeArc.Load),
        typeof(GameApi.SingleModeBreeders.CheckEvent),
        typeof(GameApi.SingleModeBreeders.ExecCommand),
        typeof(GameApi.SingleModeBreeders.Load),
        typeof(GameApi.SingleModeCook.CheckEvent),
        typeof(GameApi.SingleModeCook.ExecCommand),
        typeof(GameApi.SingleModeCook.Load),
        typeof(GameApi.SingleModeFree.CheckEvent),
        typeof(GameApi.SingleModeFree.ExecCommand),
        typeof(GameApi.SingleModeFree.Load),
        typeof(GameApi.SingleModeLegend.CheckEvent),
        typeof(GameApi.SingleModeLegend.ExecCommand),
        typeof(GameApi.SingleModeLegend.Load),
        typeof(GameApi.SingleModeLive.CheckEvent),
        typeof(GameApi.SingleModeLive.ExecCommand),
        typeof(GameApi.SingleModeLive.Load),
        typeof(GameApi.SingleModeMecha.CheckEvent),
        typeof(GameApi.SingleModeMecha.ExecCommand),
        typeof(GameApi.SingleModeMecha.Load),
        typeof(GameApi.SingleModeOnsen.CheckEvent),
        typeof(GameApi.SingleModeOnsen.ExecCommand),
        typeof(GameApi.SingleModeOnsen.Load),
        typeof(GameApi.SingleModePioneer.CheckEvent),
        typeof(GameApi.SingleModePioneer.ExecCommand),
        typeof(GameApi.SingleModePioneer.Load),
        typeof(GameApi.SingleModeRamen.CheckEvent),
        typeof(GameApi.SingleModeRamen.ExecCommand),
        typeof(GameApi.SingleModeRamen.Load),
        typeof(GameApi.SingleModeSport.CheckEvent),
        typeof(GameApi.SingleModeSport.ExecCommand),
        typeof(GameApi.SingleModeSport.Load),
        typeof(GameApi.SingleModeTeam.CheckEvent),
        typeof(GameApi.SingleModeTeam.ExecCommand),
        typeof(GameApi.SingleModeTeam.Load),
        typeof(GameApi.SingleModeVenus.CheckEvent),
        typeof(GameApi.SingleModeVenus.ExecCommand),
        typeof(GameApi.SingleModeVenus.Load),
    }.OrderBy(type => type.FullName, StringComparer.Ordinal).ToArray();

    if (!actual.SequenceEqual(expected))
        throw new InvalidOperationException(
            "EventResponseAnalyzer response endpoint coverage mismatch."
            + $" expected=[{string.Join(", ", expected.Select(x => x.FullName))}],"
            + $" actual=[{string.Join(", ", actual.Select(x => x.FullName))}]");
    plugin.Dispose();
}

static void AssertEventResponseAnalyzerPriorityAfterLegendScenarioAnalyzer(IApplication application)
{
    var sharedEndpoints = new[]
    {
        typeof(GameApi.SingleModeLegend.CheckEvent),
        typeof(GameApi.SingleModeLegend.ExecCommand),
        typeof(GameApi.SingleModeLegend.Load),
    };
    var eventPlugin = new EventResponseAnalyzer.EventResponseAnalyzer();
    var legendPlugin = new LegendScenarioAnalyzer.LegendScenarioAnalyzer();
    using var eventContext = new RuntimePluginContext(application);
    using var legendContext = new RuntimePluginContext(application);
    eventPlugin.Initialize(eventContext);
    legendPlugin.Initialize(legendContext);
    var eventPriorities = ResponseAnalyzerPriorities(eventContext.AnalyzerRegistry);
    var legendPriorities = ResponseAnalyzerPriorities(legendContext.AnalyzerRegistry);

    foreach (var endpoint in sharedEndpoints)
    {
        if (!eventPriorities.TryGetValue(endpoint, out var eventPriority))
            throw new InvalidOperationException($"EventResponseAnalyzer missing priority assertion endpoint: {endpoint.FullName}.");
        if (!legendPriorities.TryGetValue(endpoint, out var legendPriority))
            throw new InvalidOperationException($"LegendScenarioAnalyzer missing priority assertion endpoint: {endpoint.FullName}.");
        if (eventPriority <= legendPriority)
            throw new InvalidOperationException(
                $"EventResponseAnalyzer must run after LegendScenarioAnalyzer for {endpoint.FullName}: eventPriority={eventPriority}, legendPriority={legendPriority}.");
    }
    eventPlugin.Dispose();
    legendPlugin.Dispose();
}

static Dictionary<Type, int> ResponseAnalyzerPriorities(RecordingAnalyzerRegistry registry)
    => registry.Registrations
        .Where(registration => registration.Kind == AnalyzerKind.Response)
        .SelectMany(registration => PluginManager.ExpandEndpointPatterns(registration.Patterns)
            .Select(endpoint => (endpoint.EndpointType, registration.Priority)))
        .ToDictionary(pair => pair.EndpointType, pair => pair.Priority);

static void AssertGamePacketCollectorCatalog()
{
    var selected = GamePacketCollector.Capture.PacketCaptureCatalog.SelectedEndpoints;
    var expectedGroups = new Dictionary<string, int>(StringComparer.Ordinal)
    {
        ["single-mode"] = 383,
        ["room-match"] = 18,
        ["race"] = 69,
        ["gacha"] = 3,
    };
    var actualGroups = selected.GroupBy(endpoint => endpoint.Group, StringComparer.Ordinal)
        .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
    if (!actualGroups.OrderBy(pair => pair.Key).SequenceEqual(expectedGroups.OrderBy(pair => pair.Key)))
        throw new InvalidOperationException(
            $"GamePacketCollector group counts mismatch: {string.Join(", ", actualGroups.Select(pair => $"{pair.Key}={pair.Value}"))}.");
    if (selected.Count != 473)
        throw new InvalidOperationException($"GamePacketCollector selected endpoint count must be 473, actual={selected.Count}.");

    var selectedPaths = selected.Select(endpoint => endpoint.Path).ToHashSet(StringComparer.Ordinal);
    var captureNamespaces = new HashSet<string>(StringComparer.Ordinal)
    {
        "generate_succession",
        "idle_single_mode",
        "pre_single_mode",
        "room_match",
        "challenge_match",
        "champions",
        "daily_legend_race",
        "daily_race",
        "legend_race",
        "main_story_race",
        "practice_race",
        "team_building",
        "team_stadium",
        "team_stadium_skip",
        "ultimate_race",
        "gacha",
    };
    var excluded = GameEndpointCatalog.ByPath.Keys.Count(path =>
    {
        var endpointNamespace = path.Split('/')[2];
        return (endpointNamespace == "single_mode"
                || endpointNamespace.StartsWith("single_mode_", StringComparison.Ordinal)
                || captureNamespaces.Contains(endpointNamespace))
            && !selectedPaths.Contains(path);
    });
    if (excluded != 131)
        throw new InvalidOperationException($"GamePacketCollector exclusion count must be 131, actual={excluded}.");

    var patterns = GamePacketCollector.Capture.PacketCaptureCatalog.BuildPatterns(selected);
    if (patterns.Count(pattern => pattern.Kind == EndpointPatternKind.Wildcard) != 21
        || patterns.Any(pattern => pattern.Kind == EndpointPatternKind.Wildcard
            && pattern.Pattern is "/umamusume/*" or "/umamusume/**"))
        throw new InvalidOperationException("GamePacketCollector must use exactly the 21 shared single-mode action wildcard families and no root wildcard.");
    var expanded = PluginManager.ExpandEndpointPatterns(patterns)
        .Select(endpoint => endpoint.Path)
        .ToHashSet(StringComparer.Ordinal);
    if (!expanded.SetEquals(selectedPaths))
        throw new InvalidOperationException("GamePacketCollector patterns do not round-trip to the exact 473 selected catalog endpoints.");
}

static async ValueTask AssertEventResponseAnalyzerDisplayStates(WorkspaceSmokeSession ui)
{
    WriteEventResponseAnalyzerHistorySettings(0);
    var originalCulture = EventI18n.Culture;
    var captures = new Dictionary<string, EventRenderCapture>();
    var narrowText = string.Empty;

    var plugin = new EventResponseAnalyzer.EventResponseAnalyzer();
    using var context = new RuntimePluginContext(ui.Application);
    var target = Workspace.Create("EventResponseAnalyzer");
    target.SwitchTo();
    var emptyWorkspace = CaptureEventScreen(ui);
    var hostNormal = RequireEventAttribute(
        emptyWorkspace,
        RequireEventPoint(emptyWorkspace, "EventResponseAnalyzer 还没有输出。"));
    ui.Bootstrap.SwitchTo();
    try
    {
        EventI18n.Culture = CultureInfo.GetCultureInfo("zh-CN");
        plugin.Initialize(context);
        var skillFixtures = new UmamusumeResponseAnalyzer.Entities.SkillData[]
        {
                new UmamusumeResponseAnalyzer.Entities.SkillData
                {
                    Id = 910001,
                    GroupId = 91001,
                    Rarity = 1,
                    Rate = 1,
                    Name = "零级技能",
                    Propers = []
                },
                new UmamusumeResponseAnalyzer.Entities.SkillData
                {
                    Id = 910002,
                    GroupId = 91002,
                    Rarity = 1,
                    Rate = 1,
                    Name = "四级技能",
                    Propers = []
                },
                new UmamusumeResponseAnalyzer.Entities.SkillData
                {
                    Id = 910003,
                    GroupId = 91003,
                    Rarity = 1,
                    Rate = 1,
                    Name = "五级技能",
                    Propers = []
                }
        };
        var charaInfo = new SingleModeChara
        {
            skill_tips_array =
            [
                new() { group_id = 91001, rarity = 1, level = 0 },
                new() { group_id = 91002, rarity = 1, level = 4 },
                new() { group_id = 91003, rarity = 1, level = 5 }
            ]
        };

        await InitializeSmokeDatabase(
        [
            CreateStory(
                501143706,
                "[角色] 维克托瓦尔皮萨",
                "满天的星空",
                new Choice
                {
                    Option = "我也想试着连接星星",
                    SuccessEffect = "速度 +10"
                })
        ], skillFixtures);
        captures["single"] = await Render([CreateEvent(501143706, 1)]);

        await InitializeSmokeDatabase(
        [
            CreateStory(
                501143707,
                "训练事件",
                "胜负结果",
                new Choice
                {
                    Option = "挑战",
                    SuccessEffect = "体力 +10",
                    FailedEffect = "体力 -10"
                })
        ], skillFixtures);
        captures["dual"] = await Render([CreateEvent(501143707, 1)]);

        await InitializeSmokeDatabase([], skillFixtures);
        captures["unknown"] = await Render([CreateEvent(999999999, 7)]);

        await InitializeSmokeDatabase(
        [
            CreateStory(
                501143708,
                "长文本来源",
                "长文本事件",
                new Choice
                {
                    Option = "这是一个非常长的选择项文本，用来确认窄窗口下会自然换行，并且末尾内容不被水平截断",
                    SuccessEffect = "这是同样需要在狭窄宽度下完整换行显示的效果文本"
                })
        ], skillFixtures);
        captures["long"] = await Render([CreateEvent(501143708, 1)]);
        narrowText = ui.CaptureScreen(42, 30);

        await InitializeSmokeDatabase(
        [
            CreateStory(
                501143709,
                "第一来源",
                "第一个事件",
                new Choice
                {
                    Option = string.Empty,
                    SuccessEffect = "体力 -10"
                })
        ], skillFixtures);
        captures["multiple"] = await Render(
        [
            CreateEvent(501143709, 1, 2),
            CreateEvent(999999998, 3)
        ]);
        foreach (var key in new[] { Key.CursorLeft, Key.CursorUp, Key.CursorRight, Key.CursorDown })
            ui.SendKey(key);
        captures["multipleAfterArrows"] = CaptureEventScreen(ui);

        await InitializeSmokeDatabase(
        [
            CreateStory(
                501143710,
                "技能事件",
                "技能等级",
                new Choice
                {
                    Option = "查看提示等级",
                    SuccessEffect = "获得「零级技能」「四级技能」「五级技能」提示"
                })
        ], skillFixtures);
        captures["skills"] = await Render([CreateEvent(501143710, 1)]);

        async ValueTask<EventRenderCapture> Render(SingleModeEventInfo[] events)
        {
            var response = new SingleModeCheckEventResponse
            {
                data = new()
                {
                    chara_info = charaInfo,
                    unchecked_event_array = events
                }
            };
            await DispatchHostResponse(
                context.AnalyzerRegistry,
                "https://api.games.umamusume.jp/umamusume/single_mode/check_event",
                MessagePackSerializer.Serialize(response));
            var framebuffer = ui.CaptureScreen();
            if (!ReferenceEquals(Workspace.Current, target))
                throw new InvalidOperationException("EventResponseAnalyzer did not switch to its canonical workspace.");
            if (!framebuffer.Contains(events[0].story_id.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal))
                throw new InvalidOperationException("EventResponseAnalyzer content is not visible in the real Host framebuffer.");

            return CaptureEventScreen(ui);
        }
    }
    finally
    {
        await InitializeSmokeDatabase();
        EventI18n.Culture = originalCulture;
        ((IPlugin)plugin).Dispose();
    }

    target.SwitchTo();
    if (!ReferenceEquals(Workspace.Create("eventresponseanalyzer"), target)
        || ui.CaptureScreen().Contains("事件分析", StringComparison.Ordinal))
    {
        throw new InvalidOperationException(
            "EventResponseAnalyzer Dispose must remove its events panel without removing the shared workspace generation.");
    }

    var single = captures["single"];
    RequireEventText(single, "[角色] 维克托瓦尔皮萨");
    RequireEventText(single, "我也想试着连接星星");
    RequireEventText(single, "速度 +10");
    RequireEventLine(single, "└── 满天的星空(501143706)");
    RequireEventLine(single, "    └── 我也想试着连接星星");
    RequireEventLine(single, "        └── 速度 +10");
    foreach (var text in new[]
             {
                 "[角色] 维克托瓦尔皮萨",
                 "满天的星空",
                 "501143706",
                 "我也想试着连接星星",
                 "└── 满天的星空",
                 "速度 +10"
             })
    {
        RequireEvent(
            RequireEventAttribute(single, RequireEventPoint(single, text)).Equals(hostNormal),
            $"'{text}' did not inherit the Host normal attribute.");
    }

    var dual = captures["dual"];
    var successLine = RequireEventLine(dual, "        └── (成功时)体力 +10");
    var failureLine = RequireEventLine(dual, "            (失败时)体力 -10");
    RequireEvent(
        failureLine.Row == successLine.Row + 1,
        "success/failure rows no longer share one Spectre-style effect node.");
    var success = RequireEventPoint(dual, "(成功时)体力 +10");
    var failure = RequireEventPoint(dual, "(失败时)体力 -10");
    RequireEventForegroundOnly(
        dual,
        success,
        new("#00FA9A"),
        hostNormal,
        "success effect did not preserve the Host normal background and style.");
    RequireEventForegroundOnly(
        dual,
        failure,
        new("#FF0050"),
        hostNormal,
        "failure effect did not preserve the Host normal background and style.");

    var unknown = captures["unknown"];
    RequireEventLine(unknown, "未知来源");
    RequireEventLine(unknown, "└── 未知事件(999999999)");
    RequireEventLine(unknown, "    └── 未知选项 @ 7");
    RequireEventLine(unknown, "        └── 未知效果");
    foreach (var text in new[] { "未知来源", "未知事件", "999999999", "未知选项 @ 7", "未知效果" })
    {
        RequireEvent(
            RequireEventAttribute(unknown, RequireEventPoint(unknown, text)).Equals(hostNormal),
            $"unknown-event text '{text}' did not inherit the Host normal attribute.");
    }

    RequireEvent(
        narrowText.Contains("这是一个非常长的选择项文本", StringComparison.Ordinal)
        && narrowText.Contains("水平截断", StringComparison.Ordinal)
        && narrowText.Contains("这是同样需要在狭窄宽度下", StringComparison.Ordinal)
        && narrowText.Contains("效果文本", StringComparison.Ordinal),
        "The real narrow Host framebuffer did not keep the long event content visible.");

    var multiple = captures["multiple"];
    RequireEventLine(multiple, "第一来源");
    RequireEventLine(multiple, "└── 第一个事件(501143709)");
    RequireEventLine(multiple, "    ├── 无选项");
    var negativeLine = RequireEventLine(multiple, "    │   └── 体力 -10");
    RequireEventLine(multiple, "    └── 未知选项 @ 2");
    var firstUnknownEffect = RequireEventLine(multiple, "        └── 未知效果", occurrence: 0);
    var secondSource = RequireEventLine(multiple, "未知来源", occurrence: 0);
    RequireEvent(
        secondSource.Row == firstUnknownEffect.Row + 1,
        "multiple Spectre-style trees should be adjacent without an artificial section gap.");
    var negative = RequireEventPoint(multiple, "体力 -10");
    RequireEvent(
        RequireEventAttribute(multiple, negative).Equals(hostNormal),
        "ordinary negative effect did not inherit the Host normal attribute.");
    var firstUnknown = RequireEventPoint(multiple, "未知效果", occurrence: 0);
    RequireEventForegroundOnly(
        multiple,
        firstUnknown,
        new("#AFAFAF"),
        hostNormal,
        "known unknown option did not preserve the Host normal background and style.");
    var unknownEventEffect = RequireEventPoint(multiple, "未知效果", occurrence: 1);
    RequireEvent(
        RequireEventAttribute(multiple, unknownEventEffect).Equals(hostNormal),
        "unknown-event effect did not inherit the Host normal attribute.");
    RequireEvent(negativeLine.Row < secondSource.Row, "multiple events are not rendered in source order.");
    RequireEvent(
        multiple.Text == captures["multipleAfterArrows"].Text,
        "historyLimit=0 must keep the complete live event batch instead of navigating retained entries.");

    var skills = captures["skills"];
    RequireEvent(
        !skills.Text.Contains("501143709", StringComparison.Ordinal)
        && !skills.Text.Contains("999999998", StringComparison.Ordinal),
        "historyLimit=0 did not replace the preceding complete response batch.");
    RequireEventText(skills, "零级技能(Lv.0)");
    RequireEventText(skills, "四级技能(Lv.4)");
    RequireEventText(skills, "五级技能(Lv.5)");
    var levelZero = RequireEventPoint(skills, "Lv.0");
    var levelPartial = RequireEventPoint(skills, "Lv.4");
    var levelMaximum = RequireEventPoint(skills, "Lv.5");
    RequireEventForegroundOnly(
        skills,
        new(levelZero.X + 3, levelZero.Y),
        new(StandardColor.Red),
        hostNormal,
        "level zero did not preserve the Host normal background and style.");
    RequireEventForegroundOnly(
        skills,
        new(levelPartial.X + 3, levelPartial.Y),
        new(StandardColor.Yellow),
        hostNormal,
        "partial skill level did not preserve the Host normal background and style.");
    RequireEventForegroundOnly(
        skills,
        new(levelMaximum.X + 3, levelMaximum.Y),
        new(StandardColor.Green),
        hostNormal,
        "maximum skill level did not preserve the Host normal background and style.");
    RequireEvent(
        RequireEventAttribute(skills, levelZero).Equals(hostNormal),
        "skill level styling leaked into the Lv. prefix or changed its inherited attribute.");

    static Story CreateStory(int id, string triggerName, string name, params Choice[] choices)
        => new()
        {
            Id = id,
            TriggerName = triggerName,
            Name = name,
            Choices = [.. choices.Select(choice => new List<Choice> { choice })]
        };

    static SingleModeEventInfo CreateEvent(int storyId, params int[] selectIndices)
        => new()
        {
            story_id = storyId,
            event_contents_info = new EventContentsInfo
            {
                choice_array = [.. selectIndices.Select(CreateChoice)]
            }
        };

    static ChoiceArray CreateChoice(int selectIndex)
        => new()
        {
            select_index_info_array =
            [
                new SingleModeSelectIndexInfo { select_index = selectIndex }
            ]
        };
}

static async ValueTask AssertEventResponseAnalyzerHistory(WorkspaceSmokeSession ui)
{
    var originalCulture = EventI18n.Culture;
    try
    {
        EventI18n.Culture = CultureInfo.GetCultureInfo("zh-CN");
        await InitializeSmokeDatabase([]);
        await AssertIndexedHistory();
        await AssertTrimmedHistory();
    }
    finally
    {
        EventI18n.Culture = originalCulture;
        await InitializeSmokeDatabase();
    }

    async ValueTask AssertIndexedHistory()
    {
        WriteEventResponseAnalyzerHistorySettings(16);
        var plugin = new EventResponseAnalyzer.EventResponseAnalyzer();
        using var context = new RuntimePluginContext(ui.Application);
        try
        {
            plugin.Initialize(context);

            const int storyA = 990_001;
            const int storyB = 990_002;
            const int storyC = 990_003;
            const int storyAcrossEndpoints = 990_004;
            const int longStory = 990_005;

            await DispatchCheck(context.AnalyzerRegistry, 11, 22,
                CreateHistoryEvent(storyA, 1),
                CreateHistoryEvent(storyB, 2),
                CreateHistoryEvent(storyA, 3));
            RequireCurrent(storyB, 2, storyA);

            Navigate(Key.CursorLeft, storyA, 3, storyB);
            Navigate(Key.CursorDown, storyB, 2, storyA);
            Navigate(Key.CursorUp, storyA, 3, storyB);
            Navigate(Key.CursorRight, storyB, 2, storyA);

            await DispatchCheck(context.AnalyzerRegistry, 12, 22, CreateHistoryEvent(storyA, 4));
            RequireCurrent(storyA, 4);
            await DispatchCheck(context.AnalyzerRegistry, 11, 23, CreateHistoryEvent(storyA, 5));
            RequireCurrent(storyA, 5);
            await DispatchCheck(context.AnalyzerRegistry, 11, 22, CreateHistoryEvent(storyC, 6));
            RequireCurrent(storyC, 6);

            await DispatchCheck(context.AnalyzerRegistry, 30, 40, CreateHistoryEvent(storyAcrossEndpoints, 7));
            await DispatchExecCommand(context.AnalyzerRegistry, 30, 40, CreateHistoryEvent(storyAcrossEndpoints, 8));
            await DispatchLoad(context.AnalyzerRegistry, 30, 40, CreateHistoryEvent(storyAcrossEndpoints, 9));
            RequireCurrent(storyAcrossEndpoints, 9);

            Navigate(Key.CursorLeft, storyA, 3, storyB, storyC, storyAcrossEndpoints);
            Navigate(Key.CursorDown, storyB, 2, storyA, storyC, storyAcrossEndpoints);
            Navigate(Key.CursorDown, storyA, 4, storyB, storyC, storyAcrossEndpoints);
            Navigate(Key.CursorDown, storyA, 5, storyB, storyC, storyAcrossEndpoints);
            Navigate(Key.CursorDown, storyC, 6, storyA, storyB, storyAcrossEndpoints);
            Navigate(Key.CursorDown, storyAcrossEndpoints, 9, storyA, storyB, storyC);
            Navigate(Key.CursorDown, storyAcrossEndpoints, 9, storyA, storyB, storyC);

            Navigate(Key.CursorLeft, storyA, 3, storyB, storyC, storyAcrossEndpoints);
            await DispatchCheck(context.AnalyzerRegistry, 11, 22, CreateHistoryEvent(storyA, 10));
            RequireCurrent(storyA, 10, storyB, storyC, storyAcrossEndpoints);
            Navigate(Key.CursorDown, storyB, 2, storyA, storyC, storyAcrossEndpoints);

            Navigate(Key.CursorRight, storyAcrossEndpoints, 9, storyA, storyB, storyC);
            await DispatchCheck(
                context.AnalyzerRegistry,
                50,
                60,
                CreateHistoryEvent(longStory, [.. Enumerable.Range(1, 60)]));
            RequireCurrent(longStory, 1, storyA, storyB, storyC, storyAcrossEndpoints);

            var paged = ui.CaptureScreen();
            if (paged.Contains("未知选项 @ 60", StringComparison.Ordinal))
                throw new InvalidOperationException("The long Event history entry unexpectedly started at its final page.");
            for (var i = 0; i < 10 && !paged.Contains("未知选项 @ 60", StringComparison.Ordinal); i++)
            {
                ui.SendKey(Key.PageDown);
                paged = ui.CaptureScreen();
            }
            if (!paged.Contains("未知选项 @ 60", StringComparison.Ordinal))
                throw new InvalidOperationException("PageDown did not reach the end of a long Event history entry.");

            for (var i = 0; i < 10 && !paged.Contains("未知选项 @ 1", StringComparison.Ordinal); i++)
            {
                ui.SendKey(Key.PageUp);
                paged = ui.CaptureScreen();
            }
            if (!paged.Contains("未知选项 @ 1", StringComparison.Ordinal))
                throw new InvalidOperationException("PageUp did not return to the start of a long Event history entry.");

            Navigate(Key.CursorUp, storyAcrossEndpoints, 9, storyA, storyB, storyC, longStory);
            Navigate(Key.CursorDown, longStory, 1, storyA, storyB, storyC, storyAcrossEndpoints);
        }
        finally
        {
            plugin.Dispose();
        }

        void Navigate(Key key, int storyId, int selectIndex, params int[] hiddenStoryIds)
        {
            ui.SendKey(key);
            RequireCurrent(storyId, selectIndex, hiddenStoryIds);
        }
    }

    async ValueTask AssertTrimmedHistory()
    {
        WriteEventResponseAnalyzerHistorySettings(4);
        var plugin = new EventResponseAnalyzer.EventResponseAnalyzer();
        using var context = new RuntimePluginContext(ui.Application);
        try
        {
            plugin.Initialize(context);

            const int storyA = 991_001;
            const int storyB = 991_002;
            const int storyC = 991_003;
            const int storyD = 991_004;
            const int storyE = 991_005;
            const int storyF = 991_006;

            await DispatchCheck(context.AnalyzerRegistry, 70, 80,
                CreateHistoryEvent(storyA, 1),
                CreateHistoryEvent(storyB, 2));
            RequireCurrent(storyB, 2, storyA);
            ui.SendKey(Key.CursorLeft);
            RequireCurrent(storyA, 1, storyB);

            await DispatchCheck(context.AnalyzerRegistry, 70, 80, CreateHistoryEvent(storyC, 3));
            RequireCurrent(storyA, 1, storyB, storyC);
            await DispatchCheck(context.AnalyzerRegistry, 70, 80, CreateHistoryEvent(storyD, 4));
            RequireCurrent(storyA, 1, storyB, storyC, storyD);
            await DispatchCheck(context.AnalyzerRegistry, 70, 80, CreateHistoryEvent(storyE, 5));
            RequireCurrent(storyE, 5, storyA, storyB, storyC, storyD);
            await DispatchCheck(context.AnalyzerRegistry, 70, 80, CreateHistoryEvent(storyF, 6));
            RequireCurrent(storyF, 6, storyA, storyB, storyC, storyD, storyE);

            ui.SendKey(Key.CursorLeft);
            RequireCurrent(storyC, 3, storyA, storyB, storyD, storyE, storyF);
            await DispatchCheck(context.AnalyzerRegistry, 70, 80, CreateHistoryEvent(storyC, 77));
            RequireCurrent(storyC, 77, storyA, storyB, storyD, storyE, storyF);
            ui.SendKey(Key.CursorRight);
            RequireCurrent(storyF, 6, storyA, storyB, storyC, storyD, storyE);
        }
        finally
        {
            plugin.Dispose();
        }
    }

    void RequireCurrent(int storyId, int selectIndex, params int[] hiddenStoryIds)
    {
        var screen = ui.CaptureScreen();
        if (!ReferenceEquals(Workspace.Current, Workspace.Create("EventResponseAnalyzer")))
            throw new InvalidOperationException("Event history navigation left the canonical workspace.");
        if (!screen.Contains(storyId.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
            || !screen.Contains($"未知选项 @ {selectIndex}", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Expected Event history ({storyId}, select={selectIndex}) is not visible in the real Host framebuffer.");
        }

        foreach (var hiddenStoryId in hiddenStoryIds)
        {
            if (screen.Contains(hiddenStoryId.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal))
                throw new InvalidOperationException($"Event history unexpectedly rendered story {hiddenStoryId} in the selected entry.");
        }
    }

    static SingleModeEventInfo CreateHistoryEvent(int storyId, params int[] selectIndices)
        => new()
        {
            story_id = storyId,
            event_contents_info = new EventContentsInfo
            {
                choice_array = [.. selectIndices.Select(selectIndex => new ChoiceArray
                {
                    select_index_info_array = [new SingleModeSelectIndexInfo { select_index = selectIndex }]
                })]
            }
        };

    static SingleModeChara CreateHistoryChara(int charaId, int turn)
        => new()
        {
            single_mode_chara_id = charaId,
            turn = turn,
            skill_tips_array = []
        };

    static ValueTask DispatchCheck(
        RecordingAnalyzerRegistry registry,
        int charaId,
        int turn,
        params SingleModeEventInfo[] events)
        => DispatchHostResponse(
            registry,
            "https://api.games.umamusume.jp/umamusume/single_mode/check_event",
            MessagePackSerializer.Serialize(new SingleModeCheckEventResponse
            {
                data = new()
                {
                    chara_info = CreateHistoryChara(charaId, turn),
                    unchecked_event_array = events
                }
            }));

    static ValueTask DispatchExecCommand(
        RecordingAnalyzerRegistry registry,
        int charaId,
        int turn,
        params SingleModeEventInfo[] events)
        => DispatchHostResponse(
            registry,
            "https://api.games.umamusume.jp/umamusume/single_mode/exec_command",
            MessagePackSerializer.Serialize(new SingleModeExecCommandResponse
            {
                data = new()
                {
                    chara_info = CreateHistoryChara(charaId, turn),
                    unchecked_event_array = events
                }
            }));

    static ValueTask DispatchLoad(
        RecordingAnalyzerRegistry registry,
        int charaId,
        int turn,
        params SingleModeEventInfo[] events)
        => DispatchHostResponse(
            registry,
            "https://api.games.umamusume.jp/umamusume/single_mode/load",
            MessagePackSerializer.Serialize(new SingleModeLoadResponse
            {
                data = new()
                {
                    single_mode_load_common = new SingleModeLoadCommon
                    {
                        chara_info = CreateHistoryChara(charaId, turn),
                        unchecked_event_array = events
                    }
                }
            }));
}

static EventRenderCapture CaptureEventScreen(WorkspaceSmokeSession ui)
{
    var copy = ui.CaptureCells();
    return new(copy, new(0, 0, copy.GetLength(1), copy.GetLength(0)));
}

static (int Row, string Text) RequireEventLine(
    EventRenderCapture capture,
    string text,
    int occurrence = 0)
{
    for (var y = capture.Region.Top; y < capture.Region.Bottom; y++)
    {
        var line = capture.GetLine(y);
        if (!line.Contains(text, StringComparison.Ordinal))
            continue;
        if (occurrence-- == 0)
            return (y, line);
    }

    throw new InvalidOperationException($"EventResponseAnalyzer render is missing line '{text}'.");
}

static Point RequireEventPoint(
    EventRenderCapture capture,
    string text,
    int occurrence = 0)
{
    for (var y = capture.Region.Top; y < capture.Region.Bottom; y++)
    {
        var line = capture.GetLine(y);
        var searchFrom = 0;
        while (searchFrom <= line.Length - text.Length)
        {
            var index = line.IndexOf(text, searchFrom, StringComparison.Ordinal);
            if (index < 0)
                break;
            if (occurrence-- == 0)
                return new(capture.GetColumn(y, index), y);
            searchFrom = index + text.Length;
        }
    }

    throw new InvalidOperationException($"EventResponseAnalyzer render is missing '{text}'.");
}

static TAttribute RequireEventAttribute(EventRenderCapture capture, Point point)
    => capture.Cells[point.Y, point.X].Attribute
        ?? throw new InvalidOperationException(
            $"EventResponseAnalyzer render cell at ({point.X}, {point.Y}) has no attribute.");

static void RequireEventForegroundOnly(
    EventRenderCapture capture,
    Point point,
    TColor foreground,
    TAttribute normal,
    string message)
    => RequireEvent(
        RequireEventAttribute(capture, point).Equals(
            new TAttribute(foreground, normal.Background, normal.Style)),
        message);

static void RequireEventText(EventRenderCapture capture, string text)
    => RequireEvent(
        capture.Text.Contains(text, StringComparison.Ordinal),
        $"EventResponseAnalyzer render is missing '{text}'.");

static void RequireEvent(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}

static async ValueTask AssertEventResponseAnalyzerLegendExecCommandRendersEventPanel(WorkspaceSmokeSession ui)
{
    var plugin = new EventResponseAnalyzer.EventResponseAnalyzer();
    using var context = new RuntimePluginContext(ui.Application);
    var target = Workspace.Create("EventResponseAnalyzer");
    try
    {
        plugin.Initialize(context);
        await AssertPanelUpdate(
            () => DispatchHostResponse(
                context.AnalyzerRegistry,
                "https://api.games.umamusume.jp/umamusume/single_mode_legend/exec_command",
                MessagePackSerializer.Serialize(CreateExecCommandResponse(830241003))),
            switchesToTarget: true,
            storyId: 830241003,
            "Legend ExecCommand group card event");
        await AssertPanelUpdate(
            () => DispatchHostResponse(
                context.AnalyzerRegistry,
                "https://api.games.umamusume.jp/umamusume/single_mode_legend/load",
                MessagePackSerializer.Serialize(CreateLoadResponse(830241003))),
            switchesToTarget: true,
            storyId: 830241003,
            "Legend Load group card event");
        await AssertPanelUpdate(
            () => DispatchHostResponse(
                context.AnalyzerRegistry,
                "https://api.games.umamusume.jp/umamusume/single_mode_legend/check_event",
                MessagePackSerializer.Serialize(CreateCheckEventResponse(400010112))),
            switchesToTarget: false,
            storyId: 400010112,
            "Legend CheckEvent buff selection");
        await AssertPanelUpdate(
            () => DispatchHostResponse(
                context.AnalyzerRegistry,
                "https://api.games.umamusume.jp/umamusume/single_mode_legend/exec_command",
                MessagePackSerializer.Serialize(CreateExecCommandResponse(400010112))),
            switchesToTarget: false,
            storyId: 400010112,
            "Legend ExecCommand buff selection");
        await AssertPanelUpdate(
            () => DispatchHostResponse(
                context.AnalyzerRegistry,
                "https://api.games.umamusume.jp/umamusume/single_mode_legend/load",
                MessagePackSerializer.Serialize(CreateLoadResponse(400010112))),
            switchesToTarget: false,
            storyId: 400010112,
            "Legend Load buff selection");
    }
    finally
    {
        ((IPlugin)plugin).Dispose();
    }

    target.SwitchTo();
    if (!ReferenceEquals(Workspace.Create("eventresponseanalyzer"), target)
        || ui.CaptureScreen().Contains("400010112", StringComparison.Ordinal))
    {
        throw new InvalidOperationException(
            "EventResponseAnalyzer Dispose must remove its events panel without removing the shared workspace generation.");
    }

    async ValueTask AssertPanelUpdate(Func<ValueTask> dispatch, bool switchesToTarget, int storyId, string scenario)
    {
        target.RemovePanel("events");
        ui.Bootstrap.SwitchTo();
        ui.Flush();

        await dispatch();
        var expectedCurrent = switchesToTarget ? target : ui.Bootstrap;
        if (!ReferenceEquals(Workspace.Current, expectedCurrent))
        {
            throw new InvalidOperationException(
                $"{scenario}: expected final workspace '{expectedCurrent.Title}', actual '{Workspace.Current?.Title ?? "<null>"}'.");
        }
        target.SwitchTo();
        if (!ui.CaptureScreen().Contains(storyId.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal))
            throw new InvalidOperationException($"{scenario}: final events panel is not visible in the real Host framebuffer.");
        expectedCurrent.SwitchTo();
    }

    static SingleModeLegendCheckEventResponse CreateCheckEventResponse(int storyId)
        => new()
        {
            data = new SingleModeLegendCheckEventResponse.CommonResponse
            {
                chara_info = CreateLegendEventChara(),
                unchecked_event_array = [CreateLegendEvent(storyId)]
            }
        };

    static SingleModeLegendExecCommandResponse CreateExecCommandResponse(int storyId)
        => new()
        {
            data = new SingleModeLegendExecCommandResponse.CommonResponse
            {
                chara_info = CreateLegendEventChara(),
                unchecked_event_array = [CreateLegendEvent(storyId)]
            }
        };

    static SingleModeLegendLoadResponse CreateLoadResponse(int storyId)
        => new()
        {
            data = new SingleModeLegendLoadResponse.CommonResponse
            {
                single_mode_load_common = new SingleModeLoadCommon
                {
                    chara_info = CreateLegendEventChara(),
                    unchecked_event_array = [CreateLegendEvent(storyId)]
                }
            }
        };

    static SingleModeChara CreateLegendEventChara()
        => new()
        {
            state = 0,
            playing_state = 5,
            turn = 25,
            skill_tips_array = []
        };

    static SingleModeEventInfo CreateLegendEvent(int storyId)
        => new()
        {
            event_id = 21151,
            story_id = storyId,
            play_timing = 6,
            event_contents_info = new EventContentsInfo
            {
                support_card_id = 30241,
                choice_array = [CreateChoice(1), CreateChoice(2), CreateChoice(3)]
            }
        };

    static ChoiceArray CreateChoice(int selectIndex)
        => new()
        {
            select_index_info_array =
            [
                new SingleModeSelectIndexInfo
                {
                    branch_number = 1,
                    select_index = selectIndex
                }
            ],
            gain_select_id_index = selectIndex
        };
}

static async ValueTask DispatchHostResponse(
    RecordingAnalyzerRegistry registry,
    string canonicalUrl,
    byte[] payload)
{
    if (!Server.TryResolveEndpoint(canonicalUrl, out var endpoint))
        throw new InvalidOperationException($"Host did not resolve smoke endpoint: {canonicalUrl}");

    var context = new AnalyzerDispatchContext(
        endpoint,
        payload,
        new("sid-1", "1.2.3", "2026070301", "123456789", "android", "phone"));
    var registrations = registry.Registrations
        .Where(registration => registration.Kind == AnalyzerKind.Response)
        .Where(registration => PluginManager.ExpandEndpointPatterns(registration.Patterns)
            .Any(candidate => candidate.EndpointType == endpoint.EndpointType))
        .OrderBy(registration => registration.Priority)
        .ToArray();
    if (registrations.Length == 0)
        throw new InvalidOperationException($"No programmatic analyzer matched {endpoint.Path}.");

    foreach (var registration in registrations)
        await registration.Invoke(context);
}

static void InitializeHostConfigForSmoke()
    => UmamusumeResponseAnalyzer.Config.Initialize();

static async Task InitializeSmokeDatabase(
    IEnumerable<Story>? events = null,
    IEnumerable<UmamusumeResponseAnalyzer.Entities.SkillData>? skills = null)
{
    WriteBrotliJson(Database.EVENT_NAME_FILEPATH, (events ?? []).ToList());
    WriteBrotliJson(
        Database.NAMES_FILEPATH,
        new List<BaseName>
        {
            new SupportCardName(10001, "fixture support 1", "S1", 101, 1001),
            new SupportCardName(10002, "fixture support 2", "S2", 105, 1002),
            new SupportCardName(10003, "fixture support 3", "S3", 102, 1003),
            new SupportCardName(10004, "fixture support 4", "S4", 103, 1004),
            new SupportCardName(10005, "fixture support 5", "S5", 106, 1005),
            new SupportCardName(10006, "fixture support 6", "S6", 0, 1006),
        },
        new() { TypeNameHandling = TypeNameHandling.All });
    WriteBrotliJson(Database.SKILLS_FILEPATH, (skills ?? []).ToList());
    WriteBrotliJson(Database.SKILL_UPGRADE_SPECIALITY_FILEPATH, new List<SkillUpgradeSpeciality>());
    WriteBrotliJson(Database.TALENT_SKILLS_FILEPATH, new Dictionary<int, TalentSkillData[]>());
    WriteBrotliJson(Database.FACTOR_IDS_FILEPATH, new Dictionary<int, string>());
    WriteBrotliJson(Database.SADDLE_IDS_FILEPATH, Array.Empty<int>());
    WriteBrotliJson(Database.SUCCESSION_RELATION_FILEPATH, new SuccessionRelationTable());
    if (await Database.Initialize() != DatabaseAvailability.Ready)
        throw new InvalidOperationException("Smoke database fixture did not load atomically.");
}

static void WriteEventResponseAnalyzerHistorySettings(int historyLimit)
{
    var dataDirectory = Path.Combine("PluginData", "EventResponseAnalyzer");
    Directory.CreateDirectory(dataDirectory);
    File.WriteAllText(
        Path.Combine(dataDirectory, "settings.json"),
        System.Text.Json.JsonSerializer.Serialize(
            new { historyLimit },
            new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }));
}

static void WriteBrotliJson<T>(string path, T value, JsonSerializerSettings? settings = null)
{
    using var file = File.Create(path);
    using var brotli = new BrotliStream(file, CompressionLevel.SmallestSize);
    using var writer = new StreamWriter(brotli, Encoding.UTF8);
    using var json = new JsonTextWriter(writer);
    (settings is null
        ? Newtonsoft.Json.JsonSerializer.CreateDefault()
        : Newtonsoft.Json.JsonSerializer.CreateDefault(settings)).Serialize(json, value);
}

static void PrepareSmokeSideEffectGuards(TargetAssembly target)
{
    if (target.AssemblyName == "GamePacketCollector")
        PrepareGamePacketCollectorSmokeConfig();
}

static void AssertSmokeSideEffectGuards(TargetAssembly target)
{
    if (target.AssemblyName != "GamePacketCollector")
        return;

    var dataDirectory = Path.Combine("PluginData", "游戏包采集");
    AssertGamePacketCollectorSmokeConfig(Path.Combine(dataDirectory, "config.json"));

    var sentDirectory = Path.Combine(dataDirectory, "sent");
    if (Directory.Exists(sentDirectory) &&
        Directory.EnumerateFiles(sentDirectory, "*.json", SearchOption.AllDirectories).Any())
        throw new InvalidOperationException("GamePacketCollector smoke test must not upload packets or create sent upload artifacts.");
}

static void PrepareGamePacketCollectorSmokeConfig()
{
    var dataDirectory = Path.Combine("PluginData", "游戏包采集");
    Directory.CreateDirectory(dataDirectory);

    var configPath = Path.Combine(dataDirectory, "config.json");
    File.WriteAllText(
        configPath,
        System.Text.Json.JsonSerializer.Serialize(
            new
            {
                uploadUrl = "http://127.0.0.1:9/PluginSmokeTests/GamePackets",
                serverRegionHint = "smoke",
                enabled = true,
                endpointGroups = new[] { "single-mode", "room-match", "race", "gacha" },
            },
            new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }));

    AssertGamePacketCollectorSmokeConfig(configPath);
}

static void AssertGamePacketCollectorSmokeConfig(string configPath)
{
    if (!File.Exists(configPath))
        throw new InvalidOperationException($"GamePacketCollector smoke config was not created: {configPath}");

    using var document = JsonDocument.Parse(File.ReadAllText(configPath));
    var root = document.RootElement;
    if (!root.TryGetProperty("enabled", out var enabled) || enabled.ValueKind != JsonValueKind.True)
        throw new InvalidOperationException("GamePacketCollector smoke config must set enabled=true.");
    if (!root.TryGetProperty("endpointGroups", out var endpointGroups)
        || endpointGroups.ValueKind != JsonValueKind.Array
        || !endpointGroups.EnumerateArray().Select(group => group.GetString()).SequenceEqual(
            ["single-mode", "room-match", "race", "gacha"],
            StringComparer.Ordinal))
    {
        throw new InvalidOperationException("GamePacketCollector smoke config must enable the exact four approved endpoint groups.");
    }

    if (!root.TryGetProperty("uploadUrl", out var uploadUrlElement) ||
        uploadUrlElement.ValueKind != JsonValueKind.String ||
        !Uri.TryCreate(uploadUrlElement.GetString(), UriKind.Absolute, out var uploadUri) ||
        !uploadUri.IsLoopback)
        throw new InvalidOperationException("GamePacketCollector smoke config must use a loopback uploadUrl.");
}

sealed record TargetAssembly(
    string AssemblyName,
    Func<IPlugin> Create,
    string? ExpectedPanelKey = null,
    string? ExpectedWorkspaceTitle = null,
    string? ExpectedSwitchedWorkspaceTitle = null,
    string[]? ExpectedManifestDependencies = null);

sealed record PluginRunSummary(int PluginCount, int AttributeAnalyzerCount, int RegisteredAnalyzerCount);

static class PluginSmokeRunner
{
    static string? packageRoot;
    static IReadOnlyDictionary<string, string>? packagePaths;

    public static async Task<PluginRunSummary> Run(TargetAssembly target, WorkspaceSmokeSession ui)
    {
        var attributeAnalyzerCount = 0;
        var registeredAnalyzerCount = 0;
        var plugin = target.Create();
        using (var context = new RuntimePluginContext(ui.Application))
        {
            ui.Bootstrap.SwitchTo();
            var beforeInitialize = ui.CaptureScreen();
            Workspace? exercised = null;
            try
            {
                plugin.Initialize(context);
                var expectedProgrammaticRegistrations = target.AssemblyName switch
                {
                    "EventLoggerPlugin" => 9,
                    "EventResponseAnalyzer" => 2,
                    "GamePacketCollector" => 2,
                    "LegendScenarioAnalyzer" => 2,
                    "RamenScenarioAnalyzer" => 4,
                    "SendGameStatusPlugin" => 5,
                    _ => 0,
                };
                if (context.AnalyzerRegistry.Registrations.Count != expectedProgrammaticRegistrations)
                    throw new InvalidOperationException(
                        $"{target.AssemblyName} programmatic registration count mismatch: " +
                        $"expected={expectedProgrammaticRegistrations}, actual={context.AnalyzerRegistry.Registrations.Count}.");
                if (!ReferenceEquals(Workspace.Current, ui.Bootstrap))
                    throw new InvalidOperationException(
                        $"{target.AssemblyName} changed the visible workspace during Initialize.");
                if (target.AssemblyName != "GamePacketCollector"
                    && !string.Equals(beforeInitialize, ui.CaptureScreen(), StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"{target.AssemblyName} changed the visible framebuffer during Initialize.");
                }
                attributeAnalyzerCount += await InvokeAttributeAnalyzers(plugin);
                registeredAnalyzerCount += await InvokeRegisteredAnalyzers(plugin.GetType(), context.AnalyzerRegistry);
                exercised = AssertExpectedVisibleState(target, ui);
            }
            finally
            {
                plugin.Dispose();
            }

            AssertDisposeState(target, exercised, ui);
        }

        return new(1, attributeAnalyzerCount, registeredAnalyzerCount);
    }

    static Workspace? AssertExpectedVisibleState(TargetAssembly target, WorkspaceSmokeSession ui)
    {
        var workspaceTitle = target.ExpectedWorkspaceTitle ?? target.ExpectedSwitchedWorkspaceTitle
            ?? (target.ExpectedPanelKey is null
                ? null
                : throw new InvalidOperationException(
                    $"{target.AssemblyName} has a panel expectation without a workspace title."));
        if (workspaceTitle is null)
            return null;

        var current = Workspace.Current;
        if (target.ExpectedSwitchedWorkspaceTitle is not null
            && !string.Equals(current?.Title, target.ExpectedSwitchedWorkspaceTitle, StringComparison.Ordinal))
            throw new InvalidOperationException(
                $"{target.AssemblyName} final workspace is '{current?.Title ?? "<null>"}',"
                + $" expected '{target.ExpectedSwitchedWorkspaceTitle}'.");

        if (target.ExpectedPanelKey is null)
            return string.Equals(current?.Title, workspaceTitle, StringComparison.Ordinal) ? current : null;

        if (!string.Equals(current?.Title, workspaceTitle, StringComparison.Ordinal))
            throw new InvalidOperationException(
                $"{target.AssemblyName} did not switch to its visible panel workspace '{workspaceTitle}'.");
        var panelText = ExpectedPanelText(target);
        var screen = ui.CaptureScreen();
        if (!screen.Contains(panelText, StringComparison.Ordinal))
            throw new InvalidOperationException(
                $"{target.AssemblyName} did not render its expected panel content '{panelText}'.");
        return current;
    }

    static void AssertDisposeState(
        TargetAssembly target,
        Workspace? exercised,
        WorkspaceSmokeSession ui)
    {
        if (exercised is null || target.ExpectedPanelKey is null)
            return;

        var workspaceTitle = target.ExpectedWorkspaceTitle ?? target.ExpectedSwitchedWorkspaceTitle!;
        exercised.SwitchTo();
        if (!ReferenceEquals(Workspace.Create(workspaceTitle.ToUpperInvariant()), exercised)
            || ui.CaptureScreen().Contains(ExpectedPanelText(target), StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"{target.AssemblyName} Dispose must remove panel '{target.ExpectedPanelKey}'"
                + " without removing the shared workspace generation.");
        }
    }

    static string ExpectedPanelText(TargetAssembly target)
        => target.AssemblyName switch
        {
            "EventResponseAnalyzer" => $"{EventI18n.I18N_UnknownEvent}(1001)",
            "RamenScenarioAnalyzer" => "总属性: 2500, Pt: 1200",
            _ => throw new InvalidOperationException(
                $"{target.AssemblyName} has no visible panel-content expectation.")
        };

    static void AssertExpectedManifestDependency(TargetAssembly target)
    {
        var packagePath = FindPackagePath(target);
        using var archive = ZipFile.OpenRead(packagePath);
        var manifest = archive.GetEntry("manifest.json")
            ?? throw new InvalidDataException($"{target.AssemblyName} package has no root manifest.json: {packagePath}");
        using var stream = manifest.Open();
        using var document = JsonDocument.Parse(stream);
        if (!document.RootElement.TryGetProperty("InternalName", out var internalName) ||
            internalName.GetString() != target.AssemblyName)
        {
            throw new InvalidOperationException(
                $"{target.AssemblyName} package resolved to a different plugin: {packagePath}.");
        }

        var expectedDependencies = target.ExpectedManifestDependencies ?? [];
        if (!document.RootElement.TryGetProperty("Dependencies", out var dependencies) ||
            dependencies.ValueKind != JsonValueKind.Array ||
            !dependencies.EnumerateArray()
                .Select(dependency => dependency.ValueKind == JsonValueKind.String ? dependency.GetString() : null)
                .SequenceEqual(expectedDependencies, StringComparer.Ordinal))
        {
            throw new InvalidOperationException(
                $"{target.AssemblyName} manifest Dependencies must exactly match [{string.Join(", ", expectedDependencies)}].");
        }
    }

    internal static string FindPackagePath(TargetAssembly target)
    {
        if (packagePaths is null || !packagePaths.TryGetValue(target.AssemblyName, out var packagePath))
            throw new InvalidOperationException($"Package root was not validated for {target.AssemblyName}.");

        return packagePath;
    }

    public static void ValidatePackageRoot(IEnumerable<TargetAssembly> targets)
    {
        var configuredRoot = Environment.GetEnvironmentVariable("URA_PLUGIN_SMOKE_PACKAGE_ROOT");
        if (string.IsNullOrWhiteSpace(configuredRoot))
        {
            throw new InvalidOperationException(
                "URA_PLUGIN_SMOKE_PACKAGE_ROOT must point to the fresh plugin package output root for this smoke run.");
        }

        if (!Path.IsPathFullyQualified(configuredRoot))
            throw new InvalidOperationException("URA_PLUGIN_SMOKE_PACKAGE_ROOT must be an absolute path.");

        var fullPath = Path.GetFullPath(configuredRoot);
        if (!Directory.Exists(fullPath))
            throw new DirectoryNotFoundException($"Manifest root does not exist: {fullPath}");

        packageRoot = fullPath;
        var packages = Directory
            .EnumerateFiles(packageRoot, "*.zip", SearchOption.AllDirectories)
            .Select(path =>
            {
                using var archive = ZipFile.OpenRead(path);
                var rootManifests = archive.Entries
                    .Where(entry => entry.FullName.IndexOfAny(['/', '\\']) < 0
                        && string.Equals(entry.FullName, "manifest.json", StringComparison.OrdinalIgnoreCase))
                    .ToArray();
                if (rootManifests is not [{ FullName: "manifest.json" } manifest])
                    throw new InvalidDataException($"Package must contain exactly one case-exact root manifest.json: {path}");
                using var stream = manifest.Open();
                using var document = JsonDocument.Parse(stream);
                var expectedProperties = new HashSet<string>(
                [
                    "Author", "InternalName", "DisplayName", "Description", "Changelog", "Version",
                    "Dependencies", "Targets", "RepositoryUrl", "LastUpdate", "Category", "Homepage",
                ], StringComparer.Ordinal);
                var properties = document.RootElement.EnumerateObject()
                    .Select(property => property.Name)
                    .ToArray();
                var actualProperties = properties.ToHashSet(StringComparer.Ordinal);
                if (properties.Length != actualProperties.Count || !actualProperties.SetEquals(expectedProperties))
                    throw new InvalidDataException($"Package manifest schema mismatch: {path}");
                var internalName = document.RootElement.TryGetProperty("InternalName", out var value)
                    ? value.GetString()
                    : null;
                if (string.IsNullOrWhiteSpace(internalName)
                    || !string.Equals(Path.GetFileNameWithoutExtension(path), internalName, StringComparison.OrdinalIgnoreCase)
                    || archive.Entries.Count(entry => entry.FullName.IndexOfAny(['/', '\\']) < 0
                        && string.Equals(entry.FullName, $"{internalName}.dll", StringComparison.OrdinalIgnoreCase)) != 1)
                    throw new InvalidDataException($"Package filename/main assembly/InternalName mismatch: {path}");
                return (Path: path, InternalName: internalName);
            })
            .ToArray();
        if (packages.Length != targets.Count())
            throw new InvalidOperationException($"Expected exactly {targets.Count()} ZIPs under {packageRoot}, found {packages.Length}.");
        var resolvedPaths = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var target in targets)
        {
            var matches = packages
                .Where(x => x.InternalName == target.AssemblyName)
                .Select(x => x.Path)
                .ToArray();
            if (matches.Length != 1)
            {
                throw new InvalidOperationException(
                    $"{target.AssemblyName} requires exactly one package under {packageRoot}, found {matches.Length}:"
                    + $" [{string.Join(", ", matches)}].");
            }

            resolvedPaths.Add(target.AssemblyName, matches[0]);
        }

        packagePaths = resolvedPaths;
        foreach (var target in targets)
            AssertExpectedManifestDependency(target);
        Console.WriteLine($"PASS ZIP structure: packages={resolvedPaths.Count}, manifests and dependencies verified");
    }

    public static string FindRepositoryRoot()
        => Environment.GetEnvironmentVariable("URA_TEST_PLUGINS_ROOT")
           ?? throw new InvalidOperationException("URA_TEST_PLUGINS_ROOT must identify the plugin source checkouts.");

    static async Task<int> InvokeAttributeAnalyzers(IPlugin plugin)
    {
        var count = 0;
        foreach (var registration in PluginManager.CreateRegistrationPlan(plugin).Analyzers)
        {
            var descriptor = GameEndpointCatalog.ByEndpointType[registration.EndpointType];
            var dtoType = registration.Kind == AnalyzerKind.Request
                ? descriptor.RequestType
                : descriptor.ResponseType;
            var method = registration.Method
                ?? throw new InvalidOperationException($"Attribute registration has no source method: {registration.Source}");
            var dto = RandomDtoFactory.CreateForAnalyzer(
                plugin.GetType(),
                dtoType,
                registration.Kind,
                $"{plugin.GetType().FullName}:{method.Name}:{registration.Kind}:{descriptor.Path}");
            var context = new AnalyzerDispatchContext(
                descriptor,
                MessagePackSerializer.Serialize(dtoType, dto),
                SmokeGameHttpHeaders());
            await registration.Handler(context);
            count++;
        }

        return count;
    }

    static async Task<int> InvokeRegisteredAnalyzers(Type pluginType, RecordingAnalyzerRegistry registry)
    {
        var count = 0;
        foreach (var registration in registry.Registrations.OrderBy(x => x.Priority))
        {
            IEnumerable<GameEndpointDescriptor> descriptors = PluginManager.ExpandEndpointPatterns(registration.Patterns);
            if (pluginType == typeof(GamePacketCollector.GamePacketCollectorPlugin))
                descriptors = descriptors.Take(1);

            foreach (var descriptor in descriptors)
            {
                var dtoType = registration.Kind == AnalyzerKind.Request
                    ? descriptor.RequestType
                    : descriptor.ResponseType;
                var dto = RandomDtoFactory.CreateForAnalyzer(
                    pluginType,
                    dtoType,
                    registration.Kind,
                    $"programmatic:{registration.Kind}:{descriptor.Path}");
                var context = new AnalyzerDispatchContext(
                    descriptor,
                    MessagePackSerializer.Serialize(dtoType, dto),
                    SmokeGameHttpHeaders());
                await registration.Invoke(context);
                count++;
            }
        }

        return count;
    }

    static GameHttpHeaders SmokeGameHttpHeaders()
        => new(
            Sid: "sid-1",
            AppVer: "1.2.3",
            ResVer: "2026070301",
            ViewerId: "123456789",
            Device: "android",
            DeviceSubtype: "phone");

}

sealed record EventRenderCapture(
    Cell[,] Cells,
    Rectangle Region)
{
    public string GetLine(int y)
    {
        var line = new StringBuilder();
        for (var x = Region.Left; x < Region.Right; x++)
        {
            var grapheme = Cells[y, x].Grapheme;
            line.Append(grapheme);
            x += Math.Max(1, grapheme.GetColumns()) - 1;
        }

        return line.ToString().TrimEnd();
    }

    public int GetColumn(int y, int textIndex)
    {
        var currentIndex = 0;
        for (var x = Region.Left; x < Region.Right; x++)
        {
            var grapheme = Cells[y, x].Grapheme;
            if (textIndex < currentIndex + grapheme.Length)
                return x;
            currentIndex += grapheme.Length;
            x += Math.Max(1, grapheme.GetColumns()) - 1;
        }

        throw new ArgumentOutOfRangeException(nameof(textIndex));
    }

    public string Text
    {
        get
        {
            var output = new StringBuilder();
            for (var y = Region.Top; y < Region.Bottom; y++)
                output.AppendLine(GetLine(y));

            return output.ToString().TrimEnd();
        }
    }
}

sealed class TempWorkspace : IDisposable
{
    TempWorkspace(string path) => Path = path;

    public string Path { get; }

    public static TempWorkspace Create()
    {
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ura-plugin-smoke-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return new(path);
    }

    public void Dispose()
    {
        if (Directory.Exists(Path))
            Directory.Delete(Path, recursive: true);
    }
}
