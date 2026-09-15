using System.IO.Compression;
using System.Reflection;
using System.Runtime.Loader;
using System.Security.Cryptography;
using UmamusumeResponseAnalyzer.TerminalGui;

namespace UmamusumeResponseAnalyzer.Plugin;

internal static partial class PluginManager
{
    internal static void LoadMetadatas()
    {
        LifecycleMetadatas.Clear();
        foreach (var (name, metadata) in ScanPluginMetadata())
            LifecycleMetadatas.Add(name, metadata);
    }

    static Dictionary<string, PluginMetadata> ScanPluginMetadata(bool reportFailures = true)
    {
        var scanned = new Dictionary<string, PluginMetadata>(StringComparer.OrdinalIgnoreCase);
        var conflictedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var pluginsDir = new DirectoryInfo("Plugins");
        if (!pluginsDir.Exists)
            return scanned;

        foreach (var zip in pluginsDir.GetFiles("*.zip", SearchOption.TopDirectoryOnly)
                     .OrderBy(file => file.Name, StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                var metadata = ReadPluginPackage(zip.FullName, LanguageConfig.GetCulture());
                if (conflictedNames.Contains(metadata.PluginName))
                    throw new InvalidDataException(
                        $"插件 InternalName 冲突（OrdinalIgnoreCase）: {metadata.PluginName}");
                if (scanned.TryGetValue(metadata.PluginName, out var existing))
                {
                    scanned.Remove(metadata.PluginName);
                    conflictedNames.Add(metadata.PluginName);
                    if (reportFailures && !LifecycleFailedPlugins.Contains(existing.FilePath))
                        LifecycleFailedPlugins.Add(existing.FilePath);
                    throw new InvalidDataException(
                        $"插件 InternalName 冲突（OrdinalIgnoreCase）: " +
                        $"{existing.PluginName} ({existing.FilePath}) / " +
                        $"{metadata.PluginName} ({metadata.FilePath})");
                }
                scanned.Add(metadata.PluginName, metadata);
            }
            catch (Exception ex)
            {
                if (!reportFailures)
                    continue;

                ReportPluginDiagnostic(new InvalidDataException($"插件包无效: package={zip.FullName}", ex));
                if (!LifecycleFailedPlugins.Contains(zip.FullName))
                    LifecycleFailedPlugins.Add(zip.FullName);
            }
        }

        return scanned;
    }

    static PluginMetadata ReadPluginPackage(string packagePath, string culture)
    {
        var package = PluginPackageValidator.Validate(packagePath, requireMatchingPackageFileName: true);
        var manifest = package.Manifest;
        var satellites = package.Entries
            .Where(entry => entry.EndsWith(".resources.dll", StringComparison.OrdinalIgnoreCase) &&
                            entry.Split('/').Contains(culture, StringComparer.OrdinalIgnoreCase))
            .ToArray();

        return new(
            packagePath,
            package.MainAssemblyEntry,
            manifest.InternalName,
            manifest.DisplayName,
            manifest.Author,
            manifest.Version,
            manifest.Dependencies,
            manifest.Targets,
            package.Assemblies,
            satellites);
    }

    static string ResolvePluginName(string pluginName, params IEnumerable<string>[] sources)
    {
        foreach (var source in sources)
            if (source.FirstOrDefault(candidate =>
                    string.Equals(candidate, pluginName, StringComparison.OrdinalIgnoreCase)) is { } match)
                return match;

        return pluginName;
    }

    internal static void BuildGroups()
    {
        _ = TopologicalOrder(LifecycleMetadatas.Keys);

        var adjacency = LifecycleMetadatas.Keys.ToDictionary(
            name => name,
            _ => new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            StringComparer.OrdinalIgnoreCase);
        foreach (var metadata in LifecycleMetadatas.Values)
            foreach (var dependency in metadata.Dependencies)
            {
                if (!LifecycleMetadatas.ContainsKey(dependency))
                    continue;
                adjacency[metadata.PluginName].Add(dependency);
                adjacency[dependency].Add(metadata.PluginName);
            }

        LifecycleContextGroups.Clear();
        var remaining = LifecycleMetadatas.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        while (remaining.Count != 0)
        {
            var root = remaining.Min(StringComparer.OrdinalIgnoreCase)!;
            var group = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            Queue<string> queue = new([root]);
            remaining.Remove(root);
            while (queue.TryDequeue(out var name))
            {
                group.Add(name);
                foreach (var neighbor in adjacency[name].Order(StringComparer.OrdinalIgnoreCase))
                    if (remaining.Remove(neighbor))
                        queue.Enqueue(neighbor);
            }
            LifecycleContextGroups.Add(group);
        }
    }

    static IReadOnlyList<string> TopologicalOrder(IEnumerable<string> names)
        => TopologicalOrder(LifecycleMetadatas, names);

    static IReadOnlyList<string> TopologicalOrder(
        IReadOnlyDictionary<string, PluginMetadata> source,
        IEnumerable<string> names)
    {
        var scope = names.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var name in scope)
        {
            if (!source.TryGetValue(name, out var metadata))
                throw new InvalidDataException($"插件依赖图包含未安装插件: {name}");
        }

        var state = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        List<string> ordered = [];

        void Visit(string name, List<string> path)
        {
            if (state.TryGetValue(name, out var current))
            {
                if (current == 2)
                    return;
                if (current == 1)
                    throw new InvalidDataException(
                        $"插件 manifest Dependencies 存在循环: {string.Join(" -> ", [.. path, name])}");
            }

            state[name] = 1;
            path.Add(name);
            if (!source.TryGetValue(name, out var metadata))
                throw new InvalidDataException($"插件依赖图包含未安装插件: {name}");

            foreach (var dependency in metadata.Dependencies.Where(scope.Contains))
                Visit(dependency, path);
            path.RemoveAt(path.Count - 1);
            state[name] = 2;
            ordered.Add(name);
        }

        foreach (var name in source.Keys.Where(scope.Contains))
            Visit(name, []);
        return ordered;
    }

    static string GroupKey(IEnumerable<string> group)
        => string.Join("&", group.Order(StringComparer.OrdinalIgnoreCase));

    internal static void LoadGroup(HashSet<string> group)
    {
        var ordered = TopologicalOrder(group);
        var key = GroupKey(group);
        var createdContext = !LifecycleContexts.TryGetValue(key, out var context);
        context ??= new PluginLoadContext(key, ordered.Select(name => LifecycleMetadatas[name]));

        var loadedBefore = LifecycleLoadedPlugins.ToHashSet<IPlugin>(ReferenceEqualityComparer.Instance);
        foreach (var name in ordered)
        {
            var metadata = LifecycleMetadatas[name];
            var failure = LoadIntoContextAsync(context, metadata, LifecycleLoadedPlugins).GetAwaiter().GetResult();
            if (failure is null)
                continue;

            if (createdContext)
            {
                var cleanupFailures = RollBackGroupLoad(group, context, loadedBefore);
                if (cleanupFailures.Count != 0)
                    failure = new AggregateException(
                        "插件组加载及清理失败。",
                        [failure, .. cleanupFailures]);
            }

            ReportPluginDiagnostic(failure);
            if (!LifecycleFailedPlugins.Contains(metadata.FilePath))
                LifecycleFailedPlugins.Add(metadata.FilePath);
            return;
        }

        if (createdContext)
            LifecycleContexts[key] = context;
    }

    internal static void LoadPlugins()
    {
        foreach (var group in LifecycleContextGroups.OrderBy(GroupKey, StringComparer.OrdinalIgnoreCase))
            LoadGroup(group);
    }

    static async Task<Exception?> LoadIntoContextAsync(
        AssemblyLoadContext context,
        PluginMetadata metadata,
        List<IPlugin> plugins)
    {
        IPlugin? plugin = null;
        var phase = "读取插件程序集";
        try
        {
            using var stream = CreateStream(metadata);
            var assembly = context.LoadFromStream(stream);
            var assemblyName = assembly.GetName().Name;
            PluginPackageValidator.ValidateAssemblyIdentity(assemblyName, metadata.PluginName);

            if (!ShouldLoadPluginForCurrentTargets(metadata))
                return null;

            phase = "读取插件导出类型";
            var type = assembly.GetExportedTypes().FirstOrDefault(candidate =>
                typeof(IPlugin).IsAssignableFrom(candidate) && !candidate.IsAbstract);
            if (type is null)
                throw new InvalidDataException($"未找到实现 {nameof(IPlugin)} 的公开具体类型。");

            phase = "创建插件实例";
            plugin = Activator.CreateInstance(type) as IPlugin
                     ?? throw new InvalidDataException($"无法创建插件实例: type={type.FullName ?? type.Name}");
            _ = GenerationFor(plugin);

            plugins.Add(plugin);
            return null;
        }
        catch (Exception ex)
        {
            Exception failure = PluginLoadException(metadata, phase, ex);
            if (plugin is not null)
            {
                try { await CompleteFailedPluginLoadAsync(plugin, flush: false).ConfigureAwait(false); }
                catch (Exception cleanupEx)
                {
                    failure = new AggregateException(
                        "插件加载及清理失败。",
                        failure,
                        cleanupEx);
                }
            }

            return failure;
        }
    }

    static bool ShouldLoadPluginForCurrentTargets(PluginMetadata metadata)
        => metadata.Targets.Count == 0 ||
           metadata.Targets.Intersect(Config.Repository.Targets, StringComparer.OrdinalIgnoreCase).Any() ||
           Config.Repository.Targets.Count == 0;

    static InvalidOperationException PluginLoadException(PluginMetadata metadata, string phase, Exception inner)
        => new($"插件加载失败: plugin={metadata.PluginName}, phase={phase}", inner);

    static List<Exception> RollBackGroupLoad(
        HashSet<string> group,
        PluginLoadContext context,
        HashSet<IPlugin> loadedBefore)
    {
        List<Exception> failures = [];
        foreach (var plugin in LifecycleLoadedPlugins
                     .Where(candidate => !loadedBefore.Contains(candidate) && group.Contains(InternalName(candidate)))
                     .Reverse()
                     .ToList())
        {
            try { CompleteFailedPluginLoadAsync(plugin, flush: false).GetAwaiter().GetResult(); }
            catch (Exception ex) { failures.Add(ex); }
        }

        try { context.Unload(); }
        catch (Exception ex) { failures.Add(ex); }
        return failures;
    }

    internal static Assembly? ResolveSharedAssembly(AssemblyName requested)
    {
        if (requested.Name is not { } name || !SharedAssemblyNames.Contains(name))
            return null;

        var shared = AssemblyLoadContext.Default.Assemblies.FirstOrDefault(assembly =>
            string.Equals(assembly.GetName().Name, name, StringComparison.Ordinal));
        if (shared is null)
        {
            try
            {
                shared = AssemblyLoadContext.Default.LoadFromAssemblyName(requested);
            }
            catch (Exception ex)
            {
                throw new FileLoadException(
                    $"shared ABI assembly {requested.FullName} 必须由 Default ALC 加载，但宿主无法加载。",
                    requested.FullName,
                    ex);
            }
        }

        ValidateSharedAssemblyVersion(name, requested, shared.GetName());
        return shared;
    }

    static void ValidateSharedAssemblyVersion(string name, AssemblyName requested, AssemblyName actual)
    {
        if (requested.Version is null)
            return;

        if (string.Equals(name, HostAssemblyName, StringComparison.Ordinal))
        {
            if (actual.Version is not null && requested.Version > actual.Version)
                ReportPluginDiagnostic(
                    $"插件依赖的宿主 ABI 版本更高，请更新 UmamusumeResponseAnalyzer: 插件请求 {requested.FullName}，当前宿主 {actual.FullName}。",
                    UiSeverity.Warning);
            return;
        }

        if (actual.Version != requested.Version)
            throw new FileLoadException(
                $"shared ABI assembly 版本不一致: 插件请求 {requested.FullName}，宿主 Default ALC 已加载 {actual.FullName}。",
                requested.FullName);
    }

    internal static Stream CreateStream(PluginMetadata metadata)
        => CreateZipEntryStream(metadata.PackagePath, metadata.AssemblyEntry);

    static MemoryStream CreateZipEntryStream(string packagePath, string entryName)
    {
        using var archive = ZipFile.OpenRead(packagePath);
        var entry = archive.GetEntry(entryName)
                    ?? throw new InvalidDataException($"插件包缺少条目: package={packagePath}, entry={entryName}");
        var stream = new MemoryStream();
        using (var source = entry.Open())
            source.CopyTo(stream);
        stream.Position = 0;
        return stream;
    }

    internal sealed class PluginLoadContext : AssemblyLoadContext
    {
        readonly object localAssemblyGate = new();
        readonly Dictionary<string, (string PackagePath, string Entry)> availableAssemblies =
            new(StringComparer.OrdinalIgnoreCase);
        readonly PluginMetadata[] packages;

        internal PluginLoadContext(string name, IEnumerable<PluginMetadata>? packages = null) : base(name, true)
        {
            this.packages = packages?.ToArray() ?? [];
            foreach (var package in this.packages)
                foreach (var (assemblyName, entry) in package.Assemblies)
                    if (availableAssemblies.TryGetValue(assemblyName, out var existing))
                    {
                        var existingName = availableAssemblies.Keys.First(candidate =>
                            string.Equals(candidate, assemblyName, StringComparison.OrdinalIgnoreCase));
                        if (!string.Equals(existingName, assemblyName, StringComparison.Ordinal))
                            throw new InvalidDataException(
                                $"插件组程序集名称存在大小写冲突: {existingName} / {assemblyName}, context={name}");
                        if (!SameAssemblyBytes(existing, (package.PackagePath, entry)))
                            throw new InvalidDataException(
                                $"插件组包含内容不同的同名程序集 {assemblyName}: context={name}");
                    }
                    else
                    {
                        availableAssemblies.Add(assemblyName, (package.PackagePath, entry));
                    }
        }

        static bool SameAssemblyBytes(
            (string PackagePath, string Entry) left,
            (string PackagePath, string Entry) right)
        {
            using var leftStream = CreateZipEntryStream(left.PackagePath, left.Entry);
            using var rightStream = CreateZipEntryStream(right.PackagePath, right.Entry);
            return SHA256.HashData(leftStream).AsSpan().SequenceEqual(SHA256.HashData(rightStream));
        }

        protected override Assembly? Load(AssemblyName name)
        {
            if (ResolveSharedAssembly(name) is { } sharedAssembly)
                return sharedAssembly;
            if (name.Name is not { } assemblyName)
                return null;

            lock (localAssemblyGate)
            {
                if (!string.IsNullOrEmpty(name.CultureName) && assemblyName.EndsWith(".resources"))
                {
                    var suffix = $"{name.CultureName}/{assemblyName}.dll";
                    foreach (var package in packages)
                    {
                        var entry = package.SatelliteEntries.FirstOrDefault(candidate =>
                            candidate.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));
                        if (entry is null)
                            continue;
                        return LoadFromStream(CreateZipEntryStream(package.PackagePath, entry));
                    }
                }

                if (!availableAssemblies.TryGetValue(assemblyName, out var location))
                    return null;

                return LoadFromStream(CreateZipEntryStream(location.PackagePath, location.Entry));
            }
        }
    }

    internal sealed class PluginMetadata(
        string packagePath,
        string assemblyEntry,
        string internalName,
        string displayName,
        string author,
        Version version,
        IReadOnlyList<string> dependencies,
        IReadOnlyList<string> targets,
        IReadOnlyDictionary<string, string> assemblies,
        IReadOnlyList<string> satelliteEntries)
    {
        public string FilePath => PackagePath;
        public string PackagePath { get; } = packagePath;
        public string AssemblyEntry { get; } = assemblyEntry;
        public string PluginName { get; } = internalName;
        public string DisplayName { get; } = displayName;
        public string Author { get; } = author;
        public Version Version { get; } = version;
        public IReadOnlyList<string> Dependencies { get; } = dependencies;
        public IReadOnlyList<string> Targets { get; } = targets;
        public IReadOnlyDictionary<string, string> Assemblies { get; } = assemblies;
        public IReadOnlyList<string> SatelliteEntries { get; } = satelliteEntries;
    }
}
