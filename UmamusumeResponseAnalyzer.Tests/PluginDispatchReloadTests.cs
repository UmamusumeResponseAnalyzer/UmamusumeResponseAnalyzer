using i18n = UmamusumeResponseAnalyzer.Localization.PluginRegistry;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using Terminal.Gui.Input;
using UmamusumeResponseAnalyzer.Plugin;
using UmamusumeResponseAnalyzer.TerminalGui;
using WatsonWebserver.Lite;
using Xunit;

namespace UmamusumeResponseAnalyzer.Tests;

[Collection("PluginReload")]
public sealed class PluginDispatchReloadTests : IDisposable
{
    const string PluginName = "DisposeBarrierPlugin";
    const string StartedPluginName = "StartedBarrierPlugin";
    const string CallbackFriendPluginName = "PluginRuntimeSmoke";
    const string AccountIndexPath = "/umamusume/account/index";
    const string CreatedPhaseScenario = "dispatch-reload-created-phase";

    readonly string originalCwd = Directory.GetCurrentDirectory();
    readonly string tempDir = Path.Combine(Path.GetTempPath(), "ura-dispatch-reload-" + Guid.NewGuid().ToString("N"));
    readonly string disposeLog;
    readonly string disposeEntered;
    readonly string disposeRelease;
    readonly string callbackLog;
    readonly string startedDisposeEntered;
    readonly string startedDisposeRelease;
    readonly string startedDisposeLog;
    readonly string startedCallbackLog;

    public PluginDispatchReloadTests(PluginRuntimeFixture runtime)
    {
        SeedConfig();
        if (!string.Equals(
                Environment.GetEnvironmentVariable(TerminalUiLifecycleChildProcess.ScenarioEnvironmentVariable),
                CreatedPhaseScenario,
                StringComparison.Ordinal))
            PluginManager.ShutdownAsync().GetAwaiter().GetResult();
        HotkeyManager.UnregisterAll();
        HotkeyManager.OverlaySink = runtime.Host;

        Directory.CreateDirectory(Path.Combine(tempDir, "Plugins"));
        Directory.SetCurrentDirectory(tempDir);
        disposeLog = Path.Combine(tempDir, "dispose.log");
        disposeEntered = Path.Combine(tempDir, "dispose-entered");
        disposeRelease = Path.Combine(tempDir, "dispose-release");
        callbackLog = Path.Combine(tempDir, "callback.log");
        startedDisposeEntered = Path.Combine(tempDir, "started-dispose-entered");
        startedDisposeRelease = Path.Combine(tempDir, "started-dispose-release");
        startedDisposeLog = Path.Combine(tempDir, "started-dispose.log");
        startedCallbackLog = Path.Combine(tempDir, "started-callback.log");
    }

    public void Dispose()
    {
        foreach (var release in new[]
                 {
                     disposeRelease,
                     startedDisposeRelease,
                     Path.Combine(tempDir, "snapshot-release"),
                     Path.Combine(tempDir, "background-release"),
                 })
            File.WriteAllText(release, "release");

        try
        {
            PluginManager.ShutdownAsync().GetAwaiter().GetResult();
        }
        finally
        {
            HotkeyManager.UnregisterAll();
            Directory.SetCurrentDirectory(originalCwd);
            try { Directory.Delete(tempDir, recursive: true); }
            catch { }
        }
    }

    [Fact]
    public async Task ReloadClosesAdmissionBeforeDisposeAndReplacesGeneration()
    {
        const string scenario = "dispatch-reload-dispose-barrier";
        if (TerminalUiLifecycleChildProcess.IsChild(scenario))
        {
            await ReloadWhileDisposeIsBlockedAsync();
            TerminalUiLifecycleChildProcess.WriteResult("ok");
            return;
        }

        Assert.Equal(
            "ok",
            await TerminalUiLifecycleProcessTests.RunChildAsync(
                scenario,
                typeof(PluginDispatchReloadTests),
                nameof(ReloadClosesAdmissionBeforeDisposeAndReplacesGeneration)));
    }

    [Fact]
    public async Task AnalyzerSnapshotLeasesLaterGenerationUntilBlockedCallbackCompletes()
    {
        const string scenario = "dispatch-reload-snapshot-lease";
        if (TerminalUiLifecycleChildProcess.IsChild(scenario))
        {
            await UnloadSnapshottedAnalyzerBehindBlockedCallbackAsync();
            TerminalUiLifecycleChildProcess.WriteResult("ok");
            return;
        }

        Assert.Equal(
            "ok",
            await TerminalUiLifecycleProcessTests.RunChildAsync(
                scenario,
                typeof(PluginDispatchReloadTests),
                nameof(AnalyzerSnapshotLeasesLaterGenerationUntilBlockedCallbackCompletes)));
    }

    [Fact]
    public async Task SamePriorityDispatchPreservesPackageAndDeclarationOrder()
    {
        const string scenario = "dispatch-reload-stable-order";
        if (TerminalUiLifecycleChildProcess.IsChild(scenario))
        {
            var log = Path.Combine(tempDir, "stable-order.log");
            CompilePackage(StableOrderPluginSource("AOrderPlugin", log));
            CompilePackage(StableOrderPluginSource("BOrderPlugin", log));
            PluginManager.Init();
            PluginManager.InitializeLoadedPlugins();

            await WithServerAsync(port => PostPacketAsync(port, AnalyzerKind.Response, AccountIndexPath, [0xC0]));

            Assert.Equal(["AOrderPlugin:1", "AOrderPlugin:2", "BOrderPlugin:1", "BOrderPlugin:2"], File.ReadAllLines(log));
            TerminalUiLifecycleChildProcess.WriteResult("ok");
            return;
        }

        Assert.Equal(
            "ok",
            await TerminalUiLifecycleProcessTests.RunChildAsync(
                scenario,
                typeof(PluginDispatchReloadTests),
                nameof(SamePriorityDispatchPreservesPackageAndDeclarationOrder)));
    }

    [Fact]
    public async Task FailedInitializePublishesNeitherAnalyzerNorBackgroundOperation()
    {
        const string scenario = "dispatch-reload-initialize-atomic";
        if (TerminalUiLifecycleChildProcess.IsChild(scenario))
        {
            const string pluginName = "AtomicInitializePlugin";
            var analyzerLog = Path.Combine(tempDir, "atomic-initialize-analyzer.log");
            var backgroundLog = Path.Combine(tempDir, "atomic-initialize-background.log");
            CompilePackage(AtomicInitializePluginSource(pluginName, analyzerLog, backgroundLog));
            PluginManager.Init();
            PluginManager.InitializeLoadedPlugins();

            var status = Assert.Single(
                PluginManager.InspectPluginStatuses(),
                candidate => candidate.InternalName == pluginName);
            Assert.False(status.IsLoaded);
            Assert.True(status.IsAvailable);
            await WithServerAsync(port => PostPacketAsync(port, AnalyzerKind.Response, AccountIndexPath, [0xC0]));
            Assert.False(File.Exists(analyzerLog));
            Assert.False(File.Exists(backgroundLog));

            TerminalUiLifecycleChildProcess.WriteResult("ok");
            return;
        }

        Assert.Equal(
            "ok",
            await TerminalUiLifecycleProcessTests.RunChildAsync(
                scenario,
                typeof(PluginDispatchReloadTests),
                nameof(FailedInitializePublishesNeitherAnalyzerNorBackgroundOperation)));
    }

    [Fact]
    public async Task FailedStartedCallbackPublishesNeitherAnalyzerNorBackgroundOperation()
    {
        const string scenario = "dispatch-reload-started-atomic";
        if (TerminalUiLifecycleChildProcess.IsChild(scenario))
        {
            const string pluginName = "AtomicStartedPlugin";
            var analyzerLog = Path.Combine(tempDir, "atomic-started-analyzer.log");
            var backgroundLog = Path.Combine(tempDir, "atomic-started-background.log");
            CompilePackage(AtomicStartedPluginSource(pluginName, analyzerLog, backgroundLog));
            PluginManager.Init();
            PluginManager.InitializeLoadedPlugins();
            await PluginManager.TriggerStartedAsync();

            await WithServerAsync(port => PostPacketAsync(port, AnalyzerKind.Response, AccountIndexPath, [0xC0]));
            Assert.False(File.Exists(analyzerLog));
            Assert.False(File.Exists(backgroundLog));

            TerminalUiLifecycleChildProcess.WriteResult("ok");
            return;
        }

        Assert.Equal(
            "ok",
            await TerminalUiLifecycleProcessTests.RunChildAsync(
                scenario,
                typeof(PluginDispatchReloadTests),
                nameof(FailedStartedCallbackPublishesNeitherAnalyzerNorBackgroundOperation)));
    }

    [Fact]
    public async Task UnloadCancelsAndDrainsBackgroundBeforeDispose()
    {
        const string pluginName = "BackgroundBarrierPlugin";
        var entered = Path.Combine(tempDir, "background-entered");
        var release = Path.Combine(tempDir, "background-release");
        var lifecycle = Path.Combine(tempDir, "background-lifecycle.log");
        CompilePackage(BackgroundBarrierPluginSource(pluginName, entered, release, lifecycle));
        PluginManager.Init();
        PluginManager.InitializeLoadedPlugins();
        await WaitUntilAsync(() => File.Exists(entered));

        var unload = PluginManager.UnloadPluginsAsync(pluginName);
        await WaitUntilAsync(() => File.Exists(lifecycle) &&
                                   File.ReadAllLines(lifecycle).Contains("background-canceled"));

        Assert.False(unload.IsCompleted);
        Assert.DoesNotContain("disposed", File.ReadAllLines(lifecycle));
        File.WriteAllText(release, "release");

        AssertLifecycleOutcome(await unload.WaitAsync(TimeSpan.FromSeconds(5)), pluginName);
        Assert.Equal(
            ["background-entered", "background-canceled", "background-drained", "disposed"],
            File.ReadAllLines(lifecycle));
    }

    [Fact]
    public async Task StartedEventIsRejectedWhileUnloadDisposeIsBlocked()
    {
        CompilePackage(StartedPluginSource());
        PluginManager.Init();
        PluginManager.InitializeLoadedPlugins();
        var plugin = Assert.Single(
            PluginManager.LoadedPlugins,
            plugin => PluginManager.InternalName(plugin) == StartedPluginName);

        var unload = Task.Run(async () => await PluginManager.UnloadPluginsAsync(StartedPluginName));
        Exception? observationError = null;
        Exception? lateStartedError = null;
        var unloadWaited = false;
        string[] callbackOutput = [];
        try
        {
            await WaitUntilAsync(() => File.Exists(startedDisposeEntered));
            unloadWaited = !unload.IsCompleted;
            lateStartedError = await Record.ExceptionAsync(async () =>
                await PluginManager.TriggerStartedForPluginsAsync([plugin])
                    .WaitAsync(TimeSpan.FromSeconds(5)));
            callbackOutput = File.Exists(startedCallbackLog)
                ? File.ReadAllLines(startedCallbackLog)
                : [];
        }
        catch (Exception ex)
        {
            observationError = ex;
        }
        finally
        {
            File.WriteAllText(startedDisposeRelease, "release");
        }

        var unloadResults = await unload.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Null(observationError);
        Assert.True(unloadWaited, "plugin Dispose 尚未返回时 unload 不应完成");
        Assert.IsType<InvalidOperationException>(lateStartedError);
        Assert.Empty(callbackOutput);
        AssertLifecycleOutcome(unloadResults, StartedPluginName);
        Assert.Equal("disposed", File.ReadAllText(startedDisposeLog));
    }

    [Fact]
    public async Task BatchUnloadContinuesAfterPluginDisposeFailure()
    {
        const string failingName = "FailingDisposePlugin";
        const string healthyName = "HealthyDisposePlugin";
        var failingLog = Path.Combine(tempDir, "failing-dispose.log");
        var healthyLog = Path.Combine(tempDir, "healthy-dispose.log");
        CompilePackage(DisposePluginSource(failingName, failingLog, fail: true));
        CompilePackage(DisposePluginSource(healthyName, healthyLog, fail: false));
        PluginManager.Init();
        PluginManager.InitializeLoadedPlugins();

        var context = new WeakReference(CapturePluginLoadContext(failingName));
        var error = await Assert.ThrowsAsync<AggregateException>(
            () => PluginManager.UnloadPluginsAsync(failingName, healthyName));

        var failure = Assert.Single(error.InnerExceptions, ex => ex.Message.Contains(failingName, StringComparison.Ordinal));
        while (failure.InnerException is { } inner)
            failure = inner;
        Assert.IsType<InvalidOperationException>(failure);
        Assert.Contains(@"C:\plugins\dispose\state.bin", failure.Message);
        Assert.Contains("FailingDisposePluginNs.Plugin.Dispose()", failure.Message);
        Assert.Equal(["disposed"], File.ReadAllLines(failingLog));
        Assert.Equal(["disposed"], File.ReadAllLines(healthyLog));
        Assert.DoesNotContain(PluginManager.SnapshotLoadedPlugins(), plugin =>
            PluginManager.InternalName(plugin) is failingName or healthyName);
        for (var i = 0; context.IsAlive && i < 20; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            await Task.Delay(20, TestContext.Current.CancellationToken);
        }
        Assert.False(context.IsAlive, "Dispose diagnostics must not retain the plugin exception or ALC.");
    }

    [Fact]
    public async Task InitParticipatesInLifecycleTransaction()
    {
        const string pluginName = "BlockingConstructorPlugin";
        var constructorEntered = Path.Combine(tempDir, "constructor-entered");
        var constructorRelease = Path.Combine(tempDir, "constructor-release");
        CompilePackage(BlockingConstructorPluginSource(pluginName, constructorEntered, constructorRelease));

        var initialize = Task.Run(() => PluginManager.Init());
        try
        {
            await WaitUntilAsync(() => File.Exists(constructorEntered));
            var secondInit = Assert.Throws<InvalidOperationException>(() => PluginManager.Init());
            Assert.Equal(i18n.InitializationTransactionBusy, secondInit.Message);
            var unload = await Assert.ThrowsAsync<InvalidOperationException>(
                () => PluginManager.UnloadPluginsAsync(pluginName));
            Assert.Equal(i18n.LifecycleTransactionBusy, unload.Message);
        }
        finally
        {
            File.WriteAllText(constructorRelease, "release");
        }

        await initialize.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task InitializeLoadedPluginsExcludesConcurrentUnloadAndDisposesOnce()
    {
        const string pluginName = "BlockingInitializePlugin";
        var initializeEntered = Path.Combine(tempDir, "initialize-entered");
        var initializeRelease = Path.Combine(tempDir, "initialize-release");
        var disposeOutput = Path.Combine(tempDir, "initialize-dispose.log");
        CompilePackage(BlockingInitializePluginSource(pluginName, initializeEntered, initializeRelease, disposeOutput));
        PluginManager.Init();

        var initialize = Task.Run(PluginManager.InitializeLoadedPlugins);
        Exception? concurrentUnloadError = null;
        try
        {
            await WaitUntilAsync(() => File.Exists(initializeEntered));
            concurrentUnloadError = await Record.ExceptionAsync(
                () => PluginManager.UnloadPluginsAsync(pluginName));
            Assert.False(File.Exists(disposeOutput));
        }
        finally
        {
            File.WriteAllText(initializeRelease, "release");
        }

        await initialize.WaitAsync(TimeSpan.FromSeconds(5));
        var transactionError = Assert.IsType<InvalidOperationException>(concurrentUnloadError);
        Assert.Equal(i18n.LifecycleTransactionBusy, transactionError.Message);

        AssertLifecycleOutcome(await PluginManager.UnloadPluginsAsync(pluginName), pluginName);
        Assert.Equal(["disposed"], File.ReadAllLines(disposeOutput));
    }

    [Fact]
    public async Task PluginOwnedHotkeyInFlightBlocksUnloadAndCloseRejectsNewCallback()
    {
        const string pluginName = "HotkeyBarrierPlugin";
        var callbackEntered = Path.Combine(tempDir, "hotkey-callback-entered");
        var callbackRelease = Path.Combine(tempDir, "hotkey-callback-release");
        var lifecycleLog = Path.Combine(tempDir, "hotkey-lifecycle.log");
        CompilePackage(HotkeyBarrierPluginSource(pluginName, callbackEntered, callbackRelease, lifecycleLog));
        PluginManager.Init();
        Assert.False(Server.IsRunning, "前置条件:Hotkey unload barrier 测试中 HTTP server 未启动");
        PluginManager.InitializeLoadedPlugins();
        var plugin = Assert.Single(
            PluginManager.SnapshotLoadedPlugins(),
            candidate => PluginManager.InternalName(candidate) == pluginName);
        var entry = HotkeyManager.Hotkeys[(ConsoleKey.F6, 0)];

        var callback = Task.Run(async () =>
        {
            using var lease = PluginManager.EnterPluginCallback(plugin);
            using var ownerScope = HotkeyManager.RegisterScope(plugin);
            await entry.Handler();
        });
        await WaitUntilAsync(() => File.Exists(callbackEntered));
        var unload = Task.Run(() => PluginManager.UnloadPluginsAsync(pluginName));
        await WaitUntilAsync(() => !PluginManager.InspectPluginStatuses()
            .Single(status => status.InternalName == pluginName)
            .IsLoaded);

        Assert.True(await HotkeyManager.HandleKeyAsync(Key.F6).WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.False(unload.IsCompleted);

        File.WriteAllText(callbackRelease, "release");
        await callback.WaitAsync(TimeSpan.FromSeconds(5));
        AssertLifecycleOutcome(await unload.WaitAsync(TimeSpan.FromSeconds(5)), pluginName);
        Assert.Equal(
            ["callback-entered", "callback-released", "disposed"],
            File.ReadAllLines(lifecycleLog));
        Assert.False(await HotkeyManager.HandleKeyAsync(Key.F6));
    }

    [Fact]
    public async Task CreatedPhaseAllowsOnlyInitAndRejectsOtherLifecycleOperations()
    {
        if (TerminalUiLifecycleChildProcess.IsChild(CreatedPhaseScenario))
        {
            const string pluginName = "CreatedPhasePlugin";
            CompilePackage(NoopPluginSource(pluginName));

            AssertPhaseFailure(
                Assert.Throws<InvalidOperationException>(PluginManager.InitializeLoadedPlugins),
                "Created");
            AssertPhaseFailure(
                await Assert.ThrowsAsync<InvalidOperationException>(() => PluginManager.TriggerStartedAsync()),
                "Created");
            AssertHotLifecyclePhaseFailure(
                await Assert.ThrowsAsync<InvalidOperationException>(() => PluginManager.LoadPluginsAsync(pluginName)),
                "Created");
            AssertHotLifecyclePhaseFailure(
                await Assert.ThrowsAsync<InvalidOperationException>(() => PluginManager.ReloadPluginsAsync(pluginName)),
                "Created");
            AssertHotLifecyclePhaseFailure(
                await Assert.ThrowsAsync<InvalidOperationException>(() => PluginManager.UnloadPluginsAsync(pluginName)),
                "Created");

            PluginManager.Init();
            Assert.Contains(
                PluginManager.SnapshotLoadedPlugins(),
                plugin => PluginManager.InternalName(plugin) == pluginName);
            TerminalUiLifecycleChildProcess.WriteResult("ok");
            return;
        }

        Assert.Equal(
            "ok",
            await TerminalUiLifecycleProcessTests.RunChildAsync(
                CreatedPhaseScenario,
                typeof(PluginDispatchReloadTests),
                nameof(CreatedPhaseAllowsOnlyInitAndRejectsOtherLifecycleOperations)));
    }

    [Fact]
    public async Task LifecyclePhasesAreMonotonicAndIdempotent()
    {
        const string scenario = "dispatch-reload-phase-contract";
        if (!TerminalUiLifecycleChildProcess.IsChild(scenario))
        {
            Assert.Equal(
                "ok",
                await TerminalUiLifecycleProcessTests.RunChildAsync(
                    scenario,
                    typeof(PluginDispatchReloadTests),
                    nameof(LifecyclePhasesAreMonotonicAndIdempotent)));
            return;
        }

        const string pluginName = "PhaseContractPlugin";
        var lifecycleLog = Path.Combine(tempDir, "phase-contract.log");
        CompilePackage(PhaseContractPluginSource(pluginName, lifecycleLog));

        PluginManager.Init();
        AssertPhaseFailure(Assert.Throws<InvalidOperationException>(() => PluginManager.Init()), "Loaded");
        AssertLifecycleOutcome(await PluginManager.LoadPluginsAsync(pluginName), pluginName);
        AssertLifecycleOutcome(await PluginManager.ReloadPluginsAsync(pluginName), pluginName);
        AssertLifecycleOutcome(await PluginManager.UnloadPluginsAsync(pluginName), pluginName);
        AssertLifecycleOutcome(await PluginManager.LoadPluginsAsync(pluginName), pluginName);
        AssertPhaseFailure(
            await Assert.ThrowsAsync<InvalidOperationException>(() => PluginManager.TriggerStartedAsync()),
            "Loaded");

        PluginManager.InitializeLoadedPlugins();
        AssertPhaseFailure(Assert.Throws<InvalidOperationException>(() => PluginManager.Init()), "Initialized");
        PluginManager.InitializeLoadedPlugins();
        Assert.Equal(["initialize"], File.ReadAllLines(lifecycleLog));

        await PluginManager.TriggerStartedAsync();
        await PluginManager.TriggerStartedAsync();
        AssertPhaseFailure(Assert.Throws<InvalidOperationException>(() => PluginManager.Init()), "Started");
        PluginManager.InitializeLoadedPlugins();
        await PluginManager.TriggerStartedAsync();
        Assert.Equal(["initialize", "started"], File.ReadAllLines(lifecycleLog));

        await PluginManager.ShutdownAsync();
        Assert.Empty(PluginManager.SnapshotLoadedPlugins());
        Assert.Empty(PluginManager.Metadatas);
        AssertPhaseFailure(
            Assert.Throws<InvalidOperationException>(PluginManager.InitializeLoadedPlugins),
            "Stopped");
        AssertPhaseFailure(
            await Assert.ThrowsAsync<InvalidOperationException>(() => PluginManager.TriggerStartedAsync()),
            "Stopped");
        AssertHotLifecyclePhaseFailure(
            await Assert.ThrowsAsync<InvalidOperationException>(() => PluginManager.LoadPluginsAsync(pluginName)),
            "Stopped");
        AssertHotLifecyclePhaseFailure(
            await Assert.ThrowsAsync<InvalidOperationException>(() => PluginManager.ReloadPluginsAsync(pluginName)),
            "Stopped");
        AssertHotLifecyclePhaseFailure(
            await Assert.ThrowsAsync<InvalidOperationException>(() => PluginManager.UnloadPluginsAsync(pluginName)),
            "Stopped");

        PluginManager.Init();
        Assert.Contains(
            PluginManager.SnapshotLoadedPlugins(),
            plugin => PluginManager.InternalName(plugin) == pluginName);
        TerminalUiLifecycleChildProcess.WriteResult("ok");
    }

    [Fact]
    public async Task ShuttingDownPhaseRejectsOtherLifecycleOperations()
    {
        const string scenario = "dispatch-reload-shutting-down-phase";
        if (!TerminalUiLifecycleChildProcess.IsChild(scenario))
        {
            Assert.Equal(
                "ok",
                await TerminalUiLifecycleProcessTests.RunChildAsync(
                    scenario,
                    typeof(PluginDispatchReloadTests),
                    nameof(ShuttingDownPhaseRejectsOtherLifecycleOperations)));
            return;
        }

        CompilePackage(PluginSource());
        PluginManager.Init();
        PluginManager.InitializeLoadedPlugins();

        var shutdown = Task.Run(PluginManager.ShutdownAsync);
        try
        {
            await WaitUntilAsync(() => File.Exists(disposeEntered));
            AssertPhaseFailure(Assert.Throws<InvalidOperationException>(() => PluginManager.Init()), "ShuttingDown");
            AssertPhaseFailure(
                Assert.Throws<InvalidOperationException>(PluginManager.InitializeLoadedPlugins),
                "ShuttingDown");
            AssertPhaseFailure(
                await Assert.ThrowsAsync<InvalidOperationException>(() => PluginManager.TriggerStartedAsync()),
                "ShuttingDown");
            AssertHotLifecyclePhaseFailure(
                await Assert.ThrowsAsync<InvalidOperationException>(() => PluginManager.LoadPluginsAsync(PluginName)),
                "ShuttingDown");
            AssertHotLifecyclePhaseFailure(
                await Assert.ThrowsAsync<InvalidOperationException>(() => PluginManager.ReloadPluginsAsync(PluginName)),
                "ShuttingDown");
            AssertHotLifecyclePhaseFailure(
                await Assert.ThrowsAsync<InvalidOperationException>(() => PluginManager.UnloadPluginsAsync(PluginName)),
                "ShuttingDown");
        }
        finally
        {
            File.WriteAllText(disposeRelease, "release");
        }

        await shutdown.WaitAsync(TimeSpan.FromSeconds(5));
        TerminalUiLifecycleChildProcess.WriteResult("ok");
    }

    [Fact]
    public async Task AnalyzerCallbackAwaitingSelfUnloadFailsFastWithoutDeadlock()
    {
        const string scenario = "dispatch-reload-self-unload";
        if (TerminalUiLifecycleChildProcess.IsChild(scenario))
        {
            await AssertAnalyzerCallbackLifecycleReentryFailsFastAsync(shutdown: false);
            TerminalUiLifecycleChildProcess.WriteResult("ok");
            return;
        }

        Assert.Equal(
            "ok",
            await TerminalUiLifecycleProcessTests.RunChildAsync(
                scenario,
                typeof(PluginDispatchReloadTests),
                nameof(AnalyzerCallbackAwaitingSelfUnloadFailsFastWithoutDeadlock)));
    }

    [Fact]
    public async Task AnalyzerCallbackAwaitingShutdownFailsFastWithoutDeadlock()
    {
        const string scenario = "dispatch-reload-self-shutdown";
        if (TerminalUiLifecycleChildProcess.IsChild(scenario))
        {
            await AssertAnalyzerCallbackLifecycleReentryFailsFastAsync(shutdown: true);
            TerminalUiLifecycleChildProcess.WriteResult("ok");
            return;
        }

        Assert.Equal(
            "ok",
            await TerminalUiLifecycleProcessTests.RunChildAsync(
                scenario,
                typeof(PluginDispatchReloadTests),
                nameof(AnalyzerCallbackAwaitingShutdownFailsFastWithoutDeadlock)));
    }

    async Task AssertAnalyzerCallbackLifecycleReentryFailsFastAsync(bool shutdown)
    {
        var failureLog = Path.Combine(tempDir, shutdown ? "self-shutdown.log" : "self-unload.log");
        CompilePackage(SelfLifecyclePluginSource(failureLog, shutdown));
        PluginManager.Init();
        PluginManager.InitializeLoadedPlugins();

        await WithServerAsync(port => PostPacketAsync(
                port,
                AnalyzerKind.Response,
                AccountIndexPath,
                [0xC0]))
            .WaitAsync(TimeSpan.FromSeconds(5));

        var failure = File.ReadAllLines(failureLog);
        Assert.True(failure.Length >= 2, $"callback lifecycle 结果不完整: {string.Join(" | ", failure)}");
        Assert.Equal(typeof(InvalidOperationException).FullName, failure[0]);
        Assert.Equal(i18n.CallbackLifecycleReentry, failure[1]);
        Assert.Contains(
            PluginManager.SnapshotLoadedPlugins(),
            plugin => PluginManager.InternalName(plugin) == CallbackFriendPluginName);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    async Task UnloadSnapshottedAnalyzerBehindBlockedCallbackAsync()
    {
        const string blockingName = "SnapshotBlockingAnalyzer";
        const string laterName = "SnapshotLaterAnalyzer";
        var entered = Path.Combine(tempDir, "snapshot-entered");
        var release = Path.Combine(tempDir, "snapshot-release");
        var lifecycle = Path.Combine(tempDir, "snapshot-lifecycle.log");
        CompilePackage(SnapshotAnalyzerPluginSource(blockingName, lifecycle, priority: -10, entered, release));
        CompilePackage(SnapshotAnalyzerPluginSource(laterName, lifecycle, priority: 10));
        PluginManager.Init();
        PluginManager.InitializeLoadedPlugins();
        await WithServerAsync(async port =>
        {
            var dispatch = PostPacketAsync(port, AnalyzerKind.Response, AccountIndexPath, [0xC0]);
            await WaitUntilAsync(() => File.Exists(entered));
            var unload = Task.Run(() => PluginManager.UnloadPluginsAsync(laterName));
            Assert.False(unload.IsCompleted);
            Assert.Equal([$"{blockingName}:entered"], File.ReadAllLines(lifecycle));

            File.WriteAllText(release, "release");
            await dispatch.WaitAsync(TimeSpan.FromSeconds(5));
            AssertLifecycleOutcome(await unload.WaitAsync(TimeSpan.FromSeconds(5)), laterName);
            Assert.Equal(
                [
                    $"{blockingName}:entered",
                    $"{blockingName}:released",
                    $"{laterName}:callback",
                    $"{laterName}:dispose",
                ],
                File.ReadAllLines(lifecycle));
        });
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    async Task ReloadWhileDisposeIsBlockedAsync()
    {
        CompilePackage(PluginSource());
        PluginManager.Init();
        PluginManager.InitializeLoadedPlugins();

        var initialStatus = Assert.Single(
            PluginManager.InspectPluginStatuses(),
            status => status.InternalName == PluginName);
        Assert.True(initialStatus.IsLoaded);
        var oldContext = CapturePluginLoadContext(PluginName);

        await WithServerAsync(async port =>
        {
            await PostPacketAsync(port, AnalyzerKind.Response, AccountIndexPath, [0xC0]);
            Assert.Equal(["entered"], File.ReadAllLines(callbackLog));

            var reload = Task.Run(() => PluginManager.ReloadPluginsAsync(PluginName));
            var reloadWaited = false;
            Exception? observationError = null;
            Exception? lateDispatchError = null;
            string[] callbacksWhileDisposing = [];
            PluginManager.PluginRuntimeStatus? statusWhileDisposing = null;
            try
            {
                await WaitUntilAsync(() => File.Exists(disposeEntered));
                reloadWaited = !reload.IsCompleted;
                statusWhileDisposing = Assert.Single(
                    PluginManager.InspectPluginStatuses(),
                    status => status.InternalName == PluginName);
                lateDispatchError = await Record.ExceptionAsync(async () =>
                    await PostPacketAsync(port, AnalyzerKind.Response, AccountIndexPath, [0xC0])
                        .WaitAsync(TimeSpan.FromSeconds(5)));
                callbacksWhileDisposing = File.ReadAllLines(callbackLog);
            }
            catch (Exception ex)
            {
                observationError = ex;
            }
            finally
            {
                File.WriteAllText(disposeRelease, "release");
            }

            IReadOnlyList<PluginManager.PluginLifecycleResult>? reloadResults = null;
            var reloadError = await Record.ExceptionAsync(async () =>
            {
                reloadResults = await reload.WaitAsync(TimeSpan.FromSeconds(5));
            });

            Assert.Null(observationError);
            Assert.True(reloadWaited, "plugin Dispose 尚未返回时 reload 不应完成");
            Assert.NotNull(statusWhileDisposing);
            Assert.False(statusWhileDisposing.IsLoaded);
            Assert.Null(lateDispatchError);
            Assert.Equal(["entered"], callbacksWhileDisposing);
            Assert.IsNotType<SynchronizationLockException>(reloadError);
            Assert.Null(reloadError);
            AssertLifecycleOutcome(reloadResults!, PluginName);
            Assert.Equal("disposed", File.ReadAllText(disposeLog));
        });
        Assert.NotSame(oldContext, CapturePluginLoadContext(PluginName));
    }

    async Task WithServerAsync(Func<int, Task> action)
    {
        var port = GetFreeTcpPort();
        Server.Instance = new(
            new WatsonWebserver.Core.WebserverSettings("127.0.0.1", port),
            context => context.Response.Send(string.Empty));
        Server.Start(TestContext.Current.CancellationToken);
        try
        {
            await action(port);
        }
        finally
        {
            await Server.StopAsync().WaitAsync(TimeSpan.FromSeconds(5));
            Server.Instance = new(
                new WatsonWebserver.Core.WebserverSettings("127.0.0.1", GetFreeTcpPort()),
                context => context.Response.Send(string.Empty));
        }
    }

    static async Task PostPacketAsync(
        int port,
        AnalyzerKind kind,
        string canonicalUrl,
        byte[] payload)
    {
        using var client = new HttpClient();
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"http://127.0.0.1:{port}/notify/{kind.ToString().ToLowerInvariant()}");
        request.Headers.Add("X-Hachimi-Game-Url", canonicalUrl);
        request.Content = new ByteArrayContent(payload);
        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
    }

    static int GetFreeTcpPort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static AssemblyLoadContext CapturePluginLoadContext(string pluginName)
    {
        var plugin = Assert.Single(
            PluginManager.SnapshotLoadedPlugins(),
            candidate => PluginManager.InternalName(candidate) == pluginName);
        return AssemblyLoadContext.GetLoadContext(plugin.GetType().Assembly)!;
    }

    void CompilePackage((string Name, string Source) plugin)
        => PluginCompiler.CompilePackage(
            plugin.Source,
            plugin.Name,
            Path.Combine(tempDir, "Plugins", $"{plugin.Name}.zip"));

    static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!condition())
            await Task.Delay(10, cancellation.Token);
    }

    static void AssertLifecycleOutcome(
        IReadOnlyList<PluginManager.PluginLifecycleResult> results,
        string pluginName)
    {
        var result = Assert.Single(results);
        Assert.Equal(pluginName, result.PluginName);
        Assert.Equal(PluginManager.PluginLifecycleOutcome.Succeeded, result.Outcome);
    }

    static void AssertPhaseFailure(InvalidOperationException failure, string phase)
        => Assert.Contains($"phase={phase}", failure.Message, StringComparison.Ordinal);

    static void AssertHotLifecyclePhaseFailure(InvalidOperationException failure, string phase)
    {
        AssertPhaseFailure(failure, phase);
        Assert.Equal(string.Format(i18n.LifecyclePhaseInvalid, phase, "load/reload/unload"), failure.Message);
    }

    static (string Name, string Source) NoopPluginSource(string pluginName) => (pluginName, $$"""
        using UmamusumeResponseAnalyzer.Plugin;

        namespace {{pluginName}}Ns;

        public sealed class Plugin : IPlugin
        {
            public void Initialize(IPluginContext context) { }
        }
        """);

    static (string Name, string Source) PhaseContractPluginSource(string pluginName, string lifecycleLog) =>
        (pluginName, $$"""
        using System;
        using System.IO;
        using System.Threading.Tasks;
        using UmamusumeResponseAnalyzer.Plugin;

        namespace {{pluginName}}Ns;

        public sealed class Plugin : IPlugin
        {
            public void Initialize(IPluginContext context)
            {
                File.AppendAllText(@"{{lifecycleLog}}", "initialize" + Environment.NewLine);
                context.Events.OnStarted(_ =>
                {
                    File.AppendAllText(@"{{lifecycleLog}}", "started" + Environment.NewLine);
                    return ValueTask.CompletedTask;
                });
            }
        }
        """);

    static (string Name, string Source) SelfLifecyclePluginSource(string failureLog, bool shutdown)
    {
        var lifecycleCall = shutdown
            ? "await PluginManager.ShutdownAsync();"
            : $"await PluginManager.UnloadPluginsAsync(\"{CallbackFriendPluginName}\");";
        return (CallbackFriendPluginName, $$"""
            using System;
            using System.IO;
            using System.Threading.Tasks;
            using UmamusumeResponseAnalyzer.Plugin;

            namespace CallbackLifecycleReentry;

            public sealed class Plugin : IPlugin
            {
                public void Initialize(IPluginContext context)
                    => context.Analyzers.Register<ReadOnlyMemory<byte>>(
                        AnalyzerKind.Response,
                        [EndpointPattern.Exact("{{AccountIndexPath}}")],
                        async _ =>
                        {
                            try
                            {
                                {{lifecycleCall}}
                                File.WriteAllText(@"{{failureLog}}", "completed");
                            }
                            catch (Exception ex)
                            {
                                File.WriteAllLines(@"{{failureLog}}", [ex.GetType().FullName, ex.Message]);
                            }
                        });
            }
            """);
    }

    (string Name, string Source) PluginSource() => (PluginName, $$"""
        using System;
        using System.IO;
        using System.Threading;
        using System.Threading.Tasks;
        using UmamusumeResponseAnalyzer.Plugin;

        namespace {{PluginName}}Ns;

        public sealed class Plugin : IPlugin
        {
            public void Initialize(IPluginContext context)
                => context.Analyzers.Register<ReadOnlyMemory<byte>>(
                    AnalyzerKind.Response,
                    new[] { EndpointPattern.Exact("{{AccountIndexPath}}") },
                    invocation =>
                    {
                        File.AppendAllText(@"{{callbackLog}}", "entered" + Environment.NewLine);
                        return ValueTask.CompletedTask;
                    });

            public void Dispose()
            {
                File.WriteAllText(@"{{disposeEntered}}", "entered");
                while (!File.Exists(@"{{disposeRelease}}"))
                    Thread.Sleep(10);
                File.WriteAllText(@"{{disposeLog}}", "disposed");
            }
        }
        """);

    static (string Name, string Source) DisposePluginSource(string pluginName, string disposeLog, bool fail) =>
        (pluginName, $$"""
        using System;
        using System.IO;
        using UmamusumeResponseAnalyzer.Plugin;

        namespace {{pluginName}}Ns;

        public sealed class Plugin : IPlugin
        {
            public void Initialize(IPluginContext context) { }

            public void Dispose()
            {
                File.AppendAllText(@"{{disposeLog}}", "disposed" + Environment.NewLine);
                if ({{fail.ToString().ToLowerInvariant()}})
                    throw new DisposeFailureException();
            }
        }

        sealed class DisposeFailureException()
            : Exception(@"dispose failed, path=C:\plugins\dispose\state.bin");
        """);

    static (string Name, string Source) BlockingConstructorPluginSource(
        string pluginName,
        string constructorEntered,
        string constructorRelease) => (pluginName, $$"""
        using System.IO;
        using System.Threading;
        using UmamusumeResponseAnalyzer.Plugin;

        namespace {{pluginName}}Ns;

        public sealed class Plugin : IPlugin
        {
            public Plugin()
            {
                File.WriteAllText(@"{{constructorEntered}}", "entered");
                while (!File.Exists(@"{{constructorRelease}}"))
                    Thread.Sleep(10);
            }

            public void Initialize(IPluginContext context) { }
        }
        """);

    static (string Name, string Source) BlockingInitializePluginSource(
        string pluginName,
        string initializeEntered,
        string initializeRelease,
        string disposeOutput) => (pluginName, $$"""
        using System;
        using System.IO;
        using System.Threading;
        using UmamusumeResponseAnalyzer.Plugin;

        namespace {{pluginName}}Ns;

        public sealed class Plugin : IPlugin
        {
            public void Initialize(IPluginContext context)
            {
                File.WriteAllText(@"{{initializeEntered}}", "entered");
                while (!File.Exists(@"{{initializeRelease}}"))
                    Thread.Sleep(10);
            }

            public void Dispose()
                => File.AppendAllText(@"{{disposeOutput}}", "disposed" + Environment.NewLine);
        }
        """);

    (string Name, string Source) StartedPluginSource() => (StartedPluginName, $$"""
        using System;
        using System.IO;
        using System.Threading;
        using System.Threading.Tasks;
        using UmamusumeResponseAnalyzer.Plugin;

        namespace {{StartedPluginName}}Ns;

        public sealed class Plugin : IPlugin
        {
            public void Initialize(IPluginContext context)
            {
                context.Events.OnStarted(_ =>
                {
                    File.AppendAllText(@"{{startedCallbackLog}}", "started" + Environment.NewLine);
                    return ValueTask.CompletedTask;
                });
            }

            public void Dispose()
            {
                File.WriteAllText(@"{{startedDisposeEntered}}", "entered");
                while (!File.Exists(@"{{startedDisposeRelease}}"))
                    Thread.Sleep(10);
                File.WriteAllText(@"{{startedDisposeLog}}", "disposed");
            }
        }
        """);

    static (string Name, string Source) SnapshotAnalyzerPluginSource(
        string pluginName,
        string lifecycle,
        int priority,
        string? entered = null,
        string? release = null)
    {
        var callback = entered is null
            ? $$"""
                File.AppendAllText(@"{{lifecycle}}", "{{pluginName}}:callback" + Environment.NewLine);
                """
            : $$"""
                File.AppendAllText(@"{{lifecycle}}", "{{pluginName}}:entered" + Environment.NewLine);
                File.WriteAllText(@"{{entered}}", "entered");
                while (!File.Exists(@"{{release}}"))
                    await Task.Delay(10);
                File.AppendAllText(@"{{lifecycle}}", "{{pluginName}}:released" + Environment.NewLine);
                """;
        return (pluginName, $$"""
            using System;
            using System.IO;
            using System.Threading.Tasks;
            using UmamusumeResponseAnalyzer.Plugin;

            namespace {{pluginName}}Ns;

            public sealed class Plugin : IPlugin
            {
                public void Initialize(IPluginContext context)
                    => context.Analyzers.Register<ReadOnlyMemory<byte>>(
                        AnalyzerKind.Response,
                        new[] { EndpointPattern.Exact("{{AccountIndexPath}}") },
                        async invocation =>
                        {
                            {{callback}}
                        },
                        {{priority}});

                public void Dispose()
                    => File.AppendAllText(@"{{lifecycle}}", "{{pluginName}}:dispose" + Environment.NewLine);
            }
            """);
    }

    static (string Name, string Source) StableOrderPluginSource(string pluginName, string log) =>
        (pluginName, $$"""
        using System;
        using System.IO;
        using System.Threading.Tasks;
        using UmamusumeResponseAnalyzer.Plugin;

        namespace {{pluginName}}Ns;

        public sealed class Plugin : IPlugin
        {
            public void Initialize(IPluginContext context)
            {
                var endpoint = new[] { EndpointPattern.Exact("{{AccountIndexPath}}") };
                context.Analyzers.Register<ReadOnlyMemory<byte>>(
                    AnalyzerKind.Response,
                    endpoint,
                    _ =>
                    {
                        File.AppendAllText(@"{{log}}", "{{pluginName}}:1" + Environment.NewLine);
                        return ValueTask.CompletedTask;
                    });
                context.Analyzers.Register<ReadOnlyMemory<byte>>(
                    AnalyzerKind.Response,
                    endpoint,
                    _ =>
                    {
                        File.AppendAllText(@"{{log}}", "{{pluginName}}:2" + Environment.NewLine);
                        return ValueTask.CompletedTask;
                    });
            }
        }
        """);

    static (string Name, string Source) AtomicInitializePluginSource(
        string pluginName,
        string analyzerLog,
        string backgroundLog) => (pluginName, $$"""
        using System;
        using System.IO;
        using System.Threading.Tasks;
        using UmamusumeResponseAnalyzer.Plugin;

        namespace {{pluginName}}Ns;

        public sealed class Plugin : IPlugin
        {
            public void Initialize(IPluginContext context)
            {
                context.Analyzers.Register<ReadOnlyMemory<byte>>(
                    AnalyzerKind.Response,
                    new[] { EndpointPattern.Exact("{{AccountIndexPath}}") },
                    _ =>
                    {
                        File.WriteAllText(@"{{analyzerLog}}", "called");
                        return ValueTask.CompletedTask;
                    });
                context.RunBackground(_ =>
                {
                    File.WriteAllText(@"{{backgroundLog}}", "started");
                    return ValueTask.CompletedTask;
                });
                throw new InvalidOperationException("initialize failed");
            }
        }
        """);

    static (string Name, string Source) AtomicStartedPluginSource(
        string pluginName,
        string analyzerLog,
        string backgroundLog) => (pluginName, $$"""
        using System;
        using System.IO;
        using System.Threading.Tasks;
        using UmamusumeResponseAnalyzer.Plugin;

        namespace {{pluginName}}Ns;

        public sealed class Plugin : IPlugin
        {
            public void Initialize(IPluginContext context)
                => context.Events.OnStarted(cancellationToken =>
                {
                    context.Analyzers.Register<ReadOnlyMemory<byte>>(
                        AnalyzerKind.Response,
                        new[] { EndpointPattern.Exact("{{AccountIndexPath}}") },
                        invocation =>
                        {
                            File.WriteAllText(@"{{analyzerLog}}", "called");
                            return ValueTask.CompletedTask;
                        });
                    context.RunBackground(backgroundToken =>
                    {
                        File.WriteAllText(@"{{backgroundLog}}", "started");
                        return ValueTask.CompletedTask;
                    });
                    return ValueTask.FromException(new InvalidOperationException("started failed"));
                });
        }
        """);

    static (string Name, string Source) BackgroundBarrierPluginSource(
        string pluginName,
        string entered,
        string release,
        string lifecycle) => (pluginName, $$"""
        using System;
        using System.IO;
        using System.Threading;
        using System.Threading.Tasks;
        using UmamusumeResponseAnalyzer.Plugin;

        namespace {{pluginName}}Ns;

        public sealed class Plugin : IPlugin
        {
            public void Initialize(IPluginContext context)
                => context.RunBackground(async cancellationToken =>
                {
                    File.AppendAllText(@"{{lifecycle}}", "background-entered" + Environment.NewLine);
                    File.WriteAllText(@"{{entered}}", "entered");
                    try
                    {
                        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        File.AppendAllText(@"{{lifecycle}}", "background-canceled" + Environment.NewLine);
                    }
                    while (!File.Exists(@"{{release}}"))
                        await Task.Delay(10);
                    File.AppendAllText(@"{{lifecycle}}", "background-drained" + Environment.NewLine);
                });

            public void Dispose()
                => File.AppendAllText(@"{{lifecycle}}", "disposed" + Environment.NewLine);
        }
        """);

    static (string Name, string Source) HotkeyBarrierPluginSource(
        string pluginName,
        string callbackEntered,
        string callbackRelease,
        string lifecycleLog) => (pluginName, $$"""
        using System;
        using System.IO;
        using System.Threading.Tasks;
        using UmamusumeResponseAnalyzer.Plugin;
        using UmamusumeResponseAnalyzer.TerminalGui;

        namespace {{pluginName}}Ns;

        public sealed class Plugin : IPlugin
        {
            public void Initialize(IPluginContext context)
            {
                HotkeyManager.Register(ConsoleKey.F6, "blocking plugin callback", async () =>
                {
                    File.AppendAllText(@"{{lifecycleLog}}", "callback-entered" + Environment.NewLine);
                    File.WriteAllText(@"{{callbackEntered}}", "entered");
                    while (!File.Exists(@"{{callbackRelease}}"))
                        await Task.Delay(10);
                    File.AppendAllText(@"{{lifecycleLog}}", "callback-released" + Environment.NewLine);
                });
            }

            public void Dispose()
                => File.AppendAllText(@"{{lifecycleLog}}", "disposed" + Environment.NewLine);
        }
        """);

    static void SeedConfig()
    {
        var current = typeof(Config).GetProperty("Current", BindingFlags.NonPublic | BindingFlags.Static)!;
        if (current.GetValue(null) is null)
            current.SetValue(null, new YamlConfig());
    }
}
