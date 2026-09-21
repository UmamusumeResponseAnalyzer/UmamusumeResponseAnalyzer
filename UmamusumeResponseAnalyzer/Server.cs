using MessagePack;
using Gallop.Endpoints;
using Newtonsoft.Json.Linq;
using System.Reflection;
using UmamusumeResponseAnalyzer.TerminalGui;
using UmamusumeResponseAnalyzer.Plugin;
using WatsonWebserver.Core;
using WatsonWebserver.Lite;
using static UmamusumeResponseAnalyzer.Localization.Server;

namespace UmamusumeResponseAnalyzer
{
    internal sealed class AnalyzerDispatchContext(
        GameEndpointDescriptor descriptor,
        byte[] payload,
        GameHttpHeaders headers)
    {
        readonly Dictionary<Type, object> projections = [];

        public GameEndpointDescriptor Endpoint { get; } = descriptor;
        public ReadOnlyMemory<byte> Payload { get; } = payload;
        public GameHttpHeaders Headers { get; } = headers;

        public object GetDto(Type payloadType)
        {
            if (!projections.TryGetValue(payloadType, out var projection))
            {
                try
                {
                    projection = MessagePackSerializer.Deserialize(payloadType, Payload)
                        ?? throw new InvalidOperationException(
                            $"Gallop DTO 反序列化返回 null: endpoint={Endpoint.EndpointType.FullName}, " +
                            $"path={Endpoint.Path}, dto={payloadType.FullName}");
                }
                catch (Exception ex)
                {
                    projection = new AnalyzerProjectionException(Endpoint, payloadType, ex);
                }

                projections[payloadType] = projection;
            }

            if (projection is AnalyzerProjectionException failure)
                throw failure;
            return projection;
        }
    }

    internal sealed class AnalyzerProjectionException(
        GameEndpointDescriptor endpoint,
        Type payloadType,
        Exception innerException) : InvalidOperationException(
            $"Gallop DTO 投影失败: endpoint={endpoint.EndpointType.FullName}, path={endpoint.Path}, dto={payloadType.FullName}",
            innerException)
    {
        int reported;

        internal bool TryMarkReported()
            => Interlocked.Exchange(ref reported, 1) == 0;
    }

    internal static class Server
    {
        static readonly Lazy<WebserverLite> defaultInstance = new(static () => new(
            new WebserverSettings(Config.Core.ListenAddress, Config.Core.ListenPort),
            ctx => ctx.Response.Send(string.Empty)));
        static WebserverLite? instance;

        // 禁止 beforefieldinit；配置只在首次获取 Instance 时读取。
        static Server() { }

        const string GameEndpointPathPrefix = "/umamusume";
        const string CanonicalUrlHeaderName = "X-Hachimi-Game-Url";
        internal static WebserverLite Instance
        {
            get => instance ?? defaultInstance.Value;
            set => instance = value;
        }
        internal static bool IsRunning => instance?.IsListening
            ?? (defaultInstance.IsValueCreated && defaultInstance.Value.IsListening);
        internal static void Start(CancellationToken hostCancellationToken)
        {
            var server = Instance;
            var routes = server.Routes.PreAuthentication.Static;
            routes.Add(
                WatsonWebserver.Core.HttpMethod.POST,
                "/notify/response",
                ctx => HandleNotificationAsync(AnalyzerKind.Response, ctx, hostCancellationToken));
            routes.Add(
                WatsonWebserver.Core.HttpMethod.POST,
                "/notify/request",
                ctx => HandleNotificationAsync(AnalyzerKind.Request, ctx, hostCancellationToken));
            routes.Add(
                WatsonWebserver.Core.HttpMethod.GET,
                "/notify/ping",
                ctx =>
                {
                    hostCancellationToken.ThrowIfCancellationRequested();
                    TerminalUi.Log("Server", I18N_PingReceived, UiSeverity.Trace);
                    return ctx.Response.Send("pong");
                });
            WebInstallApi.Register(server, hostCancellationToken);
            server.Start(hostCancellationToken);
        }

        internal static Task StopAsync()
        {
            var server = instance;
            if (server is null)
            {
                if (!defaultInstance.IsValueCreated)
                    return Task.CompletedTask;
                server = defaultInstance.Value;
            }

            try
            {
                if (server.IsListening)
                    server.Stop();
            }
            finally
            {
                server.Dispose();
            }
            return Task.CompletedTask;
        }

        static async Task HandleNotificationAsync(
            AnalyzerKind kind,
            HttpContextBase ctx,
            CancellationToken cancellationToken)
        {
            var buffer = ctx.Request.DataAsBytes;
            var canonicalUrl = ctx.Request.Headers[CanonicalUrlHeaderName];
            if (string.IsNullOrWhiteSpace(canonicalUrl))
                throw new InvalidOperationException($"缺少 canonical URL header: {CanonicalUrlHeaderName}");

            var headers = new GameHttpHeaders(
                ctx.Request.Headers["X-Hachimi-sid"],
                ctx.Request.Headers["X-Hachimi-app-ver"],
                ctx.Request.Headers["X-Hachimi-res-ver"],
                ctx.Request.Headers["X-Hachimi-viewerid"],
                ctx.Request.Headers["X-Hachimi-device"],
                ctx.Request.Headers["X-Hachimi-device-subtype"]);
            await DispatchPacket(kind, canonicalUrl, buffer, headers);
            cancellationToken.ThrowIfCancellationRequested();
            await ctx.Response.Send(string.Empty);
        }

        internal static bool TryResolveEndpoint(string canonicalUrl, out GameEndpointDescriptor descriptor)
        {
            var path = ExtractEndpointPath(canonicalUrl);
            var prefixed = path.StartsWith(GameEndpointPathPrefix + "/", StringComparison.Ordinal);
            var firstPath = prefixed ? path : GameEndpointPathPrefix + path;
            var secondPath = prefixed ? path[GameEndpointPathPrefix.Length..] : path;
            return GameEndpointCatalog.ByPath.TryGetValue(firstPath, out descriptor!)
                || GameEndpointCatalog.ByPath.TryGetValue(secondPath, out descriptor!);
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

        static async ValueTask DispatchPacket(AnalyzerKind kind, string canonicalUrl, byte[] buffer, GameHttpHeaders headers)
        {
            try
            {
                if (!TryResolveEndpoint(canonicalUrl, out var descriptor))
                    return;

                SaveDebugPacket(kind, canonicalUrl, buffer);

                using var registrations = PluginManager.SnapshotAnalyzerRegistrations(kind, descriptor.EndpointType);
                if (registrations.Count == 0)
                    return;

                var context = new AnalyzerDispatchContext(descriptor, buffer, headers);
                for (var i = 0; i < registrations.Count; i++)
                    await InvokeAnalyzer(kind, registrations[i], context);
            }
            catch (Exception e)
            {
                var label = kind == AnalyzerKind.Request ? "请求分析失败" : I18N_ResponseAnalyzeFail;
                TerminalUi.Notify("Server", $"{label}: {e.Message}", UiSeverity.Error);
                TerminalUi.LogException("Server", e);
                throw;
            }
        }

        static void SaveDebugPacket(AnalyzerKind kind, string canonicalUrl, byte[] buffer)
        {
            if (!Config.Misc.SaveResponseForDebug)
                return;

            Directory.CreateDirectory("packets");

            CleanupOldDebugPackets();

            var suffix = kind == AnalyzerKind.Request ? "Q" : "R";
            var timestamp = DateTime.Now.ToString("yy-MM-dd HH-mm-ss-fff");
            var endpointName = ExtractEndpointPath(canonicalUrl)[1..].Replace('/', '-');
            File.WriteAllBytes($"packets/{timestamp}-{Guid.CreateVersion7():N}{suffix}-{endpointName}.msgpack", buffer);
#if DEBUG
            var debugJson = new JObject
            {
                ["url"] = canonicalUrl,
                ["payload"] = JToken.Parse(MessagePackSerializer.ConvertToJson(buffer)),
            };
            File.WriteAllText($"packets/{timestamp}{suffix}.json", debugJson.ToString(Newtonsoft.Json.Formatting.None));
#endif
        }

        static void CleanupOldDebugPackets()
        {
            foreach (var i in Directory.GetFiles("packets"))
            {
                var fileInfo = new FileInfo(i);
                if (fileInfo.CreationTime.AddDays(1) >= DateTime.Now)
                    continue;

                try
                {
                    fileInfo.Delete();
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    TerminalUi.Log("Server", $"debug packet 旧文件清理失败，已跳过 {Path.GetFileName(i)}: {ex.Message}", UiSeverity.Warning);
                }
            }
        }

        static async ValueTask InvokeAnalyzer(AnalyzerKind kind, AnalyzerRegistration registration, AnalyzerDispatchContext context)
        {
            using var owner = HotkeyManager.RegisterScope(registration.Plugin);
            try
            {
                await registration.Handler(context);
            }
            catch (Exception e)
            {
                var root = e is TargetInvocationException { InnerException: { } inner } ? inner : e;
                if (root is AnalyzerProjectionException projection && !projection.TryMarkReported())
                    return;
                var label = kind == AnalyzerKind.Request ? "请求" : "响应";
                var failure = new InvalidOperationException(
                    $"{label}分析插件处理失败: plugin={PluginManager.InternalName(registration.Plugin)}, " +
                    PluginManager.DescribeException(root));
                PluginManager.ReportPluginFailure(
                    registration.Method?.DeclaringType?.Name ?? registration.Source,
                    failure,
                    root.ToString());
            }
        }

    }

}
