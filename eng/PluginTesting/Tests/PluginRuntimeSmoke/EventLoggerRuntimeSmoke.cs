using EventLoggerPlugin;
using Gallop;
using LegendScenarioAnalyzer;
using LegendPlugin = LegendScenarioAnalyzer.LegendScenarioAnalyzer;
using RamenPlugin = RamenScenarioAnalyzer.RamenScenarioAnalyzer;
using UmamusumeResponseAnalyzer.Plugin;
using UmamusumeResponseAnalyzer.TerminalGui;
using UiText = UmamusumeResponseAnalyzer.Localization.TerminalGui;

static partial class EventLoggerRuntimeSmoke
{
    public static void Run(WorkspaceSmokeSession ui)
    {
        using var currentDirectory = new TempCurrentDirectory("EventLoggerPlugin-training-failure");
        RunSuccessionChoiceDisplay(ui);
        RunLegacySuccessionStatDisplay(ui);
        RunScenarioSummaryPlacement(ui);

        using var context = new RuntimePluginContext(ui.Application);
        var plugin = new EventLoggerPlugin.EventLoggerPlugin();
        var workspace = Workspace.Create("事件记录");
        try
        {
            plugin.Initialize(context);
            workspace.SwitchTo();
            var registration = context.AnalyzerRegistry.Registrations.Single(candidate =>
                candidate.Kind == AnalyzerKind.Response
                && candidate.PayloadType == typeof(SingleModeExecCommandResponse)
                && candidate.Priority == -1);
            registration.Invoke(new SingleModeExecCommandResponse
            {
                data = new()
                {
                    command_result = new() { result_state = 1 },
                    chara_info = null!,
                    unchecked_event_array = null!,
                }
            }).GetAwaiter().GetResult();
            ui.Flush();

            var eventScreen = ui.CaptureScreen();
            if (!eventScreen.Contains("训练失败！", StringComparison.Ordinal)
                || !eventScreen.Contains(UiText.Severity_Warning, StringComparison.Ordinal))
                throw new InvalidOperationException("The scoped training-failure warning is not visible in the EventLogger framebuffer.");

            ui.Bootstrap.SwitchTo();
            if (ui.CaptureScreen().Contains("训练失败", StringComparison.Ordinal))
                throw new InvalidOperationException("The removed EventLogger print path wrote a global training-failure log.");
        }
        finally
        {
            plugin.Dispose();
        }

        RunTargetedLegendDisplaySequence(ui);
        RunScenarioTracking(ui);
    }

    static void RunLegacySuccessionStatDisplay(WorkspaceSmokeSession ui)
    {
        using var context = new RuntimePluginContext(
            ui.Application,
            new HashSet<string>(StringComparer.Ordinal)
            {
                "LegendScenarioAnalyzer",
            });
        var eventLogger = new EventLoggerPlugin.EventLoggerPlugin();
        var legend = new LegendPlugin();
        WriteLegendBuffCsv();
        try
        {
            eventLogger.Initialize(context);
            legend.Initialize(context);

            var loadUpdate = context.AnalyzerRegistry.Registrations.Single(candidate =>
                candidate.Kind == AnalyzerKind.Response &&
                candidate.PayloadType == typeof(SingleModeLoadResponse) &&
                candidate.Priority == -1);
            var loadSuccession = context.AnalyzerRegistry.Registrations.Single(candidate =>
                candidate.Kind == AnalyzerKind.Response &&
                candidate.PayloadType == typeof(SingleModeLoadResponse) &&
                candidate.Priority == 3);
            var checkUpdate = context.AnalyzerRegistry.Registrations.Single(candidate =>
                candidate.Kind == AnalyzerKind.Response &&
                candidate.PayloadType == typeof(SingleModeCheckEventResponse) &&
                candidate.Priority == -1);
            var checkSuccession = context.AnalyzerRegistry.Registrations.Single(candidate =>
                candidate.Kind == AnalyzerKind.Response &&
                candidate.PayloadType == typeof(SingleModeCheckEventResponse) &&
                candidate.Priority == 3);
            var scenarioShow = context.AnalyzerRegistry.Registrations.Single(candidate =>
                candidate.Kind == AnalyzerKind.Response &&
                candidate.PayloadType == typeof(SingleModeLegendCheckEventResponse) &&
                candidate.Priority == 1);

            const int charaId = 14;
            const int turn = 31;
            scenarioShow.Invoke(CreateLegendResponse(charaId, turn)).GetAwaiter().GetResult();
            ui.Flush();

            var load = CreateLegacySuccessionLoad(charaId, turn);
            loadUpdate.Invoke(load).GetAwaiter().GetResult();
            loadSuccession.Invoke(load).GetAwaiter().GetResult();
            ui.Flush();
            var loadScreen = ui.CaptureScreen();
            if (Workspace.Current is not { Title: "LegendScenarioAnalyzer" } ||
                EventLogger.Current.InheritStats.Length != 0 ||
                loadScreen.Contains("继承属性:", StringComparison.Ordinal) ||
                loadScreen.Contains("------ 继承选择 ------", StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "An effect_type-only succession load published a result or changed the ScenarioAnalyzer workspace.");
            }

            var result = CreateLegacySuccessionResult(charaId, turn);
            checkUpdate.Invoke(result).GetAwaiter().GetResult();
            scenarioShow.Invoke(CreateLegendResponse(charaId, turn)).GetAwaiter().GetResult();
            checkSuccession.Invoke(result).GetAwaiter().GetResult();
            ui.Flush();

            if (!EventLogger.Current.InheritStats.SequenceEqual([45]))
            {
                throw new InvalidOperationException(
                    $"Legacy succession stats: expected [45], got [{string.Join(", ", EventLogger.Current.InheritStats)}].");
            }
            if (Workspace.Current is not { Title: "LegendScenarioAnalyzer" })
                throw new InvalidOperationException("Legacy succession stats switched to EventLogger.");

            var cells = ui.CaptureCells(160, 80);
            const string inheritEffect = "继承属性: 45, PT: 7";
            var token = FindCellToken(cells, inheritEffect);
            var sectionTitle = FindCellToken(cells, "EventLogger");
            var extrasFrame = ui.InvokeOnOwner(() =>
                Descendants(ui.Application.TopRunnableView
                        ?? throw new InvalidOperationException("Runtime smoke has no top-level view."))
                    .Single(view => view.Id == "legend-extras")
                    .FrameToScreen());
            if (token.Points.Any(point =>
                    point.X < extrasFrame.X || point.X >= extrasFrame.Right ||
                    point.Y < extrasFrame.Y || point.Y >= extrasFrame.Bottom))
            {
                throw new InvalidOperationException("Legacy succession stats were not rendered inside Legend Extra.");
            }
            if (sectionTitle.Points.Any(point =>
                    point.X < extrasFrame.X || point.X >= extrasFrame.Right ||
                    point.Y < extrasFrame.Y || point.Y >= extrasFrame.Bottom)
                || sectionTitle.Points[0].Y >= token.Points[0].Y)
            {
                throw new InvalidOperationException(
                    "Legacy succession stats were not grouped under the EventLogger Extra section.");
            }
            RequireForeground(
                cells,
                "EventLogger",
                0,
                new(Terminal.Gui.Drawing.StandardColor.BrightCyan),
                "legacy succession section title");
            RequireForeground(
                cells,
                inheritEffect,
                6,
                new(Terminal.Gui.Drawing.StandardColor.BrightCyan),
                "legacy succession stats");
            RequireForeground(
                cells,
                inheritEffect,
                14,
                new(Terminal.Gui.Drawing.StandardColor.BrightCyan),
                "legacy succession skill points");
            if (ui.CaptureScreen().Contains("------ 继承选择 ------", StringComparison.Ordinal))
                throw new InvalidOperationException("Legacy succession stats created an EventLogger succession panel.");

            result.data!.unchecked_event_array = [];
            result.data.event_effected_factor_array = [];
            checkUpdate.Invoke(result).GetAwaiter().GetResult();
            checkSuccession.Invoke(result).GetAwaiter().GetResult();
            ui.Flush();
            if (!EventLogger.Current.InheritStats.SequenceEqual([45]) ||
                Workspace.Current is not { Title: "LegendScenarioAnalyzer" })
            {
                throw new InvalidOperationException(
                    "A later response duplicated legacy succession stats or changed the foreground workspace.");
            }
        }
        finally
        {
            eventLogger.Dispose();
            legend.Dispose();
            ui.Bootstrap.SwitchTo();
        }
    }

    static void RunScenarioSummaryPlacement(WorkspaceSmokeSession ui)
    {
        RunLegendSummaryPlacement(ui);
        RunRamenSummaryPlacement(ui);
    }

    static void RunLegendSummaryPlacement(WorkspaceSmokeSession ui)
    {
        using var context = new RuntimePluginContext(
            ui.Application,
            new HashSet<string>(StringComparer.Ordinal)
            {
                "LegendScenarioAnalyzer",
            });
        var eventLogger = new EventLoggerPlugin.EventLoggerPlugin();
        var legend = new LegendPlugin();
        WriteLegendBuffCsv();
        try
        {
            eventLogger.Initialize(context);
            legend.Initialize(context);
            const int charaId = 810;
            var turn = PopulateEventLoggerScenarioSummary(
                context,
                scenario: (int)ScenarioType.Legend,
                charaId,
                startTurn: 1);
            var scenarioShow = context.AnalyzerRegistry.Registrations.Single(candidate =>
                candidate.Kind == AnalyzerKind.Response &&
                candidate.PayloadType == typeof(SingleModeLegendCheckEventResponse) &&
                candidate.Priority == 1);
            scenarioShow.Invoke(CreateLegendResponse(charaId, turn)).GetAwaiter().GetResult();
            ui.Flush();

            if (Workspace.Current is not { Title: "LegendScenarioAnalyzer" })
                throw new InvalidOperationException("Legend EventLogger summaries did not retain the scenario workspace.");
            RequireEventLoggerSummaryPlacement(
                ui,
                extraId: "legend-extras",
                importantId: "legend-important",
                cardBranchTokens: ["剩余 2 个连续事件", "完成概率:"]);
        }
        finally
        {
            eventLogger.Dispose();
            legend.Dispose();
            ui.Bootstrap.SwitchTo();
        }
    }

    static void RunRamenSummaryPlacement(WorkspaceSmokeSession ui)
    {
        using var context = new RuntimePluginContext(
            ui.Application,
            new HashSet<string>(StringComparer.Ordinal)
            {
                "RamenScenarioAnalyzer",
            });
        var eventLogger = new EventLoggerPlugin.EventLoggerPlugin();
        var ramen = new RamenPlugin();
        try
        {
            eventLogger.Initialize(context);
            ramen.Initialize(context);
            const int charaId = 813;
            var turn = PopulateEventLoggerScenarioSummary(
                context,
                scenario: (int)ScenarioType.Ramen,
                charaId,
                startTurn: 2);
            var scenarioShow = context.AnalyzerRegistry.Registrations.Single(candidate =>
                candidate.Kind == AnalyzerKind.Response &&
                candidate.PayloadType == typeof(SingleModeRamenExecCommandResponse) &&
                candidate.Priority == 1);
            scenarioShow.Invoke(CreateRamenResponse(charaId, turn)).GetAwaiter().GetResult();
            ui.Flush();

            if (Workspace.Current is not { Title: "RamenScenarioAnalyzer" })
                throw new InvalidOperationException("Ramen EventLogger summaries did not retain the scenario workspace.");
            RequireEventLoggerSummaryPlacement(
                ui,
                extraId: "ramen-extras",
                importantId: "ramen-important",
                cardBranchTokens: ["连续事件运气:"]);
        }
        finally
        {
            eventLogger.Dispose();
            ramen.Dispose();
            ui.Bootstrap.SwitchTo();
        }
    }

    static int PopulateEventLoggerScenarioSummary(
        RuntimePluginContext context,
        int scenario,
        int charaId,
        int startTurn)
    {
        var check = context.AnalyzerRegistry.Registrations.Single(candidate =>
            candidate.Kind == AnalyzerKind.Response &&
            candidate.PayloadType == typeof(SingleModeCheckEventResponse) &&
            candidate.Priority == -1);
        var request = context.AnalyzerRegistry.Registrations.Single(candidate =>
            candidate.Kind == AnalyzerKind.Request &&
            candidate.PayloadType == typeof(SingleModeExecCommandRequest));
        var exec = context.AnalyzerRegistry.Registrations.Single(candidate =>
            candidate.Kind == AnalyzerKind.Response &&
            candidate.PayloadType == typeof(SingleModeExecCommandResponse) &&
            candidate.Priority == -1);

        check.Invoke(CreateEventSummaryResponse(scenario, charaId, startTurn)).GetAwaiter().GetResult();
        check.Invoke(CreateEventSummaryResponse(scenario, charaId, startTurn + 1, 830001001))
            .GetAwaiter()
            .GetResult();
        check.Invoke(CreateEventSummaryResponse(scenario, charaId, startTurn + 2)).GetAwaiter().GetResult();
        request.Invoke(new SingleModeExecCommandRequest
        {
            single_mode_exec_command_request_common = new()
            {
                command_type = 1,
                command_id = 101,
                current_turn = startTurn + 2,
            },
        }).GetAwaiter().GetResult();
        exec.Invoke(CreateEventSummaryFailureResponse(scenario, charaId, startTurn + 3))
            .GetAwaiter()
            .GetResult();
        return startTurn + 3;
    }

    static void RequireEventLoggerSummaryPlacement(
        WorkspaceSmokeSession ui,
        string extraId,
        string importantId,
        string[] cardBranchTokens)
    {
        var cells = ui.CaptureCells(180, 80);
        var frames = ui.InvokeOnOwner(() =>
        {
            var views = Descendants(ui.Application.TopRunnableView
                    ?? throw new InvalidOperationException("Runtime smoke has no top-level view."))
                .ToArray();
            return (
                Extra: views.Single(view => view.Id == extraId).FrameToScreen(),
                Important: views.Single(view => view.Id == importantId).FrameToScreen());
        });
        string[] tokens =
        [
            "连续事件出现 1 次",
            "走完 0 张卡",
            .. cardBranchTokens,
            "训练赌博: 1次",
            "事件数: 1",
        ];
        foreach (var text in tokens)
        {
            var token = FindCellToken(cells, text);
            if (token.Points.Any(point =>
                    point.X < frames.Extra.X || point.X >= frames.Extra.Right ||
                    point.Y < frames.Extra.Y || point.Y >= frames.Extra.Bottom))
            {
                throw new InvalidOperationException($"EventLogger summary '{text}' was not rendered inside Extra.");
            }
            if (token.Points.Any(point =>
                    point.X >= frames.Important.X && point.X < frames.Important.Right &&
                    point.Y >= frames.Important.Y && point.Y < frames.Important.Bottom))
            {
                throw new InvalidOperationException($"EventLogger summary '{text}' remained inside Important.");
            }
        }

        RequireForeground(
            cells,
            "EventLogger",
            0,
            new(Terminal.Gui.Drawing.StandardColor.BrightCyan),
            "EventLogger section title");
        RequireForeground(
            cells,
            "连续事件出现 1 次",
            0,
            new(Terminal.Gui.Drawing.StandardColor.BrightYellow),
            "card event summary");
        RequireForeground(
            cells,
            "训练赌博: 1次",
            6,
            new(Terminal.Gui.Drawing.StandardColor.BrightYellow),
            "training failure summary");
        var sectionTitle = FindCellToken(cells, "EventLogger");
        if (sectionTitle.Points.Any(point =>
                point.X < frames.Extra.X || point.X >= frames.Extra.Right ||
                point.Y < frames.Extra.Y || point.Y >= frames.Extra.Bottom))
        {
            throw new InvalidOperationException("EventLogger section title was not rendered inside Extra.");
        }
        var titleY = sectionTitle.Points[0].Y;
        var cardY = FindCellToken(cells, "连续事件出现 1 次").Points[0].Y;
        var failureY = FindCellToken(cells, "训练赌博: 1次").Points[0].Y;
        var eventCountY = FindCellToken(cells, "事件数: 1").Points[0].Y;
        if (titleY >= cardY || cardY >= failureY || failureY >= eventCountY)
        {
            throw new InvalidOperationException(
                "EventLogger summaries did not preserve card events, training failures, then existing Extra row order.");
        }
    }

    static void RunTargetedLegendDisplaySequence(WorkspaceSmokeSession ui)
    {
        using var context = new RuntimePluginContext(
            ui.Application,
            new HashSet<string>(StringComparer.Ordinal)
            {
                "LegendScenarioAnalyzer",
            });
        var eventLogger = new EventLoggerPlugin.EventLoggerPlugin();
        var legend = new LegendPlugin();
        using var observer = LegendTrainingDisplay.RegisterPartProducer("Observer");
        WriteLegendBuffCsv();
        try
        {
            eventLogger.Initialize(context);
            legend.Initialize(context);
            var eventUpdate = context.AnalyzerRegistry.Registrations.Single(candidate =>
                candidate.Kind == AnalyzerKind.Response &&
                candidate.PayloadType == typeof(SingleModeCheckEventResponse) &&
                candidate.Priority == -1);
            var scenarioShow = context.AnalyzerRegistry.Registrations.Single(candidate =>
                candidate.Kind == AnalyzerKind.Response &&
                candidate.PayloadType == typeof(SingleModeLegendCheckEventResponse) &&
                candidate.Priority == 1);

            var id1 = new LegendTrainingDisplayId(700, 1);
            var id2 = new LegendTrainingDisplayId(700, 2);
            var publications = new List<string>();
            observer.Update(id1, (_, display) =>
            {
                publications.Add("id1");
                display.Extra.AddText("DISPLAY-ID-1");
            });
            eventUpdate.Invoke(CreateResponse(10, id1.SingleModeCharaId, id1.Turn, legend: true))
                .GetAwaiter()
                .GetResult();
            scenarioShow.Invoke(CreateLegendResponse(id1.SingleModeCharaId, id1.Turn))
                .GetAwaiter()
                .GetResult();
            ui.Flush();
            var id1Frame = ui.CaptureScreen();
            var id1Visible = id1Frame.Contains("DISPLAY-ID-1", StringComparison.Ordinal);
            if (!id1Visible ||
                !publications.SequenceEqual(["id1"]))
            {
                throw new InvalidOperationException(
                    $"Initial Legend DisplayId was not published exactly once: "
                    + $"visible={id1Visible}, publications=[{string.Join(", ", publications)}].");
            }

            observer.Update(id2, (_, display) =>
            {
                publications.Add("id2");
                display.Extra.AddText("DISPLAY-ID-2");
            });
            ui.Flush();
            if (!string.Equals(id1Frame, ui.CaptureScreen(), StringComparison.Ordinal) ||
                !publications.SequenceEqual(["id1"]))
            {
                throw new InvalidOperationException("Updating an unshown DisplayId changed the framebuffer.");
            }

            eventUpdate.Invoke(CreateResponse(10, id2.SingleModeCharaId, id2.Turn, legend: true))
                .GetAwaiter()
                .GetResult();
            ui.Flush();
            if (!string.Equals(id1Frame, ui.CaptureScreen(), StringComparison.Ordinal) ||
                !publications.SequenceEqual(["id1"]))
            {
                throw new InvalidOperationException("EventLogger.Update(id2) republished or redrew id1.");
            }

            scenarioShow.Invoke(CreateLegendResponse(id2.SingleModeCharaId, id2.Turn))
                .GetAwaiter()
                .GetResult();
            ui.Flush();
            var id2Frame = ui.CaptureScreen();
            if (!id2Frame.Contains("DISPLAY-ID-2", StringComparison.Ordinal) ||
                id2Frame.Contains("DISPLAY-ID-1", StringComparison.Ordinal) ||
                !publications.SequenceEqual(["id1", "id2"]))
            {
                throw new InvalidOperationException("The new DisplayId was not committed exactly once.");
            }
        }
        finally
        {
            eventLogger.Dispose();
            legend.Dispose();
            ui.Bootstrap.SwitchTo();
        }
    }

    static void RunScenarioTracking(WorkspaceSmokeSession ui)
    {
        using var context = new RuntimePluginContext(ui.Application);
        var plugin = new EventLoggerPlugin.EventLoggerPlugin();
        try
        {
            plugin.Initialize(context);
            var check = context.AnalyzerRegistry.Registrations.Single(candidate =>
                candidate.Kind == AnalyzerKind.Response &&
                candidate.PayloadType == typeof(SingleModeCheckEventResponse) &&
                candidate.Priority == -1);
            var request = context.AnalyzerRegistry.Registrations.Single(candidate =>
                candidate.Kind == AnalyzerKind.Request &&
                candidate.PayloadType == typeof(SingleModeExecCommandRequest));
            var exec = context.AnalyzerRegistry.Registrations.Single(candidate =>
                candidate.Kind == AnalyzerKind.Response &&
                candidate.PayloadType == typeof(SingleModeExecCommandResponse) &&
                candidate.Priority == -1);

            check.Invoke(CreateResponse(10, charaId: 100, turn: 1, legend: true)).GetAwaiter().GetResult();
            var first = EventLogger.Current;
            var firstTurn = first.Turns[1]
                ?? throw new InvalidOperationException("Legend turn 1 was not recorded.");
            if (!first.IsFullGame || first.Scenario != (int)ScenarioType.Legend ||
                first.CurrentTurn != 1 || firstTurn.Motivation != 5 ||
                firstTurn.FiveTrainStats[0]?.FiveValueGain[0] != 10 ||
                firstTurn.FiveTrainStats[0]?.PtGain != 5 ||
                firstTurn.FiveTrainStats[0]?.VitalGain != -5 ||
                firstTurn.FiveTrainStats[0]?.FailureRate != 25 ||
                firstTurn.TrainLevel[0] != 3 ||
                !firstTurn.LegendFriendAtTrain[0] ||
                !firstTurn.LegendIsEffect104 ||
                firstTurn.LegendFriendClickEventCountConcerned ||
                !firstTurn.LegendFriendClickEvent)
            {
                throw new InvalidOperationException("Legend common-response state was not captured completely.");
            }

            request.Invoke(new SingleModeExecCommandRequest
            {
                single_mode_exec_command_request_common = new()
                {
                    command_type = 1,
                    command_id = 101,
                    current_turn = 1,
                }
            }).GetAwaiter().GetResult();
            exec.Invoke(new SingleModeExecCommandResponse
            {
                data = new()
                {
                    chara_info = CreateChara(10, 100, 2, legend: true),
                    home_info = CreateHomeInfo(legend: true),
                    command_result = new() { command_id = 101, result_state = 1 },
                    unchecked_event_array = [],
                }
            }).GetAwaiter().GetResult();
            var afterFailure = EventLogger.Current;
            if (afterFailure.Turns[1] is not { PlayerChoice: 101, IsTrainingFailed: true })
                throw new InvalidOperationException("Training request and failed result were not attached to turn 1.");

            exec.Invoke(new SingleModeExecCommandResponse
            {
                data = new()
                {
                    chara_info = CreateChara(10, 100, 2, legend: true),
                    home_info = CreateHomeInfo(legend: true),
                    command_result = new() { command_id = 101, result_state = 1 },
                    unchecked_event_array = [],
                }
            }).GetAwaiter().GetResult();
            if (EventLogger.Current.Turns[2] is { IsTrainingFailed: true })
                throw new InvalidOperationException("Duplicate failed response was attached to the next turn.");

            check.Invoke(CreateResponse(13, charaId: 200, turn: 7, legend: false)).GetAwaiter().GetResult();
            var ramen = EventLogger.Current;
            if (ramen.IsFullGame || ramen.Scenario != (int)ScenarioType.Ramen || ramen.CurrentTurn != 7 ||
                ramen.Turns[1] is not null || EventLoggerDisplaySource.Current.ScenarioFriend is not null)
            {
                throw new InvalidOperationException("Ramen session reset or friend-stat isolation failed.");
            }

            check.Invoke(CreateResponse(13, charaId: 200, turn: 7, legend: false)).GetAwaiter().GetResult();
            if (EventLogger.Current.CurrentTurn != 7 || EventLogger.Current.Turns[7] is null)
                throw new InvalidOperationException("Duplicate Ramen response changed the tracked turn.");
        }
        finally
        {
            plugin.Dispose();
        }
    }

    static SingleModeLegendCheckEventResponse CreateLegendResponse(int charaId, int turn)
    {
        var commandIds = new[] { 101, 105, 102, 103, 106 };
        var targetTypes = new[] { 1, 2, 3, 4, 5 };
        var chara = CreateChara(10, charaId, turn, legend: true);
        chara.max_speed = 1500;
        chara.max_stamina = 1500;
        chara.max_power = 1500;
        chara.max_guts = 1500;
        chara.max_wiz = 1500;
        chara.skill_point = 100;
        chara.state = 1;
        chara.evaluation_info_array = [new() { target_id = 2, evaluation = 80 }];

        return new()
        {
            data = new()
            {
                chara_info = chara,
                home_info = new()
                {
                    command_info_array =
                    [
                        .. commandIds.Select((commandId, index) => new SingleModeCommandInfo
                        {
                            command_type = 1,
                            command_id = commandId,
                            is_enable = 1,
                            failure_rate = index * 5,
                            training_partner_array = [],
                            tips_event_partner_array = [],
                            sub_command_partner_array = [],
                            params_inc_dec_info_array =
                            [
                                new() { target_type = targetTypes[index], value = index + 1 },
                                new() { target_type = 30, value = 5 },
                                new() { target_type = 10, value = -5 },
                            ],
                        }),
                    ],
                },
                unchecked_event_array = [],
                legend_data_set = new()
                {
                    command_info_array =
                    [
                        .. commandIds.Select((commandId, index) => new SingleModeLegendCommandInfo
                        {
                            command_type = 1,
                            command_id = commandId,
                            legend_id = 9046 + index % 3,
                            gain_gauge = index + 1,
                            friend_gauge_gain_array = [],
                            params_inc_dec_info_array =
                            [
                                new() { target_type = targetTypes[index], value = index + 10 },
                                new() { target_type = 30, value = 5 },
                            ],
                        }),
                    ],
                    evaluation_info_array = [],
                    gauge_count_array =
                    [
                        new() { legend_id = 9046, count = 2 },
                        new() { legend_id = 9047, count = 4 },
                        new() { legend_id = 9048, count = 6 },
                    ],
                    buff_info_array = [],
                    obtainable_buff_id_array = [],
                    activated_buff_id_array = [],
                    masterly_bonus_info = new(),
                    race_history_array = [],
                },
                select_index_info_array = [],
            },
        };
    }

    static SingleModeCheckEventResponse CreateEventSummaryResponse(
        int scenario,
        int charaId,
        int turn,
        int? storyId = null)
        => new()
        {
            data = new()
            {
                chara_info = CreateEventSummaryChara(scenario, charaId, turn),
                home_info = CreateHomeInfo(legend: scenario == (int)ScenarioType.Legend),
                unchecked_event_array = storyId is { } value
                    ?
                    [
                        new()
                        {
                            story_id = value,
                            event_contents_info = new() { choice_array = [] },
                        },
                    ]
                    : [],
                select_index_info_array = [],
            },
        };

    static SingleModeExecCommandResponse CreateEventSummaryFailureResponse(
        int scenario,
        int charaId,
        int turn)
        => new()
        {
            data = new()
            {
                chara_info = CreateEventSummaryChara(scenario, charaId, turn),
                home_info = CreateHomeInfo(legend: scenario == (int)ScenarioType.Legend),
                command_result = new() { command_id = 101, result_state = 1 },
                unchecked_event_array = [],
            },
        };

    static SingleModeChara CreateEventSummaryChara(int scenario, int charaId, int turn)
    {
        var chara = CreateChara(
            scenario,
            charaId,
            turn,
            legend: scenario == (int)ScenarioType.Legend);
        chara.support_card_array = [new() { support_card_id = 30001, position = 1 }];
        return chara;
    }

    static SingleModeRamenExecCommandResponse CreateRamenResponse(int charaId, int turn)
    {
        var chara = CreateChara((int)ScenarioType.Ramen, charaId, turn, legend: false);
        chara.max_speed = 1500;
        chara.max_stamina = 1500;
        chara.max_power = 1500;
        chara.max_guts = 1500;
        chara.max_wiz = 1500;
        chara.skill_point = 100;
        chara.state = 1;
        var home = CreateHomeInfo(legend: false);
        foreach (var command in home.command_info_array)
            command.is_enable = 1;

        return new()
        {
            data = new()
            {
                chara_info = chara,
                race_condition_array = [],
                gain_parameter_info = new(),
                not_up_parameter_info = new(),
                not_down_parameter_info = new(),
                gain_partner_support_effect_array = [],
                home_info = home,
                command_result = new() { command_id = 101, result_state = 1 },
                unchecked_event_array = [],
                ramen_data_set = new()
                {
                    command_info_array =
                    [
                        .. new[] { 101, 105, 102, 103, 106 }.Select(commandId =>
                            new SingleModeRamenCommandInfo
                            {
                                command_type = 1,
                                command_id = commandId,
                                params_inc_dec_info_array = [],
                            }),
                    ],
                    evaluation_info_array = [],
                    feeling_reduce_turn_info_array = [],
                    feeling_turn_info_array = [],
                    feeling_info_array = [],
                    not_up_parameter_info = new(),
                    active_effect_array = [],
                    uraf_effect_info = null!,
                    command_feeling_info_array = [],
                    training_exec_info_array = [],
                    reduce_base_turn_info_array = [],
                    all_selected_region_id_array = [],
                },
            },
        };
    }

    static SingleModeLoadResponse CreateLegacySuccessionLoad(int charaId, int turn)
    {
        var chara = CreateChara(10, charaId, turn, legend: true);
        chara.speed = 312;
        chara.stamina = 347;
        chara.power = 277;
        chara.guts = 323;
        chara.wiz = 308;
        chara.skill_point = 865;

        return new()
        {
            data = new()
            {
                single_mode_load_common = new()
                {
                    chara_info = chara,
                    home_info = CreateHomeInfo(legend: true),
                    race_history = [],
                    unchecked_event_array =
                    [
                        new()
                        {
                            story_id = 400000040,
                            event_contents_info = new() { choice_array = [] },
                            succession_event_info = new() { effect_type = 1 },
                        },
                    ],
                },
            },
        };
    }

    static SingleModeCheckEventResponse CreateLegacySuccessionResult(int charaId, int turn)
    {
        var chara = CreateChara(10, charaId, turn, legend: true);
        chara.speed = 329;
        chara.stamina = 348;
        chara.power = 294;
        chara.guts = 323;
        chara.wiz = 318;
        chara.skill_point = 872;

        return new()
        {
            data = new()
            {
                chara_info = chara,
                home_info = CreateHomeInfo(legend: true),
                unchecked_event_array =
                [
                    new()
                    {
                        story_id = 501007733,
                        event_contents_info = new() { choice_array = [] },
                    },
                ],
                event_effected_factor_array =
                [
                    .. Enumerable.Range(1, 6).Select(position => new SuccessionEffectedFactor
                    {
                        position = position,
                        factor_info_array = [],
                    }),
                ],
                select_index_info_array = [],
            },
        };
    }

    static void WriteLegendBuffCsv()
    {
        var dataDirectory = Path.Combine("PluginData", "LegendScenarioAnalyzer");
        Directory.CreateDirectory(dataDirectory);
        File.WriteAllText(
            Path.Combine(dataDirectory, "legend_buff.csv"),
            "cn_effect,name,rank,color,condition,buffId,isTrigger,isPerson,youQing,ganJing,xunLian,hintLv,hintCount,deYiLv,buZaiLv,jiBan,vitalCostDrop,fenShen,mood,note");
    }

    static SingleModeCheckEventResponse CreateResponse(int scenario, int charaId, int turn, bool legend)
        => new()
        {
            data = new()
            {
                chara_info = CreateChara(scenario, charaId, turn, legend),
                home_info = CreateHomeInfo(legend),
                unchecked_event_array = legend
                    ? [new()
                    {
                        story_id = 830241003,
                        event_contents_info = new() { choice_array = [] },
                    }]
                    : [],
                select_index_info_array = [],
            }
        };

    static SingleModeChara CreateChara(int scenario, int charaId, int turn, bool legend)
        => new()
        {
            single_mode_chara_id = charaId,
            scenario_id = scenario,
            turn = turn,
            playing_state = 1,
            motivation = 5,
            speed = 100,
            stamina = 100,
            power = 100,
            guts = 100,
            wiz = 100,
            vital = 50,
            max_vital = 100,
            skill_array = [],
            skill_tips_array = [],
            support_card_array = legend
                ? [new() { support_card_id = 30241, position = 2 }]
                : [],
            training_level_info_array = [new() { command_id = 101, level = 3 }],
            chara_effect_id_array = legend ? [104] : [],
        };

    static SingleModeHomeInfo CreateHomeInfo(bool legend)
        => new()
        {
            command_info_array =
            [
                Training(101, 1, legend ? [2] : []),
                Training(105, 2),
                Training(102, 3),
                Training(103, 4),
                Training(106, 5),
            ]
        };

    static SingleModeCommandInfo Training(int commandId, int parameter, int[]? partners = null)
        => new()
        {
            command_type = 1,
            command_id = commandId,
            failure_rate = commandId == 101 ? 25 : 0,
            training_partner_array = partners ?? [],
            params_inc_dec_info_array =
            [
                new() { target_type = parameter, value = 10 },
                new() { target_type = 30, value = 5 },
                new() { target_type = 10, value = -5 },
            ],
        };
}
