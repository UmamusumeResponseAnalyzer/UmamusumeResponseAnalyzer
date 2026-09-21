using i18n = UmamusumeResponseAnalyzer.Localization.PluginRegistry;
using System.IO.Compression;
using System.Reflection;
using Gallop;
using Gallop.Endpoints;
using Terminal.Gui.App;
using UmamusumeResponseAnalyzer.TerminalGui;
using UmamusumeResponseAnalyzer.Plugin;
using Xunit;

namespace UmamusumeResponseAnalyzer.Tests
{
    [Collection("PluginReload")]
    public sealed class PluginLifecycleTests : IDisposable
    {
        readonly IApplication application;
        readonly string originalCwd = Directory.GetCurrentDirectory();
        readonly string runtimeDir = Path.Combine(
            Path.GetTempPath(),
            "ura-plugin-lifecycle-" + Guid.NewGuid().ToString("N"));

        public PluginLifecycleTests(PluginRuntimeFixture runtime)
        {
            application = runtime.Application;
            SeedConfig();
            ResetPluginState();
            Directory.CreateDirectory(runtimeDir);
            Directory.SetCurrentDirectory(runtimeDir);
            PluginManager.Init();
            HotkeyManager.OverlaySink = runtime.Host;
        }

        public void Dispose()
        {
            try
            {
                ResetPluginState();
            }
            finally
            {
                Directory.SetCurrentDirectory(originalCwd);
                try { Directory.Delete(runtimeDir, recursive: true); } catch { }
            }
        }

        [Fact]
        public void RegisterMethods_WhenLaterAnalyzerIsInvalid_RollsBackAllPluginRegistrations()
        {
            var plugin = new PartiallyInvalidPlugin();

            var ex = Assert.Throws<InvalidOperationException>(() => PluginManager.RegisterMethods(plugin));

            Assert.Contains(nameof(PartiallyInvalidPlugin.Invalid), ex.Message, StringComparison.Ordinal);
            Assert.Empty(PluginManager.ResponseAnalyzerMethods);
        }

        [Fact]
        public void InitializePlugin_CallsContextInitializeEntrypoint()
        {
            var context = new ContextInitializePlugin();

            PluginManager.InitializePlugin(context);

            Assert.True(context.Initialized);
            Assert.NotNull(context.Context);
            Assert.Same(application, context.Context.Application);
            Assert.Same(context.Context, context.Context.Events);
            Assert.NotNull(context.Context.Analyzers);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task NestedCallbacksBlockLifecycleAcrossAwaitAndReleaseAfterReturn(bool sameOwner)
        {
            var first = new ContextInitializePlugin();
            var second = new ContextInitializePlugin();
            PluginManager.InitializePlugin(first);
            PluginManager.InitializePlugin(second);

            using (PluginManager.EnterPluginCallback(first))
            {
                using (PluginManager.EnterPluginCallback(sameOwner ? first : second))
                {
                    await Task.Yield();
                    Assert.Equal(i18n.CallbackLifecycleReentry,
                        Assert.Throws<InvalidOperationException>(PluginManager.InitializeLoadedPlugins).Message);
                }
                Assert.Equal(i18n.CallbackLifecycleReentry,
                    Assert.Throws<InvalidOperationException>(PluginManager.InitializeLoadedPlugins).Message);
            }

            PluginManager.InitializeLoadedPlugins();
        }

        [Fact]
        public async Task InitializeLoadedPlugins_QuarantinesFailingPluginAndContinues()
        {
            using var fixture = new QuarantinePackageFixture();
            RestartPluginRuntime();

            PluginManager.InitializeLoadedPlugins();
            await PluginManager.TriggerStartedAsync();

            var statuses = PluginManager.InspectPluginStatuses();
            var failing = Assert.Single(statuses, status => status.InternalName == fixture.FailingPluginName);
            var healthy = Assert.Single(statuses, status => status.InternalName == fixture.HealthyPluginName);
            Assert.False(failing.IsLoaded);
            Assert.True(failing.IsAvailable);
            Assert.True(healthy.IsLoaded);
            Assert.True(healthy.IsAvailable);
            Assert.True(File.Exists(fixture.FailingInitializeMarker));
            Assert.False(File.Exists(fixture.FailingStartedMarker));
            Assert.False(File.Exists(fixture.FailingBackgroundMarker));
            Assert.True(File.Exists(fixture.HealthyInitializeMarker));
            Assert.True(File.Exists(fixture.HealthyStartedMarker));
        }

        [Fact]
        public async Task TriggerStartedForPluginsAsync_LogsHandlerFailureAndContinues()
        {
            var failing = new StartedFailurePlugin();
            var counter = new StartedCounterPlugin();
            PluginManager.InitializePlugin(failing);
            PluginManager.InitializePlugin(counter);

            var host = TerminalUi.RequireHost();
            var lines = new List<UiLogLine>();
            host.LogAdded += lines.Add;
            try
            {
                var ex = await Record.ExceptionAsync(() => PluginManager.TriggerStartedForPluginsAsync([failing, counter]));
                await host.FlushAsync();

                Assert.Null(ex);
                Assert.Equal(1, counter.StartedCalls);
                var line = Assert.Single(lines, line => line.Text.Contains("插件事件处理错误", StringComparison.Ordinal));
                Assert.Contains(nameof(StartedFailurePlugin), line.ExceptionDetails);
                Assert.Contains(@"C:\plugins\started\settings.yaml", line.ExceptionDetails);
                Assert.Contains(nameof(StartedFailurePlugin.OnStarted), line.ExceptionDetails);
            }
            finally
            {
                host.LogAdded -= lines.Add;
            }
        }

        [Fact]
        public async Task BackgroundFailureKeepsOriginalDetailsAndStillDrains()
        {
            var host = TerminalUi.RequireHost();
            var failure = new TaskCompletionSource<UiLogLine>(TaskCreationOptions.RunContinuationsAsynchronously);
            void Observe(UiLogLine line)
            {
                if (line.Text.Contains("插件后台操作失败", StringComparison.Ordinal))
                    failure.TrySetResult(line);
            }
            host.LogAdded += Observe;
            try
            {
                var plugin = new BackgroundFailurePlugin();
                PluginManager.InitializePlugin(plugin);
                var line = await failure.Task.WaitAsync(TimeSpan.FromSeconds(5));
                Assert.Contains(nameof(BackgroundFailurePlugin), line.ExceptionDetails);
                Assert.Contains(@"C:\plugins\background\pending.json", line.ExceptionDetails);
                Assert.Contains(nameof(BackgroundFailurePlugin.Run), line.ExceptionDetails);
                await PluginManager.ShutdownAsync().WaitAsync(TimeSpan.FromSeconds(5));
            }
            finally
            {
                host.LogAdded -= Observe;
            }
        }

        [Fact]
        public async Task TriggerStartedForPluginsAsync_RethrowsRequestedCancellation()
        {
            using var cancellation = new CancellationTokenSource();
            var plugin = new CanceledStartedPlugin(cancellation);
            PluginManager.InitializePlugin(plugin);

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                PluginManager.TriggerStartedForPluginsAsync([plugin], cancellation.Token));
        }

        [Fact]
        public async Task TriggerStartedForPluginsAsync_ChecksCancellationBetweenSubscriptions()
        {
            using var cancellation = new CancellationTokenSource();
            var lateOutput = Path.Combine(Path.GetTempPath(), "ura-started-cancellation-" + Guid.NewGuid().ToString("N"));
            var plugin = new CancelBetweenStartedSubscriptionsPlugin(cancellation, lateOutput);
            PluginManager.InitializePlugin(plugin);
            try
            {
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                    PluginManager.TriggerStartedForPluginsAsync([plugin], cancellation.Token));

                Assert.False(File.Exists(lateOutput));
            }
            finally
            {
                if (File.Exists(lateOutput))
                    File.Delete(lateOutput);
            }
        }

        [Fact]
        public async Task DisposeHostEventSubscriptions_PreventsLaterStartedInvocation()
        {
            var plugin = new StartedCounterPlugin();
            PluginManager.InitializePlugin(plugin);

            PluginManager.DisposeHostEventSubscriptions(plugin);
            await PluginManager.TriggerStartedForPluginsAsync([plugin]);

            Assert.Equal(0, plugin.StartedCalls);
        }

        [Fact]
        public async Task UnloadAllowsBackgroundCancellationToSnapshotLoadedPluginsBeforeDispose()
        {
            const string scenario = "plugin-generation-unload-snapshot-reentry";
            if (!TerminalUiLifecycleChildProcess.IsChild(scenario))
            {
                Assert.Equal(
                    "ok",
                    await TerminalUiLifecycleProcessTests.RunChildAsync(
                        scenario,
                        typeof(PluginLifecycleTests),
                        nameof(UnloadAllowsBackgroundCancellationToSnapshotLoadedPluginsBeforeDispose)));
                return;
            }

            using var fixture = new GenerationOrderingPackageFixture();
            RestartPluginRuntime();
            PluginManager.InitializeLoadedPlugins();
            await WaitForFileAsync(fixture.BackgroundStarted).WaitAsync(TimeSpan.FromSeconds(5));

            var unload = Task.Run(() => PluginManager.UnloadPluginsAsync(fixture.PluginName));
            var result = Assert.Single(await unload.WaitAsync(TimeSpan.FromSeconds(5)));

            Assert.Equal(PluginManager.PluginLifecycleOutcome.Succeeded, result.Outcome);
            fixture.AssertDisposeOrder();
            TerminalUiLifecycleChildProcess.WriteResult("ok");
        }

        [Fact]
        public async Task ShutdownAllowsBackgroundCancellationToSnapshotLoadedPluginsBeforeDispose()
        {
            const string scenario = "plugin-generation-shutdown-snapshot-reentry";
            if (!TerminalUiLifecycleChildProcess.IsChild(scenario))
            {
                Assert.Equal(
                    "ok",
                    await TerminalUiLifecycleProcessTests.RunChildAsync(
                        scenario,
                        typeof(PluginLifecycleTests),
                        nameof(ShutdownAllowsBackgroundCancellationToSnapshotLoadedPluginsBeforeDispose)));
                return;
            }

            using var fixture = new GenerationOrderingPackageFixture();
            RestartPluginRuntime();
            PluginManager.InitializeLoadedPlugins();
            await WaitForFileAsync(fixture.BackgroundStarted).WaitAsync(TimeSpan.FromSeconds(5));

            await Task.Run(PluginManager.ShutdownAsync).WaitAsync(TimeSpan.FromSeconds(5));

            fixture.AssertDisposeOrder();
            TerminalUiLifecycleChildProcess.WriteResult("ok");
        }

        [Fact]
        public async Task GenerationCloseFailure_UnloadStillCompletesLifecycleCleanup()
        {
            const string scenario = "generation-close-failure-unload-cleanup";
            if (!TerminalUiLifecycleChildProcess.IsChild(scenario))
            {
                Assert.Equal(
                    "ok",
                    await TerminalUiLifecycleProcessTests.RunChildAsync(
                        scenario,
                        typeof(PluginLifecycleTests),
                        nameof(GenerationCloseFailure_UnloadStillCompletesLifecycleCleanup)));
                return;
            }

            await AssertGenerationCloseFailureCleanupAsync(shutdown: false);
            TerminalUiLifecycleChildProcess.WriteResult("ok");
        }

        [Fact]
        public async Task GenerationCloseFailure_ShutdownStillCompletesLifecycleCleanup()
        {
            const string scenario = "generation-close-failure-shutdown-cleanup";
            if (!TerminalUiLifecycleChildProcess.IsChild(scenario))
            {
                Assert.Equal(
                    "ok",
                    await TerminalUiLifecycleProcessTests.RunChildAsync(
                        scenario,
                        typeof(PluginLifecycleTests),
                        nameof(GenerationCloseFailure_ShutdownStillCompletesLifecycleCleanup)));
                return;
            }

            await AssertGenerationCloseFailureCleanupAsync(shutdown: true);
            TerminalUiLifecycleChildProcess.WriteResult("ok");
        }

        static async Task AssertGenerationCloseFailureCleanupAsync(bool shutdown)
        {
            using var fixture = new FaultingGenerationPackageFixture(shutdown ? "Shutdown" : "Unload");
            RestartPluginRuntime();
            PluginManager.InitializeLoadedPlugins();
            await WaitForFileAsync(fixture.BackgroundStarted).WaitAsync(TimeSpan.FromSeconds(5));
            var plugin = Assert.Single(
                PluginManager.SnapshotLoadedPlugins(),
                candidate => PluginManager.InternalName(candidate) == fixture.PluginName);

            var error = shutdown
                ? await Assert.ThrowsAsync<AggregateException>(PluginManager.ShutdownAsync)
                : await Assert.ThrowsAsync<AggregateException>(
                    () => PluginManager.UnloadPluginsAsync(fixture.PluginName));

            AssertWorkflowFailurePrecedesCleanupFailure(error);
            Assert.Equal(["dispose"], File.ReadAllLines(fixture.DisposeLog));
            Assert.DoesNotContain(
                PluginManager.ResponseAnalyzerMethods,
                registration => ReferenceEquals(registration.Plugin, plugin));
            await PluginManager.TriggerStartedForPluginsAsync([plugin]);
            Assert.False(File.Exists(fixture.StartedMarker));
            Assert.False(Assert.Single(
                PluginManager.InspectPluginStatuses(),
                status => status.InternalName == fixture.PluginName).IsLoaded);

            if (!shutdown)
            {
                var result = Assert.Single(await PluginManager.LoadPluginsAsync(fixture.PluginName));
                Assert.Equal(PluginManager.PluginLifecycleOutcome.Succeeded, result.Outcome);
            }
            else
            {
                PluginManager.Init();
                PluginManager.InitializeLoadedPlugins();
            }

            Assert.True(Assert.Single(
                PluginManager.InspectPluginStatuses(),
                status => status.InternalName == fixture.PluginName).IsLoaded);
            await PluginManager.ShutdownAsync();
        }

        [Fact]
        public async Task DependencyGroupInitializeFailure_DoesNotStartBackgroundOrCommitAnyMember()
        {
            using var fixture = new DependencyInitializationPackageFixture(failSecond: true);
            RestartPluginRuntime();
            var stagedPlugins = PluginManager.SnapshotLoadedPlugins();

            PluginManager.InitializeLoadedPlugins();

            Assert.False(File.Exists(fixture.BackgroundMarker));
            Assert.True(File.Exists(fixture.SecondInitializeMarker));
            var failedStatuses = PluginManager.InspectPluginStatuses()
                .Where(status => fixture.Names.Contains(status.InternalName))
                .ToArray();
            Assert.Equal(2, failedStatuses.Length);
            Assert.All(failedStatuses, status =>
            {
                Assert.False(status.IsLoaded);
                Assert.True(status.IsAvailable);
            });
            Assert.DoesNotContain(
                PluginManager.ResponseAnalyzerMethods,
                registration => fixture.Names.Contains(PluginManager.InternalName(registration.Plugin)));
            await PluginManager.TriggerStartedForPluginsAsync(stagedPlugins);
            Assert.False(File.Exists(fixture.StartedMarker));

            fixture.ReplaceSecondWithSuccessfulPackage();
            var results = await PluginManager.LoadPluginsAsync([.. fixture.Names]);
            Assert.Equal(2, results.Count);
            Assert.All(
                results,
                result => Assert.Equal(PluginManager.PluginLifecycleOutcome.Succeeded, result.Outcome));
            var recoveredStatuses = PluginManager.InspectPluginStatuses()
                .Where(status => fixture.Names.Contains(status.InternalName))
                .ToArray();
            Assert.Equal(2, recoveredStatuses.Length);
            Assert.All(recoveredStatuses, status => Assert.True(status.IsLoaded));
            await PluginManager.ShutdownAsync();
        }

        [Fact]
        public async Task SuccessfulDependencyGroup_StartsBackgroundOnlyAfterWholeGroupInitialization()
        {
            using var fixture = new DependencyInitializationPackageFixture(failSecond: false);
            RestartPluginRuntime();

            PluginManager.InitializeLoadedPlugins();
            await WaitForFileAsync(fixture.BackgroundMarker).WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Equal("not-started", File.ReadAllText(fixture.EarlyStartObservation));
            Assert.Equal("after-group-initialize", File.ReadAllText(fixture.BackgroundMarker));
            var statuses = PluginManager.InspectPluginStatuses()
                .Where(status => fixture.Names.Contains(status.InternalName))
                .ToArray();
            Assert.Equal(2, statuses.Length);
            Assert.All(statuses, status => Assert.True(status.IsLoaded));
            await PluginManager.ShutdownAsync();
        }

        [Fact]
        public async Task PluginConfigPrompt_RunAsync_DelegatesToPluginOwnedPrompt()
        {
            var plugin = new ConfigPromptPlugin();
            LoadTestPlugin(plugin);
            using var cancellation = new CancellationTokenSource();

            await PluginConfigPrompt.RunAsync(plugin, cancellation.Token);

            Assert.Equal(1, plugin.ConfigPromptCalls);
            Assert.Same(application, plugin.Application);
            Assert.Equal(cancellation.Token, plugin.CancellationToken);
        }

        [Fact]
        public async Task PluginConfigPrompt_RunAsync_AllowsLoadedPluginBeforeInitialization()
        {
            using var fixture = new CompiledSettingsPluginFixture();
            RestartPluginRuntime();
            var plugin = Assert.Single(
                PluginManager.LoadedPlugins,
                plugin => PluginManager.InternalName(plugin) == "SettingsPlugin");
            Assert.False(File.Exists(fixture.InitializedMarker));

            await PluginConfigPrompt.RunAsync(plugin, TestContext.Current.CancellationToken);

            Assert.Equal("not-initialized", File.ReadAllText(fixture.PromptMarker));
            Assert.False(File.Exists(fixture.InitializedMarker));
            PluginManager.InitializeLoadedPlugins();
            Assert.Equal("initialized", File.ReadAllText(fixture.InitializedMarker));
        }

        [Fact]
        public async Task PluginConfigPrompt_RunAsync_NoCustomPromptReturnsWithoutHostEditor()
        {
            var plugin = new NoConfigPromptPlugin();
            LoadTestPlugin(plugin);

            await PluginConfigPrompt.RunAsync(plugin);
        }

        [Fact]
        public void FailedInitializeContext_RejectsLateAnalyzerAndEventRegistration()
        {
            var plugin = new CapturingFailingInitializePlugin();

            var initializeError = Assert.Throws<InvalidOperationException>(() =>
                PluginManager.InitializePlugin(plugin));

            Assert.Equal("initialize failed", initializeError.Message);
            var context = Assert.IsAssignableFrom<IPluginContext>(plugin.Context);
            var path = GameEndpointCatalog.ByEndpointType[typeof(GameApi.Account.Index)].Path;
            var analyzerError = Assert.Throws<InvalidOperationException>(() =>
                context.Analyzers.Register<ReadOnlyMemory<byte>>(
                    AnalyzerKind.Response,
                    [EndpointPattern.Exact(path)],
                    _ => ValueTask.CompletedTask));
            var eventError = Assert.Throws<InvalidOperationException>(() =>
                context.Events.OnStarted(_ => ValueTask.CompletedTask));
            Assert.Equal(string.Format(i18n.RegistrationPhaseInvalid, PluginManager.InternalName(plugin)), analyzerError.Message);
            Assert.Equal(string.Format(i18n.RegistrationPhaseInvalid, PluginManager.InternalName(plugin)), eventError.Message);
        }

        [Fact]
        public async Task PluginConfigPrompt_RunAsync_RejectsStalePluginInstance()
        {
            var loaded = new ConfigPromptPlugin();
            var stale = new ConfigPromptPlugin();
            LoadTestPlugin(loaded);

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(
                () => PluginConfigPrompt.RunAsync(stale));

            Assert.Equal(string.Format(i18n.ConfigurationUnavailable, PluginManager.InternalName(stale)), ex.Message);
            Assert.Equal(0, stale.ConfigPromptCalls);
        }

        [Fact]
        public void PluginConfig_BuildPluginChoices_AllowsDuplicateDisplayNames()
        {
            PluginManager.PluginRuntimeStatus[] plugins =
            [
                new("PluginA", "Same Display Name", "Alice", null, true, true),
                new("PluginB", "Same Display Name", "Bob", null, true, true),
            ];

            var choices = PluginConfig.BuildPluginChoices(plugins);

            Assert.Equal(2, choices.Count);
            Assert.Equal(2, choices.Keys.Distinct(StringComparer.Ordinal).Count());
            Assert.Equal(["PluginA", "PluginB"], choices.Values);
        }

        [Fact]
        public void PluginConfig_BuildPluginChoices_AllowsDuplicateDisplayNamesAndAuthors()
        {
            PluginManager.PluginRuntimeStatus[] plugins =
            [
                new("PluginA", "Same Display Name", "Same Author", null, true, true),
                new("PluginB", "Same Display Name", "Same Author", null, true, true),
            ];

            var choices = PluginConfig.BuildPluginChoices(plugins);

            Assert.Equal(2, choices.Count);
            Assert.Equal(2, choices.Keys.Distinct(StringComparer.Ordinal).Count());
            Assert.Equal(["PluginA", "PluginB"], choices.Values);
        }

        [Fact]
        public void Init_StrictZipPackage_DoesNotCreatePluginDataDirectoryOrSettingsFile()
        {
            using var fixture = new CompiledSettingsPluginFixture();

            RestartPluginRuntime();

            Assert.Contains(
                PluginManager.LoadedPlugins,
                plugin => PluginManager.InternalName(plugin) == "SettingsPlugin");
            Assert.False(Directory.Exists(fixture.PluginDataDirectory));
            Assert.False(File.Exists(fixture.SettingsPath));
        }

        [Theory]
        [InlineData(InvalidManifestKind.Comment)]
        [InlineData(InvalidManifestKind.TrailingComma)]
        [InlineData(InvalidManifestKind.SingleQuote)]
        [InlineData(InvalidManifestKind.ExtraJsonToken)]
        [InlineData(InvalidManifestKind.DuplicateProperty)]
        public void Init_StrictZipPackage_RejectsNonStrictManifest(InvalidManifestKind kind)
        {
            using var fixture = InvalidPackageFixture.ForManifest(kind);

            RestartPluginRuntime();

            AssertPackageRejected(fixture);
        }

        [Fact]
        public void Init_StrictZipPackage_RejectsMismatchedEmbeddedAssemblyName()
        {
            using var fixture = InvalidPackageFixture.ForMismatchedAssemblyName();

            RestartPluginRuntime();

            AssertPackageRejected(fixture);
        }

        [Fact]
        public void Init_StrictZipPackage_DoesNotReadOrRewritePluginSettingsFile()
        {
            using var fixture = new CompiledSettingsPluginFixture();
            const string settingsYaml = "Value: 99\n";
            Directory.CreateDirectory(Path.GetDirectoryName(fixture.SettingsPath)!);
            File.WriteAllText(fixture.SettingsPath, settingsYaml);

            RestartPluginRuntime();

            Assert.Contains(
                PluginManager.LoadedPlugins,
                plugin => PluginManager.InternalName(plugin) == "SettingsPlugin");
            Assert.Equal(settingsYaml, File.ReadAllText(fixture.SettingsPath).Replace("\r\n", "\n"));
        }

        static void LoadTestPlugin(IPlugin plugin)
            => PluginManager.InitializePlugin(plugin);

        static async Task WaitForFileAsync(string path)
        {
            while (!File.Exists(path))
                await Task.Delay(10);
        }

        static void AssertPackageRejected(InvalidPackageFixture fixture)
        {
            Assert.Empty(PluginManager.LoadedPlugins);
            Assert.Empty(PluginManager.InspectPluginStatuses());
            Assert.Contains(fixture.PackagePath, PluginManager.FailedPlugins);
            Assert.False(File.Exists(fixture.InitializeMarker));
        }

        static void AssertWorkflowFailurePrecedesCleanupFailure(AggregateException error)
        {
            var failures = EnumerateLeafFailures(error).ToList();
            var workflow = failures.FindIndex(exception =>
                exception.Message.Contains("generation close workflow failed", StringComparison.Ordinal));
            var cleanup = failures.FindIndex(exception =>
                exception.Message.Contains("dispose cleanup failed", StringComparison.Ordinal));
            Assert.True(workflow >= 0, error.ToString());
            Assert.True(cleanup >= 0, error.ToString());
            Assert.True(workflow < cleanup, error.ToString());
        }

        static IEnumerable<Exception> EnumerateLeafFailures(Exception exception)
        {
            if (exception is AggregateException aggregate)
            {
                foreach (var inner in aggregate.InnerExceptions)
                    foreach (var nested in EnumerateLeafFailures(inner))
                        yield return nested;
                yield break;
            }

            if (exception.InnerException is not null)
            {
                foreach (var nested in EnumerateLeafFailures(exception.InnerException))
                    yield return nested;
                yield break;
            }

            yield return exception;
        }

        static void ResetPluginState()
            => PluginManager.ShutdownAsync().GetAwaiter().GetResult();

        static void RestartPluginRuntime()
        {
            ResetPluginState();
            PluginManager.Init();
        }

        static void SeedConfig()
        {
            var current = typeof(Config).GetProperty("Current", BindingFlags.NonPublic | BindingFlags.Static)!;
            if (current.GetValue(null) is null)
                current.SetValue(null, new YamlConfig());
        }

        abstract class TestPlugin : IPlugin
        {
            public virtual void Initialize(IPluginContext context)
            {
            }

            public virtual Task ConfigPromptAsync(
                IApplication application,
                CancellationToken cancellationToken = default)
                => Task.CompletedTask;

        }

        public enum InvalidManifestKind
        {
            Comment,
            TrailingComma,
            SingleQuote,
            ExtraJsonToken,
            DuplicateProperty,
        }

        sealed class InvalidPackageFixture : IDisposable
        {
            readonly string originalCwd = Directory.GetCurrentDirectory();
            readonly string tempDir;

            InvalidPackageFixture(string pluginName)
            {
                PluginName = pluginName;
                tempDir = Path.Combine(
                    Path.GetTempPath(),
                    "ura-invalid-package-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(Path.Combine(tempDir, "Plugins"));
                Directory.SetCurrentDirectory(tempDir);
                PackagePath = Path.Combine(tempDir, "Plugins", $"{PluginName}.zip");
                InitializeMarker = Path.Combine(tempDir, "initialized");
                PluginCompiler.CompilePackage(Source(), PluginName, PackagePath);
            }

            public string PluginName { get; }
            public string PackagePath { get; }
            public string InitializeMarker { get; }

            public static InvalidPackageFixture ForManifest(InvalidManifestKind kind)
            {
                var fixture = new InvalidPackageFixture($"InvalidManifest{kind}");
                fixture.RewriteManifest(json => kind switch
                {
                    InvalidManifestKind.Comment => json.Insert(1, "/* comment */"),
                    InvalidManifestKind.TrailingComma => json.Insert(json.Length - 1, ","),
                    InvalidManifestKind.SingleQuote => json.Replace('"', '\''),
                    InvalidManifestKind.ExtraJsonToken => json + "{}",
                    InvalidManifestKind.DuplicateProperty =>
                        json.Insert(1, "\"Author\":\"Duplicate\","),
                    _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
                });
                return fixture;
            }

            public static InvalidPackageFixture ForMismatchedAssemblyName()
            {
                var fixture = new InvalidPackageFixture("DeclaredPluginName");
                var dllPath = Path.Combine(fixture.tempDir, "embedded-name.dll");
                try
                {
                    PluginCompiler.Compile(fixture.Source(), "EmbeddedPluginName", dllPath);
                    using var archive = ZipFile.Open(fixture.PackagePath, ZipArchiveMode.Update);
                    archive.GetEntry($"{fixture.PluginName}.dll")!.Delete();
                    archive.CreateEntryFromFile(dllPath, $"{fixture.PluginName}.dll");
                }
                finally
                {
                    File.Delete(dllPath);
                }
                return fixture;
            }

            void RewriteManifest(Func<string, string> mutate)
            {
                using var archive = ZipFile.Open(PackagePath, ZipArchiveMode.Update);
                var entry = archive.GetEntry("manifest.json")!;
                string json;
                using (var reader = new StreamReader(entry.Open()))
                    json = reader.ReadToEnd();
                entry.Delete();
                using var writer = new StreamWriter(archive.CreateEntry("manifest.json").Open());
                writer.Write(mutate(json));
            }

            string Source() => $$"""
                using System.IO;
                using UmamusumeResponseAnalyzer.Plugin;

                public sealed class StrictZipPlugin : IPlugin
                {
                    public void Initialize(IPluginContext context)
                        => File.WriteAllText(@"{{InitializeMarker}}", "initialized");
                }
                """;

            public void Dispose()
            {
                ResetPluginState();
                Directory.SetCurrentDirectory(originalCwd);
                try { Directory.Delete(tempDir, recursive: true); } catch { }
            }
        }

        sealed class CompiledSettingsPluginFixture : IDisposable
        {
            readonly string originalCwd = Directory.GetCurrentDirectory();
            readonly string tempDir = Path.Combine(Path.GetTempPath(), "ura-plugin-settings-" + Guid.NewGuid().ToString("N"));

            public CompiledSettingsPluginFixture()
            {
                Directory.CreateDirectory(Path.Combine(tempDir, "Plugins"));
                Directory.SetCurrentDirectory(tempDir);
                PluginCompiler.CompilePackage(
                    """
                    using System.IO;
                    using System.Threading.Tasks;
                    using UmamusumeResponseAnalyzer.Plugin;

                    public sealed class SettingsPlugin : IPlugin
                    {
                        bool initialized;

                        public void Initialize(IPluginContext context)
                        {
                            initialized = true;
                            File.WriteAllText("initialized.txt", "initialized");
                        }

                        public Task ConfigPromptAsync(
                            Terminal.Gui.App.IApplication application,
                            System.Threading.CancellationToken cancellationToken = default)
                        {
                            File.WriteAllText(
                                "prompt.txt",
                                initialized ? "initialized" : "not-initialized");
                            return Task.CompletedTask;
                        }
                    }
                    """,
                    "SettingsPlugin",
                    Path.Combine(tempDir, "Plugins", "SettingsPlugin.zip"));

                PluginDataDirectory = Path.Combine(tempDir, "PluginData", "SettingsPlugin");
                SettingsPath = Path.Combine(PluginDataDirectory, "settings.yaml");
                InitializedMarker = Path.Combine(tempDir, "initialized.txt");
                PromptMarker = Path.Combine(tempDir, "prompt.txt");
            }

            public string PluginDataDirectory { get; }
            public string SettingsPath { get; }
            public string InitializedMarker { get; }
            public string PromptMarker { get; }

            public void Dispose()
            {
                ResetPluginState();
                Directory.SetCurrentDirectory(originalCwd);
                try { Directory.Delete(tempDir, recursive: true); } catch { }
            }
        }

        sealed class PartiallyInvalidPlugin : TestPlugin
        {
            [ResponseAnalyzer<GameApi.Account.Index>]
            public ValueTask Valid(DataLinkIndexResponse payload)
                => ValueTask.CompletedTask;

            [ResponseAnalyzer<GameApi.Account.Index>]
            public void Invalid(DataLinkIndexResponse response)
            {
            }
        }

        sealed class QuarantinePackageFixture : IDisposable
        {
            readonly string originalCwd = Directory.GetCurrentDirectory();
            readonly string tempDir = Path.Combine(
                Path.GetTempPath(),
                "ura-plugin-quarantine-" + Guid.NewGuid().ToString("N"));

            public QuarantinePackageFixture()
            {
                Directory.CreateDirectory(Path.Combine(tempDir, "Plugins"));
                Directory.SetCurrentDirectory(tempDir);
                PluginCompiler.CompilePackage(
                    FailingSource(),
                    FailingPluginName,
                    Path.Combine(tempDir, "Plugins", $"{FailingPluginName}.zip"));
                PluginCompiler.CompilePackage(
                    HealthySource(),
                    HealthyPluginName,
                    Path.Combine(tempDir, "Plugins", $"{HealthyPluginName}.zip"));
            }

            public string FailingPluginName => "FailingInitializePlugin";
            public string HealthyPluginName => "HealthyInitializePlugin";
            public string FailingInitializeMarker => Path.Combine(tempDir, "failing-initialize");
            public string FailingStartedMarker => Path.Combine(tempDir, "failing-started");
            public string FailingBackgroundMarker => Path.Combine(tempDir, "failing-background");
            public string HealthyInitializeMarker => Path.Combine(tempDir, "healthy-initialize");
            public string HealthyStartedMarker => Path.Combine(tempDir, "healthy-started");

            string FailingSource() => $$"""
                using System;
                using System.IO;
                using System.Threading.Tasks;
                using UmamusumeResponseAnalyzer.Plugin;

                public sealed class FailingInitializePlugin : IPlugin
                {
                    public void Initialize(IPluginContext context)
                    {
                        File.WriteAllText(@"{{FailingInitializeMarker}}", "entered");
                        context.Events.OnStarted(_ =>
                        {
                            File.WriteAllText(@"{{FailingStartedMarker}}", "called");
                            return ValueTask.CompletedTask;
                        });
                        context.RunBackground(_ =>
                        {
                            File.WriteAllText(@"{{FailingBackgroundMarker}}", "started");
                            return ValueTask.CompletedTask;
                        });
                        throw new InvalidOperationException("initialize failed");
                    }
                }
                """;

            string HealthySource() => $$"""
                using System.IO;
                using System.Threading.Tasks;
                using UmamusumeResponseAnalyzer.Plugin;

                public sealed class HealthyInitializePlugin : IPlugin
                {
                    public void Initialize(IPluginContext context)
                    {
                        File.WriteAllText(@"{{HealthyInitializeMarker}}", "initialized");
                        context.Events.OnStarted(_ =>
                        {
                            File.WriteAllText(@"{{HealthyStartedMarker}}", "started");
                            return ValueTask.CompletedTask;
                        });
                    }
                }
                """;

            public void Dispose()
            {
                ResetPluginState();
                Directory.SetCurrentDirectory(originalCwd);
                try { Directory.Delete(tempDir, recursive: true); } catch { }
            }
        }

        sealed class GenerationOrderingPackageFixture : IDisposable
        {
            readonly string originalCwd = Directory.GetCurrentDirectory();
            readonly string tempDir = Path.Combine(
                Path.GetTempPath(),
                "ura-plugin-generation-order-" + Guid.NewGuid().ToString("N"));

            public GenerationOrderingPackageFixture()
            {
                Directory.CreateDirectory(Path.Combine(tempDir, "Plugins"));
                Directory.SetCurrentDirectory(tempDir);
                PluginCompiler.CompilePackage(
                    Source(),
                    PluginName,
                    Path.Combine(tempDir, "Plugins", $"{PluginName}.zip"));
            }

            public string PluginName => "UmamusumeResponseAnalyzer.Tests";
            public string BackgroundStarted => Path.Combine(tempDir, "background-started");
            string LifecycleLog => Path.Combine(tempDir, "lifecycle.log");

            public void AssertDisposeOrder()
            {
                var lifecycle = File.ReadAllLines(LifecycleLog);
                Assert.Equal(3, lifecycle.Length);
                Assert.Contains("cancellation-callback", lifecycle[..^1]);
                Assert.Contains("background-exit", lifecycle[..^1]);
                Assert.Equal("dispose", lifecycle[^1]);
            }

            string Source() => $$"""
                using System;
                using System.Collections.Concurrent;
                using System.IO;
                using System.Threading;
                using System.Threading.Tasks;
                using UmamusumeResponseAnalyzer.Plugin;

                public sealed class GenerationOrderingPlugin : IPlugin
                {
                    readonly ConcurrentQueue<string> lifecycle = new();

                    public void Initialize(IPluginContext context)
                        => context.RunBackground(async cancellationToken =>
                        {
                            File.WriteAllText(@"{{BackgroundStarted}}", "started");
                            using var registration = cancellationToken.Register(() =>
                            {
                                _ = PluginManager.SnapshotLoadedPlugins();
                                lifecycle.Enqueue("cancellation-callback");
                            });
                            try
                            {
                                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                            }
                            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                            {
                            }
                            lifecycle.Enqueue("background-exit");
                        });

                    public void Dispose()
                    {
                        lifecycle.Enqueue("dispose");
                        File.WriteAllLines(@"{{LifecycleLog}}", lifecycle);
                    }
                }
                """;

            public void Dispose()
            {
                ResetPluginState();
                Directory.SetCurrentDirectory(originalCwd);
                try { Directory.Delete(tempDir, recursive: true); } catch { }
            }
        }

        sealed class FaultingGenerationPackageFixture : IDisposable
        {
            readonly string originalCwd = Directory.GetCurrentDirectory();
            readonly string tempDir = Path.Combine(
                Path.GetTempPath(),
                "ura-plugin-generation-failure-" + Guid.NewGuid().ToString("N"));

            public FaultingGenerationPackageFixture(string operation)
            {
                PluginName = $"FaultingGeneration{operation}Plugin";
                Directory.CreateDirectory(Path.Combine(tempDir, "Plugins"));
                Directory.SetCurrentDirectory(tempDir);
                File.WriteAllText(CloseFaultFlag, "fault");
                File.WriteAllText(DisposeFaultFlag, "fault");
                PluginCompiler.CompilePackage(
                    Source(),
                    PluginName,
                    Path.Combine(tempDir, "Plugins", $"{PluginName}.zip"));
            }

            public string PluginName { get; }
            public string BackgroundStarted => Path.Combine(tempDir, "background-started");
            public string DisposeLog => Path.Combine(tempDir, "dispose.log");
            public string StartedMarker => Path.Combine(tempDir, "started");
            string AnalyzerMarker => Path.Combine(tempDir, "analyzer");
            string CloseFaultFlag => Path.Combine(tempDir, "close-fault");
            string DisposeFaultFlag => Path.Combine(tempDir, "dispose-fault");

            string Source() => $$"""
                using System;
                using System.IO;
                using System.Threading;
                using System.Threading.Tasks;
                using Gallop;
                using Gallop.Endpoints;
                using UmamusumeResponseAnalyzer.Plugin;

                public sealed class FaultingGenerationPlugin : IPlugin
                {
                    public void Initialize(IPluginContext context)
                    {
                        context.Events.OnStarted(_ =>
                        {
                            File.WriteAllText(@"{{StartedMarker}}", "started");
                            return ValueTask.CompletedTask;
                        });
                        context.RunBackground(async cancellationToken =>
                        {
                            using var cancellation = cancellationToken.Register(() =>
                            {
                                if (!File.Exists(@"{{CloseFaultFlag}}"))
                                    return;
                                File.Delete(@"{{CloseFaultFlag}}");
                                throw new InvalidOperationException("generation close workflow failed");
                            });
                            File.WriteAllText(@"{{BackgroundStarted}}", "started");
                            try
                            {
                                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                            }
                            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                            {
                            }
                        });
                    }

                    [ResponseAnalyzer<GameApi.Account.Index>]
                    public ValueTask Analyze(DataLinkIndexResponse payload)
                    {
                        File.WriteAllText(@"{{AnalyzerMarker}}", "analyzed");
                        return ValueTask.CompletedTask;
                    }

                    public void Dispose()
                    {
                        File.AppendAllText(@"{{DisposeLog}}", "dispose" + Environment.NewLine);
                        if (!File.Exists(@"{{DisposeFaultFlag}}"))
                            return;
                        File.Delete(@"{{DisposeFaultFlag}}");
                        throw new InvalidOperationException("dispose cleanup failed");
                    }
                }
                """;

            public void Dispose()
            {
                try { ResetPluginState(); } catch { }
                Directory.SetCurrentDirectory(originalCwd);
                try { Directory.Delete(tempDir, recursive: true); } catch { }
            }
        }

        sealed class DependencyInitializationPackageFixture : IDisposable
        {
            readonly string originalCwd = Directory.GetCurrentDirectory();
            readonly string tempDir = Path.Combine(
                Path.GetTempPath(),
                "ura-plugin-group-initialize-" + Guid.NewGuid().ToString("N"));
            readonly string secondPackage;
            readonly bool observeEarlyStart;

            public DependencyInitializationPackageFixture(bool failSecond)
            {
                var suffix = failSecond ? "Failure" : "Success";
                FirstName = $"AtomicBackground{suffix}Plugin";
                SecondName = $"AtomicInitializer{suffix}Plugin";
                observeEarlyStart = !failSecond;
                Directory.CreateDirectory(Path.Combine(tempDir, "Plugins"));
                Directory.SetCurrentDirectory(tempDir);
                PluginCompiler.CompilePackage(
                    FirstSource(),
                    FirstName,
                    Path.Combine(tempDir, "Plugins", $"{FirstName}.zip"));
                secondPackage = Path.Combine(tempDir, "Plugins", $"{SecondName}.zip");
                CompileSecondPackage(failSecond);
            }

            public string FirstName { get; }
            public string SecondName { get; }
            public IReadOnlySet<string> Names => new HashSet<string>(
                [FirstName, SecondName],
                StringComparer.OrdinalIgnoreCase);
            public string BackgroundMarker => Path.Combine(tempDir, "background");
            public string StartedMarker => Path.Combine(tempDir, "started");
            public string SecondInitializeMarker => Path.Combine(tempDir, "second-initialize");
            public string EarlyStartObservation => Path.Combine(tempDir, "early-start-observation");
            string AnalyzerMarker => Path.Combine(tempDir, "analyzer");
            string GroupInitializedMarker => Path.Combine(tempDir, "group-initialized");

            public void ReplaceSecondWithSuccessfulPackage()
            {
                File.Delete(secondPackage);
                CompileSecondPackage(fail: false);
            }

            void CompileSecondPackage(bool fail)
                => PluginCompiler.CompilePackage(
                    SecondSource(fail),
                    SecondName,
                    secondPackage,
                    dependencies: [FirstName]);

            string FirstSource() => $$"""
                using System.IO;
                using System.Threading.Tasks;
                using Gallop;
                using Gallop.Endpoints;
                using UmamusumeResponseAnalyzer.Plugin;

                public sealed class AtomicBackgroundPlugin : IPlugin
                {
                    public void Initialize(IPluginContext context)
                    {
                        context.Events.OnStarted(_ =>
                        {
                            File.WriteAllText(@"{{StartedMarker}}", "started");
                            return ValueTask.CompletedTask;
                        });
                        context.RunBackground(_ =>
                        {
                            File.WriteAllText(
                                @"{{BackgroundMarker}}",
                                File.Exists(@"{{GroupInitializedMarker}}")
                                    ? "after-group-initialize"
                                    : "before-group-initialize");
                            return ValueTask.CompletedTask;
                        });
                    }

                    [ResponseAnalyzer<GameApi.Account.Index>]
                    public ValueTask Analyze(DataLinkIndexResponse payload)
                    {
                        File.WriteAllText(@"{{AnalyzerMarker}}", "analyzed");
                        return ValueTask.CompletedTask;
                    }
                }
                """;

            string SecondSource(bool fail)
            {
                if (fail)
                    return $$"""
                    using System;
                    using System.IO;
                    using UmamusumeResponseAnalyzer.Plugin;

                    public sealed class AtomicFailingInitializerPlugin : IPlugin
                    {
                        public void Initialize(IPluginContext context)
                        {
                            File.WriteAllText(@"{{SecondInitializeMarker}}", "entered");
                            throw new InvalidOperationException("second initialize failed");
                        }
                    }
                    """;

                var observeEarly = observeEarlyStart
                    ? $$"""
                        var deadline = DateTime.UtcNow.AddSeconds(1);
                        while (!File.Exists(@"{{BackgroundMarker}}") && DateTime.UtcNow < deadline)
                            Thread.Sleep(10);
                        var startedEarly = File.Exists(@"{{BackgroundMarker}}");
                        File.WriteAllText(
                            @"{{EarlyStartObservation}}",
                            startedEarly ? "started" : "not-started");
                        """
                    : string.Empty;
                return $$"""
                    using System;
                    using System.IO;
                    using System.Threading;
                    using UmamusumeResponseAnalyzer.Plugin;

                    public sealed class AtomicSuccessfulInitializerPlugin : IPlugin
                    {
                        public void Initialize(IPluginContext context)
                        {
                            File.WriteAllText(@"{{SecondInitializeMarker}}", "entered");
                            {{observeEarly}}
                            File.WriteAllText(@"{{GroupInitializedMarker}}", "initialized");
                        }
                    }
                    """;
            }

            public void Dispose()
            {
                try { ResetPluginState(); } catch { }
                Directory.SetCurrentDirectory(originalCwd);
                try { Directory.Delete(tempDir, recursive: true); } catch { }
            }
        }

        sealed class ContextInitializePlugin : TestPlugin
        {
            public bool Initialized { get; private set; }
            public IPluginContext? Context { get; private set; }

            public override void Initialize(IPluginContext context)
            {
                Initialized = true;
                Context = context;
            }
        }

        sealed class StartedFailurePlugin : TestPlugin
        {
            public override void Initialize(IPluginContext context)
            {
                context.Events.OnStarted(OnStarted);
            }

            public ValueTask OnStarted(CancellationToken cancellationToken)
                => throw new InvalidOperationException(@"started failed, path=C:\plugins\started\settings.yaml");
        }

        sealed class StartedCounterPlugin : TestPlugin
        {
            public int StartedCalls { get; private set; }

            public override void Initialize(IPluginContext context)
            {
                context.Events.OnStarted(_ =>
                {
                    StartedCalls++;
                    return ValueTask.CompletedTask;
                });
            }
        }

        sealed class BackgroundFailurePlugin : TestPlugin
        {
            public override void Initialize(IPluginContext context) => context.RunBackground(Run);

            public async ValueTask Run(CancellationToken cancellationToken)
            {
                await Task.Yield();
                throw new InvalidOperationException(@"background failed, path=C:\plugins\background\pending.json");
            }
        }

        sealed class CanceledStartedPlugin(CancellationTokenSource cancellation)
            : TestPlugin
        {
            public override void Initialize(IPluginContext context)
            {
                context.Events.OnStarted(cancellationToken =>
                {
                    cancellation.Cancel();
                    return ValueTask.FromCanceled(cancellationToken);
                });
            }
        }

        sealed class CancelBetweenStartedSubscriptionsPlugin(
            CancellationTokenSource cancellation,
            string lateOutput) : TestPlugin
        {
            public override void Initialize(IPluginContext context)
            {
                context.Events.OnStarted(_ =>
                {
                    cancellation.Cancel();
                    return ValueTask.CompletedTask;
                });
                context.Events.OnStarted(_ =>
                {
                    File.WriteAllText(lateOutput, "invoked");
                    return ValueTask.CompletedTask;
                });
            }
        }

        sealed class CapturingFailingInitializePlugin : TestPlugin
        {
            public IPluginContext? Context { get; private set; }

            public override void Initialize(IPluginContext context)
            {
                Context = context;
                throw new InvalidOperationException("initialize failed");
            }
        }

        sealed class ConfigPromptPlugin : TestPlugin
        {
            public int ConfigPromptCalls { get; private set; }
            public IApplication? Application { get; private set; }
            public CancellationToken CancellationToken { get; private set; }

            public override Task ConfigPromptAsync(
                IApplication application,
                CancellationToken cancellationToken = default)
            {
                ConfigPromptCalls++;
                Application = application;
                CancellationToken = cancellationToken;
                return Task.CompletedTask;
            }
        }

        sealed class NoConfigPromptPlugin : TestPlugin
        {
        }

    }
}
