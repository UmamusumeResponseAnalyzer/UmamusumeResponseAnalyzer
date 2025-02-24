using System.Collections.Frozen;
using static UmamusumeResponseAnalyzer.Localization.Game;
using static UmamusumeResponseAnalyzer.Localization.CommandInfo.Cook;

namespace UmamusumeResponseAnalyzer.Game
{
    public static class GameGlobal
    {
        public static readonly int[] TrainIds = [101, 105, 102, 103, 106];
        public static readonly int[] TrainIdsMecha = [901, 105, 902, 103, 906]; //Mecha杯（9号剧本）
        public static readonly String[] TrainEnglishName = ["speed", "stamina", "power", "guts", "wiz"]; 
        public static readonly FrozenDictionary<int, int> XiahesuIds = new Dictionary<int, int>
        {
            { 101, 601 },
            { 105, 602 },
            { 102, 603 },
            { 103, 604 },
            { 106, 605 }
        }.ToFrozenDictionary();
        public static readonly FrozenDictionary<int, int> ToTrainId = new Dictionary<int, int>
        {
            [1101] = 101,
            [1102] = 105,
            [1103] = 102,
            [1104] = 103,
            [1105] = 106,
            [601] = 101,
            [602] = 105,
            [603] = 102,
            [604] = 103,
            [605] = 106,
            [101] = 101,
            [105] = 105,
            [102] = 102,
            [103] = 103,
            [106] = 106,
            [2101] = 101,
            [2201] = 101,
            [2301] = 101,
            [2102] = 105,
            [2202] = 105,
            [2302] = 105,
            [2103] = 102,
            [2203] = 102,
            [2303] = 102,
            [2104] = 103,
            [2204] = 103,
            [2304] = 103,
            [2105] = 106,
            [2205] = 106,
            [2305] = 106,
            [901] = 101,
            [902] = 102,
            [906] = 106
        }.ToFrozenDictionary();
        public static readonly FrozenDictionary<int, int> ToTrainIndex = new Dictionary<int, int>
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
        public static readonly FrozenDictionary<string, int> EnglishNameToTrainIndex = new Dictionary<string, int>
        {
            { "speed", 0 },
            { "stamina", 1 },
            { "power", 2 },
            { "guts", 3 },
            { "wiz", 4 },
        }.ToFrozenDictionary();
        public static readonly FrozenDictionary<int, string> TrainNames = new Dictionary<int, string>
        {
            { 101, I18N_Speed },
            { 105, I18N_Stamina },
            { 102, I18N_Power },
            { 103, I18N_Nuts },
            { 106, I18N_Wiz }
        }.ToFrozenDictionary();
        public static readonly FrozenDictionary<int, string> TrainNamesSimple = new Dictionary<int, string>
        {
            { 101, I18N_SpeedSimple },
            { 105, I18N_StaminaSimple },
            { 102, I18N_PowerSimple },
            { 103, I18N_NutsSimple },
            { 106, I18N_WizSimple }
        }.ToFrozenDictionary();
        public static readonly FrozenDictionary<int, string> GrandMastersSpiritNamesColored = new Dictionary<int, string>
        {
            { 1, $"[red]{I18N_SpeedSimple}[/]" },
            { 2, $"[red]{I18N_StaminaSimple}[/]" },
            { 3, $"[red]{I18N_PowerSimple}[/]" },
            { 4, $"[red]{I18N_NutsSimple}[/]" },
            { 5, $"[red]{I18N_WizSimple}[/]" },
            { 6, $"[red]星[/]" },
            { 9, $"[blue]{I18N_SpeedSimple}[/]" },
            { 10, $"[blue]{I18N_StaminaSimple}[/]" },
            { 11, $"[blue]{I18N_PowerSimple}[/]" },
            { 12, $"[blue]{I18N_NutsSimple}[/]" },
            { 13, $"[blue]{I18N_WizSimple}[/]" },
            { 14, $"[blue]星[/]" },
            { 17, $"[yellow]{I18N_SpeedSimple}[/]" },
            { 18, $"[yellow]{I18N_StaminaSimple}[/]" },
            { 19, $"[yellow]{I18N_PowerSimple}[/]" },
            { 20, $"[yellow]{I18N_NutsSimple}[/]" },
            { 21, $"[yellow]{I18N_WizSimple}[/]" },
            { 22, $"[yellow]星[/]" }
        }.ToFrozenDictionary();
        public static readonly FrozenDictionary<int, string> LArcSSEffectNameFullColored = new Dictionary<int, string>
        {
            { 1, "技能hint" },
            { 3, "[#00ff00]体力[/]" },
            { 4, "[#00ffff]体力与上限[/]" },//最好的，用亮色
            { 5, "[#00ff00]心情体力[/]" },
            { 6, "充电" },
            { 7, "适性pt" },
            { 8, "[#00ff00]爱娇[/]" },
            { 9, "上手" },
            { 11, "属性" },
            { 12, "[#0000ff]技能点[/]" } //最烂的，用个深色
        }.ToFrozenDictionary();
        //主要是给影响育成节奏的项目加上颜色
        public static readonly FrozenDictionary<int, string> LArcSSEffectNameColored = new Dictionary<int, string>
        {
            { 1, "技能" } ,
            { 3, "[#00ff00]体力[/]" },
            { 4, "[#00ffff]体力[/]" },
            { 5, "[#00ff00]心情[/]" },
            { 6, "[#ff00ff]充电[/]" },
            { 7, "适pt" },
            { 8, "[#ffff00]爱娇[/]" },
            { 9, "上手" },
            { 11, "属性" },
            { 12, "技pt" }
        }.ToFrozenDictionary();
        public static readonly FrozenDictionary<int, string> LArcSSEffectNameColoredShort = new Dictionary<int, string>
        {
            { 1, "技" },
            { 3, "[#00ff00]体[/]" },
            { 4, "[#00ffff]体[/]" },
            { 5, "[#00ff00]心[/]" },
            { 6, "充" },
            { 7, "适" },
            { 8, "[#ffff00]娇[/]" },
            { 9, "练" },
            { 11, "属" },
            { 12, "pt" },
        }.ToFrozenDictionary();
        // from text_data #349, 后续可能会改
        public static readonly FrozenDictionary<int, string> CookDishName = new Dictionary<int, string>
        {
            { 1, "三明治" },
            { 2, "咖喱" },
            { 3, "速度II" },
            { 4, "耐力II" },
            { 5, "力量II" },
            { 6, "根性II" },
            { 7, "智力II" },
            { 8, "速度II+1" },
            { 9, "耐力II+1" },
            { 10, "力量II+1" },
            { 11, "根性II+1" },
            { 12, "智力II+1" },
            { 13, "速度II+2" },
            { 14, "耐力II+2" },
            { 15, "力量II+2" },
            { 16, "根性II+2" },
            { 17, "智力II+2" },
            { 18, "速度III" },
            { 19, "耐力III" },
            { 20, "力量III" },
            { 21, "根性III" },
            { 22, "智力III" },
            { 23, "速度III+1" },
            { 24, "耐力III+1" },
            { 25, "力量III+1" },
            { 26, "根性III+1" },
            { 27, "智力III+1" },
            { 28, "速度III+2" },
            { 29, "耐力III+2" },
            { 30, "力量III+2" },
            { 31, "根性III+2" },
            { 32, "智力III+2" },
            { 33, "GI拼盘" },
            { 34, "GI拼盘+1" },
            { 35, "超满足GI拼盘+1" }
        }.ToFrozenDictionary();

        public static readonly FrozenDictionary<int, int> CookDishIdUmaAI = new Dictionary<int, int>
        {
            { 1, 1 },
            { 2, 2 },
            { 3, 3 },
            { 4, 4 },
            { 5, 5 },
            { 6, 6 },
            { 7, 7 },
            { 8, 3 },
            { 9, 4 },
            { 10, 5 },
            { 11, 6 },
            { 12, 7 },
            { 13, 3 },
            { 14, 4 },
            { 15, 5 },
            { 16, 6 },
            { 17, 7 },
            { 18, 8 },
            { 19, 9 },
            { 20, 10 },
            { 21, 11 },
            { 22, 12 },
            { 23, 8 },
            { 24, 9 },
            { 25, 10 },
            { 26, 11 },
            { 27, 12 },
            { 28, 8 },
            { 29, 9 },
            { 30, 10 },
            { 31, 11 },
            { 32, 12 },
            { 33, 13 },
            { 34, 13 },
            { 35, 13 }
        }.ToFrozenDictionary();
        public static FrozenDictionary<int, string> CookEffectName = new Dictionary<int, string>
        {
            { 2, "训练" },
            { 21, "赛后" },
            { 201, "体力" },
            { 202, "心情" },
            { 203, "羁绊" },
            { 204, "分身" },
            { 205, "上限" },
            { 206, "得意" },
            { 207, "PT" },
            { 208, "粉丝" }
        }.ToFrozenDictionary();
        // 期待度训练加成，每5%一档
        public static readonly int[] LArcTrainBonusEvery5Percent = [0, 5, 8, 10, 13, 15, 16, 17, 18, 19, 20, 21, 22, 23, 24, 25, 26, 27, 28, 29, 30, 30, 31, 31, 32, 32, 33, 33, 34, 34, 35, 35, 36, 36, 37, 37, 38, 38, 39, 39, 40];
        public static readonly int[] LArcScenarioLinkCharas = [1007, 1014, 1025, 1049, 1067, 1070, 1107];
        public static readonly int[] LArcLessonMapping = [2, 0, 5, 3, 1, 4, 6, 7, 8, 9];
        public static readonly int[] LArcLessonMappingInv = [2, 5, 1, 4, 6, 3, 7, 8, 9, 10];
        public static readonly string[] CookMaterialName = [I18N_Carrot, I18N_Garlic, I18N_Potato, I18N_Chili, I18N_Berry];
        public static readonly string[] CookSuccessEffect = ["体力+10", "心情+1", "羁绊+3", "分身+1", "体力上限+4"];
        public static readonly int[] CookGardenLevelUpCost = [0, 100, 180, 220, 250, 9999];
        public static readonly int[] CookGardenBaseHarvest = [20, 20, 30, 40, 40];

        public static readonly Dictionary<int, int[]> FiveStatusLimit = new Dictionary<int, int[]>
        {
            { 6, [ 2000, 2000, 1800, 1800, 1400] },
            { 7, [ 2200, 1800, 1800, 1800, 1400] },
            { 8, [ 2300, 1000, 2200, 2200, 1500] },
            { 9, [ 2300, 2200, 1800, 1400, 1400] }
        };
    }

    public static class ScoreUtils
    {
        public static double ScoreOfVital(int vital, int maxVital)
        {
            //四段折线
            if (vital <= 50)
                return 2.5 * vital;
            else if (vital <= 75)
                return 1.7 * (vital - 50) + ScoreOfVital(50, maxVital);
            else if (vital <= maxVital - 10)
                return 1.2 * (vital - 75) + ScoreOfVital(75, maxVital);
            else
                return 0.7 * (vital - (maxVital - 10)) + ScoreOfVital(maxVital - 10, maxVital);
        }
        public static int ReviseOver1200(int x)
        {
            return x > 1200 ? x * 2 - 1200 : x;
        }

    }
}
