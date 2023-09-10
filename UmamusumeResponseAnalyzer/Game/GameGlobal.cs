using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;




namespace UmamusumeResponseAnalyzer.Game
{
    public class GameGlobal
    {
        static public int[] TrainIds = new int[]{ 101, 105, 102, 103, 106 };
        static public Dictionary<int, int> XiahesuIds = new Dictionary<int, int>()
            {
                {101,601} ,
                {105,602} ,
                {102,603} ,
                {103,604} ,
                {106,605} ,
            };
        static public Dictionary<int, int> HaiwaiIds = new Dictionary<int, int>()
            {
                {101,1101} ,
                {105,1102} ,
                {102,1103} ,
                {103,1104} ,
                {106,1105} ,
            };
        static public Dictionary<int, int> ToTrainId = new Dictionary<int, int>()
            {
                {1101,101} ,
                {1102,105} ,
                {1103,102} ,
                {1104,103} ,
                {1105,106} ,
                {601,101} ,
                {602,105} ,
                {603,102} ,
                {604,103} ,
                {605,106} ,
                {101,101} ,
                {105,105} ,
                {102,102} ,
                {103,103} ,
                {106,106} ,
            };
        static public Dictionary<int, int> ToTrainIndex= new Dictionary<int, int>()
            {
                {1101,0} ,
                {1102,1} ,
                {1103,2} ,
                {1104,3} ,
                {1105,4} ,
                {601,0} ,
                {602,1} ,
                {603,2} ,
                {604,3} ,
                {605,4} ,
                {101,0} ,
                {105,1} ,
                {102,2} ,
                {103,3} ,
                {106,4} ,
            };
        static public Dictionary<int, string> TrainNames = new Dictionary<int, string>()
            {
                {101,"速"} ,
                {105,"耐"} ,
                {102,"力"} ,
                {103,"根"} ,
                {106,"智"} ,
            };

        static public Dictionary<int, string> LArcSSEffectNameFullColored = new Dictionary<int, string>()
            {
                {1,"技能hint"} ,
                {3,"[#00ff00]体力[/]"} ,
                {4,"[#00ffff]体力与上限[/]"} ,//最好的，用亮色
                {5,"[#00ff00]心情体力[/]"} ,
                {6,"充电"} ,
                {7,"适性pt"} ,
                {8,"[#00ff00]爱娇[/]"} ,
                {9,"上手"} ,
                {11,"属性"} ,
                {12,"[#0000ff]技能点[/]"} ,//最烂的，用个深色
            };
        static public Dictionary<int, string> LArcSSEffectNameColored = new Dictionary<int, string>()
            {
                {1,"技能"} ,
                {3,"[#00ff00]体力[/]"} ,
                {4,"[#00ff00]体力[/]"} ,
                {5,"[#00ff00]心情[/]"} ,
                {6,"充电"} ,
                {7,"适pt"} ,
                {8,"[#00ff00]爱娇[/]"} ,
                {9,"上手"} ,
                {11,"属性"} ,
                {12,"技pt"} ,
            };
        static public Dictionary<int, string> LArcSSEffectNameColoredShort = new Dictionary<int, string>()
            {
                {1,"技"} ,
                {3,"[#00ff00]体[/]"} ,
                {4,"[#00ff00]体[/]"} ,
                {5,"[#00ff00]心[/]"} ,
                {6,"充"} ,
                {7,"适"} ,
                {8,"[#00ff00]娇[/]"} ,
                {9,"练"} ,
                {11,"属"} ,
                {12,"pt"} ,
            };

        // 期待度训练加成，每5%一档
        static public int[] LArcTrainBonusEvery5Percent = new int[41] { 0, 5, 8, 10, 13, 15, 16, 17, 18, 19, 20, 21, 22, 23, 24, 25, 26, 27, 28, 29, 30, 30, 31, 31, 32, 32, 33, 33, 34, 34, 35, 35, 36, 36, 37, 37, 38, 38, 39, 39, 40 };



    }

    public static class ScoreUtils
    {
        public static double ScoreOfVital(int vital, int maxVital)
        {
            //四段折线
            if (vital <= 50) return 2.5 * vital;
            else if (vital <= 75) return 1.7 * (vital - 50) + ScoreOfVital(50, maxVital);
            else if (vital <= maxVital - 10) return 1.2 * (vital - 75) + ScoreOfVital(75, maxVital);
            else return 0.7 * (vital - (maxVital - 10)) + ScoreOfVital(maxVital - 10, maxVital);
        }
        public static int ReviseOver1200(int x)
        {
            if (x <= 1200) return x;
            else return x * 2 - 1200;
        }

    }
}
