using System.Reflection;
using System.Runtime.Loader;
using Gallop;
using Gallop.Endpoints;
using MessagePack;
using Newtonsoft.Json.Linq;
using Spectre.Console;
using Spectre.Console.Rendering;
using UmamusumeResponseAnalyzer.LiveDisplay;
using UmamusumeResponseAnalyzer.Plugin;
using Xunit;

namespace UmamusumeResponseAnalyzer.Tests
{
    [Collection("PluginReload")]
    public sealed class PluginAnalyzerTests
    {
        public PluginAnalyzerTests()
        {
            SeedConfig();
            ResetAnalyzerState();
        }

        [Fact]
        public void RegisterMethods_RegistersTypedRequestAndResponseAnalyzers()
        {
            var plugin = new TypedAnalyzerPlugin();

            PluginManager.RegisterMethods(plugin);

            var request = Assert.Single(PluginManager.RequestAnalyzerMethods[10]);
            Assert.Same(plugin, request.Plugin);
            Assert.Equal(nameof(TypedAnalyzerPlugin.OnRequest), request.Method.Name);
            Assert.Equal(typeof(GameApi.Account.Index), request.EndpointType);
            Assert.Equal(AnalyzerKind.Request, request.Kind);
            Assert.Equal(AnalyzerPayloadKind.Dto, request.PayloadKind);
            Assert.Equal(typeof(DataLinkIndexRequest), request.Endpoint.RequestType);

            var response = Assert.Single(PluginManager.ResponseAnalyzerMethods[20]);
            Assert.Same(plugin, response.Plugin);
            Assert.Equal(nameof(TypedAnalyzerPlugin.OnResponse), response.Method.Name);
            Assert.Equal(typeof(GameApi.Account.Index), response.EndpointType);
            Assert.Equal(AnalyzerKind.Response, response.Kind);
            Assert.Equal(AnalyzerPayloadKind.Dto, response.PayloadKind);
            Assert.Equal(typeof(DataLinkIndexResponse), response.Endpoint.ResponseType);
        }

        [Fact]
        public void RegisterMethods_RegistersRawRequestAndResponseAnalyzers()
        {
            var plugin = new RawAnalyzerPlugin();

            PluginManager.RegisterMethods(plugin);

            var request = Assert.Single(PluginManager.RequestAnalyzerMethods[1]);
            Assert.Equal(AnalyzerKind.Request, request.Kind);
            Assert.Equal(AnalyzerPayloadKind.RawMessagePack, request.PayloadKind);

            var response = Assert.Single(PluginManager.ResponseAnalyzerMethods[2]);
            Assert.Equal(AnalyzerKind.Response, response.Kind);
            Assert.Equal(AnalyzerPayloadKind.RawMessagePack, response.PayloadKind);
        }

        [Fact]
        public void RegisterMethods_RegistersEveryAnalyzerAttributeOnSameMethod()
        {
            var plugin = new MultiAttributePlugin();

            PluginManager.RegisterMethods(plugin);

            var registrations = PluginManager.ResponseAnalyzerMethods[5];
            Assert.Equal(2, registrations.Count);
            Assert.Contains(registrations, x => x.EndpointType == typeof(GameApi.Account.Index));
            Assert.Contains(registrations, x => x.EndpointType == typeof(GameApi.Banner.Url));
        }

        [Fact]
        public void RegisterMethods_FailsFastForWrongDtoParameterType()
        {
            var plugin = new WrongParameterPlugin();

            var ex = Assert.Throws<InvalidOperationException>(() => PluginManager.RegisterMethods(plugin));

            Assert.Contains(nameof(WrongParameterPlugin.OnResponse), ex.Message);
            Assert.Contains(typeof(GameApi.Account.Index).FullName!, ex.Message);
            Assert.Contains(typeof(DataLinkIndexResponse).FullName!, ex.Message);
            Assert.Contains(typeof(DataLinkIndexRequest).FullName!, ex.Message);
        }

        [Fact]
        public void RegisterMethods_FailsFastForAsyncAnalyzer()
        {
            var taskPlugin = new TaskAnalyzerPlugin();
            var asyncVoidPlugin = new AsyncVoidAnalyzerPlugin();

            var taskEx = Assert.Throws<InvalidOperationException>(() => PluginManager.RegisterMethods(taskPlugin));
            var asyncVoidEx = Assert.Throws<InvalidOperationException>(() => PluginManager.RegisterMethods(asyncVoidPlugin));

            Assert.Contains("return=System.Threading.Tasks.Task", taskEx.Message);
            Assert.Contains("async-state-machine", asyncVoidEx.Message);
        }

        [Fact]
        public void RegisterMethods_FailsFastForEndpointTypeMissingFromCatalog()
        {
            var plugin = new UnknownEndpointPlugin();

            var ex = Assert.Throws<InvalidOperationException>(() => PluginManager.RegisterMethods(plugin));

            Assert.Contains(typeof(UnknownEndpoint).FullName!, ex.Message);
            Assert.Contains(nameof(GameEndpointCatalog.ByEndpointType), ex.Message);
        }

        [Fact]
        public void ResolveEndpoint_UsesExactGameEndpointCatalogPath()
        {
            var descriptor = Server.ResolveEndpoint("/account/index");

            Assert.Equal(typeof(GameApi.Account.Index), descriptor.EndpointType);
            Assert.Throws<KeyNotFoundException>(() => Server.ResolveEndpoint("/account/index/"));
        }

        [Fact]
        public void ResolveEndpoint_AcceptsAbsoluteCanonicalUrlWithQueryAndHash()
        {
            var descriptor = Server.ResolveEndpoint("https://example.test/account/index?viewer_id=1#fragment");

            Assert.Equal(typeof(GameApi.Account.Index), descriptor.EndpointType);
        }

        [Theory]
        [InlineData("/account/index/")]
        [InlineData("/unknown/path")]
        public void ResolveEndpoint_FailsFastForUnknownPath(string canonicalUrl)
        {
            Assert.Throws<KeyNotFoundException>(() => Server.ResolveEndpoint(canonicalUrl));
        }

        [Theory]
        [InlineData("")]
        [InlineData("account/index")]
        [InlineData("?path=/account/index")]
        public void ResolveEndpoint_FailsFastForInvalidPath(string canonicalUrl)
        {
            Assert.Throws<FormatException>(() => Server.ResolveEndpoint(canonicalUrl));
        }

        [Fact]
        public void DispatchResponse_CallsRawAndDtoAnalyzersInPriorityOrder()
        {
            var plugin = new ResponseDispatchPlugin();
            PluginManager.RegisterMethods(plugin);
            var payload = MessagePackSerializer.Serialize(new DataLinkIndexResponse
            {
                data = new DataLinkIndexResponse.CommonResponse
                {
                    open_date = "2026-06-30",
                },
            });

            Server.DispatchResponse("/account/index", payload);

            Assert.Equal(1, plugin.DtoCalls);
            Assert.Equal(1, plugin.RawCalls);
            Assert.Equal(["raw", "dto"], plugin.CallOrder);
            Assert.Equal("2026-06-30", plugin.LastResponse?.data.open_date);
            Assert.Equal(payload, plugin.LastPayload);
        }

        [Fact]
        public void DispatchResponse_FailsFastAtDtoRegistrationAndDoesNotCallLaterRaw()
        {
            var plugin = new ResponseDispatchPlugin();
            PluginManager.RegisterMethods(plugin);

            var ex = Assert.Throws<InvalidOperationException>(() => Server.DispatchResponse("/account/index", [0xC1]));

            Assert.Contains("Gallop DTO", ex.Message);
            Assert.Equal(0, plugin.DtoCalls);
            Assert.Equal(0, plugin.RawCalls);
        }

        [Fact]
        public void DispatchResponse_CallsRawAnalyzerWhenNoDtoAnalyzerExists()
        {
            var plugin = new RawResponseDispatchPlugin();
            PluginManager.RegisterMethods(plugin);

            Server.DispatchResponse("/account/index", [0xC1]);

            Assert.Equal(1, plugin.RawCalls);
            Assert.Equal([0xC1], plugin.LastPayload);
        }

        [Fact]
        public void DispatchResponse_DebugFilesKeepMsgpackRawAndPutCanonicalUrlInMetadata()
        {
            var previous = Config.Misc.SaveResponseForDebug;
            var originalCwd = Directory.GetCurrentDirectory();
            var tempDir = Path.Combine(Path.GetTempPath(), "ura-debug-packets-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            Directory.SetCurrentDirectory(tempDir);
            Config.Misc.SaveResponseForDebug = true;

            try
            {
                var canonicalUrl = "https://example.test/account/index?viewer_id=1#fragment";
                byte[] payload = [0xC0];

                Server.DispatchResponse(canonicalUrl, payload);

                var packetsDir = Path.Combine(tempDir, "packets");
                var msgpack = Assert.Single(Directory.GetFiles(packetsDir, "*.msgpack"));
                Assert.Contains(Uri.EscapeDataString(canonicalUrl), Path.GetFileName(msgpack), StringComparison.Ordinal);
                Assert.Equal(payload, File.ReadAllBytes(msgpack));

#if DEBUG
                var jsonPath = Assert.Single(Directory.GetFiles(packetsDir, "*.json"));
                var debugJson = JObject.Parse(File.ReadAllText(jsonPath));
                Assert.Equal(canonicalUrl, (string?)debugJson["url"]);
                Assert.Equal(JTokenType.Null, debugJson["payload"]!.Type);
#endif
            }
            finally
            {
                Config.Misc.SaveResponseForDebug = previous;
                Directory.SetCurrentDirectory(originalCwd);
                Directory.Delete(tempDir, recursive: true);
            }
        }

        [Fact]
        public void DispatchResponse_DebugFilesAreSavedBeforeUnknownEndpointFails()
        {
            var previous = Config.Misc.SaveResponseForDebug;
            var originalCwd = Directory.GetCurrentDirectory();
            var tempDir = Path.Combine(Path.GetTempPath(), "ura-debug-packets-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            Directory.SetCurrentDirectory(tempDir);
            Config.Misc.SaveResponseForDebug = true;

            try
            {
                var canonicalUrl = "https://example.test/unknown/path?viewer_id=1#fragment";
                byte[] payload = [0xC0];

                Assert.Throws<KeyNotFoundException>(() => Server.DispatchResponse(canonicalUrl, payload));

                var packetsDir = Path.Combine(tempDir, "packets");
                var msgpack = Assert.Single(Directory.GetFiles(packetsDir, "*.msgpack"));
                Assert.Contains(Uri.EscapeDataString(canonicalUrl), Path.GetFileName(msgpack), StringComparison.Ordinal);
                Assert.Equal(payload, File.ReadAllBytes(msgpack));

#if DEBUG
                var jsonPath = Assert.Single(Directory.GetFiles(packetsDir, "*.json"));
                var debugJson = JObject.Parse(File.ReadAllText(jsonPath));
                Assert.Equal(canonicalUrl, (string?)debugJson["url"]);
#endif
            }
            finally
            {
                Config.Misc.SaveResponseForDebug = previous;
                Directory.SetCurrentDirectory(originalCwd);
                Directory.Delete(tempDir, recursive: true);
            }
        }

        [Fact]
        public void DispatchResponse_RethrowsAnalyzerException()
        {
            var plugin = new ThrowingResponsePlugin();
            PluginManager.RegisterMethods(plugin);
            var payload = MessagePackSerializer.Serialize(new DataLinkIndexResponse());

            var ex = Assert.Throws<InvalidOperationException>(() => Server.DispatchResponse("/account/index", payload));

            Assert.Equal("analyzer failed", ex.Message);
        }

        [Fact]
        public void DispatchRequest_CallsRawAndDtoAnalyzersInPriorityOrder()
        {
            var plugin = new RequestDispatchPlugin();
            PluginManager.RegisterMethods(plugin);
            var payload = MessagePackSerializer.Serialize(new DataLinkIndexRequest());

            Server.DispatchRequest("https://example.test/account/index?viewer_id=1#fragment", payload);

            Assert.Equal(1, plugin.DtoCalls);
            Assert.Equal(1, plugin.RawCalls);
            Assert.Equal(["raw", "dto"], plugin.CallOrder);
            Assert.NotNull(plugin.LastRequest);
            Assert.Equal(payload, plugin.LastPayload);
        }

        [Fact]
        public void DispatchRequest_CallsRawAnalyzerWhenNoDtoAnalyzerExists()
        {
            var plugin = new RawRequestDispatchPlugin();
            PluginManager.RegisterMethods(plugin);

            Server.DispatchRequest("/account/index?viewer_id=1", [0xC0]);

            Assert.Equal(1, plugin.RawCalls);
            Assert.Equal([0xC0], plugin.LastPayload);
        }

        [Fact]
        public async Task InitializePlugin_PassesPluginContextAndDisposesStartedSubscription()
        {
            var plugin = new ContextPlugin();
            var liveDisplay = new FakeLiveDisplayOutput();
            PluginManager.BindLiveDisplay(_ => liveDisplay);

            PluginManager.InitializePlugin(plugin);

            Assert.NotNull(plugin.Context);
            Assert.Same(liveDisplay, plugin.Context!.LiveDisplay);
            Assert.NotNull(plugin.Context.Events);

            await PluginManager.TriggerStartedForPluginsAsync([plugin]);
            Assert.Equal(1, plugin.StartedCalls);

            PluginManager.DisposeHostEventSubscriptions(plugin);
            await PluginManager.TriggerStartedForPluginsAsync([plugin]);
            Assert.Equal(1, plugin.StartedCalls);
        }

        [Fact]
        public void PluginLoadContext_ResolvesSharedAbiAssemblyFromDefaultContext()
        {
            var ctx = new PluginManager.PluginLoadContext("shared-abi-test");
            var recording = new StringWriter();
            var originalConsole = AnsiConsole.Console;
            AnsiConsole.Console = AnsiConsole.Create(new AnsiConsoleSettings { Out = new AnsiConsoleOutput(recording) });
            try
            {
                var host = ctx.LoadFromAssemblyName(typeof(IPlugin).Assembly.GetName());

                Assert.Same(typeof(Server).Assembly, host);

                var oldHostVersion = new AssemblyName(typeof(Server).Assembly.GetName().Name!)
                {
                    Version = new Version(0, 0, 0, 0),
                };
                Assert.Same(typeof(Server).Assembly, ctx.LoadFromAssemblyName(oldHostVersion));

                var futureHostVersion = new AssemblyName(typeof(Server).Assembly.GetName().Name!)
                {
                    Version = new Version(99, 0, 0, 0),
                };
                Assert.Same(typeof(Server).Assembly, ctx.LoadFromAssemblyName(futureHostVersion));
                Assert.Contains("插件依赖的宿主 ABI 版本更高", recording.ToString());

                var wrongSpectreVersion = new AssemblyName(typeof(ProgressContext).Assembly.GetName().Name!)
                {
                    Version = new Version(99, 0, 0, 0),
                };
                Assert.Throws<FileLoadException>(() => ctx.LoadFromAssemblyName(wrongSpectreVersion));
            }
            finally
            {
                AnsiConsole.Console = originalConsole;
                ctx.Unload();
            }
        }

        static void ResetAnalyzerState()
        {
            PluginManager.RequestAnalyzerMethods.Clear();
            PluginManager.ResponseAnalyzerMethods.Clear();
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

        abstract class TestPlugin : IPlugin
        {
            public string Name => GetType().Name;
            public string Author => "Test";
            public string[] Targets => [];
            public Task UpdatePlugin(ProgressContext ctx) => Task.CompletedTask;
        }

        sealed class TypedAnalyzerPlugin : TestPlugin
        {
            [RequestAnalyzer<GameApi.Account.Index>(10)]
            public void OnRequest(DataLinkIndexRequest request)
            {
            }

            [ResponseAnalyzer<GameApi.Account.Index>(20)]
            public void OnResponse(DataLinkIndexResponse response)
            {
            }
        }

        sealed class RawAnalyzerPlugin : TestPlugin
        {
            [RawRequestAnalyzer<GameApi.Account.Index>(1)]
            public void OnRequest(byte[] payload)
            {
            }

            [RawResponseAnalyzer<GameApi.Account.Index>(2)]
            public void OnResponse(byte[] payload)
            {
            }
        }

        sealed class MultiAttributePlugin : TestPlugin
        {
            [RawResponseAnalyzer<GameApi.Account.Index>(5)]
            [RawResponseAnalyzer<GameApi.Banner.Url>(5)]
            public void OnResponse(byte[] payload)
            {
            }
        }

        sealed class WrongParameterPlugin : TestPlugin
        {
            [ResponseAnalyzer<GameApi.Account.Index>]
            public void OnResponse(DataLinkIndexRequest request)
            {
            }
        }

        sealed class TaskAnalyzerPlugin : TestPlugin
        {
            [ResponseAnalyzer<GameApi.Account.Index>]
            public Task OnResponse(DataLinkIndexResponse response)
                => Task.CompletedTask;
        }

        sealed class AsyncVoidAnalyzerPlugin : TestPlugin
        {
            [ResponseAnalyzer<GameApi.Account.Index>]
            public async void OnResponse(DataLinkIndexResponse response)
            {
                await Task.Yield();
            }
        }

        sealed class UnknownEndpointPlugin : TestPlugin
        {
            [UnknownAnalyzer]
            public void OnUnknown(object payload)
            {
            }
        }

        sealed class UnknownAnalyzerAttribute()
            : AnalyzerAttribute(typeof(UnknownEndpoint), AnalyzerKind.Response, AnalyzerPayloadKind.Dto)
        {
        }

        sealed class UnknownEndpoint : IGameEndpoint
        {
        }

        sealed class ResponseDispatchPlugin : TestPlugin
        {
            public int DtoCalls { get; private set; }
            public int RawCalls { get; private set; }
            public DataLinkIndexResponse? LastResponse { get; private set; }
            public byte[]? LastPayload { get; private set; }
            public List<string> CallOrder { get; } = [];

            [ResponseAnalyzer<GameApi.Account.Index>(20)]
            public void OnDto(DataLinkIndexResponse response)
            {
                DtoCalls++;
                CallOrder.Add("dto");
                LastResponse = response;
            }

            [RawResponseAnalyzer<GameApi.Account.Index>(10)]
            public void OnRaw(byte[] payload)
            {
                RawCalls++;
                CallOrder.Add("raw");
                LastPayload = payload;
            }
        }

        sealed class RawResponseDispatchPlugin : TestPlugin
        {
            public int RawCalls { get; private set; }
            public byte[]? LastPayload { get; private set; }

            [RawResponseAnalyzer<GameApi.Account.Index>]
            public void OnRaw(byte[] payload)
            {
                RawCalls++;
                LastPayload = payload;
            }
        }

        sealed class ThrowingResponsePlugin : TestPlugin
        {
            [ResponseAnalyzer<GameApi.Account.Index>]
            public void OnDto(DataLinkIndexResponse response)
            {
                throw new InvalidOperationException("analyzer failed");
            }
        }

        sealed class RequestDispatchPlugin : TestPlugin
        {
            public int DtoCalls { get; private set; }
            public int RawCalls { get; private set; }
            public DataLinkIndexRequest? LastRequest { get; private set; }
            public byte[]? LastPayload { get; private set; }
            public List<string> CallOrder { get; } = [];

            [RequestAnalyzer<GameApi.Account.Index>(20)]
            public void OnDto(DataLinkIndexRequest request)
            {
                DtoCalls++;
                CallOrder.Add("dto");
                LastRequest = request;
            }

            [RawRequestAnalyzer<GameApi.Account.Index>(10)]
            public void OnRaw(byte[] payload)
            {
                RawCalls++;
                CallOrder.Add("raw");
                LastPayload = payload;
            }
        }

        sealed class RawRequestDispatchPlugin : TestPlugin
        {
            public int RawCalls { get; private set; }
            public byte[]? LastPayload { get; private set; }

            [RawRequestAnalyzer<GameApi.Account.Index>]
            public void OnRaw(byte[] payload)
            {
                RawCalls++;
                LastPayload = payload;
            }
        }

        sealed class ContextPlugin : TestPlugin
        {
            public IPluginContext? Context { get; private set; }
            public int StartedCalls { get; private set; }

            public void Initialize(IPluginContext context)
            {
                Context = context;
                context.Events.OnStarted(_ =>
                {
                    StartedCalls++;
                    return ValueTask.CompletedTask;
                });
            }
        }

        sealed class FakeLiveDisplayOutput : ILiveDisplayOutput
        {
            public LiveDisplayWorkspace CreateWorkspace(string id, string title) => LiveDisplayWorkspace.Create(id, title);
            public void SwitchWorkspace(LiveDisplayWorkspace workspace) { }
            public void BindWorkspaceHotkey(LiveDisplayWorkspace workspace, ConsoleKey key, ConsoleModifiers modifiers = 0, string? description = null) { }
            public void SetPanel(LiveDisplayWorkspace workspace, string key, string title, IRenderable content, bool fullBleed = false) { }
            public void Log(LiveDisplayWorkspace workspace, string text, LiveDisplaySeverity severity = LiveDisplaySeverity.Info) { }
            public void MarkupLog(LiveDisplayWorkspace workspace, string markup, LiveDisplaySeverity severity = LiveDisplaySeverity.Info) { }
            public void Notify(LiveDisplayWorkspace workspace, string text, LiveDisplaySeverity severity = LiveDisplaySeverity.Info, TimeSpan? ttl = null) { }
        }
    }
}
