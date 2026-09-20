using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UmamusumeResponseAnalyzer.Plugin;
using WatsonWebserver.Core;
using WatsonWebserver.Lite;
using Terminal.Gui.Input;
using Xunit;

namespace UmamusumeResponseAnalyzer.Tests;

[Collection("PluginReload")]
public sealed class PluginInstallTests : IDisposable
{
    readonly string tempDir = Path.Combine(Path.GetTempPath(), "ura-plugin-install-" + Guid.NewGuid().ToString("N"));
    readonly string originalCwd = Directory.GetCurrentDirectory();
    readonly Func<PluginInformation, CancellationToken, bool> originalConfirm = WebInstallApi.ConfirmInstall;
    readonly HttpClient originalHttp = ResourceUpdater.HttpClient;
    readonly PackageHandler handler;
    readonly TerminalGuiTestApp terminal;
    readonly byte[] package;
    const string Name = "InstallFixture";

    public PluginInstallTests(PluginRuntimeFixture fixture)
    {
        terminal = fixture.Terminal;
        Directory.CreateDirectory(tempDir);
        Directory.CreateDirectory(Path.Combine(tempDir, "Plugins"));
        PluginManager.ShutdownAsync().GetAwaiter().GetResult();
        Directory.SetCurrentDirectory(tempDir);
        var path = Path.Combine(tempDir, "fixture.zip");
        PluginCompiler.CompilePackage(PluginCode, Name, path, version: "2026.03.04");
        package = File.ReadAllBytes(path);
        var manifest = PluginPackageValidator.Validate(path, false).Manifest;
        handler = new PackageHandler(package, manifest);
        ResourceUpdater.HttpClient = new HttpClient(handler);
        WebInstallApi.ConfirmInstall = (_, _) => true;
        PluginManager.Init();
        PluginManager.InitializeLoadedPlugins();
    }

    public void Dispose()
    {
        try { PluginManager.ShutdownAsync().GetAwaiter().GetResult(); }
        finally
        {
            WebInstallApi.ConfirmInstall = originalConfirm;
            ResourceUpdater.HttpClient.Dispose();
            ResourceUpdater.HttpClient = originalHttp;
            Directory.SetCurrentDirectory(originalCwd);
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task WebCancellationAndInvalidRequestsNeverDownload()
    {
        WebInstallApi.ConfirmInstall = (_, _) => throw new InvalidOperationException("Must reject before confirmation.");
        using var requests = new ServerRequestBarrier(TestContext.Current.CancellationToken);
        using var server = StartServer(requests, out var port);
        using var client = new HttpClient();
        foreach (var (origin, body, expected) in new[]
        {
            ("https://example.com", "{}", HttpStatusCode.Forbidden),
            ("http://localhost:5173", "{\"repositoryId\":1,\"releaseId\":10}", HttpStatusCode.Forbidden),
            ("", "{}", HttpStatusCode.Forbidden),
            ("https://ura.shuise.net", "not json", HttpStatusCode.BadRequest),
            ("https://ura.shuise.net", "{\"repositoryId\":0,\"releaseId\":10}", HttpStatusCode.BadRequest),
        })
        {
            using var request = WebRequest(port);
            request.Headers.Remove("Origin");
            if (origin.Length > 0)
                request.Headers.Add("Origin", origin);
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, TestContext.Current.CancellationToken);
            Assert.Equal(expected, response.StatusCode);
        }
        Assert.Equal(0, handler.Requests);
        handler.Packages[10] = (2, handler.Manifest, package);
        using var mismatchedRequest = WebRequest(port);
        using var mismatchedResponse = await client.SendAsync(mismatchedRequest, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.InternalServerError, mismatchedResponse.StatusCode);
        Assert.Contains("引用与请求不匹配", await mismatchedResponse.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        handler.Packages[10] = (1, handler.Manifest, package);
        WebInstallApi.ConfirmInstall = (_, _) => false;
        using var cancelledRequest = WebRequest(port);
        using var cancelledResponse = await client.SendAsync(cancelledRequest, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Conflict, cancelledResponse.StatusCode);
        Assert.Empty(handler.Downloads);
        Assert.Empty(Directory.GetFiles("Plugins"));
    }

    [Fact]
    public async Task WebInstallUsesManifestAndStatusOnlyIncludesLoadedPlugins()
    {
        var sourcePath = Path.ChangeExtension(PluginRepository.InstallZipPath(Name), ".source.json");
        File.WriteAllText(sourcePath, "existing record is not read or rewritten");
        PluginCompiler.CompilePackage(PluginCode, "Dormant", Path.Combine("Plugins", "Dormant.zip"));
        handler.Manifest.Description = "2026-08-11T03:16:39.2341390Z";
        PluginInformation? confirmed = null;
        WebInstallApi.ConfirmInstall = (p, _) => { confirmed = p; return true; };
        using var requests = new ServerRequestBarrier(TestContext.Current.CancellationToken);
        using var server = StartServer(requests, out var port);
        using var client = new HttpClient();
        using var request = WebRequest(port);
        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        var body = JObject.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(body.Value<bool>("ok"));
        Assert.True(body.Value<bool>("loaded"));
        Assert.Equal(Name, confirmed!.InternalName);
        Assert.Equal("2026.03.04", confirmed.RawVersion);
        Assert.Equal(handler.Manifest.Description, confirmed.Description);
        var message = WebInstallApi.BuildInstallConfirmation(confirmed);
        Assert.Contains($"{confirmed.Author}/{confirmed.InternalName}", message);
        Assert.Contains(PluginRepository.Text("ExecutionWarning"), message);
        Assert.Equal(Name, body.Value<string>("installed"));
        Assert.Equal("existing record is not read or rewritten", File.ReadAllText(sourcePath));
        Assert.Single(Directory.GetFiles("Plugins", "*.source.json"));
        Assert.NotNull(PluginManager.FindLoadedPlugin(Name));
        var status = JObject.Parse(await client.GetStringAsync($"http://127.0.0.1:{port}/uracloud/status", TestContext.Current.CancellationToken));
        var installed = Assert.Single(status["plugins"]!);
        Assert.Equal(Name, installed["internalName"]!.Value<string>());
        Assert.Equal(JTokenType.Null, installed["source"]!.Type);
        Assert.True(installed["loaded"]!.Value<bool>());
        Assert.Single(handler.Downloads);
        Assert.Equal(2, handler.Requests);
    }

    [Theory]
    [InlineData("author")]
    [InlineData("name")]
    [InlineData("version")]
    [InlineData("zip")]
    [InlineData("size")]
    [InlineData("withdrawn")]
    public async Task DownloadValidationFailuresPreserveInstalledZip(string failure)
    {
        var zip = PluginRepository.InstallZipPath(Name);
        File.WriteAllBytes(zip, package);
        var plugin = await PluginRepository.GetPluginAsync(1, 10, TestContext.Current.CancellationToken);
        switch (failure)
        {
            case "author":
                plugin.Author = "other";
                break;
            case "name":
                plugin.InternalName = "OtherPlugin";
                break;
            case "version":
                plugin.RawVersion = "2026.03.05";
                break;
            case "zip":
                handler.Bytes = [1, 2, 3];
                break;
            case "size":
                handler.OnDownloadResponse = r => r.Content.Headers.ContentLength = 64L * 1024 * 1024 + 1;
                break;
            case "withdrawn":
                handler.OnDownloadResponse = r => r.StatusCode = HttpStatusCode.Gone;
                break;
        }
        if (failure == "withdrawn")
            await Assert.ThrowsAsync<HttpRequestException>(() => PluginRepository.DownloadPluginZipAsync(plugin, TestContext.Current.CancellationToken));
        else
            await Assert.ThrowsAsync<InvalidDataException>(() => PluginRepository.DownloadPluginZipAsync(plugin, TestContext.Current.CancellationToken));
        Assert.Equal(package, File.ReadAllBytes(zip));
        Assert.Empty(Directory.GetFiles("Plugins", "*.tmp"));
        Assert.Empty(Directory.GetFiles("Plugins", "*.source.json"));
        Assert.Null(PluginManager.FindLoadedPlugin(Name));
    }

    [Fact]
    public async Task WebLoadFailureKeepsInstalledZipAndReportsSeparateResults()
    {
        var zip = PluginRepository.InstallZipPath(Name);
        var sourcePath = Path.ChangeExtension(zip, ".source.json");
        Directory.CreateDirectory(sourcePath);
        var brokenPath = Path.Combine(tempDir, "broken.zip");
        PluginCompiler.CompilePackage(PluginCode.Replace("{ }", "{ throw new System.InvalidOperationException(\"load failure\"); }"),
            Name, brokenPath, version: "2026.03.04");
        handler.Bytes = File.ReadAllBytes(brokenPath);
        using var requests = new ServerRequestBarrier(TestContext.Current.CancellationToken);
        using var server = StartServer(requests, out var port);
        using var client = new HttpClient();
        using var request = WebRequest(port);
        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        var body = JObject.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(body.Value<bool>("ok"));
        Assert.False(body.Value<bool>("loaded"));
        Assert.False(string.IsNullOrWhiteSpace(body.Value<string>("error")));
        Assert.Equal(handler.Bytes, File.ReadAllBytes(zip));
        Assert.True(Directory.Exists(sourcePath));
        Assert.Null(PluginManager.FindLoadedPlugin(Name));
        var status = JObject.Parse(await client.GetStringAsync($"http://127.0.0.1:{port}/uracloud/status", TestContext.Current.CancellationToken));
        Assert.Empty(status["plugins"]!);
    }

    [Fact]
    public async Task UpdatesCompareLoadedVersionsAndRejectAmbiguousSources()
    {
        File.WriteAllBytes(PluginRepository.InstallZipPath(Name), package);
        Assert.Empty(await PluginRepository.CheckForUpdatesAsync(TestContext.Current.CancellationToken));
        Assert.Equal(0, handler.Requests);
        await PluginManager.ReloadPluginsAsync(Name);
        Assert.Empty(await PluginRepository.CheckForUpdatesAsync(TestContext.Current.CancellationToken));
        handler.Manifest.RawVersion = "2026.03.05";
        var update = Assert.Single(await PluginRepository.CheckForUpdatesAsync(TestContext.Current.CancellationToken));
        Assert.Equal(new Version(2026, 3, 4), update.CurrentVersion);
        Assert.Equal(new Version(2026, 3, 5), update.LatestVersion);
        handler.Packages.Add(20, (2, handler.Manifest, package));
        var error = await Assert.ThrowsAsync<InvalidDataException>(() => PluginRepository.CheckForUpdatesAsync(TestContext.Current.CancellationToken));
        Assert.Contains(Name, error.Message);
        await Assert.ThrowsAsync<InvalidOperationException>(() => PluginRepository.InstallPluginsAsync([handler.Manifest, handler.Manifest]));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MenuInstallsLatestStableAndReloadsAfterAllDownloads(bool updateInstalled)
    {
        var otherPath = Path.Combine(tempDir, "Zulu.zip");
        PluginCompiler.CompilePackage(PluginCode, "Zulu", otherPath);
        using (var archive = ZipFile.Open(otherPath, ZipArchiveMode.Update))
        {
            var entry = archive.GetEntry("manifest.json")!;
            JObject manifest;
            using (var reader = new StreamReader(entry.Open()))
                manifest = JObject.Parse(reader.ReadToEnd());
            manifest["Category"] = "Tools";
            entry.Delete();
            using var writer = new StreamWriter(archive.CreateEntry("manifest.json").Open());
            writer.Write(manifest.ToString());
        }
        var bytes = File.ReadAllBytes(otherPath);
        handler.Packages.Add(20, (2, PluginPackageValidator.Validate(otherPath, false).Manifest, bytes));
        var olderPath = Path.Combine(tempDir, "older.zip");
        PluginCompiler.CompilePackage(PluginCode, Name, olderPath, version: "2026.03.03");
        handler.Packages.Add(9, (1, PluginPackageValidator.Validate(olderPath, false).Manifest, File.ReadAllBytes(olderPath)));
        var prereleasePath = Path.Combine(tempDir, "prerelease.zip");
        PluginCompiler.CompilePackage(PluginCode, Name, prereleasePath, version: "2026.03.05");
        handler.Packages.Add(11, (1, PluginPackageValidator.Validate(prereleasePath, false).Manifest, File.ReadAllBytes(prereleasePath)));
        handler.Prereleases.Add(11);
        if (updateInstalled)
        {
            File.Copy(olderPath, PluginRepository.InstallZipPath(Name));
            await PluginManager.ReloadPluginsAsync(Name);
        }
        WebInstallApi.ConfirmInstall = (_, _) => throw new InvalidOperationException("Menu must not use web confirmation.");
        handler.AfterDownload = () =>
        {
            Assert.Null(PluginManager.FindLoadedPlugin("Zulu"));
            Assert.Equal(updateInstalled ? new Version(2026, 3, 3) : null,
                PluginManager.SnapshotPluginStatuses().SingleOrDefault(p => p.IsLoaded)?.Version);
        };
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cancellation.CancelAfter(TimeSpan.FromSeconds(20));
        var menu = Task.Run(() => PluginRepository.ShowMenuAsync(cancellation.Token), cancellation.Token);
        try
        {
            await terminal.WaitForScreenAsync(PluginRepository.Text("SelectPlugins"));
            var screen = await terminal.CaptureScreenAsync();
            TestContext.Current.TestOutputHelper!.WriteLine(screen);
            Assert.True(screen.IndexOf("Zulu", StringComparison.Ordinal) < screen.IndexOf(Name, StringComparison.Ordinal), screen);
            await terminal.InjectAsync(Key.Space);
            await terminal.InjectAsync(Key.CursorDown);
            await terminal.InjectAsync(Key.Space);
            await terminal.InjectAsync(Key.Tab);
            await terminal.InjectAsync(Key.Enter);
            await terminal.WaitForScreenAsync("插件已安装并生效");
            Assert.Equal([20L, 10L], handler.Downloads);
            Assert.Equal(3, handler.Requests);
            Assert.Equal(package, File.ReadAllBytes(PluginRepository.InstallZipPath(Name)));
            Assert.Equal(new Version(2026, 3, 4),
                PluginManager.SnapshotPluginStatuses().Single(p => p.InternalName == Name).Version);
            Assert.NotNull(PluginManager.FindLoadedPlugin(Name));
            Assert.NotNull(PluginManager.FindLoadedPlugin("Zulu"));
            Assert.Empty(Directory.GetFiles("Plugins", "*.source.json"));
            await terminal.InjectAsync(Key.Enter);
            await menu.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            await cancellation.CancelAsync();
            try { await menu; }
            catch (OperationCanceledException) { }
        }
    }

    internal static WebserverLite StartServer(ServerRequestBarrier requests, out int port)
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        var server = new WebserverLite(new WebserverSettings("127.0.0.1", port), ctx => ctx.Response.Send(string.Empty));
        WebInstallApi.Register(server, requests);
        server.Start(TestContext.Current.CancellationToken);
        return server;
    }

    static HttpRequestMessage WebRequest(int port)
    {
        var request = new HttpRequestMessage(System.Net.Http.HttpMethod.Post, $"http://127.0.0.1:{port}/uracloud/install");
        request.Headers.Add("Origin", "https://ura.shuise.net");
        request.Content = new StringContent("""{"repositoryId":1,"releaseId":10}""", Encoding.UTF8, "application/json");
        return request;
    }

    internal sealed class PackageHandler(byte[] bytes, PluginInformation manifest) : HttpMessageHandler
    {
        internal readonly Dictionary<long, (long RepositoryId, PluginInformation Manifest, byte[] Bytes)> Packages = new() { [10] = (1, manifest, bytes) };
        internal readonly HashSet<long> Prereleases = [];
        internal PluginInformation Manifest => Packages[10].Manifest;
        internal byte[] Bytes
        {
            get => Packages[10].Bytes;
            set => Packages[10] = (Packages[10].RepositoryId, Manifest, value);
        }
        internal readonly List<long> Downloads = [];
        internal int Requests;
        internal Action? AfterDownload;
        internal Action<HttpResponseMessage>? OnDownloadResponse;
        internal Task? DownloadBarrier;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Requests++;
            Assert.Equal(new Uri(PluginRepository.PluginApiBase).Host, request.RequestUri!.Host);
            Assert.Equal("https", request.RequestUri.Scheme);
            Assert.Empty(request.RequestUri.Query);
            var segments = request.RequestUri.AbsolutePath.TrimEnd('/').Split('/');
            if (segments[^1] == "download")
            {
                var releaseId = long.Parse(segments[^2]);
                var package = Packages[releaseId];
                Downloads.Add(releaseId);
                Assert.Equal(package.RepositoryId, long.Parse(segments[^4]));
                Assert.Empty(request.Headers.IfMatch);
                var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(package.Bytes) };
                OnDownloadResponse?.Invoke(response);
                AfterDownload?.Invoke();
                if (DownloadBarrier is { } barrier)
                    await barrier.WaitAsync(cancellationToken);
                return response;
            }
            var plugins = Packages.OrderByDescending(p => p.Value.Manifest.Version).ThenByDescending(p => p.Key)
                .Select(p => new { source = new { repositoryId = p.Value.RepositoryId }, releaseId = p.Key, prerelease = Prereleases.Contains(p.Key), manifest = p.Value.Manifest });
            var body = segments[^1] switch
            {
                "Plugins" => JsonConvert.SerializeObject(plugins.Where(p => !p.prerelease).GroupBy(p => p.source.repositoryId).Select(g => g.First())),
                _ => JsonConvert.SerializeObject(plugins.Single(p => p.releaseId == long.Parse(segments[^1])))
            };
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        }
    }

    const string PluginCode = """
        using UmamusumeResponseAnalyzer.Plugin;
        public sealed class TestPlugin : IPlugin
        {
            public void Initialize(IPluginContext context) { }
        }
        """;
}
