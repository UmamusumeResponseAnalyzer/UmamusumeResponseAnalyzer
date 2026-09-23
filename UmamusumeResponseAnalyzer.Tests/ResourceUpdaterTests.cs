using System.Net;
using System.Text;
using UmamusumeResponseAnalyzer.TerminalGui;
using Xunit;

namespace UmamusumeResponseAnalyzer.Tests
{
    [Collection("PluginRuntime")]
    public sealed class ResourceUpdaterTests : IDisposable
    {
        readonly HttpClient originalHttpClient;

        public ResourceUpdaterTests(PluginRuntimeFixture fixture)
        {
            _ = fixture;
            originalHttpClient = ResourceUpdater.HttpClient;
        }

        public void Dispose()
        {
            ResourceUpdater.HttpClient = originalHttpClient;
        }

        [Fact]
        public async Task Download_WithoutProgressSink_WritesFile()
        {
            var path = Path.Combine(Path.GetTempPath(), $"ura-download-{Guid.NewGuid():N}.br");
            ResourceUpdater.HttpClient = new(new StubHttpMessageHandler(_ =>
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("downloaded", Encoding.UTF8)
                }));

            try
            {
                await ResourceUpdater.Download(path: path);

                Assert.Equal("downloaded", await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
            }
            finally
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
        }

        [Fact]
        public async Task Download_WhenRequestFails_ThrowsAndDoesNotLeavePartialFile()
        {
            var path = Path.Combine(Path.GetTempPath(), $"ura-download-fail-{Guid.NewGuid():N}.br");
            ResourceUpdater.HttpClient = new(new StubHttpMessageHandler(_ =>
                new HttpResponseMessage(HttpStatusCode.InternalServerError)
                {
                    Content = new StringContent("server error", Encoding.UTF8)
                }));

            await Assert.ThrowsAsync<HttpRequestException>(() => ResourceUpdater.Download(path: path));
            Assert.False(File.Exists(path));
        }

        [Fact]
        public void InstallProgramUpdate_CopiesCurrentExecutableAndRequestsRestart()
        {
            var path = Path.Combine(Path.GetTempPath(), $"ura-install-update-{Guid.NewGuid():N}.exe");
            try
            {
                var exception = Assert.Throws<UmamusumeResponseAnalyzer.PostShutdownProcessRequestedException>(
                    () => ResourceUpdater.InstallProgramUpdate(path));

                Assert.Equal(Path.GetFullPath(path), exception.StartInfo.FileName);
                Assert.True(exception.StartInfo.UseShellExecute);
                Assert.Equal(
                    File.ReadAllBytes(Environment.ProcessPath!),
                    File.ReadAllBytes(path));
            }
            finally
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
        }

        sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                return Task.FromResult(respond(request));
            }
        }
    }
}
