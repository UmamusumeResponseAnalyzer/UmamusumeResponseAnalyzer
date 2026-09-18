using Microsoft.Win32;
using Newtonsoft.Json.Linq;
using Xunit;

namespace UmamusumeResponseAnalyzer.Tests;

public sealed class GameDiscoveryTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), $"ura-dmm-discovery-{Guid.NewGuid():N}");
    private string InstallationFile => Path.Combine(directory, "dmmgame.cnf");

    public GameDiscoveryTests() => Directory.CreateDirectory(directory);

    [Fact]
    public void InstalledGameReturnsNormalizedExecutableWithoutRewritingRecords()
    {
        var executable = Path.Combine(directory, "umamusume.exe");
        File.WriteAllText(executable, "synthetic executable");
        var json = new JObject { ["contents"] = new JArray(
            Record("ignored", product: "other"), Record("ignored", gameType: "other"),
            Record("ignored", installed: false), Record(Path.Combine(directory, "."))) }.ToString();
        File.WriteAllText(InstallationFile, json);

        Assert.Equal(executable, UraCoreHelper.FindDmmGameExecutable(InstallationFile));
        Assert.Equal(json, File.ReadAllText(InstallationFile));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("{\"contents\":[]}")]
    [InlineData("""{"contents":[{"productId":"umamusume","gameType":"GCL","detail":{"installed":false}}]}""")]
    [InlineData("""{"contents":[{"productId":"other","gameType":"GCL"},{"productId":"umamusume","gameType":"other"}]}""")]
    public void MissingOrUninstalledGameReturnsNull(string? json)
    {
        if (json is not null) File.WriteAllText(InstallationFile, json);
        Assert.Null(UraCoreHelper.FindDmmGameExecutable(InstallationFile));
        Assert.Null(UraCoreHelper.FindDmmGameExecutable(Path.Combine(directory, "absent", "dmmgame.cnf")));
    }

    [Theory]
    [InlineData("broken json")]
    [InlineData("{}")]
    [InlineData("{\"contents\":{}}")]
    [InlineData("{\"contents\":[null]}")]
    [InlineData("""{"contents":[{"productId":"umamusume","gameType":"GCL"}]}""")]
    [InlineData("""{"contents":[{"productId":"umamusume","gameType":"GCL","detail":{"installed":"true"}}]}""")]
    public void MalformedRecordsReportSourceAndReason(string json)
    {
        File.WriteAllText(InstallationFile, json);
        var error = Assert.Throws<InvalidDataException>(() => UraCoreHelper.FindDmmGameExecutable(InstallationFile));
        Assert.Contains(InstallationFile, error.Message);
        Assert.NotNull(error.InnerException);
        Assert.Contains(error.InnerException.Message, error.Message);
        Assert.Equal(json, File.ReadAllText(InstallationFile));
    }

    [Fact]
    public void AmbiguousRecordsAndInvalidLocationsReportSourceAndReason()
    {
        foreach (var contents in new[]
        {
            new JArray(Record(directory), Record(directory)),
            new JArray(Record("relative")),
            new JArray(Record(Path.Combine(directory, "missing-directory"))),
            new JArray(Record(InstallationFile)),
            new JArray(Record(directory)), // Existing directory without umamusume.exe.
            new JArray(Record(null)),
        })
        {
            File.WriteAllText(InstallationFile, new JObject { ["contents"] = contents }.ToString());
            var error = Assert.Throws<InvalidDataException>(() => UraCoreHelper.FindDmmGameExecutable(InstallationFile));
            Assert.Contains(InstallationFile, error.Message);
            Assert.NotNull(error.InnerException);
            Assert.Contains(error.InnerException.Message, error.Message);
        }
    }

    [Fact]
    public void DmmFailureKeepsOtherSourcesAndCandidatesStaySortedAndUnique()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Skip("Registry discovery requires Windows.");
            return;
        }
        var registryPath = $@"Software\URA.Tests\DmmDiscovery-{Guid.NewGuid():N}";
        using var registry = Registry.CurrentUser.CreateSubKey(registryPath);
        try
        {
            var other = Path.Combine(directory, "komoe");
            using (var key = registry.CreateSubKey(@"Software\komoemumamusume"))
                key.SetValue("GameInstallPath", other);
            using (var key = registry.CreateSubKey(@"Software\Classes\Local Settings\Software\Microsoft\Windows\Shell\MuiCache"))
                key.SetValue(directory.Replace('\\', '/') + "/umamusume.exe.FriendlyAppName", "synthetic");
            using (var key = registry.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\FeatureUsage\AppSwitched"))
                key.SetValue(Path.Combine(directory, "UMAMUSUME.EXE"), 1);
            var expected = new[] { directory, other }.Order(StringComparer.OrdinalIgnoreCase).ToArray();
            File.WriteAllText(Path.Combine(directory, "umamusume.exe"), "synthetic executable");

            foreach (var json in new[] { "broken json", new JObject { ["contents"] = new JArray(Record(directory)) }.ToString() })
            {
                File.WriteAllText(InstallationFile, json);
                var warnings = new List<string>();
                Assert.Equal(expected, UraCoreHelper.LoadGamePaths(warnings, InstallationFile, registry));
                if (json == "broken json") Assert.Contains(InstallationFile, Assert.Single(warnings));
                else Assert.Empty(warnings);
            }
        }
        finally
        {
            Registry.CurrentUser.DeleteSubKeyTree(registryPath);
        }
    }

    private static JObject Record(string? path, bool installed = true, string product = "umamusume", string gameType = "GCL") => new()
    {
        ["productId"] = product, ["gameType"] = gameType,
        ["detail"] = new JObject { ["installed"] = installed, ["path"] = path },
    };

    public void Dispose() => Directory.Delete(directory, true);
}
