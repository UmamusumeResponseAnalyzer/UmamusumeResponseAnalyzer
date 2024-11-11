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
        /// 回合类型，1为正常训练回合（含比赛），2为升级机甲回合
        /// </summary>
        public int gameStage;

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
        public bool mecha_overdrive_enabled;

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
            if (!islegal) return;
            var x = @event;
            var mecha = @event.data.mecha_data_set;
            var stage = @event.data.chara_info.playing_state == 1 ? 1 : //训练回合
                @event.data.chara_info.playing_state == 26 ? 2 :  //选升级回合
                0; //未知
            if (stage == 0)
            {
                islegal = false;
                throw new Exception($"GameStatusSend_Mecha playing_state={@event.data.chara_info.playing_state}");
            }
            gameStage = stage;





            if (mecha != null)
            {
                mecha_rivalLv = new[] {
                    mecha.rival_info.speed,
                    mecha.rival_info.stamina,
                    mecha.rival_info.power,
                    mecha.rival_info.guts,
                    mecha.rival_info.wiz
                };
                mecha_overdrive_energy = mecha.overdrive_info.remain_num * 3 + mecha.overdrive_info.energy_num;
                mecha_overdrive_enabled = mecha.overdrive_info.over_drive_state > 0 ? true : false;
                if(stage == 1 && mecha.overdrive_info.is_overdrive_burst > 0 && !mecha_overdrive_enabled)//ura期间连续开overdrive，还没开的时候不发给umaai
                {
                    islegal = false;
                    return;
                }
                mecha_EN = mecha.board_info_array.Sum(x => x.chip_info_array.First(x => x.chip_id > 2000).point) + @event.data.mecha_data_set.tuning_point;
                // 升级情况
                mecha_upgrade = new List<List<int>>();
                for (var i = 0; i < 3; ++i)
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
                mecha_win_history = new int[5] { 0, 0, 0, 0, 0 };
                for (var i = 0; i < 5; ++i)
                {
                    if (mecha.upgrade_race_result_array.Any(x => x.schedule_id == i + 1))
                        mecha_win_history[i] = mecha.upgrade_race_result_array.First(x => x.schedule_id == i + 1).result_type - 1;
                }

                mecha_hasGear = new bool[] { false, false, false, false, false };
                for (var i = 0; i < 5; ++i)
                {
                    var tid = GameGlobal.TrainIds[i];
                    mecha_hasGear[i] = mecha.command_info_array.First(
                        x => GameGlobal.ToTrainId[x.command_id] == tid
                    ).is_recommend;
                }
            }
            else islegal = false;
        }
    }
}
