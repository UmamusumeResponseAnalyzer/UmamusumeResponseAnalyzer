# 插件测试

[plugins.json](plugins.json) 记录 22 个公开插件仓的固定提交与项目路径；其中 Pioneer 的项目位于 `PioneerScenarioAnalyzer/PioneerScenarioAnalyzer.csproj`。跨插件测试直接引用 Host 源码与外部插件项目，插件生产构建则通过 `Version="*" PrivateAssets="all"` 引用 Host NuGet 包。包来源和构建属性见 [构建契约](../README.md)。

## 五个跨插件测试入口

| 项目 | 实际覆盖 |
| --- | --- |
| `PluginSmokeTests` | 默认运行 7 个源码插件实例、分析器注册/DTO 分发、事件显示与历史，并验证七个 ZIP 的 manifest；`--package-load` 单独验证 ZIP 加载和业务回调 |
| `PluginRuntimeSmoke` | 默认遍历 22 个插件；配置了面板探针的插件检查 framebuffer、更新、历史按键和 Dispose，另检查 EventLogger 继承/剧本输出、DMM 空 token 失败提示、采集器永久上传失败 |
| `PluginWorkspaceLifecycleSmoke` | 22 个插件初始化及未使用时 Dispose 的工作区稳定性、共享标题/面板、已移除工作区对迟到回调的拒绝 |
| `AnalyzerHistoryConfigSmoke` | 10 个分析插件的 `historyLimit` 默认值、严格配置读取、保存/取消/关闭/token 取消、重建后的持久化结果 |
| `PluginReplaySmoke` | `--self-test` 测试 HTTP 捕获解析；`--corpus` 将真实捕获依次送入 EventLogger、EventResponseAnalyzer、GamePacketCollector、RamenScenarioAnalyzer |

这些项目为可执行程序，验收应使用 `dotnet run` 或直接执行构建产物。

从 Host 仓库根目录执行以下 PowerShell。`UraTestPluginSourcesRoot` 指定包含各插件检出的目录，`Tests/Directory.Build.props` 从自身位置确定 Host 根目录；`URA_TEST_PLUGINS_ROOT` 供默认 `PluginSmokeTests` 检查插件源码。`DisableRealDriverIO` 用于无真实终端输入输出的测试运行。

```powershell
$pluginSources = 'K:\repos\URA-Plugins'
$env:URA_TEST_PLUGINS_ROOT = $pluginSources
$env:DisableRealDriverIO = '1'
$smokeBuild = @(
    '-c', 'Release',
    "-p:UraTestPluginSourcesRoot=$pluginSources",
    '-p:GenerateUraPluginManifestOnBuild=false',
    '-p:PackageUraPluginOnBuild=false',
    '-p:DeployUraPluginToLocalAppDataOnBuild=false'
)

# PluginSmokeTests 先按下一节准备七个 ZIP
$env:URA_PLUGIN_SMOKE_PACKAGE_ROOT = 'C:\absolute\fresh-plugin-packages'
dotnet run --project .\eng\PluginTesting\Tests\PluginSmokeTests @smokeBuild
dotnet run --project .\eng\PluginTesting\Tests\PluginRuntimeSmoke @smokeBuild
dotnet run --project .\eng\PluginTesting\Tests\PluginWorkspaceLifecycleSmoke @smokeBuild
dotnet run --project .\eng\PluginTesting\Tests\AnalyzerHistoryConfigSmoke @smokeBuild
dotnet run --project .\eng\PluginTesting\Tests\PluginReplaySmoke @smokeBuild -- --self-test
```

使用本地 Host 引用包时，在 `$smokeBuild` 中追加相同的 `RestoreConfigFile` 和 `RestorePackagesPath`。`PluginRuntimeSmoke` 可在 `--` 后传一个或多个插件名，例如 `-- EventLoggerPlugin GamePacketCollector`；不区分大小写，未知名称返回退出码 2，执行失败返回 1。未指定名称时运行全部 22 个。

## 七个 ZIP 与实际加载

单独构建 AIRedirector、EventLoggerPlugin、EventResponseAnalyzer、GamePacketCollector、LegendScenarioAnalyzer、RamenScenarioAnalyzer、SendGameStatusPlugin，使用 `Release`、`RuntimeIdentifier=win-x64`、`SelfContained=false`、`PlatformTarget=AnyCPU`，并设置：

```text
GenerateUraPluginManifestOnBuild=true
PackageUraPluginOnBuild=true
DeployUraPluginToLocalAppDataOnBuild=false
```

用相同属性求值 `UraPluginPackagePath`（`dotnet msbuild <project> -getProperty:UraPluginPackagePath ...`），把本次产物放入独立目录，设置为 `URA_PLUGIN_SMOKE_PACKAGE_ROOT`。该环境变量必须为存在的绝对路径；测试递归枚举目录内所有 ZIP，要求恰好七个且每个插件唯一。额外 ZIP、缺包、重复包、根 manifest 大小写/字段错误、包名/主 DLL/InternalName 不一致或 Dependencies 不匹配均失败。结构检查不能证明 ZIP 是本次源码构建，产物来源由上述构建流程保证。

```powershell
dotnet run --project .\eng\PluginTesting\Tests\PluginSmokeTests @smokeBuild -- --package-load
```

此模式在临时目录复制 ZIP，通过 `PluginManager` 加载并启动插件，用 ZIP 内主 DLL 的 MVID 验证执行来源，检查共享 Host/Gallop/Terminal.Gui 程序集身份和依赖组的 collectible load context。业务检查包括：

- GamePacketCollector：request/response 原始字节与头字段、向本地 loopback HTTP 服务发出 PUT、成功后清空 pending。
- LegendScenarioAnalyzer / AIRedirector：CheckEvent、Load 更新训练面板与目标角色/回合；AI 进程开关保持关闭。
- EventLoggerPlugin / EventResponseAnalyzer：当前回合/剧本、工作区内训练失败警告、已知事件选项及效果。
- RamenScenarioAnalyzer / SendGameStatusPlugin：拉面状态与面板、`thisTurn.json` 和编号快照、状态写入不改变当前工作区或 framebuffer。

结束时要求实例、上下文和分析器注册撤销，面板移除，插件/回调/load context 的弱引用经 GC 后释放。默认模式与 `--package-load` 各自运行，互不替代。该模式不经过真实 HTTP ingress，也不代表所有外部插件功能或真实捕获都已验证。

## HTTP 捕获回放

```powershell
dotnet run --project .\eng\PluginTesting\Tests\PluginReplaySmoke @smokeBuild -- --corpus 'C:\absolute\http-captures'
```

回放读取目录顶层的 `*.txt`，文件名必须以 `[整数序号]` 开头，按序号、文件名排序。每个文件包含 `/notify/request` 或 `/notify/response` 的 HTTP 请求行、`x-hachimi-game-url`、`content-length` 和原始二进制 body；支持 CRLF 或 LF 的头/体分隔，长度必须与 body 字节数一致。可选 `x-hachimi-*` 头保留为 `GameHttpHeaders`。请求与紧随其后的响应必须有相同 URL 和 sid。

可用首个位置参数传语料目录；无参数时默认 `F:\Desktop\ramen_full_game`，目录不存在直接失败。空语料、缺 request/response/配对/有效分发、解析错误、配对错误、未知 endpoint、分发异常、可见 ERR 或 Dispose 错误均使验收失败，不能记为跳过或通过。`--self-test` 不读取语料，仅覆盖头分隔和 body 长度检查。

回放将数据库 fixture、插件配置和结果写入 `%TEMP%\ura-plugin-replay-<guid>`，结束后保留该目录并在摘要输出路径。采集器配置为 `enabled=false`、loopback 地址。摘要分别列出 request、response、配对、分发、忽略、异常和工作区结果；捕获数据和含账号标识的结果目录不要提交到仓库。

## 共用基础设施与仓库边界

`SmokeHost.cs` 提供真实 `UiHost` / ANSI driver 的独立 owner 线程、framebuffer/单元格捕获、按键/鼠标/模态交互及 flush。`RuntimePluginContext` 记录事件与分析器，并在释放时取消、等待后台任务；`HistoryConfigDialog` 提供历史上限编辑和按钮交互。生命周期 smoke 则使用不执行事件/后台任务的最小 context；它不证明业务回调正确。

`RandomDtoGenerator` 按类型与 salt 的稳定 seed 生成公开字段，Protocol profile 用于协议往返，Analyzer profile 用于插件输入。`RandomDtoFactory` 添加角色、训练命令和剧本状态，部分 fixture 会更新 EventLogger 状态。它们是合成输入；Host 的 `URA_PACKET_CORPUS` 测试与这里的真实 replay 需另行提供捕获。

```powershell
pwsh -NoProfile -File .\eng\PluginTesting\VerifyPublicationBoundary.ps1 -PluginSourcesRoot $pluginSources
```

发布边界脚本检查 22 个插件的 NuGet 精确源映射、Host 包引用、仓内 `ProjectReference`、公开 GitHub submodule URL 及嵌套 gitlink 的检出提交。它不下载插件，也不校验每个顶层插件 HEAD 是否等于 `plugins.json` 的 `ref`；检出操作需单独落实固定提交。

AIRedirector、LegendScenarioAnalyzer、RamenScenarioAnalyzer、SendGameStatusPlugin、SkillEffectPlugin、SkillTipsResponseAnalyzer、WinSaddleAnalyzer 的专属测试位于各插件仓 `tests/`，不在本仓五个入口之内；其参数与 workflow 以对应仓源码为准。
