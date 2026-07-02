using System.IO.Compression;
using System.Reflection;
using Gallop;
using Gallop.Endpoints;
using Spectre.Console;
using UmamusumeResponseAnalyzer.LiveDisplay;
using UmamusumeResponseAnalyzer.Plugin;
using WatsonWebserver.Core;
using WatsonHttpMethod = WatsonWebserver.Core.HttpMethod;
using Xunit;

namespace UmamusumeResponseAnalyzer.Tests
{
    [Collection("PluginReload")]
    public sealed class PluginLifecycleTests : IDisposable
    {
        public PluginLifecycleTests()
        {
            SeedConfig();
            ResetPluginState();
            PluginManager.BindLiveDisplay(_ => new FakeLiveDisplayOutput());
        }

        public void Dispose()
        {
            ResetPluginState();
        }

        [Fact]
        public void RegisterMethods_WhenLaterAnalyzerIsInvalid_RollsBackAllPluginRegistrations()
        {
            var plugin = new PartiallyInvalidPlugin();

            var ex = Assert.Throws<InvalidOperationException>(() => PluginManager.RegisterMethods(plugin));

            Assert.Contains(nameof(PartiallyInvalidPlugin.Invalid), ex.Message, StringComparison.Ordinal);
            Assert.Empty(PluginManager.ResponseAnalyzerMethods);
            Assert.False(RouteExists(plugin, "valid"));
        }

        [Fact]
        public void RegisterMethods_RouteInstanceMethod_RegistersRouteForPluginInstance()
        {
            var plugin = new RoutePlugin();

            PluginManager.RegisterMethods(plugin);

            Assert.True(RouteExists(plugin, "ok"));
        }

        [Fact]
        public void RegisterMethods_RouteWithWrongSignature_FailsFastAndDoesNotRegisterRoute()
        {
            var plugin = new InvalidRoutePlugin();

            var ex = Assert.Throws<InvalidOperationException>(() => PluginManager.RegisterMethods(plugin));

            Assert.Contains("Route", ex.Message, StringComparison.Ordinal);
            Assert.False(RouteExists(plugin, "bad"));
        }

        [Fact]
        public void InitializePlugin_CallsContextInitializeEntrypoint()
        {
            var context = new ContextInitializePlugin();
            var liveDisplay = new FakeLiveDisplayOutput();
            PluginManager.BindLiveDisplay(_ => liveDisplay);

            PluginManager.InitializePlugin(context);

            Assert.True(context.Initialized);
            Assert.NotNull(context.Context);
            Assert.Same(liveDisplay, context.Context.LiveDisplay);
        }

        [Fact]
        public async Task InitializeLoadedPlugins_QuarantinesFailingPluginAndContinues()
        {
            var failing = new FailingInitializePlugin();
            var healthy = new ContextInitializePlugin();
            PluginManager.RegisterMethods(failing);
            PluginManager.LoadedPlugins.Add(failing);
            PluginManager.LoadedPlugins.Add(healthy);

            PluginManager.InitializeLoadedPlugins();

            Assert.DoesNotContain(PluginManager.LoadedPlugins, x => ReferenceEquals(x, failing));
            Assert.Contains(PluginManager.LoadedPlugins, x => ReferenceEquals(x, healthy));
            Assert.True(healthy.Initialized);
            Assert.Contains(failing.Name, PluginManager.FailedPlugins);
            Assert.False(RouteExists(failing, "boom"));
            Assert.DoesNotContain(
                PluginManager.ResponseAnalyzerMethods.Values.SelectMany(x => x),
                x => ReferenceEquals(x.Plugin, failing));

            await PluginManager.TriggerStartedForPluginsAsync([failing]);
            Assert.Equal(0, failing.StartedCalls);
        }

        [Fact]
        public async Task TriggerStartedForPluginsAsync_LogsHandlerFailureAndContinues()
        {
            var failing = new StartedFailurePlugin();
            var counter = new StartedCounterPlugin();
            PluginManager.InitializePlugin(failing);
            PluginManager.InitializePlugin(counter);

            var ex = await Record.ExceptionAsync(() => PluginManager.TriggerStartedForPluginsAsync([failing, counter]));

            Assert.Null(ex);
            Assert.Equal(1, counter.StartedCalls);
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
        public void ReloadPlugins_FailsFastInsidePluginCallback()
        {
            using var callback = PluginManager.EnterPluginCallbackScope();

            var ex = Assert.Throws<InvalidOperationException>(() => PluginManager.ReloadPlugins("AnyPlugin"));

            Assert.Contains("插件回调内禁止执行热重载", ex.Message, StringComparison.Ordinal);
        }

        [Fact]
        public async Task PluginConfigPrompt_RunAsync_DelegatesToPluginOwnedPrompt()
        {
            var plugin = new ConfigPromptPlugin();

            await PluginConfigPrompt.RunAsync(plugin);

            Assert.Equal(1, plugin.ConfigPromptCalls);
        }

        [Fact]
        public async Task PluginConfigPrompt_RunAsync_NoCustomPromptReturnsWithoutHostEditor()
        {
            var plugin = new NoConfigPromptPlugin();

            await PluginConfigPrompt.RunAsync(plugin);

            Assert.Equal(0, plugin.UpdateCalls);
        }

        [Fact]
        public async Task PluginConfigPrompt_RunAsync_DoesNotCallLegacyConfigPromptEntrypoint()
        {
            var plugin = new LegacyConfigPromptPlugin();

            await PluginConfigPrompt.RunAsync(plugin);

            Assert.Equal(0, plugin.LegacyConfigPromptCalls);
        }

        [Fact]
        public async Task PluginConfigPrompt_RunAsync_BlocksHotReloadInsidePrompt()
        {
            var plugin = new ReloadingConfigPromptPlugin();

            await PluginConfigPrompt.RunAsync(plugin);

            Assert.NotNull(plugin.ReloadException);
            Assert.Contains("插件回调内禁止执行热重载", plugin.ReloadException!.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void PluginConfig_BuildPluginChoices_AllowsDuplicateDisplayNames()
        {
            IPlugin[] plugins =
            [
                new DuplicateDisplayNamePlugin("Alice"),
                new DuplicateDisplayNamePlugin("Bob"),
            ];

            var choices = PluginConfig.BuildPluginChoices(plugins);

            Assert.Equal(2, choices.Count);
            Assert.Equal(2, choices.Keys.Distinct(StringComparer.Ordinal).Count());
            Assert.All(choices.Values, plugin => Assert.Equal("Same Display Name", plugin.Name));
        }

        [Fact]
        public void PluginConfig_BuildPluginChoices_AllowsDuplicateDisplayNamesAndAuthors()
        {
            IPlugin[] plugins =
            [
                new DuplicateDisplayNamePlugin("Same Author"),
                new DuplicateDisplayNamePlugin("Same Author"),
            ];

            var choices = PluginConfig.BuildPluginChoices(plugins);

            Assert.Equal(2, choices.Count);
            Assert.Equal(2, choices.Keys.Distinct(StringComparer.Ordinal).Count());
            Assert.All(choices.Values, plugin => Assert.Equal("Same Display Name", plugin.Name));
        }

        [Fact]
        public void LoadIntoContext_DoesNotCreatePluginDataDirectoryOrSettingsFile()
        {
            using var fixture = new CompiledSettingsPluginFixture();
            var ctx = new PluginManager.PluginLoadContext("SettingsPlugin");

            Assert.True(PluginManager.LoadIntoContext(ctx, fixture.Metadata));

            Assert.False(Directory.Exists(fixture.PluginDataDirectory));
            Assert.False(File.Exists(fixture.SettingsPath));
            ctx.Unload();
        }

        [Fact]
        public void LoadIntoContext_DoesNotReadOrRewritePluginSettingsFile()
        {
            using var fixture = new CompiledSettingsPluginFixture();
            const string settingsYaml = "Value: 99\n";
            Directory.CreateDirectory(Path.GetDirectoryName(fixture.SettingsPath)!);
            File.WriteAllText(fixture.SettingsPath, settingsYaml);
            var ctx = new PluginManager.PluginLoadContext("SettingsPlugin");

            Assert.True(PluginManager.LoadIntoContext(ctx, fixture.Metadata));

            var plugin = Assert.Single(PluginManager.LoadedPlugins, x => x.Name == "SettingsPlugin");
            var value = (int)plugin.GetType().GetProperty("Value")!.GetValue(plugin)!;
            Assert.Equal(1, value);
            Assert.Equal(settingsYaml, File.ReadAllText(fixture.SettingsPath).Replace("\r\n", "\n"));
            ctx.Unload();
        }

        [Fact]
        public void LoadMetadatas_ZipPackage_RegistersOnlyPackageNamedRootDllAsPlugin()
        {
            var originalCwd = Directory.GetCurrentDirectory();
            var tempDir = Path.Combine(Path.GetTempPath(), "ura-plugin-zip-" + Guid.NewGuid().ToString("N"));
            try
            {
                var pluginsDir = Path.Combine(tempDir, "Plugins");
                Directory.CreateDirectory(pluginsDir);

                var pluginDll = Path.Combine(tempDir, "Notifications.dll");
                var dependencyDll = Path.Combine(tempDir, "Newtonsoft.Json.dll");
                PluginCompiler.Compile(
                    """
                    using System.Threading.Tasks;
                    using Spectre.Console;
                    using UmamusumeResponseAnalyzer.Plugin;

                    public sealed class NotificationsPlugin : IPlugin
                    {
                        public string Name => "Notifications";
                        public string Author => "Test";
                        public string[] Targets => System.Array.Empty<string>();

                        public void Initialize(IPluginContext context) { }
                        public Task UpdatePlugin(ProgressContext ctx) => Task.CompletedTask;
                    }
                    """,
                    "Notifications",
                    pluginDll);
                PluginCompiler.Compile(
                    "public sealed class DependencyType { }",
                    "Newtonsoft.Json",
                    dependencyDll);

                using (var zip = ZipFile.Open(Path.Combine(pluginsDir, "Notifications.zip"), ZipArchiveMode.Create))
                {
                    zip.CreateEntryFromFile(pluginDll, "Notifications.dll");
                    zip.CreateEntryFromFile(dependencyDll, "Newtonsoft.Json.dll");
                }

                Directory.SetCurrentDirectory(tempDir);

                PluginManager.LoadMetadatas();
                PluginManager.BuildGroups();
                PluginManager.LoadPlugins();

                Assert.True(PluginManager.Metadatas.ContainsKey("Notifications"));
                Assert.False(PluginManager.Metadatas.ContainsKey("Newtonsoft.Json"));
                Assert.True(PluginManager.AssemblyMetadatas.ContainsKey("Newtonsoft.Json"));
                Assert.DoesNotContain(PluginManager.FailedPlugins, x => x.Contains("Newtonsoft.Json", StringComparison.Ordinal));
                Assert.Contains(PluginManager.LoadedPlugins, x => x.Name == "Notifications");
            }
            finally
            {
                Directory.SetCurrentDirectory(originalCwd);
                try { Directory.Delete(tempDir, recursive: true); } catch { }
                ResetPluginState();
            }
        }

        static bool RouteExists(IPlugin plugin, string route)
            => Server.Instance.Routes.PreAuthentication.Static.Exists(WatsonHttpMethod.GET, $"/{plugin.Name}/{route}");

        static void ResetPluginState()
        {
            PluginManager.RequestAnalyzerMethods.Clear();
            PluginManager.ResponseAnalyzerMethods.Clear();
            PluginManager.ClearHostEventSubscriptions();
            PluginManager.Metadatas.Clear();
            PluginManager.AssemblyMetadatas.Clear();
            PluginManager.FailedPlugins.Clear();
            PluginManager.ContextGroups.Clear();
            foreach (var context in PluginManager.Contexts.Values)
                context.Unload();
            PluginManager.Contexts.Clear();
            PluginManager.AssemblyMap.Clear();
            PluginManager.Assemblies.Clear();
            foreach (var plugin in PluginManager.LoadedPlugins.ToList())
            {
                KeyboardManager.UnregisterByOwner(plugin);
            }
            PluginManager.LoadedPlugins.Clear();
            RemoveRouteIfExists("/PartiallyInvalidPlugin/valid");
            RemoveRouteIfExists("/RoutePlugin/ok");
            RemoveRouteIfExists("/InvalidRoutePlugin/bad");
            RemoveRouteIfExists("/FailingInitializePlugin/boom");
        }

        static void RemoveRouteIfExists(string path)
        {
            if (Server.Instance.Routes.PreAuthentication.Static.Exists(WatsonHttpMethod.GET, path))
                Server.Instance.Routes.PreAuthentication.Static.Remove(WatsonHttpMethod.GET, path);
        }

        static void SeedConfig()
        {
            var current = typeof(Config).GetProperty("Current", BindingFlags.NonPublic | BindingFlags.Static)!;
            if (current.GetValue(null) is null)
                current.SetValue(null, new YamlConfig
                {
                    Core = new(),
                    Repository = new(),
                    Plugin = new(),
                    Updater = new(),
                    Language = new(),
                    Misc = new(),
                });
        }

        abstract class TestPlugin(string name) : IPlugin
        {
            public string Name { get; } = name;
            public string Author => "Test";
            public string[] Targets => [];

            public virtual void Initialize(IPluginContext context)
            {
            }

            public Task UpdatePlugin(ProgressContext ctx)
                => Task.CompletedTask;
        }

        sealed class CompiledSettingsPluginFixture : IDisposable
        {
            readonly string originalCwd = Directory.GetCurrentDirectory();
            readonly string tempDir = Path.Combine(Path.GetTempPath(), "ura-plugin-settings-" + Guid.NewGuid().ToString("N"));

            public CompiledSettingsPluginFixture()
            {
                Directory.CreateDirectory(Path.Combine(tempDir, "Plugins"));
                Directory.SetCurrentDirectory(tempDir);
                var dllPath = Path.Combine(tempDir, "Plugins", "SettingsPlugin.dll");
                PluginCompiler.Compile(
                    """
                    using System.Threading.Tasks;
                    using Spectre.Console;
                    using UmamusumeResponseAnalyzer.Plugin;

                    public sealed class SettingsPlugin : IPlugin
                    {
                        public string Name => "SettingsPlugin";
                        public string Author => "Test";
                        public string[] Targets => System.Array.Empty<string>();

                        public int Value { get; set; } = 1;

                        public void Initialize(IPluginContext context) { }
                        public Task UpdatePlugin(ProgressContext ctx) => Task.CompletedTask;
                    }
                    """,
                    "SettingsPlugin",
                    dllPath);

                Metadata = new PluginManager.PluginMetadata(dllPath, "SettingsPlugin", loadInHost: false, shared: [], isFromZip: false);
                PluginDataDirectory = Path.Combine(tempDir, "PluginData", "SettingsPlugin");
                SettingsPath = Path.Combine(PluginDataDirectory, "settings.yaml");
            }

            public PluginManager.PluginMetadata Metadata { get; }
            public string PluginDataDirectory { get; }
            public string SettingsPath { get; }

            public void Dispose()
            {
                ResetPluginState();
                Directory.SetCurrentDirectory(originalCwd);
                try { Directory.Delete(tempDir, recursive: true); } catch { }
            }
        }

        sealed class PartiallyInvalidPlugin : TestPlugin
        {
            public PartiallyInvalidPlugin() : base("PartiallyInvalidPlugin")
            {
            }

            [Route(WatsonHttpMethod.GET, "valid")]
            public Task ValidRoute(HttpContextBase ctx)
                => Task.CompletedTask;

            [ResponseAnalyzer<GameApi.Account.Index>]
            public ValueTask Valid(byte[] payload)
            {
                return ValueTask.CompletedTask;
            }

            [ResponseAnalyzer<GameApi.Account.Index>]
            public void Invalid(DataLinkIndexResponse response)
            {
            }
        }

        sealed class RoutePlugin : TestPlugin
        {
            public RoutePlugin() : base("RoutePlugin")
            {
            }

            [Route(WatsonHttpMethod.GET, "ok")]
            public Task Ok(HttpContextBase ctx)
                => Task.CompletedTask;
        }

        sealed class InvalidRoutePlugin : TestPlugin
        {
            public InvalidRoutePlugin() : base("InvalidRoutePlugin")
            {
            }

            [Route(WatsonHttpMethod.GET, "bad")]
            public Task Bad()
                => Task.CompletedTask;
        }

        sealed class ContextInitializePlugin : TestPlugin
        {
            public ContextInitializePlugin() : base("ContextInitializePlugin")
            {
            }

            public bool Initialized { get; private set; }
            public IPluginContext? Context { get; private set; }

            public override void Initialize(IPluginContext context)
            {
                Initialized = true;
                Context = context;
            }
        }

        sealed class FailingInitializePlugin : TestPlugin
        {
            public FailingInitializePlugin() : base("FailingInitializePlugin")
            {
            }

            public int StartedCalls { get; private set; }

            [Route(WatsonHttpMethod.GET, "boom")]
            public Task Boom(HttpContextBase ctx)
                => Task.CompletedTask;

            [ResponseAnalyzer<GameApi.Account.Index>]
            public ValueTask Analyze(byte[] payload)
                => ValueTask.CompletedTask;

            public override void Initialize(IPluginContext context)
            {
                context.Events.OnStarted(_ =>
                {
                    StartedCalls++;
                    return ValueTask.CompletedTask;
                });
                context.Analyzers.RegisterResponse<GameApi.Account.Index>(_ => ValueTask.CompletedTask);
                throw new InvalidOperationException("plugin initialize failed");
            }
        }

        sealed class StartedFailurePlugin : TestPlugin
        {
            public StartedFailurePlugin() : base("StartedFailurePlugin")
            {
            }

            public override void Initialize(IPluginContext context)
            {
                context.Events.OnStarted(_ => throw new InvalidOperationException("started failed"));
            }
        }

        sealed class StartedCounterPlugin : TestPlugin
        {
            public StartedCounterPlugin() : base("StartedCounterPlugin")
            {
            }

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

        sealed class ConfigPromptPlugin : TestPlugin
        {
            public ConfigPromptPlugin() : base("ConfigPromptPlugin")
            {
            }

            public int ConfigPromptCalls { get; private set; }

            public Task ConfigPromptAsync()
            {
                ConfigPromptCalls++;
                return Task.CompletedTask;
            }
        }

        sealed class NoConfigPromptPlugin : TestPlugin
        {
            public NoConfigPromptPlugin() : base("NoConfigPromptPlugin")
            {
            }

            public int UpdateCalls { get; private set; }

            public new Task UpdatePlugin(ProgressContext ctx)
            {
                UpdateCalls++;
                return Task.CompletedTask;
            }
        }

        sealed class LegacyConfigPromptPlugin : TestPlugin
        {
            public LegacyConfigPromptPlugin() : base("LegacyConfigPromptPlugin")
            {
            }

            public int LegacyConfigPromptCalls { get; private set; }

            public void ConfigPrompt()
            {
                LegacyConfigPromptCalls++;
            }
        }

        sealed class ReloadingConfigPromptPlugin : TestPlugin
        {
            public ReloadingConfigPromptPlugin() : base("ReloadingConfigPromptPlugin")
            {
            }

            public Exception? ReloadException { get; private set; }

            public async Task ConfigPromptAsync()
            {
                await Task.Yield();
                ReloadException = Assert.Throws<InvalidOperationException>(() => PluginManager.ReloadPlugins("AnyPlugin"));
            }
        }

        sealed class DuplicateDisplayNamePlugin(string author) : IPlugin
        {
            public string Name => "Same Display Name";
            public string Author => author;
            public string[] Targets => [];
            public void Initialize(IPluginContext context) { }
            public Task UpdatePlugin(ProgressContext ctx) => Task.CompletedTask;
        }

        sealed class FakeLiveDisplayOutput : ILiveDisplayOutput
        {
            public LiveDisplayWorkspace CreateWorkspace(string id, string title) => LiveDisplayWorkspace.Create(id, title);
            public void SwitchWorkspace(LiveDisplayWorkspace workspace) { }
            public void BindWorkspaceHotkey(LiveDisplayWorkspace workspace, ConsoleKey key, ConsoleModifiers modifiers = 0, string? description = null) { }
            public void SetPanel(LiveDisplayWorkspace workspace, string key, string title, Spectre.Console.Rendering.IRenderable content, bool fullBleed = false) { }
            public void Log(LiveDisplayWorkspace workspace, string text, LiveDisplaySeverity severity = LiveDisplaySeverity.Info) { }
            public void MarkupLog(LiveDisplayWorkspace workspace, string markup, LiveDisplaySeverity severity = LiveDisplaySeverity.Info) { }
            public void Notify(LiveDisplayWorkspace workspace, string text, LiveDisplaySeverity severity = LiveDisplaySeverity.Info, TimeSpan? ttl = null) { }
        }

    }
}
