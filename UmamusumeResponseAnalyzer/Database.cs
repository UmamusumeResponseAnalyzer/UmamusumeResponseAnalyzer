using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Spectre.Console;
using System.Collections.Frozen;
using System.Text;
using System.Text.RegularExpressions;
using UmamusumeResponseAnalyzer.Entities;
using static UmamusumeResponseAnalyzer.Localization.Database;

namespace UmamusumeResponseAnalyzer
{
    public static partial class Database
    {
        #region Paths
        internal static string EVENT_NAME_FILEPATH = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "UmamusumeResponseAnalyzer", "events.br");
        internal static string SUCCESS_EVENT_FILEPATH = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "UmamusumeResponseAnalyzer", "success_events.br");
        internal static string NAMES_FILEPATH = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "UmamusumeResponseAnalyzer", "names.br");
        internal static string SKILLS_FILEPATH = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "UmamusumeResponseAnalyzer", "skill_data.br");
        internal static string TALENT_SKILLS_FILEPATH = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "UmamusumeResponseAnalyzer", "talent_skill_sets.br");
        internal static string FACTOR_IDS_FILEPATH = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "UmamusumeResponseAnalyzer", "factor_ids.br");
        internal static string SKILL_UPGRADE_SPECIALITY_FILEPATH = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "UmamusumeResponseAnalyzer", "skill_upgrade_speciality.br");
        internal static string LOCALIZED_DATA_FILEPATH = Config.Get<string>(Localization.Config.I18N_LocalizedDataPath) ?? string.Empty;
        #endregion
        #region Properties
        /// <summary>
        /// index为属性，value为对应属性的评价点
        /// </summary>
        public static List<int> StatusToPoint { get; private set; } = JsonConvert.DeserializeObject<List<int>>("[0,1,1,2,2,3,3,4,4,5,5,6,6,7,7,8,8,9,9,10,10,11,11,12,12,13,13,14,14,15,15,16,16,17,17,18,18,19,19,20,20,21,21,22,22,23,23,24,24,25,25,26,27,28,29,29,30,31,32,33,33,34,35,36,37,37,38,39,40,41,41,42,43,44,45,45,46,47,48,49,49,50,51,52,53,53,54,55,56,57,57,58,59,60,61,61,62,63,64,65,66,67,68,69,70,71,72,73,74,75,76,77,78,79,80,81,82,83,84,85,86,87,88,89,90,91,92,93,94,95,96,97,98,99,100,101,102,103,104,105,106,107,108,109,110,111,112,113,114,115,116,117,118,120,121,122,124,125,126,128,129,130,131,133,134,135,137,138,139,141,142,143,144,146,147,148,150,151,152,154,155,156,157,159,160,161,163,164,165,167,168,169,170,172,173,174,176,177,178,180,181,183,184,186,188,189,191,192,194,196,197,199,200,202,204,205,207,208,210,212,213,215,216,218,220,221,223,224,226,228,229,231,232,234,236,237,239,240,242,244,245,247,248,250,252,253,255,256,258,260,261,263,265,267,269,270,272,274,276,278,279,281,283,285,287,288,290,292,294,296,297,299,301,303,305,306,308,310,312,314,315,317,319,321,323,324,326,328,330,332,333,335,337,339,341,342,344,346,348,350,352,354,356,358,360,362,364,366,368,371,373,375,377,379,381,383,385,387,389,392,394,396,398,400,402,404,406,408,410,413,415,417,419,421,423,425,427,429,431,434,436,438,440,442,444,446,448,450,452,455,457,459,462,464,467,469,471,474,476,479,481,483,486,488,491,493,495,498,500,503,505,507,510,512,515,517,519,522,524,527,529,531,534,536,539,541,543,546,548,551,553,555,558,560,563,565,567,570,572,575,577,580,582,585,588,590,593,595,598,601,603,606,608,611,614,616,619,621,624,627,629,632,634,637,640,642,645,647,650,653,655,658,660,663,666,668,671,673,676,679,681,684,686,689,692,694,697,699,702,705,707,710,713,716,719,721,724,727,730,733,735,738,741,744,747,749,752,755,758,761,763,766,769,772,775,777,780,783,786,789,791,794,797,800,803,805,808,811,814,817,819,822,825,828,831,833,836,839,842,845,847,850,853,856,859,862,865,868,871,874,876,879,882,885,888,891,894,897,900,903,905,908,911,914,917,920,923,926,929,932,934,937,940,943,946,949,952,955,958,961,963,966,969,972,975,978,981,984,987,990,993,996,999,1002,1005,1008,1011,1014,1017,1020,1023,1026,1029,1032,1035,1038,1041,1044,1047,1050,1053,1056,1059,1062,1065,1068,1071,1074,1077,1080,1083,1086,1089,1092,1095,1098,1101,1104,1107,1110,1113,1116,1119,1122,1125,1128,1131,1134,1137,1140,1143,1146,1149,1152,1155,1158,1161,1164,1167,1171,1174,1177,1180,1183,1186,1189,1192,1195,1198,1202,1205,1208,1211,1214,1217,1220,1223,1226,1229,1233,1236,1239,1242,1245,1248,1251,1254,1257,1260,1264,1267,1270,1273,1276,1279,1282,1285,1288,1291,1295,1298,1301,1304,1308,1311,1314,1318,1321,1324,1328,1331,1334,1337,1341,1344,1347,1351,1354,1357,1361,1364,1367,1370,1374,1377,1380,1384,1387,1390,1394,1397,1400,1403,1407,1410,1413,1417,1420,1423,1427,1430,1433,1436,1440,1443,1446,1450,1453,1456,1460,1463,1466,1470,1473,1477,1480,1483,1487,1490,1494,1497,1500,1504,1507,1511,1514,1517,1521,1524,1528,1531,1534,1538,1541,1545,1548,1551,1555,1558,1562,1565,1568,1572,1575,1579,1582,1585,1589,1592,1596,1599,1602,1606,1609,1613,1616,1619,1623,1626,1630,1633,1637,1640,1644,1647,1651,1654,1658,1661,1665,1668,1672,1675,1679,1682,1686,1689,1693,1696,1700,1703,1707,1710,1714,1717,1721,1724,1728,1731,1735,1738,1742,1745,1749,1752,1756,1759,1763,1766,1770,1773,1777,1780,1784,1787,1791,1794,1798,1801,1805,1808,1812,1816,1820,1824,1828,1832,1836,1840,1844,1847,1851,1855,1859,1863,1867,1871,1875,1879,1883,1886,1890,1894,1898,1902,1906,1910,1914,1918,1922,1925,1929,1933,1937,1941,1945,1949,1953,1957,1961,1964,1968,1972,1976,1980,1984,1988,1992,1996,2000,2004,2008,2012,2016,2020,2024,2028,2032,2036,2041,2045,2049,2053,2057,2061,2065,2069,2073,2077,2082,2086,2090,2094,2098,2102,2106,2110,2114,2118,2123,2127,2131,2135,2139,2143,2147,2151,2155,2159,2164,2168,2172,2176,2180,2184,2188,2192,2196,2200,2205,2209,2213,2217,2221,2226,2230,2234,2238,2242,2247,2251,2255,2259,2263,2268,2272,2276,2280,2284,2289,2293,2297,2301,2305,2310,2314,2318,2322,2326,2331,2335,2339,2343,2347,2352,2356,2360,2364,2368,2373,2377,2381,2385,2389,2394,2398,2402,2406,2410,2415,2419,2423,2427,2432,2436,2440,2445,2449,2453,2458,2462,2466,2470,2475,2479,2483,2488,2492,2496,2501,2505,2509,2513,2518,2522,2526,2531,2535,2539,2544,2548,2552,2556,2561,2565,2569,2574,2578,2582,2587,2591,2595,2599,2604,2608,2612,2617,2621,2625,2630,2635,2640,2645,2650,2656,2661,2666,2671,2676,2682,2687,2692,2697,2702,2708,2713,2718,2723,2728,2734,2739,2744,2749,2754,2760,2765,2770,2775,2780,2786,2791,2796,2801,2806,2812,2817,2822,2827,2832,2838,2843,2848,2853,2858,2864,2869,2874,2879,2884,2890,2895,2901,2906,2912,2917,2923,2928,2934,2939,2945,2950,2956,2961,2967,2972,2978,2983,2989,2994,3000,3005,3011,3016,3022,3027,3033,3038,3044,3049,3055,3060,3066,3071,3077,3082,3088,3093,3099,3104,3110,3115,3121,3126,3132,3137,3143,3148,3154,3159,3165,3171,3178,3184,3191,3198,3204,3211,3217,3224,3231,3237,3244,3250,3257,3264,3270,3277,3283,3290,3297,3303,3310,3316,3323,3330,3336,3343,3349,3356,3363,3369,3376,3382,3389,3396,3402,3409,3415,3422,3429,3435,3442,3448,3455,3462,3468,3475,3481,3488,3495,3501,3508,3515,3522,3529,3535,3542,3549,3556,3563,3569,3576,3583,3590,3597,3603,3610,3617,3624,3631,3637,3644,3651,3658,3665,3671,3678,3685,3692,3699,3705,3712,3719,3726,3733,3739,3746,3753,3760,3767,3773,3780,3787,3794,3801,3807,3814,3821,3828,3835,3841,3849,3857,3865,3873,3881,3889,3897,3905,3912,3920,3928,3936,3944,3952,3960,3968,3976,3984,3992,4001,4009,4017,4025,4033,4041,4049,4057,4065,4073,4082,4090,4098,4107,4115,4123,4132,4140,4148,4156,4165,4173,4182,4190,4198,4207,4215,4224,4232,4240,4249,4257,4266,4274,4283,4291,4300,4308,4317,4325,4334,4343,4351,4360,4368,4377,4386,4394,4403,4411,4420,4429,4438,4447,4455,4464,4473,4482,4491,4499,4508,4517,4526,4535,4544,4553,4562,4571,4580,4588,4597,4606,4615,4624,4633,4642,4651,4660,4669,4678,4688,4697,4706,4715,4724,4734,4743,4752,4761,4770,4780,4789,4798,4808,4817,4826,4836,4845,4854,4863,4873,4882,4892,4901,4910,4920,4929,4939,4948,4957,4967,4977,4986,4996,5005,5015,5025,5034,5044,5053,5063,5073,5083,5092,5102,5112,5121,5131,5141,5150,5160,5170,5180,5190,5199,5209,5219,5229,5239,5248,5258,5268,5278,5288,5298,5308,5318,5328,5338,5348,5359,5369,5379,5389,5399,5409,5419,5429,5439,5449,5460,5470,5480,5490,5500,5511,5521,5531,5541,5551,5562,5572,5582,5593,5603,5613,5624,5634,5644,5654,5665,5675,5686,5696,5707,5717,5728,5738,5749,5759,5770,5781,5791,5802,5812,5823,5834,5844,5855,5865,5876,5887,5898,5908,5919,5930,5940,5951,5962,5972,5983,5994,6005,6016,6027,6038,6049,6060,6071,6081,6092,6103,6114,6125,6136,6147,6158,6169,6180,6191,6203,6214,6225,6236,6247,6258,6269,6280,6291,6302,6314,6325,6336,6348,6359,6370,6382,6393,6404,6415,6427,6438,6450,6461,6472,6484,6495,6507,6518,6529,6541,6552,6564,6575,6587,6598,6610,6621,6633,6644,6656,6668,6680,6691,6703,6715,6726,6738,6750,6761,6773,6785,6797,6809,6820,6832,6844,6856,6868,6879,6891,6903,6915,6927,6939,6951,6963,6975,6987,6998,7011,7023,7035,7047,7059,7071,7083,7095,7107,7119,7132,7144,7156,7168,7180,7193,7205,7217,7229,7241,7254,7266,7278,7291,7303,7315,7328,7340,7352,7364,7377,7389,7402,7414,7426,7439,7451,7464,7476,7488,7501,7514,7526,7539,7551,7564,7577,7589,7602,7614,7627,7640,7653,7665,7678,7691,7703,7716,7729,7741,7754,7767,7780,7793,7805,7818,7831,7844,7857,7869,7882,7895,7908,7921,7934,7947,7960,7973,7986,7999,8013,8026,8039,8052,8065,8078,8091,8104,8117,8130,8144,8157,8170,8183,8196,8210,8223,8236,8249,8262,8276,8289,8303,8316,8329,8343,8356,8370,8383,8396,8410,8423,8437,8450,8464,8477,8491,8504,8518,8531,8545,8559,8572,8586,8599,8613,8627,8640,8654,8667,8681,8695,8709,8723,8736,8750,8764,8778,8792,8805,8819,8833,8847,8861,8875,8889,8903,8917,8931,8944,8958,8972,8986,9000,9014,9028,9042,9056,9070,9084,9099,9113,9127,9141,9155,9169,9183,9197,9211,9225,9240,9254,9268,9283,9297,9311,9326,9340,9354,9368,9383,9397,9412,9426,9440,9455,9469,9484,9498,9512,9527,9541,9556,9570,9585,9599,9614,9628,9643,9657,9672,9687,9702,9716,9731,9746,9760,9775,9790,9804,9819,9834,9849,9864,9878,9893,9908,9923,9938,9952,9967,9982,9997,10012,10027,10042,10057,10072,10087,10101,10117,10132,10147,10162,10177,10192,10207,10222,10237,10252,10268,10283,10298,10313,10328,10344,10359,10374,10389,10404,10420,10435,10450,10466,10481,10496,10512,10527,10542,10557,10573,10588,10604,10619,10635,10650,10666,10681,10697,10712,10728,10744,10759,10775,10790,10806,10822,10837,10853,10868,10884,10900,10916,10931,10947,10963,10978,10994,11010,11025,11041,11057,11073,11089,11105,11121,11137,11153,11169,11184,11200,11216,11232,11248,11264,11280,11296,11312,11328,11344,11361,11377,11393,11409,11425,11441,11457,11473,11489,11505,11522,11538,11554,11570,11586,11603,11619,11635,11651,11667,11684,11700,11717,11733,11749,11766,11782,11799,11815,11831,11848,11864,11881,11897,11914,11930,11947,11963,11980,11996,12013,12030,12046,12063,12079,12096,12113,12129,12146,12162,12179,12196,12213,12230,12246,12263,12280,12297,12314,12330,12347,12364,12381,12398,12415,12432,12449,12466,12483,12499,12516,12533,12550,12567,12584,12601,12618,12635,12652,12669,12687,12704,12721,12738,12755,12773,12790,12807,12824,12841,12859,12876,12893,12911,12928,12945,12963,12980,12997,13014,13032,13049,13067,13084,13101,13119,13136,13154,13171,13188,13206,13224,13241,13259,13276,13294,13312,13329,13347,13364,13382,13400,13418,13435,13453,13471,13488,13506,13524,13541,13559,13577,13595,13613,13630,13648,13666,13684,13702,13719,13737,13755,13773,13791,13809,13827,13845,13863,13881,13898,13917,13935,13953,13971,13989,14007,14025,14043,14061,14079,14098,14116,14134,14152,14170,14189,14207,14225,14243,14261,14280]")!;
        /// <summary>
        /// 不同等级的马所需的评价点
        /// </summary>
        public static List<GradeRank> GradeToRank { get; private set; } = JsonConvert.DeserializeObject<List<GradeRank>>("[{\"id\":1,\"min_value\":0,\"max_value\":299},{\"id\":2,\"min_value\":300,\"max_value\":599},{\"id\":3,\"min_value\":600,\"max_value\":899},{\"id\":4,\"min_value\":900,\"max_value\":1299},{\"id\":5,\"min_value\":1300,\"max_value\":1799},{\"id\":6,\"min_value\":1800,\"max_value\":2299},{\"id\":7,\"min_value\":2300,\"max_value\":2899},{\"id\":8,\"min_value\":2900,\"max_value\":3499},{\"id\":9,\"min_value\":3500,\"max_value\":4899},{\"id\":10,\"min_value\":4900,\"max_value\":6499},{\"id\":11,\"min_value\":6500,\"max_value\":8199},{\"id\":12,\"min_value\":8200,\"max_value\":9999},{\"id\":13,\"min_value\":10000,\"max_value\":12099},{\"id\":14,\"min_value\":12100,\"max_value\":14499},{\"id\":15,\"min_value\":14500,\"max_value\":15899},{\"id\":16,\"min_value\":15900,\"max_value\":17499},{\"id\":17,\"min_value\":17500,\"max_value\":19199},{\"id\":18,\"min_value\":19200,\"max_value\":19599},{\"id\":19,\"min_value\":19600,\"max_value\":19999},{\"id\":20,\"min_value\":20000,\"max_value\":20399},{\"id\":21,\"min_value\":20400,\"max_value\":20799},{\"id\":22,\"min_value\":20800,\"max_value\":21199},{\"id\":23,\"min_value\":21200,\"max_value\":21599},{\"id\":24,\"min_value\":21600,\"max_value\":22099},{\"id\":25,\"min_value\":22100,\"max_value\":22499},{\"id\":26,\"min_value\":22500,\"max_value\":22999},{\"id\":27,\"min_value\":23000,\"max_value\":23399},{\"id\":28,\"min_value\":23400,\"max_value\":23899},{\"id\":29,\"min_value\":23900,\"max_value\":24299},{\"id\":30,\"min_value\":24300,\"max_value\":24799},{\"id\":31,\"min_value\":24800,\"max_value\":25299},{\"id\":32,\"min_value\":25300,\"max_value\":25799},{\"id\":33,\"min_value\":25800,\"max_value\":26299},{\"id\":34,\"min_value\":26300,\"max_value\":26799},{\"id\":35,\"min_value\":26800,\"max_value\":27299},{\"id\":36,\"min_value\":27300,\"max_value\":27799},{\"id\":37,\"min_value\":27800,\"max_value\":28299},{\"id\":38,\"min_value\":28300,\"max_value\":28799},{\"id\":39,\"min_value\":28800,\"max_value\":29399},{\"id\":40,\"min_value\":29400,\"max_value\":29899},{\"id\":41,\"min_value\":29900,\"max_value\":30399},{\"id\":42,\"min_value\":30400,\"max_value\":30999},{\"id\":43,\"min_value\":31000,\"max_value\":31499},{\"id\":44,\"min_value\":31500,\"max_value\":32099},{\"id\":45,\"min_value\":32100,\"max_value\":32699},{\"id\":46,\"min_value\":32700,\"max_value\":33199},{\"id\":47,\"min_value\":33200,\"max_value\":33799},{\"id\":48,\"min_value\":33800,\"max_value\":34399},{\"id\":49,\"min_value\":34400,\"max_value\":34999},{\"id\":50,\"min_value\":35000,\"max_value\":35599},{\"id\":51,\"min_value\":35600,\"max_value\":36199},{\"id\":52,\"min_value\":36200,\"max_value\":36799},{\"id\":53,\"min_value\":36800,\"max_value\":37499},{\"id\":54,\"min_value\":37500,\"max_value\":38099},{\"id\":55,\"min_value\":38100,\"max_value\":38699},{\"id\":56,\"min_value\":38700,\"max_value\":39399},{\"id\":57,\"min_value\":39400,\"max_value\":39999},{\"id\":58,\"min_value\":40000,\"max_value\":40699},{\"id\":59,\"min_value\":40700,\"max_value\":41299},{\"id\":60,\"min_value\":41300,\"max_value\":41999},{\"id\":61,\"min_value\":42000,\"max_value\":42699},{\"id\":62,\"min_value\":42700,\"max_value\":43399},{\"id\":63,\"min_value\":43400,\"max_value\":43999},{\"id\":64,\"min_value\":44000,\"max_value\":44699},{\"id\":65,\"min_value\":44700,\"max_value\":45399},{\"id\":66,\"min_value\":45400,\"max_value\":46199},{\"id\":67,\"min_value\":46200,\"max_value\":46899},{\"id\":68,\"min_value\":46900,\"max_value\":47599},{\"id\":69,\"min_value\":47600,\"max_value\":48299},{\"id\":70,\"min_value\":48300,\"max_value\":48999},{\"id\":71,\"min_value\":49000,\"max_value\":49799},{\"id\":72,\"min_value\":49800,\"max_value\":50499},{\"id\":73,\"min_value\":50500,\"max_value\":51299},{\"id\":74,\"min_value\":51300,\"max_value\":51999},{\"id\":75,\"min_value\":52000,\"max_value\":52799},{\"id\":76,\"min_value\":52800,\"max_value\":53599},{\"id\":77,\"min_value\":53600,\"max_value\":54399},{\"id\":78,\"min_value\":54400,\"max_value\":55199},{\"id\":79,\"min_value\":55200,\"max_value\":55899},{\"id\":80,\"min_value\":55900,\"max_value\":56699},{\"id\":81,\"min_value\":56700,\"max_value\":57499},{\"id\":82,\"min_value\":57500,\"max_value\":58399},{\"id\":83,\"min_value\":58400,\"max_value\":59199},{\"id\":84,\"min_value\":59200,\"max_value\":59999},{\"id\":85,\"min_value\":60000,\"max_value\":60799},{\"id\":86,\"min_value\":60800,\"max_value\":61699},{\"id\":87,\"min_value\":61700,\"max_value\":62499},{\"id\":88,\"min_value\":62500,\"max_value\":63399},{\"id\":89,\"min_value\":63400,\"max_value\":64199},{\"id\":90,\"min_value\":64200,\"max_value\":65099},{\"id\":91,\"min_value\":65100,\"max_value\":65999},{\"id\":92,\"min_value\":66000,\"max_value\":66799},{\"id\":93,\"min_value\":66800,\"max_value\":67699},{\"id\":94,\"min_value\":67700,\"max_value\":68599},{\"id\":95,\"min_value\":68600,\"max_value\":69499},{\"id\":96,\"min_value\":69500,\"max_value\":70399},{\"id\":97,\"min_value\":70400,\"max_value\":71399},{\"id\":98,\"min_value\":71400,\"max_value\":99999}]")!;
        /// <summary>
        /// 技能
        /// </summary>
        public static SkillManagerGenerator Skills { get; private set; } = new SkillManagerGenerator([]);
        /// <summary>
        /// 剧本限定进化技能
        /// </summary>
        public static FrozenDictionary<int, SkillUpgradeSpeciality> SkillUpgradeSpeciality { get; private set; }
        /// <summary>
        /// 育成事件
        /// </summary>
        public static Dictionary<int, Story> Events { get; set; } = [];
        /// <summary>
        /// 需要手动记录的成功育成事件
        /// </summary>
        public static Dictionary<int, SuccessStory> SuccessEvent { get; set; } = [];
        /// <summary>
        /// 马娘ID到马娘全名（包括前缀）的Dictionary
        /// </summary>
        public static NameManager Names { get; set; } = new NameManager([]);
        /// <summary>
        /// 巅峰杯道具的ID及其对应名称
        /// </summary>
        public static NullableIntStringDictionary ClimaxItem { get; private set; } = new() { { 1001, "速+3" }, { 1002, "耐+3" }, { 1003, "力+3" }, { 1004, "根+3" }, { 1005, "智+3" }, { 1101, "速+7" }, { 1102, "耐+7" }, { 1103, "力+7" }, { 1104, "根+7" }, { 1105, "智+7" }, { 1201, "速+15" }, { 1202, "耐+15" }, { 1203, "力+15" }, { 1204, "根+15" }, { 1205, "智+15" }, { 2001, "体力+20" }, { 2002, "体力+40" }, { 2003, "体力+65" }, { 2101, "苦茶" }, { 2201, "体力上限+4" }, { 2202, "体力上限+8" }, { 2301, "干劲+1" }, { 2302, "干劲+2" }, { 3001, "猫罐头" }, { 3101, "BBQ" }, { 4001, "爱娇" }, { 4002, "注目株" }, { 4003, "练习上手" }, { 4004, "切者" }, { 4101, "解寝不足" }, { 4102, "解摸鱼癖" }, { 4103, "解肌荒" }, { 4104, "解发胖" }, { 4105, "解头痛" }, { 4106, "解练习下手" }, { 4201, "解全DB" }, { 5001, "速请愿书" }, { 5002, "耐请愿书" }, { 5003, "力请愿书" }, { 5004, "根请愿书" }, { 5005, "智请愿书" }, { 7001, "哨子" }, { 8001, "20%喇叭" }, { 8002, "40%喇叭" }, { 8003, "60%喇叭" }, { 9001, "速负重脚环" }, { 9002, "耐负重脚环" }, { 9003, "力负重脚环" }, { 9004, "根负重脚环" }, { 10001, "御守" }, { 11001, "蹄铁・匠" }, { 11002, "蹄铁・極" }, { 11003, "荧光棒" } };
        /// <summary>
        /// 马娘的天赋技能,Key是CardId
        /// </summary>
        public static Dictionary<int, TalentSkillData[]> TalentSkill { get; set; } = [];
        /// <summary>
        /// 
        /// </summary>
        public static NullableIntStringDictionary FactorIds { get; set; }
        /// <summary>
        /// 可获得胜鞍的Id
        /// </summary>
        public static int[] SaddleIds { get; } = [10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21, 22, 23, 24, 25, 26, 27, 28, 29, 30, 31, 32, 33, 34, 35, 36, 37, 38, 39, 147, 148, 153, 155, 156, 157, 158, 159, 160, 161, 162, 163, 164, 165, 166, 167, 168, 184];
        /// <summary>
        /// 胜鞍ID对应的比赛/奖章名
        /// </summary>
        public static Dictionary<int, string> SaddleNames { get; set; } = [];
        #endregion
        public static void Initialize()
        {
            Directory.CreateDirectory(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "UmamusumeResponseAnalyzer"));
            if (TryDeserialize(EVENT_NAME_FILEPATH, out var events, x => x.ToObject<List<Story>>()!.ToDictionary(y => y.Id, y => y)))
            {
                Events = events;
            }
            if (TryDeserialize(SUCCESS_EVENT_FILEPATH, out var successEvent, x => x.ToObject<List<SuccessStory>>()!.ToDictionary(y => y.Id, y => y)))
            {
                SuccessEvent = successEvent;
            }
            // Names需要额外设定TypeNameHandling，所以要单独处理
            if (File.Exists(NAMES_FILEPATH))
            {
                try
                {
                    var names = JsonConvert.DeserializeObject<List<BaseName>>(Load(NAMES_FILEPATH), new JsonSerializerSettings { TypeNameHandling = TypeNameHandling.All })!;
                    Names = new(names);
                }
                catch
                {
                    AnsiConsole.MarkupLine(I18N_LoadFail, Path.GetFileName(NAMES_FILEPATH));
                }
            }
            else
            {
                AnsiConsole.MarkupLine(I18N_NotExist, Path.GetFileName(NAMES_FILEPATH));
            }
            if (TryDeserialize(SKILLS_FILEPATH, out var skills, x => x.ToObject<List<SkillData>>()!))
            {
                Skills = new(skills);
                SkillManagerGenerator.Default = new(skills);
            }
            if (TryDeserialize(SKILL_UPGRADE_SPECIALITY_FILEPATH, out var skillUpgradeSpeciality, x => x.ToObject<List<SkillUpgradeSpeciality>>()!))
            {
                SkillUpgradeSpeciality = skillUpgradeSpeciality.ToDictionary(x => x.BaseSkillId, x => x).ToFrozenDictionary();
            }
            if (TryDeserialize(TALENT_SKILLS_FILEPATH, out var talentSkill, x => x.ToObject<Dictionary<int, TalentSkillData[]>>()!))
            {
                TalentSkill = talentSkill;
            }
            if (TryDeserialize(FACTOR_IDS_FILEPATH, out var factorIds, x => x.ToObject<NullableIntStringDictionary>()!))
            {
                FactorIds = factorIds;
            }
            if (Config.Get(Localization.Config.I18N_LoadLocalizedData) && !string.IsNullOrEmpty(LOCALIZED_DATA_FILEPATH) && File.Exists(LOCALIZED_DATA_FILEPATH))
            {
                var textData = JsonConvert.DeserializeObject<Dictionary<TextDataCategory, Dictionary<int, string>>>(File.ReadAllText(LOCALIZED_DATA_FILEPATH));
                var staticTranslation = JsonConvert.DeserializeObject<Dictionary<string, string>>("{\"ウマ娘の\":\"马娘的\",\"スピード\":\"速度\",\"スタミナ\":\"耐力\",\"パワー\":\"力量\",\"根性\":\"根性\",\"賢さ\":\"智力\",\"マイナススキル\":\"负面寄能\",\"スキル\":\"技能\",\"ヒント\":\"Hint \",\"やる気\":\"干劲\",\"の絆ゲージ\":\"的羁绊\",\"ステータス\":\"属性\",\"ランダムな\":\"随机\",\"つの\":\"项\",\"〜\":\"~\",\"練習上手\":\"擅长练习\",\"愛嬌\":\"惹人怜爱\",\"切れ者\":\"能人（概率获得）\",\"直前のトレーニング能力\":\"之前训练的属性\",\"直前のトレーニングに応じた\":\"之前训练的\",\"太り気味\":\"变胖\",\"練習ベタ\":\"不擅长练习\",\"夜ふかし気味\":\"熬夜\",\"バッドコンディションが治る\":\"治疗负面状态\",\"バッドコンディションが解消\":\"解除部分负面状态\",\"確率で\":\"概率\",\"なまけ癖\":\"摸鱼癖\",\"進行イベント打ち切り\":\"事件中断\",\"アタシに指図しないで！！！\":\"别对我指指点点！\",\"スターゲージ\":\"明星量表\",\"お出かけ不可になる\":\"不能外出\",\"とお出かけできる\":\"外出解锁\",\"ようになる\":\"\",\"ポジティブ思考\":\"正向思考\",\"ファン\":\"粉丝\",\"ランダム\":\"随机\",\"」の\":\"」的\"}")!;
                var localizedSkillNames = new Dictionary<string, string>();  // 日文-中文技能名
                var localizedUmaNames = new Dictionary<string, string>();  // 日文-中文角色和马娘全名
                if (textData != default)
                {
                    foreach (var i in textData)
                    {
                        switch (i.Key)
                        {
                            case TextDataCategory.UmamusumeFullName:
                                foreach (var j in i.Value)
                                    localizedUmaNames[Names.GetUmamusume(j.Key).FullName] = j.Value;  // 暂存马娘全名
                                break;
                            case TextDataCategory.CostumeName:
                                foreach (var j in i.Value)
                                    Names.GetUmamusume(j.Key).Name = j.Value;
                                break;
                            case TextDataCategory.CharacterName:
                                foreach (var j in i.Value)
                                {
                                    localizedUmaNames[Names.GetCharacter(j.Key).Name] = j.Value;  // 暂存角色名字
                                    Names.GetCharacter(j.Key).Name = j.Value;
                                }
                                break;
                            case TextDataCategory.SkillName:
                                var translatedSkills = JsonConvert.DeserializeObject<List<SkillData>>(Load(SKILLS_FILEPATH)) ?? [];
                                foreach (var j in translatedSkills)
                                {
                                    if (i.Value.TryGetValue(j.Id, out var localized_name))
                                    {
                                        localizedSkillNames[j.Name] = localized_name;   // 暂存技能名字
                                        j.Name = localized_name;
                                    }
                                }
                                Skills = new SkillManagerGenerator(translatedSkills);
                                SkillManagerGenerator.Default = new(translatedSkills);
                                break;
                            case TextDataCategory.WinSaddleName:
                                SaddleNames = i.Value;
                                break;
                            case TextDataCategory.FactorName:
                                foreach (var j in i.Value)
                                    FactorIds[j.Key] = $"{j.Value}{string.Join(string.Empty, Enumerable.Repeat('★', int.Parse(j.Key.ToString()[^1..])))}";
                                break;
                            case TextDataCategory.EventName:
                                foreach (var j in Events)
                                    if (i.Value.TryGetValue(j.Key, out var localized_name))
                                        j.Value.Name = localized_name;
                                break;
                            case TextDataCategory.ClimaxItemName:
                                ClimaxItem = [];
                                foreach (var j in i.Value)
                                    ClimaxItem.Add(j.Key, j.Value);
                                break;
                        }
                    }

                    // 替换事件触发者名字和效果行
                    foreach (var j in Events)
                    {
                        if (localizedUmaNames.TryGetValue(j.Value.TriggerName, out var localized_name))
                            j.Value.TriggerName = localized_name;
                        foreach (var choiceList in j.Value.Choices)
                        {
                            foreach (var choice in choiceList)
                            {
                                choice.SuccessEffect = TranslateLine(choice.SuccessEffect);
                                if (choice.FailedEffect.Length > 0)
                                    choice.FailedEffect = TranslateLine(choice.FailedEffect);
                            }
                        }
                    }

                    // 替换成功失败事件效果行
                    foreach (var j in SuccessEvent)
                    {
                        foreach (var choiceList in j.Value.Choices)
                            foreach (var choice in choiceList)
                                choice.Effect = TranslateLine(choice.Effect);
                    }

                    string TranslateLine(string s)
                    {
                        s = DictionaryReplace(s, localizedUmaNames); // 替换马娘名字
                        // 技能名字
                        foreach (var match in LocalizedLineRegex().Matches(s).Cast<Match>())
                        {
                            if (match.Success)
                            {
                                var skillName = match.Groups[1].Value;
                                if (localizedSkillNames.TryGetValue(skillName, out var value))
                                    s = s.Replace(skillName, $"[aqua]{skillName}/{value}[/]");
                            }
                        }
                        s = DictionaryReplace(s, staticTranslation);    // 替换固定文本
                        return s;
                    }
                    static string DictionaryReplace(string line, Dictionary<string, string> dict)
                    {
                        var s = new StringBuilder(line);
                        foreach (var item in dict)
                            s.Replace(item.Key, item.Value);
                        return s.ToString();
                    }
                }
            }

            static bool TryDeserialize<T>(string filepath, out T result, Func<JToken, T> func)
            {
                if (File.Exists(filepath))
                {
                    try
                    {
                        var json = JToken.Parse(Load(filepath));
                        result = func(json);
                        return true;
                    }
                    catch
                    {
                        AnsiConsole.MarkupLine(I18N_LoadFail, Path.GetFileName(NAMES_FILEPATH));
                    }
                }
                else
                {
                    AnsiConsole.MarkupLine(I18N_NotExist, Path.GetFileName(NAMES_FILEPATH));
                }
                result = default!;
                return false;
            }
        }
        static string Load(string path)
        {
            try
            {
                return Encoding.UTF8.GetString(Brotli.Decompress(File.ReadAllBytes(path)));
            }
            catch (InvalidOperationException)
            {
                AnsiConsole.MarkupLine(I18N_DecompressError, Path.GetFileName(path));
                return string.Empty;
            }
        }

        [GeneratedRegex(@"「(.*?)」")]
        private static partial Regex LocalizedLineRegex();
    }

    public class NullableIntStringDictionary : Dictionary<int, string>
    {
        public new string this[int key]
        {
            get => ContainsKey(key) ? base[key] : "未知";
            set => base[key] = value;
        }
    }
}