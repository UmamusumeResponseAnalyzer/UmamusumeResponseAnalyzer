using System.ComponentModel;
using System.Net;
using System.Net.Http.Json;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Microsoft.Win32;
using UmamusumeResponseAnalyzer.TerminalGui;
using Xunit;

namespace UmamusumeResponseAnalyzer.Tests;

[Collection("PluginReload")]
public sealed class HachimiEdgeTests(PluginRuntimeFixture fixture)
{
    [Theory]
    [InlineData("umamusume.exe", "umamusume.exe.local/UnityPlayer.dll", null)]
    [InlineData("umamusume.exe", "umamusume.exe.local/UnityPlayer.dll", 1)]
    [InlineData("komoeumamusume.exe", "winhttp.dll", null)]
    [InlineData("UmamusumePrettyDerby.exe", "cri_mana_vpx.dll", null)]
    [InlineData("UmamusumePrettyDerby_Jpn.exe", "cri_mana_vpx.dll", null)]
    public async Task InstallUsesFixedMappingWithoutBackupsAndPreservesUserConfiguration(string executable, string loader, int? registryBefore)
    {
        _ = fixture;
        using var setup = new Installation(executable, registryBefore);
        setup.Write(loader, "old loader");
        setup.Write("other-loader.dll", "unrelated");
        setup.Write(HachimiEdgeGame.ConfigPath, """{"load_libraries":["user.dll"],"user_setting":true}""");
        setup.Write(HachimiEdgeGame.ForwardConfigPath, """{"notifier_timeout_ms":350,"user_setting":"keep"}""");

        await setup.Prepare();
        Assert.Equal(setup.Game.Binaries.Length, setup.Downloads);
        HachimiEdgeInstallation.Execute(setup.RequestPath, registry: setup.Registry);
        foreach (var binary in setup.Game.Binaries)
            Assert.Equal(setup.Image, File.ReadAllBytes(Path.Combine(setup.Game.Directory, binary.RelativePath)));
        var config = JsonNode.Parse(File.ReadAllText(Path.Combine(setup.Game.Directory, HachimiEdgeGame.ConfigPath)))!;
        Assert.True(config["user_setting"]!.GetValue<bool>());
        Assert.Equal(2, config["load_libraries"]!.AsArray().Count);
        var forward = JsonNode.Parse(File.ReadAllText(Path.Combine(setup.Game.Directory, HachimiEdgeGame.ForwardConfigPath)))!;
        Assert.Equal(350, forward["notifier_timeout_ms"]!.GetValue<int>());
        Assert.Equal("http://127.0.0.1:5001", forward["notifier_host"]!.GetValue<string>());
        Assert.Equal("unrelated", File.ReadAllText(Path.Combine(setup.Game.Directory, "other-loader.dll")));
        if (setup.Game.Platform == HachimiEdgePlatform.Dmm) Assert.Equal(1, HachimiEdgeInstallation.ReadDllRedirection(setup.Registry));
        Assert.False(Directory.Exists(Path.Combine(setup.Game.Directory, ".ura-hachimi-edge")));
        var expectedFiles = setup.Game.TargetPaths.Append(Path.GetFileName(setup.Game.Executable)).Append("other-loader.dll")
            .Select(p => p.Replace('/', Path.DirectorySeparatorChar)).Distinct().Order().ToArray();
        Assert.Equal(expectedFiles, Directory.GetFiles(setup.Game.Directory, "*", SearchOption.AllDirectories)
            .Select(p => Path.GetRelativePath(setup.Game.Directory, p)).Order().ToArray());
        await setup.Prepare();
        HachimiEdgeInstallation.Execute(setup.RequestPath, registry: setup.Registry);
        config = JsonNode.Parse(File.ReadAllText(Path.Combine(setup.Game.Directory, HachimiEdgeGame.ConfigPath)))!;
        Assert.Equal(2, config["load_libraries"]!.AsArray().Count);
    }

    [Theory]
    [InlineData("umamusume.exe", false)]
    [InlineData("umamusume.exe", true)]
    [InlineData("komoeumamusume.exe", false)]
    [InlineData("komoeumamusume.exe", true)]
    public async Task FileLinksAreReplacedWithoutChangingTheirTargets(string executable, bool interrupt)
    {
        using var setup = new Installation(executable);
        var external = Path.Combine(setup.Staging, "development.dll");
        File.WriteAllText(external, "development build");
        var loader = Path.Combine(setup.Game.Directory, setup.Game.Binaries[0].RelativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(loader)!);
        try { File.CreateSymbolicLink(loader, external); }
        catch (IOException ex) when ((ex.HResult & 0xffff) == 1314)
        {
            Assert.Skip("File symlink tests require Windows Developer Mode or an elevated test process.");
        }
        File.SetAttributes(external, FileAttributes.ReadOnly);
        var attributes = File.GetAttributes(external);
        var modified = File.GetLastWriteTimeUtc(external);
        try
        {
            await setup.Prepare();
            if (interrupt)
                Assert.Throws<IOException>(() => HachimiEdgeInstallation.Execute(setup.RequestPath,
                    new InlineProgress(_ => throw new IOException("interrupted")), setup.Registry));
            else HachimiEdgeInstallation.Execute(setup.RequestPath, registry: setup.Registry);
            Assert.Null(new FileInfo(loader).LinkTarget);
            Assert.Equal(setup.Image, File.ReadAllBytes(loader));
            Assert.Equal("development build", File.ReadAllText(external));
            Assert.Equal(attributes, File.GetAttributes(external));
            Assert.Equal(modified, File.GetLastWriteTimeUtc(external));
        }
        finally { File.SetAttributes(external, FileAttributes.Normal); }
    }

    [Fact]
    public async Task DirectoryAndStagingLinksAreRejectedBeforeApplying()
    {
        using var setup = new Installation("komoeumamusume.exe");
        var external = Path.Combine(setup.Staging, "external");
        Directory.CreateDirectory(external);
        var directoryLink = Path.Combine(setup.Game.Directory, "hachimi");
        try { Directory.CreateSymbolicLink(directoryLink, external); }
        catch (IOException ex) when ((ex.HResult & 0xffff) == 1314)
        {
            Assert.Skip("File symlink tests require Windows Developer Mode or an elevated test process.");
        }
        Assert.Contains(directoryLink, Assert.Throws<IOException>(() => HachimiEdgeInstallation.ValidateTargetPaths(setup.Game)).Message);
        Directory.Delete(directoryLink);
        await setup.Prepare();
        var component = Path.Combine(setup.Staging, "edge.bin");
        var source = Path.Combine(external, "edge.bin");
        File.Move(component, source);
        File.CreateSymbolicLink(component, source);
        Assert.Contains(component, Assert.Throws<IOException>(() => HachimiEdgeInstallation.Execute(setup.RequestPath)).Message);
        Assert.DoesNotContain(setup.Game.TargetPaths, p => File.Exists(Path.Combine(setup.Game.Directory, p)));
    }

    [Fact]
    public async Task LockedTargetKeepsEarlierReplacementsAndAllowsRetry()
    {
        using var setup = new Installation("komoeumamusume.exe");
        setup.Write("winhttp.dll", "old loader");
        setup.Write(HachimiEdgeGame.ForwarderPath, "old plugin");
        var loader = Path.Combine(setup.Game.Directory, "winhttp.dll");
        var plugin = Path.Combine(setup.Game.Directory, HachimiEdgeGame.ForwarderPath);
        File.SetAttributes(loader, FileAttributes.ReadOnly);
        await setup.Prepare();
        using (var held = new FileStream(plugin, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            Assert.Throws<UnauthorizedAccessException>(() => HachimiEdgeInstallation.Execute(setup.RequestPath));
            Assert.Equal(setup.Image, File.ReadAllBytes(loader));
            Assert.Equal("old plugin", File.ReadAllText(plugin));
            Assert.Empty(Directory.GetFiles(setup.Game.Directory, "*.tmp", SearchOption.AllDirectories));
        }
        await setup.Prepare();
        HachimiEdgeInstallation.Execute(setup.RequestPath);
        Assert.Equal(setup.Image, File.ReadAllBytes(plugin));
    }

    [Fact]
    public async Task ElevationCancellationLeavesTargetsUntouchedAndChildIsAwaited()
    {
        using var setup = new Installation("komoeumamusume.exe");
        await setup.Prepare();
        await Assert.ThrowsAsync<OperationCanceledException>(() => HachimiEdgeInstallation.ApplyAsync(setup.RequestPath, true,
            runElevated: _ => throw new Win32Exception(1223)));
        Assert.DoesNotContain(setup.Game.TargetPaths, p => File.Exists(Path.Combine(setup.Game.Directory, p)));
        var child = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var applying = HachimiEdgeInstallation.ApplyAsync(setup.RequestPath, true, runElevated: start =>
        {
            Assert.Equal("runas", start.Verb);
            Assert.Contains("--confirmed", start.ArgumentList);
            Assert.Equal(System.Globalization.CultureInfo.CurrentUICulture.Name,
                start.ArgumentList[start.ArgumentList.IndexOf("--culture") + 1]);
            return child.Task;
        });
        Assert.False(applying.IsCompleted);
        child.SetResult(HachimiEdgeInstallation.RunApplyCommand(setup.RequestPath));
        await applying;
        Assert.Equal(setup.Image, File.ReadAllBytes(Path.Combine(setup.Game.Directory, "winhttp.dll")));
        Assert.False(File.Exists(Path.Combine(setup.Staging, "error.json")));
    }

    [Fact]
    public async Task ElevatedFailureReportsTheErrorAndSuccessfulRetryUsesTheExitCode()
    {
        using var setup = new Installation("komoeumamusume.exe");
        await setup.Prepare();
        File.WriteAllBytes(Path.Combine(setup.Staging, "edge.bin"), [1, 2, 3]);
        var error = await Assert.ThrowsAsync<IOException>(() => HachimiEdgeInstallation.ApplyAsync(setup.RequestPath, true,
            runElevated: _ => Task.FromResult(HachimiEdgeInstallation.RunApplyCommand(setup.RequestPath))));
        Assert.Contains(HachimiEdgeInstaller.Text("ComponentMismatch", "edge"), error.Message);
        Assert.DoesNotContain(setup.Game.TargetPaths, p => File.Exists(Path.Combine(setup.Game.Directory, p)));
        await setup.Prepare();
        await HachimiEdgeInstallation.ApplyAsync(setup.RequestPath, true,
            runElevated: _ => Task.FromResult(HachimiEdgeInstallation.RunApplyCommand(setup.RequestPath)));
        Assert.Equal(setup.Image, File.ReadAllBytes(Path.Combine(setup.Game.Directory, "winhttp.dll")));
    }

    [Fact]
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    public void DllRedirectionEnablementIsIdempotentAndRequiresDword()
    {
        using var setup = new Installation("umamusume.exe");
        Assert.True(HachimiEdgeInstallation.EnableDllRedirection(setup.Registry));
        Assert.Equal(1, HachimiEdgeInstallation.ReadDllRedirection(setup.Registry));
        Assert.False(HachimiEdgeInstallation.EnableDllRedirection(setup.Registry));
        setup.Registry.SetValue("DevOverrideEnable", "1", RegistryValueKind.String);
        Assert.Throws<InvalidDataException>(() => HachimiEdgeInstallation.EnableDllRedirection(setup.Registry));
        Assert.Equal("1", setup.Registry.GetValue("DevOverrideEnable"));
    }

    [Theory]
    [InlineData("{broken", "{}")]
    [InlineData("{\"load_libraries\":null}", "{}")]
    [InlineData("{}", "{\"notifier_timeout_ms\":\"100\"}")]
    public async Task InvalidExistingConfigurationIsReportedBeforeWriting(string config, string forward)
    {
        using var setup = new Installation("komoeumamusume.exe");
        setup.Write(HachimiEdgeGame.ConfigPath, config);
        setup.Write(HachimiEdgeGame.ForwardConfigPath, forward);
        await Assert.ThrowsAsync<InvalidDataException>(() => setup.Prepare());
        Assert.Equal(config, File.ReadAllText(Path.Combine(setup.Game.Directory, HachimiEdgeGame.ConfigPath)));
        Assert.DoesNotContain(setup.Game.Binaries, b => File.Exists(Path.Combine(setup.Game.Directory, b.RelativePath)));
    }

    [Fact]
    public async Task InvalidComponentsCannotBeApplied()
    {
        using var setup = new Installation("komoeumamusume.exe");
        setup.Snapshot = setup.Snapshot with { Client = "dmm" };
        Assert.Equal(HachimiEdgeInstaller.Text("ClientMismatch", "taiwan", "dmm"),
            (await Assert.ThrowsAsync<InvalidDataException>(() => setup.Prepare())).Message);
        Assert.Equal(0, setup.Downloads);
        setup.Snapshot = setup.Snapshot with { Client = "taiwan" };
        await setup.Prepare();
        setup.Write("winhttp.dll", "old loader");
        File.WriteAllBytes(Path.Combine(setup.Staging, "edge.bin"), [1, 2, 3]);
        Assert.Throws<InvalidDataException>(() => HachimiEdgeInstallation.Execute(setup.RequestPath));
        Assert.Equal("old loader", File.ReadAllText(Path.Combine(setup.Game.Directory, "winhttp.dll")));
    }

    [Theory]
    [InlineData("0.0.0.0", "http://127.0.0.1:4693")]
    [InlineData("::", "http://[::1]:4693")]
    [InlineData("192.168.1.5", "http://192.168.1.5:4693")]
    public void ForwardingDefaultUsesCurrentListener(string listener, string expected) =>
        Assert.Equal(expected, HachimiEdgeInstaller.DefaultNotifier(listener, 4693));

    sealed class InlineProgress(Action<DownloadProgress> action) : IProgress<DownloadProgress>
    {
        public void Report(DownloadProgress value) => action(value);
    }

    sealed class Installation : IDisposable
    {
        readonly HttpClient originalClient = ResourceUpdater.HttpClient;
        readonly string registryName = @"Software\URA-HachimiEdge-Tests\" + Guid.NewGuid().ToString("N");
        readonly string root = Path.Combine(Path.GetTempPath(), "ura-hachimi-edge-" + Guid.NewGuid().ToString("N"));
        public HachimiEdgeGame Game { get; }
        public string Staging { get; }
        public string RequestPath => Path.Combine(Staging, "request.json");
        public RegistryKey Registry { get; }
        public byte[] Image { get; }
        public HachimiEdgeSnapshot Snapshot { get; set; }
        public int Downloads { get; private set; }

        public Installation(string executable, int? registryBefore = null)
        {
            Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows installer and registry tests");
            if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
            Staging = Path.Combine(root, "ura-hachimi-edge-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Staging);
            var directory = Path.Combine(root, "游戏 directory");
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, executable), "game executable");
            Game = HachimiEdgeGame.FromExecutable(Path.Combine(directory, executable));
            Registry = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(registryName);
            if (registryBefore is { } value) Registry.SetValue("DevOverrideEnable", value);
            var metadata = new MetadataBuilder();
            metadata.AddModule(0, metadata.GetOrAddString("native fixture"), default, default, default);
            var pe = new ManagedPEBuilder(new PEHeaderBuilder(machine: Machine.Amd64), new MetadataRootBuilder(metadata), new BlobBuilder());
            var image = new BlobBuilder();
            pe.Serialize(image);
            Image = image.ToArray();
            var sha = Convert.ToHexStringLower(SHA256.HashData(Image));
            Snapshot = new HachimiEdgeSnapshot(Game.Client, [.. new[] { "edge", "httpforward", "cellar", "funnyhoney" }
                .Select(name => new HachimiEdgeComponent(name, "v1", Image.Length, sha, "HachimiEdge/components/" + sha))]);
            ResourceUpdater.HttpClient = new(new Handler(request =>
            {
                Assert.StartsWith(HachimiEdgeInstaller.ApiRoot, request.RequestUri!.AbsoluteUri);
                if (request.RequestUri.AbsolutePath.EndsWith("/HachimiEdge"))
                {
                    Assert.Equal("?client=" + Game.Client, request.RequestUri.Query);
                    return new(HttpStatusCode.OK) { Content = JsonContent.Create(Snapshot) };
                }
                Downloads++;
                return new(HttpStatusCode.OK) { Content = new ByteArrayContent(Image) };
            }));
        }

        public Task<HachimiEdgeRequest> Prepare() => HachimiEdgeInstaller.PrepareAsync(Game, "http://127.0.0.1:5001", Staging, null,
            TestContext.Current.CancellationToken, Registry);

        public void Write(string relative, string value)
        {
            var path = Path.Combine(Game.Directory, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, value);
        }

        public void Dispose()
        {
            ResourceUpdater.HttpClient.Dispose();
            ResourceUpdater.HttpClient = originalClient;
            if (OperatingSystem.IsWindows())
            {
                Registry.Dispose();
                Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(registryName);
            }
            HachimiEdgeInstallation.DeleteOwnedDirectory(Path.GetTempPath(), Path.GetFileName(root));
        }
    }

    sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond(request));
    }
}
