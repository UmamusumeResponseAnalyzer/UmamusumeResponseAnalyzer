using i18n = UmamusumeResponseAnalyzer.Localization.PluginRegistry;
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

    static Dictionary<string, PluginMetadata> ScanPluginMetadata()
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
                        string.Format(i18n.InternalNameConflict, metadata.PluginName));
                if (scanned.TryGetValue(metadata.PluginName, out var existing))
                {
                    scanned.Remove(metadata.PluginName);
                    conflictedNames.Add(metadata.PluginName);
                    if (!LifecycleFailedPlugins.Contains(existing.PackagePath))
                        LifecycleFailedPlugins.Add(existing.PackagePath);
                    throw new InvalidDataException(
                        string.Format(i18n.InternalNamePackageConflict, existing.PluginName, existing.PackagePath,
                            metadata.PluginName, metadata.PackagePath));
                }
                scanned.Add(metadata.PluginName, metadata);
            }
            catch (Exception ex)
            {
                ReportPluginDiagnostic(new InvalidDataException(string.Format(i18n.InvalidPackage, zip.FullName), ex));
                if (!LifecycleFailedPlugins.Contains(zip.FullName))
                    LifecycleFailedPlugins.Add(zip.FullName);
            }
        }

        return scanned;
    }

    static PluginMetadata ReadPluginPackage(string packagePath, string culture)
    {
        var bytes = File.ReadAllBytes(packagePath);
        var package = PluginPackageValidator.Validate(bytes, packagePath, requireMatchingPackageFileName: true);
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
            satellites,
            bytes);
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
    {
        var scope = names.ToHashSet(StringComparer.OrdinalIgnoreCase);
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
                        string.Format(i18n.DependencyCycle, string.Join(" -> ", [.. path, name])));
            }

            state[name] = 1;
            path.Add(name);
            var metadata = LifecycleMetadatas[name];
            foreach (var dependency in metadata.Dependencies.Where(scope.Contains))
                Visit(dependency, path);
            path.RemoveAt(path.Count - 1);
            state[name] = 2;
            ordered.Add(name);
        }

        foreach (var name in LifecycleMetadatas.Keys.Where(scope.Contains))
            Visit(name, []);
        return ordered;
    }

    static string GroupKey(IEnumerable<string> group)
        => string.Join("&", group.Order(StringComparer.OrdinalIgnoreCase));

    internal static void LoadPlugins()
    {
        foreach (var group in LifecycleContextGroups.OrderBy(GroupKey, StringComparer.OrdinalIgnoreCase))
        {
            var ordered = TopologicalOrder(group);
            var key = GroupKey(group);
            PluginLoadContext context;
            try
            {
                context = new PluginLoadContext(key, ordered.Select(name => LifecycleMetadatas[name]));
            }
            catch (InvalidDataException ex)
            {
                foreach (var name in ordered)
                {
                    Lifecycle.Failures[name] = ex.Message;
                    LifecycleFailedPlugins.Add(LifecycleMetadatas[name].PackagePath);
                }
                ReportPluginDiagnostic(ex);
                continue;
            }
            LifecycleContexts.Add(key, context);
            foreach (var name in ordered)
            {
                var metadata = LifecycleMetadatas[name];
                var failure = LoadIntoContextAsync(context, metadata).GetAwaiter().GetResult();
                if (failure is null)
                    continue;
                Lifecycle.Failures[name] = failure.GetBaseException().Message;
                LifecycleFailedPlugins.Add(metadata.PackagePath);
                ReportPluginDiagnostic(failure);
            }
        }
    }

    static async Task<Exception?> LoadIntoContextAsync(
        AssemblyLoadContext context,
        PluginMetadata metadata)
    {
        IPlugin? plugin = null;
        var phase = i18n.ReadAssembly;
        try
        {
            using var stream = CreateZipEntryStream(metadata, metadata.AssemblyEntry);
            var assembly = context.LoadFromStream(stream);

            if (!ShouldLoadPluginForCurrentTargets(metadata))
                return null;

            phase = i18n.ReadExportedTypes;
            var type = assembly.GetExportedTypes().FirstOrDefault(candidate =>
                typeof(IPlugin).IsAssignableFrom(candidate) && !candidate.IsAbstract);
            if (type is null)
                throw new InvalidDataException(string.Format(i18n.PluginTypeMissing, nameof(IPlugin)));

            phase = i18n.CreatePluginInstance;
            plugin = Activator.CreateInstance(type) as IPlugin
                     ?? throw new InvalidDataException(string.Format(i18n.CannotCreatePluginInstance, type.FullName ?? type.Name));
            _ = GenerationFor(plugin);

            LifecycleLoadedPlugins.Add(plugin);
            return null;
        }
        catch (Exception ex)
        {
            Exception failure = new InvalidOperationException(string.Format(i18n.LoadingFailed, metadata.PluginName, phase), ex);
            if (plugin is not null)
            {
                try { await CleanupPluginAsync(plugin).ConfigureAwait(false); }
                catch (Exception cleanupEx)
                {
                    failure = new AggregateException(
                        i18n.LoadingCleanupFailed,
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
            catch (FileNotFoundException ex)
            {
                throw new MissingPluginAssemblyException(requested, ex);
            }
            catch (Exception ex)
            {
                throw new FileLoadException(
                    string.Format(i18n.SharedAssemblyUnavailable, requested.FullName),
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
                    string.Format(i18n.HostUpdateRequired, requested.FullName, actual.FullName),
                    UiSeverity.Warning);
            return;
        }

        if (actual.Version != requested.Version)
            throw new FileLoadException(
                string.Format(i18n.SharedAssemblyVersionMismatch, requested.FullName, actual.FullName),
                requested.FullName);
    }

    static MemoryStream CreateZipEntryStream(PluginMetadata package, string entryName)
    {
        using var bytes = new MemoryStream(package.PackageBytes, writable: false);
        using var archive = new ZipArchive(bytes, ZipArchiveMode.Read);
        var entry = archive.GetEntry(entryName)
                    ?? throw new InvalidDataException(string.Format(i18n.PackageEntryMissing, package.PackagePath, entryName));
        var stream = new MemoryStream();
        using (var source = entry.Open())
            source.CopyTo(stream);
        stream.Position = 0;
        return stream;
    }

    internal sealed class PluginLoadContext : AssemblyLoadContext
    {
        readonly object localAssemblyGate = new();
        readonly Dictionary<string, (PluginMetadata Package, string Entry)> availableAssemblies =
            new(StringComparer.OrdinalIgnoreCase);
        readonly PluginMetadata[] packages;

        internal PluginLoadContext(string name, IEnumerable<PluginMetadata>? packages = null) : base(name)
        {
            // Default ALC 已有机会解析；卫星资源缺失交由 ResourceManager 正常回退。
            Resolving += (_, requested) =>
                !string.IsNullOrEmpty(requested.CultureName) &&
                requested.Name?.EndsWith(".resources", StringComparison.Ordinal) == true
                    ? null
                    : throw new MissingPluginAssemblyException(requested);
            this.packages = packages?.ToArray() ?? [];
            foreach (var package in this.packages)
                foreach (var (assemblyName, entry) in package.Assemblies)
                    if (availableAssemblies.TryGetValue(assemblyName, out var existing))
                    {
                        var existingName = availableAssemblies.Keys.First(candidate =>
                            string.Equals(candidate, assemblyName, StringComparison.OrdinalIgnoreCase));
                        if (!string.Equals(existingName, assemblyName, StringComparison.Ordinal))
                            throw new InvalidDataException(
                                string.Format(i18n.AssemblyCaseConflict, existingName, assemblyName, name));
                        if (!SameAssemblyBytes(existing, (package, entry)))
                            throw new InvalidDataException(
                                string.Format(i18n.AssemblyContentConflict, assemblyName, name));
                    }
                    else
                    {
                        availableAssemblies.Add(assemblyName, (package, entry));
                    }
        }

        static bool SameAssemblyBytes(
            (PluginMetadata Package, string Entry) left,
            (PluginMetadata Package, string Entry) right)
        {
            using var leftStream = CreateZipEntryStream(left.Package, left.Entry);
            using var rightStream = CreateZipEntryStream(right.Package, right.Entry);
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
                        using var satellite = CreateZipEntryStream(package, entry);
                        return LoadFromStream(satellite);
                    }
                }

                if (!availableAssemblies.TryGetValue(assemblyName, out var location))
                    return null;

                using var stream = CreateZipEntryStream(location.Package, location.Entry);
                return LoadFromStream(stream);
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
        IReadOnlyList<string> satelliteEntries,
        byte[] packageBytes)
    {
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
        internal byte[] PackageBytes { get; } = packageBytes;
    }

    internal sealed class MissingPluginAssemblyException(AssemblyName assemblyName, Exception? inner = null)
        : Exception(assemblyName.FullName, inner)
    {
        internal AssemblyName AssemblyName { get; } = assemblyName;
    }
}
