using System.Reflection;
using EventLoggerPlugin;
using Gallop;
using UmamusumeResponseAnalyzer.Plugin;

sealed class RandomDtoFactory(int seed)
{
    const int MaxDepth = 8;
    readonly Random random = new(seed);

    public static object Create(Type type, string salt)
    {
        var factory = new RandomDtoFactory(StableSeed(type.FullName + ":" + salt));
        var value = factory.CreateValue(type, 0)
            ?? throw new InvalidOperationException($"Cannot create DTO root value: {type.FullName}");
        StabilizeDto(value);
        return value;
    }

    public static object CreateForAnalyzer(Type pluginType, Type dtoType, AnalyzerKind kind, string salt)
    {
        if (kind == AnalyzerKind.Response
            && pluginType.Namespace == "EventResponseAnalyzer")
        {
            var value = Create(dtoType, salt);
            StabilizeEventResponseAnalyzerResponse(value);
            return value;
        }

        return Create(dtoType, salt);
    }

    static int StableSeed(string text)
    {
        unchecked
        {
            var hash = 17;
            foreach (var c in text)
                hash = (hash * 31) + c;
            return hash;
        }
    }

    object? CreateValue(Type type, int depth)
    {
        if (type == typeof(string))
            return $"dto-{depth}-{random.Next(1, 1_000_000)}";
        if (type == typeof(bool))
            return random.Next(0, 2) == 0;
        if (type == typeof(byte))
            return (byte)random.Next(byte.MinValue, byte.MaxValue + 1);
        if (type == typeof(sbyte))
            return (sbyte)random.Next(sbyte.MinValue, sbyte.MaxValue + 1);
        if (type == typeof(short))
            return (short)random.Next(short.MinValue, short.MaxValue);
        if (type == typeof(ushort))
            return (ushort)random.Next(ushort.MinValue, ushort.MaxValue);
        if (type == typeof(int))
            return random.Next(0, 100);
        if (type == typeof(uint))
            return (uint)random.Next(0, 100);
        if (type == typeof(long))
            return random.NextInt64(0, 100);
        if (type == typeof(ulong))
            return (ulong)random.NextInt64(0, 100);
        if (type == typeof(float))
            return (float)(random.NextDouble() * 100);
        if (type == typeof(double))
            return random.NextDouble() * 100;
        if (type == typeof(decimal))
            return (decimal)(random.NextDouble() * 100);
        if (type.IsEnum)
        {
            var values = Enum.GetValues(type);
            return values.Length == 0 ? Activator.CreateInstance(type) : values.GetValue(random.Next(values.Length));
        }
        if (Nullable.GetUnderlyingType(type) is { } nullableType)
            return CreateValue(nullableType, depth);
        if (type == typeof(byte[]))
        {
            var bytes = new byte[4];
            random.NextBytes(bytes);
            return bytes;
        }
        if (type.IsArray)
        {
            var elementType = type.GetElementType()!;
            var length = depth >= MaxDepth ? 0 : 1;
            var array = Array.CreateInstance(elementType, length);
            for (var i = 0; i < length; i++)
                array.SetValue(CreateValue(elementType, depth + 1), i);
            return array;
        }
        if (type.IsAbstract || type.IsInterface)
            return type.IsValueType ? Activator.CreateInstance(type) : null;
        if (depth >= MaxDepth && !type.IsValueType)
            return null;

        var instance = Activator.CreateInstance(type);
        if (instance is null)
            return null;

        foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.Public))
            field.SetValue(instance, CreateValue(field.FieldType, depth + 1));
        return instance;
    }

    static void StabilizeDto(object value)
    {
        var type = value.GetType();
        StabilizeSingleModeRequest(value, "single_mode_exec_command_request_common", common =>
        {
            SetIntField(common, "command_type", 0);
            SetIntField(common, "command_id", 0);
            SetIntField(common, "current_turn", 1);
        });
        StabilizeSingleModeRequest(value, "single_mode_check_event_request_common", common =>
        {
            SetIntField(common, "choice_number", 0);
        });

        if (type.Name.EndsWith("CheckEventResponse", StringComparison.Ordinal))
            StabilizeCheckEventResponse(value);
        if (type.Name.EndsWith("RaceEndOutResponse", StringComparison.Ordinal))
            StabilizeCheckEventResponse(value);
        if (type.Name.EndsWith("ExecCommandResponse", StringComparison.Ordinal))
            StabilizeExecCommandResponse(value);
        if (type.Name.EndsWith("RaceEndResponse", StringComparison.Ordinal))
            StabilizeRaceEndResponse(value);
        if (type.Name.StartsWith("SingleModeRamen", StringComparison.Ordinal) &&
            type.Name.EndsWith("Response", StringComparison.Ordinal))
        {
            StabilizeRamenResponse(value);
        }
        if (type.Name == "SingleModeLegendCheckEventResponse")
            StabilizeLegendCheckEventResponse(value);

        switch (type.Name)
        {
            case "FriendSearchResponse":
                StabilizeFriendSearchResponse(value);
                break;
            case "SingleModeStartResponse":
                StabilizeSingleModeStartResponse(value);
                break;
            case "TrainedCharaLoadResponse":
                StabilizeTrainedCharaLoadResponse(value);
                break;
        }
    }

    static void StabilizeSingleModeRequest(object value, string fieldName, Action<object> stabilize)
    {
        var common = value.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.Public)?.GetValue(value);
        if (common is not null)
            stabilize(common);
    }

    static void StabilizeCheckEventResponse(object value)
    {
        var dataField = value.GetType().GetField("data", BindingFlags.Instance | BindingFlags.Public);
        var data = dataField?.GetValue(value);
        if (data is null)
            return;

        SetArrayFieldEmpty(data, "unchecked_event_array");
        SetArrayFieldEmpty(data, "select_index_info_array");

        var charaInfo = (SingleModeChara?)data.GetType().GetField("chara_info", BindingFlags.Instance | BindingFlags.Public)?.GetValue(data);
        if (charaInfo is not null)
        {
            charaInfo.state = 1;
            charaInfo.turn = 1;
            charaInfo.skill_array = [];
            charaInfo.skill_tips_array = [];
            charaInfo.support_card_array = [];
            StabilizeProperFields(charaInfo);
        }

    }

    static void StabilizeSendGameStatusExecCommandResponse(object value)
    {
        var scenarioId = value.GetType().Name switch
        {
            "SingleModeArcExecCommandResponse" => 6,
            "SingleModeSportExecCommandResponse" => 7,
            "SingleModeCookExecCommandResponse" => 8,
            "SingleModeMechaExecCommandResponse" => 9,
            "SingleModeLegendExecCommandResponse" => 10,
            "SingleModeOnsenExecCommandResponse" => 12,
            _ => 0
        };
        if (scenarioId == 0)
            return;

        var data = value.GetType().GetField("data", BindingFlags.Instance | BindingFlags.Public)?.GetValue(value);
        if (data is null)
            return;

        var trainIds = scenarioId == 9
            ? new[] { 901, 105, 902, 103, 906 }
            : new[] { 101, 105, 102, 103, 106 };
        SetArrayFieldEmpty(data, "unchecked_event_array");

        var charaInfo = (SingleModeChara?)data.GetType().GetField("chara_info", BindingFlags.Instance | BindingFlags.Public)?.GetValue(data);
        if (charaInfo is not null)
        {
            StabilizeSendGameStatusCharaInfo(charaInfo, scenarioId, trainIds);
            StabilizeEventLoggerTurnState(charaInfo, trainIds);
        }

        var homeInfo = (SingleModeHomeInfo?)data.GetType().GetField("home_info", BindingFlags.Instance | BindingFlags.Public)?.GetValue(data);
        if (homeInfo is not null)
            StabilizeHomeCommandInfo(homeInfo, trainIds);

        switch (scenarioId)
        {
            case 6:
                StabilizeArcDataSet(EnsureObjectField(data, "arc_data_set"));
                break;
            case 7:
                StabilizeSportDataSet(EnsureObjectField(data, "sport_data_set"));
                break;
            case 8:
                StabilizeCookDataSet(EnsureObjectField(data, "cook_data_set"));
                break;
            case 9:
                StabilizeMechaDataSet(EnsureObjectField(data, "mecha_data_set"));
                break;
            case 10:
                StabilizeLegendDataSet(EnsureObjectField(data, "legend_data_set"));
                break;
            case 12:
                StabilizeOnsenDataSet(EnsureObjectField(data, "onsen_data_set"));
                break;
        }
    }

    static void StabilizeSendGameStatusCharaInfo(SingleModeChara charaInfo, int scenarioId, int[] trainIds)
    {
        StabilizeTrainingChara(charaInfo, trainIds, [1, 2, 3, 4, 5, 6, 102, 103, 111]);
        charaInfo.scenario_id = scenarioId;
        charaInfo.card_id = 1001;
        charaInfo.rarity = 3;
        charaInfo.chara_effect_id_array = [];
        charaInfo.skill_array = [];
        charaInfo.skill_tips_array = [];
        StabilizeProperFields(charaInfo);
    }

    static void StabilizeTrainingChara(SingleModeChara charaInfo, int[] trainIds, int[] evaluationIds)
    {
        charaInfo.state = 1;
        charaInfo.playing_state = 1;
        charaInfo.turn = 1;
        charaInfo.motivation = 5;
        charaInfo.vital = 80;
        charaInfo.max_vital = 100;
        charaInfo.speed = charaInfo.stamina = charaInfo.power = charaInfo.guts = charaInfo.wiz = 500;
        charaInfo.max_speed = charaInfo.max_stamina = charaInfo.max_power = charaInfo.max_guts = charaInfo.max_wiz = 1200;
        charaInfo.skill_point = 1200;
        charaInfo.support_card_array = [.. Enumerable.Range(1, 6).Select(position => new SingleModeSupportCard
        {
            position = position,
            support_card_id = 10000 + position
        })];
        charaInfo.evaluation_info_array = [.. evaluationIds.Select(targetId => new EvaluationInfo
        {
            target_id = targetId,
            evaluation = 80
        })];
        charaInfo.training_level_info_array = [.. trainIds.Select(commandId => new TrainingLevelInfo
        {
            command_id = commandId,
            level = 1
        })];
    }

    static void StabilizeEventLoggerTurnState(SingleModeChara charaInfo, int[] trainIds)
    {
        EventLogger.ResetAndStartSession(new EventLoggerSnapshot(charaInfo, [], []), isFullGame: false);
        EventLogger.CommitScenarioTurn(
            charaInfo.scenario_id,
            charaInfo.turn,
            new TurnStats
            {
                isTraining = true,
                motivation = 5,
                playerChoice = trainIds[0],
                trainLevel = [1, 1, 1, 1, 1],
                trainLevelCount = [0, 0, 0, 0, 0]
            });
    }

    static void StabilizeExecCommandResponse(object value)
    {
        var data = value.GetType().GetField("data", BindingFlags.Instance | BindingFlags.Public)?.GetValue(value);
        if (data is null)
            return;

        SetArrayFieldEmpty(data, "unchecked_event_array");

        var commandResult = data.GetType().GetField("command_result", BindingFlags.Instance | BindingFlags.Public)?.GetValue(data);
        if (commandResult is not null)
            SetIntField(commandResult, "result_state", 0);

        var charaInfo = (SingleModeChara?)data.GetType().GetField("chara_info", BindingFlags.Instance | BindingFlags.Public)?.GetValue(data);
        if (charaInfo is not null)
        {
            charaInfo.state = 1;
            charaInfo.turn = 1;
            charaInfo.skill_array = [];
            charaInfo.skill_tips_array = [];
            charaInfo.support_card_array = [];
            StabilizeProperFields(charaInfo);
        }

        StabilizeSendGameStatusExecCommandResponse(value);
    }

    static void StabilizeProperFields(SingleModeChara charaInfo)
    {
        charaInfo.proper_distance_short = charaInfo.proper_distance_mile =
            charaInfo.proper_distance_middle = charaInfo.proper_distance_long = 1;
        charaInfo.proper_running_style_nige = charaInfo.proper_running_style_oikomi =
            charaInfo.proper_running_style_sashi = charaInfo.proper_running_style_senko = 1;
        charaInfo.proper_ground_turf = charaInfo.proper_ground_dirt = 1;
    }

    static void StabilizeRaceEndResponse(object value)
    {
        var data = value.GetType().GetField("data", BindingFlags.Instance | BindingFlags.Public)?.GetValue(value);
        if (data is null)
            return;

        SetArrayFieldEmpty(data, "race_history");
    }

    static void StabilizeEventResponseAnalyzerResponse(object value)
    {
        var data = value.GetType().GetField("data", BindingFlags.Instance | BindingFlags.Public)?.GetValue(value)
            ?? throw new InvalidOperationException($"{value.GetType().Name} smoke DTO has no data.");

        var eventContainer = data.GetType().GetField("single_mode_load_common", BindingFlags.Instance | BindingFlags.Public) is null
            ? data
            : EnsureObjectField(data, "single_mode_load_common");

        StabilizeArrayField(eventContainer, "unchecked_event_array", 1, (eventInfo, _) =>
        {
            SetIntField(eventInfo, "event_id", 1);
            SetIntField(eventInfo, "chara_id", 1);
            SetIntField(eventInfo, "story_id", 1001);

            var contents = EnsureObjectField(eventInfo, "event_contents_info");
            StabilizeArrayField(contents, "choice_array", 2, (choice, index) =>
            {
                StabilizeArrayField(choice, "select_index_info_array", 1, (selectIndexInfo, _) =>
                {
                    SetIntField(selectIndexInfo, "branch_number", 0);
                    SetIntField(selectIndexInfo, "select_index", index + 1);
                });
            });
        });

        var charaInfo = (SingleModeChara?)eventContainer.GetType().GetField("chara_info", BindingFlags.Instance | BindingFlags.Public)?.GetValue(eventContainer);
        if (charaInfo is not null)
        {
            charaInfo.state = 1;
            charaInfo.turn = 1;
            charaInfo.skill_array = [];
            charaInfo.skill_tips_array = [];
            StabilizeProperFields(charaInfo);
        }
    }

    static void StabilizeRamenResponse(object value)
    {
        var data = value.GetType().GetField("data", BindingFlags.Instance | BindingFlags.Public)?.GetValue(value);
        if (data is null)
            return;

        SetArrayFieldEmpty(data, "unchecked_event_array");

        var charaInfo = (SingleModeChara?)data.GetType().GetField("chara_info", BindingFlags.Instance | BindingFlags.Public)?.GetValue(data);
        if (charaInfo is not null)
        {
            StabilizeTrainingChara(charaInfo, [101, 105, 102, 103, 106], [1, 2, 3, 4, 5, 6]);
        }

        var homeInfo = (SingleModeHomeInfo?)data.GetType().GetField("home_info", BindingFlags.Instance | BindingFlags.Public)?.GetValue(data);
        if (homeInfo is not null)
            StabilizeHomeCommandInfo(homeInfo);

        var ramenDataSet = data.GetType().GetField("ramen_data_set", BindingFlags.Instance | BindingFlags.Public)?.GetValue(data);
        if (ramenDataSet is not null)
            StabilizeRamenDataSet(ramenDataSet);
    }

    static void StabilizeHomeCommandInfo(SingleModeHomeInfo homeInfo, int[]? trainIds = null)
    {
        trainIds ??= [101, 105, 102, 103, 106];
        homeInfo.command_info_array = [.. trainIds.Select((commandId, index) => new SingleModeCommandInfo
        {
            command_type = 1,
            command_id = commandId,
            is_enable = 1,
            failure_rate = index * 5,
            training_partner_array = [.. Enumerable.Range(1, index % 3 + 1)],
            tips_event_partner_array = [],
            params_inc_dec_info_array = CreateParams(commandId, includeVital: true)
        })];
    }

    static void StabilizeArcDataSet(object arcDataSet)
    {
        var trainIds = new[] { 101, 105, 102, 103, 106 };
        var arcInfo = EnsureObjectField(arcDataSet, "arc_info");
        SetIntField(arcInfo, "global_exp", 100);
        var potentialIds = new[] { 2, 5, 1, 4, 6, 3, 7, 8, 9, 10 };
        StabilizeArrayField(arcInfo, "potential_array", potentialIds.Length, (item, index) =>
        {
            SetIntField(item, "potential_id", potentialIds[index]);
            SetIntField(item, "level", 1);
        });

        SetArrayFieldEmpty(arcDataSet, "arc_rival_array");
        SetArrayFieldEmpty(arcDataSet, "evaluation_info_array");
        SetReferenceFieldNull(arcDataSet, "selection_info");
        StabilizeArrayField(arcDataSet, "command_info_array", trainIds.Length, (item, index) =>
        {
            SetIntField(item, "command_type", 1);
            SetIntField(item, "command_id", trainIds[index]);
            SetArrayValue(item, "params_inc_dec_info_array", CreateParams(trainIds[index] + 10, includeVital: false));
        });
    }

    static void StabilizeSportDataSet(object sportDataSet)
    {
        var trainIds = new[] { 101, 105, 102, 103, 106 };
        StabilizeArrayField(sportDataSet, "training_array", 15, (item, index) =>
        {
            SetIntField(item, "command_type", 1);
            SetIntField(item, "command_id", trainIds[index % trainIds.Length]);
            SetIntField(item, "sport_rank", 10 + index);
        });
        StabilizeArrayField(sportDataSet, "command_info_array", trainIds.Length, (item, index) =>
        {
            SetIntField(item, "command_type", 1);
            SetIntField(item, "command_id", trainIds[index]);
            SetArrayValue(item, "params_inc_dec_info_array", CreateParams(trainIds[index] + 10, includeVital: false));
            SetArrayFieldEmpty(item, "gain_sport_rank_array");
        });
        SetArrayValue(sportDataSet, "item_id_array", Array.Empty<int>());
        SetArrayValue(sportDataSet, "effected_item_id_array", Array.Empty<int>());
        SetArrayFieldEmpty(sportDataSet, "competition_result_array");
        SetArrayFieldEmpty(sportDataSet, "effected_stance_array");
        SetArrayValue(sportDataSet, "compe_effect_id_array", Array.Empty<int>());
        SetArrayFieldEmpty(sportDataSet, "training_gain_rank_array");
    }

    static void StabilizeCookDataSet(object cookDataSet)
    {
        EnsureObjectField(cookDataSet, "cook_info");
        var materialIds = new[] { 100, 200, 300, 400, 500 };
        StabilizeArrayField(cookDataSet, "material_info_array", materialIds.Length, (item, index) =>
        {
            SetIntField(item, "material_id", materialIds[index]);
            SetIntField(item, "num", 100 + index);
        });
        StabilizeArrayField(cookDataSet, "material_harvest_info_array", materialIds.Length, (item, index) =>
        {
            SetIntField(item, "material_id", materialIds[index]);
            SetIntField(item, "harvest_num", 20 + index);
        });
        StabilizeArrayField(cookDataSet, "facility_info_array", materialIds.Length, (item, index) =>
        {
            SetIntField(item, "facility_id", materialIds[index]);
            SetIntField(item, "facility_level", 1);
        });
        SetIntField(cookDataSet, "cooking_success_rate", 100);
        SetReferenceFieldNull(cookDataSet, "dish_info");
        SetArrayFieldEmpty(cookDataSet, "care_history_info_array");
        SetArrayFieldEmpty(cookDataSet, "cook_result_info_array");
        SetArrayFieldEmpty(cookDataSet, "command_material_care_info_array");
    }

    static void StabilizeMechaDataSet(object mechaDataSet)
    {
        var trainIds = new[] { 901, 105, 902, 103, 906 };
        StabilizeArrayField(mechaDataSet, "command_info_array", trainIds.Length, (item, index) =>
        {
            SetIntField(item, "command_type", 1);
            SetIntField(item, "command_id", trainIds[index]);
            SetArrayValue(item, "params_inc_dec_info_array", CreateParams(trainIds[index] + 10, includeVital: false));
            SetArrayFieldEmpty(item, "point_up_info_array");
            SetBoolField(item, "is_recommend", index == 0);
        });

        var rivalInfo = EnsureObjectField(mechaDataSet, "rival_info");
        SetIntField(rivalInfo, "speed", 1);
        SetIntField(rivalInfo, "stamina", 1);
        SetIntField(rivalInfo, "power", 1);
        SetIntField(rivalInfo, "guts", 1);
        SetIntField(rivalInfo, "wiz", 1);

        var overdriveInfo = EnsureObjectField(mechaDataSet, "overdrive_info");
        SetIntField(overdriveInfo, "energy_num", 1);
        SetIntField(overdriveInfo, "remain_num", 1);
        SetIntField(overdriveInfo, "over_drive_state", 0);
        SetIntField(overdriveInfo, "is_overdrive_burst", 0);

        StabilizeArrayField(mechaDataSet, "board_info_array", 3, (board, boardIndex) =>
        {
            SetIntField(board, "board_id", boardIndex + 1);
            StabilizeArrayField(board, "chip_info_array", 4, (chip, chipIndex) =>
            {
                SetIntField(chip, "chip_id", chipIndex == 3 ? 2001 + boardIndex : 1001 + chipIndex);
                SetIntField(chip, "point", chipIndex + 1);
            });
        });
        SetArrayFieldEmpty(mechaDataSet, "upgrade_race_result_array");
    }

    static void StabilizeOnsenDataSet(object onsenDataSet)
    {
        var trainIds = new[] { 101, 105, 102, 103, 106 };
        StabilizeArrayField(onsenDataSet, "command_info_array", trainIds.Length, (item, index) =>
        {
            SetIntField(item, "command_type", 1);
            SetIntField(item, "command_id", trainIds[index]);
            SetArrayValue(item, "params_inc_dec_info_array", CreateParams(trainIds[index] + 10, includeVital: false));
        });

        var bathingInfo = EnsureObjectField(onsenDataSet, "bathing_info");
        SetIntField(bathingInfo, "ticket_num", 1);
        SetIntField(bathingInfo, "onsen_effect_remain_count", 2);
        SetIntField(bathingInfo, "superior_state", 0);

        StabilizeArrayField(onsenDataSet, "onsen_info_array", 10, (onsenInfo, index) =>
        {
            SetIntField(onsenInfo, "onsen_id", index + 1);
            SetIntField(onsenInfo, "state", index == 0 ? 2 : 0);
            StabilizeArrayField(onsenInfo, "stratum_info_array", 3, (stratum, stratumIndex) =>
            {
                SetIntField(stratum, "stratum_id", 4 + stratumIndex);
                SetIntField(stratum, "rest_volume", 10 + stratumIndex);
            });
        });
        StabilizeArrayField(onsenDataSet, "dig_effect_info_array", 3, (item, index) =>
        {
            SetIntField(item, "stratum_type", index);
            SetIntField(item, "item_level", 1);
            SetIntField(item, "dig_effect_value", 10 + index);
            SetBoolField(item, "is_enable", true);
        });
        SetArrayValue(onsenDataSet, "dug_onsen_id_array", new[] { 1 });
        SetArrayValue(onsenDataSet, "effected_onsen_id_array", Array.Empty<int>());
        SetArrayFieldEmpty(onsenDataSet, "level_up_dig_effect_info_array");
    }

    static void StabilizeRamenDataSet(object ramenDataSet)
    {
        var trainIds = new[] { 101, 105, 102, 103, 106 };
        StabilizeArrayField(ramenDataSet, "command_info_array", trainIds.Length, (item, index) =>
        {
            SetIntField(item, "command_type", 1);
            SetIntField(item, "command_id", trainIds[index]);
            SetArrayValue(item, "params_inc_dec_info_array", CreateParams(trainIds[index] + 10, includeVital: false));
        });
        StabilizeArrayField(ramenDataSet, "command_feeling_info_array", trainIds.Length, (item, index) =>
        {
            SetIntField(item, "command_type", 1);
            SetIntField(item, "command_id", trainIds[index]);
            SetIntField(item, "feeling_id", index + 1);
        });
        StabilizeArrayField(ramenDataSet, "training_exec_info_array", trainIds.Length, (item, index) =>
        {
            SetIntField(item, "base_command_id", trainIds[index]);
            SetIntField(item, "exec_count", index);
        });
        StabilizeArrayField(ramenDataSet, "feeling_info_array", 2, (item, index) =>
        {
            SetIntField(item, "feeling_index", index + 1);
            SetIntField(item, "feeling_id", index + 1);
        });
        StabilizeArrayField(ramenDataSet, "feeling_turn_info_array", 2, (item, index) =>
        {
            SetIntField(item, "feeling_id", index + 1);
            SetIntField(item, "remain_turn", 3 - index);
        });
        StabilizeArrayField(ramenDataSet, "feeling_reduce_turn_info_array", trainIds.Length, (item, index) =>
        {
            SetIntField(item, "command_type", 1);
            SetIntField(item, "command_id", trainIds[index]);
            StabilizeArrayField(item, "feeling_turn_array", 2, (feelingTurn, feelingIndex) =>
            {
                SetIntField(feelingTurn, "feeling_id", feelingIndex + 1);
                SetIntField(feelingTurn, "turn", 1);
            });
        });
    }

    static void StabilizeLegendCheckEventResponse(object value)
    {
        var data = value.GetType().GetField("data", BindingFlags.Instance | BindingFlags.Public)?.GetValue(value);
        if (data is null)
            return;

        SetArrayFieldEmpty(data, "unchecked_event_array");
        SetArrayFieldEmpty(data, "select_index_info_array");
        SetReferenceFieldNull(data, "race_start_info");

        var charaInfo = (SingleModeChara?)data.GetType().GetField("chara_info", BindingFlags.Instance | BindingFlags.Public)?.GetValue(data);
        if (charaInfo is not null)
        {
            StabilizeTrainingChara(charaInfo, [101, 105, 102, 103, 106], [1, 2, 3, 4, 5, 6]);
            charaInfo.scenario_id = 10;
        }

        var homeInfo = (SingleModeHomeInfo?)data.GetType().GetField("home_info", BindingFlags.Instance | BindingFlags.Public)?.GetValue(data);
        if (homeInfo is not null)
            StabilizeHomeCommandInfo(homeInfo);

        var legendDataSet = data.GetType().GetField("legend_data_set", BindingFlags.Instance | BindingFlags.Public)?.GetValue(data);
        if (legendDataSet is not null)
            StabilizeLegendDataSet(legendDataSet);
    }

    static void StabilizeLegendDataSet(object legendDataSet)
    {
        var trainIds = new[] { 101, 105, 102, 103, 106 };
        StabilizeArrayField(legendDataSet, "command_info_array", trainIds.Length, (item, index) =>
        {
            SetIntField(item, "command_type", 1);
            SetIntField(item, "command_id", trainIds[index]);
            SetIntField(item, "legend_id", 9046 + index % 3);
            SetIntField(item, "gain_gauge", index + 1);
            SetArrayValue(item, "params_inc_dec_info_array", CreateParams(trainIds[index] + 10, includeVital: false));
            SetArrayFieldEmpty(item, "friend_gauge_gain_array");
        });
        SetArrayFieldEmpty(legendDataSet, "evaluation_info_array");
        StabilizeArrayField(legendDataSet, "gauge_count_array", 3, (item, index) =>
        {
            SetIntField(item, "legend_id", 9046 + index);
            SetIntField(item, "count", (index + 1) * 2);
        });
        StabilizeArrayField(legendDataSet, "buff_info_array", 3, (item, index) =>
        {
            SetIntField(item, "buff_id", 1101 + index);
            SetIntField(item, "is_active", index == 2 ? 0 : 1);
        });
        SetArrayValue(legendDataSet, "obtainable_buff_id_array", new[] { 1101 });
        SetArrayFieldEmpty(legendDataSet, "race_history_array");
        SetArrayValue(legendDataSet, "activated_buff_id_array", Array.Empty<int>());
    }

    static SingleModeParamsIncDecInfo[] CreateParams(int baseValue, bool includeVital)
    {
        var targetTypes = includeVital ? new[] { 1, 2, 3, 4, 5, 30, 10 } : [1, 2, 3, 4, 5, 30];
        return [.. targetTypes.Select((targetType, index) => new SingleModeParamsIncDecInfo
        {
            target_type = targetType,
            value = targetType == 10 ? -10 : baseValue + index
        })];
    }

    static void StabilizeArrayField(object value, string fieldName, int length, Action<object, int> stabilize)
    {
        var field = value.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.Public);
        if (field?.FieldType.IsArray != true)
            return;

        var elementType = field.FieldType.GetElementType()!;
        var array = Array.CreateInstance(elementType, length);
        for (var i = 0; i < length; i++)
        {
            var item = Activator.CreateInstance(elementType)
                ?? throw new InvalidOperationException($"Cannot create DTO array value: {elementType.FullName}");
            stabilize(item, i);
            array.SetValue(item, i);
        }

        field.SetValue(value, array);
    }

    static void StabilizeFriendSearchResponse(object value)
    {
        var data = value.GetType().GetField("data", BindingFlags.Instance | BindingFlags.Public)?.GetValue(value);
        if (data is null)
            return;

        SetArrayFieldEmpty(data, "partner_chara_info_array");
        var userInfo = data.GetType().GetField("user_info_summary", BindingFlags.Instance | BindingFlags.Public)?.GetValue(data);
        if (userInfo is not null)
            SetArrayFieldEmpty(userInfo, "user_trained_chara_array");
    }

    static void StabilizeSingleModeStartResponse(object value)
    {
        var data = value.GetType().GetField("data", BindingFlags.Instance | BindingFlags.Public)?.GetValue(value);
        var common = data?.GetType().GetField("single_mode_start_common", BindingFlags.Instance | BindingFlags.Public)?.GetValue(data);
        if (common is not null)
            SetReferenceFieldNull(common, "add_trained_chara_array");
    }

    static void StabilizeTrainedCharaLoadResponse(object value)
    {
        var data = value.GetType().GetField("data", BindingFlags.Instance | BindingFlags.Public)?.GetValue(value);
        if (data is null)
            return;

        SetArrayFieldEmpty(data, "trained_chara_array");
        SetArrayFieldEmpty(data, "trained_chara_favorite_array");
    }

    static void SetIntField(object value, string fieldName, int fieldValue)
    {
        var field = value.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.Public);
        if (field is not null && field.FieldType == typeof(int))
            field.SetValue(value, fieldValue);
    }

    static void SetBoolField(object value, string fieldName, bool fieldValue)
    {
        var field = value.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.Public);
        if (field is not null && field.FieldType == typeof(bool))
            field.SetValue(value, fieldValue);
    }

    static object EnsureObjectField(object value, string fieldName)
    {
        var field = value.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.Public)
            ?? throw new InvalidOperationException($"{value.GetType().Name} has no field '{fieldName}'.");
        var current = field.GetValue(value);
        if (current is not null)
            return current;

        var created = Activator.CreateInstance(field.FieldType)
            ?? throw new InvalidOperationException($"Cannot create DTO field value: {field.FieldType.FullName}");
        field.SetValue(value, created);
        return created;
    }

    static void SetArrayFieldEmpty(object value, string fieldName)
    {
        var field = value.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.Public);
        if (field?.FieldType.IsArray != true)
            return;

        field.SetValue(value, Array.CreateInstance(field.FieldType.GetElementType()!, 0));
    }

    static void SetArrayValue(object value, string fieldName, Array fieldValue)
    {
        var field = value.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.Public);
        if (field?.FieldType.IsArray != true)
            return;

        field.SetValue(value, fieldValue);
    }

    static void SetReferenceFieldNull(object value, string fieldName)
    {
        var field = value.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.Public);
        if (field is not null && !field.FieldType.IsValueType)
            field.SetValue(value, null);
    }
}
