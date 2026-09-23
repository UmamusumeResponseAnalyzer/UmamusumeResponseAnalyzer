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
    [Collection("PluginRuntime")]
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
        public void AttributeRegistration_WhenLaterAnalyzerIsInvalid_RollsBackAllPluginRegistrations()
        {
            var plugin = new PartiallyInvalidPlugin();

            var ex = Assert.Throws<InvalidOperationException>(() => PluginManager.InitializePlugin(plugin));

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
            Assert.NotNull(context.Context.Analyzers);
        }

        [Fact]
        public async Task InitializeLoadedPlugins_QuarantinesFailingPluginAndContinues()
        {
            using var fixture = new QuarantinePackageFixture();
            RestartPluginRuntime();

            PluginManager.InitializeLoadedPlugins();
            await PluginManager.TriggerStartedAsync();

            Assert.Null(PluginManager.FindLoadedPlugin(fixture.FailingPluginName));
            Assert.NotNull(PluginManager.FindLoadedPlugin(fixture.HealthyPluginName));
            Assert.Contains(Path.GetFullPath(Path.Combine("Plugins", $"{fixture.FailingPluginName}.zip")), PluginManager.FailedPlugins);
            Assert.True(File.Exists(fixture.FailingInitializeMarker));
            Assert.False(File.Exists(fixture.FailingStartedMarker));
            Assert.True(File.Exists(fixture.HealthyInitializeMarker));
            Assert.True(File.Exists(fixture.HealthyStartedMarker));
        }

        [Fact]
        public async Task TriggerStartedAsync_LogsHandlerFailureAndContinues()
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
                var ex = await Record.ExceptionAsync(async () =>
                {
                    await PluginManager.StartPluginAsync(failing);
                    await PluginManager.StartPluginAsync(counter);
                });
                await host.FlushAsync();

                Assert.Null(ex);
                Assert.Equal(1, counter.StartedCalls);
                var line = Assert.Single(lines, line => line.Text.Contains("插件启动错误", StringComparison.Ordinal));
                Assert.Contains(nameof(StartedFailurePlugin), line.ExceptionDetails);
                Assert.Contains(@"C:\plugins\started\settings.yaml", line.ExceptionDetails);
                Assert.Contains(nameof(StartedFailurePlugin.StartAsync), line.ExceptionDetails);
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
                await PluginManager.CleanupPluginAsync(plugin).WaitAsync(TimeSpan.FromSeconds(5));
            }
            finally
            {
                host.LogAdded -= Observe;
            }
        }

        [Fact]
        public async Task TriggerStartedAsync_RethrowsRequestedCancellation()
        {
            using var cancellation = new CancellationTokenSource();
            var plugin = new CanceledStartedPlugin(cancellation);
            PluginManager.InitializePlugin(plugin);

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                PluginManager.StartPluginAsync(plugin, cancellation.Token));
        }

        [Fact]
        public async Task CleanupPreventsLaterStartedInvocation()
        {
            var plugin = new StartedCounterPlugin();
            PluginManager.InitializePlugin(plugin);
            await PluginManager.CleanupPluginAsync(plugin);
            await PluginManager.StartPluginAsync(plugin);
            Assert.Equal(0, plugin.StartedCalls);
        }

        [Fact]
        public async Task ShutdownAllowsBackgroundCancellationToSnapshotLoadedPluginsBeforeDispose()
        {
            const string scenario = "plugin-lifecycle-shutdown-snapshot-reentry";
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

            using var fixture = new LifecycleOrderingPackageFixture();
            RestartPluginRuntime();
            PluginManager.InitializeLoadedPlugins();
            await WaitForFileAsync(fixture.BackgroundStarted).WaitAsync(TimeSpan.FromSeconds(5));

            await Task.Run(PluginManager.ShutdownAsync).WaitAsync(TimeSpan.FromSeconds(5));

            fixture.AssertDisposeOrder();
            TerminalUiLifecycleChildProcess.WriteResult("ok");
        }

        [Fact]
        public async Task DisposeFailure_ShutdownStillCompletesLifecycleCleanup()
        {
            PluginCompiler.CompilePackage("""
                using System;
                using System.Threading.Tasks;
                using UmamusumeResponseAnalyzer.Plugin;
                public sealed class FailingDispose : IPlugin
                {
                    public void Initialize(IPluginContext context) => context.Analyzers.Register<ReadOnlyMemory<byte>>(
                        AnalyzerKind.Response, [EndpointPattern.Exact("/umamusume/account/index")], _ => ValueTask.CompletedTask);
                    public ValueTask DisposeAsync() => throw new InvalidOperationException("dispose cleanup failed");
                }
                """, "FailingDispose", Path.Combine("Plugins", "FailingDispose.zip"));
            RestartPluginRuntime();
            PluginManager.InitializeLoadedPlugins();
            Assert.NotEmpty(PluginManager.ResponseAnalyzerMethods);

            var error = await Assert.ThrowsAsync<AggregateException>(PluginManager.ShutdownAsync);

            Assert.Contains("dispose cleanup failed", error.ToString());
            Assert.Empty(PluginManager.ResponseAnalyzerMethods);
            Assert.Empty(PluginManager.LoadedPlugins);
            Assert.Empty(PluginManager.Metadatas);
        }

        [Fact]
        public async Task DependencyGroupInitializeFailureKeepsHealthyMemberRunning()
        {
            using var fixture = new DependencyInitializationPackageFixture();
            RestartPluginRuntime();
            PluginManager.InitializeLoadedPlugins();
            await WaitForFileAsync(fixture.BackgroundMarker).WaitAsync(TimeSpan.FromSeconds(5));
            await PluginManager.TriggerStartedAsync();

            Assert.True(File.Exists(fixture.SecondInitializeMarker));
            Assert.True(File.Exists(fixture.StartedMarker));
            Assert.Single(PluginManager.LoadedPlugins);
            Assert.Equal(Path.GetFullPath(Path.Combine("Plugins", $"{fixture.SecondName}.zip")), Assert.Single(PluginManager.FailedPlugins));
            Assert.Contains(PluginManager.ResponseAnalyzerMethods,
                registration => fixture.Names.Contains(PluginManager.InternalName(registration.Plugin)));

            fixture.ReplaceSecondWithSuccessfulPackage();
            Assert.Single(PluginManager.FailedPlugins);
            RestartPluginRuntime();
            PluginManager.InitializeLoadedPlugins();
            Assert.Equal(2, PluginManager.LoadedPlugins.Count);
            Assert.Empty(PluginManager.FailedPlugins);
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
        public void FailedInitializeContext_RejectsLateAnalyzerRegistration()
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
            Assert.Equal(string.Format(i18n.RegistrationPhaseInvalid, PluginManager.InternalName(plugin)), analyzerError.Message);
        }

        [Fact]
        public void CompletedRegistrationStage_RejectsCapturedContextForAnalyzerRegistration()
        {
            var plugin = new CapturingRegistrationPlugin();
            PluginManager.InitializePlugin(plugin);
            var path = GameEndpointCatalog.ByEndpointType[typeof(GameApi.Account.Index)].Path;
            var register = () => plugin.Context.Analyzers.Register<ReadOnlyMemory<byte>>(
                AnalyzerKind.Response, [EndpointPattern.Exact(path)], _ => ValueTask.CompletedTask);

            Assert.Throws<InvalidOperationException>(register);
            ExecutionContext.Run(plugin.CapturedContext, _ =>
            {
                Assert.Throws<ObjectDisposedException>(register);
            }, null);

            Assert.Empty(PluginManager.ResponseAnalyzerMethods);
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
        public void PluginConfigPrompt_BuildPluginChoices_AllowsDuplicateDisplayNames()
        {
            PluginManager.PluginMetadata[] plugins =
            [
                new("", "", "PluginA", "Same Display Name", "Alice", new Version(1, 0), [], new Dictionary<string, string>(), [], []),
                new("", "", "PluginB", "Same Display Name", "Bob", new Version(1, 0), [], new Dictionary<string, string>(), [], []),
            ];

            var choices = PluginConfigPrompt.BuildPluginChoices(plugins);

            Assert.Equal(2, choices.Count);
            Assert.Equal(2, choices.Keys.Distinct(StringComparer.Ordinal).Count());
            Assert.Equal(["PluginA", "PluginB"], choices.Values);
        }

        [Fact]
        public void PluginConfigPrompt_BuildPluginChoices_AllowsDuplicateDisplayNamesAndAuthors()
        {
            PluginManager.PluginMetadata[] plugins =
            [
                new("", "", "PluginA", "Same Display Name", "Same Author", new Version(1, 0), [], new Dictionary<string, string>(), [], []),
                new("", "", "PluginB", "Same Display Name", "Same Author", new Version(1, 0), [], new Dictionary<string, string>(), [], []),
            ];

            var choices = PluginConfigPrompt.BuildPluginChoices(plugins);

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
            Assert.Empty(PluginManager.Metadatas);
            Assert.Contains(fixture.PackagePath, PluginManager.FailedPlugins);
            Assert.False(File.Exists(fixture.InitializeMarker));
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
            public virtual ValueTask StartAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
            public virtual ValueTask DisposeAsync() => ValueTask.CompletedTask;

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
                        throw new InvalidOperationException("initialize failed");
                    }
                    public ValueTask StartAsync(System.Threading.CancellationToken token = default)
                    {
                        File.WriteAllText(@"{{FailingStartedMarker}}", "called");
                        return ValueTask.CompletedTask;
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
                    }
                    public ValueTask StartAsync(System.Threading.CancellationToken token = default)
                    {
                        File.WriteAllText(@"{{HealthyStartedMarker}}", "started");
                        return ValueTask.CompletedTask;
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

        sealed class LifecycleOrderingPackageFixture : IDisposable
        {
            readonly string originalCwd = Directory.GetCurrentDirectory();
            readonly string tempDir = Path.Combine(
                Path.GetTempPath(),
                "ura-plugin-lifecycle-order-" + Guid.NewGuid().ToString("N"));

            public LifecycleOrderingPackageFixture()
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

                public sealed class LifecycleOrderingPlugin : IPlugin
                {
                    readonly ConcurrentQueue<string> lifecycle = new();

                    readonly CancellationTokenSource cancellation = new();
                    Task background = Task.CompletedTask;
                    public void Initialize(IPluginContext context)
                        => background = Task.Run(async () =>
                        {
                            var cancellationToken = cancellation.Token;
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

                    public async ValueTask DisposeAsync()
                    {
                        await cancellation.CancelAsync();
                        await background;
                        cancellation.Dispose();
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

        sealed class DependencyInitializationPackageFixture : IDisposable
        {
            readonly string originalCwd = Directory.GetCurrentDirectory();
            readonly string tempDir = Path.Combine(
                Path.GetTempPath(),
                "ura-plugin-group-initialize-" + Guid.NewGuid().ToString("N"));
            readonly string secondPackage;

            public DependencyInitializationPackageFixture()
            {
                FirstName = "DependencyBackgroundPlugin";
                SecondName = "DependencyInitializerPlugin";
                Directory.CreateDirectory(Path.Combine(tempDir, "Plugins"));
                Directory.SetCurrentDirectory(tempDir);
                PluginCompiler.CompilePackage(
                    FirstSource(),
                    FirstName,
                    Path.Combine(tempDir, "Plugins", $"{FirstName}.zip"));
                secondPackage = Path.Combine(tempDir, "Plugins", $"{SecondName}.zip");
                CompileSecondPackage(fail: true);
            }

            public string FirstName { get; }
            public string SecondName { get; }
            public IReadOnlySet<string> Names => new HashSet<string>(
                [FirstName, SecondName],
                StringComparer.OrdinalIgnoreCase);
            public string BackgroundMarker => Path.Combine(tempDir, "background");
            public string StartedMarker => Path.Combine(tempDir, "started");
            public string SecondInitializeMarker => Path.Combine(tempDir, "second-initialize");
            string AnalyzerMarker => Path.Combine(tempDir, "analyzer");

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

                public sealed class DependencyBackgroundPlugin : IPlugin
                {
                    Task background = Task.CompletedTask;
                    public void Initialize(IPluginContext context)
                        => background = Task.Run(() => File.WriteAllText(@"{{BackgroundMarker}}", "started"));
                    public ValueTask StartAsync(System.Threading.CancellationToken token = default)
                    {
                        File.WriteAllText(@"{{StartedMarker}}", "started");
                        return ValueTask.CompletedTask;
                    }
                    public async ValueTask DisposeAsync() => await background;

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

                    public sealed class FailingInitializerPlugin : IPlugin
                    {
                        public void Initialize(IPluginContext context)
                        {
                            File.WriteAllText(@"{{SecondInitializeMarker}}", "entered");
                            throw new InvalidOperationException("second initialize failed");
                        }
                    }
                    """;

                return $$"""
                    using System;
                    using System.IO;
                    using System.Threading;
                    using UmamusumeResponseAnalyzer.Plugin;

                    public sealed class SuccessfulInitializerPlugin : IPlugin
                    {
                        public void Initialize(IPluginContext context)
                        {
                            File.WriteAllText(@"{{SecondInitializeMarker}}", "entered");
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

        sealed class CapturingRegistrationPlugin : TestPlugin
        {
            public IPluginContext Context { get; private set; } = null!;
            public ExecutionContext CapturedContext { get; private set; } = null!;
            public override void Initialize(IPluginContext context)
            {
                Context = context;
                CapturedContext = ExecutionContext.Capture()!;
            }
        }

        sealed class StartedFailurePlugin : TestPlugin
        {
            public override ValueTask StartAsync(CancellationToken cancellationToken = default)
                => throw new InvalidOperationException(@"started failed, path=C:\plugins\started\settings.yaml");
        }

        sealed class StartedCounterPlugin : TestPlugin
        {
            public int StartedCalls { get; private set; }
            public override ValueTask StartAsync(CancellationToken cancellationToken = default)
            {
                StartedCalls++;
                return ValueTask.CompletedTask;
            }
        }

        sealed class BackgroundFailurePlugin : TestPlugin
        {
            Task background = Task.CompletedTask;
            public override void Initialize(IPluginContext context) => background = Task.Run(async () =>
            {
                try { await Run(); }
                catch (Exception error) { context.ReportBackgroundFailure(error); }
            });
            public async Task Run()
            {
                await Task.Yield();
                throw new InvalidOperationException(@"background failed, path=C:\plugins\background\pending.json");
            }
            public override async ValueTask DisposeAsync() => await background;
        }

        sealed class CanceledStartedPlugin(CancellationTokenSource cancellation) : TestPlugin
        {
            public override ValueTask StartAsync(CancellationToken cancellationToken = default)
            {
                cancellation.Cancel();
                return ValueTask.FromCanceled(cancellationToken);
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
