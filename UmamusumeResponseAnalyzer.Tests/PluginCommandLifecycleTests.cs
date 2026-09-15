using System.Net;
using System.Net.Sockets;
using System.Reflection;
using UmamusumeResponseAnalyzer.Commands;
using UmamusumeResponseAnalyzer.Plugin;
using UmamusumeResponseAnalyzer.TerminalGui;
using WatsonWebserver.Core;
using WatsonWebserver.Lite;
using Xunit;

namespace UmamusumeResponseAnalyzer.Tests;

[Collection("PluginReload")]
public sealed class PluginCommandLifecycleTests : IDisposable
{
    readonly TerminalGuiTestApp terminal;
    readonly UiHost host;
    readonly string originalCwd;
    readonly string tempDir;

    public PluginCommandLifecycleTests(PluginRuntimeFixture runtime)
    {
        terminal = runtime.Terminal;
        host = runtime.Host;
        SeedConfig();
        ResetPluginState();
        HotkeyManager.UnregisterAll();
        HotkeyManager.OverlaySink = runtime.Host;

        originalCwd = Directory.GetCurrentDirectory();
        tempDir = Path.Combine(
            Path.GetTempPath(),
            "ura-plugin-command-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(tempDir, "Plugins"));
        Directory.SetCurrentDirectory(tempDir);
    }

    public void Dispose()
    {
        ResetPluginState();
        HotkeyManager.UnregisterAll();
        Directory.SetCurrentDirectory(originalCwd);
        try { Directory.Delete(tempDir, recursive: true); } catch { }
    }

    [Fact]
    public async Task PluginCommands_LoadUnloadReloadUseRealPluginManager()
    {
        const string pluginName = "CommandLifecycle";
        var pluginPath = PackagePlugin(pluginName, version: 1);
        PluginManager.Init();
        Assert.False(Server.IsRunning, "前置条件:plugin command 在初始插件阶段完成后、HTTP server 启动前执行");
        PluginManager.InitializeLoadedPlugins();
        AssertInitializedAndOpen(pluginName);

        var unload = Assert.IsType<HostCommands.Result>(
            await HostCommands.ExecuteAsync(
                $"/plugin unload {pluginName.ToLowerInvariant()}",
                Snapshot()));
        AssertLifecycleSuccess(unload, pluginName, "卸载");
        Assert.DoesNotContain(
            PluginManager.SnapshotLoadedPlugins(),
            plugin => PluginManager.InternalName(plugin) == pluginName);
        Assert.True(File.Exists(pluginPath));

        var load = Assert.IsType<HostCommands.Result>(
            await HostCommands.ExecuteAsync($"/plugin load {pluginName}", Snapshot()));
        AssertLifecycleSuccess(load, pluginName, "加载");
        AssertInitializedAndOpen(pluginName);

        PackagePlugin(pluginName, version: 2);
        var reload = Assert.IsType<HostCommands.Result>(
            await HostCommands.ExecuteAsync($"/plugin reload {pluginName}", Snapshot()));
        AssertLifecycleSuccess(reload, pluginName, "重载");
        AssertInitializedAndOpen(pluginName);
        Assert.Equal(
            new Version(2, 0),
            Assert.Single(PluginManager.InspectPluginStatuses(), plugin =>
                plugin.InternalName == pluginName).Version);
    }

    [Fact]
    public async Task PluginCommand_PreCancelledMutationDoesNotStart()
    {
        const string pluginName = "CommandCancellation";
        PackagePlugin(pluginName, version: 1);
        PluginManager.Init();
        Assert.False(Server.IsRunning, "前置条件:plugin command 在 HTTP server 启动前执行");
        PluginManager.InitializeLoadedPlugins();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            HostCommands.ExecuteAsync(
                $"/plugin unload {pluginName}",
                Snapshot(),
                new CancellationToken(canceled: true)));

        Assert.Contains(
            PluginManager.SnapshotLoadedPlugins(),
            plugin => PluginManager.InternalName(plugin) == pluginName);
    }

    [Fact]
    public async Task PluginCommand_UnloadFailurePropagatesAfterRealCleanup()
    {
        const string pluginName = "CommandDisposeFailure";
        PackagePlugin(pluginName, version: 1, failDispose: true);
        PluginManager.Init();
        Assert.False(Server.IsRunning, "前置条件:plugin command 在 HTTP server 启动前执行");
        PluginManager.InitializeLoadedPlugins();

        var error = await Assert.ThrowsAsync<AggregateException>(() =>
            HostCommands.ExecuteAsync($"/plugin unload {pluginName}", Snapshot()));

        Assert.Contains(error.InnerExceptions, exception =>
            exception.Message.Contains(pluginName, StringComparison.Ordinal));
        Assert.DoesNotContain(
            PluginManager.SnapshotLoadedPlugins(),
            plugin => PluginManager.InternalName(plugin) == pluginName);

        var load = Assert.IsType<HostCommands.Result>(
            await HostCommands.ExecuteAsync($"/plugin load {pluginName}", Snapshot()));
        AssertLifecycleSuccess(load, pluginName, "加载");

        await Assert.ThrowsAsync<AggregateException>(() =>
            HostCommands.ExecuteAsync($"/plugin unload {pluginName}", Snapshot()));
        Assert.DoesNotContain(
            PluginManager.SnapshotLoadedPlugins(),
            plugin => PluginManager.InternalName(plugin) == pluginName);
    }

    [Fact]
    public async Task PluginCommand_InitializeFailureIsReported()
    {
        const string pluginName = "CommandInitializeFailure";
        PackagePlugin(pluginName, version: 1, failInitialize: true);
        PluginManager.Init();
        PluginManager.InitializeLoadedPlugins();

        var result = Assert.IsType<HostCommands.Result>(
            await HostCommands.ExecuteAsync($"/plugin load {pluginName}", Snapshot()));

        Assert.Equal($"插件 {pluginName} 加载失败。", result.Message);
        Assert.Equal(UiSeverity.Error, result.Severity);
        Assert.DoesNotContain(
            PluginManager.SnapshotLoadedPlugins(),
            plugin => PluginManager.InternalName(plugin) == pluginName);
    }

    [Fact]
    public async Task BlockingPluginCommandKeepsOwnerResponsiveAndPublishesInOrder()
    {
        const string pluginName = "BlockingCommand";
        PackagePlugin(pluginName, version: 1, source: BlockingPluginSource(pluginName));
        PluginManager.Init();
        PluginManager.InitializeLoadedPlugins();
        var plugin = Assert.Single(
            PluginManager.SnapshotLoadedPlugins(),
            candidate => PluginManager.InternalName(candidate) == pluginName);
        var pluginType = plugin.GetType();
        pluginType.GetMethod("BlockDispose")!.Invoke(null, null);

        var unload = host.HandleCommandAsync($"/plugin unload {pluginName}");
        Assert.True((bool)pluginType.GetMethod("WaitUntilBlocked")!.Invoke(
            null,
            [TimeSpan.FromSeconds(5)])!);
        var switchWorkspace = host.HandleCommandAsync(
            "/workspace switch \"Queued snapshot target\"");
        Assert.False(switchWorkspace.IsCompleted);
        var target = Workspace.Create("Queued snapshot target");
        var workspace = Workspace.Create("Responsive command owner");
        try
        {
            await terminal.InvokeAsync(() => workspace.SetPanel(
                "responsive",
                "responsive",
                WorkspaceContent.Text("OwnerResponsiveWhileCommandBlocked"),
                fullBleed: true,
                switchToWorkspace: true));
            await host.FlushAsync().WaitAsync(TimeSpan.FromSeconds(5));
            await terminal.WaitForScreenAsync("OwnerResponsiveWhileCommandBlocked");

            pluginType.GetMethod("ReleaseDispose")!.Invoke(null, null);
            await Task.WhenAll(unload, switchWorkspace).WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Same(target, Workspace.Current);

            await host.FlushAsync();
            await terminal.WaitForScreenAsync($"{pluginName} 已卸载");
        }
        finally
        {
            pluginType.GetMethod("ReleaseDispose")!.Invoke(null, null);
            workspace.Remove();
            target.Remove();
            await host.FlushAsync();
        }

        Assert.DoesNotContain(
            PluginManager.SnapshotLoadedPlugins(),
            plugin => PluginManager.InternalName(plugin) == pluginName);
    }

    [Fact]
    public async Task StartedPhaseDeliversOnceAcrossPreGlobalLoadAndPostGlobalReload()
    {
        const string pluginName = "StartedPhasePlugin";
        PluginManager.Init();
        PluginManager.InitializeLoadedPlugins();

        var originalServer = Server.Instance;
        using var listeningServer = new WebserverLite(
            new WebserverSettings("127.0.0.1", GetFreePort()),
            context => context.Response.Send(string.Empty));
        Server.Instance = listeningServer;
        try
        {
            listeningServer.Start(TestContext.Current.CancellationToken);
            using var listeningTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            while (!Server.IsRunning)
                await Task.Delay(10, listeningTimeout.Token);
            Assert.True(Server.IsRunning);

            PackagePlugin(pluginName, version: 1);
            AssertLifecycleOutcome(await PluginManager.LoadPluginsAsync(pluginName), pluginName);
            AssertInitializedAndOpen(pluginName, startedCalls: 0);

            await PluginManager.TriggerStartedAsync();
            AssertInitializedAndOpen(pluginName, startedCalls: 1);
            await PluginManager.TriggerStartedAsync();
            AssertInitializedAndOpen(pluginName, startedCalls: 1);

            PackagePlugin(pluginName, version: 2);
            AssertLifecycleOutcome(await PluginManager.ReloadPluginsAsync(pluginName), pluginName);
            AssertInitializedAndOpen(pluginName, startedCalls: 1);
            AssertLifecycleOutcome(await PluginManager.UnloadPluginsAsync(pluginName), pluginName);
        }
        finally
        {
            Server.Instance = originalServer;
        }
    }

    HostCommands.Snapshot Snapshot()
        => new(
            [new(host.Bootstrap.Workspace)],
            host.Bootstrap.Workspace,
            PluginManager.InspectPluginStatuses());

    static void AssertLifecycleSuccess(
        HostCommands.Result result,
        string pluginName,
        string action)
    {
        Assert.Equal($"插件 {pluginName} 已{action}。", result.Message);
        Assert.Equal(UiSeverity.Success, result.Severity);
        Assert.Equal("Plugin command", result.Display?.Title);
        Assert.Equal(
            $"{pluginName} 已{action}。",
            Assert.Single(result.Display!.Items).Text);
    }

    static void AssertLifecycleOutcome(
        IReadOnlyList<PluginManager.PluginLifecycleResult> results,
        string pluginName)
    {
        var result = Assert.Single(results);
        Assert.Equal(pluginName, result.PluginName);
        Assert.Equal(PluginManager.PluginLifecycleOutcome.Succeeded, result.Outcome);
    }

    static void AssertInitializedAndOpen(string pluginName, int startedCalls = 0)
    {
        var plugin = Assert.Single(
            PluginManager.SnapshotLoadedPlugins(),
            candidate => PluginManager.InternalName(candidate) == pluginName);
        Assert.True(Assert.IsType<bool>(plugin.GetType().GetProperty("Initialized")!.GetValue(plugin)));
        Assert.Equal(startedCalls, Assert.IsType<int>(plugin.GetType().GetProperty("StartedCalls")!.GetValue(plugin)));
        using var callback = PluginManager.TryEnterPluginCallback(plugin);
        Assert.NotNull(callback);
    }

    static int GetFreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    string PackagePlugin(
        string pluginName,
        int version,
        bool failDispose = false,
        bool failInitialize = false,
        string? source = null)
    {
        var packagePath = Path.Combine(tempDir, "Plugins", $"{pluginName}.zip");
        var pendingPath = Path.Combine(tempDir, $"{pluginName}-{Guid.NewGuid():N}.pending");
        PluginCompiler.CompilePackage(
            source ?? PluginSource(pluginName, failDispose, failInitialize),
            pluginName,
            pendingPath,
            version: $"{version}.0");
        File.Move(pendingPath, packagePath, overwrite: true);
        return packagePath;
    }

    static string PluginSource(
        string pluginName,
        bool failDispose = false,
        bool failInitialize = false)
    {
        var dispose = failDispose
            ? "throw new InvalidOperationException(\"dispose failed\");"
            : string.Empty;
        var initialize = failInitialize
            ? "throw new InvalidOperationException(\"initialize failed\");"
            : """
                Initialized = true;
                context.Events.OnStarted(_ =>
                {
                    StartedCalls++;
                    return ValueTask.CompletedTask;
                });
                """;
        return $$"""
            using System;
            using System.Threading.Tasks;
            using UmamusumeResponseAnalyzer.Plugin;

            public sealed class {{pluginName}} : IPlugin
            {
                public bool Initialized { get; private set; }
                public int StartedCalls { get; private set; }

                public void Initialize(IPluginContext context)
                {
                    {{initialize}}
                }
                public void Dispose() { {{dispose}} }
            }
            """;
    }

    static string BlockingPluginSource(string pluginName)
        => $$"""
            using System;
            using System.Threading;
            using UmamusumeResponseAnalyzer.Plugin;

            public sealed class {{pluginName}} : IPlugin
            {
                static readonly ManualResetEventSlim Entered = new();
                static readonly ManualResetEventSlim Release = new();
                static volatile bool block;

                public void Initialize(IPluginContext context) { }
                public void Dispose()
                {
                    if (block)
                    {
                        Entered.Set();
                        Release.Wait();
                    }
                }
                public static void BlockDispose() => block = true;
                public static bool WaitUntilBlocked(TimeSpan timeout) => Entered.Wait(timeout);
                public static void ReleaseDispose() => Release.Set();
            }
            """;

    static void ResetPluginState()
        => PluginManager.ShutdownAsync().GetAwaiter().GetResult();

    static void SeedConfig()
    {
        var current = typeof(Config).GetProperty(
            "Current",
            BindingFlags.NonPublic | BindingFlags.Static)!;
        if (current.GetValue(null) is null)
        {
            current.SetValue(null, new YamlConfig());
        }
    }
}
