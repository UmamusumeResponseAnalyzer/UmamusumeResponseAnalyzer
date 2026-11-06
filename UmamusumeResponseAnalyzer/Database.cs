using Newtonsoft.Json;
using System.Collections.Frozen;
using System.Text;
using System.Text.RegularExpressions;
using UmamusumeResponseAnalyzer.Entities;
using UmamusumeResponseAnalyzer.TerminalGui;
using static UmamusumeResponseAnalyzer.Localization.Database;

namespace UmamusumeResponseAnalyzer
{
    public enum DatabaseAvailability
    {
        Unavailable,
        Ready
    }

    public static class Database
    {
        private static DatabaseSnapshot? snapshot;

        public static DatabaseAvailability Availability => Volatile.Read(ref snapshot) is null
            ? DatabaseAvailability.Unavailable
            : DatabaseAvailability.Ready;

        public static SkillCatalog SkillCatalog => Current.SkillCatalog;

        private static DatabaseSnapshot Current => Volatile.Read(ref snapshot)
            ?? throw new InvalidOperationException(I18N_NotLoaded);

        #region Paths
        internal static string EVENT_NAME_FILEPATH => $"events_{(Config.Updater.TrainerIsMale ? "male" : "female")}.br";
        internal const string NAMES_FILEPATH = "names.br";
        internal const string SKILLS_FILEPATH = "skill_data.br";
        internal const string SKILL_PLANNING_FILEPATH = "skill_planning.br";
        internal const string TALENT_SKILLS_FILEPATH = "talent_skill_sets.br";
        internal const string FACTOR_IDS_FILEPATH = "factor_ids.br";
        internal const string SKILL_UPGRADE_SPECIALITY_FILEPATH = "skill_upgrade_speciality.br";
        internal const string SADDLE_IDS_FILEPATH = "wins_saddle.br";
        internal const string SUCCESSION_RELATION_FILEPATH = "succession_relation.br";
        #endregion
        #region Properties
        /// <summary>
        /// 计算单项属性的预计评价分。
        /// </summary>
        public static int StatusToPoint(int status)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(status);

            // 以十分之一分累加，整项属性最后取整。
            if (status <= 1200)
            {
                var rates = (ReadOnlySpan<int>)[5, 8, 10, 13, 16, 18, 21, 24, 26, 28, 29, 30,
                    31, 33, 34, 35, 39, 41, 42, 43, 52, 55, 66, 68];
                // 0 点参与首档累计，1200 点归入最后一档。
                var remaining = status + 1;
                var score = 0L;
                for (var band = 0; remaining > 0; band++)
                {
                    var count = Math.Min(remaining, band == rates.Length - 1 ? 51 : 50);
                    score += (long)count * rates[band];
                    remaining -= count;
                }
                return checked((int)(score / 10));
            }

            if (status <= 2000)
            {
                var remaining = status - 1200;
                var score = 38418L;
                // 首尾档分别覆盖 1201..1209、1990..2000。
                for (var band = 0; remaining > 0; band++)
                {
                    var count = Math.Min(remaining, band == 0 ? 9 : band == 79 ? 11 : 10);
                    score += (long)count * ((7910 + 131 * band) / 100);
                    remaining -= count;
                }
                return checked((int)(score / 10));
            }

            var delta = (long)status - 2000;
            var bands = delta / 25;
            var remainder = delta % 25;
            return checked((int)(14280 + (183 * delta + 25 * bands * (bands - 1) / 2 + bands * remainder + 1) / 10));
        }
        /// <summary>
        /// 技能
        /// </summary>
        public static SkillManagerGenerator Skills => Current.Skills;
        /// <summary>
        /// 剧本限定进化技能&lt;(基础技能id,剧本id),升级后&gt;
        /// </summary>
        public static FrozenDictionary<(int, int), SkillUpgradeSpeciality> SkillUpgradeSpeciality => Current.SkillUpgradeSpeciality;
        /// <summary>
        /// 育成事件
        /// </summary>
        public static FrozenDictionary<int, Story> Events => Current.Events;
        /// <summary>
        /// 马娘ID到马娘全名（包括前缀）的Dictionary
        /// </summary>
        public static NameManager Names => Current.Names;
        /// <summary>
        /// 巅峰杯道具的ID及其对应名称
        /// </summary>
        public static NullableIntStringDictionary ClimaxItem { get; private set; } = null!;

        static Database() => RefreshLocalizedText();

        internal static void RefreshLocalizedText() => ClimaxItem = new()
        {
            { 1001, ClimaxItem_1001 },
            { 1002, ClimaxItem_1002 },
            { 1003, ClimaxItem_1003 },
            { 1004, ClimaxItem_1004 },
            { 1005, ClimaxItem_1005 },
            { 1101, ClimaxItem_1101 },
            { 1102, ClimaxItem_1102 },
            { 1103, ClimaxItem_1103 },
            { 1104, ClimaxItem_1104 },
            { 1105, ClimaxItem_1105 },
            { 1201, ClimaxItem_1201 },
            { 1202, ClimaxItem_1202 },
            { 1203, ClimaxItem_1203 },
            { 1204, ClimaxItem_1204 },
            { 1205, ClimaxItem_1205 },
            { 2001, ClimaxItem_2001 },
            { 2002, ClimaxItem_2002 },
            { 2003, ClimaxItem_2003 },
            { 2101, ClimaxItem_2101 },
            { 2201, ClimaxItem_2201 },
            { 2202, ClimaxItem_2202 },
            { 2301, ClimaxItem_2301 },
            { 2302, ClimaxItem_2302 },
            { 3001, ClimaxItem_3001 },
            { 3101, ClimaxItem_3101 },
            { 4001, ClimaxItem_4001 },
            { 4002, ClimaxItem_4002 },
            { 4003, ClimaxItem_4003 },
            { 4004, ClimaxItem_4004 },
            { 4101, ClimaxItem_4101 },
            { 4102, ClimaxItem_4102 },
            { 4103, ClimaxItem_4103 },
            { 4104, ClimaxItem_4104 },
            { 4105, ClimaxItem_4105 },
            { 4106, ClimaxItem_4106 },
            { 4201, ClimaxItem_4201 },
            { 5001, ClimaxItem_5001 },
            { 5002, ClimaxItem_5002 },
            { 5003, ClimaxItem_5003 },
            { 5004, ClimaxItem_5004 },
            { 5005, ClimaxItem_5005 },
            { 7001, ClimaxItem_7001 },
            { 8001, ClimaxItem_8001 },
            { 8002, ClimaxItem_8002 },
            { 8003, ClimaxItem_8003 },
            { 9001, ClimaxItem_9001 },
            { 9002, ClimaxItem_9002 },
            { 9003, ClimaxItem_9003 },
            { 9004, ClimaxItem_9004 },
            { 10001, ClimaxItem_10001 },
            { 11001, ClimaxItem_11001 },
            { 11002, ClimaxItem_11002 },
            { 11003, ClimaxItem_11003 },
        };
        /// <summary>
        /// 马娘的天赋技能,Key是CardId
        /// </summary>
        public static FrozenDictionary<int, TalentSkillData[]> TalentSkill => Current.TalentSkill;
        /// <summary>
        /// 
        /// </summary>
        public static FrozenDictionary<int, string> FactorIds => Current.FactorIds;
        /// <summary>
        /// 可获得胜鞍的Id
        /// </summary>
        public static IReadOnlyList<int> SaddleIds => Current.SaddleIds;
        public static SuccessionRelationTable SuccessionRelation => Current.SuccessionRelation;
        #endregion
        public static Task<DatabaseAvailability> Initialize() => Task.FromResult(Initialize(out _));

        internal static DatabaseAvailability Initialize(out string? errorDetails)
        {
            var dataDirectory = Directory.GetCurrentDirectory();
            List<string> failures = [];
            var events = Deserialize<List<Story>>(Path.Combine(dataDirectory, EVENT_NAME_FILEPATH), failures);
            var names = Deserialize<List<BaseName>>(Path.Combine(dataDirectory, NAMES_FILEPATH), failures, new JsonSerializerSettings { TypeNameHandling = TypeNameHandling.All });
            var skills = Deserialize<List<SkillData>>(Path.Combine(dataDirectory, SKILLS_FILEPATH), failures);
            var skillUpgrade = Deserialize<List<SkillUpgradeSpeciality>>(Path.Combine(dataDirectory, SKILL_UPGRADE_SPECIALITY_FILEPATH), failures);
            var talentSkill = Deserialize<Dictionary<int, TalentSkillData[]>>(Path.Combine(dataDirectory, TALENT_SKILLS_FILEPATH), failures);
            var factorIds = Deserialize<Dictionary<int, string>>(Path.Combine(dataDirectory, FACTOR_IDS_FILEPATH), failures);
            var saddleIds = Deserialize<int[]>(Path.Combine(dataDirectory, SADDLE_IDS_FILEPATH), failures);
            var succession = Deserialize<SuccessionRelationTable>(Path.Combine(dataDirectory, SUCCESSION_RELATION_FILEPATH), failures);

            if (events is null || names is null || skills is null || skillUpgrade is null
                || talentSkill is null || factorIds is null || saddleIds is null || succession is null)
            {
                errorDetails = string.Join(Environment.NewLine + Environment.NewLine, failures);
                return DatabaseAvailability.Unavailable;
            }

            try
            {
                var unknownSkillUpgradeConditions = talentSkill.Values
                    .SelectMany(x => x)
                    .SelectMany(x => x.UpgradeSkills.Values)
                    .Concat(skillUpgrade.SelectMany(x => x.UpgradeSkills.Values))
                    .SelectMany(x => x)
                    .Where(x => !x.HasSupportedType)
                    .DistinctBy(x => (x.ConditionId, x.Type));
                foreach (var condition in unknownSkillUpgradeConditions)
                {
                    ReportWarning(string.Format(
                        I18N_UnknownSkillUpgradeConditionType,
                        condition.ConditionId,
                        condition.Type));
                }

                var next = new DatabaseSnapshot(
                    SkillCatalog.Load(Path.Combine(dataDirectory, SKILL_PLANNING_FILEPATH)),
                    new SkillManagerGenerator(skills),
                    skillUpgrade.ToFrozenDictionary(x => (x.BaseSkillId, x.ScenarioId)),
                    events.ToFrozenDictionary(x => x.Id),
                    new NameManager(names),
                    talentSkill.ToFrozenDictionary(),
                    factorIds.ToFrozenDictionary(),
                    Array.AsReadOnly([.. saddleIds]),
                    succession);
                Volatile.Write(ref snapshot, next);
                errorDetails = null;
                return DatabaseAvailability.Ready;
            }
            catch (Exception ex)
            {
                ReportWarning(string.Format(I18N_InvalidSnapshot, ex.Message));
                errorDetails = $"{dataDirectory}{Environment.NewLine}{ex}";
                return DatabaseAvailability.Unavailable;
            }
        }

        static void ReportWarning(string message)
        {
            TerminalUi.Log("Database", message, UiSeverity.Warning);
            TerminalUi.Notify("Database", message, UiSeverity.Warning);
        }

        static T? Deserialize<T>(string filepath, List<string> failures, JsonSerializerSettings? settings = null)
        {
            if (!File.Exists(filepath))
            {
                failures.Add(string.Format(I18N_NotExist, filepath));
                ReportWarning(string.Format(I18N_NotExist, Path.GetFileName(filepath)));
                return default;
            }

            try
            {
                using var fileStream = new FileStream(filepath, FileMode.Open, FileAccess.Read, FileShare.Read, 16384);
                using var brotliStream = new System.IO.Compression.BrotliStream(fileStream, System.IO.Compression.CompressionMode.Decompress);
                using var streamReader = new StreamReader(brotliStream, Encoding.UTF8);
                using var jsonReader = new JsonTextReader(streamReader);

                var serializer = settings is null ? JsonSerializer.CreateDefault() : JsonSerializer.Create(settings);
                if (serializer.Deserialize<T>(jsonReader) is { } value)
                    return value;

                failures.Add($"{filepath}: JSON null");
                ReportWarning(string.Format(I18N_LoadFail, Path.GetFileName(filepath)));
            }
            catch (InvalidDataException ex)
            {
                failures.Add($"{filepath}{Environment.NewLine}{ex}");
                ReportWarning(string.Format(I18N_DecompressError, Path.GetFileName(filepath)));
            }
            catch (Exception ex)
            {
                failures.Add($"{filepath}{Environment.NewLine}{ex}");
                ReportWarning(string.Format(I18N_LoadFail, Path.GetFileName(filepath)));
            }
            return default;
        }
    }

    internal sealed record DatabaseSnapshot(
        SkillCatalog SkillCatalog,
        SkillManagerGenerator Skills,
        FrozenDictionary<(int BaseSkillId, int ScenarioId), SkillUpgradeSpeciality> SkillUpgradeSpeciality,
        FrozenDictionary<int, Story> Events,
        NameManager Names,
        FrozenDictionary<int, TalentSkillData[]> TalentSkill,
        FrozenDictionary<int, string> FactorIds,
        IReadOnlyList<int> SaddleIds,
        SuccessionRelationTable SuccessionRelation);

    public class NullableIntStringDictionary : Dictionary<int, string>
    {
        public new string this[int key]
        {
            get => TryGetValue(key, out var val) ? val : Localization.NameManager.I18N_Unknown;
            set => base[key] = value;
        }
    }
}
