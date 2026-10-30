using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using Gallop;
using Gallop.Endpoints;
using UmamusumeResponseAnalyzer.Plugin;
using WatsonWebserver.Core;
using WatsonWebserver.Lite;
using Xunit;
using HttpMethod = WatsonWebserver.Core.HttpMethod;

namespace UmamusumeResponseAnalyzer.Tests;

[Collection("PluginRuntime")]
public sealed class PluginRouteTests : IAsyncDisposable
{
    readonly WebserverLite originalServer;
    readonly List<IPlugin> plugins = [];
    readonly CancellationTokenSource lifetime = new(TimeSpan.FromSeconds(20));
    readonly HttpClient http = new(new HttpClientHandler { UseProxy = false });
    readonly int port;
    static string Prefix => "/" + typeof(PluginRouteTests).Assembly.GetName().Name;

    public PluginRouteTests(PluginRuntimeFixture runtime)
    {
        _ = runtime.Application;
        PluginManager.ShutdownAsync().GetAwaiter().GetResult();
        PluginManager.Init();
        originalServer = Server.Instance;
        using var reserve = new TcpListener(IPAddress.Loopback, 0);
        reserve.Start();
        port = ((IPEndPoint)reserve.LocalEndpoint).Port;
        reserve.Stop();
        Server.Instance = new(new WebserverSettings("127.0.0.1", port), context =>
        {
            context.Response.StatusCode = 404;
            return context.Response.Send(string.Empty);
        });
        Server.Start(lifetime.Token);
    }

    void Initialize(IPlugin plugin)
    {
        plugins.Add(plugin);
        PluginManager.InitializePlugin(plugin);
    }

    Uri Http(string path) => new($"http://127.0.0.1:{port}{path}");
    Uri Ws(string path) => new($"ws://127.0.0.1:{port}{Prefix}/{path}");

    [Fact]
    public async Task HttpAndFragmentedWebSocketShareListenerAndInstance()
    {
        var plugin = new EchoPlugin();
        Initialize(plugin);
        Assert.Equal("instance", await http.GetStringAsync(Http(Prefix + "/status"), lifetime.Token));
        using var client = new ClientWebSocket();
        await client.ConnectAsync(Ws("echo"), lifetime.Token);
        await client.SendAsync("hel"u8.ToArray(), WebSocketMessageType.Text, false, lifetime.Token);
        await client.SendAsync("lo"u8.ToArray(), WebSocketMessageType.Text, true, lifetime.Token);
        var buffer = new byte[64];
        var received = await client.ReceiveAsync(new ArraySegment<byte>(buffer), lifetime.Token);
        Assert.Equal("hello", Encoding.UTF8.GetString(buffer, 0, received.Count));
        Assert.True(received.EndOfMessage);
        await client.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", lifetime.Token);
        await plugin.Exited.Task.WaitAsync(lifetime.Token);
        Assert.Equal(1, plugin.HttpCalls);
        Assert.Equal("pong", await http.GetStringAsync(Http("/notify/ping"), lifetime.Token));
    }

    [Fact]
    public async Task InvalidHandshakeDoesNotInvokeWebSocketHandler()
    {
        var plugin = new EchoPlugin();
        Initialize(plugin);
        using var response = await http.GetAsync(Http(Prefix + "/echo"), lifetime.Token);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.False(plugin.Entered.Task.IsCompleted);
        Assert.Equal("instance", await http.GetStringAsync(Http(Prefix + "/status"), lifetime.Token));
    }

    [Fact]
    public async Task CleanupCancelsHttpAndWebSocketBeforeWaitingForCallbacks()
    {
        var plugin = new EchoPlugin();
        Initialize(plugin);
        using var client = new ClientWebSocket();
        await client.ConnectAsync(Ws("echo"), lifetime.Token);
        await plugin.Entered.Task.WaitAsync(lifetime.Token);
        var pendingHttp = http.GetAsync(Http(Prefix + "/wait"), lifetime.Token);
        await plugin.HttpWaiting.Task.WaitAsync(lifetime.Token);

        await PluginManager.CleanupPluginAsync(plugin).WaitAsync(TimeSpan.FromSeconds(3), lifetime.Token);

        Assert.True(plugin.Disposed);
        Assert.True(plugin.Exited.Task.IsCompletedSuccessfully);
        Assert.True(plugin.HttpExited);
        Assert.False(Server.Instance.Routes.PreAuthentication.Static.Exists(HttpMethod.GET, Prefix + "/echo"));
        try { using var response = await pendingHttp; }
        catch (HttpRequestException) { }
        Assert.Equal("pong", await http.GetStringAsync(Http("/notify/ping"), lifetime.Token));
    }

    [Fact]
    public async Task FaultedLifecycleAlsoCancelsWebSocketBeforeCleanup()
    {
        var plugin = new EchoPlugin();
        Initialize(plugin);
        using var client = new ClientWebSocket();
        await client.ConnectAsync(Ws("echo"), lifetime.Token);
        await plugin.Entered.Task.WaitAsync(lifetime.Token);
        Assert.True(PluginManager.TryDisableForMissingAssembly(plugin,
            new PluginManager.MissingPluginAssemblyException(new System.Reflection.AssemblyName("RouteMissingDependency")), "Route"));
        await plugin.Disposal.Task.WaitAsync(TimeSpan.FromSeconds(3), lifetime.Token);
        Assert.True(plugin.Exited.Task.IsCompletedSuccessfully);
        Assert.False(Server.Instance.Routes.PreAuthentication.Static.Exists(HttpMethod.GET, Prefix + "/echo"));
    }

    [Fact]
    public void RouteConflictRollsBackRoutesAndAnalyzersWithoutRemovingExistingRoute()
    {
        var registry = Server.Instance.Routes.PreAuthentication.Static;
        registry.Add(HttpMethod.GET, Prefix + "/conflict", context => context.Response.Send("existing"));
        try
        {
            Assert.Throws<InvalidOperationException>(() => Initialize(new ConflictPlugin()));
            Assert.False(registry.Exists(HttpMethod.GET, Prefix + "/added"));
            Assert.True(registry.Exists(HttpMethod.GET, Prefix + "/conflict"));
            Assert.Empty(PluginManager.ResponseAnalyzerMethods);
        }
        finally { registry.Remove(HttpMethod.GET, Prefix + "/conflict"); }
    }

    [Fact]
    public void InvalidSignatureAndFailedInitializationLeaveNoRoutes()
    {
        Assert.Throws<InvalidOperationException>(() => Initialize(new InvalidPlugin()));
        Assert.Throws<InvalidOperationException>(() => Initialize(new FailedPlugin()));
        Assert.False(Server.Instance.Routes.PreAuthentication.Static.Exists(HttpMethod.GET, Prefix + "/invalid"));
        Assert.False(Server.Instance.Routes.PreAuthentication.Static.Exists(HttpMethod.GET, Prefix + "/failed"));
    }

    [Fact]
    public async Task UpgradedHandlerFailureClosesConnectionWithoutWritingHttpError()
    {
        Initialize(new ThrowingPlugin());
        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, port, lifetime.Token);
        var stream = client.GetStream();
        await stream.WriteAsync(Encoding.ASCII.GetBytes(
            $"GET {Prefix}/throw HTTP/1.1\r\nHost: 127.0.0.1:{port}\r\nUpgrade: websocket\r\nConnection: Upgrade\r\nSec-WebSocket-Version: 13\r\nSec-WebSocket-Key: dGhlIHNhbXBsZSBub25jZQ==\r\n\r\n"), lifetime.Token);
        var headers = new StringBuilder();
        var single = new byte[1];
        while (!headers.ToString().EndsWith("\r\n\r\n", StringComparison.Ordinal))
        {
            Assert.Equal(1, await stream.ReadAsync(single, lifetime.Token));
            headers.Append((char)single[0]);
        }
        Assert.StartsWith("HTTP/1.1 101 ", headers.ToString());
        Assert.Equal(0, await stream.ReadAsync(single, lifetime.Token));
        Assert.Equal("pong", await http.GetStringAsync(Http("/notify/ping"), lifetime.Token));
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var plugin in plugins) await PluginManager.CleanupPluginAsync(plugin);
        lifetime.Cancel();
        await Server.StopAsync();
        Server.Instance = originalServer;
        http.Dispose();
        lifetime.Dispose();
        await PluginManager.ShutdownAsync();
    }

    sealed class EchoPlugin : IPlugin
    {
        internal int HttpCalls;
        internal bool HttpExited;
        internal bool Disposed;
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Exited { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource HttpWaiting { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Disposal { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void Initialize(IPluginContext context) { }

        [Route(HttpMethod.GET, "status")]
        Task Status(HttpContextBase context)
        {
            HttpCalls++;
            return context.Response.Send("instance", context.Token);
        }

        [Route(HttpMethod.GET, "wait")]
        async Task Wait(HttpContextBase context)
        {
            HttpWaiting.TrySetResult();
            try { await Task.Delay(Timeout.Infinite, context.Token); }
            finally { HttpExited = true; }
        }

        [WebSocketRoute("echo")]
        async Task Echo(HttpContextBase context, WebSocket socket, CancellationToken token)
        {
            Entered.TrySetResult();
            try
            {
                var buffer = new byte[64];
                using var message = new MemoryStream();
                while (socket.State == WebSocketState.Open)
                {
                    var received = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), token);
                    if (received.MessageType == WebSocketMessageType.Close)
                    {
                        await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "ok", token);
                        return;
                    }
                    message.Write(buffer, 0, received.Count);
                    if (!received.EndOfMessage) continue;
                    await socket.SendAsync(message.ToArray(), received.MessageType, true, token);
                    message.SetLength(0);
                }
            }
            finally { Exited.TrySetResult(); }
        }

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            Disposal.TrySetResult();
            return ValueTask.CompletedTask;
        }
    }

    sealed class ConflictPlugin : IPlugin
    {
        public void Initialize(IPluginContext context) { }
        [Route(HttpMethod.GET, "added")]
        static Task Added(HttpContextBase context) => context.Response.Send("added");
        [Route(HttpMethod.GET, "conflict")]
        static Task Conflict(HttpContextBase context) => context.Response.Send("conflict");
        [ResponseAnalyzer<GameApi.Account.Index>]
        static ValueTask Analyze(DataLinkIndexResponse payload) => ValueTask.CompletedTask;
    }

    sealed class InvalidPlugin : IPlugin
    {
        public void Initialize(IPluginContext context) { }
        [WebSocketRoute("invalid")]
        static Task Invalid(HttpContextBase context) => Task.CompletedTask;
    }

    sealed class FailedPlugin : IPlugin
    {
        public void Initialize(IPluginContext context) => throw new InvalidOperationException("initialize failed");
        [Route(HttpMethod.GET, "failed")]
        static Task Failed(HttpContextBase context) => context.Response.Send("failed");
    }

    sealed class ThrowingPlugin : IPlugin
    {
        public void Initialize(IPluginContext context) { }
        [WebSocketRoute("throw")]
        static Task Throw(HttpContextBase context, WebSocket socket, CancellationToken token)
            => throw new InvalidOperationException("route failure after upgrade");
    }
}
