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
    internal sealed class ServerRequestBarrier(CancellationToken hostCancellationToken) : IDisposable
    {
        readonly object gate = new();
        readonly CancellationTokenSource lifetime = CancellationTokenSource.CreateLinkedTokenSource(hostCancellationToken);
        readonly TaskCompletionSource drained = new(TaskCreationOptions.RunContinuationsAsynchronously);
        int inFlight;
        bool stopping;

        internal CancellationToken Token => lifetime.Token;

        internal Func<HttpContextBase, Task> Wrap(Func<HttpContextBase, CancellationToken, Task> handler) => ctx => InvokeAsync(ctx, handler);

        async Task InvokeAsync(HttpContextBase ctx, Func<HttpContextBase, CancellationToken, Task> handler)
        {
            var rejected = false;
            lock (gate)
            {
                if (stopping)
                {
                    rejected = true;
                    ctx.Response.StatusCode = 503;
                }
                else
                {
                    inFlight++;
                }
            }

            if (rejected)
            {
                await ctx.Response.Send("server_stopping");
                return;
            }

            try
            {
                await handler(ctx, lifetime.Token);
            }
            finally
            {
                lock (gate)
                {
                    if (--inFlight == 0 && stopping)
                        drained.TrySetResult();
                }
            }
        }

        internal Task StopAsync()
        {
            lock (gate)
            {
                if (stopping)
                    return drained.Task;

                stopping = true;
                if (inFlight == 0)
                    drained.TrySetResult();
            }

            lifetime.Cancel();
            return drained.Task;
        }

        public void Dispose() => lifetime.Dispose();
    }

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
        static ServerRequestBarrier? requests;
        static Task? shutdownTask;
        internal static WebserverLite Instance
        {
            get => Volatile.Read(ref instance) ?? defaultInstance.Value;
            set => Volatile.Write(ref instance, value);
        }
        internal static bool IsRunning => Volatile.Read(ref instance)?.IsListening
            ?? (defaultInstance.IsValueCreated && defaultInstance.Value.IsListening);
        internal static void Start(CancellationToken hostCancellationToken)
        {
            if (Volatile.Read(ref shutdownTask) is not null)
                throw new InvalidOperationException("HTTP server lifecycle 已启动，不能重复 Start。");

            var requestBarrier = new ServerRequestBarrier(hostCancellationToken);
            if (Interlocked.CompareExchange(ref requests, requestBarrier, null) is not null)
            {
                requestBarrier.Dispose();
                throw new InvalidOperationException("HTTP server lifecycle 已启动，不能重复 Start。");
            }

            Instance.Routes.PreAuthentication.Static.Add(
                WatsonWebserver.Core.HttpMethod.POST,
                "/notify/response",
                requestBarrier.Wrap((ctx, cancellationToken) => HandleNotificationAsync(AnalyzerKind.Response, ctx, cancellationToken)));
            Instance.Routes.PreAuthentication.Static.Add(
                WatsonWebserver.Core.HttpMethod.POST,
                "/notify/request",
                requestBarrier.Wrap((ctx, cancellationToken) => HandleNotificationAsync(AnalyzerKind.Request, ctx, cancellationToken)));
            Instance.Routes.PreAuthentication.Static.Add(
                WatsonWebserver.Core.HttpMethod.GET,
                "/notify/ping",
                requestBarrier.Wrap((ctx, cancellationToken) =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    TerminalUi.Log("Server", I18N_PingReceived, UiSeverity.Trace);
                    return ctx.Response.Send("pong");
                }));
            WebInstallApi.Register(Instance, requestBarrier);
            Instance.Start(requestBarrier.Token);
        }

        internal static Task StopAsync()
        {
            var existing = Volatile.Read(ref shutdownTask);
            if (existing is not null)
                return existing;

            var server = Volatile.Read(ref instance);
            if (server is null)
            {
                if (!defaultInstance.IsValueCreated)
                    return Task.CompletedTask;
                server = defaultInstance.Value;
            }

            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var task = completion.Task;
            existing = Interlocked.CompareExchange(ref shutdownTask, task, null);
            if (existing is not null)
                return existing;

            _ = CompleteShutdownAsync(completion, server, Volatile.Read(ref requests));
            return task;
        }

        static async Task CompleteShutdownAsync(
            TaskCompletionSource completion,
            WebserverLite server,
            ServerRequestBarrier? requestBarrier)
        {
            try
            {
                await ShutdownCoreAsync(server, requestBarrier);
                completion.SetResult();
            }
            catch (OperationCanceledException ex)
            {
                completion.SetCanceled(ex.CancellationToken);
            }
            catch (Exception ex)
            {
                completion.SetException(ex);
            }
        }

        internal static Task ShutdownCoreAsync(WebserverLite server, ServerRequestBarrier? requestBarrier)
        {
            var drained = Task.CompletedTask;
            return UmamusumeResponseAnalyzer.RunCleanupAsync(
                null,
                [
                    () =>
                    {
                        drained = requestBarrier?.StopAsync() ?? Task.CompletedTask;
                        return ValueTask.CompletedTask;
                    },
                    () =>
                    {
                        if (server.IsListening)
                            server.Stop();
                        return ValueTask.CompletedTask;
                    },
                    () => new ValueTask(drained),
                    () =>
                    {
                        server.Dispose();
                        return ValueTask.CompletedTask;
                    },
                    () =>
                    {
                        requestBarrier?.Dispose();
                        return ValueTask.CompletedTask;
                    },
                ],
                "HTTP server shutdown 失败。");
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
                _ = PluginManager.ReportPluginFailure(
                    registration.Method?.DeclaringType?.Name ?? registration.Source,
                    failure,
                    root.ToString());
            }
        }

    }

}
