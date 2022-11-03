using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Text;
using UmamusumeResponseAnalyzer.Entities;

namespace UmamusumeResponseAnalyzer
{
    public static class Database
    {
        internal static string EVENT_NAME_FILEPATH = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "UmamusumeResponseAnalyzer", "events.br");
        internal static string SUCCESS_EVENT_FILEPATH = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "UmamusumeResponseAnalyzer", "successevents.br");
        internal static string ID_TO_NAME_FILEPATH = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "UmamusumeResponseAnalyzer", "id.br");
        internal static string SKILLS_FILEPATH = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "UmamusumeResponseAnalyzer", "skilldata.br");
        internal static string SUPPORT_ID_SHORTNAME_FILEPATH = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "UmamusumeResponseAnalyzer", "name_cn.br");
        internal static string CLIMAX_ITEM_FILEPATH = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "UmamusumeResponseAnalyzer", "climaxitems.br");
        internal static string TALENT_SKILLS_FILEPATH = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "UmamusumeResponseAnalyzer", "talentskillsets.br");
        internal static string FACTOR_IDS_FILEPATH = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "UmamusumeResponseAnalyzer", "factor_ids.br");
        /// <summary>
        /// index为属性，value为对应属性的评价点
        /// </summary>
        public static List<int> StatusToPoint { get; private set; } = JsonConvert.DeserializeObject<List<int>>("[0,1,1,2,2,3,3,4,4,5,5,6,6,7,7,8,8,9,9,10,10,11,11,12,12,13,13,14,14,15,15,16,16,17,17,18,18,19,19,20,20,21,21,22,22,23,23,24,24,25,25,26,27,28,29,29,30,31,32,33,33,34,35,36,37,37,38,39,40,41,41,42,43,44,45,45,46,47,48,49,49,50,51,52,53,53,54,55,56,57,57,58,59,60,61,61,62,63,64,65,66,67,68,69,70,71,72,73,74,75,76,77,78,79,80,81,82,83,84,85,86,87,88,89,90,91,92,93,94,95,96,97,98,99,100,101,102,103,104,105,106,107,108,109,110,111,112,113,114,115,116,117,118,120,121,122,124,125,126,128,129,130,131,133,134,135,137,138,139,141,142,143,144,146,147,148,150,151,152,154,155,156,157,159,160,161,163,164,165,167,168,169,170,172,173,174,176,177,178,180,181,183,184,186,188,189,191,192,194,196,197,199,200,202,204,205,207,208,210,212,213,215,216,218,220,221,223,224,226,228,229,231,232,234,236,237,239,240,242,244,245,247,248,250,252,253,255,256,258,260,261,263,265,267,269,270,272,274,276,278,279,281,283,285,287,288,290,292,294,296,297,299,301,303,305,306,308,310,312,314,315,317,319,321,323,324,326,328,330,332,333,335,337,339,341,342,344,346,348,350,352,354,356,358,360,362,364,366,368,371,373,375,377,379,381,383,385,387,389,392,394,396,398,400,402,404,406,408,410,413,415,417,419,412,423,425,427,429,431,434,436,438,440,442,444,446,448,450,452,455,457,459,462,464,467,469,471,474,476,479,481,483,486,488,491,493,495,498,500,503,505,507,510,512,515,517,519,522,524,527,529,531,534,536,539,541,543,546,548,551,553,555,558,560,563,565,567,570,572,575,577,580,582,585,588,590,593,595,598,601,603,606,608,611,614,616,619,621,624,627,629,632,634,637,640,642,645,647,650,653,655,658,660,663,666,668,671,673,676,679,681,684,686,689,692,694,697,699,702,705,707,710,713,716,719,721,724,727,730,733,735,738,741,744,747,749,752,755,758,761,763,766,769,772,775,777,780,783,786,789,791,794,797,800,803,805,808,811,814,817,819,822,825,828,831,833,836,839,842,845,847,850,853,856,859,862,865,868,871,874,876,879,882,885,888,891,894,897,900,903,905,908,911,914,917,920,923,926,929,931,934,937,940,943,946,949,952,955,958,961,963,966,969,972,975,978,981,984,987,990,993,996,999,1002,1005,1008,1011,1014,1017,1020,1023,1026,1029,1032,1035,1038,1041,1044,1047,1050,1053,1056,1059,1062,1065,1068,1071,1074,1077,1080,1083,1086,1089,1092,1095,1098,1101,1104,1107,1110,1113,1116,1119,1122,1125,1128,1131,1134,1137,1140,1143,1146,1149,1152,1155,1158,1161,1164,1167,1171,1174,1177,1180,1183,1186,1189,1192,1195,1198,1202,1205,1208,1211,1214,1217,1220,1223,1226,1229,1233,1236,1239,1242,1245,1248,1251,1254,1257,1260,1264,1267,1270,1273,1276,1279,1282,1285,1288,1291,1295,1298,1301,1304,1308,1311,1314,1318,1321,1324,1328,1331,1334,1337,1341,1344,1347,1351,1354,1357,1361,1364,1367,1370,1374,1377,1380,1384,1387,1390,1394,1397,1400,1403,1407,1410,1413,1417,1420,1423,1427,1430,1433,1436,1440,1443,1446,1450,1453,1456,1460,1463,1466,1470,1473,1477,1480,1483,1487,1490,1494,1497,1500,1504,1507,1511,1514,1517,1521,1524,1528,1531,1534,1538,1541,1545,1548,1551,1555,1558,1562,1565,1568,1572,1575,1579,1582,1585,1589,1592,1596,1596,1602,1606,1609,1613,1616,1619,1623,1626,1630,1633,1637,1640,1644,1647,1651,1654,1658,1661,1665,1668,1672,1675,1679,1682,1686,1689,1693,1696,1700,1703,1707,1710,1714,1717,1721,1724,1728,1731,1735,1738,1742,1745,1749,1752,1756,1759,1763,1766,1770,1773,1777,1780,1784,1787,1791,1794,1798,1801,1805,1808,1812,1816,1820,1824,1828,1832,1836,1840,1844,1847,1851,1855,1859,1863,1867,1871,1875,1879,1883,1886,1890,1894,1898,1902,1906,1910,1914,1918,1922,1925,1929,1933,1937,1941,1945,1949,1953,1957,1961,1964,1968,1972,1976,1980,1984,1988,1992,1996,2000,2004,2008,2012,2016,2020,2024,2028,2032,2036,2041,2045,2049,2053,2057,2061,2065,2069,2073,2077,2082,2086,2090,2094,2098,2102,2106,2110,2114,2118,2123,2127,2131,2135,2139,2143,2147,2151,2155,2159,2164,2168,2172,2176,2180,2184,2188,2192,2196,2200,2205,2209,2213,2217,2221,2226,2230,2234,2238,2242,2247,2251,2255,2259,2263,2268,2272,2276,2280,2284,2289,2293,2297,2301,2305,2310,2314,2318,2322,2326,2331,2335,2339,2343,2347,2352,2356,2360,2364,2368,2373,2377,2381,2385,2389,2394,2398,2402,2406,2410,2415,2419,2423,2427,2432,2436,2440,2445,2449,2453,2458,2462,2466,2470,2475,2479,2483,2488,2492,2496,2501,2505,2509,2513,2518,2522,2526,2531,2535,2539,2544,2548,2552,2556,2561,2565,2569,2574,2578,2582,2587,2591,2595,2599,2604,2608,2612,2617,2621,2625,2630,2635,2640,2645,2650,2656,2661,2666,2671,2676,2682,2687,2692,2697,2702,2708,2713,2718,2723,2728,2734,2739,2744,2749,2754,2760,2765,2770,2775,2780,2786,2791,2796,2801,2806,2812,2817,2822,2827,2832,2838,2843,2848,2853,2858,2864,2869,2874,2879,2884,2890,2895,2901,2906,2912,2917,2923,2928,2934,2939,2945,2950,2956,2961,2967,2972,2978,2983,2989,2994,3000,3005,3011,3016,3022,3027,3033,3038,3044,3049,3055,3060,3066,3071,3077,3082,3088,3093,3099,3104,3110,3115,3121,3126,3132,3137,3143,3148,3154,3159,3165,3171,3178,3184,3191,3198,3204,3211,3217,3224,3231,3237,3244,3250,3257,3264,3283,3277,3283,3290,3297,3303,3310,3316,3323,3330,3336,3343,3349,3356,3363,3369,3376,3382,3389,3396,3402,3409,3415,3422,3429,3435,3442,3448,3455,3462,3468,3475,3481,3488,3495,3501,3508,3515,3522,3529,3535,3542,3549,3556,3563,3569,3576,3583,3590,3597,3603,3610,3617,3624,3631,3637,3644,3651,3658,3665,3671,3678,3685,3692,3699,3705,3712,3719,3726,3733,3739,3746,3753,3760,3767,3773,3780,3787,3794,3801,3807,3814,3821,3828,3835,3841]")!;
        /// <summary>
        /// 不同等级的马所需的评价点
        /// </summary>
        public static List<GradeRank> GradeToRank { get; private set; } = JsonConvert.DeserializeObject<List<GradeRank>>("[{\"id\":1,\"min_value\":0,\"max_value\":299},{\"id\":2,\"min_value\":300,\"max_value\":599},{\"id\":3,\"min_value\":600,\"max_value\":899},{\"id\":4,\"min_value\":900,\"max_value\":1299},{\"id\":5,\"min_value\":1300,\"max_value\":1799},{\"id\":6,\"min_value\":1800,\"max_value\":2299},{\"id\":7,\"min_value\":2300,\"max_value\":2899},{\"id\":8,\"min_value\":2900,\"max_value\":3499},{\"id\":9,\"min_value\":3500,\"max_value\":4899},{\"id\":10,\"min_value\":4900,\"max_value\":6499},{\"id\":11,\"min_value\":6500,\"max_value\":8199},{\"id\":12,\"min_value\":8200,\"max_value\":9999},{\"id\":13,\"min_value\":10000,\"max_value\":12099},{\"id\":14,\"min_value\":12100,\"max_value\":14499},{\"id\":15,\"min_value\":14500,\"max_value\":15899},{\"id\":16,\"min_value\":15900,\"max_value\":17499},{\"id\":17,\"min_value\":17500,\"max_value\":19199},{\"id\":18,\"min_value\":19200,\"max_value\":19599},{\"id\":19,\"min_value\":19600,\"max_value\":19999},{\"id\":20,\"min_value\":20000,\"max_value\":20399},{\"id\":21,\"min_value\":20400,\"max_value\":20799},{\"id\":22,\"min_value\":20800,\"max_value\":21199},{\"id\":23,\"min_value\":21200,\"max_value\":21599},{\"id\":24,\"min_value\":21600,\"max_value\":22099},{\"id\":25,\"min_value\":22100,\"max_value\":22499},{\"id\":26,\"min_value\":22500,\"max_value\":22999},{\"id\":27,\"min_value\":23000,\"max_value\":23399},{\"id\":28,\"min_value\":23400,\"max_value\":23899},{\"id\":29,\"min_value\":23900,\"max_value\":24299},{\"id\":30,\"min_value\":24300,\"max_value\":24799},{\"id\":31,\"min_value\":24800,\"max_value\":25299},{\"id\":32,\"min_value\":25300,\"max_value\":25799},{\"id\":33,\"min_value\":25800,\"max_value\":26299},{\"id\":34,\"min_value\":26300,\"max_value\":26799},{\"id\":35,\"min_value\":26800,\"max_value\":27299},{\"id\":36,\"min_value\":27300,\"max_value\":27799},{\"id\":37,\"min_value\":27800,\"max_value\":28299},{\"id\":38,\"min_value\":28300,\"max_value\":28799},{\"id\":39,\"min_value\":28800,\"max_value\":29399},{\"id\":40,\"min_value\":29400,\"max_value\":29899},{\"id\":41,\"min_value\":29900,\"max_value\":30399},{\"id\":42,\"min_value\":30400,\"max_value\":30999},{\"id\":43,\"min_value\":31000,\"max_value\":31499},{\"id\":44,\"min_value\":31500,\"max_value\":32099},{\"id\":45,\"min_value\":32100,\"max_value\":32699},{\"id\":46,\"min_value\":32700,\"max_value\":33199},{\"id\":47,\"min_value\":33200,\"max_value\":33799},{\"id\":48,\"min_value\":33800,\"max_value\":34399},{\"id\":49,\"min_value\":34400,\"max_value\":34999},{\"id\":50,\"min_value\":35000,\"max_value\":35599},{\"id\":51,\"min_value\":35600,\"max_value\":36199},{\"id\":52,\"min_value\":36200,\"max_value\":36799},{\"id\":53,\"min_value\":36800,\"max_value\":37499},{\"id\":54,\"min_value\":37500,\"max_value\":38099},{\"id\":55,\"min_value\":38100,\"max_value\":38699},{\"id\":56,\"min_value\":38700,\"max_value\":39399},{\"id\":57,\"min_value\":39400,\"max_value\":39999},{\"id\":58,\"min_value\":40000,\"max_value\":40699},{\"id\":59,\"min_value\":40700,\"max_value\":41299},{\"id\":60,\"min_value\":41300,\"max_value\":41999},{\"id\":61,\"min_value\":42000,\"max_value\":42699},{\"id\":62,\"min_value\":42700,\"max_value\":43399},{\"id\":63,\"min_value\":43400,\"max_value\":43999},{\"id\":64,\"min_value\":44000,\"max_value\":44699},{\"id\":65,\"min_value\":44700,\"max_value\":45399},{\"id\":66,\"min_value\":45400,\"max_value\":46199},{\"id\":67,\"min_value\":46200,\"max_value\":46899},{\"id\":68,\"min_value\":46900,\"max_value\":47599},{\"id\":69,\"min_value\":47600,\"max_value\":48299},{\"id\":70,\"min_value\":48300,\"max_value\":48999},{\"id\":71,\"min_value\":49000,\"max_value\":49799},{\"id\":72,\"min_value\":49800,\"max_value\":50499},{\"id\":73,\"min_value\":50500,\"max_value\":51299},{\"id\":74,\"min_value\":51300,\"max_value\":51999},{\"id\":75,\"min_value\":52000,\"max_value\":52799},{\"id\":76,\"min_value\":52800,\"max_value\":53599},{\"id\":77,\"min_value\":53600,\"max_value\":54399},{\"id\":78,\"min_value\":54400,\"max_value\":55199},{\"id\":79,\"min_value\":55200,\"max_value\":55899},{\"id\":80,\"min_value\":55900,\"max_value\":56699},{\"id\":81,\"min_value\":56700,\"max_value\":57499},{\"id\":82,\"min_value\":57500,\"max_value\":58399},{\"id\":83,\"min_value\":58400,\"max_value\":59199},{\"id\":84,\"min_value\":59200,\"max_value\":59999},{\"id\":85,\"min_value\":60000,\"max_value\":60799},{\"id\":86,\"min_value\":60800,\"max_value\":61699},{\"id\":87,\"min_value\":61700,\"max_value\":62499},{\"id\":88,\"min_value\":62500,\"max_value\":63399},{\"id\":89,\"min_value\":63400,\"max_value\":64199},{\"id\":90,\"min_value\":64200,\"max_value\":65099},{\"id\":91,\"min_value\":65100,\"max_value\":65999},{\"id\":92,\"min_value\":66000,\"max_value\":66799},{\"id\":93,\"min_value\":66800,\"max_value\":67699},{\"id\":94,\"min_value\":67700,\"max_value\":68599},{\"id\":95,\"min_value\":68600,\"max_value\":69499},{\"id\":96,\"min_value\":69500,\"max_value\":70399},{\"id\":97,\"min_value\":70400,\"max_value\":71399},{\"id\":98,\"min_value\":71400,\"max_value\":99999}]")!;
        /// <summary>
        /// 技能
        /// </summary>
        public static SkillManager Skills { get; private set; } = null!;
        /// <summary>
        /// 育成事件
        /// </summary>
        public static Dictionary<int, Story> Events { get; set; } = new();
        /// <summary>
        /// 需要手动记录的成功育成事件
        /// </summary>
        public static Dictionary<int, SuccessStory> SuccessEvent { get; set; } = new();
        /// <summary>
        /// 马娘ID到马娘全名（包括前缀）的Dictionary
        /// </summary>
        public static NullableIdNameDictionary IdToName { get; set; } = new();
        /// <summary>
        /// S卡ID到马娘名的Dictionary，用于显示训练时的人头信息
        /// </summary>
        public static NullableIdNameDictionary SupportIdToShortName { get; set; } = new();
        /// <summary>
        /// 巅峰杯道具的ID及其对应名称
        /// </summary>
        public static NullableIdNameDictionary ClimaxItem { get; set; } = new();
        /// <summary>
        /// 马娘的天赋技能,Key是CardId
        /// </summary>
        public static Dictionary<int, TalentSkillData[]> TalentSkill { get; set; } = new();
        /// <summary>
        /// 
        /// </summary>
        public static NullableIdNameDictionary FactorIds { get; set; }
        public static void Initialize()
        {
            Directory.CreateDirectory(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "UmamusumeResponseAnalyzer"));
            if (File.Exists(EVENT_NAME_FILEPATH))
            {
                var events = JArray.Parse(Load(EVENT_NAME_FILEPATH)).ToObject<List<Story>>()?.ToDictionary(x => x.Id, x => x);
                if (events != default)
                    Events = events;
            }
            if (File.Exists(SUCCESS_EVENT_FILEPATH))
            {
                var successEvent = JArray.Parse(Load(SUCCESS_EVENT_FILEPATH)).ToObject<List<SuccessStory>>()?.ToDictionary(x => x.Id, x => x);
                if (successEvent != default)
                    SuccessEvent = successEvent;
            }
            if (File.Exists(ID_TO_NAME_FILEPATH))
            {
                var idToName = JsonConvert.DeserializeObject<NullableIdNameDictionary>(Load(ID_TO_NAME_FILEPATH));
                if (idToName != default)
                    IdToName = idToName;
            }
            if (File.Exists(SKILLS_FILEPATH))
            {
                var skills = JsonConvert.DeserializeObject<List<SkillData>>(Load(SKILLS_FILEPATH));
                if (skills != default)
                {
                    Skills = new SkillManager(skills);
                }
            }
            if (File.Exists(SUPPORT_ID_SHORTNAME_FILEPATH))
            {
                var supportIdToShortName = JsonConvert.DeserializeObject<NullableIdNameDictionary>(Load(SUPPORT_ID_SHORTNAME_FILEPATH));
                if (supportIdToShortName != default)
                    SupportIdToShortName = supportIdToShortName;
            }
            if (File.Exists(CLIMAX_ITEM_FILEPATH))
            {
                var climaxItem = JsonConvert.DeserializeObject<NullableIdNameDictionary>(Load(CLIMAX_ITEM_FILEPATH));
                if (climaxItem != default)
                    ClimaxItem = climaxItem;
            }
            if (File.Exists(TALENT_SKILLS_FILEPATH))
            {
                var talentSkill = JsonConvert.DeserializeObject<Dictionary<int, TalentSkillData[]>>(Load(TALENT_SKILLS_FILEPATH));
                if (talentSkill != default)
                    TalentSkill = talentSkill;
            }
            if (File.Exists(FACTOR_IDS_FILEPATH))
            {
                var factor_ids = JsonConvert.DeserializeObject<NullableIdNameDictionary>(Load(FACTOR_IDS_FILEPATH));
                if (factor_ids != default)
                    FactorIds = factor_ids;
            }
        }
        static string Load(string path) => Encoding.UTF8.GetString(Brotli.Decompress(File.ReadAllBytes(path)));
    }

    public class NullableIdNameDictionary : Dictionary<int, string>
    {
        public new string this[int key]
        {
            get => ContainsKey(key) ? base[key] : "未知";
            set => base[key] = value;
        }
    }
}