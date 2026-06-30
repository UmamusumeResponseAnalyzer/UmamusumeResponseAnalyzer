using System.Reflection;
using System.Runtime.CompilerServices;
using Spectre.Console;
using UmamusumeResponseAnalyzer;
using UmamusumeResponseAnalyzer.Plugin;
using Xunit;

namespace UmamusumeResponseAnalyzer.Tests
{
    // 这个 collection 改 CWD（进程级）并驱动 PluginManager 全局静态状态，必须与所有其它测试串行隔离
    [CollectionDefinition("PluginReload", DisableParallelization = true)]
    public class PluginReloadCollection { }

    /// <summary>
    /// 热重载端到端集成测试：用 Roslyn 现场编译插件，经真实 analyzer 分发路径验证两种重载场景——
    /// ① 独立插件 v1→v2：旧 collectible ALC 被 GC（零泄漏）、重载后命中 v2 新代码；
    /// ② 共享上下文组（<c>[SharedContextWith]</c>，对应 EventLoggerPlugin + 依赖者那种结构）：
    ///    整组进一个 collectible ALC，重载任一成员会整组卸载（共享 ALC 被 GC）并重载。
    /// 全部断言基于真实加载/分发路径。
    /// PluginManager 是静态单例，故所有验证放进一次 Init() 里完成（避免跨测试方法的静态状态串扰）。
    /// </summary>
    [Collection("PluginReload")]
    public sealed class HotReloadTests : IDisposable
    {
        readonly string _tempDir;
        readonly string _logPath;
        readonly string _initLog;
        readonly string _originalCwd;

        public HotReloadTests()
        {
            SeedConfig(); // 触碰 PluginManager/LoadIntoContext 会读 Config.Repository.Targets，先注入一个 YamlConfig

            _originalCwd = Directory.GetCurrentDirectory();
            _tempDir = Path.Combine(Path.GetTempPath(), "ura-hotreload-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(_tempDir, "Plugins")); // PluginManager 从相对目录 Plugins/ 加载
            _logPath = Path.Combine(_tempDir, "analyze-log.txt");         // 插件跨 ALC 写这里，测试读它观测
            _initLog = Path.Combine(_tempDir, "init-log.txt");            // 插件 Initialize() 写这里；Server 未启动时重载不应写入(High#2 门控)
            Directory.SetCurrentDirectory(_tempDir);
        }

        public void Dispose()
        {
            Directory.SetCurrentDirectory(_originalCwd);
            try { Directory.Delete(_tempDir, recursive: true); } catch { /* 进程仍持有内存中的程序集，文件残留无妨 */ }
        }

        [Fact]
        public void HotReload_Works_ForStandalonePluginAndSharedContextGroup()
        {
            // 加载（独立插件 v1 + 共享组 Anchor/Member）→ analyzer dispatch → 重载，全部在不内联的辅助方法里完成，
            // 它返回后持有过旧插件/MethodInfo 的栈帧消失，GC 才能如实反映卸载结果。
            // 同时捕获 Spectre 控制台输出，验证生产侧的卸载自检不再打误报警告。
            var recording = new StringWriter();
            var originalConsole = AnsiConsole.Console;
            AnsiConsole.Console = AnsiConsole.Create(new AnsiConsoleSettings { Out = new AnsiConsoleOutput(recording) });
            WeakReference weakStandalone, weakGroup;
            try
            {
                (weakStandalone, weakGroup) = LoadDispatchThenReload();
            }
            finally
            {
                AnsiConsole.Console = originalConsole;
            }

            var consoleOutput = recording.ToString();
            Assert.Contains("已重载", consoleOutput); // 重载确实发生
            // 回归断言：正常卸载不应打印假阳性的"仍存活"警告。
            // 生产侧已移除卸载处的同步 GC 自检——在 reload 调用栈内做该检查会被保守栈扫描误报；
            // 真正的卸载实证由本测试下方的 WeakReference 检查（调用栈展开后）完成。
            Assert.DoesNotContain("卸载后仍存活", consoleOutput);

            // 核心断言①：两个旧 ALC（独立插件的 + 共享组的）都被回收 —— 零引用泄漏、真卸载
            for (var i = 0; (weakStandalone.IsAlive || weakGroup.IsAlive) && i < 10; i++)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
            }
            Assert.False(weakStandalone.IsAlive, "独立插件旧 ALC 未被回收 —— 存在引用泄漏");
            Assert.False(weakGroup.IsAlive, "共享组旧 ALC 未被回收 —— 存在引用泄漏（整组卸载没卸干净）");

            // 核心断言②：三个插件都重新注册
            foreach (var name in (string[])["Standalone", "Anchor", "Member"])
                Assert.Contains(PluginManager.LoadedPlugins, p => p.Name == name);

            // 核心断言③：重载后再跑 analyzer 分发，独立插件命中 v2 新代码、共享组两成员都重新分发
            File.Delete(_logPath); // 只保留重载后的记录
            Dispatch();
            var log = File.ReadAllText(_logPath);
            Assert.Contains("Standalone-v2:", log); // 独立插件换上了新代码
            Assert.Contains("Anchor:", log);        // 组成员（锚点）重载后仍分发
            Assert.Contains("Member:", log);        // 组成员（依赖者）重载后仍分发

            // 核心断言④：模拟插件仓库 install 一个【新成员】到一个【已加载】的共享组（PluginManager.ReloadPlugins）。
            // 回归点：必须整组重载进【单个】共享 ALC；绝不能因组 key 变化把锚点 Anchor 加载进新旧两个 ALC（共享库单例会失效）。
            var pluginsDir = Path.Combine(_tempDir, "Plugins");
            PluginCompiler.Compile(PluginSource("Member2", "Member2", sharedWith: "Anchor"), "Member2", Path.Combine(pluginsDir, "Member2.dll"));
            var needRestart = PluginManager.ReloadPlugins("Member2");

            Assert.Empty(needRestart); // 全部生效，无需重启
            foreach (var n in (string[])["Anchor", "Member", "Member2"])
                Assert.Contains(PluginManager.LoadedPlugins, p => p.Name == n);
            // 含 Anchor 的 collectible 上下文有且仅一个（双加载会出现两个）
            var anchorContexts = PluginManager.Contexts.Keys.Where(k => k.Split('&').Contains("Anchor")).ToList();
            Assert.Single(anchorContexts);
            // 新成员并入了同一个组（同一 ALC），而非新建一个把 Anchor 重复加载
            var groupMembers = anchorContexts[0].Split('&');
            Assert.Contains("Member", groupMembers);
            Assert.Contains("Member2", groupMembers);

            // 再跑 analyzer 分发：新老成员都分发，证明同组共享且新成员生效
            File.Delete(_logPath);
            Dispatch();
            var log2 = File.ReadAllText(_logPath);
            Assert.Contains("Anchor:", log2);
            Assert.Contains("Member:", log2);
            Assert.Contains("Member2:", log2);

            // 回归:之前某个 [SharedContextWith] 插件因缺少锚点加载失败后，会在 ContextGroups 里留下无 ALC 的失败组。
            // 之后热重载一个无关插件时，不能顺手尝试加载这个失败组，否则会绕过 LoadGroup 的缺失锚点处理并崩溃。
            const string staleMember = "MissingSharedMember";
            const string staleAnchor = "MissingSharedAnchor";
            var staleMeta = new PluginManager.PluginMetadata(
                $"X:/nonexistent/{staleMember}.zip|{staleMember}.dll", staleMember,
                loadInHost: false, shared: [staleAnchor], isFromZip: true);
            PluginManager.Metadatas[staleMember] = staleMeta;
            var staleGroup = new HashSet<string> { staleMember, staleAnchor };
            var staleKey = string.Join("&", staleGroup);
            PluginManager.ContextGroups.Add(staleGroup);
            PluginManager.LoadGroup(staleGroup);
            Assert.Contains(staleMeta.FilePath, PluginManager.FailedPlugins);

            PluginCompiler.Compile(PluginSource("Other", "Other"), "Other", Path.Combine(pluginsDir, "Other.dll"));
            var ex = Record.Exception(() => PluginManager.ReloadPlugins("Other"));

            Assert.Null(ex);
            Assert.Contains(PluginManager.LoadedPlugins, p => p.Name == "Other");
            Assert.False(PluginManager.Contexts.ContainsKey(staleKey), "缺失锚点的失败组不应被无关热重载建出幽灵 ALC");

            // 回归:缺锚点失败后,若后来补装锚点,重载原成员应直接重建该失败组,而不是尝试卸载一个不存在的 ALC。
            const string waitingMember = "WaitingMember";
            const string waitingAnchor = "WaitingAnchor";
            PluginCompiler.Compile(PluginSource(waitingMember, waitingMember, sharedWith: waitingAnchor), waitingMember, Path.Combine(pluginsDir, $"{waitingMember}.dll"));
            var missingAnchorResult = PluginManager.ReloadPlugins(waitingMember);
            Assert.Contains(waitingMember, missingAnchorResult);
            Assert.DoesNotContain(PluginManager.LoadedPlugins, p => p.Name == waitingMember);

            PluginCompiler.Compile(PluginSource(waitingAnchor, waitingAnchor), waitingAnchor, Path.Combine(pluginsDir, $"{waitingAnchor}.dll"));
            var fixedAnchorResult = PluginManager.ReloadPlugins(waitingMember);
            Assert.Empty(fixedAnchorResult);
            Assert.Contains(PluginManager.LoadedPlugins, p => p.Name == waitingMember);
            Assert.Contains(PluginManager.LoadedPlugins, p => p.Name == waitingAnchor);

            // 回归:宿主内部身份是程序集名(InternalName),不能用 IPlugin.Name(显示名)卸载。
            PluginCompiler.Compile(PluginSource("InternalNamePlugin", "internal-v1", displayName: "显示名"), "InternalNamePlugin", Path.Combine(pluginsDir, "InternalNamePlugin.dll"));
            Assert.Empty(PluginManager.ReloadPlugins("InternalNamePlugin"));
            PluginCompiler.Compile(PluginSource("InternalNamePlugin", "internal-v2", displayName: "显示名"), "InternalNamePlugin", Path.Combine(pluginsDir, "InternalNamePlugin.dll"));
            Assert.Empty(PluginManager.ReloadPlugins("InternalNamePlugin"));
            File.Delete(_logPath);
            Dispatch();
            var internalNameLog = File.ReadAllText(_logPath);
            Assert.DoesNotContain("internal-v1:", internalNameLog);
            Assert.Contains("internal-v2:", internalNameLog);

            // 回归:旧共享组拆开后,被整组卸载的其它旧成员也必须按新拓扑重载回来。
            PluginCompiler.Compile(PluginSource("TopologyAnchor", "topology-anchor"), "TopologyAnchor", Path.Combine(pluginsDir, "TopologyAnchor.dll"));
            PluginCompiler.Compile(PluginSource("TopologyMember", "topology-member-v1", sharedWith: "TopologyAnchor"), "TopologyMember", Path.Combine(pluginsDir, "TopologyMember.dll"));
            Assert.Empty(PluginManager.ReloadPlugins("TopologyMember"));
            PluginCompiler.Compile(PluginSource("TopologyMember", "topology-member-v2"), "TopologyMember", Path.Combine(pluginsDir, "TopologyMember.dll"));
            Assert.Empty(PluginManager.ReloadPlugins("TopologyMember"));
            Assert.Contains(PluginManager.LoadedPlugins, p => p.Name == "TopologyAnchor");
            Assert.Contains(PluginManager.LoadedPlugins, p => p.Name == "TopologyMember");

            // 回归:共享组中一个成员被删除时,应卸掉该成员并把仍存在的成员重建回来。
            PluginCompiler.Compile(PluginSource("DeleteAnchor", "delete-anchor"), "DeleteAnchor", Path.Combine(pluginsDir, "DeleteAnchor.dll"));
            PluginCompiler.Compile(PluginSource("DeleteMember", "delete-member", sharedWith: "DeleteAnchor"), "DeleteMember", Path.Combine(pluginsDir, "DeleteMember.dll"));
            Assert.Empty(PluginManager.ReloadPlugins("DeleteMember"));
            Assert.Contains(PluginManager.LoadedPlugins, p => p.Name == "DeleteAnchor");
            Assert.Contains(PluginManager.LoadedPlugins, p => p.Name == "DeleteMember");

            File.Delete(Path.Combine(pluginsDir, "DeleteMember.dll"));
            Assert.Empty(PluginManager.ReloadPlugins("DeleteMember"));
            Assert.Contains(PluginManager.LoadedPlugins, p => p.Name == "DeleteAnchor");
            Assert.DoesNotContain(PluginManager.LoadedPlugins, p => p.Name == "DeleteMember");
            File.Delete(_logPath);
            Dispatch();
            var deleteMemberLog = File.ReadAllText(_logPath);
            Assert.Contains("delete-anchor:", deleteMemberLog);
            Assert.DoesNotContain("delete-member:", deleteMemberLog);

            // 回归:新版本切到 LoadInHost 时不能先卸载旧 collectible 插件,否则“需重启”期间旧功能直接掉线。
            PluginCompiler.Compile(PluginSource("HostSwitch", "host-switch-v1"), "HostSwitch", Path.Combine(pluginsDir, "HostSwitch.dll"));
            Assert.Empty(PluginManager.ReloadPlugins("HostSwitch"));
            PluginCompiler.Compile(PluginSource("HostSwitch", "host-switch-v2", loadInHost: true), "HostSwitch", Path.Combine(pluginsDir, "HostSwitch.dll"));
            var hostSwitchResult = PluginManager.ReloadPlugins("HostSwitch");
            Assert.Contains("HostSwitch", hostSwitchResult);
            Assert.Contains(PluginManager.LoadedPlugins, p => p.Name == "HostSwitch");
            File.Delete(_logPath);
            Dispatch();
            var hostSwitchLog = File.ReadAllText(_logPath);
            Assert.Contains("host-switch-v1:", hostSwitchLog);
            Assert.DoesNotContain("host-switch-v2:", hostSwitchLog);

            // 回归(High#2):上面所有重载/装新成员都发生在 server 未启动时(测试没有 Server.Start)。
            // 热重载加载新组时必须门控跳过 Initialize——否则 Program.Main 启动时还会对全部 LoadedPlugins
            // 再统一 Initialize 一遍,造成新插件双初始化。这里断言整个过程从未触发过 Initialize。
            Assert.False(Server.IsRunning, "前置条件:测试中 server 未启动");
            Assert.False(File.Exists(_initLog),
                "server 未启动时重载触发了 Initialize —— 会与 Program.Main 的启动初始化重复,导致双初始化");
        }

        /// <summary>
        /// 编译并加载 3 个插件 → dispatch → 把独立插件升级到 v2 并重载、把共享组重载，返回两个旧 ALC 的弱引用。
        /// NoInlining：让本帧产生的所有指向旧 ALC 的临时引用随返回而释放（测 collectible ALC 卸载的标准手法）。
        /// </summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        (WeakReference Standalone, WeakReference Group) LoadDispatchThenReload()
        {
            var pluginsDir = Path.Combine(_tempDir, "Plugins");
            // 独立插件
            PluginCompiler.Compile(PluginSource("Standalone", "Standalone-v1"), "Standalone", Path.Combine(pluginsDir, "Standalone.dll"));
            // 共享上下文组：Member 用 [SharedContextWith("Anchor")] 与 Anchor 同进一个 collectible ALC
            PluginCompiler.Compile(PluginSource("Anchor", "Anchor"), "Anchor", Path.Combine(pluginsDir, "Anchor.dll"));
            PluginCompiler.Compile(PluginSource("Member", "Member", sharedWith: "Anchor"), "Member", Path.Combine(pluginsDir, "Member.dll"));

            PluginManager.Init();

            // 都加载了
            foreach (var name in (string[])["Standalone", "Anchor", "Member"])
                Assert.Contains(PluginManager.LoadedPlugins, p => p.Name == name);
            // Anchor 与 Member 在【同一个】context（共享 ALC），独立插件自成一组
            Assert.True(PluginManager.Contexts.Keys.Any(k => k.Contains("Anchor") && k.Contains("Member")),
                "Anchor 与 Member 应在同一个共享 collectible ALC");
            Assert.True(PluginManager.Contexts.ContainsKey("Standalone"));

            // analyzer 分发，三个 analyzer 都应被调用
            Dispatch();
            var first = File.ReadAllText(_logPath);
            Assert.Contains("Standalone-v1:", first);
            Assert.Contains("Anchor:", first);
            Assert.Contains("Member:", first);

            // 捕获两个旧 ALC 的弱引用（表达式临时，不落进局部强引用）
            var weakStandalone = new WeakReference(PluginManager.Contexts["Standalone"]);
            var weakGroup = new WeakReference(
                PluginManager.Contexts.First(kv => kv.Key.Contains("Anchor") && kv.Key.Contains("Member")).Value);

            // 独立插件升级到 v2 并重载
            PluginCompiler.Compile(PluginSource("Standalone", "Standalone-v2"), "Standalone", Path.Combine(pluginsDir, "Standalone.dll"));
            Assert.Empty(PluginManager.ReloadPlugins("Standalone"));

            // 重载共享组（重载任一成员都会整组卸载+重载）
            Assert.Empty(PluginManager.ReloadPlugins("Anchor"));

            return (weakStandalone, weakGroup);
        }

        /// <summary>经真实 typed/raw analyzer 分发路径调用已注册插件。</summary>
        static void Dispatch()
        {
            Server.DispatchResponse("/account/index", [0xC0]);
        }

        /// <summary>生成一个最小 IPlugin 插件源码；analyzer 把 <paramref name="marker"/> 写进日志文件；可选 SharedContextWith。</summary>
        string PluginSource(
            string pluginName,
            string marker,
            string? sharedWith = null,
            string? displayName = null,
            bool loadInHost = false)
        {
            var attributes = new List<string>();
            if (sharedWith is not null)
                attributes.Add($"[assembly: SharedContextWith(\"{sharedWith}\")]");
            if (loadInHost)
                attributes.Add("[assembly: LoadInHostContext]");
            var assemblyAttributes = string.Join("\n", attributes);
            var pluginDisplayName = displayName ?? pluginName;
            return $$"""
                using System.IO;
                using System.Threading.Tasks;
                using Gallop.Endpoints;
                using Spectre.Console;
                using UmamusumeResponseAnalyzer.Plugin;

                {{assemblyAttributes}}
                namespace {{pluginName}}Ns
                {
                    public class {{pluginName}}Plugin : IPlugin
                    {
                        public string Name => "{{pluginDisplayName}}";
                        public string Author => "test";
                        public string[] Targets => System.Array.Empty<string>();
                        public Task UpdatePlugin(ProgressContext ctx) => Task.CompletedTask;

                        // 记录 Initialize 被调用——测 High#2 门控:Server 未启动时,重载不应触发 Initialize。
                        public void Initialize(IPluginContext context) => File.AppendAllText(@"{{_initLog}}", "{{pluginName}}\n");

                        [RawResponseAnalyzer<GameApi.Account.Index>]
                        public void Analyze(byte[] payload)
                        {
                            File.AppendAllText(@"{{_logPath}}", "{{marker}}:" + payload.Length + "\n");
                        }
                    }
                }
                """;
        }

        /// <summary>反射注入一个 YamlConfig 到 private static Config.Current（避开会写盘的 Config.Initialize）。</summary>
        static void SeedConfig()
        {
            var current = typeof(Config).GetProperty("Current", BindingFlags.NonPublic | BindingFlags.Static)!;
            if (current.GetValue(null) is null)
                current.SetValue(null, new YamlConfig
                {
                    Core = new(),
                    Repository = new(),
                    Plugin = new(),
                    Updater = new(),
                    Language = new(),
                    Misc = new(),
                });
        }
    }
}
