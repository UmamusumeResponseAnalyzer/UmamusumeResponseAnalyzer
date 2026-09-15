using System.Reflection;
using System.Runtime.ExceptionServices;
using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using Terminal.Gui.Input;
using UmamusumeResponseAnalyzer.Plugin;
using UmamusumeResponseAnalyzer.TerminalGui;
using Xunit;
using static UmamusumeResponseAnalyzer.Localization.LaunchMenu;

namespace UmamusumeResponseAnalyzer.Tests;

public sealed class StartupWorkflowTests
{
    [Fact]
    public Task InstallNewPluginFromStartupWorkspace()
        => RunScenarioAsync(nameof(InstallNewPluginFromStartupWorkspace), s => InstallAsync(s, existing: false, shared: false));

    [Fact]
    public Task ReinstallLoadedPluginFromStartupWorkspace()
        => RunScenarioAsync(nameof(ReinstallLoadedPluginFromStartupWorkspace), s => InstallAsync(s, existing: true, shared: false));

    [Fact]
    public Task InstallingSharedMemberReloadsExistingGroup()
        => RunScenarioAsync(nameof(InstallingSharedMemberReloadsExistingGroup), s => InstallAsync(s, existing: true, shared: true));

    [Fact]
    public Task StartLoadsDataInitializesPluginsAndThenStartsHttp()
        => RunScenarioAsync(nameof(StartLoadsDataInitializesPluginsAndThenStartsHttp), async session =>
        {
            foreach (var (path, json) in new Dictionary<string, string>
            {
                ["events_male.br"] = "[]", ["names.br"] = "[]", ["skill_data.br"] = "[]",
                ["skill_upgrade_speciality.br"] = "[]", ["wins_saddle.br"] = "[]",
                ["talent_skill_sets.br"] = "{}", ["factor_ids.br"] = "{}", ["succession_relation.br"] = "{}"
            })
            {
                using var file = File.Create(path);
                using var compressed = new BrotliStream(file, CompressionMode.Compress);
                using var writer = new StreamWriter(compressed);
                writer.Write(json);
            }
            PluginCompiler.CompilePackage(PluginSource("StartedPlugin"), "StartedPlugin", "Plugins/StartedPlugin.zip");
            var manifest = PluginPackageValidator.Validate("Plugins/StartedPlugin.zip", false).Manifest;
            ResourceUpdater.HttpClient = new HttpClient(new PluginInstallTests.PackageHandler([], manifest));
            await session.StartAsync();
            await session.Terminal.WaitForScreenAsync(I18N_Instruction);
            Assert.Contains("插件仓库", session.FirstFrame);
            Assert.Contains("正在准备启动", session.FirstFrame);
            Assert.DoesNotContain("运行环境", session.FirstFrame);
            Assert.Equal(DatabaseAvailability.Unavailable, Database.Availability);
            Assert.False(Server.IsRunning);
            Assert.DoesNotContain("initialized", File.ReadAllText("lifecycle.log"));
            var workspace = Workspace.Current;
            await session.Terminal.InjectAsync(Key.Enter);
            await session.Terminal.WaitForAsync(() => File.ReadAllText("lifecycle.log").Contains("started"));
            Assert.Equal(DatabaseAvailability.Ready, Database.Availability);
            Assert.True(Server.IsRunning);
            Assert.Same(workspace, Workspace.Current);
            await session.Terminal.WaitForScreenAsync("初始化完成");
            await session.CloseAsync();
            Assert.False(Server.IsRunning);
            Assert.Equal(1, File.ReadAllLines("lifecycle.log").Count(line => line.EndsWith("disposed")));
        });

    static async Task InstallAsync(StartupSession session, bool existing, bool shared)
    {
        var name = shared ? "Consumer" : "Installed";
        if (existing)
            PluginCompiler.CompilePackage(PluginSource(shared ? "Anchor" : name), shared ? "Anchor" : name,
                Path.Combine("Plugins", shared ? "Anchor.zip" : "Installed.zip"));
        PluginCompiler.CompilePackage(PluginSource(name), name, "download.zip",
            dependencies: shared ? ["Anchor"] : [], version: "2.0.0");
        var manifest = PluginPackageValidator.Validate("download.zip", false).Manifest;
        var releaseDownload = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new PluginInstallTests.PackageHandler(File.ReadAllBytes("download.zip"), manifest)
        {
            DownloadBarrier = releaseDownload.Task
        };
        ResourceUpdater.HttpClient = new HttpClient(handler);
        await session.StartAsync();
        var terminal = session.Terminal;
        await terminal.WaitForScreenAsync(I18N_Instruction);
        var host = TerminalUi.RequireHost();
        Assert.True(host.Ready.IsCompletedSuccessfully);
        var workspace = host.Bootstrap.Workspace;
        await terminal.InjectAsync(Key.CursorDown);
        await terminal.InjectAsync(Key.CursorDown);
        await terminal.InjectAsync(Key.Enter);
        await terminal.WaitForScreenAsync(PluginRepository.Text("SelectPlugins"));

        // 子对话框取消必须返回同一个启动菜单。
        await terminal.InjectAsync(Key.Esc);
        await terminal.WaitForScreenAsync(I18N_Instruction);
        await terminal.InjectAsync(Key.CursorDown);
        await terminal.InjectAsync(Key.CursorDown);
        await terminal.InjectAsync(Key.Enter);
        await terminal.WaitForScreenAsync(PluginRepository.Text("SelectPlugins"));
        await terminal.InjectAsync(Key.Space);
        await terminal.InjectAsync(Key.Tab);
        await terminal.InjectAsync(Key.Enter);
        await terminal.WaitForAsync(() => handler.Downloads.Count == 1);
        host.Bootstrap.Log("startup-test", "download-still-responsive");
        await host.FlushAsync().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(Server.IsRunning);
        Assert.DoesNotContain("initialized", File.Exists("lifecycle.log") ? File.ReadAllText("lifecycle.log") : "");
        releaseDownload.SetResult();
        await terminal.WaitForScreenAsync("插件已安装并生效");
        Assert.Equal(new Version(2, 0, 0), PluginManager.SnapshotPluginStatuses().Single(p => p.InternalName == name).Version);
        Assert.Equal(existing ? 1 : 0, File.ReadAllLines("lifecycle.log").Count(line => line.EndsWith("disposed")));
        Assert.Contains($"/plugin reload {name}", host.CompleteCommand("/plugin reload "));
        await terminal.InjectAsync(Key.Enter);
        await terminal.WaitForScreenAsync(I18N_Instruction);
        Assert.Same(workspace, Workspace.Current);

        // 缺少数据时停在同一个信息页，不提前 Initialize 或监听 HTTP。
        await terminal.InjectAsync(Key.Enter);
        await terminal.WaitForAsync(() => Environment.ExitCode == 1);
        await host.FlushAsync();
        var screen = await terminal.CaptureScreenAsync();
        Assert.Contains("数据文件不完整或损坏", screen);
        Assert.Contains(name, screen);
        Assert.Contains("2.0.0", screen);
        Assert.Same(workspace, Workspace.Current);
        Assert.False(Server.IsRunning);
        Assert.DoesNotContain("initialized", File.ReadAllText("lifecycle.log"));
        Assert.False(session.Run.IsCompleted);
        await session.CloseAsync();
        Assert.Equal(1, Environment.ExitCode);
    }

    [Fact]
    public Task StartupFailureKeepsInformationVisibleUntilExit()
        => RunScenarioAsync(nameof(StartupFailureKeepsInformationVisibleUntilExit), async session =>
        {
            Directory.Delete("Plugins");
            File.WriteAllText("Plugins", "not a directory");
            await session.StartAsync();
            await session.Terminal.WaitForAsync(() => Environment.ExitCode == 1);
            await session.Terminal.WaitForScreenAsync("Plugins");
            Assert.False(session.Run.IsCompleted);
            Assert.False(Server.IsRunning);
            await Assert.ThrowsAsync<IOException>(() => session.CloseAsync());
        });

    [Fact]
    public Task FirstSetupCompletesBeforePluginScanAndStartupMenu()
        => RunScenarioAsync(nameof(FirstSetupCompletesBeforePluginScanAndStartupMenu), async session =>
        {
            var config = Config.Deserialize(File.ReadAllText(Config.CONFIG_FILEPATH), Config.CONFIG_FILEPATH);
            config.Core.ShowFirstRunPrompt = true;
            File.WriteAllText(Config.CONFIG_FILEPATH, Config.Serialize(config));
            PluginCompiler.CompilePackage(PluginSource("FirstSetupPlugin"), "FirstSetupPlugin", "Plugins/FirstSetupPlugin.zip");
            await session.StartAsync();
            var terminal = session.Terminal;
            await terminal.WaitForScreenAsync("首次设置");
            Assert.Contains("插件仓库", session.FirstFrame);
            Assert.DoesNotContain("运行环境", session.FirstFrame);
            Assert.False(File.Exists("lifecycle.log"));
            Assert.True(TerminalUi.RequireHost().Ready.IsCompletedSuccessfully);
            await terminal.InjectAsync(Key.CursorDown);
            await terminal.InjectAsync(Key.Enter);
            await terminal.WaitForScreenAsync("请选择所使用");
            await terminal.InjectAsync(Key.Tab);
            await terminal.InjectAsync(Key.Enter);
            await terminal.WaitForScreenAsync("请选择事件数据语言");
            await terminal.InjectAsync(Key.Enter);
            await terminal.WaitForScreenAsync("请选择训练员性别");
            await terminal.InjectAsync(Key.Enter);
            await terminal.WaitForScreenAsync("首次设置完成");
            await terminal.InjectAsync(Key.Enter);
            await terminal.WaitForScreenAsync(I18N_Instruction);
            Assert.False(Config.Core.ShowFirstRunPrompt);
            Assert.Equal(["FirstSetupPlugin constructed"], File.ReadAllLines("lifecycle.log"));
            Assert.False(Server.IsRunning);
            await session.CloseAsync();
        });

    [Fact]
    public Task StartupMenuEscapeStopsTheHost()
        => RunScenarioAsync(nameof(StartupMenuEscapeStopsTheHost), async session =>
        {
            await session.StartAsync();
            await session.Terminal.WaitForScreenAsync(I18N_Instruction);
            await session.Terminal.InjectAsync(Key.Esc);
            await session.Run.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(Server.IsRunning);
        });

    [Fact]
    public Task CancellationDuringPluginScanClosesLoadedInstances()
        => RunScenarioAsync(nameof(CancellationDuringPluginScanClosesLoadedInstances),
            session => CancelDuringPluginScanAsync(session, Key.C.WithCtrl));

    [Fact]
    public Task PreparingMenuEscapeClosesHostDuringScan()
        => RunScenarioAsync(nameof(PreparingMenuEscapeClosesHostDuringScan),
            session => CancelDuringPluginScanAsync(session, Key.Esc));

    static async Task CancelDuringPluginScanAsync(StartupSession session, Key cancelKey)
    {
        PluginCompiler.CompilePackage("""
            using System;
            using System.IO;
            using System.Threading;
            using UmamusumeResponseAnalyzer.Plugin;
            public sealed class ScanPlugin : IPlugin
            {
                public ScanPlugin()
                {
                    File.WriteAllText("scan-entered", "entered");
                    if (!SpinWait.SpinUntil(() => File.Exists("scan-release"), TimeSpan.FromSeconds(10)))
                        throw new TimeoutException("scan-release");
                }
                public void Initialize(IPluginContext context) => throw new Exception("Must not initialize");
                public void Dispose() => File.WriteAllText("scan-disposed", "disposed");
            }
            """, "ScanPlugin", "Plugins/ScanPlugin.zip");
        await session.StartAsync();
        await session.Terminal.WaitForAsync(() => File.Exists("scan-entered"));
        Assert.Contains("插件仓库", session.FirstFrame);
        Assert.DoesNotContain("运行环境", session.FirstFrame);
        await session.Terminal.InjectAsync(Key.Enter);
        await session.Terminal.WaitForScreenAsync("正在准备启动");
        Assert.Equal(DatabaseAvailability.Unavailable, Database.Availability);
        Assert.False(Server.IsRunning);
        await session.Terminal.InjectAsync(cancelKey);
        File.WriteAllText("scan-release", "release");
        await session.Run.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(File.Exists("scan-disposed"));
        Assert.Empty(PluginManager.SnapshotLoadedPlugins());
        Assert.ThrowsAny<OperationCanceledException>(() => PluginManager.Init(new CancellationToken(true)));
        Assert.Empty(PluginManager.SnapshotLoadedPlugins());
    }

    [Fact]
    public Task UiCreationFailureEndsStartupWaitingForReady()
        => RunScenarioAsync(nameof(UiCreationFailureEndsStartupWaitingForReady), async session =>
        {
            session.Terminal.Application.SessionBegun += (_, _) =>
            {
                Assert.False(TerminalUi.RequireHost().Ready.IsCompleted);
                throw new InvalidOperationException("create-ui-failure");
            };
            var error = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            {
                await session.StartAsync();
                await session.Run.WaitAsync(TimeSpan.FromSeconds(5));
            });
            Assert.Equal("create-ui-failure", error.Message);
            Assert.Empty(PluginManager.SnapshotLoadedPlugins());
            Assert.Contains("stopped", Assert.Throws<InvalidOperationException>(() => TerminalUi.Log("test", "late")).Message);
        });

    [Fact]
    public Task StartupUpdateRequestEscapesOnlyAfterTerminalCleanup()
        => RunScenarioAsync(nameof(StartupUpdateRequestEscapesOnlyAfterTerminalCleanup), async session =>
        {
            Directory.CreateDirectory(".portable");
            foreach (var file in Directory.EnumerateFiles(AppContext.BaseDirectory)
                .Where(path => Path.GetExtension(path) is ".dll" or ".json"))
                File.Copy(file, Path.Combine(Path.GetTempPath(), Path.GetFileName(file)));
            File.Copy(Path.Combine(AppContext.BaseDirectory, "UmamusumeResponseAnalyzer.exe"),
                Path.Combine(Path.GetTempPath(), "latest-UmamusumeResponseAnalyzer.exe"));
            var error = await Assert.ThrowsAsync<UmamusumeResponseAnalyzer.PostShutdownProcessRequestedException>(
                async () =>
                {
                    await session.StartAsync();
                    await session.Run.WaitAsync(TimeSpan.FromSeconds(5));
                });
            Assert.EndsWith("latest-UmamusumeResponseAnalyzer.exe", error.StartInfo.FileName);
            Assert.Contains("stopped", Assert.Throws<InvalidOperationException>(() => TerminalUi.Log("test", "late")).Message);
        });

    static string PluginSource(string name) => $$"""
        using System.IO;
        using System.Threading.Tasks;
        using UmamusumeResponseAnalyzer.Plugin;
        public sealed class {{name}} : IPlugin
        {
            public {{name}}() => File.AppendAllText("lifecycle.log", "{{name}} constructed\n");
            public void Initialize(IPluginContext context)
            {
                if (UmamusumeResponseAnalyzer.Database.Availability != UmamusumeResponseAnalyzer.DatabaseAvailability.Ready)
                    throw new System.Exception("Initialize before data is ready");
                File.AppendAllText("lifecycle.log", "{{name}} initialized\n");
                context.Events.OnStarted(_ =>
                {
                    if (!UmamusumeResponseAnalyzer.UmamusumeResponseAnalyzer.Started)
                        throw new System.Exception("OnStarted before HTTP is ready");
                    File.AppendAllText("lifecycle.log", "{{name}} started\n");
                    return ValueTask.CompletedTask;
                });
            }
            public void Dispose() => File.AppendAllText("lifecycle.log", "{{name}} disposed\n");
        }
        """;

    static async Task RunScenarioAsync(string scenario, Func<StartupSession, Task> action)
    {
        if (!TerminalUiLifecycleChildProcess.IsChild(scenario))
        {
            Assert.Equal("ok", await TerminalUiLifecycleProcessTests.RunChildAsync(scenario, typeof(StartupWorkflowTests), scenario));
            return;
        }
        await using (var session = new StartupSession())
            await action(session);
        Environment.ExitCode = 0;
        TerminalUiLifecycleChildProcess.WriteResult("ok");
    }

    sealed class StartupSession : IAsyncDisposable
    {
        readonly string root = Path.Combine(Path.GetTempPath(), "ura-startup-" + Guid.NewGuid().ToString("N"));
        readonly string originalCwd = Directory.GetCurrentDirectory();
        readonly string? originalTemp = Environment.GetEnvironmentVariable("TEMP");
        readonly string? originalTmp = Environment.GetEnvironmentVariable("TMP");
        readonly HttpClient originalHttp = ResourceUpdater.HttpClient;

        internal TerminalGuiTestApp Terminal { get; }
        internal string? FirstFrame { get; private set; }
        internal Task Run { get; private set; } = Task.CompletedTask;

        internal StartupSession()
        {
            Directory.CreateDirectory(Path.Combine(root, "Plugins"));
            var temp = Directory.CreateDirectory(Path.Combine(root, "temp")).FullName;
            Environment.SetEnvironmentVariable("TEMP", temp);
            Environment.SetEnvironmentVariable("TMP", temp);
            Directory.SetCurrentDirectory(root);
            var config = new YamlConfig();
            config.Core.ShowFirstRunPrompt = false;
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            config.Core.ListenPort = ((IPEndPoint)listener.LocalEndpoint).Port;
            config.Language.Selected = LanguageConfig.Language.SimplifiedChinese;
            File.WriteAllText(Config.CONFIG_FILEPATH, Config.Serialize(config));
            Terminal = new TerminalGuiTestApp(120, 40);
            Terminal.Application.LayoutAndDrawComplete += (_, _) => FirstFrame ??= Terminal.Application.Driver!.ToString();
        }

        internal async Task StartAsync()
        {
            Run = await Terminal.StartAsync(() =>
            {
                var contextType = typeof(UmamusumeResponseAnalyzer).GetNestedType("SingleThreadSynchronizationContext", BindingFlags.NonPublic)!;
                var context = (SynchronizationContext)Activator.CreateInstance(contextType, nonPublic: true)!;
                var previous = SynchronizationContext.Current;
                SynchronizationContext.SetSynchronizationContext(context);
                contextType.GetMethod("Bind")!.Invoke(context, [Terminal.Application]);
                try
                {
                    var workflow = (Task)typeof(UmamusumeResponseAnalyzer)
                        .GetMethod("RunInteractiveAsync", BindingFlags.Static | BindingFlags.NonPublic)!
                        .Invoke(null, [Terminal.Application, context])!;
                    contextType.GetMethod("Run")!.Invoke(context, [workflow]);
                    return workflow;
                }
                catch (TargetInvocationException ex) when (ex.InnerException is not null)
                {
                    ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
                    throw;
                }
                finally
                {
                    SynchronizationContext.SetSynchronizationContext(previous);
                    ((IDisposable)context).Dispose();
                }
            });
        }

        internal async Task CloseAsync()
        {
            await Terminal.InjectAsync(Key.C.WithCtrl);
            await Run.WaitAsync(TimeSpan.FromSeconds(5));
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                if (!Run.IsCompleted)
                    await CloseAsync();
            }
            finally
            {
                Terminal.Dispose();
                if (!ReferenceEquals(ResourceUpdater.HttpClient, originalHttp))
                    ResourceUpdater.HttpClient.Dispose();
                ResourceUpdater.HttpClient = originalHttp;
                Directory.SetCurrentDirectory(originalCwd);
                Environment.SetEnvironmentVariable("TEMP", originalTemp);
                Environment.SetEnvironmentVariable("TMP", originalTmp);
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
