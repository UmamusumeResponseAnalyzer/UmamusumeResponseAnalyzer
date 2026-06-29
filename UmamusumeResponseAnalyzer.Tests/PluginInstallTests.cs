using System.Net;
using System.Net.Sockets;
using System.Text;
using UmamusumeResponseAnalyzer.Plugin;
using WatsonWebserver.Core;
using WatsonWebserver.Lite;
using Xunit;

namespace UmamusumeResponseAnalyzer.Tests
{
    [Collection("PluginReload")]
    public sealed class PluginInstallTests : IDisposable
    {
        readonly string tempDir;
        readonly string originalCwd;
        readonly Func<string, string, string, bool> originalConfirmInstall;

        public PluginInstallTests()
        {
            tempDir = Path.Combine(Path.GetTempPath(), "ura-plugin-install-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            Directory.CreateDirectory(Path.Combine(tempDir, "Plugins"));
            originalCwd = Directory.GetCurrentDirectory();
            originalConfirmInstall = WebInstallApi.ConfirmInstall;
            Directory.SetCurrentDirectory(tempDir);
        }

        public void Dispose()
        {
            WebInstallApi.ConfirmInstall = originalConfirmInstall;
            Directory.SetCurrentDirectory(originalCwd);
            try { Directory.Delete(tempDir, recursive: true); } catch { }
        }

        [Fact]
        public async Task WebInstall_RequiresLocalConfirmationBeforeDownload()
        {
            WebInstallApi.ConfirmInstall = (_, _, _) => false;
            var port = GetFreePort();
            using var server = new WebserverLite(new WebserverSettings("127.0.0.1", port), ctx => ctx.Response.Send(string.Empty));
            WebInstallApi.Register(server);
            server.Start(TestContext.Current.CancellationToken);

            using var client = new HttpClient();
            using var request = new HttpRequestMessage(System.Net.Http.HttpMethod.Post, $"http://127.0.0.1:{port}/uracloud/install");
            request.Headers.Add("Origin", "https://ura.shuise.net");
            request.Content = new StringContent(
                """{"author":"tester","internalName":"NoConfirm","version":"1.0.0"}""",
                Encoding.UTF8,
                "application/json");

            using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            Assert.False(File.Exists(Path.Combine(tempDir, "Plugins", "NoConfirm.zip")));
        }

        [Fact]
        public async Task InstallPluginsAsync_SkipsBatchForkConflicts()
        {
            var installed = await PluginRepository.InstallPluginsAsync([
                new() { Author = "author-a", InternalName = "SameName", RawVersion = "1.0.0" },
                new() { Author = "author-b", InternalName = "SameName", RawVersion = "1.0.0" },
            ], TestContext.Current.CancellationToken);

            Assert.Empty(installed);
            Assert.False(File.Exists(Path.Combine(tempDir, "Plugins", "SameName.zip")));
        }

        [Fact]
        public async Task DownloadPluginZipAsync_WhenCopyFails_KeepsExistingZipAndDeletesTempFile()
        {
            var existing = Path.Combine(tempDir, PluginRepository.InstallZipPath("KeepOld"));
            await File.WriteAllTextAsync(existing, "old-zip", TestContext.Current.CancellationToken);
            var url = StartOneShotHttpServer("partial", declaredLength: 100);

            await Assert.ThrowsAnyAsync<Exception>(() =>
                PluginRepository.DownloadPluginZipAsync(url, "KeepOld", TestContext.Current.CancellationToken));

            Assert.Equal("old-zip", await File.ReadAllTextAsync(existing, TestContext.Current.CancellationToken));
            var tempRoot = Path.Combine(Path.GetTempPath(), "UmamusumeResponseAnalyzer");
            if (Directory.Exists(tempRoot))
                Assert.DoesNotContain(Directory.GetFiles(tempRoot), path => Path.GetFileName(path).Contains("KeepOld"));
        }

        [Fact]
        public async Task DownloadPluginZipAsync_CleansStaleTempFiles()
        {
            var tempRoot = Path.Combine(Path.GetTempPath(), "UmamusumeResponseAnalyzer");
            Directory.CreateDirectory(tempRoot);
            var stale = Path.Combine(tempRoot, "plugin-stale.tmp");
            await File.WriteAllTextAsync(stale, "stale", TestContext.Current.CancellationToken);
            File.SetLastWriteTimeUtc(stale, DateTime.UtcNow.AddDays(-2));
            var url = StartOneShotHttpServer("new-zip", declaredLength: "new-zip".Length);

            await PluginRepository.DownloadPluginZipAsync(url, "Fresh", TestContext.Current.CancellationToken);

            Assert.Equal("new-zip", await File.ReadAllTextAsync(Path.Combine(tempDir, "Plugins", "Fresh.zip"), TestContext.Current.CancellationToken));
            Assert.False(File.Exists(stale));
        }

        static int GetFreePort()
        {
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }

        static string StartOneShotHttpServer(string body, int declaredLength)
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            _ = Task.Run(async () =>
            {
                using var client = await listener.AcceptTcpClientAsync(TestContext.Current.CancellationToken);
                await using var stream = client.GetStream();
                var buffer = new byte[4096];
                await stream.ReadAtLeastAsync(buffer, minimumBytes: 1, throwOnEndOfStream: false, cancellationToken: TestContext.Current.CancellationToken);
                var header = $"HTTP/1.1 200 OK\r\nContent-Length: {declaredLength}\r\nConnection: close\r\n\r\n";
                await stream.WriteAsync(Encoding.ASCII.GetBytes(header));
                await stream.WriteAsync(Encoding.UTF8.GetBytes(body));
                listener.Stop();
            });
            return $"http://127.0.0.1:{port}/plugin.zip";
        }
    }
}
