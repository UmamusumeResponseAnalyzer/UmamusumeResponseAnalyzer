using System.IO.Compression;
using System.Reflection;
using System.Text;
using Newtonsoft.Json;
using UmamusumeResponseAnalyzer.Entities;
using Xunit;

namespace UmamusumeResponseAnalyzer.Tests;

[CollectionDefinition("Database", DisableParallelization = true)]
public sealed class DatabaseCollection : ICollectionFixture<DatabaseFixture> { }

public sealed class DatabaseFixture : IDisposable
{
    private readonly string directory;
    private readonly string originalDirectory;
    private readonly string[] paths;

    public DatabaseFixture()
    {
        EnsureConfigInitialized();
        originalDirectory = Directory.GetCurrentDirectory();
        directory = Path.Combine(Path.GetTempPath(), $"ura-database-fixture-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        Directory.SetCurrentDirectory(directory);
        paths =
        [
            Path.Combine(directory, Database.EVENT_NAME_FILEPATH),
            Path.Combine(directory, Database.NAMES_FILEPATH),
            Path.Combine(directory, Database.SKILLS_FILEPATH),
            Path.Combine(directory, Database.SKILL_UPGRADE_SPECIALITY_FILEPATH),
            Path.Combine(directory, Database.TALENT_SKILLS_FILEPATH),
            Path.Combine(directory, Database.FACTOR_IDS_FILEPATH),
            Path.Combine(directory, Database.SADDLE_IDS_FILEPATH),
            Path.Combine(directory, Database.SUCCESSION_RELATION_FILEPATH)
        ];

        try
        {
            Write(paths[0], Array.Empty<Story>());
            Write(
                paths[1],
                new List<BaseName>
            {
                new SupportCardName(30001, "速卡", "波旁", 101, 1001),
                new SupportCardName(30002, "力卡", "力卡", 102, 1002),
                new SupportCardName(30003, "友卡", "友卡", 0, 1003),
                new SupportCardName(30137, "神团", "神团", 0, 1004),
                new SupportCardName(30067, "皇团", "皇团", 101, 1005),
                new SupportCardName(30241, "传奇团", "传奇团", 0, 9047),
                new BaseName(101, "理事长", "理事长"),
                new BaseName(1001, "美浦波旁", "波旁"),
                new BaseName(1004, "美浦波旁", "波旁"),
                new BaseName(1006, "无声铃鹿", "铃鹿")
            },
                new() { TypeNameHandling = TypeNameHandling.All });
            Write(paths[2], Array.Empty<SkillData>());
            Write(paths[3], Array.Empty<SkillUpgradeSpeciality>());
            Write(paths[4], new Dictionary<int, TalentSkillData[]>());
            Write(paths[5], new Dictionary<int, string>());
            Write(paths[6], Array.Empty<int>());
            Write(paths[7], new SuccessionRelationTable());

            var availability = Database.Initialize().GetAwaiter().GetResult();
            if (availability != DatabaseAvailability.Ready)
                throw new InvalidOperationException($"Database test fixture initialization failed: {availability}.");
        }
        catch
        {
            Directory.SetCurrentDirectory(originalDirectory);
            Directory.Delete(directory, recursive: true);
            throw;
        }
    }

    public void Dispose()
    {
        Directory.SetCurrentDirectory(originalDirectory);
        Directory.Delete(directory, recursive: true);
    }

    private static void Write<T>(string path, T value, JsonSerializerSettings? settings = null)
    {
        using var file = File.Create(path);
        using var brotli = new BrotliStream(file, CompressionMode.Compress);
        using var writer = new StreamWriter(brotli, Encoding.UTF8);
        writer.Write(JsonConvert.SerializeObject(value, settings));
    }

    private static void EnsureConfigInitialized()
    {
        var current = typeof(Config).GetProperty("Current", BindingFlags.NonPublic | BindingFlags.Static)!;
        if (current.GetValue(null) is not null)
            return;
        current.SetValue(null, new YamlConfig());
    }
}
