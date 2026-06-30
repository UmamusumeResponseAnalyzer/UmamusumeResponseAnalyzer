using MessagePack;
using Gallop.Endpoints;
using Newtonsoft.Json.Linq;
using System.Reflection;
using System.Runtime.ExceptionServices;
using UmamusumeResponseAnalyzer.LiveDisplay;
using UmamusumeResponseAnalyzer.Plugin;
using WatsonWebserver.Core;
using WatsonWebserver.Lite;
using static UmamusumeResponseAnalyzer.Localization.Server;

namespace UmamusumeResponseAnalyzer
{
    public static class Server
    {
        internal const string CanonicalUrlHeaderName = "X-Hachimi-Game-Url";
        internal static WebserverLite Instance = new(new WebserverSettings(Config.Core.ListenAddress, Config.Core.ListenPort), (ctx) => { return ctx.Response.Send(string.Empty); });
        public static bool IsRunning => Instance.IsListening;
        internal static void Start()
        {
            Instance.Routes.PreAuthentication.Static.Add(WatsonWebserver.Core.HttpMethod.POST, "/notify/response", async (ctx) =>
            {
                var buffer = ctx.Request.DataAsBytes;
                var canonicalUrl = ReadCanonicalUrl(ctx);
                DispatchResponse(canonicalUrl, buffer);
                await ctx.Response.Send(string.Empty);
            });
            Instance.Routes.PreAuthentication.Static.Add(WatsonWebserver.Core.HttpMethod.POST, "/notify/request", async (ctx) =>
            {
                var buffer = ctx.Request.DataAsBytes;
                var canonicalUrl = ReadCanonicalUrl(ctx);
                DispatchRequest(canonicalUrl, buffer);
                await ctx.Response.Send(string.Empty);
            });
            Instance.Routes.PreAuthentication.Static.Add(WatsonWebserver.Core.HttpMethod.GET, "/notify/ping", (ctx) =>
            {
                LiveDisplayConsole.Log("Server", I18N_PingReceived, LiveDisplaySeverity.Trace);
                return ctx.Response.Send("pong");
            });
            // URACloud 网页集成端点(/uracloud/*):探测 + 网页触发安装并热重载。安全模型见 WebInstallApi。
            WebInstallApi.Register(Instance);
            Instance.Start();
        }
        internal static void Stop() => Instance.Dispose();
        internal static string ReadCanonicalUrl(HttpContextBase ctx)
        {
            var canonicalUrl = ctx.Request.Headers[CanonicalUrlHeaderName];
            if (string.IsNullOrWhiteSpace(canonicalUrl))
                throw new InvalidOperationException($"缺少 canonical URL header: {CanonicalUrlHeaderName}");
            return canonicalUrl;
        }

        internal static GameEndpointDescriptor ResolveEndpoint(string canonicalUrl)
        {
            var path = ExtractEndpointPath(canonicalUrl);
            if (GameEndpointCatalog.ByPath.TryGetValue(path, out var descriptor))
                return descriptor;

            throw new KeyNotFoundException($"未识别 Gallop endpoint: url={canonicalUrl}, path={path}");
        }

        static string ExtractEndpointPath(string canonicalUrl)
        {
            var value = canonicalUrl.Trim();
            if (Uri.TryCreate(value, UriKind.Absolute, out var uri))
                value = uri.AbsolutePath;
            else
            {
                var queryStart = value.IndexOfAny(['?', '#']);
                if (queryStart >= 0)
                    value = value[..queryStart];
            }

            if (!value.StartsWith('/'))
                throw new FormatException($"canonical URL 必须包含绝对 path: {canonicalUrl}");

            return value;
        }

        internal static void DispatchRequest(string canonicalUrl, byte[] buffer)
            => DispatchPacket(AnalyzerKind.Request, canonicalUrl, buffer);

        internal static void DispatchResponse(string canonicalUrl, byte[] buffer)
            => DispatchPacket(AnalyzerKind.Response, canonicalUrl, buffer);

        static void DispatchPacket(AnalyzerKind kind, string canonicalUrl, byte[] buffer)
        {
            try
            {
                SaveDebugPacket(kind, canonicalUrl, buffer);
                var descriptor = ResolveEndpoint(canonicalUrl);

                // 持分发读锁，确保热重载（写锁）不会在分发途中拆毁插件
                PluginManager.EnterDispatch();
                try
                {
                    DispatchPacketLocked(kind, descriptor, buffer);
                }
                finally
                {
                    PluginManager.ExitDispatch();
                }
            }
            catch (Exception e)
            {
                ReportDispatchError(kind, e);
                throw;
            }
        }

        static void SaveDebugPacket(AnalyzerKind kind, string canonicalUrl, byte[] buffer)
        {
            if (!Config.Misc.SaveResponseForDebug)
                return;

            if (Directory.Exists("packets"))
            {
                foreach (var i in Directory.GetFiles("packets"))
                {
                    var fileInfo = new FileInfo(i);
                    if (fileInfo.CreationTime.AddDays(1) < DateTime.Now)
                        fileInfo.Delete();
                }
            }
            else
            {
                Directory.CreateDirectory("packets");
            }
            var suffix = kind == AnalyzerKind.Request ? "Q" : "R";
            var fileBase = $"{DateTime.Now:yy-MM-dd HH-mm-ss-fff}{suffix}-{Uri.EscapeDataString(canonicalUrl)}";
            File.WriteAllBytes($"packets/{fileBase}.msgpack", buffer);
#if DEBUG
            var debugJson = new JObject
            {
                ["url"] = canonicalUrl,
                ["payload"] = JToken.Parse(MessagePackSerializer.ConvertToJson(buffer)),
            };
            File.WriteAllText($"packets/{fileBase}.json", debugJson.ToString(Newtonsoft.Json.Formatting.None));
#endif
        }

        static void DispatchPacketLocked(AnalyzerKind kind, GameEndpointDescriptor descriptor, byte[] buffer)
        {
            var registrations = RegistrationsFor(kind)
                .SelectMany(x => x.Value)
                .Where(x => x.EndpointType == descriptor.EndpointType)
                .ToList();
            if (registrations.Count == 0)
                return;

            var dto = registrations.Any(x => x.PayloadKind == AnalyzerPayloadKind.Dto)
                ? DeserializeDto(kind, descriptor, buffer)
                : null;

            foreach (var registration in registrations)
            {
                var payload = registration.PayloadKind == AnalyzerPayloadKind.RawMessagePack
                    ? buffer
                    : dto!;
                InvokeAnalyzer(kind, registration, payload);
            }
        }

        static object DeserializeDto(AnalyzerKind kind, GameEndpointDescriptor descriptor, byte[] buffer)
        {
            var dtoType = kind == AnalyzerKind.Request ? descriptor.RequestType : descriptor.ResponseType;
            try
            {
                return MessagePackSerializer.Deserialize(dtoType, buffer)
                    ?? throw new InvalidOperationException(
                        $"Gallop DTO 反序列化返回 null: endpoint={descriptor.EndpointType.FullName}, path={descriptor.Path}");
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"Gallop DTO 反序列化失败: endpoint={descriptor.EndpointType.FullName}, path={descriptor.Path}, dto={dtoType.FullName}",
                    ex);
            }
        }

        static SortedDictionary<int, List<AnalyzerRegistration>> RegistrationsFor(AnalyzerKind kind)
            => kind == AnalyzerKind.Request
                ? PluginManager.RequestAnalyzerMethods
                : PluginManager.ResponseAnalyzerMethods;

        static void InvokeAnalyzer(AnalyzerKind kind, AnalyzerRegistration registration, object payload)
        {
            try
            {
                using var callback = PluginManager.EnterPluginCallbackScope();
                registration.Method.Invoke(registration.Plugin, [payload]);
            }
            catch (Exception e)
            {
                var root = e is TargetInvocationException { InnerException: { } inner } ? inner : e;
                var label = kind == AnalyzerKind.Request ? "请求" : "响应";
                LiveDisplayConsole.Notify("Plugin", $"{label}分析插件处理失败: {root.Message}", LiveDisplaySeverity.Error);
                LiveDisplayConsole.Log(registration.Method.DeclaringType?.Name ?? "Plugin", root.ToString(), LiveDisplaySeverity.Error);
                ExceptionDispatchInfo.Capture(root).Throw();
                throw;
            }
        }

        static void ReportDispatchError(AnalyzerKind kind, Exception ex)
        {
            var label = kind == AnalyzerKind.Request ? "请求分析失败" : I18N_ResponseAnalyzeFail;
            LiveDisplayConsole.Notify("Server", $"{label}: {ex.Message}", LiveDisplaySeverity.Error);
            LiveDisplayConsole.Log("Server", ex.ToString(), LiveDisplaySeverity.Error);
        }
    }

}
