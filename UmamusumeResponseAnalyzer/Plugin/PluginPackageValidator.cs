using i18n = UmamusumeResponseAnalyzer.Localization.PluginRegistry;
using System.IO.Compression;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text.Json;

namespace UmamusumeResponseAnalyzer.Plugin;

internal sealed record ValidatedPluginPackage(
    PluginInformation Manifest,
    string MainAssemblyEntry,
    IReadOnlyDictionary<string, string> Assemblies,
    IReadOnlyList<string> Entries);

internal static class PluginPackageValidator
{
    internal static ValidatedPluginPackage Validate(
        string packagePath,
        bool requireMatchingPackageFileName)
        => Validate(File.ReadAllBytes(packagePath), packagePath, requireMatchingPackageFileName);

    internal static ValidatedPluginPackage Validate(
        byte[] packageBytes,
        string packagePath,
        bool requireMatchingPackageFileName)
    {
        using var packageStream = new MemoryStream(packageBytes, writable: false);
        using var archive = new ZipArchive(packageStream, ZipArchiveMode.Read);
        var manifestEntries = archive.Entries
            .Where(entry => IsRoot(entry.FullName) &&
                            string.Equals(entry.FullName, "manifest.json", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (manifestEntries.Length != 1 || manifestEntries[0].FullName != "manifest.json")
            throw new InvalidDataException(i18n.RootManifestRequired);

        PluginInformation manifest;
        using (var stream = manifestEntries[0].Open())
        {
            try
            {
                manifest = JsonSerializer.Deserialize<PluginInformation>(stream, JsonSerializerOptions.Strict)
                    ?? throw new InvalidDataException(i18n.ManifestObjectRequired);
            }
            catch (System.Text.Json.JsonException ex)
            {
                throw new InvalidDataException(ex.Message, ex);
            }
        }

        ValidateManifest(manifest);
        if (requireMatchingPackageFileName)
        {
            var packageName = Path.GetFileNameWithoutExtension(packagePath);
            if (!string.Equals(packageName, manifest.InternalName, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException(
                    string.Format(i18n.PackageNameMismatch, packageName, manifest.InternalName));
        }

        var mainEntryName = $"{manifest.InternalName}.dll";
        var rootDlls = archive.Entries
            .Where(entry => IsRoot(entry.FullName) &&
                            entry.FullName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var mainEntries = rootDlls
            .Where(entry => string.Equals(entry.FullName, mainEntryName, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (mainEntries.Length != 1)
            throw new InvalidDataException(string.Format(i18n.RootAssemblyRequired, mainEntryName));
        ValidateMainAssemblyIdentity(mainEntries[0], manifest.InternalName);

        var assemblies = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in rootDlls)
        {
            var assemblyName = Path.GetFileNameWithoutExtension(entry.FullName);
            if (!assemblies.TryAdd(assemblyName, entry.FullName))
                throw new InvalidDataException(string.Format(i18n.DuplicateAssembly, assemblyName));
        }

        return new(
            manifest,
            mainEntries[0].FullName,
            assemblies,
            [.. archive.Entries.Select(entry => entry.FullName)]);
    }

    static void ValidateMainAssemblyIdentity(ZipArchiveEntry entry, string internalName)
    {
        using var source = entry.Open();
        using var stream = new MemoryStream();
        source.CopyTo(stream);
        stream.Position = 0;
        try
        {
            using var pe = new PEReader(stream);
            if (!pe.HasMetadata)
                throw new BadImageFormatException(i18n.ClrMetadataMissing);
            var metadata = pe.GetMetadataReader();
            var assembly = metadata.GetAssemblyDefinition();
            var assemblyName = metadata.GetString(assembly.Name);
            ValidateAssemblyIdentity(assemblyName, internalName);
        }
        catch (BadImageFormatException ex)
        {
            throw new InvalidDataException(string.Format(i18n.InvalidManagedAssembly, entry.FullName), ex);
        }
    }

    internal static void ValidateAssemblyIdentity(string? assemblyName, string internalName)
    {
        if (!string.Equals(assemblyName, internalName, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException(
                string.Format(i18n.AssemblyIdentityMismatch, assemblyName ?? "<null>", internalName));
    }

    internal static void ValidateManifest(PluginInformation manifest)
    {
        if (string.IsNullOrWhiteSpace(manifest.Author))
            throw new InvalidDataException(string.Format(i18n.ManifestFieldEmpty, "Author"));
        if (string.IsNullOrWhiteSpace(manifest.InternalName))
            throw new InvalidDataException(string.Format(i18n.ManifestFieldEmpty, "InternalName"));
        if (manifest.InternalName != Path.GetFileName(manifest.InternalName) ||
            manifest.InternalName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new InvalidDataException(string.Format(i18n.InvalidInternalName, manifest.InternalName));
        if (string.IsNullOrWhiteSpace(manifest.DisplayName))
            throw new InvalidDataException(string.Format(i18n.ManifestFieldEmpty, "DisplayName"));
        if (string.IsNullOrWhiteSpace(manifest.RawVersion) ||
            !Version.TryParse(manifest.RawVersion, out _))
            throw new InvalidDataException(string.Format(i18n.InvalidVersion, manifest.RawVersion));
        if (manifest.Dependencies is null)
            throw new InvalidDataException(string.Format(i18n.ManifestFieldNull, "Dependencies"));

        ValidateNames(manifest.Dependencies, "Dependencies");
        if (manifest.Dependencies.Contains(manifest.InternalName, StringComparer.OrdinalIgnoreCase))
            throw new InvalidDataException(string.Format(i18n.SelfDependency, manifest.InternalName));
    }

    static void ValidateNames(IEnumerable<string> names, string field)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in names)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new InvalidDataException(string.Format(i18n.EmptyManifestName, field));
            if (!seen.Add(name))
                throw new InvalidDataException(string.Format(i18n.DuplicateManifestName, field, name));
        }
    }

    static bool IsRoot(string entryName)
        => entryName.IndexOfAny(['/', '\\']) < 0;
}
