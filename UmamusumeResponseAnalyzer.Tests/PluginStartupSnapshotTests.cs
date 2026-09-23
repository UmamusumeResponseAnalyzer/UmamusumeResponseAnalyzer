using System.IO.Compression;
using System.Runtime.Loader;
using UmamusumeResponseAnalyzer.Plugin;
using Xunit;

namespace UmamusumeResponseAnalyzer.Tests;

[Collection("PluginRuntime")]
public sealed class PluginStartupSnapshotTests : IDisposable
{
    readonly string originalDirectory = Directory.GetCurrentDirectory();
    readonly string directory = Path.Combine(Path.GetTempPath(), $"ura-startup-snapshot-{Guid.NewGuid():N}");

    public PluginStartupSnapshotTests()
    {
        PluginManager.ShutdownAsync().GetAwaiter().GetResult();
        Directory.CreateDirectory(Path.Combine(directory, "Plugins"));
        Directory.SetCurrentDirectory(directory);
    }

    [Fact]
    public async Task UpdatedZipAndNewDependencyTakeEffectOnlyInFreshRuntime()
    {
        WriteVersion("v1", "1.0.0", []);
        PluginManager.Init();
        var original = Assert.Single(PluginManager.LoadedPlugins);
        var originalContext = AssemblyLoadContext.GetLoadContext(original.GetType().Assembly)!;
        Assert.False(originalContext.IsCollectible);

        File.Delete("Plugins/Snapshot.zip");
        WriteVersion("v2", "2.0.0", ["Added"]);
        PluginCompiler.CompilePackage("""
            using UmamusumeResponseAnalyzer.Plugin;
            public sealed class Added : IPlugin { public void Initialize(IPluginContext context) { } }
            """, "Added", "Plugins/Added.zip");
        PluginManager.InitializeLoadedPlugins();

        Assert.Equal("v1", File.ReadAllText("version.txt"));
        Assert.Equal(new Version(1, 0, 0), Assert.Single(PluginManager.Metadatas).Value.Version);
        Assert.Same(original, Assert.Single(PluginManager.LoadedPlugins));
        Assert.Same(originalContext, Assert.Single(PluginManager.Contexts).Value);
        Assert.Single(Assert.Single(PluginManager.ContextGroups));

        await PluginManager.ShutdownAsync();
        PluginManager.Init();
        PluginManager.InitializeLoadedPlugins();
        Assert.Equal("v2", File.ReadAllText("version.txt"));
        Assert.Equal(new Version(2, 0, 0), PluginManager.Metadatas["Snapshot"].Version);
        Assert.Equal(2, PluginManager.LoadedPlugins.Count);
        Assert.Equal(2, Assert.Single(PluginManager.ContextGroups).Count);
    }

    static void WriteVersion(string text, string version, string[] dependencies)
    {
        PluginCompiler.Compile($$"""
            public static class LateLibrary { public static string Value => "{{text}}"; }
            """, "LateLibrary", "LateLibrary.dll");
        PluginCompiler.CompilePackage("""
            using System.IO;
            using System.Runtime.CompilerServices;
            using UmamusumeResponseAnalyzer.Plugin;
            public sealed class Snapshot : IPlugin
            {
                public void Initialize(IPluginContext context) => Write();
                [MethodImpl(MethodImplOptions.NoInlining)]
                static void Write() => File.WriteAllText("version.txt", LateLibrary.Value);
            }
            """, "Snapshot", "Plugins/Snapshot.zip", dependencies, version: version,
            referencePaths: [Path.GetFullPath("LateLibrary.dll")]);
        using var archive = ZipFile.Open("Plugins/Snapshot.zip", ZipArchiveMode.Update);
        archive.CreateEntryFromFile("LateLibrary.dll", "LateLibrary.dll");
    }

    public void Dispose()
    {
        try { PluginManager.ShutdownAsync().GetAwaiter().GetResult(); }
        finally
        {
            Directory.SetCurrentDirectory(originalDirectory);
            Directory.Delete(directory, recursive: true);
        }
    }
}
