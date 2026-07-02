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
        const string AccountIndexPath = "/umamusume/account/index";
        const string AccountIndexAbsoluteUrl = "https://example.test/umamusume/account/index?viewer_id=1#fragment";
        const string AccountIndexUrlWithQuery = AccountIndexPath + "?viewer_id=1";

        public PluginAnalyzerTests()
        {
            SeedConfig();
            ResetAnalyzerState();
            PluginManager.BindLiveDisplay(_ => new FakeLiveDisplayOutput());
        }

        [Fact]
        public void RegisterMethods_RegistersTypedRequestAndResponseAnalyzers()
        {
            var plugin = new TypedAnalyzerPlugin();

            PluginManager.RegisterMethods(plugin);

            var request = Assert.Single(PluginManager.RequestAnalyzerMethods[10]);
            Assert.Same(plugin, request.Plugin);
            Assert.Equal(nameof(TypedAnalyzerPlugin.OnRequest), request.Method!.Name);
            Assert.Equal(typeof(GameApi.Account.Index), request.EndpointType);
            Assert.Equal(AnalyzerKind.Request, request.Kind);
            Assert.Equal(typeof(DataLinkIndexRequest), GameEndpointCatalog.ByEndpointType[request.EndpointType].RequestType);

            var response = Assert.Single(PluginManager.ResponseAnalyzerMethods[20]);
            Assert.Same(plugin, response.Plugin);
            Assert.Equal(nameof(TypedAnalyzerPlugin.OnResponse), response.Method!.Name);
            Assert.Equal(typeof(GameApi.Account.Index), response.EndpointType);
            Assert.Equal(AnalyzerKind.Response, response.Kind);
            Assert.Equal(typeof(DataLinkIndexResponse), GameEndpointCatalog.ByEndpointType[response.EndpointType].ResponseType);
        }

        [Fact]
        public void RegisterMethods_RegistersRawRequestAndResponseAnalyzers()
        {
            var plugin = new RawAnalyzerPlugin();

            PluginManager.RegisterMethods(plugin);

            var request = Assert.Single(PluginManager.RequestAnalyzerMethods[1]);
            Assert.Equal(AnalyzerKind.Request, request.Kind);

            var response = Assert.Single(PluginManager.ResponseAnalyzerMethods[2]);
            Assert.Equal(AnalyzerKind.Response, response.Kind);
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
        public void RegisterMethods_FailsFastForInvalidAnalyzerReturnType()
        {
            var taskPlugin = new TaskAnalyzerPlugin();
            var voidPlugin = new VoidAnalyzerPlugin();

            var taskEx = Assert.Throws<InvalidOperationException>(() => PluginManager.RegisterMethods(taskPlugin));
            var voidEx = Assert.Throws<InvalidOperationException>(() => PluginManager.RegisterMethods(voidPlugin));

            Assert.Contains("return=System.Threading.Tasks.Task", taskEx.Message);
            Assert.Contains("return=System.Void", voidEx.Message);
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
            var descriptor = Server.ResolveEndpoint(AccountIndexPath);

            Assert.Equal(typeof(GameApi.Account.Index), descriptor.EndpointType);
            Assert.Throws<KeyNotFoundException>(() => Server.ResolveEndpoint(AccountIndexPath + "/"));
        }

        [Fact]
        public void ResolveEndpoint_AcceptsAbsoluteCanonicalUrlWithQueryAndHash()
        {
            var descriptor = Server.ResolveEndpoint(AccountIndexAbsoluteUrl);

            Assert.Equal(typeof(GameApi.Account.Index), descriptor.EndpointType);
        }

        [Theory]
        [InlineData("/account/index")]
        [InlineData("/umamusume/account/index/")]
        [InlineData("/unknown/path")]
        public void ResolveEndpoint_FailsFastForUnknownPath(string canonicalUrl)
        {
            Assert.Throws<KeyNotFoundException>(() => Server.ResolveEndpoint(canonicalUrl));
        }

        [Theory]
        [InlineData("")]
        [InlineData("account/index")]
        [InlineData("?path=/umamusume/account/index")]
        public void ResolveEndpoint_FailsFastForInvalidPath(string canonicalUrl)
        {
            Assert.Throws<FormatException>(() => Server.ResolveEndpoint(canonicalUrl));
        }

        [Fact]
        public async Task DispatchResponse_CallsRawAndDtoAnalyzersInPriorityOrder()
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

            await Server.DispatchResponse(AccountIndexPath, payload);

            Assert.Equal(1, plugin.DtoCalls);
            Assert.Equal(1, plugin.RawCalls);
            Assert.Equal(["raw", "dto"], plugin.CallOrder);
            Assert.Equal("2026-06-30", plugin.LastResponse?.data.open_date);
            Assert.Equal(payload, plugin.LastPayload);
        }

        [Fact]
        public async Task DispatchResponse_DeserializesDtoAtDtoAnalyzerExecutionPoint()
        {
            var plugin = new ResponseDispatchPlugin();
            PluginManager.RegisterMethods(plugin);

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () => await Server.DispatchResponse(AccountIndexPath, [0xC1]));

            Assert.Contains("Gallop DTO", ex.Message);
            Assert.Equal(0, plugin.DtoCalls);
            Assert.Equal(1, plugin.RawCalls);
        }

        [Fact]
        public async Task DispatchResponse_CallsRawAnalyzerWhenNoDtoAnalyzerExists()
        {
            var plugin = new RawResponseDispatchPlugin();
            PluginManager.RegisterMethods(plugin);

            await Server.DispatchResponse(AccountIndexPath, [0xC1]);

            Assert.Equal(1, plugin.RawCalls);
            Assert.Equal([0xC1], plugin.LastPayload);
        }

        [Fact]
        public async Task DispatchResponse_DebugFilesKeepMsgpackRawAndPutCanonicalUrlInMetadata()
        {
            var previous = Config.Misc.SaveResponseForDebug;
            var originalCwd = Directory.GetCurrentDirectory();
            var tempDir = Path.Combine(Path.GetTempPath(), "ura-debug-packets-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            Directory.SetCurrentDirectory(tempDir);
            Config.Misc.SaveResponseForDebug = true;

            try
            {
                var canonicalUrl = AccountIndexAbsoluteUrl;
                byte[] payload = [0xC0];

                await Server.DispatchResponse(canonicalUrl, payload);

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
        public async Task DispatchResponse_DebugFilesAreSavedBeforeUnknownEndpointFails()
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

                await Assert.ThrowsAsync<KeyNotFoundException>(async () => await Server.DispatchResponse(canonicalUrl, payload));

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
        public async Task DispatchResponse_RethrowsAnalyzerException()
        {
            var plugin = new ThrowingResponsePlugin();
            PluginManager.RegisterMethods(plugin);
            var payload = MessagePackSerializer.Serialize(new DataLinkIndexResponse());

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () => await Server.DispatchResponse(AccountIndexPath, payload));

            Assert.Equal("analyzer failed", ex.Message);
        }

        [Fact]
        public async Task DispatchRequest_CallsRawAndDtoAnalyzersInPriorityOrder()
        {
            var plugin = new RequestDispatchPlugin();
            PluginManager.RegisterMethods(plugin);
            var payload = MessagePackSerializer.Serialize(new DataLinkIndexRequest());

            await Server.DispatchRequest(AccountIndexAbsoluteUrl, payload);

            Assert.Equal(1, plugin.DtoCalls);
            Assert.Equal(1, plugin.RawCalls);
            Assert.Equal(["raw", "dto"], plugin.CallOrder);
            Assert.NotNull(plugin.LastRequest);
            Assert.Equal(payload, plugin.LastPayload);
        }

        [Fact]
        public async Task DispatchRequest_CallsRawAnalyzerWhenNoDtoAnalyzerExists()
        {
            var plugin = new RawRequestDispatchPlugin();
            PluginManager.RegisterMethods(plugin);

            await Server.DispatchRequest(AccountIndexUrlWithQuery, [0xC0]);

            Assert.Equal(1, plugin.RawCalls);
            Assert.Equal([0xC0], plugin.LastPayload);
        }

        [Fact]
        public async Task DispatchRequestAndResponse_DeliverRandomDtosForEveryCatalogEndpoint()
        {
            var plugin = new CatalogDispatchPlugin();
            PluginManager.InitializePlugin(plugin);

            foreach (var descriptor in GameEndpointCatalog.ByPath.Values.OrderBy(x => x.Path, StringComparer.Ordinal))
            {
                var request = RandomDtoFactory.Create(descriptor.RequestType, $"request:{descriptor.Path}");
                var requestPayload = MessagePackSerializer.Serialize(descriptor.RequestType, request);
                await Server.DispatchRequest(descriptor.Path, requestPayload);

                Assert.True(
                    plugin.RequestDtos.Remove(descriptor.EndpointType, out var receivedRequest),
                    $"Request analyzer was not called for endpoint {descriptor.Path} ({descriptor.EndpointType.FullName}).");
                AssertDtoPayloadMatches(descriptor.RequestType, requestPayload, receivedRequest!, descriptor, "request");

                var response = RandomDtoFactory.Create(descriptor.ResponseType, $"response:{descriptor.Path}");
                var responsePayload = MessagePackSerializer.Serialize(descriptor.ResponseType, response);
                await Server.DispatchResponse(descriptor.Path, responsePayload);

                Assert.True(
                    plugin.ResponseDtos.Remove(descriptor.EndpointType, out var receivedResponse),
                    $"Response analyzer was not called for endpoint {descriptor.Path} ({descriptor.EndpointType.FullName}).");
                AssertDtoPayloadMatches(descriptor.ResponseType, responsePayload, receivedResponse!, descriptor, "response");
            }

            Assert.Empty(plugin.RequestDtos);
            Assert.Empty(plugin.ResponseDtos);
        }

        [Fact]
        public async Task ProgrammaticRegistry_RegistersRawAndDtoAnalyzers()
        {
            var plugin = new ProgrammaticAnalyzerPlugin();
            PluginManager.InitializePlugin(plugin);
            var payload = MessagePackSerializer.Serialize(new DataLinkIndexResponse
            {
                data = new DataLinkIndexResponse.CommonResponse
                {
                    open_date = "2026-07-01",
                },
            });

            await Server.DispatchResponse(AccountIndexPath, payload);

            Assert.Equal(["raw", "dto"], plugin.CallOrder);
            Assert.Equal(payload, plugin.LastPayload);
            Assert.Equal("2026-07-01", plugin.LastResponse?.data.open_date);
        }

        [Fact]
        public async Task ProgrammaticRegistry_DisposePreventsFutureDispatchOnly()
        {
            var plugin = new DisposingProgrammaticAnalyzerPlugin();
            PluginManager.InitializePlugin(plugin);

            await Server.DispatchResponse(AccountIndexPath, MessagePackSerializer.Serialize(new DataLinkIndexResponse()));
            await Server.DispatchResponse(AccountIndexPath, MessagePackSerializer.Serialize(new DataLinkIndexResponse()));

            Assert.Equal(["first", "second", "second"], plugin.CallOrder);
        }

        [Fact]
        public void ProgrammaticRegistry_RejectsByteArrayDtoOverload()
        {
            var plugin = new ByteArrayDtoProgrammaticPlugin();

            var ex = Assert.Throws<InvalidOperationException>(() => PluginManager.InitializePlugin(plugin));

            Assert.Contains("byte[]", ex.Message);
            Assert.Contains("raw", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void ProgrammaticRegistry_RejectsNullHandler()
        {
            var plugin = new NullHandlerProgrammaticPlugin();

            Assert.Throws<ArgumentNullException>(() => PluginManager.InitializePlugin(plugin));
        }

        [Fact]
        public async Task DispatchResponse_SharesDtoInstanceWithinDispatch()
        {
            var plugin = new DtoCacheDispatchPlugin();
            PluginManager.RegisterMethods(plugin);
            var payload = MessagePackSerializer.Serialize(new DataLinkIndexResponse());

            await Server.DispatchResponse(AccountIndexPath, payload);

            Assert.Same(plugin.FirstResponse, plugin.SecondResponse);
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

        static void AssertDtoPayloadMatches(
            Type dtoType,
            byte[] expectedPayload,
            object actualDto,
            GameEndpointDescriptor descriptor,
            string direction)
        {
            var actualPayload = MessagePackSerializer.Serialize(dtoType, actualDto);
            var expectedJson = MessagePackSerializer.ConvertToJson(expectedPayload);
            var actualJson = MessagePackSerializer.ConvertToJson(actualPayload);
            Assert.True(
                expectedJson == actualJson,
                $"{direction} DTO mismatch for endpoint {descriptor.Path} ({descriptor.EndpointType.FullName}).");
        }

        abstract class TestPlugin : IPlugin
        {
            public string Name => GetType().Name;
            public string Author => "Test";
            public string[] Targets => [];
            public virtual void Initialize(IPluginContext context) { }
            public Task UpdatePlugin(ProgressContext ctx) => Task.CompletedTask;
        }

        sealed class RandomDtoFactory(int seed)
        {
            const int MaxDepth = 4;
            readonly Random random = new(seed);

            public static object Create(Type type, string salt)
                => new RandomDtoFactory(StableSeed(type.FullName + ":" + salt)).CreateValue(type, 0)!;

            static int StableSeed(string text)
            {
                unchecked
                {
                    var hash = 17;
                    foreach (var c in text)
                        hash = (hash * 31) + c;
                    return hash;
                }
            }

            object? CreateValue(Type type, int depth)
            {
                if (type == typeof(string))
                    return $"dto-{depth}-{random.Next(1, 1_000_000)}";
                if (type == typeof(bool))
                    return random.Next(0, 2) == 0;
                if (type == typeof(byte))
                    return (byte)random.Next(byte.MinValue, byte.MaxValue + 1);
                if (type == typeof(sbyte))
                    return (sbyte)random.Next(sbyte.MinValue, sbyte.MaxValue + 1);
                if (type == typeof(short))
                    return (short)random.Next(short.MinValue, short.MaxValue);
                if (type == typeof(ushort))
                    return (ushort)random.Next(ushort.MinValue, ushort.MaxValue);
                if (type == typeof(int))
                    return random.Next(-1_000_000, 1_000_000);
                if (type == typeof(uint))
                    return (uint)random.Next(0, 1_000_000);
                if (type == typeof(long))
                    return random.NextInt64(-1_000_000_000, 1_000_000_000);
                if (type == typeof(ulong))
                    return (ulong)random.NextInt64(0, 1_000_000_000);
                if (type == typeof(float))
                    return (float)(random.NextDouble() * 10_000);
                if (type == typeof(double))
                    return random.NextDouble() * 10_000;
                if (type == typeof(decimal))
                    return (decimal)(random.NextDouble() * 10_000);
                if (type.IsEnum)
                {
                    var values = Enum.GetValues(type);
                    return values.Length == 0 ? Activator.CreateInstance(type) : values.GetValue(random.Next(values.Length));
                }
                if (Nullable.GetUnderlyingType(type) is { } nullableType)
                    return CreateValue(nullableType, depth);
                if (type == typeof(byte[]))
                {
                    var bytes = new byte[4];
                    random.NextBytes(bytes);
                    return bytes;
                }
                if (type.IsArray)
                {
                    var elementType = type.GetElementType()!;
                    var length = depth >= MaxDepth ? 0 : 2;
                    var array = Array.CreateInstance(elementType, length);
                    for (var i = 0; i < length; i++)
                        array.SetValue(CreateValue(elementType, depth + 1), i);
                    return array;
                }
                if (type.IsAbstract || type.IsInterface)
                    return type.IsValueType ? Activator.CreateInstance(type) : null;
                if (depth >= MaxDepth && !type.IsValueType)
                    return null;

                var instance = Activator.CreateInstance(type);
                if (instance is null)
                    return null;

                foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.Public))
                    field.SetValue(instance, CreateValue(field.FieldType, depth + 1));
                return instance;
            }
        }

        sealed class CatalogDispatchPlugin : TestPlugin
        {
            static readonly MethodInfo RegisterEndpointMethod = typeof(CatalogDispatchPlugin)
                .GetMethod(nameof(RegisterEndpoint), BindingFlags.NonPublic | BindingFlags.Static)!;

            public Dictionary<Type, object> RequestDtos { get; } = [];
            public Dictionary<Type, object> ResponseDtos { get; } = [];

            public override void Initialize(IPluginContext context)
            {
                foreach (var descriptor in GameEndpointCatalog.ByPath.Values)
                {
                    RegisterEndpointMethod
                        .MakeGenericMethod(descriptor.EndpointType, descriptor.RequestType, descriptor.ResponseType)
                        .Invoke(null, [context.Analyzers, this]);
                }
            }

            static void RegisterEndpoint<TEndpoint, TRequest, TResponse>(
                IPluginAnalyzerRegistry registry,
                CatalogDispatchPlugin plugin)
                where TEndpoint : IGameEndpoint
            {
                registry.RegisterRequest<TEndpoint, TRequest>(request =>
                {
                    plugin.RequestDtos[typeof(TEndpoint)] = request!;
                    return ValueTask.CompletedTask;
                });
                registry.RegisterResponse<TEndpoint, TResponse>(response =>
                {
                    plugin.ResponseDtos[typeof(TEndpoint)] = response!;
                    return ValueTask.CompletedTask;
                });
            }
        }

        sealed class TypedAnalyzerPlugin : TestPlugin
        {
            [RequestAnalyzer<GameApi.Account.Index>(10)]
            public ValueTask OnRequest(DataLinkIndexRequest request)
            {
                return ValueTask.CompletedTask;
            }

            [ResponseAnalyzer<GameApi.Account.Index>(20)]
            public ValueTask OnResponse(DataLinkIndexResponse response)
            {
                return ValueTask.CompletedTask;
            }
        }

        sealed class RawAnalyzerPlugin : TestPlugin
        {
            [RequestAnalyzer<GameApi.Account.Index>(1)]
            public ValueTask OnRequest(byte[] payload)
            {
                return ValueTask.CompletedTask;
            }

            [ResponseAnalyzer<GameApi.Account.Index>(2)]
            public ValueTask OnResponse(byte[] payload)
            {
                return ValueTask.CompletedTask;
            }
        }

        sealed class MultiAttributePlugin : TestPlugin
        {
            [ResponseAnalyzer<GameApi.Account.Index>(5)]
            [ResponseAnalyzer<GameApi.Banner.Url>(5)]
            public ValueTask OnResponse(byte[] payload)
            {
                return ValueTask.CompletedTask;
            }
        }

        sealed class WrongParameterPlugin : TestPlugin
        {
            [ResponseAnalyzer<GameApi.Account.Index>]
            public ValueTask OnResponse(DataLinkIndexRequest request)
            {
                return ValueTask.CompletedTask;
            }
        }

        sealed class TaskAnalyzerPlugin : TestPlugin
        {
            [ResponseAnalyzer<GameApi.Account.Index>]
            public Task OnResponse(DataLinkIndexResponse response)
                => Task.CompletedTask;
        }

        sealed class VoidAnalyzerPlugin : TestPlugin
        {
            [ResponseAnalyzer<GameApi.Account.Index>]
            public void OnResponse(DataLinkIndexResponse response)
            {
            }
        }

        sealed class UnknownEndpointPlugin : TestPlugin
        {
            [UnknownAnalyzer]
            public ValueTask OnUnknown(object payload)
            {
                return ValueTask.CompletedTask;
            }
        }

        sealed class UnknownAnalyzerAttribute()
            : AnalyzerAttribute(typeof(UnknownEndpoint), AnalyzerKind.Response)
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
            public ValueTask OnDto(DataLinkIndexResponse response)
            {
                DtoCalls++;
                CallOrder.Add("dto");
                LastResponse = response;
                return ValueTask.CompletedTask;
            }

            [ResponseAnalyzer<GameApi.Account.Index>(10)]
            public ValueTask OnRaw(byte[] payload)
            {
                RawCalls++;
                CallOrder.Add("raw");
                LastPayload = payload;
                return ValueTask.CompletedTask;
            }
        }

        sealed class RawResponseDispatchPlugin : TestPlugin
        {
            public int RawCalls { get; private set; }
            public byte[]? LastPayload { get; private set; }

            [ResponseAnalyzer<GameApi.Account.Index>]
            public ValueTask OnRaw(byte[] payload)
            {
                RawCalls++;
                LastPayload = payload;
                return ValueTask.CompletedTask;
            }
        }

        sealed class ThrowingResponsePlugin : TestPlugin
        {
            [ResponseAnalyzer<GameApi.Account.Index>]
            public ValueTask OnDto(DataLinkIndexResponse response)
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
            public ValueTask OnDto(DataLinkIndexRequest request)
            {
                DtoCalls++;
                CallOrder.Add("dto");
                LastRequest = request;
                return ValueTask.CompletedTask;
            }

            [RequestAnalyzer<GameApi.Account.Index>(10)]
            public ValueTask OnRaw(byte[] payload)
            {
                RawCalls++;
                CallOrder.Add("raw");
                LastPayload = payload;
                return ValueTask.CompletedTask;
            }
        }

        sealed class RawRequestDispatchPlugin : TestPlugin
        {
            public int RawCalls { get; private set; }
            public byte[]? LastPayload { get; private set; }

            [RequestAnalyzer<GameApi.Account.Index>]
            public ValueTask OnRaw(byte[] payload)
            {
                RawCalls++;
                LastPayload = payload;
                return ValueTask.CompletedTask;
            }
        }

        sealed class ProgrammaticAnalyzerPlugin : TestPlugin
        {
            public byte[]? LastPayload { get; private set; }
            public DataLinkIndexResponse? LastResponse { get; private set; }
            public List<string> CallOrder { get; } = [];

            public override void Initialize(IPluginContext context)
            {
                context.Analyzers.RegisterResponse<GameApi.Account.Index>(payload =>
                {
                    LastPayload = payload;
                    CallOrder.Add("raw");
                    return ValueTask.CompletedTask;
                }, 10);

                context.Analyzers.RegisterResponse<GameApi.Account.Index, DataLinkIndexResponse>(response =>
                {
                    LastResponse = response;
                    CallOrder.Add("dto");
                    return ValueTask.CompletedTask;
                }, 20);
            }
        }

        sealed class DisposingProgrammaticAnalyzerPlugin : TestPlugin
        {
            IDisposable? firstRegistration;
            public List<string> CallOrder { get; } = [];

            public override void Initialize(IPluginContext context)
            {
                firstRegistration = context.Analyzers.RegisterResponse<GameApi.Account.Index>(_ =>
                {
                    CallOrder.Add("first");
                    firstRegistration!.Dispose();
                    firstRegistration.Dispose();
                    return ValueTask.CompletedTask;
                }, 10);

                context.Analyzers.RegisterResponse<GameApi.Account.Index>(_ =>
                {
                    CallOrder.Add("second");
                    return ValueTask.CompletedTask;
                }, 20);
            }
        }

        sealed class ByteArrayDtoProgrammaticPlugin : TestPlugin
        {
            public override void Initialize(IPluginContext context)
            {
                context.Analyzers.RegisterResponse<GameApi.Account.Index, byte[]>(_ => ValueTask.CompletedTask);
            }
        }

        sealed class NullHandlerProgrammaticPlugin : TestPlugin
        {
            public override void Initialize(IPluginContext context)
            {
                Func<byte[], ValueTask> handler = null!;
                context.Analyzers.RegisterResponse<GameApi.Account.Index>(handler);
            }
        }

        sealed class DtoCacheDispatchPlugin : TestPlugin
        {
            public DataLinkIndexResponse? FirstResponse { get; private set; }
            public DataLinkIndexResponse? SecondResponse { get; private set; }

            [ResponseAnalyzer<GameApi.Account.Index>(10)]
            public ValueTask First(DataLinkIndexResponse response)
            {
                FirstResponse = response;
                return ValueTask.CompletedTask;
            }

            [ResponseAnalyzer<GameApi.Account.Index>(20)]
            public ValueTask Second(DataLinkIndexResponse response)
            {
                SecondResponse = response;
                return ValueTask.CompletedTask;
            }
        }

        sealed class ContextPlugin : TestPlugin
        {
            public IPluginContext? Context { get; private set; }
            public int StartedCalls { get; private set; }

            public override void Initialize(IPluginContext context)
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
