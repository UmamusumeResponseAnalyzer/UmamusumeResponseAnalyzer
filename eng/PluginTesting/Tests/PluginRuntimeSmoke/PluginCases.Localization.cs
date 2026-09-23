using System.Globalization;
using System.Reflection;
using Gallop;
using UmamusumeResponseAnalyzer;
using UmamusumeResponseAnalyzer.Entities;
using UmamusumeResponseAnalyzer.TerminalGui;
using HostProgram = UmamusumeResponseAnalyzer.UmamusumeResponseAnalyzer;

static partial class PluginCases
{
    public static void VerifyLocalizedTraining(PluginCase pluginCase, WorkspaceSmokeSession ui)
    {
        var previousCulture = CultureInfo.CurrentCulture;
        var previousUiCulture = CultureInfo.CurrentUICulture;
        try
        {
            foreach (var (culture, speed, wit, friend) in new[]
            {
                ("zh-CN", "速", "智", "友"),
                ("en-US", "Spd", "Wit", "Pal"),
                ("ja-JP", "速", "賢", "友")
            })
            {
                HostProgram.ApplyCultureInfo(CultureInfo.GetCultureInfo(culture));
                var localized = pluginCase with
                {
                    Panel = pluginCase.Panel! with
                    {
                        CreateResponse = () => CreateLocalizedTrainingResponse(pluginCase.Panel!.CreateResponse),
                        CreateUpdatedResponse = () => CreateLocalizedTrainingResponse(pluginCase.Panel!.CreateUpdatedResponse!),
                        VisibleText = $"[{friend}]Friend",
                        History = null,
                        VerifyFramebuffer = frame => VerifyPartnerFramebuffer(frame, pluginCase.Id, speed, wit, friend)
                    }
                };
                localized.Run(ui);
                if (pluginCase.Id == "OldScenarioAnalyzer")
                    VerifyOldScenarioDetails(ui, culture);
                Console.WriteLine($"PASS {pluginCase.Id} localized training {culture}");
            }
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
            CultureInfo.CurrentUICulture = previousUiCulture;
            HostProgram.ApplyResourceCulture(previousUiCulture);
        }
    }

    static object CreateLocalizedTrainingResponse(Func<object> createResponse)
    {
        InitializePartnerDatabase();
        var response = createResponse();
        var data = response.GetType().GetField("data")!.GetValue(response)!;
        AddPartners(
            (SingleModeChara)data.GetType().GetField("chara_info")!.GetValue(data)!,
            (SingleModeHomeInfo)data.GetType().GetField("home_info")!.GetValue(data)!);
        return response;
    }

    static void InitializePartnerDatabase() => InitializeDatabase(
        "localized partners", [],
        [
            new BaseName(1001, "Mismatch", "Mismatch"),
            new BaseName(1002, "Ready", "Ready"),
            new BaseName(1003, "Friend", "Friend"),
            new BaseName(1004, "Low", "Low"),
            new BaseName(1005, "Group", "Group"),
            new SupportCardName(30001, "Mismatch", "Mismatch", 106, 1001),
            new SupportCardName(30002, "Ready", "Ready", 101, 1002),
            new SupportCardName(30003, "Friend", "Friend", 0, 1003),
            new SupportCardName(30004, "Low", "Low", 101, 1004),
            new SupportCardName(30241, "Group", "Group", 0, 1005)
        ], new Dictionary<int, string>(), []);

    static void AddPartners(SingleModeChara chara, SingleModeHomeInfo home)
    {
        chara.support_card_array =
        [
            new() { position = 1, support_card_id = 30001 },
            new() { position = 2, support_card_id = 30002 },
            new() { position = 3, support_card_id = 30003 },
            new() { position = 4, support_card_id = 30004 },
            new() { position = 5, support_card_id = 30241 }
        ];
        chara.evaluation_info_array =
        [
            .. Enumerable.Range(1, 5).Select(position => new EvaluationInfo
            {
                target_id = position,
                evaluation = position == 4 ? 60 : 90
            })
        ];
        home.command_info_array[0].training_partner_array = [1, 2, 4, 3, 5];
    }

    static void VerifyPartnerFramebuffer(string screen, string pluginId, string speed, string wit, string friend)
    {
        var previous = -1;
        foreach (var token in new[] { $"[{friend}]Friend", $"[{friend}]Group", $"[{speed}]Ready", $"[{speed}]Low", $"[{wit}]Mismatch" })
        {
            var index = screen.IndexOf(token, StringComparison.Ordinal);
            if (index <= previous)
                throw new InvalidOperationException($"{pluginId}: localized partner '{token}' is missing or has the wrong priority.");
            previous = index;
        }
        if (pluginId == "OldScenarioAnalyzer")
            return; // OldScenarioAnalyzer exposes friendship priority through row order.
        foreach (var (name, shining) in new[] { ("Ready", true), ("Low", false), ("Mismatch", false), ("Friend", false), ("Group", false) })
        {
            var line = screen.Split('\n').Distinct().Single(row => row.Contains("]" + name, StringComparison.Ordinal));
            if (line.Contains('★') != shining)
                throw new InvalidOperationException($"{pluginId}: {name} friendship training changed with the UI language; 30241 must keep this scenario's existing rule.");
        }
    }

    static void VerifyOldScenarioDetails(WorkspaceSmokeSession ui, string culture)
    {
        using var directory = new TempCurrentDirectory("OldScenarioLocalizedDetails");
        using var context = new RuntimePluginContext(ui.Application);
        using var eventContext = new RuntimePluginContext(ui.Application);
        var eventLogger = new EventLoggerPlugin.EventLoggerPlugin();
        var plugin = new OldScenarioAnalyzer.OldScenarioAnalyzer();
        var workspace = Workspace.Create("OldScenarioAnalyzer");
        try
        {
            InitializePartnerDatabase();
            eventLogger.Initialize(eventContext);
            plugin.Initialize(context);
            // The current registration accepts only the URA DTO. Exercise the existing
            // scenario core with real Free/Arc DTOs without claiming endpoint coverage.
            var analyze = plugin.GetType().GetMethod("AnalyzeCore", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var chara = CreateChara(4);
            var home = CreateHomeInfo();
            AddPartners(chara, home);
            var response = new SingleModeFreeCheckEventResponse
            {
                data = new()
                {
                    chara_info = chara, home_info = home, unchecked_event_array = [],
                    race_condition_array = [], select_index_info_array = [],
                    free_data_set = new()
                    {
                        command_info_array = [], user_item_info_array = [],
                        pick_up_item_info_array =
                        [
                            .. new[] { 1201, 2001, 2101, 1001, 11002 }.Select(id => new SingleModeFreePickUpItem { item_id = id })
                        ]
                    }
                }
            };
            foreach (var promoteTea in new[] { false, true })
            {
                response.data.free_data_set.user_item_info_array = promoteTea ? [new() { item_id = 2301, num = 1 }] : [];
                ((ValueTask)analyze.Invoke(plugin, [response, 4])!).GetAwaiter().GetResult();
                workspace.SwitchTo();
                var screen = PluginCase.CaptureScrollablePanel(ui);
                foreach (var (id, promoted) in new[] { (1201, true), (2001, true), (2101, promoteTea), (1001, false), (11002, true) })
                {
                    var token = Database.ClimaxItem[id] + ":1/";
                    if (!screen.Contains(token, StringComparison.Ordinal) || screen.Contains("*" + token, StringComparison.Ordinal) != promoted)
                        throw new InvalidOperationException($"OldScenarioAnalyzer {culture}: item {id} promotion changed (tea={promoteTea}).");
                }
                var energy = culture switch { "zh-CN" => "体力+20", "en-US" => "Energy+20", _ => "体力+20" };
                var tea = culture switch { "zh-CN" => "苦茶", "en-US" => "Bitter tea", _ => "苦いお茶" };
                if (!screen.Contains(energy, StringComparison.Ordinal) || !screen.Contains(tea, StringComparison.Ordinal))
                    throw new InvalidOperationException($"OldScenarioAnalyzer {culture}: item names did not follow the UI language.");
            }

            chara.scenario_id = 6;
            chara.turn = 3;
            var arc = new SingleModeArcCheckEventResponse
            {
                data = new()
                {
                    chara_info = chara, home_info = home, unchecked_event_array = [],
                    race_condition_array = [], select_index_info_array = [],
                    arc_data_set = new()
                    {
                        arc_info = new(), command_info_array = [],
                        evaluation_info_array = [.. Enumerable.Range(1, 5).Select(position => new ArcEvaluationInfo { target_id = position, chara_id = 1000 + position })],
                        arc_rival_array = [new() { chara_id = 1005, command_id = 101, selection_peff_array = [new() { effect_group_id = 1 }] }]
                    }
                }
            };
            ((ValueTask)analyze.Invoke(plugin, [arc, 6])!).GetAwaiter().GetResult();
            if (!PluginCase.CaptureScrollablePanel(ui).Contains("格数2|满数0", StringComparison.Ordinal))
                throw new InvalidOperationException($"OldScenarioAnalyzer {culture}: card 30241 incorrectly added an L'Arc friendship charge.");
        }
        finally
        {
            plugin.DisposeAsync().GetAwaiter().GetResult();
            eventLogger.DisposeAsync().GetAwaiter().GetResult();
            workspace.Remove();
        }
    }
}
