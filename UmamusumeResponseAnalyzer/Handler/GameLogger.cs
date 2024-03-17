using Gallop;
using MathNet.Numerics.Distributions;
using Spectre.Console;
using System;
using System.Collections.Generic;
using System.Diagnostics.Eventing.Reader;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UmamusumeResponseAnalyzer.Entities;
using UmamusumeResponseAnalyzer.Game;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace UmamusumeResponseAnalyzer.Handler { 
    public static partial class Handlers
    {
        public static void GameLogger(Gallop.SingleModeCheckEventResponse @event)
        {
            if ((@event.data.unchecked_event_array != null && @event.data.unchecked_event_array.Length > 0) || @event.data.race_start_info != null) return;
            int turn = @event.data.chara_info.turn;
            string regular_path = "Logs/Regular.log";
            string uaf_path = "Logs/uaf.log";
            string race_path = "Logs/race.log";
            string UAFLog = $"{turn} ";
            //声明所有变量
            int lh_id1 = 30188;//SSR凉花
            int lh_id2 = 10104;//R凉花
            int lh_id = 0; //凉花 position
            int qc_id = 102;//橙色矮子 position
            int yms_id = 103; //嗜血记者 position

            int lh_pos = 0;
            int qc_pos = 0;
            int yms_pos = 0;//三人的位置，按照凉花-理事长-记者顺序排，0代表咕咕，别的代表位置

            int[] training_colorful_num = new int[5]; ;//每个训练的彩圈数
            int is_red_boom = 0;
            int is_yellow_boom = 0;
            int is_blue_boom = 0; //红黄蓝爆了吗
            int total_training_effect = 0;//总训练加成
            string all_equipment_lvl = ""; //全体设施等级
            string all_trains = "";//所有训练
       
            int[] equip_color=new int[5];//每个训练颜色
            int[] equip_lvl=new int[5];//每个训练等级
            List<string> headers = new List<string>();//训练头部
            List<string> upper_val= new List<string>();//上数值
            List<string> lower_val= new List<string>();//下数值
            List<string> training_colorful_list=new List<string>();//每个训练的人头彩圈情况
            //逐个解决
            //首先找一下凉花的位置
            var card_info=@event.data.chara_info.support_card_array;
            foreach (var a in card_info)
            {
                if(a.support_card_id==lh_id1 | a.support_card_id == lh_id2)
                {
                    lh_id = a.position;
                }
            }
            //看看爆没爆
            var stances = @event.data.sport_data_set.effected_stance_array;
            if ( stances != null) {
                foreach (var item in stances)
                {
                    if (Convert.ToInt32(item.stance_id) / 100 == 1)
                    {
                        is_blue_boom = 1;
                    }
                    if (Convert.ToInt32(item.stance_id) / 100 == 2)
                    {
                        is_red_boom = 1;
                    }
                    if (Convert.ToInt32(item.stance_id) / 100 == 3)
                    {
                        is_yellow_boom = 1;
                    }
                }
            }
            //总体训练等级
            int[] training_effect =new int[] { 1, 3, 7, 12, 17 };
            var traineffects = @event.data.sport_data_set.compe_effect_id_array;
            if ( traineffects != null) {  
                foreach (var item in traineffects)
                {
                    int te_lvl= (Convert.ToInt32(item) % 100) / 10;
                    if (te_lvl <= 0 | te_lvl>=5)
                    {
                        continue;
                    }
                    else
                    {
                        total_training_effect += training_effect[te_lvl - 1];
                    }
                } 
            }
            //15个设施等级
            var training_equipment_lvls = @event.data.sport_data_set.training_array;
            foreach(var item in training_equipment_lvls)
            {
                all_equipment_lvl += $" {item.sport_rank}";
            }
            //遍历一次base训练
            var home_info = @event.data.home_info;
            var home_commands = home_info.command_info_array;
            foreach (var item in home_commands)
            {
                if (item.command_type != 1) {
                    //不是训练的不要
                    continue;


                }
                var partners = item.training_partner_array;
                foreach (var partner in partners)
                {
                    if (partner == lh_id)
                    {
                        //芝士凉花
                        lh_pos = item.command_id % 10;//代表训练种类
                    }
                    else if (partner == qc_id)
                    {
                        //小矮子
                        qc_pos = item.command_id % 10;
                    }
                    else if (partner == yms_id)
                    {
                        //嗜血记者
                        yms_pos = item.command_id % 10;
                    }
                }
                //组装临时消息（下数值）
                string lowval = "[";
                var trainlowvalinfo = item.params_inc_dec_info_array;
                int[] six_low_vals=new int[] { 0, 0, 0, 0, 0, 0 };
                foreach(var a in trainlowvalinfo)
                {
                    if(a.target_type == 10)
                    {
                        continue;
                    }
                    else if(a.target_type == 30)
                    {
                        six_low_vals[5] = a.value;
                    }
                    else
                    {
                        six_low_vals[Convert.ToInt32(a.target_type) - 1] = a.value;
                    }
                }

                foreach(var a in six_low_vals)
                {
                    lowval += $"{a} ";
                }

                lowval += "]";
                lower_val.Add(lowval);
                //组装临时消息（彩圈情况）
                int localshining = 0;
                string shining = "[";
                partners = item.training_partner_array;
                var cards = @event.data.chara_info.support_card_array.ToDictionary(x => x.position, x => x.support_card_id);
                if (partners != null) {
                    foreach (var a in partners)
                    {
                        if (a >= 1 && a < 7)
                        {
                            var shouldshining = false;
                            var name = Database.Names.GetSupportCard(cards[a]).Nickname;
                            var friendship = @event.data.chara_info.evaluation_info_array.First(x => x.target_id == a).evaluation;
                            if (name.Contains("[友]")){
                                
                            }
                            else if (friendship < 80)
                            {
                                
                            }
                            else
                            {
                                //看看是不是得意
                                
                                var commandId1 = GameGlobal.ToTrainId[item.command_id];
                                shouldshining = friendship >= 80 &&
                                    name.Contains(commandId1 switch
                                    {
                                        101 => "[速]",
                                        105 => "[耐]",
                                        102 => "[力]",
                                        103 => "[根]",
                                        106 => "[智]",
                                    });
                                if ((cards[a] == 30137 && @event.data.chara_info.chara_effect_id_array.Any(x => x == 102)) || //神团
                                (cards[a] == 30067 && @event.data.chara_info.chara_effect_id_array.Any(x => x == 101)) || //皇团
                                (cards[a] == 30081 && @event.data.chara_info.chara_effect_id_array.Any(x => x == 100)) //天狼星
                                )
                                {
                                    shouldshining = true;
                                    
                                }


                            }

                            if (shouldshining)
                            {
                                localshining += 1;
                                //shining += $" {cards[a]}";
                            }
                           
                            
                                shining += $" {cards[a]}";
                            
                        }
                    }
                }
                shining += "]";
                training_colorful_list.Add(shining);
                training_colorful_num[Convert.ToInt32(item.command_id) % 10 - 1] = localshining;
            }

            var uppertraininglst = @event.data.sport_data_set.command_info_array;
            var traininglvldict = @event.data.sport_data_set.training_array.ToDictionary(x => x.command_id, x => x.sport_rank);
            foreach ( var item in uppertraininglst)
            {
                //先造header
                string header = "";
                switch(Convert.ToInt32(item.command_id)%10)
                {
                    case 1:
                        header += "1: ";
                        break;
                    case 2:
                        header += "2: ";
                        break;
                    case 3:
                        header += "3: ";
                        break;
                    case 4:
                        header += "4: ";
                        break;
                    case 5:
                        header += "5: ";
                        break;
                }
                //颜色
                header += $"{(Convert.ToInt32(item.command_id) % 1000) / 100} ";
                //设施等级
                header += $"{traininglvldict[item.command_id]} ";
                //点完这下加多少
                int[] trainlvlinc = new int[] { 0, 0, 0, 0, 0 };
                var trainlvlinclst = item.gain_sport_rank_array;
                foreach(var a in trainlvlinclst)
                {
                    trainlvlinc[Convert.ToInt32(a.command_id) % 10 - 1] = a.gain_rank;
                }

                foreach(var a in trainlvlinc)
                {
                    header += $"{a} ";
                }

                headers.Add(header);

                //最后准备上数值

                string localuppervalue = "[";
                var localuppervallst = item.params_inc_dec_info_array;
                int[] six_high_vals = new int[] { 0, 0, 0, 0, 0, 0 };
                foreach (var a in localuppervallst)
                {
                    if (a.target_type == 10)
                    {
                        continue;
                    }
                    else if (a.target_type == 30)
                    {
                        six_high_vals[5] = a.value;
                    }
                    else
                    {
                        six_high_vals[Convert.ToInt32(a.target_type) - 1] = a.value;
                    }
                }

                foreach (var a in six_high_vals)
                {
                    localuppervalue += $"{a} ";
                }

                localuppervalue += "]";
                upper_val.Add(localuppervalue);
            }
            //数值极其上限
            var value_now = @event.data.chara_info;
            all_trains += $" {value_now.speed}";
            all_trains += $" {value_now.stamina}";
            all_trains += $" {value_now.power}";
            all_trains += $" {value_now.guts}";
            all_trains += $" {value_now.wiz}";
            all_trains += $" {value_now.max_speed}";
            all_trains += $" {value_now.max_stamina}";
            all_trains += $" {value_now.max_power}";
            all_trains += $" {value_now.max_guts}";
            all_trains += $" {value_now.max_wiz}";
            //组装
            UAFLog += $"{lh_pos}{yms_pos}{qc_pos}";
            UAFLog += $" {is_blue_boom}{is_red_boom}{is_yellow_boom}";
            UAFLog += $" {total_training_effect}";
            UAFLog += all_equipment_lvl;
            UAFLog += all_trains + " ";
            string lianghualog = $"{turn} ";
            for (int i = 0; i < 5; i++)
            {
                UAFLog += headers[i] + " ";
                UAFLog += upper_val[i] + " ";
                UAFLog += lower_val[i] + " ";
                UAFLog += $"{training_colorful_num[i]} ";
                lianghualog += training_colorful_list[i] + " ";
            }
            //System.Console.WriteLine(UAFLog);
            var is_regular = @event.data.chara_info.playing_state == 1;
            var is_race = @event.data.chara_info.playing_state == 2;
            var is_uaf = (!is_regular && !is_race);
            UAFLog += "\n";
            lianghualog += "\n";
            if (is_regular)
            {
                File.AppendAllText(regular_path, UAFLog);
            }
            else if(is_race)
            {
                File.AppendAllText(race_path, UAFLog);

            }
            else if(is_uaf)
            {
                File.AppendAllText(uaf_path, UAFLog);
            }

            //昨天的部分重写了一下
            string lhdir = "lianghua.log";
            string cddir = "chongdian.log";

            if (lh_id != 0)
            {
                //证明你带了凉花，凉花，我的凉花，好想和凉花打真人cs啊
                var friendship = @event.data.chara_info.evaluation_info_array.First(x => x.target_id == lh_id).evaluation;
                if (friendship >= 60 && is_regular)
                {
                    File.AppendAllText(lhdir, lianghualog);
                }
                else if (friendship <60)
                {
                    System.Console.WriteLine($"凉花还没激活，当前羁绊{friendship}");
                }
            }

            if (turn >= 2)
            {
                var t = turn;
                if (GameStats.stats[t] == null) { }
                    
                else if (!GameGlobal.TrainIds.Any(x => x == GameStats.stats[t].playerChoice)) { }//没训练

                else if (GameStats.stats[t].isTrainingFailed) { }//训练失败

                else if (!GameStats.stats[t].uaf_friendAtTrain[GameGlobal.ToTrainIndex[GameStats.stats[t].playerChoice]]) { }

                else if (GameStats.stats[t].uaf_friendEvent == 5) { }//启动事件

                else
                {
                    if (GameStats.stats[t].uaf_friendEvent == 1 || GameStats.stats[t].uaf_friendEvent == 2)
                    {
                        File.AppendAllText(cddir, $"{t} 1\n");
                    }
                    else
                    {
                        File.AppendAllText(cddir, $"{t} 0\n");
                    }
                }
            }



        }
    }
}
