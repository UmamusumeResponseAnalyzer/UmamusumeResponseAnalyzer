using System.Collections.Frozen;
using Gallop.Endpoints;
using Spectre.Console;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Runtime.Loader;
using UmamusumeResponseAnalyzer;
using UmamusumeResponseAnalyzer.LiveDisplay;
using WatsonWebserver.Core;

namespace UmamusumeResponseAnalyzer.Plugin
{
    internal sealed record AnalyzerRegistration(
        IPlugin Plugin,
        MethodInfo? Method,
        Type EndpointType,
        AnalyzerKind Kind,
        int Priority,
        Func<AnalyzerDispatchContext, ValueTask> Handler,
        string Source);

    sealed class PluginScopedAnalyzerRegistry(IPlugin plugin) : IPluginAnalyzerRegistry
    {
        public IDisposable RegisterRequest<TEndpoint>(
            Func<byte[], ValueTask> handler,
            int priority = 0)
            where TEndpoint : IGameEndpoint
            => RegisterRaw(AnalyzerKind.Request, typeof(TEndpoint), handler, priority);

        public IDisposable RegisterResponse<TEndpoint>(
            Func<byte[], ValueTask> handler,
            int priority = 0)
            where TEndpoint : IGameEndpoint
            => RegisterRaw(AnalyzerKind.Response, typeof(TEndpoint), handler, priority);

        public IDisposable RegisterRequest<TEndpoint, TRequest>(
            Func<TRequest, ValueTask> handler,
            int priority = 0)
            where TEndpoint : IGameEndpoint
            => RegisterDto(AnalyzerKind.Request, typeof(TEndpoint), typeof(TRequest), handler, priority);

        public IDisposable RegisterResponse<TEndpoint, TResponse>(
            Func<TResponse, ValueTask> handler,
            int priority = 0)
            where TEndpoint : IGameEndpoint
            => RegisterDto(AnalyzerKind.Response, typeof(TEndpoint), typeof(TResponse), handler, priority);

        IDisposable RegisterRaw(
            AnalyzerKind kind,
            Type endpointType,
            Func<byte[], ValueTask> handler,
            int priority)
        {
            ArgumentNullException.ThrowIfNull(handler);
            return PluginManager.RegisterProgrammaticAnalyzer(
                plugin,
                kind,
                endpointType,
                typeof(byte[]),
                priority,
                context => handler(context.Payload),
                "programmatic raw analyzer");
        }

        IDisposable RegisterDto<TPayload>(
            AnalyzerKind kind,
            Type endpointType,
            Type payloadType,
            Func<TPayload, ValueTask> handler,
            int priority)
        {
            ArgumentNullException.ThrowIfNull(handler);
            if (payloadType == typeof(byte[]))
                throw new InvalidOperationException("DTO analyzer 不能使用 byte[]；raw analyzer 请使用单泛型 RegisterRequest/RegisterResponse overload。");

            return PluginManager.RegisterProgrammaticAnalyzer(
                plugin,
                kind,
                endpointType,
                payloadType,
                priority,
                context => handler((TPayload)context.GetDto()),
                "programmatic DTO analyzer");
        }
    }

    sealed class AnalyzerRegistrationHandle(AnalyzerRegistration registration) : IDisposable
    {
        int disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0)
                return;

            PluginManager.RemoveAnalyzerRegistration(registration);
        }
    }

    sealed class RouteRegistration(
        IPlugin plugin,
        WatsonWebserver.Core.HttpMethod method,
        string path,
        Func<HttpContextBase, Task> handler)
    {
        int removed;
        int inFlight;
        readonly ManualResetEventSlim idle = new(initialState: true);

        public IPlugin Plugin { get; } = plugin;
        public WatsonWebserver.Core.HttpMethod Method { get; } = method;
        public string Path { get; } = path;

        public async Task InvokeAsync(HttpContextBase ctx)
        {
            PluginManager.EnterDispatch();
            try
            {
                if (!TryEnter())
                    throw new ObjectDisposedException(Path, $"插件路由已卸载: plugin={Plugin.Name}, path={Path}");
            }
            finally
            {
                PluginManager.ExitDispatch();
            }

            try
            {
                using var callback = PluginManager.EnterPluginCallbackScope();
                await handler(ctx);
            }
            finally
            {
                Exit();
            }
        }

        public void MarkRemoved()
            => Interlocked.Exchange(ref removed, 1);

        public void WaitForIdle()
            => idle.Wait();

        bool TryEnter()
        {
            if (Volatile.Read(ref removed) != 0)
                return false;

            if (Interlocked.Increment(ref inFlight) == 1)
                idle.Reset();

            if (Volatile.Read(ref removed) == 0)
                return true;

            Exit();
            return false;
        }

        void Exit()
        {
            if (Interlocked.Decrement(ref inFlight) == 0)
                idle.Set();
        }
    }

    sealed record PluginRegistrationPlan(
        List<AnalyzerRegistration> Analyzers,
        List<RouteRegistration> Routes);

    sealed record PendingPluginUnload(
        PluginManager.PluginLoadContext Context,
        List<IPlugin> Plugins,
        List<RouteRegistration> Routes,
        List<Action> EventWaits,
        HashSet<Assembly> Assemblies);

    sealed record StagedAssembly(string Name, Assembly Assembly);

    sealed record StagedPlugin(IPlugin Plugin, PluginRegistrationPlan Plan);

    sealed record StagedGroupLoad(
        HashSet<string> Group,
        string Key,
        PluginManager.PluginLoadContext Context,
        List<StagedAssembly> Assemblies,
        List<StagedPlugin> Plugins);

    public static class PluginManager
    {
        internal static Dictionary<string, PluginMetadata> Metadatas { get; } = [];
        internal static Dictionary<string, PluginMetadata> AssemblyMetadatas { get; } = [];
        internal static List<string> FailedPlugins { get; } = [];
        public static List<IPlugin> LoadedPlugins { get; } = [];
        internal static SortedDictionary<int, List<AnalyzerRegistration>> RequestAnalyzerMethods { get; } = [];
        internal static SortedDictionary<int, List<AnalyzerRegistration>> ResponseAnalyzerMethods { get; } = [];
        internal static List<HashSet<string>> ContextGroups { get; } = [];
        internal static Dictionary<string, PluginLoadContext> Contexts { get; } = [];
        internal static Dictionary<string, Assembly> AssemblyMap { get; } = [];
        internal static List<Assembly> Assemblies { get; } = [];
        static Dictionary<string, Assembly> SharedAssemblies { get; } = new(StringComparer.Ordinal);
        static readonly string HostAssemblyName = typeof(PluginManager).Assembly.GetName().Name ?? "UmamusumeResponseAnalyzer";
        static readonly FrozenSet<string> SharedAssemblyNames = new[]
        {
            HostAssemblyName,
            "Spectre.Console",
            "Spectre.Console.Ansi",
            "Watson.Lite",
            "WatsonWebserver.Core",
            "WatsonWebserver.Lite",
        }.ToFrozenSet(StringComparer.Ordinal);
        static readonly object SharedAssemblyGate = new();
        static bool sharedAssembliesInitialized;
        static readonly PluginHostEvents HostEvents = new();
        static Func<IPlugin, ILiveDisplayOutput>? liveDisplayFactory;
        static readonly AsyncLocal<int> PluginCallbackDepth = new();
        static readonly object AnalyzerGate = new();

        // 每个插件注册的 HTTP 路由，卸载时凭此精确移除（Watson 的 StaticRouteManager 支持 Remove）
        private static Dictionary<IPlugin, List<RouteRegistration>> PluginRoutes { get; } = new(ReferenceEqualityComparer.Instance);

        // 分发(读) 与 卸载/重载(写) 的互斥：reload 会等待在途分发结束并阻塞新分发，
        // 避免 Invoke 一个正在被拆毁的插件。非递归——分发线程不会重入。
        private static readonly ReaderWriterLockSlim ReloadLock = new(LockRecursionPolicy.SupportsRecursion);
        static int reloadTransactionActive;

        /// <summary>进入分发读锁；Server 的 analyzer dispatch 包在 <see cref="EnterDispatch"/>/<see cref="ExitDispatch"/> 之间。</summary>
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

        internal static void LoadMetadatas()
        {
            Dictionary<string, PluginMetadata> assemblies = [];
            ScanAll(Metadatas, assemblies);
            ReplaceAssemblyMetadatas(assemblies);
        }

        /// <summary>扫描 Plugins/ 下所有 dll 与 zip，把插件元数据（含卫星资源关联）写入 <paramref name="target"/>。</summary>
        static void ScanAll(Dictionary<string, PluginMetadata> target, Dictionary<string, PluginMetadata> assemblyTarget)
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
                    assemblyTarget[metadata.PluginName] = metadata;
                    target[metadata.PluginName] = metadata;
                }
                catch (Exception ex)
                {
                    LiveDisplayConsole.LogException("Plugin", ex);
                    if (!FailedPlugins.Contains(dll.FullName)) FailedPlugins.Add(dll.FullName);
                }
            }

            foreach (var zip in pluginsDir.GetFiles("*.zip", SearchOption.TopDirectoryOnly).Select(x => x.FullName))
                LoadZipMetadatas(zip, culture, target, assemblyTarget);
        }

        /// <summary>读取单个 zip 内的主插件元数据并关联其卫星资源，写入 <paramref name="target"/>。供初始加载与热重载复用。</summary>
        static void LoadZipMetadatas(
            string zip,
            string culture,
            Dictionary<string, PluginMetadata> target,
            Dictionary<string, PluginMetadata> assemblyTarget)
        {
            try
            {
                using var archive = ZipFile.OpenRead(zip);
                var pluginName = Path.GetFileNameWithoutExtension(zip);
                var hasMainPlugin = false;
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
                            var pluginPath = $"{zip}|{entry.FullName}";
                            var metadata = LoadMetadata(pluginPath, ms, true);
                            assemblyTarget[metadata.PluginName] = metadata;
                            if (string.Equals(metadata.PluginName, pluginName, StringComparison.OrdinalIgnoreCase))
                            {
                                target[metadata.PluginName] = metadata;
                                hasMainPlugin = true;
                            }
                        }
                        catch (Exception ex)
                        {
                            LiveDisplayConsole.LogException("Plugin", ex);
                            var pluginPath = $"{zip}|{entry.FullName}";
                            if (!FailedPlugins.Contains(pluginPath)) FailedPlugins.Add(pluginPath);
                        }
                    }
                    else if (entry.FullName.EndsWith(".resources.dll", StringComparison.OrdinalIgnoreCase) &&
                             entry.FullName.Contains(culture, StringComparison.OrdinalIgnoreCase))
                    {
                        // 卫星资源文件（子目录下），延迟处理以确保主dll元数据已加载
                        satelliteEntries.Add(entry);
                    }
                }

                if (!hasMainPlugin)
                    LiveDisplayConsole.MarkupLog("Plugin", $"[yellow]插件包 {Path.GetFileName(zip).EscapeMarkup()} 未找到主插件 DLL {pluginName.EscapeMarkup()}.dll，已跳过。[/]", LiveDisplaySeverity.Warning);

                // 关联卫星资源到对应的程序集元数据
                foreach (var entry in satelliteEntries)
                {
                    var resourceName = Path.GetFileNameWithoutExtension(entry.FullName);
                    if (resourceName.EndsWith(".resources"))
                    {
                        var assemblyName = resourceName[..^".resources".Length];
                        if (assemblyTarget.TryGetValue(assemblyName, out var metadata))
                        {
                            metadata.SatelliteEntries.Add(entry.FullName);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LiveDisplayConsole.LogException("Plugin", ex);
                if (!FailedPlugins.Contains(zip)) FailedPlugins.Add(zip);
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

        static void ReplaceAssemblyMetadatas(Dictionary<string, PluginMetadata> assemblies)
        {
            AssemblyMetadatas.Clear();
            foreach (var (name, metadata) in assemblies)
                AssemblyMetadatas[name] = metadata;
        }

        static bool TryGetAssemblyMetadata(string name, out PluginMetadata metadata)
            => AssemblyMetadatas.TryGetValue(name, out metadata!) || Metadatas.TryGetValue(name, out metadata!);

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
                        LiveDisplayConsole.MarkupLog("Plugin", $"[red]插件 {name.EscapeMarkup()} 加载失败[/]:依赖的共享上下文插件 {string.Join("、", missing).EscapeMarkup()} 未安装。", LiveDisplaySeverity.Error);
                        if (!FailedPlugins.Contains(present.FilePath)) FailedPlugins.Add(present.FilePath);
                    }
                return;
            }

            var key = GroupKey(group);
            var createdContext = !Contexts.TryGetValue(key, out var ctx);
            if (createdContext)
            {
                ctx = new PluginLoadContext(key);
            }

            var loadedBefore = LoadedPlugins.ToHashSet<IPlugin>(ReferenceEqualityComparer.Instance);
            foreach (var name in group)
            {
                if (LoadIntoContext(ctx!, Metadatas[name]))
                    continue;

                if (createdContext)
                    RollBackGroupLoad(group, ctx!, loadedBefore);
                return;
            }

            if (createdContext)
                Contexts[key] = ctx!;
        }

        internal static void LoadPlugins()
        {
            EnsureSharedAssembliesLoaded();

            foreach (var m in Metadatas.Values.Where(x => x.LoadInHost))
            {
                LoadIntoContext(AssemblyLoadContext.Default, m);
            }

            foreach (var group in ContextGroups)
                LoadGroup(group);
        }

        internal static bool LoadIntoContext(AssemblyLoadContext ctx, PluginMetadata m)
        {
            IPlugin? plugin = null;
            Assembly? assembly = null;
            string? assemblyName = null;
            var phase = "读取插件程序集";
            try
            {
                using var stream = CreateStream(m);
                assembly = ctx.LoadFromStream(stream);

                phase = "读取插件导出类型";
                var type = assembly.GetExportedTypes().FirstOrDefault(x => typeof(IPlugin).IsAssignableFrom(x));
                if (type == null)
                {
                    LiveDisplayConsole.MarkupLog("Plugin", $"[red]插件 {m.PluginName.EscapeMarkup()} 加载失败[/]: 未找到实现 {nameof(IPlugin)} 的公开类型。", LiveDisplaySeverity.Error);
                    FailedPlugins.Add(m.FilePath);
                    return false;
                }

                phase = "创建插件实例";
                if (Activator.CreateInstance(type) is not IPlugin createdPlugin)
                {
                    LiveDisplayConsole.MarkupLog("Plugin", $"[red]插件 {m.PluginName.EscapeMarkup()} 加载失败[/]: 无法创建插件实例。type={(type.FullName ?? type.Name).EscapeMarkup()}", LiveDisplaySeverity.Error);
                    FailedPlugins.Add(m.FilePath);
                    return false;
                }
                plugin = createdPlugin;

                if (plugin.Targets.Length == 0 || plugin.Targets.Intersect(Config.Repository.Targets).Any() || Config.Repository.Targets.Count == 0)
                {
                    phase = "注册插件入口";
                    RegisterMethods(plugin);
                    LoadedPlugins.Add(plugin);
                }

                phase = "登记插件程序集";
                assemblyName = assembly.GetName().Name;
                if (assemblyName != null) AssemblyMap[assemblyName] = assembly;
                Assemblies.Add(assembly);

                if (m.LoadInHost)
                {
                    phase = "加载宿主上下文依赖";
                    foreach (var r in assembly.GetReferencedAssemblies())
                    {
                        if (ResolveSharedAssembly(r) is not null)
                            continue;

                        if (r.Name != null && TryGetAssemblyMetadata(r.Name, out var dep) &&
                            AssemblyLoadContext.Default.Assemblies.All(a => a.GetName().Name != r.Name))
                        {
                            using var s = CreateStream(dep);
                            AssemblyLoadContext.Default.LoadFromStream(s);
                        }
                    }
                }
                return true;
            }
            catch (Exception ex)
            {
                if (plugin is not null)
                {
                    LoadedPlugins.Remove(plugin);
                    RemoveAnalyzerMethods(plugin);
                    foreach (var route in RemoveRoutes(plugin))
                        route.WaitForIdle();
                    DisposeHostEventSubscriptions(plugin);
                    KeyboardManager.UnregisterByOwner(plugin);
                    try { plugin.Dispose(); }
                    catch (Exception disposeEx) { LiveDisplayConsole.LogException("Plugin", disposeEx); }
                }

                if (assemblyName is not null)
                    AssemblyMap.Remove(assemblyName);
                if (assembly is not null)
                    Assemblies.Remove(assembly);

                LiveDisplayConsole.LogException("Plugin", PluginLoadException(m, phase, ex));
                FailedPlugins.Add(m.FilePath);
                return false;
            }
        }

        static InvalidOperationException PluginLoadException(PluginMetadata metadata, string phase, Exception inner)
            => new(
                $"插件加载失败: plugin={metadata.PluginName}, phase={phase}",
                inner);

        static void RollBackGroupLoad(HashSet<string> group, PluginLoadContext ctx, HashSet<IPlugin> loadedBefore)
        {
            foreach (var plugin in LoadedPlugins
                         .Where(p => !loadedBefore.Contains(p) && group.Contains(InternalName(p)))
                         .ToList())
            {
                LoadedPlugins.Remove(plugin);
                RemoveAnalyzerMethods(plugin);
                foreach (var route in RemoveRoutes(plugin))
                    route.WaitForIdle();
                DisposeHostEventSubscriptions(plugin);
                KeyboardManager.UnregisterByOwner(plugin);
                try { plugin.Dispose(); }
                catch (Exception ex) { LiveDisplayConsole.LogException("Plugin", ex); }
            }

            foreach (var name in group)
            {
                if (!AssemblyMap.TryGetValue(name, out var asm))
                    continue;

                Assemblies.Remove(asm);
                AssemblyMap.Remove(name);
            }

            ctx.Unload();
        }

        static void EnsureSharedAssembliesLoaded()
        {
            if (Volatile.Read(ref sharedAssembliesInitialized))
                return;

            lock (SharedAssemblyGate)
            {
                if (sharedAssembliesInitialized)
                    return;

                RegisterSharedAssembly(typeof(IPlugin).Assembly);
                RegisterSharedAssembly(typeof(IGameEndpoint).Assembly);
                RegisterSharedAssembly(typeof(AnsiConsole).Assembly);
                RegisterSharedAssembly(typeof(HttpContextBase).Assembly);
                RegisterSharedAssembly(typeof(WatsonWebserver.Lite.WebserverLite).Assembly);

                foreach (var assembly in AssemblyLoadContext.Default.Assemblies)
                    if (assembly.GetName().Name is { } name && SharedAssemblyNames.Contains(name))
                        RegisterSharedAssembly(assembly);

                Volatile.Write(ref sharedAssembliesInitialized, true);
            }
        }

        static void RegisterSharedAssembly(Assembly assembly)
        {
            var name = assembly.GetName().Name;
            if (name is not null)
                SharedAssemblies[name] = assembly;
        }

        internal static Assembly? ResolveSharedAssembly(AssemblyName requested)
        {
            if (requested.Name is not { } name || !SharedAssemblyNames.Contains(name))
                return null;

            EnsureSharedAssembliesLoaded();
            lock (SharedAssemblyGate)
            {
                if (!SharedAssemblies.TryGetValue(name, out var shared))
                {
                    try
                    {
                        shared = AssemblyLoadContext.Default.LoadFromAssemblyName(requested);
                        RegisterSharedAssembly(shared);
                    }
                    catch (Exception ex)
                    {
                        throw new FileLoadException($"shared ABI assembly {requested.FullName} 必须由 Default ALC 加载，但宿主无法加载。", requested.FullName, ex);
                    }
                }

                ValidateSharedAssemblyVersion(name, requested, shared.GetName());

                return shared;
            }
        }

        static bool IsHostAssembly(string name) => string.Equals(name, HostAssemblyName, StringComparison.Ordinal);

        static void ValidateSharedAssemblyVersion(string name, AssemblyName requested, AssemblyName actual)
        {
            if (requested.Version is null)
                return;

            if (IsHostAssembly(name))
            {
                if (actual.Version is not null && requested.Version > actual.Version)
                    LiveDisplayConsole.Log(
                        "Plugin",
                        $"插件依赖的宿主 ABI 版本更高，请更新 UmamusumeResponseAnalyzer: 插件请求 {requested.FullName}，当前宿主 {actual.FullName}。",
                        LiveDisplaySeverity.Warning);
                return;
            }

            if (actual.Version != requested.Version)
                throw new FileLoadException(
                    $"shared ABI assembly 版本不一致: 插件请求 {requested.FullName}，宿主 Default ALC 已加载 {actual.FullName}。",
                    requested.FullName);
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
            var plan = CreateRegistrationPlan(plugin);
            try
            {
                CommitRegistrationPlan(plan);
            }
            catch
            {
                RemoveAnalyzerMethods(plugin);
                RemoveRoutes(plugin);
                throw;
            }
        }

        static PluginRegistrationPlan CreateRegistrationPlan(IPlugin plugin)
        {
            var analyzers = new List<AnalyzerRegistration>();
            var routes = new List<RouteRegistration>();
            foreach (var method in plugin.GetType().GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
            {
                foreach (var analyzer in method.GetCustomAttributes<AnalyzerAttribute>())
                {
                    var registration = CreateAnalyzerRegistration(plugin, method, analyzer);
                    analyzers.Add(registration);
                }

                var route = method.GetCustomAttribute<RouteAttribute>();
                if (route is not null)
                    routes.Add(CreateRouteRegistration(plugin, method, route));
            }

            return new(analyzers, routes);
        }

        static RouteRegistration CreateRouteRegistration(IPlugin plugin, MethodInfo method, RouteAttribute route)
        {
            var parameters = method.GetParameters();
            if (parameters.Length != 1 || parameters[0].ParameterType != typeof(HttpContextBase) || method.ReturnType != typeof(Task))
            {
                var actualParameters = parameters.Length == 0
                    ? "<none>"
                    : string.Join(", ", parameters.Select(x => x.ParameterType.FullName ?? x.ParameterType.Name));
                throw new InvalidOperationException(
                    $"插件 Route 签名无效: plugin={plugin.Name} ({InternalName(plugin)}), " +
                    $"method={method.DeclaringType?.FullName}.{method.Name}, path={route.Path}, " +
                    $"expected=Task {nameof(HttpContextBase)}, actual={method.ReturnType.FullName} ({actualParameters})");
            }

            var handler = method.IsStatic
                ? method.CreateDelegate<Func<HttpContextBase, Task>>()
                : method.CreateDelegate<Func<HttpContextBase, Task>>(plugin);
            return new(plugin, route.Method, $"/{plugin.Name}/{route.Path}", handler);
        }

        static void CommitRegistrationPlan(PluginRegistrationPlan plan)
        {
            foreach (var registration in plan.Analyzers)
                CommitAnalyzerRegistration(registration);

            foreach (var route in plan.Routes)
            {
                // 包一层在途登记：使路由调用与 reload 互斥，避免在 ctx.Unload() 拆毁插件时仍有 handler 在跑（防泄漏）
                Server.Instance.Routes.PreAuthentication.Static.Add(route.Method, route.Path, route.InvokeAsync);
                if (!PluginRoutes.TryGetValue(route.Plugin, out var routes))
                {
                    routes = [];
                    PluginRoutes[route.Plugin] = routes;
                }
                routes.Add(route);
            }
        }

        internal static IPluginAnalyzerRegistry AnalyzersFor(IPlugin plugin)
            => new PluginScopedAnalyzerRegistry(plugin);

        internal static IDisposable RegisterProgrammaticAnalyzer(
            IPlugin plugin,
            AnalyzerKind kind,
            Type endpointType,
            Type payloadType,
            int priority,
            Func<AnalyzerDispatchContext, ValueTask> handler,
            string source)
        {
            var registration = RegisterAnalyzerCore(plugin, kind, endpointType, payloadType, priority, handler, source, method: null);
            CommitAnalyzerRegistration(registration);
            return new AnalyzerRegistrationHandle(registration);
        }

        static AnalyzerRegistration CreateAnalyzerRegistration(IPlugin plugin, MethodInfo method, AnalyzerAttribute analyzer)
        {
            var payloadType = ValidateAttributeAnalyzerSignature(plugin, method, analyzer);
            var source = $"{method.DeclaringType?.FullName}.{method.Name}";
            Func<AnalyzerDispatchContext, ValueTask> handler = payloadType == typeof(byte[])
                ? context => InvokeAttributeAnalyzer(plugin, method, context.Payload)
                : context => InvokeAttributeAnalyzer(plugin, method, context.GetDto());

            return RegisterAnalyzerCore(
                plugin,
                analyzer.Kind,
                analyzer.EndpointType,
                payloadType,
                analyzer.Priority,
                handler,
                source,
                method,
                attribute: analyzer);
        }

        static Type ValidateAttributeAnalyzerSignature(IPlugin plugin, MethodInfo method, AnalyzerAttribute analyzer)
        {
            var parameters = method.GetParameters();
            if (parameters.Length != 1 || method.ReturnType != typeof(ValueTask))
                throw AnalyzerRegistrationException(
                    plugin,
                    method,
                    analyzer.EndpointType,
                    analyzer.Kind,
                    "<signature>",
                    "ValueTask analyzer(TPayload payload)",
                    DescribeAnalyzerSignature(method));

            return parameters[0].ParameterType;
        }

        static AnalyzerRegistration RegisterAnalyzerCore(
            IPlugin plugin,
            AnalyzerKind kind,
            Type endpointType,
            Type payloadType,
            int priority,
            Func<AnalyzerDispatchContext, ValueTask> handler,
            string source,
            MethodInfo? method,
            AnalyzerAttribute? attribute = null)
        {
            if (!GameEndpointCatalog.ByEndpointType.TryGetValue(endpointType, out var endpoint))
                throw AnalyzerRegistrationException(
                    plugin,
                    method,
                    endpointType,
                    kind,
                    "<signature>",
                    "catalog endpoint",
                    $"未在 {nameof(GameEndpointCatalog)}.{nameof(GameEndpointCatalog.ByEndpointType)} 注册");

            var expected = payloadType == typeof(byte[])
                ? typeof(byte[])
                : kind == AnalyzerKind.Request
                    ? endpoint.RequestType
                    : endpoint.ResponseType;
            if (payloadType != expected)
                throw AnalyzerRegistrationException(
                    plugin,
                    method,
                    endpointType,
                    kind,
                    payloadType == typeof(byte[]) ? "raw" : "DTO",
                    expected.FullName ?? expected.Name,
                    method is null
                        ? payloadType.FullName ?? payloadType.Name
                        : DescribeAnalyzerSignature(method));

            return new(
                plugin,
                method,
                endpointType,
                kind,
                priority,
                handler,
                attribute is null ? source : $"{source} [{attribute.GetType().Name}]");
        }

        static string DescribeAnalyzerSignature(MethodInfo method)
        {
            var parameters = method.GetParameters();
            var parameterText = parameters.Length == 0
                ? "<none>"
                : string.Join(", ", parameters.Select(p => p.ParameterType.FullName ?? p.ParameterType.Name));
            var asyncVoid = method.GetCustomAttribute<AsyncStateMachineAttribute>() is null ? string.Empty : ", async-state-machine";
            return $"return={method.ReturnType.FullName ?? method.ReturnType.Name}, parameters=({parameterText}){asyncVoid}";
        }

        static ValueTask InvokeAttributeAnalyzer(IPlugin plugin, MethodInfo method, object payload)
        {
            var target = method.IsStatic ? null : plugin;
            return (ValueTask)method.Invoke(target, [payload])!;
        }

        static void CommitAnalyzerRegistration(AnalyzerRegistration registration)
        {
            lock (AnalyzerGate)
            {
                var dict = registration.Kind == AnalyzerKind.Response ? ResponseAnalyzerMethods : RequestAnalyzerMethods;
                if (!dict.TryGetValue(registration.Priority, out var list))
                {
                    list = [];
                    dict[registration.Priority] = list;
                }

                list.Add(registration);
            }
        }

        internal static void RemoveAnalyzerRegistration(AnalyzerRegistration registration)
        {
            lock (AnalyzerGate)
                RemoveAnalyzerRegistrationLocked(registration);
        }

        static void RemoveAnalyzerRegistrationLocked(AnalyzerRegistration registration)
        {
            var dict = registration.Kind == AnalyzerKind.Response ? ResponseAnalyzerMethods : RequestAnalyzerMethods;
            if (!dict.TryGetValue(registration.Priority, out var list))
                return;

            list.Remove(registration);
            if (list.Count == 0)
                dict.Remove(registration.Priority);
        }

        internal static List<AnalyzerRegistration> SnapshotAnalyzerRegistrations(AnalyzerKind kind, Type endpointType)
        {
            lock (AnalyzerGate)
            {
                var dict = kind == AnalyzerKind.Request ? RequestAnalyzerMethods : ResponseAnalyzerMethods;
                return dict
                    .SelectMany(x => x.Value)
                    .Where(x => x.EndpointType == endpointType)
                    .ToList();
            }
        }

        static InvalidOperationException AnalyzerRegistrationException(
            IPlugin plugin,
            MethodInfo? method,
            Type endpointType,
            AnalyzerKind kind,
            string payload,
            string expected,
            string actual)
        {
            var methodName = method is null
                ? "<programmatic>"
                : $"{method.DeclaringType?.FullName}.{method.Name}";
            return new InvalidOperationException(
                $"插件 analyzer 签名无效: plugin={plugin.Name} ({InternalName(plugin)}), " +
                $"method={methodName}, endpoint={endpointType.FullName}, kind={kind}, payload={payload}, " +
                $"expected={expected}, actual={actual}");
        }

        /// <summary>
        /// 调用插件 Initialize，并把期间注册的快捷键归属到该插件实例。
        /// 初始批量加载（Program.cs）与热重载都经此入口，保证 owner 标记一致。
        /// </summary>
        internal static void InitializePlugin(IPlugin plugin)
        {
            using (KeyboardManager.RegisterScope(plugin))
            {
                var factory = GetLiveDisplayFactory();
                var liveDisplay = factory(plugin);
                using var callback = EnterPluginCallbackScope();
                InvokeContextInitialize(plugin, new PluginContext(plugin, liveDisplay, HostEvents));
            }
        }

        internal static void InitializeLoadedPlugins()
        {
            foreach (var plugin in LoadedPlugins.ToList())
                TryInitializePlugin(plugin, removeFromLoadedPlugins: true, disposeOnFailure: true);
        }

        static bool TryInitializePlugin(IPlugin plugin, bool removeFromLoadedPlugins, bool disposeOnFailure)
        {
            _ = GetLiveDisplayFactory();
            try
            {
                InitializePlugin(plugin);
                return true;
            }
            catch (Exception ex)
            {
                var failedPlugin = FailedPluginPath(plugin);
                LiveDisplayConsole.LogException("Plugin", PluginInitializeException(plugin, failedPlugin, ex));
                if (!FailedPlugins.Contains(failedPlugin))
                    FailedPlugins.Add(failedPlugin);
                CleanupPluginAfterInitializationFailure(plugin, removeFromLoadedPlugins, disposeOnFailure);
                return false;
            }
        }

        static Func<IPlugin, ILiveDisplayOutput> GetLiveDisplayFactory()
            => liveDisplayFactory ?? throw new InvalidOperationException("插件初始化前必须先绑定 LiveDisplay。");

        static string FailedPluginPath(IPlugin plugin)
        {
            var internalName = InternalName(plugin);
            return Metadatas.TryGetValue(internalName, out var metadata)
                ? metadata.FilePath
                : plugin.Name;
        }

        static InvalidOperationException PluginInitializeException(IPlugin plugin, string failedPlugin, Exception inner)
            => new(
                $"插件初始化失败: plugin={plugin.Name} ({InternalName(plugin)})",
                inner);

        static void CleanupPluginAfterInitializationFailure(IPlugin plugin, bool removeFromLoadedPlugins, bool dispose)
        {
            if (removeFromLoadedPlugins)
                LoadedPlugins.Remove(plugin);

            RemoveAnalyzerMethods(plugin);
            foreach (var route in RemoveRoutes(plugin))
                route.WaitForIdle();
            DisposeHostEventSubscriptions(plugin);
            KeyboardManager.UnregisterByOwner(plugin);

            if (!dispose)
                return;

            try { plugin.Dispose(); }
            catch (Exception ex) { LiveDisplayConsole.LogException("Plugin", ex); }
        }

        static void InvokeContextInitialize(IPlugin plugin, IPluginContext context)
        {
            var method = plugin.GetType().GetMethod(
                nameof(IPlugin.Initialize),
                BindingFlags.Instance | BindingFlags.Public,
                [typeof(IPluginContext)]);
            if (method is null || method.DeclaringType == typeof(IPlugin))
                throw new InvalidOperationException(
                    $"插件必须实现 Initialize(IPluginContext context): plugin={plugin.Name} ({InternalName(plugin)})");

            if (method.ReturnType != typeof(void))
                throw new InvalidOperationException(
                    $"插件 Initialize 签名无效: plugin={plugin.Name} ({InternalName(plugin)}), " +
                    $"method={method.DeclaringType?.FullName}.{method.Name}, expected=void, actual={method.ReturnType.FullName}");

            try
            {
                method.Invoke(plugin, [context]);
            }
            catch (TargetInvocationException ex) when (ex.InnerException is not null)
            {
                ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
                throw;
            }
        }

        internal static Task TriggerStartedAsync(CancellationToken cancellationToken = default)
            => HostEvents.TriggerStartedAsync(cancellationToken: cancellationToken);

        internal static Task TriggerStartedForPluginsAsync(IEnumerable<IPlugin> plugins, CancellationToken cancellationToken = default)
            => HostEvents.TriggerStartedAsync(plugins, cancellationToken);

        internal static void DisposeHostEventSubscriptions(IPlugin plugin)
            => HostEvents.DisposeFor(plugin);

        internal static Action DisposeHostEventSubscriptionsLater(IPlugin plugin)
            => HostEvents.DisposeForLater(plugin);

        internal static void ClearHostEventSubscriptions()
            => HostEvents.Clear();

        // ── 热重载 ───────────────────────────────────────────────────────────

        /// <summary>
        /// 批量应用插件重载，返回仍需重启才能生效的插件名。
        /// 新安装的共享上下文成员会先把已加载的同组插件排入重载顺序，避免共享锚点被加载进两个 ALC。
        /// </summary>
        public static IReadOnlyList<string> ReloadPlugins(params string[] pluginNames)
        {
            var requested = pluginNames.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (requested.Count == 0) return [];

            using var transaction = EnterReloadTransaction();
            var scanned = ScanPluginMetadata();
            var pendingUnloads = new List<PendingPluginUnload>();
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
                        ReloadPluginLocked(name, scanned, outcomes, pendingUnloads);
                    }
                    catch (Exception ex)
                    {
                        LiveDisplayConsole.LogException("Plugin", ex);
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
                CompletePendingUnloads(pendingUnloads);
            }

            return needRestart;
        }

        static IDisposable EnterReloadTransaction()
        {
            if (PluginCallbackDepth.Value != 0)
                throw new InvalidOperationException("插件回调内禁止执行热重载；请在当前回调返回后由宿主侧重新发起 reload。");

            if (Interlocked.CompareExchange(ref reloadTransactionActive, 1, 0) != 0)
                throw new InvalidOperationException("已有插件热重载事务正在运行，拒绝并发或重入 reload。");

            return new ReloadTransaction();
        }

        sealed class ReloadTransaction : IDisposable
        {
            public void Dispose()
                => Volatile.Write(ref reloadTransactionActive, 0);
        }

        internal static IDisposable EnterPluginCallbackScope()
        {
            PluginCallbackDepth.Value++;
            return new PluginCallbackScope();
        }

        sealed class PluginCallbackScope : IDisposable
        {
            bool disposed;

            public void Dispose()
            {
                if (disposed)
                    return;

                PluginCallbackDepth.Value--;
                disposed = true;
            }
        }

        static Dictionary<string, PluginMetadata> ScanPluginMetadata()
        {
            Dictionary<string, PluginMetadata> scanned = [];
            Dictionary<string, PluginMetadata> assemblies = [];
            ScanAll(scanned, assemblies);
            ReplaceAssemblyMetadatas(assemblies);
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
            List<PendingPluginUnload> pendingUnloads)
        {
            // 同批次内该插件已随所属组一并处理过 → 复用既得结果，避免整组被二次卸载/重载
            if (outcomes.TryGetValue(pluginName, out var prior)) return prior;

            var existing = Metadatas.GetValueOrDefault(pluginName);
            var affectedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { pluginName };

            // 约束1：已加载且为 LoadInHost → 进了 Default ALC，永不可卸载
            if (existing is { LoadInHost: true })
            {
                LiveDisplayConsole.MarkupLog("Plugin", $"[yellow]插件 {pluginName.EscapeMarkup()} 在宿主上下文加载，不支持热重载，请重启。[/]", LiveDisplaySeverity.Warning);
                return outcomes[pluginName] = false;
            }

            var group = existing is null ? null : ContextGroups.FirstOrDefault(g => g.Contains(pluginName, StringComparer.OrdinalIgnoreCase));
            if (group != null)
                affectedNames.UnionWith(group);

            if (affectedNames.Any(name => scanned.TryGetValue(name, out var m) && m.LoadInHost))
            {
                LiveDisplayConsole.MarkupLog("Plugin", $"[yellow]插件 {pluginName.EscapeMarkup()} 在宿主上下文加载，不支持热重载，请重启。[/]", LiveDisplaySeverity.Warning);
                foreach (var name in affectedNames)
                    outcomes[name] = false;
                return false;
            }

            // 已加载的上下文组 → 先按整组卸载（含约束检查，如组内有注册了不可移除资源的插件则拒绝），失败则中止。
            // 整组成员都记入结果：组内其它插件若也在本批次，命中上面的复用短路后跳过重复卸载/重载。
            if (group != null)
            {
                if (Contexts.ContainsKey(GroupKey(group)) && !TryUnloadGroup(group, pendingUnloads))
                {
                    foreach (var name in group) outcomes[name] = false;
                    return false;
                }
                CompletePendingUnloadsOutsideReloadLock(pendingUnloads);
            }

            // 重新扫描磁盘，补回所有"当前未加载"的插件元数据（含刚卸载的、以及全新增的）
            MergeUnloadedMetadatas(scanned);

            if (!Metadatas.ContainsKey(pluginName))
            {
                // 文件已被删除：卸载即完成
                LiveDisplayConsole.MarkupLog("Plugin", $"[yellow]插件 {pluginName.EscapeMarkup()} 的文件已不存在，已卸载。[/]", LiveDisplaySeverity.Warning);
                BuildGroups();
                LoadAffectedGroups(affectedNames, outcomes, pendingUnloads);
                return outcomes[pluginName] = true;
            }

            // 新元数据若为 LoadInHost（如刚加上该特性），不走热重载路径
            if (Metadatas[pluginName].LoadInHost)
            {
                LiveDisplayConsole.MarkupLog("Plugin", $"[yellow]插件 {pluginName.EscapeMarkup()} 在宿主上下文加载，不支持热重载，请重启。[/]", LiveDisplaySeverity.Warning);
                return outcomes[pluginName] = false;
            }

            BuildGroups();
            LoadAffectedGroups(affectedNames, outcomes, pendingUnloads);

            var loaded = IsPluginLoaded(pluginName);
            if (loaded)
                LiveDisplayConsole.MarkupLog("Plugin", $"[green]插件 {pluginName.EscapeMarkup()} 已重载。[/]", LiveDisplaySeverity.Success);
            return outcomes[pluginName] = loaded;
        }

        /// <summary>加载受本轮重载影响且尚无 ALC 的上下文组，对新实例调用 Initialize，并补发一次启动事件。</summary>
        static void LoadAffectedGroups(
            IEnumerable<string> affectedNames,
            Dictionary<string, bool> outcomes,
            List<PendingPluginUnload> pendingUnloads)
        {
            var affected = affectedNames.ToHashSet(StringComparer.OrdinalIgnoreCase);
            var pendingGroups = ContextGroups
                .Where(group => group.Any(affected.Contains) && !Contexts.ContainsKey(GroupKey(group)))
                .ToList();
            foreach (var group in pendingGroups)
            {
                // 清掉本组上次的加载失败记录，避免修好后重载仍显示"加载失败"
                foreach (var name in group)
                    if (Metadatas.TryGetValue(name, out var m))
                        FailedPlugins.Remove(m.FilePath);

                var staged = StageGroupLoad(group);
                if (staged is null)
                {
                    foreach (var name in group.Where(Metadatas.ContainsKey))
                        outcomes[name] = false;
                    continue;
                }

                if (Server.IsRunning)
                {
                    var initialized = true;
                    RunOutsideReloadWriteLock(() => initialized = InitializeStagedPlugins(staged));
                    if (!initialized)
                    {
                        foreach (var name in group.Where(Metadatas.ContainsKey))
                            outcomes[name] = false;
                        RunOutsideReloadWriteLock(() => DisposeStagedGroup(staged));
                        continue;
                    }
                }

                try
                {
                    CommitStagedGroupLoad(staged);
                }
                catch
                {
                    foreach (var name in group.Where(Metadatas.ContainsKey))
                        outcomes[name] = false;
                    RunOutsideReloadWriteLock(() => DisposeStagedGroup(staged));
                    throw;
                }

                if (Server.IsRunning && staged.Plugins.Count != 0)
                {
                    RunOutsideReloadWriteLock(() =>
                        TriggerStartedForPluginsAsync(staged.Plugins.Select(x => x.Plugin)).GetAwaiter().GetResult());
                }

                foreach (var name in group.Where(Metadatas.ContainsKey))
                    outcomes[name] = IsPluginLoaded(name);
            }
        }

        static StagedGroupLoad? StageGroupLoad(HashSet<string> group)
        {
            var missing = group.Where(name => !Metadatas.ContainsKey(name)).ToList();
            if (missing.Count != 0)
            {
                foreach (var name in group)
                    if (Metadatas.TryGetValue(name, out var present))
                    {
                        LiveDisplayConsole.MarkupLog("Plugin", $"[red]插件 {name.EscapeMarkup()} 加载失败[/]:依赖的共享上下文插件 {string.Join("、", missing).EscapeMarkup()} 未安装。", LiveDisplaySeverity.Error);
                        if (!FailedPlugins.Contains(present.FilePath)) FailedPlugins.Add(present.FilePath);
                    }
                return null;
            }

            var key = GroupKey(group);
            var ctx = new PluginLoadContext(key);
            var staged = new StagedGroupLoad(group, key, ctx, [], []);

            foreach (var name in group)
            {
                if (StageIntoContext(staged, Metadatas[name]))
                    continue;

                DisposeStagedGroup(staged);
                return null;
            }

            return staged;
        }

        static bool StageIntoContext(StagedGroupLoad staged, PluginMetadata metadata)
        {
            IPlugin? plugin = null;
            var phase = "读取插件程序集";
            try
            {
                using var stream = CreateStream(metadata);
                var assembly = staged.Context.LoadFromStream(stream);
                if (assembly.GetName().Name is { } assemblyName)
                {
                    staged.Context.LocalAssemblyMap[assemblyName] = assembly;
                    staged.Assemblies.Add(new(assemblyName, assembly));
                }

                phase = "读取插件导出类型";
                var type = assembly.GetExportedTypes().FirstOrDefault(x => typeof(IPlugin).IsAssignableFrom(x));
                if (type is null)
                {
                    LiveDisplayConsole.MarkupLog("Plugin", $"[red]插件 {metadata.PluginName.EscapeMarkup()} 加载失败[/]: 未找到实现 {nameof(IPlugin)} 的公开类型。", LiveDisplaySeverity.Error);
                    FailedPlugins.Add(metadata.FilePath);
                    return false;
                }

                phase = "创建插件实例";
                if (Activator.CreateInstance(type) is not IPlugin createdPlugin)
                {
                    LiveDisplayConsole.MarkupLog("Plugin", $"[red]插件 {metadata.PluginName.EscapeMarkup()} 加载失败[/]: 无法创建插件实例。type={(type.FullName ?? type.Name).EscapeMarkup()}", LiveDisplaySeverity.Error);
                    FailedPlugins.Add(metadata.FilePath);
                    return false;
                }
                plugin = createdPlugin;

                if (plugin.Targets.Length != 0 && !plugin.Targets.Intersect(Config.Repository.Targets).Any() && Config.Repository.Targets.Count != 0)
                    return true;

                phase = "注册插件入口";
                staged.Plugins.Add(new(plugin, CreateRegistrationPlan(plugin)));
                return true;
            }
            catch (Exception ex)
            {
                if (plugin is not null)
                {
                    RemoveAnalyzerMethods(plugin);
                    DisposeHostEventSubscriptions(plugin);
                    KeyboardManager.UnregisterByOwner(plugin);
                    try { plugin.Dispose(); }
                    catch (Exception disposeEx) { LiveDisplayConsole.LogException("Plugin", disposeEx); }
                }

                LiveDisplayConsole.LogException("Plugin", PluginLoadException(metadata, phase, ex));
                FailedPlugins.Add(metadata.FilePath);
                return false;
            }
        }

        static bool InitializeStagedPlugins(StagedGroupLoad staged)
        {
            foreach (var plugin in staged.Plugins)
                if (!TryInitializePlugin(plugin.Plugin, removeFromLoadedPlugins: false, disposeOnFailure: false))
                    return false;

            return true;
        }

        static void CommitStagedGroupLoad(StagedGroupLoad staged)
        {
            var committedPlugins = new List<IPlugin>();
            try
            {
                foreach (var assembly in staged.Assemblies)
                {
                    AssemblyMap[assembly.Name] = assembly.Assembly;
                    Assemblies.Add(assembly.Assembly);
                }

                foreach (var plugin in staged.Plugins)
                {
                    CommitRegistrationPlan(plugin.Plan);
                    LoadedPlugins.Add(plugin.Plugin);
                    committedPlugins.Add(plugin.Plugin);
                }

                Contexts[staged.Key] = staged.Context;
            }
            catch
            {
                foreach (var plugin in committedPlugins)
                    LoadedPlugins.Remove(plugin);

                foreach (var plugin in staged.Plugins)
                {
                    RemoveAnalyzerMethods(plugin.Plugin);
                    RemoveRoutes(plugin.Plugin);
                    DisposeHostEventSubscriptions(plugin.Plugin);
                    KeyboardManager.UnregisterByOwner(plugin.Plugin);
                }

                foreach (var assembly in staged.Assemblies)
                {
                    Assemblies.Remove(assembly.Assembly);
                    AssemblyMap.Remove(assembly.Name);
                }
                Contexts.Remove(staged.Key);
                throw;
            }
        }

        static void DisposeStagedGroup(StagedGroupLoad staged)
        {
            foreach (var plugin in staged.Plugins)
            {
                RemoveAnalyzerMethods(plugin.Plugin);
                DisposeHostEventSubscriptions(plugin.Plugin);
                KeyboardManager.UnregisterByOwner(plugin.Plugin);
                try { plugin.Plugin.Dispose(); }
                catch (Exception ex) { LiveDisplayConsole.LogException("Plugin", ex); }
            }

            staged.Context.Unload();
        }

        static void RunOutsideReloadWriteLock(Action action)
        {
            ReloadLock.ExitWriteLock();
            try
            {
                action();
            }
            finally
            {
                ReloadLock.EnterWriteLock();
            }
        }

        static void CompletePendingUnloadsOutsideReloadLock(List<PendingPluginUnload> pendingUnloads)
        {
            if (pendingUnloads.Count == 0)
                return;

            RunOutsideReloadWriteLock(() =>
            {
                CompletePendingUnloads(pendingUnloads);
                pendingUnloads.Clear();
            });
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
        static bool TryUnloadGroup(HashSet<string> group, List<PendingPluginUnload> pendingUnloads)
        {
            // 约束1：组内任一插件 LoadInHost → 整组进了 Default ALC，永不可卸载
            if (group.Any(n => Metadatas.TryGetValue(n, out var m) && m.LoadInHost))
            {
                LiveDisplayConsole.MarkupLog("Plugin", $"[yellow]插件 {string.Join("、", group).EscapeMarkup()} 在宿主上下文加载，不支持热重载，请重启。[/]", LiveDisplaySeverity.Warning);
                return false;
            }

            var key = GroupKey(group);
            // 斩断引用 + Unload，放在不内联方法里：其所有局部（ALC/Assembly/插件实例）随返回离开作用域，
            // 运行时方能在后续 GC 时真正回收该 ALC。
            // 这里【刻意不】同步校验 ALC 是否已回收：ALC 卸载是异步的，且在 reload 调用栈尚未展开时做 GC 自检，
            // 会被运行时的保守栈扫描误判为"仍存活"（其实卸载已成功，栈展开后即被回收）。卸载的实证由集成测试
            // HotReloadTests 在调用栈完全展开后用 WeakReference 完成。若将来要在运行期检出真正的泄漏，
            // 应改为延迟校验：把弱引用记下来，到【下一次】reload（上一次的栈已展开）时再 GC + 检查。
            pendingUnloads.Add(CutReferencesForUnload(group, key));
            return true;
        }

        // 不内联：保证返回后栈帧里不残留对插件 ALC/Assembly/实例的强引用，运行时方能在后续 GC 时回收该 ALC
        [MethodImpl(MethodImplOptions.NoInlining)]
        static PendingPluginUnload CutReferencesForUnload(HashSet<string> group, string key)
        {
            var ctx = Contexts[key];

            // 组内主程序集集合：用于快捷键按程序集兜底清扫
            var groupAssemblies = group
                .Select(n => AssemblyMap.GetValueOrDefault(n))
                .Where(a => a != null)
                .Cast<Assembly>()
                .ToHashSet();

            var plugins = new List<IPlugin>();
            var routes = new List<RouteRegistration>();
            var eventWaits = new List<Action>();

            // 逐插件实例斩断引用；耗时等待和 Dispose 放到写锁外执行，避免 route/event handler 反查宿主状态时死锁
            foreach (var plugin in LoadedPlugins.Where(p => group.Contains(InternalName(p))).ToList())
            {
                LoadedPlugins.Remove(plugin);
                RemoveAnalyzerMethods(plugin);
                routes.AddRange(RemoveRoutes(plugin));
                eventWaits.Add(DisposeHostEventSubscriptionsLater(plugin));
                plugins.Add(plugin);
            }

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

            return new(ctx, plugins, routes, eventWaits, groupAssemblies);
        }

        static void CompletePendingUnloads(List<PendingPluginUnload> pendingUnloads)
        {
            if (pendingUnloads.Count == 0) return;

            foreach (var unload in pendingUnloads)
            {
                foreach (var route in unload.Routes)
                    route.WaitForIdle();

                foreach (var wait in unload.EventWaits)
                    wait();

                foreach (var plugin in unload.Plugins)
                {
                    try { plugin.Dispose(); }
                    catch (Exception ex) { LiveDisplayConsole.LogException("Plugin", ex); }

                    KeyboardManager.UnregisterByOwner(plugin);
                }

                // 兜底清扫遗漏的快捷键（如未走 owner 作用域的）
                KeyboardManager.ClearHandlersByAssembly(unload.Assemblies);
                unload.Context.Unload();
            }
        }

        static void RemoveAnalyzerMethods(IPlugin plugin)
        {
            lock (AnalyzerGate)
            {
                foreach (var dict in (SortedDictionary<int, List<AnalyzerRegistration>>[])[RequestAnalyzerMethods, ResponseAnalyzerMethods])
                {
                    foreach (var priority in dict.Keys.ToList())
                    {
                        var list = dict[priority];
                        list.RemoveAll(x => ReferenceEquals(x.Plugin, plugin));
                        if (list.Count == 0) dict.Remove(priority);
                    }
                }
            }
        }

        static List<RouteRegistration> RemoveRoutes(IPlugin plugin)
        {
            if (!PluginRoutes.TryGetValue(plugin, out var routes)) return [];
            foreach (var route in routes)
            {
                route.MarkRemoved();
                // StaticRouteManager.Remove 按 (method, path) 精确移除，清掉钉住插件程序集的 handler 闭包
                if (Server.Instance.Routes.PreAuthentication.Static.Exists(route.Method, route.Path))
                    Server.Instance.Routes.PreAuthentication.Static.Remove(route.Method, route.Path);
            }
            PluginRoutes.Remove(plugin);
            return routes;
        }

        internal class PluginLoadContext(string name) : AssemblyLoadContext(name, true)
        {
            internal Dictionary<string, Assembly> LocalAssemblyMap { get; } = new(StringComparer.Ordinal);

            protected override Assembly? Load(AssemblyName name)
            {
                if (ResolveSharedAssembly(name) is { } sharedAssembly)
                    return sharedAssembly;

                if (name.Name != null && LocalAssemblyMap.TryGetValue(name.Name, out var local))
                    return local;

                if (name.Name != null && AssemblyMap.TryGetValue(name.Name, out var shared))
                    return shared;

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

                if (name.Name == null || !TryGetAssemblyMetadata(name.Name, out var dep)) return null;

                using var stream = CreateStream(dep);
                var assembly = LoadFromStream(stream);
                if (assembly.GetName().Name is { } assemblyName)
                    LocalAssemblyMap[assemblyName] = assembly;
                return assembly;
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
