using Gallop;
using Spectre.Console;
using UmamusumeResponseAnalyzer.Entities;

namespace UmamusumeResponseAnalyzer.Game
{
    public class CommandInfo
    {
        public int CommandId { get; }
        /// <summary>
        /// 项目会出现的训练位置(1~5，对应速耐力根智)
        /// </summary>
        public int TrainIndex { get; set; }
        public int TrainLevel { get; }
        public IEnumerable<TrainingPartner> TrainingPartners { get; }

        public CommandInfo(SingleModeCheckEventResponse.CommonResponse resp, TurnInfo.TurnInfo turn, int commandId)
        {
            CommandId = commandId;
            if (GameGlobal.ToTrainIndex.TryGetValue(commandId, out var ti))
                TrainIndex = ti + 1;
            else
                AnsiConsole.WriteLine($"[red]未找到{commandId}对应的训练位置[/]");
            var training = resp.chara_info.training_level_info_array.FirstOrDefault(x => x.command_id == CommandId);
            TrainLevel = training != default ? training.level : 0;
            var normalCommand = resp.home_info.command_info_array.First(x => x.command_id == CommandId);
            TrainingPartners = normalCommand.training_partner_array.Select(x => new TrainingPartner(turn, x, normalCommand)).OrderBy(x => x.Priority);
        }
    }
}
