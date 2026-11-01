using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Globalization;

namespace UmamusumeResponseAnalyzer.Entities;

public enum SkillEvolutionKind { Character = 1, Scenario = 2, Inherited = 3 }
public enum SkillConditionKind { ServerProgress, TagCount, SkillOwned, Unverified, EffectCount }

public sealed record SkillDefinition(
    int Id, string Name, int GroupId, int Rarity, int Rate, int Grade,
    int? Cost, int StatusType, int StatusValue, int SolvableType, int DisplayOrder,
    int DisableCountCondition, ImmutableArray<int> Tags, ImmutableArray<int> EffectTags,
    ImmutableArray<string> Preconditions, ImmutableArray<string> Conditions);

public sealed record SkillConditionRule(
    int Id, int Group, SkillConditionKind Kind, int Operand, string Description);

public sealed record SkillEvolutionRule(
    int BaseSkillId, int SkillId, SkillEvolutionKind Kind, int CardId, int Rank,
    int ScenarioId, long StartDate, ImmutableArray<SkillConditionRule> Conditions);

public sealed record SkillTalent(int CardId, int SkillId, int Rank);
public sealed record SkillScenarioCondition(int ScenarioId, int Rank, ImmutableArray<SkillConditionRule> Conditions);

public sealed record SkillPlanningData(
    int Format, ImmutableArray<SkillDefinition> Skills, ImmutableArray<SkillTalent> Talents,
    ImmutableArray<SkillEvolutionRule> Evolutions, ImmutableDictionary<int, int> ScenarioLimits,
    ImmutableArray<SkillScenarioCondition> ScenarioConditions,
    ImmutableDictionary<int, int> HintDiscounts)
{
    public const int CurrentFormat = 1;

    public SkillCatalog CreateCatalog()
    {
        if (Format != CurrentFormat)
            throw new InvalidDataException($"skill_planning.br: format {Format}, expected {CurrentFormat}");
        var definitions = Skills.ToFrozenDictionary(x => x.Id);
        foreach (var skill in Skills)
            if (skill.Id <= 0 || skill.Cost < 0 || skill.StatusType is < 0 or > 5
                || skill.Preconditions.Length is not (2 or 3) || skill.Conditions.Length != skill.Preconditions.Length)
                throw new InvalidDataException($"skill_planning.br: invalid skill {skill.Id}");
        foreach (var talent in Talents)
            if (!definitions.ContainsKey(talent.SkillId))
                throw new InvalidDataException($"skill_planning.br: missing talent skill {talent.SkillId}");
        foreach (var rule in Evolutions)
            if (!Enum.IsDefined(rule.Kind) || !definitions.ContainsKey(rule.BaseSkillId) || !definitions.ContainsKey(rule.SkillId))
                throw new InvalidDataException($"skill_planning.br: missing evolution skill {rule.BaseSkillId} -> {rule.SkillId}");
        foreach (var condition in Evolutions.SelectMany(x => x.Conditions).Concat(ScenarioConditions.SelectMany(x => x.Conditions)))
            if (condition.Id <= 0 || !Enum.IsDefined(condition.Kind)
                || condition.Kind == SkillConditionKind.SkillOwned && !definitions.ContainsKey(condition.Operand))
                throw new InvalidDataException($"skill_planning.br: invalid condition {condition.Id}");
        if (ScenarioLimits.Values.Any(x => x < 0) || Enumerable.Range(1, 5).Any(x => !HintDiscounts.ContainsKey(x))
            || HintDiscounts.Values.Any(x => x is < 0 or > 100))
            throw new InvalidDataException("skill_planning.br: invalid scenario limits or hint discounts");
        return new(this, definitions);
    }
}

public sealed record SkillCatalog(SkillPlanningData Data, FrozenDictionary<int, SkillDefinition> Skills)
{
    public static SkillCatalog Load(string path)
    {
        using var stream = File.OpenRead(path);
        using var compressed = new System.IO.Compression.BrotliStream(stream, System.IO.Compression.CompressionMode.Decompress);
        return (System.Text.Json.JsonSerializer.Deserialize<SkillPlanningData>(compressed)
            ?? throw new InvalidDataException($"{path}: JSON null")).CreateCatalog();
    }

    public IEnumerable<SkillEvolutionRule> GetEvolutions(Gallop.SingleModeChara chara)
    {
        // 协议中的无时区时间为 JST；候选生效时间按育成开始时刻判断。
        var start = new DateTimeOffset(DateTime.ParseExact(chara.start_time, "yyyy-MM-dd HH:mm:ss",
            CultureInfo.InvariantCulture), TimeSpan.FromHours(9));
        var unix = start.ToUnixTimeSeconds();
        return Data.Evolutions.Where(x => x.StartDate <= unix && x.Kind switch
        {
            SkillEvolutionKind.Character => x.CardId == chara.card_id && x.Rank <= chara.talent_level,
            SkillEvolutionKind.Scenario => x.ScenarioId == chara.scenario_id,
            SkillEvolutionKind.Inherited => true,
            _ => throw new InvalidDataException($"Unknown evolution kind {x.Kind}")
        });
    }

    public static bool IsComplete(IEnumerable<SkillConditionRule> conditions, Gallop.SingleModeChara chara)
        => conditions.GroupBy(x => x.Group).All(group => group.Any(rule =>
            chara.skill_upgrade_info_array.Any(x => x.condition_id == rule.Id && x.current_count >= x.total_count)));

    public bool IsComplete(SkillEvolutionRule rule, Gallop.SingleModeChara chara)
        => IsComplete(rule.Conditions, chara) || rule.Kind == SkillEvolutionKind.Character
            && Data.ScenarioConditions.Where(x => x.ScenarioId == chara.scenario_id && x.Rank == rule.Rank)
                .Any(x => IsComplete(x.Conditions, chara));

    public int Cost(SkillDefinition skill, Gallop.SingleModeChara chara)
    {
        var cost = skill.Cost ?? throw new InvalidDataException($"Skill {skill.Id} has no learning cost");
        var level = chara.skill_tips_array.FirstOrDefault(x => x.group_id == skill.GroupId && x.rarity == skill.Rarity)?.level ?? 0;
        var hint = level == 0 ? 0 : Data.HintDiscounts[level];
        var condition = chara.chara_effect_id_array.Contains(7) ? 10 : chara.chara_effect_id_array.Contains(40) ? 4 : 0;
        return cost * Math.Clamp(100 - hint - condition, 0, 100) / 100;
    }
}
