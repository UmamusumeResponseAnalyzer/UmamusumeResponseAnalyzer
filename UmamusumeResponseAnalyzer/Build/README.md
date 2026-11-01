# URA 插件构建契约

游戏通知的 RequestId、正文与 headers 约定见[游戏通知契约](../../docs/packet-forwarding.md)。

`UmamusumeResponseAnalyzer` NuGet 包提供 Host 引用程序集，并通过 `buildTransitive` 导入 [URA.Plugin.Build.props](URA.Plugin.Build.props) 和 [URA.Plugin.Build.targets](URA.Plugin.Build.targets)。构建目标生成 `manifest.json`、选择 NuGet 运行时资产、制作插件 ZIP，并可复制到本机插件目录。

## 引用与构建属性

插件项目声明 `IsUraPlugin=true`，使用 `Version="*" PrivateAssets="all"` 引用 Host 包。单独构建插件不需要 Host 源码；插件之间的源码依赖使用固定提交的 submodule。`props` 禁用传递 `ProjectReference` 展开、保留父项目 Configuration/Platform，并从 SDK 默认项排除 `deps/**`、`tests/**` 和构建输出。

宿主使用 Terminal.Gui 2.5.0，插件运行时共用宿主加载的程序集。直接引用 Terminal.Gui 包的插件应与宿主版本一致。

| 属性 | 默认值或作用 |
| --- | --- |
| `IsUraPlugin` | `false`；插件在项目中显式启用 |
| `GenerateUraPluginManifestOnBuild` | 随 `IsUraPlugin`；生成 manifest |
| `PackageUraPluginOnBuild` | 随 `IsUraPlugin`；打包要求同次构建生成 manifest |
| `DeployUraPluginToLocalAppDataOnBuild` | `props` 默认 `true`；部署目标还要求启用插件打包 |
| `UraPluginManifestPath` | `$(TargetDir)manifest.json`；值为裸 `manifest.json` 时也按 TargetDir 解析 |
| `UraPluginPackagePath` | `$(BaseOutputPath)$(Configuration)\$(AssemblyName).zip` |

manifest 要求 `PluginAuthor`、`PluginInternalName`、`PluginDisplayName` 和 `Version`；`PluginInternalName` 必须等于 `AssemblyName`，`Version` 必须可解析为 `System.Version`。可选项为 `PluginDescription`、`PluginCategory`、`PluginChangelog`、`PluginRepositoryUrl`、`PluginHomepage`和 `PluginDependencies`。依赖名称以逗号或分号分隔，写成数组；`LastUpdate` 为构建时的 UTC Unix 秒数。

manifest 序列化在运行时加载构建宿主的 `Newtonsoft.Json.dll`：`dotnet msbuild` 使用 `MSBuildToolsPath`，Visual Studio full-framework MSBuild（含 `amd64` 入口）使用 `MSBuildToolsPath32`。资产选择同样使用构建环境的 `NuGet.ProjectModel.dll` 读取 `project.assets.json`。

## ZIP 资产与部署

ZIP 根目录包含 `manifest.json` 和 `<AssemblyName>.dll`。资产选择从插件直接声明的非自动 NuGet 包开始遍历依赖，排除 Host 包这条引用分支。插件直接引用的运行时包仍参与选择；仅提供 Host 共享 ABI 程序集的包在此处终止遍历。共享程序集为 `UmamusumeResponseAnalyzer`、`Terminal.Gui`、`Watson.Lite`、`WatsonWebserver.Core` 和 `WatsonWebserver.Lite`。

普通托管依赖 DLL 放到 ZIP 根目录，语言资源 DLL 保留目录；插件自身的 `<AssemblyName>.resources.dll` 也随包复制。Windows x64 原生 DLL 保留在 `runtimes/win-x64/native/`。其他 native 资产、非 DLL 运行时文件、PDB、deps.json、临时文件、危险路径或不同来源的同名目标均使打包失败。`project.assets.json` 必须包含与本次 `TargetFramework` / `RuntimeIdentifier` 完全一致的目标。

Host 验证原生 DLL 的 PE x64 架构和 ZIP 路径。每个插件依赖组按包内容哈希解压到数据目录的 `PluginData/.native/`，首次使用时加载；已有缓存必须通过 SHA-256 校验。同组同名原生 DLL 的内容必须相同。

对 ZIP 验证构建显式使用 `Release`、`RuntimeIdentifier=win-x64`、`SelfContained=false`、`PlatformTarget=AnyCPU`。Visual Studio full-framework MSBuild 在插件打包未指定 RID 时设置后三项；`dotnet` 构建应自行传入 RID。资产冲突错误会提示指定 RID，不会猜选某个 DLL。

部署目标仅在 `$(LOCALAPPDATA)\UmamusumeResponseAnalyzer\Plugins\` 已存在时复制 ZIP，并可覆盖同名文件；验证时传 `DeployUraPluginToLocalAppDataOnBuild=false`。

## 宿主单文件发布

本机发布配置 `Properties/PublishProfiles/standalone.pubxml` 被 Git 忽略。配置使用 `net10.0`、`win-x64`、自包含单文件发布，并通过 `EnableCompressionInSingleFile=true` 压缩内嵌程序集；压缩会增加启动时的解压开销。保留嵌入调试符号和语言资源。

`PublishTrimmed=false` 保留开放插件所需的运行时能力。`TrimMode=partial` 也会裁剪 .NET 框架及声明支持裁剪的依赖，发布时无法获知后装插件使用的全部 API。

从宿主仓库根目录发布到独立构建目录：

```powershell
dotnet publish .\UmamusumeResponseAnalyzer\UmamusumeResponseAnalyzer.csproj -p:PublishProfile=standalone -p:PublishDir="$PWD\artifacts\standalone\"
```

## 本地包验证

以下命令从宿主仓库根目录执行，只生成引用包：

```powershell
dotnet pack .\UmamusumeResponseAnalyzer\UmamusumeResponseAnalyzer.csproj -c Release -o .\artifacts\nuget
```

用临时 NuGet 配置把 `UmamusumeResponseAnalyzer` 精确映射到该目录，其他包映射到相应源。所有消费项目及递归依赖传同一组 `RestoreConfigFile` / `RestorePackagesPath`；同版本重打包后使用新的空缓存。核对各项目的 `project.assets.json` 和缓存 `.nupkg.metadata`，确认解析版本及来源。

`UraCoreHelper.FindDmmGameExecutable(string? installationFile = null)` 读取官方 DMM 安装记录并返回规范化绝对路径。记录不存在或没有已安装的 `umamusume/GCL` 项时返回 `null`；记录格式错误、歧义、无效目录或缺少可执行文件时，抛出含来源与原因的本地化 `InvalidDataException`。Host 发现流程把该失败记为警告并继续检查其他来源。使用该 API 的 DMMPlugin 自动模式保留手动覆盖；发布顺序为提供此 API 的 Host 运行时与引用包，再发布依赖它的插件。
