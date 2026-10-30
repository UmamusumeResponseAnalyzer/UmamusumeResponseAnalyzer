# URA 插件构建契约

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

普通托管依赖 DLL 放到 ZIP 根目录，语言资源 DLL 保留目录；插件自身的 `<AssemblyName>.resources.dll` 也随包复制。标记为 native 的资产、非 DLL 运行时文件、PDB、deps.json、临时文件、危险路径或不同来源的同名目标均使打包失败。`project.assets.json` 必须包含与本次 `TargetFramework` / `RuntimeIdentifier` 完全一致的目标。

对 ZIP 验证构建显式使用 `Release`、`RuntimeIdentifier=win-x64`、`SelfContained=false`、`PlatformTarget=AnyCPU`。Visual Studio full-framework MSBuild 在插件打包未指定 RID 时设置后三项；`dotnet` 构建应自行传入 RID。资产冲突错误会提示指定 RID，不会猜选某个 DLL。

部署目标仅在 `$(LOCALAPPDATA)\UmamusumeResponseAnalyzer\Plugins\` 已存在时复制 ZIP，并可覆盖同名文件；验证时传 `DeployUraPluginToLocalAppDataOnBuild=false`。

## 本地包验证

以下命令从宿主仓库根目录执行，只生成引用包：

```powershell
dotnet pack .\UmamusumeResponseAnalyzer\UmamusumeResponseAnalyzer.csproj -c Release -o .\artifacts\nuget
```

用临时 NuGet 配置把 `UmamusumeResponseAnalyzer` 精确映射到该目录，其他包映射到相应源。所有消费项目及递归依赖传同一组 `RestoreConfigFile` / `RestorePackagesPath`；同版本重打包后使用新的空缓存。核对各项目的 `project.assets.json` 和缓存 `.nupkg.metadata`，确认解析版本及来源。测试所用 Host 源码与引用包应来自同一份实现，包括未提交改动；仅有相同版本号或测试项目的 Host `ProjectReference` 不能证明生产插件使用了本地包。

`UraCoreHelper.FindDmmGameExecutable(string? installationFile = null)` 读取官方 DMM 安装记录并返回规范化绝对路径。记录不存在或没有已安装的 `umamusume/GCL` 项时返回 `null`；记录格式错误、歧义、无效目录或缺少可执行文件时，抛出含来源与原因的本地化 `InvalidDataException`。Host 发现流程把该失败记为警告并继续检查其他来源。使用该 API 的 DMMPlugin 自动模式保留手动覆盖；发布顺序为提供此 API 的 Host 运行时与引用包，再发布依赖它的插件。

构建契约与 manifest JSON 往返验证：

```powershell
pwsh -NoProfile -File .\UmamusumeResponseAnalyzer.Tests\Build\VerifyUraPluginBuildTargets.ps1
# 也可分别传入 VS 的 Bin\MSBuild.exe 与 Bin\amd64\MSBuild.exe
pwsh -NoProfile -File .\UmamusumeResponseAnalyzer.Tests\Build\VerifyUraPluginBuildTargets.ps1 -MSBuildPath '<VS-installation>\MSBuild\Current\Bin\MSBuild.exe'
```

该脚本检查包布局声明、默认项目排除项、构建目标和 manifest 字符转义/默认值；它不运行完整 ZIP 资产选择或业务回调。

## 插件测试

插件的业务、配置、framebuffer、回放和 ZIP 加载测试位于各插件仓的 `tests/`。测试工具由各仓独立持有；测试项目通过 `UraTestHostRoot` 引用匹配的 Host 源码，生产插件仍引用 NuGet SDK。

共用 CI 递归构建并执行 `tests/**/*.csproj`。这些项目必须可执行，辅助源码直接编入测试项目。发布边界脚本由本仓默认测试入口执行，核对 NuGet 来源、生产项目引用和固定 submodule。

ZIP 业务测试在本地通过 `URA_PLUGIN_SMOKE_PACKAGE_ROOT` 指定 fresh build 目录；GitHub Actions 使用既有工作区的 `artifacts` 目录。只匹配本插件及所需依赖的 InternalName，缺失和重复均失败。普通测试构建关闭打包和部署，准备验证 ZIP 时只开启 manifest 与打包，部署始终关闭。

真实捕获通过对应插件测试的 `--corpus` 参数提供。无参数入口执行自测；自测通过不代表真实语料通过。捕获及测试结果中的账号数据不得提交。

宿主测试只维护协议、ABI、插件加载及 workspace 契约；不检出或运行真实插件集合。新增测试程序集若需要宿主内部接口，应先随 Host SDK 发布对应授权，再更新依赖它的插件测试。
