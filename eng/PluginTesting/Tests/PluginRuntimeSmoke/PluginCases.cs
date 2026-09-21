using System.IO.Compression;
using System.Text;
using Gallop;
using Gallop.Endpoints;
using Newtonsoft.Json;
using UmamusumeResponseAnalyzer;
using UmamusumeResponseAnalyzer.Entities;
using UmamusumeResponseAnalyzer.TerminalGui;
using HostSkillData = UmamusumeResponseAnalyzer.Entities.SkillData;

static partial class PluginCases
{
    static readonly int[] BaseTrainingIds = [101, 105, 102, 103, 106];

    public static IReadOnlyList<PluginCase> All { get; } =
    [
        new(
            "AIRedirector",
            static () => new AIRedirector.AIRedirector()),
        new(
            "BreedersScenarioAnalyzer",
            static () => new BreedersScenarioAnalyzer.BreedersScenarioAnalyzer(),
            new(
                static () => CreateBreedersTrainingResponse(),
                "BreedersScenarioAnalyzer",
                "training",
                "训练分析",
                FullBleed: true,
                RequiresEventLogger: true,
                VisibleText: "== 训练信息 ==",
                CreateUpdatedResponse: static () => CreateBreedersTrainingResponse(73),
                InitialUpdateText: "80/100",
                UpdatedVisibleText: "73/100",
                History: new(
                    static (vital, charaId, turn) => CreateBreedersTrainingResponse(vital, charaId, turn)))),
        new(
            "CookScenarioAnalyzer",
            static () => new CookScenarioAnalyzer.CookScenarioAnalyzer(),
            new(
                static () => CreateCookTrainingResponse(),
                "CookScenarioAnalyzer",
                "training",
                "训练分析",
                FullBleed: true,
                RequiresEventLogger: true,
                VisibleText: "== 训练信息 ==",
                CreateUpdatedResponse: static () => CreateCookTrainingResponse(73),
                InitialUpdateText: "80/100",
                UpdatedVisibleText: "63/100",
                History: new(
                    static (vital, charaId, turn) => CreateCookTrainingResponse(vital, charaId, turn)))),
        new(
            "DMMPlugin",
            static () => new DMMPlugin.DMMPlugin(),
            ExpectedBootstrapLogOnInitialize: "等待 URA 启动。"),
        new(
            "EventLoggerPlugin",
            static () => new EventLoggerPlugin.EventLoggerPlugin()),
        new(
            "EventResponseAnalyzer",
            static () => new EventResponseAnalyzer.EventResponseAnalyzer()),
        new(
            "ExamplePlugin",
            static () => new ExamplePlugin.ExamplePlugin()),
        new(
            "GamePacketCollector",
            static () => new GamePacketCollector.GamePacketCollectorPlugin(),
            ExpectedBootstrapLogOnInitialize: "GamePacketCollector"),
        new(
            "LegendScenarioAnalyzer",
            static () => new LegendScenarioAnalyzer.LegendScenarioAnalyzer()),
        new(
            "MechaScenarioAnalyzer",
            static () => new MechaScenarioAnalyzer.MechaScenarioAnalyzer(),
            new(
                static () => CreateMechaTrainingResponse(),
                "MechaScenarioAnalyzer",
                "training",
                "训练分析",
                FullBleed: true,
                RequiresEventLogger: true,
                VisibleText: "总Lv:",
                CreateUpdatedResponse: static () => CreateMechaTrainingResponse(73),
                InitialUpdateText: "80/100",
                UpdatedVisibleText: "73/100",
                History: new(
                    static (vital, charaId, turn) => CreateMechaTrainingResponse(vital, charaId, turn)))),
        new(
            "Notifications",
            static () => new Notifications.Notifications()),
        new(
            "OldScenarioAnalyzer",
            static () => new OldScenarioAnalyzer.OldScenarioAnalyzer(),
            new(
                static () => CreateOldTrainingResponse(),
                "OldScenarioAnalyzer",
                "training",
                "训练分析",
                FullBleed: true,
                RequiresEventLogger: true,
                VisibleText: "回合数：1/78",
                CreateUpdatedResponse: static () => CreateOldTrainingResponse(73),
                InitialUpdateText: "80/100",
                UpdatedVisibleText: "73/100",
                History: new(
                    static (vital, charaId, turn) => CreateOldTrainingResponse(vital, charaId, turn)))),
        new(
            "OnsenScenarioAnalyzer",
            static () => new OnsenScenarioAnalyzer.OnsenScenarioAnalyzer(),
            new(
                static () => CreateOnsenTrainingResponse(),
                "OnsenScenarioAnalyzer",
                "training",
                "训练分析",
                FullBleed: true,
                RequiresEventLogger: true,
                VisibleText: "== 训练信息 ==",
                CreateUpdatedResponse: static () => CreateOnsenTrainingResponse(73),
                InitialUpdateText: "80/100",
                UpdatedVisibleText: "63/100",
                History: new(
                    static (vital, charaId, turn) => CreateOnsenTrainingResponse(vital, charaId, turn)))),
        new(
            "PioneerScenarioAnalyzer",
            static () => new PioneerScenarioAnalyzer.PioneerScenarioAnalyzer(),
            new(
                static () => CreatePioneerTrainingResponse(),
                "PioneerScenarioAnalyzer",
                "training",
                "训练分析",
                FullBleed: true,
                RequiresEventLogger: true,
                VisibleText: "== 训练信息 ==",
                CreateUpdatedResponse: static () => CreatePioneerTrainingResponse(73),
                InitialUpdateText: "80/100",
                UpdatedVisibleText: "73/100",
                History: new(
                    static (vital, charaId, turn) => CreatePioneerTrainingResponse(vital, charaId, turn)))),
        new(
            "RaceAnalyzer",
            static () => new RaceAnalyzer.RaceAnalyzer()),
        new(
            "RamenScenarioAnalyzer",
            static () => new RamenScenarioAnalyzer.RamenScenarioAnalyzer()),
        new(
            "SendGameStatusPlugin",
            static () => new SendGameStatusPlugin.SendGameStatusPlugin()),
        new(
            "SkillEffectPlugin",
            static () => CreateSkillEffectPlugin(),
            new(
                static () => CreateSkillEffectResponse(),
                "技能收益",
                "skill-effects",
                "技能收益",
                FullBleed: false,
                NotificationCount: 0,
                VisibleText: "runtime-effect-skill")),
        new(
            "SkillTipsResponseAnalyzer",
            static () => new SkillTipsResponseAnalyzer.SkillTipsResponseAnalyzer(),
            new(
                static () =>
                {
                    InitializeSkillTipsDatabase();
                    var response = CreateTerminalResponse(cardId: 0);
                    response.data.chara_info.skill_point = 10;
                    response.data.chara_info.skill_tips_array =
                    [
                        new()
                        {
                            group_id = 90001,
                            rarity = 1,
                            level = 0
                        }
                    ];
                    response.data.chara_info.chara_effect_id_array = [];
                    return response;
                },
                "SkillTipsResponseAnalyzer",
                "skill-plan",
                "技能评分建议",
                FullBleed: true,
                NotificationCount: 1,
                NotificationSeverity: UiSeverity.Warning,
                VisibleText: "[red]literal skill[/]",
                RemovesWorkspaceOnDispose: true)),
        new(
            "TeamStadiumOpponentListResponseAnalyzer",
            static () => new TeamStadiumOpponentListResponseAnalyzer.TeamStadiumOpponentListResponseAnalyzer(),
            new(
                static () => CreateTeamStadiumOpponentListResponse(),
                "TeamStadiumOpponentListResponseAnalyzer",
                "opponents",
                "对手列表",
                FullBleed: false,
                NotificationCount: 0,
                VisibleText: "[red]markup[/]")),
        new(
            "UAFScenarioAnalyzer",
            static () => new UAFScenarioAnalyzer.UAFScenarioAnalyzer(),
            new(
                static () => CreateUafTrainingResponse(),
                "UAFScenarioAnalyzer",
                "training",
                "训练分析",
                FullBleed: true,
                RequiresEventLogger: true,
                VisibleText: "== 训练信息 ==",
                CreateUpdatedResponse: static () => CreateUafTrainingResponse(73),
                InitialUpdateText: "80/100",
                UpdatedVisibleText: "53/100",
                History: new(
                    static (vital, charaId, turn) => CreateUafTrainingResponse(vital, charaId, turn)))),
        new(
            "WinSaddleAnalyzer",
            static () => new WinSaddleAnalyzer.WinSaddleAnalyzer(),
            new(
                static () => CreateFriendSimpleSearchResponse(),
                "WinSaddleAnalyzer",
                "friend",
                "好友",
                FullBleed: false,
                NotificationCount: 0,
                VisibleText: "simple friend"))
    ];

    static void InitializeSkillTipsDatabase()
    {
        InitializeDatabase(
            "SkillTips",
            [
                new()
                {
                    Id = 900001,
                    GroupId = 90001,
                    Rarity = 1,
                    Rate = 1,
                    Name = "[red]literal skill[/]",
                    DisplayName = "[red]literal skill[/]",
                    Cost = 10,
                    Grade = 100,
                    Propers = []
                }
            ],
            [],
            new Dictionary<int, string>(),
            []);
    }

    static SkillEffectPlugin.SkillEffectPlugin CreateSkillEffectPlugin()
    {
        InitializeDatabase(
            "SkillEffect",
            [
                new()
                {
                    Id = 900002,
                    GroupId = 90002,
                    Rarity = 1,
                    Rate = 1,
                    Name = "runtime-effect-skill",
                    DisplayName = "runtime-effect-skill",
                    Cost = 100,
                    Grade = 100,
                    Propers = []
                }
            ],
            [],
            new Dictionary<int, string>(),
            []);

        var dataDirectory = Path.Combine("PluginData", "SkillEffectPlugin");
        var effectDirectory = Path.Combine(dataDirectory, "runtime-course");
        Directory.CreateDirectory(effectDirectory);
        File.WriteAllText(
            Path.Combine(dataDirectory, "settings.json"),
            """
            {
              "DisplayOrder": 0,
              "MinimumExpectedEffect": 0,
              "Race": "runtime-course",
              "RunningStyle": "runtime-style",
              "URACloudBaseUrl": "http://127.0.0.1:4694",
              "AutoUpdateSkillEffects": false
            }
            """);
        File.WriteAllText(
            Path.Combine(effectDirectory, "runtime-style.json"),
            """
            [
              { "name": "runtime-effect-skill", "effect": "12.34" }
            ]
            """);

        return new();
    }

    static SingleModeCheckEventResponse CreateSkillEffectResponse()
    {
        var response = CreateTerminalResponse(cardId: 0);
        response.data.chara_info.skill_point = 100;
        response.data.chara_info.skill_tips_array =
        [
            new()
            {
                group_id = 90002,
                rarity = 1,
                level = 0
            }
        ];
        return response;
    }

    internal static void InitializeEventLoggerSuccessionDatabase()
        => InitializeDatabase(
            "EventLogger succession",
            [
                new()
                {
                    Name = "测试技能",
                    DisplayName = "测试技能",
                    Id = 100010,
                    GroupId = 10,
                    Rarity = 1,
                    Rate = 1,
                    Propers = [],
                },
                new()
                {
                    Name = "新增技能",
                    DisplayName = "新增技能",
                    Id = 100020,
                    GroupId = 20,
                    Rarity = 1,
                    Rate = 1,
                    Propers = [],
                },
            ],
            [],
            new Dictionary<int, string> { [1_000_001] = "测试白因子★" },
            []);

    static void InitializeWinSaddleDatabase()
        => InitializeDatabase(
            "WinSaddle",
            [],
            [new BaseName(100100, "fixture uma", "测试")],
            new Dictionary<int, string> { [1001] = "fixture factor" },
            [1001]);

    static void InitializeDatabase(
        string fixtureName,
        IReadOnlyList<HostSkillData> skills,
        IReadOnlyList<BaseName> names,
        IReadOnlyDictionary<int, string> factorIds,
        IReadOnlyList<int> saddleIds)
    {
        WriteBrotliJson(Database.EVENT_NAME_FILEPATH, new List<Story>());
        WriteBrotliJson(Database.NAMES_FILEPATH, names.ToList(), new() { TypeNameHandling = TypeNameHandling.All });
        WriteBrotliJson(Database.SKILLS_FILEPATH, skills);
        WriteBrotliJson(Database.SKILL_UPGRADE_SPECIALITY_FILEPATH, new List<SkillUpgradeSpeciality>());
        WriteBrotliJson(Database.TALENT_SKILLS_FILEPATH, new Dictionary<int, TalentSkillData[]>());
        WriteBrotliJson(Database.FACTOR_IDS_FILEPATH, factorIds);
        WriteBrotliJson(Database.SADDLE_IDS_FILEPATH, saddleIds);
        WriteBrotliJson(Database.SUCCESSION_RELATION_FILEPATH, new SuccessionRelationTable());

        if (Database.Initialize().GetAwaiter().GetResult() != DatabaseAvailability.Ready)
            throw new InvalidOperationException($"{fixtureName} database fixture did not load atomically.");
    }

    static void WriteBrotliJson<T>(string path, T value, JsonSerializerSettings? settings = null)
    {
        using var file = File.Create(path);
        using var brotli = new BrotliStream(file, CompressionLevel.SmallestSize);
        using var writer = new StreamWriter(brotli, Encoding.UTF8);
        using var json = new JsonTextWriter(writer);
        JsonSerializer.CreateDefault(settings).Serialize(json, value);
    }

    static TeamStadiumOpponentListResponse CreateTeamStadiumOpponentListResponse() => new()
    {
        data = new()
        {
            opponent_info_array =
            [
                new()
                {
                    strength = 3,
                    user_info = new()
                    {
                        name = "[red]markup[/]",
                        single_mode_play_count = 42
                    }
                }
            ]
        }
    };

    static FriendSimpleSearchResponse CreateFriendSimpleSearchResponse()
    {
        InitializeWinSaddleDatabase();
        return new()
        {
            data = new()
            {
                user_info_summary = new()
                {
                    name = "simple friend",
                    viewer_id = 123456789,
                    user_trained_chara_array =
                    [
                        new()
                        {
                            card_id = 100100,
                            factor_info_array = [new() { factor_id = 1001 }]
                        }
                    ]
                }
            }
        };
    }

    static SingleModeCheckEventResponse CreateTerminalResponse(int cardId) => new()
    {
        data = new()
        {
            chara_info = new()
            {
                state = 2,
                card_id = cardId,
                speed = 0,
                stamina = 0,
                power = 0,
                guts = 0,
                wiz = 0,
                skill_point = 0,
                skill_array = [],
                skill_tips_array = []
            },
            unchecked_event_array = []
        }
    };

    static SingleModeCheckEventResponse CreateOldTrainingResponse(
        int vital = 80,
        int singleModeCharaId = 1001,
        int turn = 1) => new()
    {
        data = new()
        {
            chara_info = CreateChara(
                1,
                vital: vital,
                singleModeCharaId: singleModeCharaId,
                turn: turn),
            home_info = CreateHomeInfo(),
            unchecked_event_array = [],
            race_condition_array = [],
            select_index_info_array = []
        }
    };

    static SingleModeBreedersCheckEventResponse CreateBreedersTrainingResponse(
        int vital = 80,
        int singleModeCharaId = 1001,
        int turn = 1)
    {
        var response = (SingleModeBreedersCheckEventResponse)RandomDtoFactory.Create(
            typeof(SingleModeBreedersCheckEventResponse),
            "BreedersScenarioAnalyzer:valid");
        response.data.chara_info = CreateChara(
            4,
            vital: vital,
            singleModeCharaId: singleModeCharaId,
            turn: turn);
        response.data.home_info = CreateHomeInfo();
        response.data.unchecked_event_array = [];
        response.data.race_start_info = null!;
        response.data.breeders_data_set = new()
        {
            command_info_array =
            [
                .. BaseTrainingIds.Select((commandId, index) => new SingleModeBreedersCommandInfo
                {
                    command_type = 1,
                    command_id = commandId,
                    params_inc_dec_info_array = CreateParams(index + 1),
                    team_member_info_array = []
                })
            ],
            team_member_info_array = [],
            team_sp_training_info = new() { stock_num = 1, stock_max = 3 },
            team_rank = 1,
            command_gain_exp_array = [],
            link_friend_outing_member_info_array = []
        };
        return response;
    }

    static SingleModeCookCheckEventResponse CreateCookTrainingResponse(
        int vital = 80,
        int singleModeCharaId = 1001,
        int turn = 1)
    {
        var response = (SingleModeCookCheckEventResponse)RandomDtoFactory.Create(
            typeof(SingleModeCookCheckEventResponse),
            "CookScenarioAnalyzer:valid");
        response.data.chara_info = CreateChara(
            8,
            vital: vital,
            singleModeCharaId: singleModeCharaId,
            turn: turn);
        response.data.home_info = CreateHomeInfo();
        response.data.unchecked_event_array = [];
        response.data.race_start_info = null!;
        response.data.cook_data_set = new()
        {
            cook_info = new()
            {
                care_point = 50,
                cooking_friends_power = 100,
                cooking_success_point = 10,
                cooking_success_base_point = 100
            },
            command_info_array =
            [
                .. BaseTrainingIds.Select((commandId, index) => new SingleModeCookCommandInfo
                {
                    command_type = 1,
                    command_id = commandId,
                    params_inc_dec_info_array = CreateParams(index + 1)
                })
            ],
            material_info_array =
            [
                .. Enumerable.Range(1, 5).Select(index => new SingleModeCookMaterialInfo
                {
                    material_id = index * 100,
                    num = 100
                })
            ],
            material_harvest_info_array =
            [
                .. Enumerable.Range(1, 5).Select(index => new SingleModeCookMaterialHarvestInfo
                {
                    material_id = index * 100,
                    harvest_num = 20
                })
            ],
            care_history_info_array = [],
            dish_info = null!,
            facility_info_array =
            [
                .. Enumerable.Range(1, 5).Select(index => new SingleModeCookFacilityInfo
                {
                    facility_id = index * 100,
                    facility_level = 1
                })
            ],
            cooking_success_rate = 50,
            cook_result_info_array = [],
            success_effect_id_array = [],
            care_point_gain_num = 10,
            command_material_care_info_array = []
        };
        return response;
    }

    static SingleModeMechaCheckEventResponse CreateMechaTrainingResponse(
        int vital = 80,
        int singleModeCharaId = 1001,
        int turn = 1)
    {
        int[] trainIds = [901, 105, 902, 103, 906];
        var response = (SingleModeMechaCheckEventResponse)RandomDtoFactory.Create(
            typeof(SingleModeMechaCheckEventResponse),
            "MechaScenarioAnalyzer:valid");
        response.data.chara_info = CreateChara(
            9,
            trainIds,
            vital,
            singleModeCharaId,
            turn);
        response.data.home_info = CreateHomeInfo(trainIds);
        response.data.unchecked_event_array = [];
        response.data.race_start_info = null!;
        response.data.mecha_data_set = new()
        {
            command_info_array =
            [
                .. trainIds.Select((commandId, index) => new SingleModeMechaCommandInfo
                {
                    command_type = 1,
                    command_id = commandId,
                    params_inc_dec_info_array = CreateParams(index + 1),
                    point_up_info_array = [new() { status_type = index + 1, value = 10 }],
                    is_recommend = index == 0,
                    energy_num = 1
                })
            ],
            tuning_point = 1,
            rival_info = new()
            {
                speed = 10,
                stamina = 10,
                power = 10,
                guts = 10,
                wiz = 10,
                speed_limit = 200,
                stamina_limit = 200,
                power_limit = 200,
                guts_limit = 200,
                wiz_limit = 200,
                progress_rate = 10
            },
            overdrive_info = new()
            {
                energy_num = 1,
                remain_num = 1,
                over_drive_state = 0
            },
            board_info_array =
            [
                .. Enumerable.Range(1, 3).Select(boardId => new SingleModeMechaBoardInfo
                {
                    board_id = boardId,
                    chip_info_array = [new() { chip_id = 2000 + boardId, point = 1 }]
                })
            ],
            upgrade_race_result_array = []
        };
        return response;
    }

    static SingleModePioneerCheckEventResponse CreatePioneerTrainingResponse(
        int vital = 80,
        int singleModeCharaId = 1001,
        int turn = 1) => new()
    {
        data = new()
        {
            chara_info = CreateChara(
                11,
                vital: vital,
                singleModeCharaId: singleModeCharaId,
                turn: turn),
            home_info = CreateHomeInfo(),
            unchecked_event_array = [],
            race_condition_array = [],
            select_index_info_array = [],
            pioneer_data_set = new()
            {
                command_info_array =
                [
                    .. BaseTrainingIds.Select((commandId, index) => new SingleModePioneerCommandInfo
                    {
                        command_type = 1,
                        command_id = commandId,
                        params_inc_dec_info_array = CreateParams(index + 1)
                    })
                ],
                pioneer_point_gain_info_array =
                [
                    .. BaseTrainingIds.Select((commandId, index) => new SingleModePioneerPointGainInfo
                    {
                        command_type = 1,
                        command_id = commandId,
                        gain_num = index + 1
                    })
                ]
            }
        }
    };

    static SingleModeOnsenCheckEventResponse CreateOnsenTrainingResponse(
        int vital = 80,
        int singleModeCharaId = 1001,
        int turn = 1) => new()
    {
        data = new()
        {
            chara_info = CreateChara(
                12,
                vital: vital,
                singleModeCharaId: singleModeCharaId,
                turn: turn),
            home_info = CreateHomeInfo(),
            unchecked_event_array = [],
            race_condition_array = [],
            select_index_info_array = [],
            onsen_data_set = new()
            {
                command_info_array =
                [
                    .. BaseTrainingIds.Select((commandId, index) => new SingleModeOnsenCommandInfo
                    {
                        command_type = 1,
                        command_id = commandId,
                        params_inc_dec_info_array = CreateParams(index + 1),
                        dig_info_array = []
                    })
                ],
                bathing_info = new(),
                onsen_info_array =
                [
                    new()
                    {
                        onsen_id = 1,
                        state = 1,
                        stratum_info_array =
                        [
                            new() { stratum_id = 4, rest_volume = 10 },
                            new() { stratum_id = 5, rest_volume = 10 },
                            new() { stratum_id = 6, rest_volume = 10 }
                        ]
                    }
                ],
                dig_effect_info_array =
                [
                    new() { stratum_type = 1, item_level = 1, dig_effect_value = 10, is_enable = true },
                    new() { stratum_type = 2, item_level = 1, dig_effect_value = 10, is_enable = true },
                    new() { stratum_type = 3, item_level = 1, dig_effect_value = 10, is_enable = true }
                ],
                dug_onsen_id_array = [],
                effected_onsen_id_array = [],
                level_up_dig_effect_info_array = []
            }
        }
    };

    static SingleModeSportCheckEventResponse CreateUafTrainingResponse(
        int vital = 80,
        int singleModeCharaId = 1001,
        int turn = 1)
    {
        var currentCommands = new[] { 2101, 2202, 2303, 2104, 2205 };
        return new()
        {
            data = new()
            {
                chara_info = CreateChara(
                    7,
                    vital: vital,
                    singleModeCharaId: singleModeCharaId,
                    turn: turn),
                home_info = CreateHomeInfo(),
                unchecked_event_array = [],
                race_condition_array = [],
                select_index_info_array = [],
                sport_data_set = new()
                {
                    training_array =
                    [
                        .. Enumerable.Range(1, 3).SelectMany(color =>
                            Enumerable.Range(1, 5).Select(index => new SingleModeSportTraining
                            {
                                command_type = 1,
                                command_id = 2000 + color * 100 + index,
                                sport_rank = 50
                            }))
                    ],
                    command_info_array =
                    [
                        .. currentCommands.Select((commandId, index) => new SingleModeSportCommandInfo
                        {
                            command_type = 1,
                            command_id = commandId,
                            params_inc_dec_info_array = CreateParams(index + 1),
                            gain_sport_rank_array = [new() { command_id = commandId, gain_rank = 3 }]
                        })
                    ],
                    item_id_array = [],
                    effected_item_id_array = [],
                    competition_result_array = [],
                    effected_stance_array = [],
                    compe_effect_id_array = [],
                    training_gain_rank_array = []
                }
            }
        };
    }

    static SingleModeChara CreateChara(
        int scenarioId,
        int[]? trainIds = null,
        int vital = 80,
        int singleModeCharaId = 1001,
        int turn = 1) => new()
    {
        single_mode_chara_id = singleModeCharaId,
        card_id = 100100,
        state = 1,
        playing_state = 1,
        scenario_id = scenarioId,
        turn = turn,
        motivation = 5,
        vital = vital,
        max_vital = 100,
        speed = 500,
        stamina = 500,
        power = 500,
        guts = 500,
        wiz = 500,
        max_speed = 1200,
        max_stamina = 1200,
        max_power = 1200,
        max_guts = 1200,
        max_wiz = 1200,
        skill_point = 1200,
        skill_array = [],
        skill_tips_array = [],
        support_card_array = [],
        evaluation_info_array = [],
        training_level_info_array =
        [
            .. (trainIds ?? BaseTrainingIds).Select(commandId => new TrainingLevelInfo
            {
                command_id = commandId,
                level = 1
            })
        ],
        chara_effect_id_array = [],
        proper_distance_short = 1,
        proper_distance_mile = 1,
        proper_distance_middle = 1,
        proper_distance_long = 1,
        proper_running_style_nige = 1,
        proper_running_style_senko = 1,
        proper_running_style_sashi = 1,
        proper_running_style_oikomi = 1,
        proper_ground_turf = 1,
        proper_ground_dirt = 1
    };

    static SingleModeHomeInfo CreateHomeInfo(int[]? trainIds = null) => new()
    {
        command_info_array =
        [
            .. (trainIds ?? BaseTrainingIds).Select((commandId, index) => new SingleModeCommandInfo
            {
                command_type = 1,
                command_id = commandId,
                is_enable = 1,
                failure_rate = index * 5,
                training_partner_array = [],
                tips_event_partner_array = [],
                sub_command_partner_array = [],
                params_inc_dec_info_array = CreateParams(index + 1)
            })
        ]
    };

    static SingleModeParamsIncDecInfo[] CreateParams(int value) =>
    [
        new() { target_type = 1, value = value },
        new() { target_type = 2, value = value },
        new() { target_type = 3, value = value },
        new() { target_type = 4, value = value },
        new() { target_type = 5, value = value },
        new() { target_type = 30, value = value },
        new() { target_type = 10, value = -10 }
    ];
}
