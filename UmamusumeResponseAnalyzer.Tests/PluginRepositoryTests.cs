using i18n = UmamusumeResponseAnalyzer.Localization.PluginRegistry;
using System.IO.Compression;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UmamusumeResponseAnalyzer.Plugin;
using Xunit;

namespace UmamusumeResponseAnalyzer.Tests
{
    public class PluginRepositoryTests
    {
        static PluginInformation Info(string author, string internalName, string category = "") => new()
        {
            Author = author,
            InternalName = internalName,
            DisplayName = internalName,
            Category = category,
            Version = new(1, 0, 0),
        };

        const string PackageInternalName = "UmamusumeResponseAnalyzer";
        [Fact]
        public void InstallPathUsesInternalName()
        {
            Assert.Equal(Path.Combine("Plugins", "Same.zip"), PluginRepository.InstallZipPath("Same"));
        }

        [Fact]
        public void ValidatePackage_ReturnsManifestMetadata()
        {
            var manifest = Info("author", PackageInternalName);
            manifest.RawVersion = "2026.03.04";
            manifest.Dependencies = ["Dependency"];
            var package = CreatePackage(manifest);
            try
            {
                var actual = PluginRepository.ValidatePackage(
                    package,
                    "AUTHOR",
                    "umamusumeresponseanalyzer",
                    "2026.3.4");

                Assert.Equal(PackageInternalName, actual.InternalName);
                Assert.Equal(["Dependency"], actual.Dependencies);
                Assert.Equal("2026.03.04", actual.RawVersion);
            }
            finally
            {
                File.Delete(package);
            }
        }

        [Fact]
        public void ValidatePackage_RequiresExactRequiredFieldNames()
        {
            var manifest = Info("author", PackageInternalName);
            var package = CreatePackage(manifest, json =>
            {
                json["version"] = json["Version"];
                json.Remove("Version");
            });
            try
            {
                var error = Assert.Throws<InvalidDataException>(() =>
                    PluginRepository.ValidatePackage(package, "author", PackageInternalName, "1.0.0"));

                Assert.IsType<System.Text.Json.JsonException>(error.InnerException);
                Assert.Contains("Version", error.Message);
            }
            finally
            {
                File.Delete(package);
            }
        }

        [Theory]
        [InlineData("other", PackageInternalName, "1.0.0", "Author")]
        [InlineData("author", "OtherPlugin", "1.0.0", "InternalName")]
        [InlineData("author", PackageInternalName, "2.0.0", "Version")]
        public void ValidatePackage_RejectsRequestedIdentityMismatch(
            string author,
            string internalName,
            string version,
            string field)
        {
            var package = CreatePackage(Info("author", PackageInternalName));
            try
            {
                var error = Assert.Throws<InvalidDataException>(() =>
                    PluginRepository.ValidatePackage(package, author, internalName, version));

                Assert.Contains(field, error.Message);
                Assert.Equal(field switch
                {
                    "Author" => string.Format(i18n.PackageAuthorMismatch, author, "author"),
                    "InternalName" => string.Format(i18n.PackageInternalNameMismatch, internalName, PackageInternalName),
                    _ => string.Format(i18n.PackageVersionMismatch, version, "1.0.0"),
                }, error.Message);
            }
            finally
            {
                File.Delete(package);
            }
        }

        [Fact]
        public void ValidatePackage_RequiresRootMainAssembly()
        {
            var package = CreatePackage(
                Info("author", PackageInternalName),
                mainAssemblyPath: $"lib/{PackageInternalName}.dll");
            try
            {
                var error = Assert.Throws<InvalidDataException>(() =>
                    PluginRepository.ValidatePackage(package, "author", PackageInternalName, "1.0.0"));

                Assert.Equal(string.Format(i18n.RootAssemblyRequired, $"{PackageInternalName}.dll"), error.Message);
                Assert.Contains($"{PackageInternalName}.dll", error.Message);
            }
            finally
            {
                File.Delete(package);
            }
        }

        [Fact]
        public void ValidatePackage_DoesNotTreatBackslashNestedDllAsRootAssembly()
        {
            var package = CreatePackage(
                Info("author", PackageInternalName),
                additionalEntries: ["Dependency.dll", @"lib\dependency.DLL"]);
            try
            {
                var manifest = PluginRepository.ValidatePackage(
                    package,
                    "author",
                    PackageInternalName,
                    "1.0.0");

                Assert.Equal(PackageInternalName, manifest.InternalName);
            }
            finally
            {
                File.Delete(package);
            }
        }

        [Fact]
        public void ValidatePackage_RejectsDuplicateRootAssemblyNamesIgnoringCase()
        {
            var package = CreatePackage(
                Info("author", PackageInternalName),
                additionalEntries: ["Dependency.dll", "dependency.DLL"]);
            try
            {
                var error = Assert.Throws<InvalidDataException>(() =>
                    PluginRepository.ValidatePackage(package, "author", PackageInternalName, "1.0.0"));

                Assert.Equal(string.Format(i18n.DuplicateAssembly, "dependency"), error.Message);
            }
            finally
            {
                File.Delete(package);
            }
        }

        [Fact]
        public void ValidatePackage_RejectsCaseDuplicateRootManifest()
        {
            var package = CreatePackage(
                Info("author", PackageInternalName),
                additionalEntries: ["Manifest.json"]);
            try
            {
                var error = Assert.Throws<InvalidDataException>(() =>
                    PluginRepository.ValidatePackage(package, "author", PackageInternalName, "1.0.0"));

                Assert.Contains("manifest.json", error.Message);
            }
            finally
            {
                File.Delete(package);
            }
        }

        [Theory]
        [InlineData("LastUpdate", "not-an-integer")]
        [InlineData("Dependencies", 1)]
        public void ValidatePackage_RejectsWrongManifestTokenShape(
            string property,
            object value)
        {
            var package = CreatePackage(Info("author", PackageInternalName), json =>
            {
                if (property == "Dependencies")
                    json[property] = new JArray(value);
                else
                    json[property] = JToken.FromObject(value);
            });
            try
            {
                var error = Assert.Throws<InvalidDataException>(() =>
                    PluginRepository.ValidatePackage(package, "author", PackageInternalName, "1.0.0"));

                Assert.IsType<System.Text.Json.JsonException>(error.InnerException);
                Assert.Contains(property, error.Message);
            }
            finally
            {
                File.Delete(package);
            }
        }

        [Theory]
        [InlineData("comment")]
        [InlineData("trailing-comma")]
        [InlineData("single-quotes")]
        [InlineData("extra-token")]
        public void ValidatePackage_RejectsInvalidJsonSyntax(string mutation)
        {
            var package = CreatePackage(
                Info("author", PackageInternalName),
                editRawManifest: json => mutation switch
                {
                    "comment" => $"/*comment*/{json}",
                    "trailing-comma" => $"{json[..^1]},}}",
                    "single-quotes" => json.Replace('"', '\''),
                    "extra-token" => $"{json}{{}}",
                    _ => throw new InvalidOperationException($"未知 JSON mutation: {mutation}"),
                });
            try
            {
                var error = Assert.Throws<InvalidDataException>(() =>
                    PluginRepository.ValidatePackage(package, "author", PackageInternalName, "1.0.0"));

                Assert.IsType<System.Text.Json.JsonException>(error.InnerException);
            }
            finally
            {
                File.Delete(package);
            }
        }

        [Fact]
        public void ValidatePackage_UsesDefaultJsonDeserialization()
        {
            var package = CreatePackage(
                Info("author", PackageInternalName),
                json => json["Description"] = JValue.CreateNull(),
                editRawManifest: json => $"{json[..^1]},\"Targets\":[],\"DownloadUrl\":\"https://example.com\",\"RawVersion\":\"2.0\",\"Author\":\"other\"}}");
            try
            {
                var manifest = PluginRepository.ValidatePackage(package, "other", PackageInternalName, "1.0.0");

                Assert.Equal("other", manifest.Author);
                Assert.Equal("1.0.0", manifest.RawVersion);
                Assert.Null(manifest.Description);
            }
            finally
            {
                File.Delete(package);
            }
        }

        [Fact]
        public void ValidatePackage_RequiresAllManifestFields()
        {
            var info = Info("author", PackageInternalName);
            var fields = JObject.FromObject(info).Properties().Select(property => property.Name).ToArray();
            Assert.Equal(11, fields.Length);
            foreach (var field in fields)
            {
                var package = CreatePackage(info, json => json.Remove(field));
                try
                {
                    var error = Assert.Throws<InvalidDataException>(() =>
                        PluginRepository.ValidatePackage(package, "author", PackageInternalName, "1.0.0"));
                    Assert.IsType<System.Text.Json.JsonException>(error.InnerException);
                    Assert.Contains(field, error.Message);
                }
                finally
                {
                    File.Delete(package);
                }
            }
        }

        [Theory]
        [InlineData("Author")]
        [InlineData("InternalName")]
        [InlineData("DisplayName")]
        [InlineData("Version")]
        [InlineData("Dependencies")]
        [InlineData("LastUpdate")]
        public void ValidatePackage_RejectsNullRequiredValues(string field)
        {
            var package = CreatePackage(Info("author", PackageInternalName), json => json[field] = JValue.CreateNull());
            try
            {
                var error = Assert.Throws<InvalidDataException>(() =>
                    PluginRepository.ValidatePackage(package, "author", PackageInternalName, "1.0.0"));
                Assert.Contains(field, error.Message);
            }
            finally
            {
                File.Delete(package);
            }
        }

        [Fact]
        public void ValidatePackage_AllowsDotInLoaderValidInternalName()
        {
            var package = CreatePackage(Info("author", PackageInternalName));
            try
            {
                var manifest = PluginRepository.ValidatePackage(
                    package,
                    "author",
                    PackageInternalName,
                    "1.0.0");

                Assert.Equal(PackageInternalName, manifest.InternalName);
            }
            finally
            {
                File.Delete(package);
            }
        }

        [Fact]
        public void ValidatePackage_PreservesDateLikeManifestStrings()
        {
            var expected = "2026-08-11T03:16:39.2341390Z";
            var info = Info("author", PackageInternalName);
            info.Description = expected;
            var package = CreatePackage(info);
            try
            {
                var manifest = PluginRepository.ValidatePackage(
                    package,
                    "author",
                    PackageInternalName,
                    "1.0.0");

                Assert.Equal(expected, manifest.Description);
            }
            finally
            {
                File.Delete(package);
            }
        }

        [Fact]
        public void ValidatePackage_RejectsEmbeddedMainAssemblyNameMismatch()
        {
            const string manifestName = "Different.Plugin";
            var package = CreatePackage(Info("author", manifestName));
            try
            {
                var error = Assert.Throws<InvalidDataException>(() =>
                    PluginRepository.ValidatePackage(package, "author", manifestName, "1.0.0"));

                Assert.Equal(string.Format(i18n.AssemblyIdentityMismatch, PackageInternalName, manifestName), error.Message);
                Assert.Contains(PackageInternalName, error.Message);
                Assert.Contains(manifestName, error.Message);
            }
            finally
            {
                File.Delete(package);
            }
        }

        static string CreatePackage(
            PluginInformation manifest,
            Action<JObject>? editManifest = null,
            string? mainAssemblyPath = null,
            string[]? additionalEntries = null,
            Func<string, string>? editRawManifest = null)
        {
            var path = Path.Combine(Path.GetTempPath(), $"ura-repository-{Guid.NewGuid():N}.zip");
            using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
            var json = JObject.FromObject(manifest, JsonSerializer.CreateDefault());
            editManifest?.Invoke(json);
            using (var writer = new StreamWriter(archive.CreateEntry("manifest.json").Open()))
                writer.Write(editRawManifest?.Invoke(json.ToString(Formatting.None)) ?? json.ToString(Formatting.None));
            using (var source = File.OpenRead(typeof(PluginInformation).Assembly.Location))
            using (var stream = archive.CreateEntry(mainAssemblyPath ?? $"{manifest.InternalName}.dll").Open())
                source.CopyTo(stream);
            foreach (var entry in additionalEntries ?? [])
            {
                using var stream = archive.CreateEntry(entry).Open();
                stream.WriteByte(0);
            }
            return path;
        }
    }
}
