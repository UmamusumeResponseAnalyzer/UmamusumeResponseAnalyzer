using EventLoggerPlugin;
using Gallop;
using Gallop.Endpoints;
using MessagePack;
using PioneerScenarioAnalyzer;
using System.Reflection;
using System.Runtime.Loader;
using System.Text;
using Terminal.Gui.Drawing;
using Terminal.Gui.Text;
using Terminal.Gui.ViewBase;
using UmamusumeResponseAnalyzer;
using UmamusumeResponseAnalyzer.Plugin;
using UmamusumeResponseAnalyzer.TerminalGui;
using PioneerPlugin = PioneerScenarioAnalyzer.PioneerScenarioAnalyzer;

static class PioneerEventLoggerRuntimeSmoke
{
    public static void RunWithoutPioneer(WorkspaceSmokeSession ui)
    {
        using var directory = new TempCurrentDirectory("EventLogger-without-Pioneer");
        using var context = new RuntimePluginContext(ui.Application);
        var loadContext = new WithoutPioneerLoadContext();
        var assembly = loadContext.LoadFromAssemblyPath(typeof(EventLoggerPlugin.EventLoggerPlugin).Assembly.Location);
        var plugin = (IPlugin)Activator.CreateInstance(assembly.GetType("EventLoggerPlugin.EventLoggerPlugin", throwOnError: true)!)!;
        try
        {
            plugin.Initialize(context);
            Require(!loadContext.Assemblies.Any(item => item.GetName().Name == "PioneerScenarioAnalyzer"),
                "EventLogger loaded the missing optional Pioneer dependency.");
        }
        finally
        {
            plugin.DisposeAsync().GetAwaiter().GetResult();
            loadContext.Unload();
        }
    }

    public static void Run(WorkspaceSmokeSession ui)
    {
        using var directory = new TempCurrentDirectory("Pioneer-EventLogger-packets");
        using var context = new RuntimePluginContext(ui.Application, new HashSet<string> { "PioneerScenarioAnalyzer" });
        var pioneer = new PioneerPlugin();
        var logger = new EventLoggerPlugin.EventLoggerPlugin();
        pioneer.Initialize(context);
        logger.Initialize(context);
        try
        {
            RunSession(ui, context);
            RunSummer(context);
            RunEventEndpoints(context);
        }
        finally
        {
            logger.DisposeAsync().GetAwaiter().GetResult();
            pioneer.DisposeAsync().GetAwaiter().GetResult();
            ui.Bootstrap.SwitchTo();
            ui.CaptureScreen(120, 40, restore: false);
        }
    }

    static void RunSession(WorkspaceSmokeSession ui, RuntimePluginContext context)
    {
        const int charaId = 9101;
        var initial = Response(charaId, 1);
        Dispatch(context, "load", new SingleModePioneerLoadResponse
        {
            data = new()
            {
                pioneer_data_set = initial.data.pioneer_data_set,
                single_mode_load_common = new()
                {
                    chara_info = initial.data.chara_info,
                    home_info = initial.data.home_info,
                    unchecked_event_array = [Event(830001001)],
                    race_history = []
                }
            }
        });
        Require(EventLogger.Current is { IsStarted: true, Scenario: 11, CurrentTurn: 1 },
            "Pioneer pending-event load did not start the EventLogger session.");

        Dispatch(context, "check_event", Response(charaId, 1, speedGain: 10));
        Require(EventLogger.Current.CardEventCount == 1 && EventLogger.Current.AllEvents.Length == 1 &&
            EventLogger.Current.CardEvents.Single().Stats == 10,
            "The first Pioneer training display lost or duplicated the opening event.");
        var first = EventLogger.Current.Turns[1]!;
        Require(first.FiveTrainStats[0]!.FiveValueGain.SequenceEqual([2, 2, 2, 2, 2]) &&
            first.FiveTrainStats[4] is { PtGain: 10, FailureRate: 20 },
            "Pioneer training statistics did not include the scenario bonus.");

        Train(context, 1, 101);
        var failure = Exec(Response(charaId, 2, speedGain: 10), 101, failed: true);
        Dispatch(context, "exec_command", failure);
        Require(EventLogger.Current.Turns[1] is { PlayerChoice: 101, IsTrainingFailed: true },
            "Pioneer training failure was not attributed to the requested turn.");
        Dispatch(context, "exec_command", failure);
        Require(EventLogger.Current.Turns[2] is { IsTrainingFailed: false } && EventLogger.Current.CardEventCount == 1,
            "Duplicate training response contaminated the next turn or event count.");
        Train(context, 2, 105);
        Dispatch(context, "gain_skills", Response(charaId, 2, speedGain: 10));
        Require(EventLogger.Current.Turns[2]!.PlayerChoice == 105, "Same-turn refresh erased the recorded choice.");
        Dispatch(context, "exec_command", Exec(Response(charaId, 3, speedGain: 10), 105));
        Require(EventLogger.Current.Turns[3]!.TrainLevelCount[1] == 1,
            "Pioneer successful training did not advance the training-level count.");

        var inheritance = Response(charaId, 3, speedGain: 10);
        inheritance.data.unchecked_event_array = [new()
        {
            story_id = 400000040,
            event_contents_info = new() { choice_array = [] },
            succession_event_info = new() { effect_type = 1 }
        }];
        Dispatch(context, "check_event", inheritance);
        var inherited = Response(charaId, 3, speedGain: 55, ptGain: 7);
        Dispatch(context, "check_event", inherited);
        Require(EventLogger.Current.InheritStats.SequenceEqual([45]), "Pioneer succession statistics were lost.");
        Dispatch(context, "race_end", new SingleModePioneerRaceEndResponse
        {
            data = new()
            {
                chara_info = inherited.data.chara_info,
                home_info = inherited.data.home_info,
                pioneer_data_set = inherited.data.pioneer_data_set,
                race_history = [new() { turn = 2, result_rank = 1 }]
            }
        });
        RequireExtras(ui, "EventLogger", "连续事件出现 1 次", "训练赌博: 2次", "事件数: 1", "继承属性: 45, PT: 7", "胜场: 1");
        var savedId = new PioneerTrainingDisplayId(charaId, 3);

        var island = Response(charaId, 4, speedGain: 155, ptGain: 7);
        Dispatch(context, "shima_training_exec", new SingleModePioneerShimaTrainingExecRequest { current_turn = 3 }, AnalyzerKind.Request);
        Dispatch(context, "shima_training_exec", new SingleModePioneerShimaTrainingExecResponse
        {
            data = new()
            {
                chara_info = island.data.chara_info,
                home_info = island.data.home_info,
                pioneer_data_set = island.data.pioneer_data_set,
                command_result = new() { result_state = 0 },
                unchecked_event_array = [Event(830001002)]
            }
        });
        Dispatch(context, "check_event", Response(charaId, 4, speedGain: 161, ptGain: 7));
        Require(EventLogger.Current.CardEventCount == 2 && EventLogger.Current.CardEvents.Last().Stats == 6,
            "Island training gains were attributed to the following support event.");
        Require(EventLogger.Current.Turns[3]!.PlayerChoice == 3101 &&
            EventLoggerDisplaySource.Current.TrainingFailures?.GambleTimes == 2,
            "Island training was not recorded separately from the five normal trainings.");
        RequireExtras(ui, "连续事件出现 2 次", "继承属性: 45, PT: 7");

        Dispatch(context, "check_event", Response(charaId + 1, 1));
        Require(EventLogger.Current.CardEventCount == 0 && EventLogger.Current.AllEvents.Length == 0 &&
            EventLogger.Current.InheritStats.Length == 0 && EventLogger.Current.RaceHistory.Length == 0 &&
            EventLogger.Current.Turns[3] is null,
            "New Pioneer session retained events, succession, race, or training data from the prior run.");
        Require(!ui.CaptureScreen(180, 80).Contains("继承属性:", StringComparison.Ordinal),
            "New Pioneer display retained the prior session's Extra rows.");
        Require(PioneerTrainingDisplay.Show(savedId), "Previous Pioneer history could not be reopened.");
        RequireExtras(ui, "连续事件出现 1 次", "继承属性: 45, PT: 7", "胜场: 1");
    }

    static void RunSummer(RuntimePluginContext context)
    {
        int[] normalIds = [101, 105, 102, 103, 106];
        for (var index = 0; index < normalIds.Length; index++)
        {
            var charaId = 9200 + index;
            Dispatch(context, "check_event", Response(charaId, 37, summer: true));
            var commandId = 3601 + index;
            Train(context, 37, commandId);
            Dispatch(context, "exec_command", Exec(Response(charaId, 38, summer: true), commandId));
            Require(EventLogger.Current.Turns[37]!.PlayerChoice == normalIds[index],
                $"Summer command {commandId} was not normalized to the correct training.");
            Require(EventLogger.Current.Turns[38]!.TrainLevelCount.All(count => count == 0),
                "Summer training incorrectly advanced normal training-level counters.");
        }
    }

    static void RunEventEndpoints(RuntimePluginContext context)
    {
        const int charaId = 9301;
        var initial = Response(charaId, 1);
        Dispatch(context, "start", new SingleModePioneerStartResponse
        {
            data = new()
            {
                pioneer_data_set = initial.data.pioneer_data_set,
                single_mode_start_common = new()
                {
                    chara_info = initial.data.chara_info,
                    home_info = initial.data.home_info,
                    unchecked_event_array = [Event(500001000)]
                }
            }
        });
        Dispatch(context, "check_event", Response(charaId, 1, speedGain: 10));
        Require(EventLogger.Current.AllEvents is [{ Stats: 10 }], "Pioneer start lost its first event or retained the preceding session.");
        string[] endpoints = ["continue", "planning", "pioneer_live", "gain_skills"];
        for (var index = 0; index < endpoints.Length; index++)
        {
            var pending = Response(charaId, 1, speedGain: (index + 1) * 10);
            pending.data.unchecked_event_array = [Event(500001001 + index)];
            Dispatch(context, endpoints[index], pending);
            Dispatch(context, "check_event", Response(charaId, 1, speedGain: (index + 2) * 10));
            Require(EventLogger.Current.AllEvents.Length == index + 2 && EventLogger.Current.AllEvents.Last().Stats == 10,
                $"Pioneer {endpoints[index]} lost or duplicated its pending event.");
        }
    }

    static SingleModePioneerCheckEventResponse Response(int charaId, int turn, int speedGain = 0, int ptGain = 0, bool summer = false)
    {
        var response = Fixtures.Response(charaId: charaId, turn: turn, summer: summer);
        response.data.chara_info.speed += speedGain;
        response.data.chara_info.skill_point += ptGain;
        response.data.chara_info.support_card_array = [new() { position = 1, support_card_id = 30001 }];
        response.data.home_info.command_info_array[0].failure_rate = 25;
        return response;
    }

    static SingleModeEventInfo Event(int storyId)
        => new() { story_id = storyId, event_contents_info = new() { choice_array = [] } };

    static void Train(RuntimePluginContext context, int turn, int commandId)
        => Dispatch(context, "exec_command", new SingleModePioneerExecCommandRequest
        {
            single_mode_exec_command_request_common = new() { current_turn = turn, command_type = 1, command_id = commandId }
        }, AnalyzerKind.Request);

    static SingleModePioneerExecCommandResponse Exec(SingleModePioneerCheckEventResponse source, int commandId, bool failed = false)
        => new()
        {
            data = new()
            {
                chara_info = source.data.chara_info,
                home_info = source.data.home_info,
                pioneer_data_set = source.data.pioneer_data_set,
                unchecked_event_array = source.data.unchecked_event_array,
                command_result = new() { command_id = commandId, result_state = failed ? 1 : 0 }
            }
        };

    static void Dispatch(RuntimePluginContext context, string endpointName, object payload, AnalyzerKind kind = AnalyzerKind.Response)
    {
        var endpoint = GameEndpointCatalog.ByPath[$"/umamusume/single_mode_pioneer/{endpointName}"];
        var dispatch = new AnalyzerDispatchContext(endpoint, MessagePackSerializer.Serialize(payload.GetType(), payload), new(null, null, null, null, null, null));
        var handlers = context.AnalyzerRegistry.Registrations.Where(registration => registration.Kind == kind &&
            PluginManager.ExpandEndpointPatterns(registration.Patterns).Any(candidate => candidate.Path == endpoint.Path))
            .OrderBy(registration => registration.Priority).ToArray();
        Require(handlers.Length >= (kind == AnalyzerKind.Request || endpointName == "start" ? 1 : 2),
            $"Missing Pioneer/EventLogger handlers for {endpoint.Path}.");
        foreach (var handler in handlers)
            handler.Invoke(dispatch).GetAwaiter().GetResult();
    }

    static void RequireExtras(WorkspaceSmokeSession ui, params string[] tokens)
    {
        var cells = ui.CaptureCells(180, 80);
        var frame = ui.InvokeOnOwner(() => Descendants(ui.Application.TopRunnableView!)
            .Single(view => view.Id == "pioneer-extras").FrameToScreen());
        var lines = new string[frame.Height];
        for (var y = frame.Top; y < frame.Bottom; y++)
        {
            var line = new StringBuilder();
            for (var x = frame.Left; x < frame.Right;)
            {
                var grapheme = cells[y, x].Grapheme;
                line.Append(grapheme);
                x += Math.Max(1, grapheme.GetColumns());
            }
            lines[y - frame.Top] = line.ToString();
        }
        var text = string.Join('\n', lines);
        foreach (var token in tokens)
            Require(text.Contains(token, StringComparison.Ordinal), $"Pioneer Extras lacks '{token}'.\n{text}");
        var titleY = Array.FindIndex(lines, line => line.Contains("EventLogger", StringComparison.Ordinal)) + frame.Top;
        var titleX = Enumerable.Range(frame.Left, frame.Width).First(x => cells[titleY, x].Grapheme == "E");
        Require(cells[titleY, titleX].Attribute?.Foreground == new Color(StandardColor.BrightCyan),
            "Pioneer EventLogger section lost its heading color.");
    }

    static IEnumerable<View> Descendants(View view)
    {
        yield return view;
        foreach (var child in view.SubViews)
            foreach (var descendant in Descendants(child))
                yield return descendant;
    }

    static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    sealed class WithoutPioneerLoadContext() : AssemblyLoadContext(isCollectible: true)
    {
        protected override Assembly? Load(AssemblyName name)
        {
            if (name.Name == "PioneerScenarioAnalyzer")
                throw new FileNotFoundException("PioneerScenarioAnalyzer is absent for this isolated-loading test.");
            return Default.Assemblies.FirstOrDefault(assembly => AssemblyName.ReferenceMatchesDefinition(assembly.GetName(), name))
                ?? Default.LoadFromAssemblyName(name);
        }
    }
}
