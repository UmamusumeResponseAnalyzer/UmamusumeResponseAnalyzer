using System.IO.Compression;
using System.Collections.Concurrent;
using System.Globalization;
using System.Reflection;
using System.Resources;
using Microsoft.CodeAnalysis;
using UmamusumeResponseAnalyzer.Plugin;
using UmamusumeResponseAnalyzer.TerminalGui;
using Xunit;
using i18n = UmamusumeResponseAnalyzer.Localization.PluginRegistry;

namespace UmamusumeResponseAnalyzer.Tests;

[Collection("PluginRuntime")]
public sealed class SharedContextTests : IDisposable
{
    readonly string originalDirectory = Directory.GetCurrentDirectory();
    readonly string testDirectory = Path.Combine(
        Path.GetTempPath(),
        $"ura-manifest-dependencies-{Guid.NewGuid():N}");
    readonly string pluginsDirectory;
    readonly UiHost host;
    readonly ConcurrentQueue<UiLogLine> diagnostics = new();

    public SharedContextTests(PluginRuntimeFixture runtime)
    {
        pluginsDirectory = Path.Combine(testDirectory, "Plugins");
        Directory.CreateDirectory(pluginsDirectory);
        Directory.SetCurrentDirectory(testDirectory);
        host = runtime.Host;
        host.FlushAsync().GetAwaiter().GetResult();
        host.LogAdded += diagnostics.Enqueue;
    }

    public void Dispose()
    {
        try
        {
            PluginManager.ShutdownAsync().GetAwaiter().GetResult();
        }
        finally
        {
            host.LogAdded -= diagnostics.Enqueue;
            Directory.SetCurrentDirectory(originalDirectory);
            try { Directory.Delete(testDirectory, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    [Fact]
    public void MissingManifestDependencyLoadsConsumerAndReportsUnavailable()
    {
        var resultPath = Path.Combine(testDirectory, "availability-result.txt");
        CreatePackage(
            "Member",
            ["Missing"],
            $$"""
            using System.IO;
            using UmamusumeResponseAnalyzer.Plugin;

            public sealed class MemberPlugin : IPlugin
            {
                public void Initialize(IPluginContext context)
                    => File.WriteAllText(@"{{resultPath.Replace("\"", "\"\"")}}", context.IsPluginAvailable("Missing").ToString());
            }
            """);

        RestartPluginManager();
        PluginManager.InitializeLoadedPlugins();

        Assert.Empty(PluginManager.FailedPlugins);
        Assert.Contains(PluginManager.LoadedPlugins, plugin => PluginManager.InternalName(plugin) == "Member");
        Assert.Equal("False", File.ReadAllText(resultPath));
    }

    [Fact]
    public void ManifestDependencyCycleFailsBeforeCreatingLoadContext()
    {
        CreatePackage("CycleA", ["CycleB"]);
        CreatePackage("CycleB", ["CycleA"]);

        var error = Assert.Throws<InvalidDataException>(RestartPluginManager);

        Assert.Contains("CycleA", error.Message, StringComparison.Ordinal);
        Assert.Contains("CycleB", error.Message, StringComparison.Ordinal);
        Assert.Contains("循环", error.Message, StringComparison.Ordinal);
        Assert.Empty(PluginManager.Contexts);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConflictingSharedAssemblyFailsOnlyItsDependencyGroup(bool differentCase)
    {
        var constructedPath = Path.Combine(testDirectory, "conflicted-constructed.txt");
        foreach (var name in new[] { "Anchor", "Member" })
            CreatePackage(name, name == "Member" ? ["Anchor"] : [], $$"""
                using System.IO;
                using UmamusumeResponseAnalyzer.Plugin;

                public sealed class {{name}}Plugin : IPlugin
                {
                    public {{name}}Plugin() => File.AppendAllLines(@"{{constructedPath}}", ["{{name}}"]);
                    public void Initialize(IPluginContext context) { }
                }
                """);

        var firstAssembly = Path.Combine(testDirectory, "first.dll");
        var secondAssembly = Path.Combine(testDirectory, "second.dll");
        PluginCompiler.Compile("public static class SharedLibrary { public const int Value = 1; }",
            "SharedLibrary", firstAssembly);
        if (!differentCase)
            PluginCompiler.Compile("public static class SharedLibrary { public const int Value = 2; }",
                "SharedLibrary", secondAssembly);
        using (var archive = ZipFile.Open(Path.Combine(pluginsDirectory, "Anchor.zip"), ZipArchiveMode.Update))
            archive.CreateEntryFromFile(firstAssembly, "SharedLibrary.dll");
        using (var archive = ZipFile.Open(Path.Combine(pluginsDirectory, "Member.zip"), ZipArchiveMode.Update))
            archive.CreateEntryFromFile(differentCase ? firstAssembly : secondAssembly,
                differentCase ? "sharedlibrary.dll" : "SharedLibrary.dll");

        var initializedPath = Path.Combine(testDirectory, "healthy-initialized.txt");
        CreatePackage("ZuluHealthy", source: $$"""
            using System.IO;
            using UmamusumeResponseAnalyzer.Plugin;

            public sealed class ZuluHealthyPlugin : IPlugin
            {
                public void Initialize(IPluginContext context)
                    => File.WriteAllText(@"{{initializedPath}}", "initialized");
            }
            """);

        RestartPluginManager();
        PluginManager.InitializeLoadedPlugins();
        await host.FlushAsync();

        Assert.Equal("ZuluHealthy", PluginManager.InternalName(Assert.Single(PluginManager.LoadedPlugins)));
        Assert.Equal("ZuluHealthy", Assert.Single(PluginManager.Contexts).Key);
        Assert.Equal("initialized", File.ReadAllText(initializedPath));
        Assert.False(File.Exists(constructedPath));
        Assert.Equal(
            [Path.Combine(pluginsDirectory, "Anchor.zip"), Path.Combine(pluginsDirectory, "Member.zip")],
            PluginManager.FailedPlugins);
        var expectedError = differentCase
            ? string.Format(i18n.AssemblyCaseConflict, "SharedLibrary", "sharedlibrary", "Anchor&Member")
            : string.Format(i18n.AssemblyContentConflict, "SharedLibrary", "Anchor&Member");
        var report = Assert.Single(diagnostics, line => line.Severity == UiSeverity.Error);
        Assert.Contains(expectedError, report.Text);
        foreach (var name in new[] { "Anchor", "Member" })
        {
            Assert.DoesNotContain(PluginManager.SnapshotActivePluginMetadatas(), metadata => metadata.PluginName == name);
            Assert.Contains(name, PluginManager.Metadatas.Keys);
        }
    }

    [Fact]
    public async Task ConstructionFailureKeepsEarlierGroupMemberLoaded()
    {
        var logPath = Path.Combine(testDirectory, "group-lifecycle.txt");
        CreatePackage("Anchor", source: $$"""
            using System.IO;
            using UmamusumeResponseAnalyzer.Plugin;

            public sealed class AnchorPlugin : IPlugin
            {
                public AnchorPlugin() => File.AppendAllLines(@"{{logPath}}", ["constructed"]);
                public void Initialize(IPluginContext context)
                    => File.AppendAllLines(@"{{logPath}}", ["initialized"]);
                public System.Threading.Tasks.ValueTask DisposeAsync()
                {
                    File.AppendAllLines(@"{{logPath}}", ["disposed"]);
                    return System.Threading.Tasks.ValueTask.CompletedTask;
                }
            }
            """);
        CreatePackage("Member", ["Anchor"], """
            using System;
            using UmamusumeResponseAnalyzer.Plugin;

            public sealed class MemberPlugin : IPlugin
            {
                public MemberPlugin() => throw new InvalidOperationException("constructor failed");
                public void Initialize(IPluginContext context) { }
            }
            """);

        RestartPluginManager();
        PluginManager.InitializeLoadedPlugins();
        Assert.Equal("Anchor", PluginManager.InternalName(Assert.Single(PluginManager.LoadedPlugins)));
        Assert.Single(PluginManager.Contexts);
        Assert.Equal(Path.Combine(pluginsDirectory, "Member.zip"), Assert.Single(PluginManager.FailedPlugins));
        await host.FlushAsync();
        var report = Assert.Single(diagnostics, line => line.Severity == UiSeverity.Error);
        Assert.Contains("Member", report.Text);
        Assert.Contains("constructor failed", report.ExceptionDetails);
        await PluginManager.ShutdownAsync();
        Assert.Equal(["constructed", "initialized", "disposed"], File.ReadAllLines(logPath));
    }

    [Fact]
    public void ManifestDependencyLoadsBothPackagesInOneAssemblyLoadContext()
    {
        var resultPath = Path.Combine(testDirectory, "context-result.txt");
        CreatePackage("Anchor");
        CreatePackage(
            "Member",
            ["Anchor"],
            $$"""
            using System.IO;
            using System.Reflection;
            using System.Runtime.Loader;
            using UmamusumeResponseAnalyzer.Plugin;

            public sealed class MemberPlugin : IPlugin
            {
                public void Initialize(IPluginContext context)
                {
                    var ownContext = AssemblyLoadContext.GetLoadContext(GetType().Assembly)!;
                    var dependency = ownContext.LoadFromAssemblyName(new AssemblyName("Anchor"));
                    var dependencyContext = AssemblyLoadContext.GetLoadContext(dependency);
                    File.WriteAllText(
                        @"{{resultPath.Replace("\"", "\"\"")}}",
                        context.IsPluginAvailable("Anchor") && object.ReferenceEquals(ownContext, dependencyContext)
                            ? "available-same"
                            : "unavailable-or-different");
                }
            }
            """);

        RestartPluginManager();
        PluginManager.InitializeLoadedPlugins();

        Assert.Empty(PluginManager.FailedPlugins);
        Assert.Equal("available-same", File.ReadAllText(resultPath));
    }

    [Fact]
    public async Task ConcurrentLazyLoadsShareDependenciesAndSatelliteResources()
    {
        var dependency = Path.Combine(testDirectory, "SharedLibrary.dll");
        PluginCompiler.Compile("public static class SharedLibrary { public static int Value => 7; }", "SharedLibrary", dependency);
        CreatePackage("Concurrent");
        var resourcePath = Path.Combine(testDirectory, "Messages.resources");
        using (var writer = new ResourceWriter(resourcePath))
            writer.AddResource("Message", "并发加载");
        var satellite = Path.Combine(testDirectory, "Concurrent.resources.dll");
        PluginCompiler.Compile("[assembly: System.Reflection.AssemblyCulture(\"zh-CN\")]", "Concurrent.resources", satellite,
            resources: [new ResourceDescription("Messages.zh-CN.resources", () => File.OpenRead(resourcePath), true)]);
        using (var archive = ZipFile.Open(Path.Combine(pluginsDirectory, "Concurrent.zip"), ZipArchiveMode.Update))
        {
            archive.CreateEntryFromFile(dependency, "SharedLibrary.dll");
            archive.CreateEntryFromFile(satellite, "zh-CN/Concurrent.resources.dll");
        }
        RestartPluginManager();
        var context = Assert.Single(PluginManager.Contexts).Value;
        var plugin = Assert.Single(PluginManager.LoadedPlugins);
        using var ready = new CountdownEvent(8);
        using var start = new ManualResetEventSlim();
        var loads = Enumerable.Range(0, 8).Select(_ => Task.Factory.StartNew(() =>
        {
            ready.Signal();
            start.Wait();
            var assembly = context.LoadFromAssemblyName(new AssemblyName("SharedLibrary"));
            var resource = new ResourceManager("Messages", plugin.GetType().Assembly);
            Assert.Equal("并发加载", resource.GetString("Message", CultureInfo.GetCultureInfo("zh-CN")));
            return assembly;
        }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default)).ToArray();
        try
        {
            Assert.True(ready.Wait(TimeSpan.FromSeconds(10)));
        }
        finally
        {
            start.Set();
        }
        var assemblies = await Task.WhenAll(loads).WaitAsync(TimeSpan.FromSeconds(10));
        Assert.All(assemblies, assembly => Assert.Same(assemblies[0], assembly));
        Assert.Equal(7, assemblies[0].GetType("SharedLibrary")!.GetProperty("Value")!.GetValue(null));
    }

    static void RestartPluginManager()
    {
        PluginManager.ShutdownAsync().GetAwaiter().GetResult();
        PluginManager.Init();
    }

    void CreatePackage(
        string internalName,
        IReadOnlyList<string>? dependencies = null,
        string? source = null)
        => PluginCompiler.CompilePackage(
            source ?? $$"""
            using UmamusumeResponseAnalyzer.Plugin;

            public sealed class {{internalName}}Plugin : IPlugin
            {
                public void Initialize(IPluginContext context) { }
            }
            """,
            internalName,
            Path.Combine(pluginsDirectory, $"{internalName}.zip"),
            dependencies);
}
