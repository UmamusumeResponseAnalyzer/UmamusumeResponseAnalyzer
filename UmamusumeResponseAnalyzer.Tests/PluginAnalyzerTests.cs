using UmamusumeResponseAnalyzer.PluginTesting;
using System.Reflection;
using System.Runtime.Loader;
using System.Runtime.CompilerServices;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using Gallop;
using Gallop.Endpoints;
using MessagePack;
using Newtonsoft.Json.Linq;
using Terminal.Gui.App;
using Terminal.Gui.ViewBase;
using UmamusumeResponseAnalyzer.TerminalGui;
using UmamusumeResponseAnalyzer.Plugin;
using WatsonWebserver.Core;
using WatsonWebserver.Lite;
using Xunit;

namespace UmamusumeResponseAnalyzer.Tests
{
    [Collection("PluginRuntime")]
    public sealed class PluginAnalyzerTests : IDisposable
    {
        readonly IApplication application;
        readonly PluginRuntimeFixture runtime;
        HttpClient? notificationClient;
        bool notificationServerStarted;
        const string NotificationScenarioPrefix = "plugin-analyzer-http:";

        const string AccountIndexPath = "/umamusume/account/index";
        const string AccountIndexPathWithoutPrefix = "/account/index";
        const string AccountIndexAbsoluteUrl = "https://example.test/umamusume/account/index?viewer_id=1#fragment";
        const string AccountIndexAbsoluteUrlWithoutPrefix = "https://l18-prod-all-gs-uma.komoejoy.com/account/index?viewer_id=1#fragment";
        const string AccountIndexUrlWithQuery = AccountIndexPath + "?viewer_id=1";
        const string LegendLoadAbsoluteUrlWithoutPrefix = "https://l18-prod-all-gs-uma.komoejoy.com/single_mode_legend/load";
        const string RamenCheckEventAbsoluteUrl = "https://api.games.umamusume.jp/umamusume/single_mode_ramen/check_event";
        static GameHttpHeaders TestHeaders => new(
            "sid-1",
            "app-2026.07.03",
            "res-2026.07.03",
            "viewer-1",
            "android",
            "tablet");
        static GameHttpHeaders MissingHeaders => new(null, null, null, null, null, null);

        public PluginAnalyzerTests(PluginRuntimeFixture runtime)
        {
            this.runtime = runtime;
            application = runtime.Application;
            PluginManager.ShutdownAsync().GetAwaiter().GetResult();
            PluginManager.Init();
            HotkeyManager.OverlaySink = runtime.Host;
        }

        public void Dispose()
        {
            if (!notificationServerStarted)
                return;

            notificationClient?.Dispose();
            Server.StopAsync().GetAwaiter().GetResult();
            notificationClient = null;
            notificationServerStarted = false;
            TerminalUiLifecycleChildProcess.WriteResult("ok");
        }

        [Fact]
        public void AttributeRegistration_RegistersTypedRequestAndResponseAnalyzers()
        {
            var plugin = new TypedAnalyzerPlugin();
            var requestCount = PluginManager.RequestAnalyzerMethods.Count;
            var responseCount = PluginManager.ResponseAnalyzerMethods.Count;

            PluginManager.InitializePlugin(plugin);

            Assert.Equal(requestCount + 1, PluginManager.RequestAnalyzerMethods.Count);
            Assert.Equal(responseCount + 1, PluginManager.ResponseAnalyzerMethods.Count);
            var request = Assert.Single(
                PluginManager.RequestAnalyzerMethods,
                registration => ReferenceEquals(registration.Plugin, plugin) && registration.Priority == 10);
            Assert.Same(plugin, request.Plugin);
            Assert.Equal(nameof(TypedAnalyzerPlugin.OnRequest), request.Method!.Name);
            Assert.Equal(typeof(GameApi.Account.Index), request.EndpointType);
            Assert.Equal(AnalyzerKind.Request, request.Kind);
            Assert.Equal(typeof(DataLinkIndexRequest), GameEndpointCatalog.ByEndpointType[request.EndpointType].RequestType);

            var response = Assert.Single(
                PluginManager.ResponseAnalyzerMethods,
                registration => ReferenceEquals(registration.Plugin, plugin) && registration.Priority == 20);
            Assert.Same(plugin, response.Plugin);
            Assert.Equal(nameof(TypedAnalyzerPlugin.OnResponse), response.Method!.Name);
            Assert.Equal(typeof(GameApi.Account.Index), response.EndpointType);
            Assert.Equal(AnalyzerKind.Response, response.Kind);
            Assert.Equal(typeof(DataLinkIndexResponse), GameEndpointCatalog.ByEndpointType[response.EndpointType].ResponseType);
        }

        [Fact]
        public void AttributeRegistration_RejectsRawAttributeAnalyzers()
        {
            var plugin = new RawAnalyzerPlugin();

            var exception = Assert.Throws<InvalidOperationException>(
                () => PluginManager.InitializePlugin(plugin));

            Assert.Contains(typeof(ReadOnlyMemory<byte>).FullName!, exception.Message);
            Assert.Contains(typeof(DataLinkIndexRequest).FullName!, exception.Message);
        }

        [Fact]
        public void AttributeRegistration_PreservesDeclarationOrderAtSamePriority()
        {
            var plugin = new MultiAttributePlugin();
            var count = PluginManager.ResponseAnalyzerMethods.Count;

            PluginManager.InitializePlugin(plugin);

            Assert.Equal(count + 2, PluginManager.ResponseAnalyzerMethods.Count);
            var registrations = PluginManager.ResponseAnalyzerMethods
                .Where(registration => ReferenceEquals(registration.Plugin, plugin) && registration.Priority == 5)
                .ToList();
            Assert.Equal(2, registrations.Count);
            Assert.Equal(
                [nameof(MultiAttributePlugin.First), nameof(MultiAttributePlugin.Second)],
                registrations.Select(x => x.Method!.Name));
        }

        [Fact]
        public void AttributeRegistration_FailsFastForWrongDtoParameterType()
        {
            var plugin = new WrongParameterPlugin();

            var ex = Assert.Throws<InvalidOperationException>(() => PluginManager.InitializePlugin(plugin));

            Assert.Contains(nameof(WrongParameterPlugin.OnResponse), ex.Message);
            Assert.Contains(typeof(GameApi.Account.Index).FullName!, ex.Message);
            Assert.Contains(typeof(DataLinkIndexResponse).FullName!, ex.Message);
            Assert.Contains(typeof(DataLinkIndexRequest).FullName!, ex.Message);
        }

        [Fact]
        public void AttributeRegistration_FailsFastForInvalidAnalyzerReturnType()
        {
            var taskPlugin = new TaskAnalyzerPlugin();
            var voidPlugin = new VoidAnalyzerPlugin();

            var taskEx = Assert.Throws<InvalidOperationException>(() => PluginManager.InitializePlugin(taskPlugin));
            var voidEx = Assert.Throws<InvalidOperationException>(() => PluginManager.InitializePlugin(voidPlugin));

            Assert.Contains("return=System.Threading.Tasks.Task", taskEx.Message);
            Assert.Contains("return=System.Void", voidEx.Message);
        }

        [Fact]
        public void AttributeRegistration_FailsFastForInvalidAnalyzerHeadersParameterType()
        {
            var plugin = new WrongHeadersParameterPlugin();

            var ex = Assert.Throws<InvalidOperationException>(() => PluginManager.InitializePlugin(plugin));

            Assert.Contains(nameof(WrongHeadersParameterPlugin.OnResponse), ex.Message);
            Assert.Contains("ValueTask analyzer(TConcreteDto payload)", ex.Message);
            Assert.Contains(typeof(string).FullName!, ex.Message);
        }

        [Fact]
        public void AttributeRegistration_FailsFastForEndpointTypeMissingFromCatalog()
        {
            var plugin = new UnknownEndpointPlugin();

            var ex = Assert.Throws<InvalidOperationException>(() => PluginManager.InitializePlugin(plugin));

            Assert.Contains(typeof(UnknownEndpoint).FullName!, ex.Message);
            Assert.Contains(nameof(GameEndpointCatalog), ex.Message);
        }

        [Fact]
        public void TryResolveEndpoint_UsesExactGameEndpointCatalogPath()
        {
            Assert.True(Server.TryResolveEndpoint(AccountIndexPath, out var descriptor));

            Assert.Equal(typeof(GameApi.Account.Index), descriptor.EndpointType);
            Assert.False(Server.TryResolveEndpoint(AccountIndexPath + "/", out _));
        }

        [Fact]
        public void TryResolveEndpoint_AcceptsAbsoluteCanonicalUrlWithQueryAndHash()
        {
            Assert.True(Server.TryResolveEndpoint(AccountIndexAbsoluteUrl, out var descriptor));

            Assert.Equal(typeof(GameApi.Account.Index), descriptor.EndpointType);
        }

        [Fact]
        public void TryResolveEndpoint_AcceptsPathWithoutUmamusumePrefixWhenCatalogPathExists()
        {
            Assert.True(Server.TryResolveEndpoint(AccountIndexPathWithoutPrefix, out var descriptor));

            Assert.Equal(typeof(GameApi.Account.Index), descriptor.EndpointType);
            Assert.Equal(AccountIndexPath, descriptor.Path);
        }

        [Theory]
        [InlineData(LegendLoadAbsoluteUrlWithoutPrefix, typeof(GameApi.SingleModeLegend.Load))]
        [InlineData(AccountIndexAbsoluteUrlWithoutPrefix, typeof(GameApi.Account.Index))]
        public void TryResolveEndpoint_AcceptsAbsoluteCanonicalUrlWithoutUmamusumePrefix(string canonicalUrl, Type endpointType)
        {
            Assert.True(Server.TryResolveEndpoint(canonicalUrl, out var descriptor));

            Assert.Equal(endpointType, descriptor.EndpointType);
        }

        [Theory]
        [InlineData("/umamusume/account/index/")]
        [InlineData("/unknown/path")]
        [InlineData("/umamusume/account/indx")]
        public void TryResolveEndpoint_ReturnsFalseForUnknownPath(string canonicalUrl)
        {
            Assert.False(Server.TryResolveEndpoint(canonicalUrl, out _));
        }

        [Theory]
        [InlineData("")]
        [InlineData("account/index")]
        [InlineData("?path=/umamusume/account/index")]
        public void TryResolveEndpoint_FailsFastForInvalidPath(string canonicalUrl)
        {
            Assert.Throws<FormatException>(() => Server.TryResolveEndpoint(canonicalUrl, out _));
        }

        [Fact]
        public async Task DispatchUnknownEndpoints_AreSilentAndDoNotAffectKnownDispatch()
        {
            if (await RunThroughNotificationServerAsync())
                return;

            var terminal = runtime.Terminal;
            var host = runtime.Host;
            var bootstrap = host.Bootstrap;
            var requestPlugin = new RequestDispatchPlugin();
            var responsePlugin = new ResponseDispatchPlugin();
            try
            {
                bootstrap.Workspace.SwitchTo();
                await host.FlushAsync();
                await terminal.RedrawAsync();
                var screen = await terminal.CaptureScreenAsync();

                LoadTestPlugin(requestPlugin);
                LoadTestPlugin(responsePlugin);

                await PostRequestAsync("/unknown/path", [0xC1], MissingHeaders);
                await PostResponseAsync("/umamusume/account/indx", [0xC1], MissingHeaders);
                await host.FlushAsync();
                await terminal.RedrawAsync();

                Assert.Equal(screen, await terminal.CaptureScreenAsync());
                Assert.Equal(0, requestPlugin.RawCalls);
                Assert.Equal(0, requestPlugin.DtoCalls);
                Assert.Equal(0, responsePlugin.RawCalls);
                Assert.Equal(0, responsePlugin.DtoCalls);

                await PostRequestAsync(
                    AccountIndexPath,
                    MessagePackSerializer.Serialize(new DataLinkIndexRequest()),
                    MissingHeaders);
                await PostResponseAsync(
                    AccountIndexPath,
                    MessagePackSerializer.Serialize(new DataLinkIndexResponse()),
                    MissingHeaders);

                Assert.Equal(1, requestPlugin.RawCalls);
                Assert.Equal(1, requestPlugin.DtoCalls);
                Assert.Equal(1, responsePlugin.RawCalls);
                Assert.Equal(1, responsePlugin.DtoCalls);
            }
            finally
            {
                await host.FlushAsync();
            }
        }

        [Fact]
        public async Task DispatchResponse_CallsRawAndDtoAnalyzersInPriorityOrder()
        {
            if (await RunThroughNotificationServerAsync())
                return;

            var plugin = new ResponseDispatchPlugin();
            LoadTestPlugin(plugin);
            var payload = MessagePackSerializer.Serialize(new DataLinkIndexResponse
            {
                data = new DataLinkIndexResponse.CommonResponse
                {
                    open_date = "2026-06-30",
                },
            });

            await PostResponseAsync(AccountIndexPath, payload, MissingHeaders);

            Assert.Equal(1, plugin.DtoCalls);
            Assert.Equal(1, plugin.RawCalls);
            Assert.Equal(["raw", "dto"], plugin.CallOrder);
            Assert.Equal("2026-06-30", plugin.LastResponse?.data.open_date);
            Assert.Equal(payload, plugin.LastPayload);
        }

        [Fact]
        public async Task DispatchResponse_AcceptsCanonicalUrlWithoutUmamusumePrefix()
        {
            if (await RunThroughNotificationServerAsync())
                return;

            var plugin = new ResponseDispatchPlugin();
            LoadTestPlugin(plugin);
            var payload = MessagePackSerializer.Serialize(new DataLinkIndexResponse
            {
                data = new DataLinkIndexResponse.CommonResponse
                {
                    open_date = "2026-07-05",
                },
            });

            await PostResponseAsync(AccountIndexAbsoluteUrlWithoutPrefix, payload, MissingHeaders);

            Assert.Equal(1, plugin.DtoCalls);
            Assert.Equal(1, plugin.RawCalls);
            Assert.Equal(["raw", "dto"], plugin.CallOrder);
            Assert.Equal("2026-07-05", plugin.LastResponse?.data.open_date);
            Assert.Equal(payload, plugin.LastPayload);
        }

        [Fact]
        public async Task DispatchRequest_DeliversHeadersToRawInvocation()
        {
            if (await RunThroughNotificationServerAsync())
                return;

            var plugin = new RawHeadersDispatchPlugin();
            LoadTestPlugin(plugin);
            var payload = MessagePackSerializer.Serialize(new DataLinkIndexRequest());
            var headers = TestHeaders;

            await PostRequestAsync(AccountIndexPath, payload, headers);

            Assert.Equal(payload, plugin.LastPayload);
            Assert.Equal(headers, plugin.LastHeaders);
        }

        [Fact]
        public async Task DispatchResponse_DeliversHeadersToDtoInvocation()
        {
            if (await RunThroughNotificationServerAsync())
                return;

            var plugin = new DtoHeadersDispatchPlugin();
            LoadTestPlugin(plugin);
            var payload = MessagePackSerializer.Serialize(new DataLinkIndexResponse
            {
                data = new DataLinkIndexResponse.CommonResponse
                {
                    open_date = "2026-07-03",
                },
            });
            var headers = TestHeaders;

            await PostResponseAsync(AccountIndexPath, payload, headers);

            Assert.Equal("2026-07-03", plugin.LastResponse?.data.open_date);
            Assert.Equal(headers, plugin.LastHeaders);
        }

        [Fact]
        public async Task DispatchResponse_IsolatesDtoDeserializationAtDtoAnalyzerExecutionPoint()
        {
            if (await RunThroughNotificationServerAsync())
                return;

            var plugin = new ResponseDispatchPlugin();
            LoadTestPlugin(plugin);

            await PostResponseAsync(AccountIndexPath, [0xC1], MissingHeaders);

            Assert.Equal(0, plugin.DtoCalls);
            Assert.Equal(1, plugin.RawCalls);
        }

        [Fact]
        public async Task DispatchKnownEndpointFailures_RemainReported()
        {
            const string scenario = "known-endpoint-failures";
            if (TerminalUiLifecycleChildProcess.IsChild(scenario))
            {
                StartNotificationServer();
                var terminal = runtime.Terminal;
                var host = runtime.Host;
                var bootstrap = host.Bootstrap;
                try
                {
                    bootstrap.Workspace.SwitchTo();
                    await host.FlushAsync();
                    await terminal.ResizeAsync(320, 48);
                    await terminal.RedrawAsync();

                    var malformedPayloadPlugin = new ResponseDispatchPlugin();
                    LoadTestPlugin(malformedPayloadPlugin);

                    await PostRejectedResponseAsync("account/index", [0xC0], MissingHeaders);
                    await terminal.WaitForScreenAsync("canonical URL");

                    await PostResponseAsync(AccountIndexPath, [0xC1], MissingHeaders);
                    await terminal.WaitForScreenAsync(Localization.Server.I18N_ProjectionFailed.Split("{0}", StringSplitOptions.None)[0]);

                    LoadTestPlugin(new ThrowingResponsePlugin());
                    await PostResponseAsync(
                        AccountIndexPath,
                        MessagePackSerializer.Serialize(new DataLinkIndexResponse()),
                        MissingHeaders);
                    await terminal.WaitForScreenAsync(Localization.Server.I18N_ResponsePluginFailed.Split("{0}", StringSplitOptions.None)[0]);
                    await terminal.WaitForScreenAsync("analyzer failed");
                }
                finally
                {
                    await host.FlushAsync();
                }

                TerminalUiLifecycleChildProcess.WriteResult("ok");
                return;
            }

            Assert.Equal(
                "ok",
                await TerminalUiLifecycleProcessTests.RunChildAsync(
                    scenario,
                    typeof(PluginAnalyzerTests),
                    nameof(DispatchKnownEndpointFailures_RemainReported)));
        }

        [Fact]
        public async Task DispatchResponse_CallsRawAnalyzerWhenNoDtoAnalyzerExists()
        {
            if (await RunThroughNotificationServerAsync())
                return;

            var plugin = new RawResponseDispatchPlugin();
            LoadTestPlugin(plugin);

            await PostResponseAsync(AccountIndexPath, [0xC1], MissingHeaders);

            Assert.Equal(1, plugin.RawCalls);
            Assert.Equal([0xC1], plugin.LastPayload);
        }

        [Fact]
        public async Task DispatchResponse_DebugFilesKeepMsgpackRawAndUseEndpointFileName()
        {
            if (await RunThroughNotificationServerAsync())
                return;

            var previous = Config.Misc.SaveResponseForDebug;
            var originalCwd = Directory.GetCurrentDirectory();
            var tempDir = Path.Combine(Path.GetTempPath(), "ura-debug-packets-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            Directory.SetCurrentDirectory(tempDir);
            Config.Misc.SaveResponseForDebug = true;

            try
            {
                var canonicalUrl = RamenCheckEventAbsoluteUrl;
                byte[] payload = [0xC0];

                await PostResponseAsync(canonicalUrl, payload, MissingHeaders);

                var packetsDir = Path.Combine(tempDir, "packets");
                var msgpack = Assert.Single(Directory.GetFiles(packetsDir, "*.msgpack"));
                var msgpackName = Path.GetFileName(msgpack);
                Assert.Matches(@"^\d{2}-\d{2}-\d{2} \d{2}-\d{2}-\d{2}-\d{3}-[0-9a-f]{32}R-umamusume-single_mode_ramen-check_event\.msgpack$", msgpackName);
                var responseMarker = msgpackName.IndexOf("R-", StringComparison.Ordinal);
                Assert.Equal(7, Guid.ParseExact(msgpackName[(responseMarker - 32)..responseMarker], "N").Version);
                Assert.Equal(payload, File.ReadAllBytes(msgpack));
                Assert.True(PacketCorpus.TryGetCanonicalUrl(msgpack, out var packetKind, out var packetUrl));
                Assert.Equal(AnalyzerKind.Response, packetKind);
                Assert.Equal("/umamusume/single_mode_ramen/check_event", packetUrl);

#if DEBUG
                var jsonPath = Assert.Single(Directory.GetFiles(packetsDir, "*.json"));
                var jsonName = Path.GetFileName(jsonPath);
                Assert.Matches(@"^\d{2}-\d{2}-\d{2} \d{2}-\d{2}-\d{2}-\d{3}R\.json$", jsonName);
                Assert.DoesNotContain("umamusume", jsonName, StringComparison.Ordinal);
                Assert.DoesNotContain("https", jsonName, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("api.games.umamusume.jp", jsonName, StringComparison.Ordinal);
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
        public async Task DispatchResponse_DebugCleanupSkipsOldFilesThatCannotBeDeleted()
        {
            if (await RunThroughNotificationServerAsync())
                return;

            var previous = Config.Misc.SaveResponseForDebug;
            var originalCwd = Directory.GetCurrentDirectory();
            var tempDir = Path.Combine(Path.GetTempPath(), "ura-debug-packets-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            Directory.SetCurrentDirectory(tempDir);
            Config.Misc.SaveResponseForDebug = true;
            FileStream? lockedOldPacket = null;

            try
            {
                var packetsDir = Path.Combine(tempDir, "packets");
                Directory.CreateDirectory(packetsDir);
                var oldPacket = Path.Combine(packetsDir, "26-07-05 16-19-10-526R-umamusume-single_mode_ramen-check_event.msgpack");
                File.WriteAllBytes(oldPacket, [0xC0]);
                File.SetCreationTime(oldPacket, DateTime.Now.AddDays(-2));
                lockedOldPacket = new FileStream(oldPacket, FileMode.Open, FileAccess.Read, FileShare.None);

                await PostResponseAsync(RamenCheckEventAbsoluteUrl, [0xC0], MissingHeaders);

                Assert.True(File.Exists(oldPacket));
                Assert.Equal(2, Directory.GetFiles(packetsDir, "*.msgpack").Length);
            }
            finally
            {
                lockedOldPacket?.Dispose();
                Config.Misc.SaveResponseForDebug = previous;
                Directory.SetCurrentDirectory(originalCwd);
                Directory.Delete(tempDir, recursive: true);
            }
        }

        [Fact]
        public async Task DispatchResponse_UnknownEndpointDoesNotSaveDebugFiles()
        {
            if (await RunThroughNotificationServerAsync())
                return;

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

                await PostResponseAsync(canonicalUrl, payload, MissingHeaders);

                var packetsDir = Path.Combine(tempDir, "packets");
                Assert.False(Directory.Exists(packetsDir));
            }
            finally
            {
                Config.Misc.SaveResponseForDebug = previous;
                Directory.SetCurrentDirectory(originalCwd);
                Directory.Delete(tempDir, recursive: true);
            }
        }

        [Fact]
        public async Task DispatchResponse_IsolatesAnalyzerExceptionAndContinues()
        {
            if (await RunThroughNotificationServerAsync())
                return;

            var throwingPlugin = new ThrowingResponsePlugin();
            var nextPlugin = new ResponseDispatchPlugin();
            LoadTestPlugin(throwingPlugin);
            LoadTestPlugin(nextPlugin);
            var payload = MessagePackSerializer.Serialize(new DataLinkIndexResponse());

            var lines = new List<UiLogLine>();
            runtime.Host.LogAdded += lines.Add;
            try
            {
                await PostResponseAsync(AccountIndexPath, payload, MissingHeaders);
                await runtime.Host.FlushAsync();

                Assert.Equal(1, nextPlugin.RawCalls);
                Assert.Equal(1, nextPlugin.DtoCalls);
                var line = Assert.Single(lines, line => line.Text.Contains("响应分析插件处理失败", StringComparison.Ordinal));
                Assert.Contains(nameof(ThrowingResponsePlugin), line.ExceptionDetails);
                Assert.Contains(@"C:\plugins\analyzer\data.bin", line.ExceptionDetails);
                Assert.Contains(nameof(ThrowingResponsePlugin.OnDto), line.ExceptionDetails);
            }
            finally
            {
                runtime.Host.LogAdded -= lines.Add;
            }
        }

        [Fact]
        public async Task DispatchRequest_CallsRawAndDtoAnalyzersInPriorityOrder()
        {
            if (await RunThroughNotificationServerAsync())
                return;

            var plugin = new RequestDispatchPlugin();
            LoadTestPlugin(plugin);
            var payload = MessagePackSerializer.Serialize(new DataLinkIndexRequest());

            await PostRequestAsync(AccountIndexAbsoluteUrl, payload, MissingHeaders);

            Assert.Equal(1, plugin.DtoCalls);
            Assert.Equal(1, plugin.RawCalls);
            Assert.Equal(["raw", "dto"], plugin.CallOrder);
            Assert.NotNull(plugin.LastRequest);
            Assert.Equal(payload, plugin.LastPayload);
        }

        [Fact]
        public async Task DispatchRequest_CallsRawAnalyzerWhenNoDtoAnalyzerExists()
        {
            if (await RunThroughNotificationServerAsync())
                return;

            var plugin = new RawRequestDispatchPlugin();
            LoadTestPlugin(plugin);

            await PostRequestAsync(AccountIndexUrlWithQuery, [0xC0], MissingHeaders);

            Assert.Equal(1, plugin.RawCalls);
            Assert.Equal([0xC0], plugin.LastPayload);
        }

        [Fact]
        public async Task DispatchRequestAndResponse_DeliverRandomDtosForEveryCatalogEndpoint()
        {
            if (await RunThroughNotificationServerAsync())
                return;

            var plugin = new CatalogDispatchPlugin();
            PluginManager.InitializePlugin(plugin);

            foreach (var descriptor in GameEndpointCatalog.ByPath.Values.OrderBy(x => x.Path, StringComparer.Ordinal))
            {
                var request = RandomDtoGenerator.Create(descriptor.RequestType, $"request:{descriptor.Path}", RandomDtoProfile.Protocol);
                var requestPayload = MessagePackSerializer.Serialize(descriptor.RequestType, request);
                await PostRequestAsync(descriptor.Path, requestPayload, MissingHeaders);

                Assert.True(
                    plugin.RequestDtos.Remove(descriptor.EndpointType, out var receivedRequest),
                    $"Request analyzer was not called for endpoint {descriptor.Path} ({descriptor.EndpointType.FullName}).");
                AssertDtoPayloadMatches(descriptor.RequestType, requestPayload, receivedRequest!, descriptor, "request");

                var response = RandomDtoGenerator.Create(descriptor.ResponseType, $"response:{descriptor.Path}", RandomDtoProfile.Protocol);
                var responsePayload = MessagePackSerializer.Serialize(descriptor.ResponseType, response);
                await PostResponseAsync(descriptor.Path, responsePayload, MissingHeaders);

                Assert.True(
                    plugin.ResponseDtos.Remove(descriptor.EndpointType, out var receivedResponse),
                    $"Response analyzer was not called for endpoint {descriptor.Path} ({descriptor.EndpointType.FullName}).");
                AssertDtoPayloadMatches(descriptor.ResponseType, responsePayload, receivedResponse!, descriptor, "response");
            }

            Assert.Empty(plugin.RequestDtos);
            Assert.Empty(plugin.ResponseDtos);
        }

        [Fact]
        public async Task DispatchRequest_DeliversRequestCorpusDtos()
        {
            Assert.SkipUnless(PacketCorpus.RequestEndpointPackets.Count > 0, "无带 canonical URL 的请求语料");

            if (await RunThroughNotificationServerAsync())
                return;

            var plugin = new CatalogDispatchPlugin();
            PluginManager.InitializePlugin(plugin);

            foreach (var packet in PacketCorpus.RequestEndpointPackets)
            {
                Assert.True(PacketCorpus.TryGetCanonicalUrl(packet.Path, out var kind, out var canonicalUrl));
                Assert.Equal(AnalyzerKind.Request, kind);

                await PostRequestAsync(canonicalUrl, PacketCorpus.LoadBytes(packet.Path), MissingHeaders);

                Assert.True(
                    plugin.RequestDtos.Remove(packet.Endpoint.EndpointType, out var receivedRequest),
                    $"Request analyzer was not called for endpoint {packet.Endpoint.Path} ({packet.Endpoint.EndpointType.FullName}).");
                Assert.IsType(packet.Endpoint.RequestType, receivedRequest);
            }
        }

        [Fact]
        public async Task ProgrammaticRegistry_RegistersRawAndDtoAnalyzers()
        {
            if (await RunThroughNotificationServerAsync())
                return;

            var plugin = new ProgrammaticAnalyzerPlugin();
            PluginManager.InitializePlugin(plugin);
            var payload = MessagePackSerializer.Serialize(new DataLinkIndexResponse
            {
                data = new DataLinkIndexResponse.CommonResponse
                {
                    open_date = "2026-07-01",
                },
            });

            await PostResponseAsync(AccountIndexPath, payload, MissingHeaders);

            Assert.Equal(["raw", "dto"], plugin.CallOrder);
            Assert.Equal(payload, plugin.LastPayload);
            Assert.Equal("2026-07-01", plugin.LastResponse?.data.open_date);
        }

        [Fact]
        public async Task ProgrammaticRegistry_DeliversHeadersToRawAndDtoAnalyzerOverloads()
        {
            if (await RunThroughNotificationServerAsync())
                return;

            var plugin = new ProgrammaticHeadersAnalyzerPlugin();
            PluginManager.InitializePlugin(plugin);
            var payload = MessagePackSerializer.Serialize(new DataLinkIndexResponse
            {
                data = new DataLinkIndexResponse.CommonResponse
                {
                    open_date = "2026-07-03",
                },
            });
            var headers = TestHeaders;

            await PostResponseAsync(AccountIndexPath, payload, headers);

            Assert.Equal(["raw", "dto"], plugin.CallOrder);
            Assert.Equal(payload, plugin.LastPayload);
            Assert.Equal("2026-07-03", plugin.LastResponse?.data.open_date);
            Assert.Equal(headers, plugin.LastRawHeaders);
            Assert.Same(plugin.LastRawHeaders, plugin.LastDtoHeaders);
        }

        [Fact]
        public async Task DispatchResponse_DeliversNonNullHeadersWhenHeaderValuesAreOmitted()
        {
            if (await RunThroughNotificationServerAsync())
                return;

            var plugin = new DtoHeadersDispatchPlugin();
            LoadTestPlugin(plugin);
            var payload = MessagePackSerializer.Serialize(new DataLinkIndexResponse());

            await PostResponseAsync(AccountIndexPath, payload, MissingHeaders);

            Assert.NotNull(plugin.LastHeaders);
            Assert.Equal(MissingHeaders, plugin.LastHeaders);
        }

        [Fact]
        public async Task ProgrammaticRegistry_PreservesSamePriorityRegistrationOrder()
        {
            if (await RunThroughNotificationServerAsync())
                return;

            var plugin = new OrderedProgrammaticAnalyzerPlugin();
            PluginManager.InitializePlugin(plugin);

            await PostResponseAsync(
                AccountIndexPath,
                MessagePackSerializer.Serialize(new DataLinkIndexResponse()),
                MissingHeaders);
            await PostResponseAsync(
                AccountIndexPath,
                MessagePackSerializer.Serialize(new DataLinkIndexResponse()),
                MissingHeaders);

            Assert.Equal(["first", "second", "first", "second"], plugin.CallOrder);
        }

        [Fact]
        public void ProgrammaticRegistry_RejectsByteArrayDtoOverload()
        {
            var plugin = new ByteArrayDtoProgrammaticPlugin();

            var ex = Assert.Throws<InvalidOperationException>(() => PluginManager.InitializePlugin(plugin));

            Assert.Contains(typeof(byte[]).FullName!, ex.Message);
            Assert.Contains("Host", ex.Message);
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
            if (await RunThroughNotificationServerAsync())
                return;

            var plugin = new DtoCacheDispatchPlugin();
            LoadTestPlugin(plugin);
            var payload = MessagePackSerializer.Serialize(new DataLinkIndexResponse());

            await PostResponseAsync(AccountIndexPath, payload, MissingHeaders);

            Assert.Same(plugin.FirstResponse, plugin.SecondResponse);
        }

        [Fact]
        public async Task Registrations_CommitAtomicallyForInitializeAndStartAsync()
        {
            var count = PluginManager.ResponseAnalyzerMethods.Count;
            var failedInitialize = new FailingInitializeRegistrationPlugin();
            Assert.Throws<InvalidOperationException>(() => PluginManager.InitializePlugin(failedInitialize));
            Assert.Equal(count, PluginManager.ResponseAnalyzerMethods.Count);

            var committed = new StartedRegistrationPlugin(throwAfterRegistration: false);
            var rolledBack = new StartedRegistrationPlugin(throwAfterRegistration: true);
            try
            {
                PluginManager.InitializePlugin(committed);
                PluginManager.InitializePlugin(rolledBack);

                await PluginManager.StartPluginAsync(committed);
                await PluginManager.StartPluginAsync(rolledBack);

                Assert.Equal(count + 1, PluginManager.ResponseAnalyzerMethods.Count);
                var registration = Assert.Single(
                    PluginManager.ResponseAnalyzerMethods,
                    candidate => ReferenceEquals(candidate.Plugin, committed) && candidate.Priority == 0);
                Assert.Same(committed, registration.Plugin);
                Assert.Equal(typeof(GameApi.Account.Index), registration.EndpointType);
                Assert.DoesNotContain(
                    PluginManager.ResponseAnalyzerMethods,
                    candidate => ReferenceEquals(candidate.Plugin, rolledBack));
            }
            finally
            {
                await PluginManager.CleanupPluginAsync(committed).WaitAsync(TimeSpan.FromSeconds(5));
                await PluginManager.CleanupPluginAsync(rolledBack).WaitAsync(TimeSpan.FromSeconds(5));
            }
        }

        [Fact]
        public async Task InitializePlugin_PassesPluginContextAndCleanupPreventsStart()
        {
            var plugin = new ContextPlugin();

            PluginManager.InitializePlugin(plugin);

            Assert.NotNull(plugin.Context);
            Assert.Same(application, plugin.Context!.Application);
            Assert.NotNull(plugin.Context.Analyzers);

            await PluginManager.StartPluginAsync(plugin);
            Assert.Equal(1, plugin.StartedCalls);

            await PluginManager.CleanupPluginAsync(plugin);
            await PluginManager.StartPluginAsync(plugin);
            Assert.Equal(1, plugin.StartedCalls);
        }

        [Fact]
        public async Task PluginLoadContext_ResolvesSharedAbiAssemblyFromDefaultContext()
        {
            var host = runtime.Host;
            var bootstrap = host.Bootstrap;
            var ctx = new PluginManager.PluginLoadContext("shared-abi-test");
            var warnings = new List<UiLogLine>();
            host.LogAdded += warnings.Add;
            try
            {
                bootstrap.Workspace.SwitchTo();
                await host.FlushAsync();

                var sharedHost = ctx.LoadFromAssemblyName(typeof(IPlugin).Assembly.GetName());

                Assert.Same(typeof(Server).Assembly, sharedHost);

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
                await host.FlushAsync();
                Assert.Contains(warnings, line => line.Text.Contains("99.0.0.0", StringComparison.Ordinal));

                var wrongTerminalGuiVersion = new AssemblyName(typeof(View).Assembly.GetName().Name!)
                {
                    Version = new Version(99, 0, 0, 0),
                };
                Assert.Throws<FileLoadException>(() => ctx.LoadFromAssemblyName(wrongTerminalGuiVersion));
            }
            finally
            {
                host.LogAdded -= warnings.Add;
                Assert.False(ctx.IsCollectible);
                await host.FlushAsync();
            }
        }

        [Fact]
        public void PluginLoadContextAllowsFrameworkAndResourceFallback()
        {
            var context = new PluginManager.PluginLoadContext("fallback-test");
            var framework = context.LoadFromAssemblyName(new AssemblyName("System.Xml.ReaderWriter"));
            Assert.Same(AssemblyLoadContext.Default, AssemblyLoadContext.GetLoadContext(framework));
            var satellite = new AssemblyName(typeof(PluginAnalyzerTests).Assembly.GetName().Name + ".resources") { CultureName = "ja-JP" };
            Assert.Throws<FileNotFoundException>(() => context.LoadFromAssemblyName(satellite));
        }

        async Task<bool> RunThroughNotificationServerAsync(
            [CallerMemberName] string methodName = "")
        {
            var scenario = NotificationScenarioPrefix + methodName;
            if (TerminalUiLifecycleChildProcess.IsChild(scenario))
            {
                StartNotificationServer();
                return false;
            }

            Assert.Equal(
                "ok",
                await TerminalUiLifecycleProcessTests.RunChildAsync(
                    scenario,
                    typeof(PluginAnalyzerTests),
                    methodName));
            return true;
        }

        void StartNotificationServer()
        {
            if (notificationServerStarted)
                return;

            var port = GetFreePort();
            Server.Instance = new(
                new WebserverSettings("127.0.0.1", port),
                context => context.Response.Send(string.Empty));
            Server.Start(TestContext.Current.CancellationToken);
            notificationClient = new HttpClient
            {
                BaseAddress = new Uri($"http://127.0.0.1:{port}"),
            };
            notificationServerStarted = true;
        }

        Task PostRequestAsync(
            string canonicalUrl,
            byte[] payload,
            GameHttpHeaders headers)
            => PostPacketAsync(AnalyzerKind.Request, canonicalUrl, payload, headers);

        Task PostResponseAsync(
            string canonicalUrl,
            byte[] payload,
            GameHttpHeaders headers)
            => PostPacketAsync(AnalyzerKind.Response, canonicalUrl, payload, headers);

        async Task PostRejectedResponseAsync(
            string canonicalUrl,
            byte[] payload,
            GameHttpHeaders headers)
        {
            using var response = await SendPacketAsync(
                AnalyzerKind.Response,
                canonicalUrl,
                payload,
                headers);
            Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
        }

        async Task PostPacketAsync(
            AnalyzerKind kind,
            string canonicalUrl,
            byte[] payload,
            GameHttpHeaders headers)
        {
            using var response = await SendPacketAsync(kind, canonicalUrl, payload, headers);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        async Task<HttpResponseMessage> SendPacketAsync(
            AnalyzerKind kind,
            string canonicalUrl,
            byte[] payload,
            GameHttpHeaders headers)
        {
            var client = notificationClient
                ?? throw new InvalidOperationException("Notification test server is not started.");
            using var request = new HttpRequestMessage(
                System.Net.Http.HttpMethod.Post,
                kind == AnalyzerKind.Request ? "/notify/request" : "/notify/response");
            request.Headers.Add("X-Hachimi-Game-Url", canonicalUrl);
            AddHeader(request, "X-Hachimi-sid", headers.Sid);
            AddHeader(request, "X-Hachimi-app-ver", headers.AppVer);
            AddHeader(request, "X-Hachimi-res-ver", headers.ResVer);
            AddHeader(request, "X-Hachimi-viewerid", headers.ViewerId);
            AddHeader(request, "X-Hachimi-device", headers.Device);
            AddHeader(request, "X-Hachimi-device-subtype", headers.DeviceSubtype);
            request.Content = new ByteArrayContent(payload);

            return await client.SendAsync(request, TestContext.Current.CancellationToken);
        }

        static void AddHeader(HttpRequestMessage request, string name, string? value)
        {
            if (value is not null)
                request.Headers.Add(name, value);
        }

        static int GetFreePort()
        {
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }

        static void LoadTestPlugin(IPlugin plugin)
        {
            PluginManager.InitializePlugin(plugin);
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
            public virtual void Initialize(IPluginContext context) { }
            public virtual ValueTask StartAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
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
                        .MakeGenericMethod(descriptor.RequestType, descriptor.ResponseType)
                        .Invoke(null, [context.Analyzers, this, descriptor]);
                }
            }

            static void RegisterEndpoint<TRequest, TResponse>(
                IPluginAnalyzerRegistry registry,
                CatalogDispatchPlugin plugin,
                GameEndpointDescriptor descriptor)
            {
                registry.Register<TRequest>(
                    AnalyzerKind.Request,
                    [EndpointPattern.Exact(descriptor.Path)],
                    invocation =>
                    {
                        plugin.RequestDtos[invocation.Endpoint.EndpointType] = invocation.Payload!;
                        return ValueTask.CompletedTask;
                    });
                registry.Register<TResponse>(
                    AnalyzerKind.Response,
                    [EndpointPattern.Exact(descriptor.Path)],
                    invocation =>
                    {
                        plugin.ResponseDtos[invocation.Endpoint.EndpointType] = invocation.Payload!;
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
            public ValueTask OnRequest(ReadOnlyMemory<byte> payload)
            {
                return ValueTask.CompletedTask;
            }

            [ResponseAnalyzer<GameApi.Account.Index>(2)]
            public ValueTask OnResponse(ReadOnlyMemory<byte> payload)
            {
                return ValueTask.CompletedTask;
            }
        }

        sealed class MultiAttributePlugin : TestPlugin
        {
            [ResponseAnalyzer<GameApi.Account.Index>(5)]
            public ValueTask First(DataLinkIndexResponse response)
            {
                return ValueTask.CompletedTask;
            }

            [ResponseAnalyzer<GameApi.Account.Index>(5)]
            public ValueTask Second(DataLinkIndexResponse response)
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

        sealed class WrongHeadersParameterPlugin : TestPlugin
        {
            [ResponseAnalyzer<GameApi.Account.Index>]
            public ValueTask OnResponse(DataLinkIndexResponse response, string headers)
            {
                return ValueTask.CompletedTask;
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

            public override void Initialize(IPluginContext context)
            {
                context.Analyzers.Register<ReadOnlyMemory<byte>>(
                    AnalyzerKind.Response,
                    [EndpointPattern.Exact(AccountIndexPath)],
                    invocation =>
                    {
                        RawCalls++;
                        CallOrder.Add("raw");
                        LastPayload = invocation.Payload.ToArray();
                        return ValueTask.CompletedTask;
                    },
                    10);
            }
        }

        sealed class RawResponseDispatchPlugin : TestPlugin
        {
            public int RawCalls { get; private set; }
            public byte[]? LastPayload { get; private set; }

            public override void Initialize(IPluginContext context)
            {
                context.Analyzers.Register<ReadOnlyMemory<byte>>(
                    AnalyzerKind.Response,
                    [EndpointPattern.Exact(AccountIndexPath)],
                    invocation =>
                    {
                        RawCalls++;
                        LastPayload = invocation.Payload.ToArray();
                        return ValueTask.CompletedTask;
                    });
            }
        }

        sealed class ThrowingResponsePlugin : TestPlugin
        {
            [ResponseAnalyzer<GameApi.Account.Index>]
            public ValueTask OnDto(DataLinkIndexResponse response)
            {
                throw new InvalidOperationException(@"analyzer failed, path=C:\plugins\analyzer\data.bin");
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

            public override void Initialize(IPluginContext context)
            {
                context.Analyzers.Register<ReadOnlyMemory<byte>>(
                    AnalyzerKind.Request,
                    [EndpointPattern.Exact(AccountIndexPath)],
                    invocation =>
                    {
                        RawCalls++;
                        CallOrder.Add("raw");
                        LastPayload = invocation.Payload.ToArray();
                        return ValueTask.CompletedTask;
                    },
                    10);
            }
        }

        sealed class RawRequestDispatchPlugin : TestPlugin
        {
            public int RawCalls { get; private set; }
            public byte[]? LastPayload { get; private set; }

            public override void Initialize(IPluginContext context)
            {
                context.Analyzers.Register<ReadOnlyMemory<byte>>(
                    AnalyzerKind.Request,
                    [EndpointPattern.Exact(AccountIndexPath)],
                    invocation =>
                    {
                        RawCalls++;
                        LastPayload = invocation.Payload.ToArray();
                        return ValueTask.CompletedTask;
                    });
            }
        }

        sealed class RawHeadersDispatchPlugin : TestPlugin
        {
            public byte[]? LastPayload { get; private set; }
            public GameHttpHeaders? LastHeaders { get; private set; }

            public override void Initialize(IPluginContext context)
            {
                context.Analyzers.Register<ReadOnlyMemory<byte>>(
                    AnalyzerKind.Request,
                    [EndpointPattern.Exact(AccountIndexPath)],
                    invocation =>
                    {
                        LastPayload = invocation.Payload.ToArray();
                        LastHeaders = invocation.Headers;
                        return ValueTask.CompletedTask;
                    });
            }
        }

        sealed class DtoHeadersDispatchPlugin : TestPlugin
        {
            public DataLinkIndexResponse? LastResponse { get; private set; }
            public GameHttpHeaders? LastHeaders { get; private set; }

            public override void Initialize(IPluginContext context)
            {
                context.Analyzers.Register<DataLinkIndexResponse>(
                    AnalyzerKind.Response,
                    [EndpointPattern.Exact(AccountIndexPath)],
                    invocation =>
                    {
                        LastResponse = invocation.Payload;
                        LastHeaders = invocation.Headers;
                        return ValueTask.CompletedTask;
                    });
            }
        }

        sealed class ProgrammaticAnalyzerPlugin : TestPlugin
        {
            public byte[]? LastPayload { get; private set; }
            public DataLinkIndexResponse? LastResponse { get; private set; }
            public List<string> CallOrder { get; } = [];

            public override void Initialize(IPluginContext context)
            {
                context.Analyzers.Register<ReadOnlyMemory<byte>>(
                    AnalyzerKind.Response,
                    [EndpointPattern.Exact(AccountIndexPath)],
                    invocation =>
                    {
                        LastPayload = invocation.Payload.ToArray();
                        CallOrder.Add("raw");
                        return ValueTask.CompletedTask;
                    },
                    10);

                context.Analyzers.Register<DataLinkIndexResponse>(
                    AnalyzerKind.Response,
                    [EndpointPattern.Exact(AccountIndexPath)],
                    invocation =>
                    {
                        LastResponse = invocation.Payload;
                        CallOrder.Add("dto");
                        return ValueTask.CompletedTask;
                    },
                    20);
            }
        }

        sealed class ProgrammaticHeadersAnalyzerPlugin : TestPlugin
        {
            public byte[]? LastPayload { get; private set; }
            public DataLinkIndexResponse? LastResponse { get; private set; }
            public GameHttpHeaders? LastRawHeaders { get; private set; }
            public GameHttpHeaders? LastDtoHeaders { get; private set; }
            public List<string> CallOrder { get; } = [];

            public override void Initialize(IPluginContext context)
            {
                context.Analyzers.Register<ReadOnlyMemory<byte>>(
                    AnalyzerKind.Response,
                    [EndpointPattern.Exact(AccountIndexPath)],
                    invocation =>
                    {
                        LastPayload = invocation.Payload.ToArray();
                        LastRawHeaders = invocation.Headers;
                        CallOrder.Add("raw");
                        return ValueTask.CompletedTask;
                    },
                    10);

                context.Analyzers.Register<DataLinkIndexResponse>(
                    AnalyzerKind.Response,
                    [EndpointPattern.Exact(AccountIndexPath)],
                    invocation =>
                    {
                        LastResponse = invocation.Payload;
                        LastDtoHeaders = invocation.Headers;
                        CallOrder.Add("dto");
                        return ValueTask.CompletedTask;
                    },
                    20);
            }
        }

        sealed class OrderedProgrammaticAnalyzerPlugin : TestPlugin
        {
            public List<string> CallOrder { get; } = [];

            public override void Initialize(IPluginContext context)
            {
                context.Analyzers.Register<ReadOnlyMemory<byte>>(
                    AnalyzerKind.Response,
                    [EndpointPattern.Exact(AccountIndexPath)],
                    _ =>
                    {
                        CallOrder.Add("first");
                        return ValueTask.CompletedTask;
                    },
                    10);

                context.Analyzers.Register<ReadOnlyMemory<byte>>(
                    AnalyzerKind.Response,
                    [EndpointPattern.Exact(AccountIndexPath)],
                    _ =>
                    {
                        CallOrder.Add("second");
                        return ValueTask.CompletedTask;
                    },
                    10);
            }
        }

        sealed class ByteArrayDtoProgrammaticPlugin : TestPlugin
        {
            public override void Initialize(IPluginContext context)
            {
                context.Analyzers.Register<byte[]>(
                    AnalyzerKind.Response,
                    [EndpointPattern.Exact(AccountIndexPath)],
                    _ => ValueTask.CompletedTask);
            }
        }

        sealed class NullHandlerProgrammaticPlugin : TestPlugin
        {
            public override void Initialize(IPluginContext context)
            {
                Func<AnalyzerInvocation<ReadOnlyMemory<byte>>, ValueTask> handler = null!;
                context.Analyzers.Register(
                    AnalyzerKind.Response,
                    [EndpointPattern.Exact(AccountIndexPath)],
                    handler);
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

        sealed class FailingInitializeRegistrationPlugin : TestPlugin
        {
            public override void Initialize(IPluginContext context)
            {
                context.Analyzers.Register<ReadOnlyMemory<byte>>(
                    AnalyzerKind.Response,
                    [EndpointPattern.Exact(AccountIndexPath)],
                    _ => ValueTask.CompletedTask);
                throw new InvalidOperationException("initialize failed");
            }
        }

        sealed class StartedRegistrationPlugin(bool throwAfterRegistration) : TestPlugin
        {
            IPluginContext context = null!;
            public override void Initialize(IPluginContext context) => this.context = context;
            public override ValueTask StartAsync(CancellationToken cancellationToken = default)
            {
                context.Analyzers.Register<ReadOnlyMemory<byte>>(
                    AnalyzerKind.Response, [EndpointPattern.Exact(AccountIndexPath)], _ => ValueTask.CompletedTask);
                if (throwAfterRegistration)
                    throw new InvalidOperationException("started registration failed");
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
            }
            public override ValueTask StartAsync(CancellationToken cancellationToken = default)
            {
                StartedCalls++;
                return ValueTask.CompletedTask;
            }
        }

    }
}
