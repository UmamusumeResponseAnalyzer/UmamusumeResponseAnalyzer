using UmamusumeResponseAnalyzer.Entities;

namespace UmamusumeResponseAnalyzer;

public static class SkillScoring
{
    // 服务器未公开完整评分公式；调用方必须保留评分校准状态。
    public static int Estimate(SkillDefinition skill, Gallop.SingleModeChara chara, int level = 1)
    {
        if (skill.Id is > 1000000 and < 2000000) return 0;
        if (skill.Id is >= 100000 and < 200000) return 170 * level;
        if (skill.Id is >= 10000 and < 100000) return 120 * level;
        // 发动条件的赛道限制和附加效果段不能代替 master 的评分适性标签。
        var styles = skill.Tags.Where(tag => tag is >= 101 and <= 104).Select(tag => tag switch
        {
            101 => chara.proper_running_style_nige, 102 => chara.proper_running_style_senko,
            103 => chara.proper_running_style_sashi, 104 => chara.proper_running_style_oikomi,
            _ => throw new InvalidDataException($"Skill {skill.Id}: running style tag {tag}")
        }).ToArray();
        var distances = skill.Tags.Where(tag => tag is >= 201 and <= 204).Select(tag => tag switch
        {
            201 => chara.proper_distance_short, 202 => chara.proper_distance_mile,
            203 => chara.proper_distance_middle, 204 => chara.proper_distance_long,
            _ => throw new InvalidDataException($"Skill {skill.Id}: distance tag {tag}")
        }).ToArray();
        var score = (decimal)skill.Grade;
        if (styles.Length != 0) score *= Multiplier(styles.Max());
        if (distances.Length != 0) score *= Multiplier(distances.Max());
        return (int)Math.Round(score);
    }

    static decimal Multiplier(int aptitude) => aptitude switch
    {
        8 or 7 => 1.1m, 6 or 5 => 0.9m, 4 or 3 or 2 => 0.8m, 1 => 0.7m,
        _ => throw new InvalidDataException($"Invalid skill aptitude {aptitude}")
    };
}
