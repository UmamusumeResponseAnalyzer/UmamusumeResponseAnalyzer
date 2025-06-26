using Gallop;

namespace UmamusumeResponseAnalyzer.Game.TurnInfo
{
    public class TurnInfoUAF : TurnInfo
    {
        public static readonly IReadOnlyList<int> LinkCharacterIds = Array.AsReadOnly([1035, 1027, 1048, 1072, 1077, 9044]); // 奖券，6，佐敦，八重，NTR，友人
        public IEnumerable<TrainingSport> TrainingArray { get; }
        /// <summary>
        /// 这里的CommandInfo是剧本特色CommandInfo，基础的需要用GetCommonResponse().home_info.command_info_array
        /// </summary>
        public IEnumerable<ParsedSingleModeSportCommandInfo> CommandInfoArray { get; }
        public int AvailableTalkCount { get; }

        public Dictionary<SportColor, List<TrainingSport>> SportsByColor { get; }
        public int BlueLevel { get; }
        public int RedLevel { get; }
        public int YellowLevel { get; }
        /// <summary>
        /// 是否有额外的项目等级获得（非训练回合后获得）。
        /// </summary>
        /// <returns>
        /// 任意训练项目通过判定即为true。
        /// </returns>
        public bool IsRankGainIncreased { get; }
        public TurnInfoUAF(SingleModeCheckEventResponse.CommonResponse resp) : base(resp)
        {
            var uaf = resp.sport_data_set;
            TrainingArray = uaf.training_array.Select(x => new TrainingSport(x));
            CommandInfoArray = uaf.command_info_array.Where(x => x.command_id > 1000).Select(x => new ParsedSingleModeSportCommandInfo(resp, this, x.command_id));
            AvailableTalkCount = uaf.item_id_array.Count(x => x == 6);

            SportsByColor = TrainingArray.GroupBy(x => x.Color).ToDictionary(x => x.Key, x => x.ToList());
            BlueLevel = SportsByColor[SportColor.Blue].Sum(x => x.SportRank);
            RedLevel = SportsByColor[SportColor.Red].Sum(x => x.SportRank);
            YellowLevel = SportsByColor[SportColor.Yellow].Sum(x => x.SportRank);
            IsRankGainIncreased = CommandInfoArray.Any(x => x.IsCurrentRankGainIncreased);
        }

        public class ParsedSingleModeSportCommandInfo : CommandInfo
        {
            private int ActualGainRankWithoutBuff { get; }
            public SportColor Color { get; }
            /// <summary>
            /// 该训练对应的项目等级
            /// </summary>
            public int SportRank { get; }
            /// <summary>
            /// 该项目会获得的[单个]项目等级
            /// </summary>
            public int GainRank { get; }
            public int ActualGainRank
            {
                get
                {
                    if (IsRankGainIncreased)
                        return ActualGainRankWithoutBuff + 3;
                    else
                        return ActualGainRankWithoutBuff;
                }
            }
            /// <summary>
            /// 该项目会获得的[全部]项目等级之和
            /// </summary>
            public int TotalGainRank { get; }
            /// <summary>
            /// 是否有额外的项目等级获得（非训练回合后获得）。<br/>
            /// 仅用当前项目判断，不准确，请优先使用<c>TurninfoUAF.IsRankGainIncreased</c>
            /// </summary>
            /// <returns>
            /// 项目等级已满时为false
            /// 项目等级过高导致无法判断时为false（如当前为99级，训练后无论如何都会到达满级时）
            /// </returns>
            public bool IsCurrentRankGainIncreased { get; }
            public bool IsRankGainIncreased { get; }

            public ParsedSingleModeSportCommandInfo(SingleModeCheckEventResponse.CommonResponse resp, TurnInfoUAF turn, int commandId) : base(resp, turn, commandId)
            {
                TrainIndex = int.Parse(CommandId.ToString()[3].ToString());
                Color = (SportColor)int.Parse(CommandId.ToString()[1].ToString());
                SportRank = resp.sport_data_set.training_array.First(x => x.command_id == CommandId).sport_rank;
                var test = resp.sport_data_set.command_info_array[TrainIndex - 1].gain_sport_rank_array;
                GainRank = test.First(x => x.command_id == CommandId).gain_rank;
                TotalGainRank = resp.sport_data_set.command_info_array[TrainIndex - 1].gain_sport_rank_array.Sum(x => x.gain_rank);
                IsRankGainIncreased = turn.IsRankGainIncreased;

                var supports = TrainingPartners.Where(x => !x.IsNpc);
                var shining = TrainingPartners.Any(x => x.Shining) ? 2 : 1;
                var partnerAdd = supports.Count() switch
                {
                    0 => 0,
                    1 => 1,
                    2 => 2,
                    3 => 2,
                    4 => 3,
                    5 => 3
                };
                var links = TrainingPartners.Select(x => Database.Names.GetSupportCard(x.CardId).CharaId).Intersect(LinkCharacterIds);
                ActualGainRankWithoutBuff = shining * (3 + partnerAdd) + links.Count();
                IsCurrentRankGainIncreased = GainRank - ActualGainRankWithoutBuff == 3;
            }
        }
        public class TrainingSport
        {
            /// <summary>
            /// 未知
            /// </summary>
            public int CommandType { get; }
            /// <summary>
            /// 训练ID
            /// </summary>
            public int CommandId { get; }
            /// <summary>
            /// 项目等级
            /// </summary>
            public int SportRank { get; }
            /// <summary>
            /// 项目颜色
            /// </summary>
            public SportColor Color { get; }
            /// <summary>
            /// 项目颜色的MarkupText形式（如[red]红[/])
            /// </summary>
            public string ColoredText { get; }
            /// <summary>
            /// 项目会出现的训练位置(1~5，对应速耐力根智)
            /// </summary>
            public int TrainIndex { get; }

            public TrainingSport(SingleModeSportTraining train)
            {
                CommandType = train.command_type;
                CommandId = train.command_id;
                SportRank = train.sport_rank;
                Color = (SportColor)int.Parse(CommandId.ToString()[1].ToString());
                ColoredText = Color switch
                {
                    SportColor.Blue => $"[blue]蓝[/]",
                    SportColor.Red => $"[red]红[/]",
                    SportColor.Yellow => $"[yellow]黄[/]",
                    _ => throw new NotImplementedException(),
                };
                TrainIndex = int.Parse(CommandId.ToString()[3].ToString());
            }
        }
        public enum SportColor
        {
            Blue = 1,
            Red = 2,
            Yellow = 3,
        }
    }
}
