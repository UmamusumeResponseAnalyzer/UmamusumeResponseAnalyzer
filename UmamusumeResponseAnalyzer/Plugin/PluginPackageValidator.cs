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
    static readonly string[] ManifestPropertyNames =
    [
        "Author",
        "InternalName",
        "DisplayName",
        "Description",
        "Changelog",
        "Version",
        "Dependencies",
        "Targets",
        "RepositoryUrl",
        "LastUpdate",
        "Category",
        "Homepage",
    ];

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
                using var json = JsonDocument.Parse(stream, new()
                {
                    AllowTrailingCommas = false,
                    CommentHandling = JsonCommentHandling.Disallow,
                });
                manifest = ParseManifest(json.RootElement);
            }
            catch (System.Text.Json.JsonException ex)
            {
                throw new InvalidDataException(i18n.StrictJsonRequired, ex);
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

    static PluginInformation ParseManifest(JsonElement manifest)
    {
        if (manifest.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException(i18n.ManifestObjectRequired);

        var properties = manifest.EnumerateObject().ToArray();
        var actualProperties = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in properties)
            if (!actualProperties.Add(property.Name))
                throw new InvalidDataException(string.Format(i18n.DuplicateManifestField, property.Name));

        var missing = ManifestPropertyNames.Except(actualProperties, StringComparer.Ordinal).ToArray();
        var unexpected = actualProperties.Except(ManifestPropertyNames, StringComparer.Ordinal).ToArray();
        if (missing.Length != 0 || unexpected.Length != 0)
            throw new InvalidDataException(
                string.Format(i18n.ManifestSchemaMismatch, string.Join(", ", missing), string.Join(", ", unexpected)));

        string String(string name)
        {
            var value = manifest.GetProperty(name);
            if (value.ValueKind != JsonValueKind.String)
                throw new InvalidDataException(string.Format(i18n.ManifestTypeInvalid, name, "String", value.ValueKind));
            return value.GetString()!;
        }

        string[] Strings(string name)
        {
            var value = manifest.GetProperty(name);
            if (value.ValueKind != JsonValueKind.Array)
                throw new InvalidDataException(string.Format(i18n.ManifestTypeInvalid, name, "Array", value.ValueKind));
            return value.EnumerateArray().Select(item =>
            {
                if (item.ValueKind != JsonValueKind.String)
                    throw new InvalidDataException(string.Format(i18n.ManifestStringsRequired, name));
                return item.GetString()!;
            }).ToArray();
        }

        var lastUpdate = manifest.GetProperty("LastUpdate");
        if (lastUpdate.ValueKind != JsonValueKind.Number || !lastUpdate.TryGetInt64(out var lastUpdateValue))
            throw new InvalidDataException(
                string.Format(i18n.ManifestTypeInvalid, "LastUpdate", "Integer", lastUpdate.ValueKind));

        return new()
        {
            Author = String("Author"),
            InternalName = String("InternalName"),
            DisplayName = String("DisplayName"),
            Description = String("Description"),
            Changelog = String("Changelog"),
            RawVersion = String("Version"),
            Dependencies = Strings("Dependencies"),
            Targets = Strings("Targets"),
            RepositoryUrl = String("RepositoryUrl"),
            LastUpdate = lastUpdateValue,
            Category = String("Category"),
            Homepage = String("Homepage"),
        };
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
        if (manifest.Targets is null)
            throw new InvalidDataException(string.Format(i18n.ManifestFieldNull, "Targets"));

        ValidateNames(manifest.Dependencies, "Dependencies");
        ValidateNames(manifest.Targets, "Targets");
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
