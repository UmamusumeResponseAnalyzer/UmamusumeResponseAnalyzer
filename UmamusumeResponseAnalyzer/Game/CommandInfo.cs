using Gallop;
using Spectre.Console;
using System.Collections.Frozen;
using UmamusumeResponseAnalyzer.Entities;

namespace UmamusumeResponseAnalyzer.Game
{
    public class CommandInfo
    {
        static readonly FrozenDictionary<int, int> ToTrainIndex = new Dictionary<int, int>
        {
            { 1101, 0 },
            { 1102, 1 },
            { 1103, 2 },
            { 1104, 3 },
            { 1105, 4 },
            { 601, 0 },
            { 602, 1 },
            { 603, 2 },
            { 604, 3 },
            { 605, 4 },
            { 101, 0 },
            { 105, 1 },
            { 102, 2 },
            { 103, 3 },
            { 106, 4 },
            { 2101, 0 },
            { 2201, 0 },
            { 2301, 0 },
            { 2102, 1 },
            { 2202, 1 },
            { 2302, 1 },
            { 2103, 2 },
            { 2203, 2 },
            { 2303, 2 },
            { 2104, 3 },
            { 2204, 3 },
            { 2304, 3 },
            { 2105, 4 },
            { 2205, 4 },
            { 2305, 4 },
            { 901, 0 },
            { 902, 2 },
            { 906, 4 }
        }.ToFrozenDictionary();
        public int CommandId { get; }
        /// <summary>
        /// 项目会出现的训练位置(1~5，对应速耐力根智)
        /// </summary>
        public int TrainIndex { get; set; }
        public int TrainLevel { get; }
        public IEnumerable<TrainingPartner> TrainingPartners { get; }

        public CommandInfo(SingleModeCheckEventResponse.CommonResponse resp, TurnInfo.TurnInfo turn, int commandId, IDictionary<int, int> trainIndexDictionary = null!, IDictionary<int, int> toTrainIdDictionary = null!)
        {
            CommandId = commandId;
            if ((trainIndexDictionary ?? ToTrainIndex).TryGetValue(commandId, out var ti))
                TrainIndex = ti + 1;
            else
                AnsiConsole.MarkupLine($"[red]未找到{commandId}对应的训练位置[/]");
            var training = resp.chara_info.training_level_info_array.FirstOrDefault(x => x.command_id == CommandId);
            TrainLevel = training != default ? training.level : 0;
            var normalCommand = resp.home_info.command_info_array.First(x => x.command_id == CommandId);
            TrainingPartners = normalCommand.training_partner_array.Select(x => new TrainingPartner(turn, x, normalCommand, toTrainIdDictionary)).OrderBy(x => x.Priority);
        }
    }
}
