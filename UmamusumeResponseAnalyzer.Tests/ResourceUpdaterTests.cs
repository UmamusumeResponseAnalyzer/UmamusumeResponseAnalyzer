using System.Net;
using System.Text;
using UmamusumeResponseAnalyzer.LiveDisplay;
using Xunit;

namespace UmamusumeResponseAnalyzer.Tests
{
    [Collection("ResourceUpdater")]
    public sealed class ResourceUpdaterTests : IDisposable
    {
        readonly string configPath = Path.Combine(Path.GetTempPath(), $"ura-test-{Guid.NewGuid():N}.yaml");
        readonly string originalConfigPath;
        readonly HttpClient originalHttpClient;

        public ResourceUpdaterTests()
        {
            originalConfigPath = Config.CONFIG_FILEPATH;
            originalHttpClient = ResourceUpdater.HttpClient;
            Config.CONFIG_FILEPATH = configPath;
            Config.Initialize();
        }

        public void Dispose()
        {
            if (File.Exists(configPath))
                File.Delete(configPath);

            Config.CONFIG_FILEPATH = originalConfigPath;
            ResourceUpdater.HttpClient = originalHttpClient;
            LiveDisplayConsole.UnbindForTests();
        }

        [Fact]
        public async Task Download_WithoutProgressContext_WritesFile()
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

        sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                return Task.FromResult(respond(request));
            }
        }
    }

    [CollectionDefinition("ResourceUpdater")]
    public sealed class ResourceUpdaterCollection
    {
    }
}
