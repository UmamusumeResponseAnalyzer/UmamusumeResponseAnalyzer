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
                19 => "[mediumpurple1]U[mediumpurple2]g[/][/]",
                20 => "[mediumpurple1]U[mediumpurple2]g[/][purple_2]1[/][/]",
                21 => "[mediumpurple1]U[mediumpurple2]g[/][purple_2]2[/][/]",
                22 => "[mediumpurple1]U[mediumpurple2]g[/][purple_2]3[/][/]",
                23 => "[mediumpurple1]U[mediumpurple2]g[/][purple_2]4[/][/]",
                24 => "[mediumpurple1]U[mediumpurple2]g[/][purple_2]5[/][/]",
                25 => "[mediumpurple1]U[mediumpurple2]g[/][purple_2]6[/][/]",
                26 => "[mediumpurple1]U[mediumpurple2]g[/][purple_2]7[/][/]",
                27 => "[mediumpurple1]U[mediumpurple2]g[/][purple_2]8[/][/]",
                28 => "[mediumpurple1]U[mediumpurple2]g[/][purple_2]9[/][/]",
                29 => "[mediumpurple1]U[mediumpurple2]f[/][/]",
                _ => "[mediumpurple1]Uf1[mediumpurple2]及以上[/][/]"
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
}
