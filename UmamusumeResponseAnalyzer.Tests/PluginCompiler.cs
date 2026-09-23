using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Gallop.Endpoints;
using Newtonsoft.Json;
using System.IO.Compression;
using Terminal.Gui.ViewBase;
using UmamusumeResponseAnalyzer.Plugin;

namespace UmamusumeResponseAnalyzer.Tests
{
    /// <summary>
    /// 用 Roslyn 编译真实插件 DLL，验证启动加载、依赖解析与重启前后的包快照。
    /// </summary>
    static class PluginCompiler
    {
        static readonly MetadataReference[] References = BuildReferences();

        static MetadataReference[] BuildReferences()
        {
            var refs = new Dictionary<string, MetadataReference>(StringComparer.OrdinalIgnoreCase);
            // 框架程序集（System.Runtime / netstandard / System.IO 等）
            var tpa = (string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ?? string.Empty;
            foreach (var p in tpa.Split(Path.PathSeparator))
                if (!string.IsNullOrEmpty(p) && File.Exists(p))
                    refs[p] = MetadataReference.CreateFromFile(p);
            // 插件源码会用到的 ABI 依赖：宿主公开面、Gallop endpoint marker 与 Terminal.Gui View。
            foreach (var asm in new[] { typeof(IPlugin).Assembly, typeof(IGameEndpoint).Assembly, typeof(View).Assembly })
                if (!string.IsNullOrEmpty(asm.Location))
                    refs[asm.Location] = MetadataReference.CreateFromFile(asm.Location);
            return [.. refs.Values];
        }

        /// <summary>编译 <paramref name="source"/> 成 DLL 写到 <paramref name="dllPath"/>；编译报错则抛异常列出诊断。</summary>
        public static void Compile(
            string source,
            string assemblyName,
            string dllPath,
            IReadOnlyList<string>? referencePaths = null,
            IReadOnlyList<ResourceDescription>? resources = null)
        {
            var references = referencePaths is null
                ? References
                : [.. References, .. referencePaths.Select(path => MetadataReference.CreateFromFile(path))];
            var compilation = CSharpCompilation.Create(
                assemblyName,
                [CSharpSyntaxTree.ParseText(source)],
                references,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, optimizationLevel: OptimizationLevel.Release));

            // 单独 Emit 到内存再写盘：避免目标文件被占用时留下半截文件
            using var ms = new MemoryStream();
            var result = compilation.Emit(ms, manifestResources: resources);
            if (!result.Success)
            {
                var errors = string.Join("\n", result.Diagnostics
                    .Where(d => d.Severity == DiagnosticSeverity.Error)
                    .Select(d => d.ToString()));
                throw new InvalidOperationException($"插件编译失败:\n{errors}");
            }
            File.WriteAllBytes(dllPath, ms.ToArray());
        }

        public static void CompilePackage(
            string source,
            string internalName,
            string packagePath,
            IReadOnlyList<string>? dependencies = null,
            string version = "1.0.0",
            IReadOnlyList<string>? referencePaths = null)
        {
            var dllPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.dll");
            try
            {
                Compile(source, internalName, dllPath, referencePaths);
                using var archive = ZipFile.Open(packagePath, ZipArchiveMode.Create);
                archive.CreateEntryFromFile(dllPath, $"{internalName}.dll");
                using var writer = new StreamWriter(archive.CreateEntry("manifest.json").Open());
                writer.Write(JsonConvert.SerializeObject(new
                {
                    Author = "Tests",
                    InternalName = internalName,
                    DisplayName = internalName,
                    Description = string.Empty,
                    Changelog = string.Empty,
                    Version = version,
                    Dependencies = dependencies ?? [],
                    RepositoryUrl = string.Empty,
                    LastUpdate = 0,
                    Category = string.Empty,
                    Homepage = string.Empty,
                }));
            }
            finally
            {
                File.Delete(dllPath);
            }
        }
    }
}
