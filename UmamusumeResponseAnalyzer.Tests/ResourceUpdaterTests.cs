using System.Reflection;
using System.Net;
using System.Text;
using UmamusumeResponseAnalyzer.LiveDisplay;
using Xunit;

namespace UmamusumeResponseAnalyzer.Tests
{
    [Collection("ResourceUpdater")]
    public sealed class ResourceUpdaterTests : IDisposable
    {
        const string UpdateAssetsHookName = "UpdateAssetsAfterProgramUpdateAsync";
        readonly string latestProgramPath = Path.Combine(Path.GetTempPath(), "latest-UmamusumeResponseAnalyzer.exe");
        readonly string configPath = Path.Combine(Path.GetTempPath(), $"ura-test-{Guid.NewGuid():N}.yaml");
        readonly FieldInfo? updateAssetsHook;
        readonly object? originalUpdateAssetsHook;
        readonly string originalConfigPath;
        readonly HttpClient originalHttpClient;

        public ResourceUpdaterTests()
        {
            originalConfigPath = Config.CONFIG_FILEPATH;
            originalHttpClient = ResourceUpdater.HttpClient;
            updateAssetsHook = typeof(ResourceUpdater).GetField(UpdateAssetsHookName, BindingFlags.NonPublic | BindingFlags.Static)!;
            originalUpdateAssetsHook = updateAssetsHook?.GetValue(null);
            Config.CONFIG_FILEPATH = configPath;
            Config.Initialize();
        }

        public void Dispose()
        {
            if (updateAssetsHook is not null)
                updateAssetsHook.SetValue(null, originalUpdateAssetsHook);

            if (File.Exists(latestProgramPath))
                File.Delete(latestProgramPath);

            if (File.Exists(configPath))
                File.Delete(configPath);

            Config.CONFIG_FILEPATH = originalConfigPath;
            ResourceUpdater.HttpClient = originalHttpClient;
            LiveDisplayConsole.UnbindForTests();
        }

        [Fact]
        public async Task TryUpdateProgram_WhenLatestExecutableAlreadyApplied_UpdatesAssets()
        {
            Assert.NotNull(updateAssetsHook);
            File.Copy(Environment.ProcessPath!, latestProgramPath, overwrite: true);
            var updateAssetsCallCount = 0;
            updateAssetsHook!.SetValue(null, (Func<Task>)(() =>
            {
                updateAssetsCallCount++;
                return Task.CompletedTask;
            }));

            try
            {
                await ResourceUpdater.TryUpdateProgram();
            }
            catch (IOException)
            {
                // Test runners may not expose a real console handle for the final clear.
            }

            Assert.Equal(1, updateAssetsCallCount);
            Assert.False(File.Exists(latestProgramPath));
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
