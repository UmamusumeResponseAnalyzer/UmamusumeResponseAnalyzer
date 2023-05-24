using Newtonsoft.Json;

namespace UmamusumeResponseAnalyzer.Entities
{
    public class GradeRank
    {
        public string Rank
        {
            get => Id switch
            {
                1 => "[grey46]G[/]",
                2 => "[grey46]G+[/]",
                3 => "[mediumpurple3_1]F[/]",
                4 => "[mediumpurple3_1]F+[/]",
                5 => "[pink3]E[/]",
                6 => "[pink3]E+[/]",
                7 => "[deepskyblue3_1]D[/]",
                8 => "[deepskyblue3_1]D+[/]",
                9 => "[darkolivegreen3_1]C[/]",
                10 => "[darkolivegreen3_1]C+[/]",
                11 => "[palevioletred1]B[/]",
                12 => "[palevioletred1]B+[/]",
                13 => "[darkorange]A[/]",
                14 => "[darkorange]A+[/]",
                15 => "[lightgoldenrod2_2]S[/]",
                16 => "[lightgoldenrod2_2]S+[/]",
                17 => "[lightgoldenrod2_2]SS[/]",
                18 => "[lightgoldenrod2_2]SS+[/]",
                19 => "[mediumpurple1]U[mediumpurple2]G[/][/]",
                20 => "[mediumpurple1]U[mediumpurple2]G[/][purple_2]1[/][/]",
                21 => "[mediumpurple1]U[mediumpurple2]G[/][purple_2]2[/][/]",
                22 => "[mediumpurple1]U[mediumpurple2]G[/][purple_2]3[/][/]",
                23 => "[mediumpurple1]U[mediumpurple2]G[/][purple_2]4[/][/]",
                24 => "[mediumpurple1]U[mediumpurple2]G[/][purple_2]5[/][/]",
                25 => "[mediumpurple1]U[mediumpurple2]G[/][purple_2]6[/][/]",
                26 => "[mediumpurple1]U[mediumpurple2]G[/][purple_2]7[/][/]",
                27 => "[mediumpurple1]U[mediumpurple2]G[/][purple_2]8[/][/]",
                28 => "[mediumpurple1]U[mediumpurple2]G[/][purple_2]9[/][/]",
                29 => "[mediumpurple1]U[mediumpurple2]F[/][/]",
                30 => "[mediumpurple1]U[mediumpurple2]F[/][purple_2]1[/][/]",
                31 => "[mediumpurple1]U[mediumpurple2]F[/][purple_2]2[/][/]",
                32 => "[mediumpurple1]U[mediumpurple2]F[/][purple_2]3[/][/]",
                33 => "[mediumpurple1]U[mediumpurple2]F[/][purple_2]4[/][/]",
                34 => "[mediumpurple1]U[mediumpurple2]F[/][purple_2]5[/][/]",
                35 => "[mediumpurple1]U[mediumpurple2]F[/][purple_2]6[/][/]",
                36 => "[mediumpurple1]U[mediumpurple2]F[/][purple_2]7[/][/]",
                37 => "[mediumpurple1]U[mediumpurple2]F[/][purple_2]8[/][/]",
                38 => "[mediumpurple1]U[mediumpurple2]F[/][purple_2]9[/][/]",
                39 => "[mediumpurple1]U[mediumpurple2]E[/][/]",
                40 => "[mediumpurple1]U[mediumpurple2]E[/][purple_2]1[/][/]",
                41 => "[mediumpurple1]U[mediumpurple2]E[/][purple_2]2[/][/]",
                42 => "[mediumpurple1]U[mediumpurple2]E[/][purple_2]3[/][/]",
                43 => "[mediumpurple1]U[mediumpurple2]E[/][purple_2]4[/][/]",
                44 => "[mediumpurple1]U[mediumpurple2]E[/][purple_2]5[/][/]",
                45 => "[mediumpurple1]U[mediumpurple2]E[/][purple_2]6[/][/]",
                46 => "[mediumpurple1]U[mediumpurple2]E[/][purple_2]7[/][/]",
                47 => "[mediumpurple1]U[mediumpurple2]E[/][purple_2]8[/][/]",
                48 => "[mediumpurple1]U[mediumpurple2]E[/][purple_2]9[/][/]",
                49 => "[mediumpurple1]U[mediumpurple2]D[/][/]",
                50 => "[mediumpurple1]U[mediumpurple2]D[/][purple_2]1[/][/]",
                51 => "[mediumpurple1]U[mediumpurple2]D[/][purple_2]2[/][/]",
                52 => "[mediumpurple1]U[mediumpurple2]D[/][purple_2]3[/][/]",
                53 => "[mediumpurple1]U[mediumpurple2]D[/][purple_2]4[/][/]",
                54 => "[mediumpurple1]U[mediumpurple2]D[/][purple_2]5[/][/]",
                55 => "[mediumpurple1]U[mediumpurple2]D[/][purple_2]6[/][/]",
                56 => "[mediumpurple1]U[mediumpurple2]D[/][purple_2]7[/][/]",
                57 => "[mediumpurple1]U[mediumpurple2]D[/][purple_2]8[/][/]",
                58 => "[mediumpurple1]U[mediumpurple2]D[/][purple_2]9[/][/]",
                59 => "[mediumpurple1]U[mediumpurple2]C[/][/]",
                60 => "[mediumpurple1]U[mediumpurple2]C[/][purple_2]1[/][/]",
                61 => "[mediumpurple1]U[mediumpurple2]C[/][purple_2]2[/][/]",
                62 => "[mediumpurple1]U[mediumpurple2]C[/][purple_2]3[/][/]",
                63 => "[mediumpurple1]U[mediumpurple2]C[/][purple_2]4[/][/]",
                64 => "[mediumpurple1]U[mediumpurple2]C[/][purple_2]5[/][/]",
                65 => "[mediumpurple1]U[mediumpurple2]C[/][purple_2]6[/][/]",
                66 => "[mediumpurple1]U[mediumpurple2]C[/][purple_2]7[/][/]",
                67 => "[mediumpurple1]U[mediumpurple2]C[/][purple_2]8[/][/]",
                68 => "[mediumpurple1]U[mediumpurple2]C[/][purple_2]9[/][/]",
                69 => "[mediumpurple1]U[mediumpurple2]B[/][/]",
                70 => "[mediumpurple1]U[mediumpurple2]B[/][purple_2]1[/][/]",
                71 => "[mediumpurple1]U[mediumpurple2]B[/][purple_2]2[/][/]",
                72 => "[mediumpurple1]U[mediumpurple2]B[/][purple_2]3[/][/]",
                73 => "[mediumpurple1]U[mediumpurple2]B[/][purple_2]4[/][/]",
                74 => "[mediumpurple1]U[mediumpurple2]B[/][purple_2]5[/][/]",
                75 => "[mediumpurple1]U[mediumpurple2]B[/][purple_2]6[/][/]",
                76 => "[mediumpurple1]U[mediumpurple2]B[/][purple_2]7[/][/]",
                77 => "[mediumpurple1]U[mediumpurple2]B[/][purple_2]8[/][/]",
                78 => "[mediumpurple1]U[mediumpurple2]B[/][purple_2]9[/][/]",
                79 => "[mediumpurple1]U[mediumpurple2]A[/][/]",
                80 => "[mediumpurple1]U[mediumpurple2]A[/][purple_2]1[/][/]",
                81 => "[mediumpurple1]U[mediumpurple2]A[/][purple_2]2[/][/]",
                82 => "[mediumpurple1]U[mediumpurple2]A[/][purple_2]3[/][/]",
                83 => "[mediumpurple1]U[mediumpurple2]A[/][purple_2]4[/][/]",
                84 => "[mediumpurple1]U[mediumpurple2]A[/][purple_2]5[/][/]",
                85 => "[mediumpurple1]U[mediumpurple2]A[/][purple_2]6[/][/]",
                86 => "[mediumpurple1]U[mediumpurple2]A[/][purple_2]7[/][/]",
                87 => "[mediumpurple1]U[mediumpurple2]A[/][purple_2]8[/][/]",
                88 => "[mediumpurple1]U[mediumpurple2]A[/][purple_2]9[/][/]",
                89 => "[mediumpurple1]U[mediumpurple2]S[/][/]",
                90 => "[mediumpurple1]U[mediumpurple2]S[/][purple_2]1[/][/]",
                91 => "[mediumpurple1]U[mediumpurple2]S[/][purple_2]2[/][/]",
                92 => "[mediumpurple1]U[mediumpurple2]S[/][purple_2]3[/][/]",
                93 => "[mediumpurple1]U[mediumpurple2]S[/][purple_2]4[/][/]",
                94 => "[mediumpurple1]U[mediumpurple2]S[/][purple_2]5[/][/]",
                95 => "[mediumpurple1]U[mediumpurple2]S[/][purple_2]6[/][/]",
                96 => "[mediumpurple1]U[mediumpurple2]S[/][purple_2]7[/][/]",
                97 => "[mediumpurple1]U[mediumpurple2]S[/][purple_2]8[/][/]",
                98 => "[mediumpurple1]U[mediumpurple2]S[/][purple_2]9[/][/]",
                _ => "[mediumpurple1]US9[mediumpurple2]以上[/][/]"
            };
        }
        [JsonProperty("id")]
        public int Id { get; set; }
        /// <summary>
        /// 满足该评分所需的最低评价点
        /// </summary>
        [JsonProperty("min_value")]
        public int Min { get; set; }
        /// <summary>
        /// 满足该评分所需的最高评价点
        /// </summary>
        [JsonProperty("max_value")]
        public int Max { get; set; }
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
    public class TrainStats
    {
        public int[] FiveValueGain;
        public int PtGain;
        public int VitalGain;
        public int FailureRate;
        public double ScoreAssumeSuccessNoVital()
        {
            return FiveValueGain.Sum() + PtGain * 0.7;
        }
        public double ScoreNoVital()
        {
            const double scoreAssumeFailed = -100;//掉1心情5属性，还相当于浪费一回合，有各种隐性影响
            return ScoreAssumeSuccessNoVital() * (1 - 0.01 * FailureRate) + 0.01 * FailureRate * scoreAssumeFailed;
        }
        public double Score(int currentVital, int maxVital)
        {
            double scoreAssumeFailed = -100;
            double scoreAssumeSuccess = FiveValueGain.Sum() + PtGain * 0.7;
            return (scoreAssumeSuccess - ScoreUtils.ScoreOfVital(currentVital, maxVital) + ScoreUtils.ScoreOfVital(currentVital + VitalGain, maxVital)) * (1 - 0.01 * FailureRate) + 0.01 * FailureRate * scoreAssumeFailed;
        }
    }
}
