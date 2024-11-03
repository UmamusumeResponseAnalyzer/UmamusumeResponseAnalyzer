using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UmamusumeResponseAnalyzer.Game.TurnInfo;
using UmamusumeResponseAnalyzer.Game;
using Spectre.Console;

namespace UmamusumeResponseAnalyzer.AI
{
    public class GameStatusSend_Mecha: GameStatusSend_Base<PersonBase>
    {
        /// <summary>
        /// 研究lv
        /// </summary>
        public int[] mecha_rivalLv;

        /// <summary>
        /// 齿轮量表 0-6
        /// </summary>
        public int mecha_overdrive_energy;

        /// <summary>
        /// 齿轮效果是否启动
        /// </summary>
        public int mecha_overdrive_enabled;

        /// <summary>
        /// 升级点
        /// </summary>
        public int mecha_EN;

        /// <summary>
        /// 头胸脚
        /// </summary>
        public List<List<int>> mecha_upgrade;

        /// <summary>
        /// 每个训练是否有齿轮
        /// </summary>
        public bool[] mecha_hasGear;

        /// <summary>
        /// UGE结果 012分别为BAS
        /// </summary>
        public int[] mecha_win_history;

        public GameStatusSend_Mecha(Gallop.SingleModeCheckEventResponse @event): base(@event)
        {
            var x = @event;
            var mecha = @event.data.mecha_data_set;
            if (mecha != null)
            {
                mecha_rivalLv =  new[] {
                    mecha.rival_info.speed,
                    mecha.rival_info.stamina,
                    mecha.rival_info.power,
                    mecha.rival_info.guts,
                    mecha.rival_info.wiz
                };
                mecha_overdrive_energy = mecha.overdrive_info.remain_num * 3 + mecha.overdrive_info.energy_num;
                mecha_overdrive_enabled = mecha.overdrive_info.over_drive_state > 0 ? 1 : 0;
                mecha_EN = mecha.board_info_array.Sum(x => x.chip_info_array.First(x => x.chip_id > 2000).point);
                // 升级情况
                mecha_upgrade = new List<List<int>>();
                for (var i=0; i<3; ++i)
                {
                    mecha_upgrade.Add(new List<int> { 0, 0, 0 });
                    var board = mecha.board_info_array[i];
                    // 0-2是芯片等级，3是总等级
                    // 芯片编号（备用）：
                    // 头 1005 1006 1007
                    // 胸 1002 1004 1008
                    // 脚 1001 1003 1009
                    for (var j = 0; j < 3; ++j)
                        mecha_upgrade[i][j] = board.chip_info_array[j].point;
                }
                mecha_win_history = mecha.upgrade_race_result_array.Select(x => x.result_type - 1).ToArray();
                
                mecha_hasGear = new bool[] { false, false, false, false, false };
                for (var i=0; i<5; ++i)
                {
                    var tid = GameGlobal.TrainIds[i];
                    mecha_hasGear[i] = mecha.command_info_array.First(
                        x => GameGlobal.ToTrainId[x.command_id] == tid
                    ).is_recommend;
                }
            }
        }
    }
}
