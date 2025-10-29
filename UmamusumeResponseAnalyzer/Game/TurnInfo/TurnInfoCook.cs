using Gallop;
using Spectre.Console;
using System.ComponentModel.Design;

namespace UmamusumeResponseAnalyzer.Game.TurnInfo
{
    public class TurnInfoCook : TurnInfo
    {
        /// <summary>
        /// 将要收货的食材
        /// </summary>
        public SingleModeCookMaterialHarvestInfo[] Harvests { get; set; } = [];
        /// <summary>
        /// 左上角那四个
        /// </summary>
        public SingleModeCookCareHistoryInfo[] Cares { get; set; } = [];
        /// <summary>
        /// 当前拥有的食材
        /// </summary>
        public SingleModeCookMaterialInfo[] Materials { get; set; } = [];
        /// <summary>
        /// 耕地等级
        /// </summary>
        public SingleModeCookFacilityInfo[] Facilities { get; set; } = [];
        /// <summary>
        /// <c>训练后</c>的待收货食材数量、点数
        /// </summary>
        public SingleModeCookCommandMaterialCareInfo[] CommandMaterials { get; set; } = [];
        public IEnumerable<CommandInfo> CommandInfoArray { get; set; } = [];
        public TurnInfoCook(SingleModeCheckEventResponse.CommonResponse resp) : base(resp)
        {
            var cook = resp.cook_data_set;
            Harvests = cook.material_harvest_info_array;
            Cares = cook.care_history_info_array;
            Materials = cook.material_info_array;
            Facilities = cook.facility_info_array;
            CommandMaterials = cook.command_material_care_info_array;
            CommandInfoArray = cook.command_info_array.Select(x => new CommandInfo(resp, this, x.command_id));
            CommandInfoArray = CommandInfoArray.Where(x => x.TrainIndex != 0);
        }
    }
}
