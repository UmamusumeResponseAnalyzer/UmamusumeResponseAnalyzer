using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using GamePacketCollector;
using UmamusumeResponseAnalyzer.TerminalGui;
using UiText = UmamusumeResponseAnalyzer.Localization.TerminalGui;

static class GamePacketCollectorRuntimeSmoke
{
    public static void Run(WorkspaceSmokeSession ui)
    {
        using var currentDirectory = new TempCurrentDirectory("GamePacketCollector-permanent-failure");
        using var server = new PermanentFailureServer(expectedRequests: 2);
        var dataDirectory = Path.Combine("PluginData", "游戏包采集");
        var pendingDirectory = Path.Combine(dataDirectory, "pending");
        var failedDirectory = Path.Combine(dataDirectory, "failed");
        Directory.CreateDirectory(pendingDirectory);
        new PacketUploadConfig
        {
            Enabled = true,
            UploadUrl = server.Url,
            EndpointGroups = [PacketUploadConfig.SingleModeEndpointGroup],
        }.Save(Path.Combine(dataDirectory, "config.json"), new(JsonSerializerDefaults.Web));
        File.WriteAllText(Path.Combine(pendingDirectory, "first.json"), "{\"packetIdemKey\":\"first\"}");
        File.WriteAllText(Path.Combine(pendingDirectory, "second.json"), "{\"packetIdemKey\":\"second\"}");

        ui.Bootstrap.SwitchTo();
        var beforeFailure = ui.CaptureScreen();
        if (HasPermanentFailureNotification(beforeFailure))
            throw new InvalidOperationException("A GamePacketCollector upload-failure notification was already visible before the upload attempt.");

        using var context = new RuntimePluginContext(ui.Application);
        var plugin = new GamePacketCollectorPlugin();
        try
        {
            plugin.Initialize(context);
            server.WaitForRequests();
            WaitUntil(
                () => Directory.Exists(failedDirectory)
                    && Directory.GetFiles(failedDirectory, "*.json", SearchOption.AllDirectories).Length == 2,
                "Both permanent upload failures were not moved to the failed directory.");

            if (Directory.GetFiles(pendingDirectory, "*.json", SearchOption.TopDirectoryOnly).Length != 0)
                throw new InvalidOperationException("Permanent upload failures remained in the pending directory.");
            if (!ReferenceEquals(Workspace.Current, ui.Bootstrap))
                throw new InvalidOperationException("GamePacketCollector changed the active Workspace while logging upload failures.");

            var framebuffer = string.Empty;
            WaitUntil(
                () => HasPermanentFailureNotification(framebuffer = ui.CaptureScreen()),
                "The public framebuffer did not show the new GamePacketCollector Error notification card.");
            if (!framebuffer.Contains("GamePackets upload failed: 400", StringComparison.Ordinal))
                throw new InvalidOperationException("The Bootstrap recent-log framebuffer did not show the upload failure.");
        }
        finally
        {
            plugin.Dispose();
        }

        if (!ReferenceEquals(Workspace.Current, ui.Bootstrap))
            throw new InvalidOperationException("GamePacketCollector Dispose changed the active Workspace.");
    }

    static bool HasPermanentFailureNotification(string framebuffer)
    {
        var lines = framebuffer.ReplaceLineEndings("\n").Split('\n');
        for (var index = 0; index < lines.Length - 1; index++)
        {
            if (lines[index].Contains(UiText.Severity_Error, StringComparison.Ordinal)
                && lines[index + 1].Contains("[GamePacketCollector] 上传失败", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    static void WaitUntil(Func<bool> condition, string failure)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (!condition())
        {
            if (DateTime.UtcNow >= deadline)
                throw new TimeoutException(failure);
            Thread.Sleep(10);
        }
    }

    sealed class PermanentFailureServer : IDisposable
    {
        readonly TcpListener listener = new(IPAddress.Loopback, 0);
        readonly CancellationTokenSource stopping = new();
        readonly Task serveTask;

        public PermanentFailureServer(int expectedRequests)
        {
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            Url = $"http://127.0.0.1:{port}/api/GamePackets";
            serveTask = Task.Run(() => ServeAsync(expectedRequests, stopping.Token));
        }

        public string Url { get; }

        public void WaitForRequests()
            => serveTask.WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();

        async Task ServeAsync(int expectedRequests, CancellationToken cancellationToken)
        {
            for (var request = 0; request < expectedRequests; request++)
            {
                using var client = await listener.AcceptTcpClientAsync(cancellationToken);
                await RespondAsync(client.GetStream(), cancellationToken);
            }
        }

        static async Task RespondAsync(NetworkStream stream, CancellationToken cancellationToken)
        {
            var headerBytes = new List<byte>();
            var next = new byte[1];
            while (!EndsWithHeaderTerminator(headerBytes))
            {
                if (headerBytes.Count == 32 * 1024)
                    throw new InvalidOperationException("HTTP request headers exceeded the smoke-test limit.");
                if (await stream.ReadAsync(next, cancellationToken) == 0)
                    throw new EndOfStreamException("HTTP request ended before its headers completed.");
                headerBytes.Add(next[0]);
            }

            var headers = Encoding.ASCII.GetString([.. headerBytes]);
            var contentLength = headers
                .Split("\r\n", StringSplitOptions.RemoveEmptyEntries)
                .Where(line => line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
                .Select(line => int.Parse(line[(line.IndexOf(':') + 1)..].Trim()))
                .SingleOrDefault();
            if (contentLength > 0)
                await stream.ReadExactlyAsync(new byte[contentLength], cancellationToken);

            var response = Encoding.UTF8.GetBytes(
                "HTTP/1.1 400 Bad Request\r\nContent-Type: text/plain\r\nContent-Length: 10\r\nConnection: close\r\n\r\nbad packet");
            await stream.WriteAsync(response, cancellationToken);
            await stream.FlushAsync(cancellationToken);
        }

        static bool EndsWithHeaderTerminator(List<byte> bytes)
            => bytes.Count >= 4
                && bytes[^4] == '\r'
                && bytes[^3] == '\n'
                && bytes[^2] == '\r'
                && bytes[^1] == '\n';

        public void Dispose()
        {
            stopping.Cancel();
            listener.Stop();
            try
            {
                serveTask.GetAwaiter().GetResult();
            }
            catch (OperationCanceledException) { }
            catch (SocketException) when (stopping.IsCancellationRequested) { }
            stopping.Dispose();
        }
    }
}
