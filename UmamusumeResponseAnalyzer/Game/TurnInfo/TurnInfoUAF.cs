using Gallop;
using MathNet.Numerics.RootFinding;
using System;
using System.Collections.Generic;
using System.ComponentModel.Design;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UmamusumeResponseAnalyzer.Entities;

namespace UmamusumeResponseAnalyzer.Game.TurnInfo
{
    public class TurnInfoUAF : TurnInfo
    {
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
        public TurnInfoUAF(SingleModeCheckEventResponse.CommonResponse resp) : base(resp)
        {
            var uaf = resp.sport_data_set;
            TrainingArray = uaf.training_array.Select(x => new TrainingSport(x));
            CommandInfoArray = uaf.command_info_array.Where(x => x.command_id > 1000).Select(x => new ParsedSingleModeSportCommandInfo(resp, this, x));
            AvailableTalkCount = uaf.item_id_array.Count(x => x == 6);

            SportsByColor = TrainingArray.GroupBy(x => x.Color).ToDictionary(x => x.Key, x => x.ToList());
            BlueLevel = SportsByColor[SportColor.Blue].Sum(x => x.SportRank);
            RedLevel = SportsByColor[SportColor.Red].Sum(x => x.SportRank);
            YellowLevel = SportsByColor[SportColor.Yellow].Sum(x => x.SportRank);
        }

        public class ParsedSingleModeSportCommandInfo
        {
            public int CommandId { get; }
            /// <summary>
            /// 项目会出现的训练位置(1~5，对应速耐力根智)
            /// </summary>
            public int TrainIndex { get; }
            public SportColor Color { get; }
            public int TrainLevel { get; }
            /// <summary>
            /// 该训练对应的项目等级
            /// </summary>
            public int SportRank { get; }
            /// <summary>
            /// 该项目会获得的[单个]项目等级
            /// </summary>
            public int GainRank { get; }
            /// <summary>
            /// 该项目会获得的[全部]项目等级之和
            /// </summary>
            public int TotalGainRank { get; }
            public IEnumerable<TrainingPartner> TrainingPartners { get; }

            public ParsedSingleModeSportCommandInfo(SingleModeCheckEventResponse.CommonResponse resp, TurnInfoUAF turn, SingleModeSportCommandInfo ci)
            {
                CommandId = ci.command_id;
                TrainIndex = int.Parse(CommandId.ToString()[3].ToString());
                Color = (SportColor)int.Parse(CommandId.ToString()[1].ToString());
                TrainLevel = resp.chara_info.training_level_info_array.First(x => x.command_id == CommandId).level;
                SportRank = resp.sport_data_set.training_array.First(x => x.command_id == CommandId).sport_rank;
                var test = resp.sport_data_set.command_info_array[TrainIndex - 1].gain_sport_rank_array;
                GainRank = test.First(x => x.command_id == CommandId).gain_rank;
                TotalGainRank = resp.sport_data_set.command_info_array[TrainIndex - 1].gain_sport_rank_array.Sum(x => x.gain_rank);
                var normalCommand = resp.home_info.command_info_array.First(x => x.command_id == CommandId);
                TrainingPartners = normalCommand.training_partner_array.Select(x => new TrainingPartner(turn, x, normalCommand)).OrderBy(x => x.Priority);
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
