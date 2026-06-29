using Spectre.Console;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using UmamusumeResponseAnalyzer.LiveDisplay;
using WatsonWebserver.Core;

namespace UmamusumeResponseAnalyzer.Plugin
{
    public static class PluginManager
    {
        internal static Dictionary<string, PluginMetadata> Metadatas { get; } = [];
        internal static List<string> FailedPlugins { get; } = [];
        public static List<IPlugin> LoadedPlugins { get; } = [];
        internal static SortedDictionary<int, List<(IPlugin Self, MethodInfo Method)>> RequestAnalyzerMethods { get; } = [];
        internal static SortedDictionary<int, List<(IPlugin Self, MethodInfo Method)>> ResponseAnalyzerMethods { get; } = [];
        internal static List<HashSet<string>> ContextGroups { get; } = [];
        internal static Dictionary<string, PluginLoadContext> Contexts { get; } = [];
        internal static Dictionary<string, Assembly> AssemblyMap { get; } = [];
        internal static List<Assembly> Assemblies { get; } = [];
        private static HashSet<string> HostAssemblyNames { get; } = [];
        static Func<IPlugin, ILiveDisplayOutput>? liveDisplayFactory;

        // 每个插件注册的 HTTP 路由，卸载时凭此精确移除（Watson 的 StaticRouteManager 支持 Remove）
        private static Dictionary<IPlugin, List<(WatsonWebserver.Core.HttpMethod Method, string Path)>> PluginRoutes { get; } = [];

        // 分发(读) 与 卸载/重载(写) 的互斥：reload 会等待在途分发结束并阻塞新分发，
        // 避免 Invoke 一个正在被拆毁的插件。非递归——分发线程不会重入。
        private static readonly ReaderWriterLockSlim ReloadLock = new(LockRecursionPolicy.SupportsRecursion);

        // [Route] handler 是第二个分发面：由 Watson 线程直接调用，且可能 async（await 后续在线程池续跑），
        // 无法用线程亲和的 ReaderWriterLockSlim 跨 await 持锁。故另用一个在途计数 + 空闲信号来 quiesce：
        // - 路由进入时在读锁内登记（与写锁互斥，登记是纯同步操作，不违反线程亲和）；
        // - 卸载在写锁内先移除路由（挡住旧路由的新派发），释放写锁后等待已登记的在途路由排空，最后才 Unload。
        private static int _routesInFlight;
        private static readonly ManualResetEventSlim RoutesIdle = new(initialState: true);

        /// <summary>进入分发读锁；Server 的 ParseRequest/ParseResponse 包在 <see cref="EnterDispatch"/>/<see cref="ExitDispatch"/> 之间。</summary>
        internal static void EnterDispatch() => ReloadLock.EnterReadLock();
        /// <summary>退出分发读锁。务必放在 finally 中。</summary>
        internal static void ExitDispatch() => ReloadLock.ExitReadLock();

        /// <summary>
        /// 线程安全地快照当前已加载插件。分发路径之外的消费者（菜单、更新检查等）应经此枚举：
        /// 热重载会在写锁内增删 <see cref="LoadedPlugins"/>，直接枚举该 List 会与之并发触发 InvalidOperationException。
        /// </summary>
        public static IReadOnlyList<IPlugin> SnapshotLoadedPlugins()
        {
            EnterDispatch();
            try { return [.. LoadedPlugins]; }
            finally { ExitDispatch(); }
        }

        internal static string InternalName(IPlugin plugin) => plugin.GetType().Assembly.GetName().Name ?? plugin.Name;

        // 在读锁内登记一个在途路由：与 reload(写锁) 的"开始拆卸"互斥，确保 reload 看到的在途集合是确定的。
        static void EnterRoute()
        {
            EnterDispatch();
            try
            {
                if (Interlocked.Increment(ref _routesInFlight) == 1)
                    RoutesIdle.Reset();
            }
            finally
            {
                ExitDispatch();
            }
        }

        static void ExitRoute()
        {
            if (Interlocked.Decrement(ref _routesInFlight) == 0)
                RoutesIdle.Set();
        }

        internal static void BindLiveDisplay(Func<IPlugin, ILiveDisplayOutput> factory)
        {
            liveDisplayFactory = factory;
        }

        internal static void Init()
        {
            Directory.CreateDirectory("Plugins");
            LoadMetadatas();
            BuildGroups();
            LoadPlugins();
        }

        internal static void LoadMetadatas() => ScanAll(Metadatas);

        /// <summary>扫描 Plugins/ 下所有 dll 与 zip，把插件元数据（含卫星资源关联）写入 <paramref name="target"/>。</summary>
        static void ScanAll(Dictionary<string, PluginMetadata> target)
        {
            var culture = LanguageConfig.GetCulture();
            var pluginsDir = new DirectoryInfo("Plugins");
            if (!pluginsDir.Exists) return;

            foreach (var dll in pluginsDir.GetFiles("*.dll", SearchOption.AllDirectories))
            {
                if (dll.Name.EndsWith(".resources.dll") && !dll.FullName.Contains(culture)) continue;
                try
                {
                    var metadata = LoadMetadata(dll.FullName, null, false);
                    target[metadata.PluginName] = metadata;
                }
                catch { }
            }

            foreach (var zip in pluginsDir.GetFiles("*.zip", SearchOption.TopDirectoryOnly).Select(x => x.FullName))
                LoadZipMetadatas(zip, culture, target);
        }

        /// <summary>读取单个 zip 内的主插件元数据并关联其卫星资源，写入 <paramref name="target"/>。供初始加载与热重载复用。</summary>
        static void LoadZipMetadatas(string zip, string culture, Dictionary<string, PluginMetadata> target)
        {
            try
            {
                using var archive = ZipFile.OpenRead(zip);
                // 收集需要后处理的卫星资源条目
                List<ZipArchiveEntry> satelliteEntries = [];

                foreach (var entry in archive.Entries)
                {
                    if (!entry.FullName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)) continue;

                    if (!entry.FullName.Contains('/'))
                    {
                        // 主dll（根目录下）
                        using var stream = entry.Open();
                        var ms = new MemoryStream();
                        stream.CopyTo(ms);
                        ms.Position = 0;

                        try
                        {
                            var metadata = LoadMetadata($"{zip}|{entry.FullName}", ms, true);
                            target[metadata.PluginName] = metadata;
                        }
                        catch { }
                    }
                    else if (entry.FullName.EndsWith(".resources.dll", StringComparison.OrdinalIgnoreCase) &&
                             entry.FullName.Contains(culture, StringComparison.OrdinalIgnoreCase))
                    {
                        // 卫星资源文件（子目录下），延迟处理以确保主dll元数据已加载
                        satelliteEntries.Add(entry);
                    }
                }

                // 关联卫星资源到对应的主插件元数据
                foreach (var entry in satelliteEntries)
                {
                    var resourceName = Path.GetFileNameWithoutExtension(entry.FullName);
                    if (resourceName.EndsWith(".resources"))
                    {
                        var assemblyName = resourceName[..^".resources".Length];
                        if (target.TryGetValue(assemblyName, out var metadata))
                        {
                            metadata.SatelliteEntries.Add(entry.FullName);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LiveDisplayConsole.WriteException(ex);
            }
        }

        internal static PluginMetadata LoadMetadata(string path, Stream? stream, bool isFromZip)
        {
            var tempContext = new PluginLoadContext("temp");
            // 一律从内存流加载，绝不用 LoadFromAssemblyPath：后者会内存映射并锁住 DLL 文件直到该 ALC 被 GC，
            // 会妨碍开发者重建插件 DLL（热重载的前提）。与实际加载（CreateStream→LoadFromStream）保持一致。
            Assembly assembly;
            if (stream != null)
            {
                assembly = tempContext.LoadFromStream(stream);
            }
            else
            {
                using var fileStream = PluginLoadContext.LoadFileStream(path);
                assembly = tempContext.LoadFromStream(fileStream);
            }
            var loadInHost = assembly.GetCustomAttribute<LoadInHostContextAttribute>() != null;
            var sharedWith = assembly.GetCustomAttributes<SharedContextWithAttribute>().SelectMany(x => x.PluginNames).ToList();
            var metadata = new PluginMetadata(path, assembly.GetName().Name ?? string.Empty, loadInHost, sharedWith, isFromZip);
            tempContext.Unload();
            return metadata;
        }

        internal static void BuildGroups()
        {
            // 用已有分组播种，使本方法可重入：已成组的插件（含初始加载/已热重载的）不会被重复建组。
            var pluginToGroup = new Dictionary<string, HashSet<string>>();
            foreach (var existing in ContextGroups)
                foreach (var name in existing)
                    pluginToGroup[name] = existing;

            foreach (var m in Metadatas.Values.Where(x => x.SharedContextsWith.Count != 0 && !pluginToGroup.ContainsKey(x.PluginName)))
            {
                m.SharedContextsWith.RemoveAll(x => Metadatas.TryGetValue(x, out var v) && v.LoadInHost);
                foreach (var share in m.SharedContextsWith)
                {
                    if (pluginToGroup.TryGetValue(share, out var group))
                    {
                        group.Add(m.PluginName);
                        pluginToGroup[m.PluginName] = group;
                    }
                    else
                    {
                        var newGroup = new HashSet<string> { share, m.PluginName };
                        ContextGroups.Add(newGroup);
                        pluginToGroup[share] = newGroup;
                        pluginToGroup[m.PluginName] = newGroup;
                    }
                }
            }

            foreach (var m in Metadatas.Values.Where(x => !x.LoadInHost && x.SharedContextsWith.Count == 0))
            {
                if (!pluginToGroup.ContainsKey(m.PluginName))
                    ContextGroups.Add([m.PluginName]);
            }
        }

        static string GroupKey(IEnumerable<string> group) => string.Join("&", group);

        internal static void LoadGroup(HashSet<string> group)
        {
            var missing = group.Where(name => !Metadatas.ContainsKey(name)).ToList();
            if (missing.Count != 0)
            {
                foreach (var name in group)
                    if (Metadatas.TryGetValue(name, out var present))
                    {
                        LiveDisplayConsole.MarkupLine($"[red]插件 {name.EscapeMarkup()} 加载失败[/]:依赖的共享上下文插件 {string.Join("、", missing).EscapeMarkup()} 未安装。");
                        if (!FailedPlugins.Contains(present.FilePath)) FailedPlugins.Add(present.FilePath);
                    }
                return;
            }

            var key = GroupKey(group);
            if (!Contexts.TryGetValue(key, out var ctx))
            {
                ctx = new PluginLoadContext(key);
                Contexts[key] = ctx;
            }
            foreach (var name in group)
                LoadIntoContext(ctx, Metadatas[name]);
        }

        internal static void LoadPlugins()
        {
            foreach (var a in AssemblyLoadContext.Default.Assemblies)
            {
                var name = a.GetName().Name;
                if (name != null) HostAssemblyNames.Add(name);
            }

            foreach (var m in Metadatas.Values.Where(x => x.LoadInHost))
            {
                LoadIntoContext(AssemblyLoadContext.Default, m);
            }

            foreach (var group in ContextGroups)
                LoadGroup(group);
        }

        internal static void LoadIntoContext(AssemblyLoadContext ctx, PluginMetadata m)
        {
            try
            {
                using var stream = CreateStream(m);
                var assembly = ctx.LoadFromStream(stream);

                var type = assembly.GetExportedTypes().FirstOrDefault(x => typeof(IPlugin).IsAssignableFrom(x));
                if (type == null) return;

                if (Activator.CreateInstance(type) is not IPlugin plugin)
                {
                    FailedPlugins.Add(m.FilePath);
                    return;
                }

                if (plugin.Targets.Length == 0 || plugin.Targets.Intersect(Config.Repository.Targets).Any() || Config.Repository.Targets.Count == 0)
                {
                    Directory.CreateDirectory($"./PluginData/{plugin.Name}");
                    LoadedPlugins.Add(plugin);
                    PluginSettingsManager.LoadSettings(plugin);
                    RegisterMethods(plugin);
                }

                var assemblyName = assembly.GetName().Name;
                if (assemblyName != null) AssemblyMap[assemblyName] = assembly;
                Assemblies.Add(assembly);

                if (m.LoadInHost)
                {
                    foreach (var r in assembly.GetReferencedAssemblies())
                    {
                        if (r.Name != null && Metadatas.TryGetValue(r.Name, out var dep) &&
                            !HostAssemblyNames.Contains(r.Name))
                        {
                            using var s = CreateStream(dep);
                            var loaded = AssemblyLoadContext.Default.LoadFromStream(s);
                            HostAssemblyNames.Add(r.Name);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LiveDisplayConsole.WriteException(ex);
                FailedPlugins.Add(m.FilePath);
            }
        }

        internal static Stream CreateStream(PluginMetadata m)
        {
            if (!m.IsFromZip)
                return PluginLoadContext.LoadFileStream(m.FilePath);

            var parts = m.FilePath.Split('|', 2);
            using var archive = ZipFile.OpenRead(parts[0]);
            var entry = archive.GetEntry(parts[1])!;
            var ms = new MemoryStream();
            using (var s = entry.Open()) s.CopyTo(ms);
            ms.Position = 0;
            return ms;
        }

        internal static void RegisterMethods(IPlugin plugin)
        {
            foreach (var method in plugin.GetType().GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
            {
                var analyzer = method.GetCustomAttribute<AnalyzerAttribute>();
                if (analyzer != null)
                {
                    var dict = analyzer.Response ? ResponseAnalyzerMethods : RequestAnalyzerMethods;
                    if (!dict.TryGetValue(analyzer.Priority, out var list))
                    {
                        list = [];
                        dict[analyzer.Priority] = list;
                    }
                    list.Add((plugin, method));
                    continue;
                }

                var route = method.GetCustomAttribute<RouteAttribute>();
                if (route != null)
                {
                    var ps = method.GetParameters();
                    if (ps.Length == 1 && ps[0].ParameterType == typeof(HttpContextBase))
                    {
                        var path = $"/{plugin.Name}/{route.Path}";
                        // 包一层在途登记：使路由调用与 reload 互斥，避免在 ctx.Unload() 拆毁插件时仍有 handler 在跑（防泄漏）
                        Server.Instance.Routes.PreAuthentication.Static.Add(route.Method, path, async x =>
                        {
                            EnterRoute();
                            try { await (Task)method.Invoke(null, [x])!; }
                            finally { ExitRoute(); }
                        });
                        if (!PluginRoutes.TryGetValue(plugin, out var routes))
                        {
                            routes = [];
                            PluginRoutes[plugin] = routes;
                        }
                        routes.Add((route.Method, path));
                    }
                    else
                    {
                        LiveDisplayConsole.MarkupLine($"{plugin.Name.EscapeMarkup()}注册Route{route.Path.EscapeMarkup()}失败:参数有且只能有一个HttpContextBase，实际为{string.Join(", ", ps.Select(x => x.ParameterType.Name)).EscapeMarkup()}");
                    }
                }
            }
        }

        /// <summary>
        /// 调用插件 Initialize，并把期间注册的快捷键归属到该插件实例。
        /// 初始批量加载（Program.cs）与热重载都经此入口，保证 owner 标记一致。
        /// </summary>
        internal static void InitializePlugin(IPlugin plugin)
        {
            using (KeyboardManager.RegisterScope(plugin))
            {
                if (liveDisplayFactory is { } factory)
                    plugin.Initialize(factory(plugin));
                else
                    plugin.Initialize();
            }
        }

        // ── 热重载 ───────────────────────────────────────────────────────────

        /// <summary>
        /// 批量应用插件重载，返回仍需重启才能生效的插件名。
        /// 新安装的共享上下文成员会先把已加载的同组插件排入重载顺序，避免共享锚点被加载进两个 ALC。
        /// </summary>
        public static IReadOnlyList<string> ReloadPlugins(params string[] pluginNames)
        {
            var requested = pluginNames.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (requested.Count == 0) return [];

            var scanned = ScanPluginMetadata();
            var contextsToUnload = new List<PluginLoadContext>();
            ReloadLock.EnterWriteLock();
            List<string> needRestart = [];
            try
            {
                var ordered = BuildReloadOrder(requested, scanned);
                var outcomes = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
                foreach (var name in ordered)
                {
                    try
                    {
                        ReloadPluginLocked(name, scanned, outcomes, contextsToUnload);
                    }
                    catch (Exception ex)
                    {
                        LiveDisplayConsole.WriteException(ex);
#if DEBUG
                        throw;
#endif
                    }
                }

                needRestart = requested.Where(name =>
                    outcomes.TryGetValue(name, out var ok) ? !ok : Metadatas.ContainsKey(name) && !IsPluginLoaded(name))
                    .ToList();
            }
            finally
            {
                ReloadLock.ExitWriteLock();
                UnloadContextsAfterRoutesIdle(contextsToUnload);
            }

            return needRestart;
        }

        static Dictionary<string, PluginMetadata> ScanPluginMetadata()
        {
            Dictionary<string, PluginMetadata> scanned = [];
            ScanAll(scanned);
            return scanned;
        }

        static List<string> BuildReloadOrder(IReadOnlyList<string> requested, IReadOnlyDictionary<string, PluginMetadata> scanned)
        {
            var ordered = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var name in requested)
            {
                if (scanned.TryGetValue(name, out var metadata))
                {
                    foreach (var share in metadata.SharedContextsWith)
                    {
                        var group = ContextGroups.FirstOrDefault(g => g.Contains(share, StringComparer.OrdinalIgnoreCase));
                        if (group == null) continue;
                        foreach (var member in group)
                            if (seen.Add(member)) ordered.Add(member);
                    }
                }
                if (seen.Add(name)) ordered.Add(name);
            }
            return ordered;
        }

        static bool ReloadPluginLocked(
            string pluginName,
            IReadOnlyDictionary<string, PluginMetadata> scanned,
            Dictionary<string, bool> outcomes,
            List<PluginLoadContext> contextsToUnload)
        {
            // 同批次内该插件已随所属组一并处理过 → 复用既得结果，避免整组被二次卸载/重载
            if (outcomes.TryGetValue(pluginName, out var prior)) return prior;

            var existing = Metadatas.GetValueOrDefault(pluginName);
            var affectedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { pluginName };

            // 约束1：已加载且为 LoadInHost → 进了 Default ALC，永不可卸载
            if (existing is { LoadInHost: true })
            {
                LiveDisplayConsole.MarkupLine($"[yellow]插件 {pluginName.EscapeMarkup()} 在宿主上下文加载，不支持热重载，请重启。[/]");
                return outcomes[pluginName] = false;
            }

            var group = existing is null ? null : ContextGroups.FirstOrDefault(g => g.Contains(pluginName, StringComparer.OrdinalIgnoreCase));
            if (group != null)
                affectedNames.UnionWith(group);

            if (affectedNames.Any(name => scanned.TryGetValue(name, out var m) && m.LoadInHost))
            {
                LiveDisplayConsole.MarkupLine($"[yellow]插件 {pluginName.EscapeMarkup()} 在宿主上下文加载，不支持热重载，请重启。[/]");
                foreach (var name in affectedNames)
                    outcomes[name] = false;
                return false;
            }

            // 已加载的上下文组 → 先按整组卸载（含约束检查，如组内有注册了不可移除资源的插件则拒绝），失败则中止。
            // 整组成员都记入结果：组内其它插件若也在本批次，命中上面的复用短路，不再重复卸载/重载。
            if (group != null)
            {
                if (Contexts.ContainsKey(GroupKey(group)) && !TryUnloadGroup(group, contextsToUnload))
                {
                    foreach (var name in group) outcomes[name] = false;
                    return false;
                }
            }

            // 重新扫描磁盘，补回所有"当前未加载"的插件元数据（含刚卸载的、以及全新增的）
            MergeUnloadedMetadatas(scanned);

            if (!Metadatas.ContainsKey(pluginName))
            {
                // 文件已被删除：卸载即完成
                LiveDisplayConsole.MarkupLine($"[yellow]插件 {pluginName.EscapeMarkup()} 的文件已不存在，已卸载。[/]");
                return outcomes[pluginName] = true;
            }

            // 新元数据若为 LoadInHost（如刚加上该特性），不走热重载路径
            if (Metadatas[pluginName].LoadInHost)
            {
                LiveDisplayConsole.MarkupLine($"[yellow]插件 {pluginName.EscapeMarkup()} 在宿主上下文加载，不支持热重载，请重启。[/]");
                return outcomes[pluginName] = false;
            }

            BuildGroups();
            LoadAffectedGroups(affectedNames, outcomes);

            var loaded = IsPluginLoaded(pluginName);
            if (loaded)
                LiveDisplayConsole.MarkupLine($"[green]插件 {pluginName.EscapeMarkup()} 已重载。[/]");
            return outcomes[pluginName] = loaded;
        }

        /// <summary>加载受本轮重载影响且尚无 ALC 的上下文组，对新实例调用 Initialize，并补发一次启动事件。</summary>
        static void LoadAffectedGroups(IEnumerable<string> affectedNames, Dictionary<string, bool> outcomes)
        {
            var affected = affectedNames.ToHashSet(StringComparer.OrdinalIgnoreCase);
            var before = LoadedPlugins.ToHashSet();
            var pendingGroups = ContextGroups
                .Where(group => group.Any(affected.Contains) && !Contexts.ContainsKey(GroupKey(group)))
                .ToList();
            foreach (var group in pendingGroups)
            {
                // 清掉本组上次的加载失败记录，避免修好后重载仍显示"加载失败"
                foreach (var name in group)
                    if (Metadatas.TryGetValue(name, out var m))
                        FailedPlugins.Remove(m.FilePath);

                LoadGroup(group);
                foreach (var name in group.Where(Metadatas.ContainsKey))
                    outcomes[name] = IsPluginLoaded(name);
            }

            var fresh = LoadedPlugins.Where(p => !before.Contains(p)).ToList();
            foreach (var plugin in fresh)
            {
                outcomes[InternalName(plugin)] = true;
            }

            if (!Server.IsRunning)
                return;

            // 仅对本轮新加载进来的实例调用 Initialize（带 owner 作用域）
            foreach (var plugin in fresh)
                InitializePlugin(plugin);

            // OnStarted 设计上只在宿主启动时 fire 一次；热重载后必须对新实例补发，
            // 否则依赖启动事件做异步工作的插件（如 DMMPlugin.OnUraStarted）会得到永不触发的"半初始化"实例。
            // 仅对本轮新实例触发，已在运行的其它插件不受影响。在写锁内同步等待完成（与上面的 Initialize 一致）。
            if (fresh.Count != 0)
            {
                var freshAssemblies = fresh.Select(p => p.GetType().Assembly).ToHashSet();
                UraEvents.TriggerStartedForAsync(freshAssemblies).GetAwaiter().GetResult();
            }
        }

        static void MergeUnloadedMetadatas(IReadOnlyDictionary<string, PluginMetadata> scanned)
        {
            foreach (var (name, m) in scanned)
            {
                if (Metadatas.ContainsKey(name)) continue; // 仍加载中的插件不动
                Metadatas[name] = m;
            }
        }

        static bool IsPluginLoaded(string pluginName) =>
            LoadedPlugins.Any(p => string.Equals(InternalName(p), pluginName, StringComparison.OrdinalIgnoreCase));

        /// <summary>
        /// 卸载整个上下文组：逐项斩断对组内插件的托管引用，并把 ALC 交给调用方在写锁外 Unload。
        /// 触碰 LoadInHost 时拒绝并返回 false（此 Watson 版本路由可移除，故仅 LoadInHost 会被拒）。
        /// </summary>
        static bool TryUnloadGroup(HashSet<string> group, List<PluginLoadContext> contextsToUnload)
        {
            // 约束1：组内任一插件 LoadInHost → 整组进了 Default ALC，永不可卸载
            if (group.Any(n => Metadatas.TryGetValue(n, out var m) && m.LoadInHost))
            {
                LiveDisplayConsole.MarkupLine($"[yellow]插件 {string.Join("、", group).EscapeMarkup()} 在宿主上下文加载，不支持热重载，请重启。[/]");
                return false;
            }

            var key = GroupKey(group);
            // 斩断引用 + Unload，放在不内联方法里：其所有局部（ALC/Assembly/插件实例）随返回离开作用域，
            // 运行时方能在后续 GC 时真正回收该 ALC。
            // 这里【刻意不】同步校验 ALC 是否已回收：ALC 卸载是异步的，且在 reload 调用栈尚未展开时做 GC 自检，
            // 会被运行时的保守栈扫描误判为"仍存活"（其实卸载已成功，栈展开后即被回收）。卸载的实证由集成测试
            // HotReloadTests 在调用栈完全展开后用 WeakReference 完成。若将来要在运行期检出真正的泄漏，
            // 应改为延迟校验：把弱引用记下来，到【下一次】reload（上一次的栈已展开）时再 GC + 检查。
            contextsToUnload.Add(CutReferencesForUnload(group, key));
            return true;
        }

        // 不内联：保证返回后栈帧里不残留对插件 ALC/Assembly/实例的强引用，运行时方能在后续 GC 时回收该 ALC
        [MethodImpl(MethodImplOptions.NoInlining)]
        static PluginLoadContext CutReferencesForUnload(HashSet<string> group, string key)
        {
            var ctx = Contexts[key];

            // 组内主程序集集合：用于 OnStarted/快捷键的按程序集兜底清扫
            var groupAssemblies = group
                .Select(n => AssemblyMap.GetValueOrDefault(n))
                .Where(a => a != null)
                .Cast<Assembly>()
                .ToHashSet();

            // 逐插件实例斩断引用
            foreach (var plugin in LoadedPlugins.Where(p => group.Contains(InternalName(p))).ToList())
            {
                try { plugin.Dispose(); }
                catch (Exception ex) { LiveDisplayConsole.WriteException(ex); }

                LoadedPlugins.Remove(plugin);
                RemoveAnalyzerMethods(plugin);
                RemoveRoutes(plugin);
                KeyboardManager.UnregisterByOwner(plugin);
            }

            // 解除 OnStarted 订阅、兜底清扫遗漏的快捷键（如在 OnStarted 里注册、未走 owner 作用域的）
            UraEvents.UnsubscribeStarted(groupAssemblies);
            KeyboardManager.ClearHandlersByAssembly(groupAssemblies);

            // 从全局表移除该组的一切引用（PluginRoutes 已在 RemoveRoutes 中按实例清除）
            foreach (var name in group)
            {
                if (AssemblyMap.TryGetValue(name, out var asm))
                {
                    Assemblies.Remove(asm);
                    AssemblyMap.Remove(name);
                }
                Metadatas.Remove(name);
            }
            Contexts.Remove(key);
            ContextGroups.RemoveAll(g => g.SetEquals(group));

            return ctx;
        }

        static void UnloadContextsAfterRoutesIdle(List<PluginLoadContext> contextsToUnload)
        {
            if (contextsToUnload.Count == 0) return;

            RoutesIdle.Wait();
            foreach (var ctx in contextsToUnload)
                ctx.Unload();
        }

        static void RemoveAnalyzerMethods(IPlugin plugin)
        {
            foreach (var dict in (SortedDictionary<int, List<(IPlugin Self, MethodInfo Method)>>[])[RequestAnalyzerMethods, ResponseAnalyzerMethods])
            {
                foreach (var priority in dict.Keys.ToList())
                {
                    var list = dict[priority];
                    list.RemoveAll(x => ReferenceEquals(x.Self, plugin));
                    if (list.Count == 0) dict.Remove(priority);
                }
            }
        }

        static void RemoveRoutes(IPlugin plugin)
        {
            if (!PluginRoutes.TryGetValue(plugin, out var routes)) return;
            foreach (var (method, path) in routes)
            {
                // StaticRouteManager.Remove 按 (method, path) 精确移除，清掉钉住插件程序集的 handler 闭包
                if (Server.Instance.Routes.PreAuthentication.Static.Exists(method, path))
                    Server.Instance.Routes.PreAuthentication.Static.Remove(method, path);
            }
            PluginRoutes.Remove(plugin);
        }

        internal class PluginLoadContext(string name) : AssemblyLoadContext(name, true)
        {
            protected override Assembly? Load(AssemblyName name)
            {
                if (name.Name != null && AssemblyMap.TryGetValue(name.Name, out var shared))
                    return shared;

                if (name.Name != null && HostAssemblyNames.Contains(name.Name))
                {
                    var host = Default.Assemblies.FirstOrDefault(a => a.GetName().Name == name.Name);
                    if (host != null) return host;
                }

                // 处理卫星资源文件加载（从zip中）
                if (!string.IsNullOrEmpty(name.CultureName) && name.Name != null && name.Name.EndsWith(".resources"))
                {
                    var searchSuffix = $"{name.CultureName}/{name.Name}.dll";
                    foreach (var metadata in Metadatas.Values.Where(m => m.IsFromZip))
                    {
                        var satelliteEntry = metadata.SatelliteEntries.FirstOrDefault(e =>
                            e.Contains(searchSuffix, StringComparison.OrdinalIgnoreCase));

                        if (satelliteEntry != null)
                        {
                            var zipPath = metadata.FilePath.Split('|', 2)[0];
                            using var satelliteStream = CreateZipEntryStream(zipPath, satelliteEntry);
                            if (satelliteStream != null) return LoadFromStream(satelliteStream);
                        }
                    }
                }

                if (name.Name == null || !Metadatas.TryGetValue(name.Name, out var dep)) return null;

                using var stream = CreateStream(dep);
                return LoadFromStream(stream);
            }

            private static MemoryStream? CreateZipEntryStream(string zipPath, string entryName)
            {
                using var archive = ZipFile.OpenRead(zipPath);
                var entry = archive.GetEntry(entryName);
                if (entry == null) return null;

                var ms = new MemoryStream();
                using (var s = entry.Open()) s.CopyTo(ms);
                ms.Position = 0;
                return ms;
            }

            internal static MemoryStream LoadFileStream(string path)
            {
                var ms = new MemoryStream();
                using var fs = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
                fs.CopyTo(ms);
                ms.Position = 0;
                return ms;
            }
        }

        internal class PluginMetadata(string path, string name, bool loadInHost, List<string> shared, bool isFromZip)
        {
            public string FilePath { get; } = path;
            public string PluginName { get; } = name;
            public bool LoadInHost { get; } = loadInHost;
            public List<string> SharedContextsWith { get; } = shared;
            public bool IsFromZip { get; } = isFromZip;
            public List<string> SatelliteEntries { get; } = [];
        }
    }
}
