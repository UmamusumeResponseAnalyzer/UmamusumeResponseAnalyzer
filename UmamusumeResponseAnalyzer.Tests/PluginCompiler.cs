using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Newtonsoft.Json.Linq;
using Spectre.Console;
using UmamusumeResponseAnalyzer.Plugin;

namespace UmamusumeResponseAnalyzer.Tests
{
    /// <summary>
    /// 用 Roslyn 在运行时把插件源码字符串编译成 DLL，供热重载集成测试加载/重载。
    /// 这样可以编出 v1/v2 两个版本，验证 reload 真的换上了新代码（而不仅是 ALC 卸载）。
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
            // 宿主 + 插件源码会用到的 app 依赖：IPlugin/Analyzer(host)、JObject(Newtonsoft)、ProgressContext(Spectre)
            foreach (var asm in new[] { typeof(IPlugin).Assembly, typeof(JObject).Assembly, typeof(ProgressContext).Assembly })
                if (!string.IsNullOrEmpty(asm.Location))
                    refs[asm.Location] = MetadataReference.CreateFromFile(asm.Location);
            return [.. refs.Values];
        }

        /// <summary>编译 <paramref name="source"/> 成 DLL 写到 <paramref name="dllPath"/>；编译报错则抛异常列出诊断。</summary>
        public static void Compile(string source, string assemblyName, string dllPath)
        {
            var compilation = CSharpCompilation.Create(
                assemblyName,
                [CSharpSyntaxTree.ParseText(source)],
                References,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, optimizationLevel: OptimizationLevel.Release));

            // 单独 Emit 到内存再写盘：避免目标文件被占用时留下半截文件
            using var ms = new MemoryStream();
            var result = compilation.Emit(ms);
            if (!result.Success)
            {
                var errors = string.Join("\n", result.Diagnostics
                    .Where(d => d.Severity == DiagnosticSeverity.Error)
                    .Select(d => d.ToString()));
                throw new InvalidOperationException($"插件编译失败:\n{errors}");
            }
            File.WriteAllBytes(dllPath, ms.ToArray());
        }
    }
}
