# UmamusumeResponseAnalyzer 过度防御与低效率代码简化实施计划

修订日期：2026-09-09。状态：待实施；本文规定实现、验证和回填，不表示这些工作已经完成。

## 1. 目标、授权与边界

完成 A–H 八项修改、23 个插件的宿主包升级、七个新 ZIP 的业务验收、文档更新及原工作区回填。保留现有功能，以人类可维护性为首要目标。

### 1.1 实现手段的授权

**如果某个操作（新增依赖、框架等）可以显著降低代码量、复杂度，则允许执行。**

这条授权取代本计划对依赖、框架等实现手段的绝对禁令，适用于实现和验收工具。优先复用现有代码、标准库和已有依赖；存在明确净收益时，允许采用其他方案，不因“新增依赖或框架”这一事实重复请求许可。

- 下文给出一条可直接实施的默认路线，不要求执行 Agent 重新开展架构选型。
- 替代默认路线时，先明确它替代哪些代码或步骤，再用实际实现和验证证明收益；报告被删除的维护负担以及引入的约束。不能只把代码藏进自建中间层，或只看本仓行数而忽略配置、适配和运行负担。
- 不为“显著”设置机械的行数或百分比门槛；不能说明具体净收益时，采用下文默认路线。
- 该授权不改变已确认的功能、数据语义、公开 API 目标、验收范围和已有工作保护规则。改变这些结果仍需用户定范围；实际危险操作仍按仓库规则，在操作具体可审查后确认。

### 1.2 已确认的产品和交付决策

- 允许修改公开 API；宿主与本次受影响插件同步升级，不保留旧 API 适配层。
- YAML 缺失配置节或字段时使用默认值；未知字段读取时忽略，保存时丢弃。
- 重复键、无效标量、不允许的 null 继续报错，包括通过 YAML 别名传入的 null。
- 取消对 Exception.Message、Exception.ToString() 自身抛异常的特殊保障；保留普通插件异常隔离和清理。
- 保留全分析器随机 DTO 烟测及全部既有场景修补，只合并重复生成核心。
- TurnInfo 两个集合属性返回 IReadOnlyDictionary，每次读取当前 DTO，不缓存。
- 默认日志保留原始异常消息中的路径；详细日志保留原始堆栈。
- 升级 Integration 的既有 22 个插件及独立 MouseDebugger，共 23 个插件；不把 MouseDebugger 加入 Integration 正式插件集合。
- 七个 Minimal Release 新 ZIP 必须分别执行自己的业务回调并检查结果，不能仅验证初始化或源码实例。
- AIRedirector 的新 ZIP 验收只验证进程内目标更新；不新增辅助进程，不把结果描述为完整 stdout 转发链路验证。现有源码层转发烟测保留。
- 分配持平可以接受，不要求所有样本都下降，但不得引入分配回退；结果必须如实报告。
- 最终回填原 Host、Integration、插件及相关依赖副本。原 Integration 内的 Host 也升级到独立 Host 基线并同步本次修改，完成回填后的验收。
- 不提交、不推送、不发布 NuGet 包、不上传插件包、不部署到本机安装目录。

### 1.3 模块处理范围

| 模块 | 本次处理 |
|---|---|
| M02 配置 | 合并模型，落实 YAML 输入规则 |
| M03、M05 数据分发 | 删除空 scope，建立 endpoint 索引，简化快照准入 |
| M06、M07 插件加载与生命周期 | 简化回调上下文和异常文本处理 |
| M12 快捷键 | 仅删除空 scope 调用 |
| M14 日志与通知 | 删除异常文案反向解析 |
| M18 育成领域 | 修改总属性计算及两个集合属性 |
| S01、S02 构建与测试 | 指定测试、随机 DTO 核心、包消费及真实加载验收 |
| S03 文档 | 实际改变的契约、命令与模块说明 |
| 其他模块 | 不扩大生产功能改动，通过现有行为测试验证联动影响 |

保留 HTTP/协议输入校验、ZIP 路径和 manifest 校验、程序集冲突检测、更新文件哈希比较、生命周期串行化、在途回调排空、UI 线程归属、Workspace 生命周期、鼠标捕获及数据库边界检查。不得仅因名称含 lock、gate、snapshot 或 fallback 就删除。

不手工修改 Gallop，不恢复 MessagePack 源生成器。既有外部边界检查不能因减少代码而失去等价保障。

## 2. A–H 默认实现路线

### A. 删除无作用的回调 scope

- 删除 PluginManager.EnterPluginCallbackScope、NoopScope 及全部调用。
- 调用点包括 Server.InvokeAnalyzer、HotkeyInputDispatcher，以及 PluginDispatchReloadTests 中模拟插件热键执行的调用。
- 保留相邻的真实 callback 租约和 owner scope；保留 EnterPluginCallback、TryEnterPluginCallback、generation 租约及 HotkeyManager.RegisterScope。
- 删除后搜索符号，确认测试也没有遗漏；不以另一种空对象、委托或包装方法替代。

### B. 简化回调上下文，避免相同值写入

在 PluginGeneration 中把 AsyncLocal<ImmutableHashSet<PluginGeneration>?> 改为 AsyncLocal<bool>。

- 进入回调时保存此前值；此前为 false 时才写入 true。
- 退出时比较当前值和保存值，仅在不同时恢复保存值。
- HasActiveCallbackFlow 直接返回该值。
- 保留 CallbackFlowLease.Dispose 幂等性，以及先恢复上下文、再释放真实租约的顺序。
- 保留跨 await、嵌套回调、回调内拒绝卸载和关闭的行为。

不能无条件反复写入相同 bool。审计中的同步上下文探针测得：同一 owner 嵌套时，原集合操作为 0 B/次，无条件 bool 写入为 288 B/次，仅状态变化时写入为 0 B/次；这些数字不含共同租约分配，不替代实施后的完整路径测量。

默认不增加计数器、全局注册表或线程本地状态。

### C. 优化分析器快照

保留 AnalyzerRuntimeSnapshot 的 Request、Response 有序 ImmutableArray，并在每次新快照构造时分别生成：

```csharp
FrozenDictionary<Type, ImmutableArray<AnalyzerRegistration>>
```

键为 EndpointType，值为该 endpoint 按现有分发顺序排列的注册项。

- 索引与数组属于同一个快照，一起原子发布；注册、注销、整组移除都通过新快照完成。
- 保持优先级升序，以及同优先级下的包顺序、声明顺序和注册顺序。
- 分发先选择方向，再查 endpoint，不逐包扫描该方向分析器全集。
- 保留 raw payload、DTO 投影、headers、未知 endpoint 和投影错误去重行为。

将仅用于分析器的 PluginCallbackSnapshot<T> 收敛为 AnalyzerCallbackSnapshot：

- 输入固定为目标 endpoint 的 ImmutableArray<AnalyzerRegistration>。
- 删除泛型 owner selector、候选 ToList() 和第二次 LINQ 筛选。
- 用按引用比较的 Dictionary<IPlugin, bool> 记录 owner 是否准入。
- 单次遍历：首次遇到 owner 时尝试取得租约，成功后保存租约，按原顺序收集其注册项。
- **全部候选的准入完成后才能调用第一个 handler**，保证后续被选中插件也已受租约保护。
- 异常路径和正常释放都逆序释放租约；保留重复 Dispose 安全，释放后清除注册项和租约引用。

默认不增加池化、共享可变空快照或逐 handler 准入。

### D. 简化配置解析，覆盖 null 别名

默认继续使用 YamlDotNet 18.1.0。

- 删除 YamlConfigDto、六个配置节 DTO、ToDomain、RequiredSection、Required 和复制列表的 RequiredStrings。
- 直接反序列化默认初始化的 YamlConfig；默认值只保留在运行时模型，包括按区域和 UI culture 计算的 IsGithubBlocked。
- 使用现有命名规则，以及 WithDuplicateKeyChecking()、WithEnforceNullability()、IgnoreUnmatchedProperties()。
- LanguageConfig.Selected 保留 internal setter，不启用非公开属性扫描。

| 输入 | 结果 |
|---|---|
| {}、缺少配置节或字段 | 使用运行时模型默认值 |
| 根节点 null、空文档 | 拒绝，包含来源路径和 $ |
| 已知配置节、必需字符串、列表为 null | 拒绝 |
| 非 nullable 值类型为 null | 拒绝，不转换为 0、false 或默认枚举值 |
| null 别名赋给上述必需位置 | 按相同规则拒绝 |
| 两个字符串列表含 null 元素 | 拒绝，指出带索引字段路径 |
| custom-database-repository 缺失、null、空标量或空字符串 | 归一为 string.Empty |
| 未知根字段或已知节中的未知字段 | 忽略 |
| 保存 | 仅序列化当前模型，丢弃未知字段 |
| 正在解析的 mapping 出现重复键 | 拒绝，包括重复的未知键 |
| 整个被忽略的未知子树 | 不额外检查内部字段或重复键 |
| 无效数字、布尔、枚举标量 | 拒绝，保留解析原因和位置 |

默认在解析边界保留三处必要检查：

1. 包装 NullNodeDeserializer：其识别到 null 且目标为非 nullable 值类型时，抛出带 YAML 位置的 YamlException。
2. 包装 ObjectNodeDeserializer 的 nestedObjectDeserializer：嵌套值解析返回后、属性转换和赋值前，拒绝“结果为 null 且目标为非 nullable 值类型”。此处覆盖已经解析的 null 别名。
3. 解析后检查 Repository.Targets、WorkspaceTaskbarTitleOrder 的 null 元素，直接报出 repository.targets[i] 或 workspace-taskbar-title-order[i]，不复制列表。

两个包装放在现有配置实现内。仅检查原始 null 节点不能覆盖别名，不能省略第二处等价保障。必须分别覆盖下例的整数属性，以及将别名赋给 show-first-run-prompt 的布尔属性：

```yaml
updater:
  custom-database-repository: &nil null
core:
  listen-port: *nil
```

合法别名保持原生行为；未声明或前向别名沿用 YamlDotNet 原生错误。CustomDatabaseRepository 改为 string?，在反序列化边界执行一次 ??= string.Empty。

保留外层 InvalidDataException：原生解析错误包含文件路径、行列和原因；根节点及列表元素检查包含明确字段路径。不要求把所有原生错误重建成点分字段路径。删除无作用的 catch (InvalidDataException) { throw; }。

默认不引入 YAML AST、迁移器或修复器。若依据 1.1 采用其他实现，必须完整保留本节输入表，不能通过默默补值、保留未知字段或降低错误保障减少代码。

### E. 优化 TurnInfo

公开 API 固定为：

```csharp
public IReadOnlyDictionary<int, int> SupportCards { get; }
public IReadOnlyDictionary<int, EvaluationInfo> Evaluations { get; }
```

- getter 对当前 DTO 数组直接调用 ToDictionary，删除 ToFrozenDictionary；每次生成新字典，不缓存，不额外包 ReadOnlyDictionary。
- 保留重复键抛异常、缺失键查询失败和读取当前 DTO 的行为；只保证公开接口只读，不增加深度不可变承诺。
- TotalStats 改为五个 revised 属性直接相加，用 checked 保留 Enumerable.Sum 的整数溢出行为。
- 不修改 Stats、StatsRevised、修正公式、GetCommonResponse 或插件自有 TurnInfo。

### F. 简化异常展示和特殊防御

删除 TerminalUi 中按中文初始化错误前缀识别类型、从消息提取插件名，以及按 path=、配置文件: 等标记裁剪消息的逻辑。

保留现有 AggregateException 展开及 InnerException 遍历顺序、消息去重和空消息处理。仅整理首尾空白；全部消息为空时显示异常类型名。

- 初始化错误在产生位置附带插件 internal name，直接展示外层和内层消息。
- DescribeException 保留类型与消息格式，删除读取 Message 的特殊 try/catch。
- 分析器、Started、后台操作入口在捕获处生成原异常 ToString() 文本。
- 内部 ReportPluginFailure 增加详细文本参数，内部 TerminalUi.LogException 增加可选详细文本参数，沿用日志详情字段。
- 详情包含宿主错误上下文和原异常文本；通知失败时追加该诊断失败的详情，不能被原异常文本覆盖。
- 日志和通知持久状态只保存文本，不保存插件异常、Type、MethodInfo 或捕获异常的委托；生命周期调用过程中的瞬时异常传递不因此扩大改造。
- Dispose 失败继续使用宿主创建的 InvalidOperationException，把原异常全文写入消息，不通过 InnerException 保留插件异常。
- 保留通知失败后尝试日志、日志失败返回诊断错误，以及普通插件失败后继续其他回调和清理的行为。

保留以下行为测试，只将故意破坏 Message/ToString 的异常替换为普通 InvalidOperationException：

- DispatchResponse_IsolatesAnalyzerExceptionAndContinues
- TriggerStartedForPluginsAsync_LogsHandlerFailureAndContinues
- BatchUnloadContinuesAfterPluginDisposeFailure

默认不增加错误总线、重试或日志配置。

### G. 删除指定实现形状测试

从 PluginAbiBoundaryTests 删除：

- WorkspacePublicApiMatchesTargetManifest
- WorkspaceSupportingTypesMatchTargetManifest
- HotkeyPublicApiMatchesTargetManifest
- TerminalUiPublicApiMatchesTargetManifest
- PluginContractsMatchDestructiveCutover
- TerminalUiLifecycleIsOneShot

删除随之失去调用者的反射断言 helper。PluginLifecycleManagementIsHostInternal 仅保留 manager、生命周期结果和 outcome 类型的可见性断言。

保留 Host/Gallop 程序集身份检查、真实合成插件加载与调用、生成 Gallop 源文件输出契约。在现有合成消费者中实际读取修改后的两个 TurnInfo 属性。

保留真实 one-shot UI 生命周期、加载失败、热重载、卸载排空、输入及 framebuffer 测试。不按反射使用情况、测试数量或文件行数继续删测试。

### H. 合并随机 DTO 生成核心

新增纯测试共享文件 eng/PluginTesting/RandomDtoGenerator.cs，提供 RandomDtoGenerator.Create(Type type, string salt, RandomDtoProfile profile)，返回 object。RandomDtoProfile 仅有 Protocol、Analyzer。

保留两个现有范围，区间上界不包含：

| 行为 | Protocol | Analyzer |
|---|---:|---:|
| 最大深度 | 4 | 8 |
| 普通数组长度 | 2 | 1 |
| int | [-1_000_000, 1_000_000) | [0, 100) |
| uint | [0, 1_000_000) | [0, 100) |
| long | [-1_000_000_000, 1_000_000_000) | [0, 100) |
| ulong | [0, 1_000_000_000) | [0, 100) |
| float、double、decimal 缩放 | 10,000 | 100 |

保留 seed 算法、随机调用顺序、其他整数范围、字符串、四字节 byte 数组、枚举、nullable、公共字段遍历和深度截断规则。

- Host 测试链接纯核心，使用 Protocol，删除嵌套生成器。
- 现有 RandomDtoFactory 保留为场景修补入口，调用 Analyzer 后执行原有修补；保留 CreateForAnalyzer、EventLogger 状态准备及全部场景修补。
- Integration 的 PluginSmokeTests、PluginRuntimeSmoke 链接新核心。
- 纯核心不引用 Gallop、EventLogger 或插件程序集；Host 测试不因此依赖 EventLogger。

修改前保存两个生成核心作为临时比较基线。使用现有调用集合中的 DTO 类型、salt 和 profile，对比场景修补前生成对象的 MessagePack 字节；两个 profile 均一致后，再运行全部既有随机和场景烟测。

现有“生成后再反序列化”不能代替改前/改后对照。默认对照程序仅保留为本轮验收产物，不新增长期框架；不能减少 endpoint、分析器、场景或语料集合。

## 3. 隔离基线与原始工作保护

### 3.1 固定布局和变量

默认布局：

```text
K:\repos\URA-Plugins\.worktrees\ura-simplification\
  integration\
    host\UmamusumeResponseAnalyzer\
    plugins\<既有22个插件及递归依赖>\
  MouseDebugger\
  artifacts\
    baseline\
    nuget\
    packages-cache\
    plugin-packages\
    NuGet.Config
```

```powershell
$RunRoot = 'K:\repos\URA-Plugins\.worktrees\ura-simplification'
$IntegrationRoot = Join-Path $RunRoot 'integration'
$HostRoot = Join-Path $IntegrationRoot 'host\UmamusumeResponseAnalyzer'
$MouseRoot = Join-Path $RunRoot 'MouseDebugger'
$ArtifactsRoot = Join-Path $RunRoot 'artifacts'
$NugetFeed = Join-Path $ArtifactsRoot 'nuget'
$NugetConfig = Join-Path $ArtifactsRoot 'NuGet.Config'
$PackageCache = Join-Path $ArtifactsRoot 'packages-cache'
$PluginPackageRoot = Join-Path $ArtifactsRoot 'plugin-packages'
```

各有提交历史的独立仓主实施分支使用 codex/simplify-defenses；重复依赖副本使用各自基线的 detached worktree。若目标路径或分支已存在，RunRoot 和主分支使用同一时间戳后缀，不覆盖未知状态。

### 3.2 基线与差异保留

独立 Host 当前 HEAD 为 e8b78772b136b837060b89b3df8022ca99724c21。保留并带入以下已有增量：README、WorkspaceViewport、WorkspaceViewTests、未跟踪的 MODULES.md，以及本 PLAN.md。

Integration 当前 HEAD 为 0e35c38e9fc74c81f8df100f765883edcb94cb4d；保留其已有 staged/unstaged 改动。其 Host 子模块当前固定为 3ff2b4132090211df25eb1e2e80de2fcb05bf237。

准备步骤：

1. 逐仓记录 HEAD、index 中 gitlink 状态、普通 tracked 文件的 staged/unstaged 差异，以及必要未跟踪源文件清单。保存改前文件副本，作为最终回填的三方比较基线。
2. 普通 tracked 文件分别导出 staged、unstaged 二进制 patch；使用 Git --output 写入，不经 PowerShell 文本重编码。gitlink 与普通文件分开处理。
3. 从各仓实际 HEAD 建 worktree，先 git apply --index 恢复普通 staged patch，再普通应用 unstaged patch。
4. gitlink 按原 index 恢复：同时处理新增、修改和删除，不能只遍历仍存在的 gitlink。递归副本按各自来源和 HEAD 准备。
5. 逐项复制必要未跟踪源文件；不复制 .git、运行数据、.vs 或构建产物。
6. 对比隔离区与记录，确认已有 staged/unstaged 状态和源文件被保留，原目录及其 index 未改变。

隔离 Integration 的 Host 是明确例外：使用独立 Host 的 e8b78772 基线、已有工作区增量及本次方案文件；不使用原 Integration 的旧 Host 作为验收宿主。在隔离 Integration 中更新这一条 index：

```powershell
git -C $IntegrationRoot update-index --cacheinfo `
  160000,e8b78772b136b837060b89b3df8022ca99724c21,host/UmamusumeResponseAnalyzer
```

核对 index gitlink 等于隔离 Host 实际 HEAD；不提交。其余 gitlink 保留原始基线，不通过删除检查或批量重写引用消除差异。源仓发生基线漂移时先查明并保护新增工作，不把本文旧 SHA 强制覆盖到源仓。

不在原目录执行 reset、clean、强制 checkout 或整目录覆盖；不修改全局 Git safe.directory。需要时仅对已核实的精确仓根使用命令级 safe.directory。

### 3.3 MouseDebugger 的无提交仓例外

MouseDebugger 当前没有首个 commit，源码全部未跟踪，不能从 HEAD 建 worktree。

- 使用普通隔离源码副本，不为本任务创建首个提交或变更其分支。
- 复制并保存改前副本：.gitignore、NuGet.Config、README.md、MouseDebugger.csproj、DiagnosticRecorder.cs、MouseDebuggerPlugin.cs、ReportWriter.cs、RuntimeProbe.cs、WindowsSnapshot.cs，以及 tests 下的项目文件和两个 .cs 文件；执行时以实际源文件清单核对，不复制 bin/obj/artifacts。
- 回填只应用本次差异，保留原文件的未跟踪状态；不以新建仓或提交代替差异管理。

## 4. 包升级、构建隔离和工具修正

### 4.1 版本及来源

- Host Version 固定为 1.15.0.0，PackageVersion 固定为 2026.9.8；不因执行日期改变而自动递增。
- 将 Integration 既有 22 个插件及 MouseDebugger 的 Host PackageReference 升为 2026.9.8，递归依赖副本中的相同引用同步修改。
- 插件业务代码、自有 TurnInfo 和正式 NuGet.Config 来源契约保持不变。
- 新 Host 包只输出到本轮 NugetFeed。临时 NuGet.Config 精确把 UmamusumeResponseAnalyzer 映射到本地源，其他包映射到 nuget.org；使用独立 PackageCache。
- 检查实际 project.assets.json 中的 Host 版本和包路径，以及缓存中的包来源，确认消费本轮包。所有相关 restore/build 都显式传入本节参数。

```powershell
$restoreArgs = @(
    "-p:RestoreConfigFile=$NugetConfig"
    "-p:RestorePackagesPath=$PackageCache"
)
$noPackageArgs = @(
    '-p:GenerateUraPluginManifestOnBuild=false'
    '-p:PackageUraPluginOnBuild=false'
    '-p:DeployUraPluginToLocalAppDataOnBuild=false'
)
```

### 4.2 不共享同名项目的 obj/bin

顶层 LegendScenarioAnalyzer 和 AIRedirector/deps 内的副本，在统一 ArtifactsPath 下会获得相同 obj/bin 路径；串行构建不能消除这种覆盖。

默认方案：**各隔离 checkout 使用自己的默认 bin/obj；只把最终七个顶层 ZIP 收集到 PluginPackageRoot。** 不对整组插件传入共享 --artifacts-path、ArtifactsPath 或相同 BaseIntermediateOutputPath。

最小消费者脚本的唯一临时项目仍可使用自己的独立输出目录。新依赖或其他构建手段若按 1.1 采用，也必须保证不同物理项目的中间和输出路径不冲突。

### 4.3 实际 NuGet 消费验证

改造现有 Host eng/tests/VerifyUraPluginBuildTargets.ps1，保留无参数入口：

1. 在唯一临时目录打包当前 Host，从 nupkg/nuspec 读取版本。
2. 检查 reference assembly、两个 buildTransitive 文件及不存在 Host runtime DLL。
3. 构建最小 NuGet 消费者，编译实际插件 API 调用，包括修改后的两个 TurnInfo 属性。
4. 在消费者 deps/、tests/ 放入编译失败标记，证明包的默认源码排除实际生效。
5. 未声明 IsUraPlugin 时构建，确认无 manifest/ZIP。
6. 显式启用插件、manifest 和打包，输出仅写临时目录，部署始终关闭。
7. 检查 ZIP 中 manifest、主 DLL，以及未混入 Host、PDB、deps.json。
8. 失败保留产物并报告路径；删除历史版本常量及 XML 布局断言。

默认不创建 receipt、额外哈希清单或验证框架。Integration 的 VerifyPublicationBoundary.ps1 从其 Host 项目读取 PackageVersion，替换固定版本检查；保留其余 gitlink、源码依赖、仓库和发布边界检查。

### 4.4 smoke 源码定位

AIRedirectorSmoke、WinSaddleAnalyzerSmoke 的 FindRepositoryRoot 向上查找 URA-Plugins.Integration.slnx，再定位该根的 plugins/AIRedirector、plugins/WinSaddleAnalyzer。

校验解析后的完整路径位于该 Integration 根内且项目存在；找不到明确失败，不继续向外查找原独立仓。输出实际读取的源码路径。所有跨仓 smoke 的启动工作目录固定为当前被验收的 IntegrationRoot。

## 5. 七个新 ZIP 的真实业务验收

### 5.1 模式、来源和生命周期

在现有 PluginSmokeTests 中增加 --package-load 模式，用独立进程执行；在创建原项目引用实例和进入原烟测流程之前分流。默认增加一个 PackageLoadSmoke.cs，不新增生产加载接口或专用测试项目。

原默认模式及全部随机烟测保留，分别报告“源码实例行为”“包结构”“新 ZIP 业务执行”，不能混称。

- URA_PLUGIN_SMOKE_PACKAGE_ROOT 指向仅含本轮七个指定 ZIP 的目录，每个插件恰好一个包。
- 包复制到唯一临时运行目录的 Plugins；运行数据和插件配置使用该目录，复用 WorkspaceSmokeSession 及现有数据库 fixture。
- AIRedirector 保持禁用外部进程的默认配置；不启动用户 UmaAI 或新增测试辅助进程。
- GamePacketCollector 预写启用配置，保留四个既有 endpoint group，上传地址仅指向测试进程的 loopback 接收端。
- 用真实 PluginManager.Init、InitializeLoadedPlugins、TriggerStartedAsync 加载全部七个包。无失败插件，实际加载的名称集合与七包集合一致。
- 每个实例的 PluginMetadata.PackagePath 指向本轮 ZIP；程序集位于宿主创建的 collectible PluginLoadContext，不是默认上下文中的项目引用实例。
- 通过框架 PEReader 读取 ZIP 主 DLL 的 MVID，与实际加载程序集 ManifestModule.ModuleVersionId 比较；Host、Terminal.Gui 仍使用当前宿主共享程序集身份。
- 宿主从 ZIP 流加载 DLL，不使用 Assembly.Location 位于解压目录作为证据，不为测试修改生产加载器。
- finally 中先关闭插件宿主并排空后台任务，再释放 UI 和接收端；释放所有快照及插件强引用，沿用现有回收行为测试。

### 5.2 真实回调调用规则

本模式验证每个打包插件自身的业务结果，不把它声明为 HTTP 到 UI 的全链路测试；完整分发与生命周期继续由现有 Host/跨仓测试验证。

- 从实际宿主登记的 SnapshotAnalyzerRegistrations 取得 endpoint 快照，使用 AnalyzerDispatchContext，输入序列化后的 Gallop DTO 或 raw MessagePack。
- 每个案例执行快照中属于目标 ZIP 实例的注册项，保持注册顺序，断言实际执行数大于零。整个调用期间持有快照；每次 handler 调用保留 HotkeyManager.RegisterScope(owner)。
- 不调用项目引用插件工厂，不反射直调业务分析器方法，不以 MethodInfo 清单或“没有异常”代替结果断言。
- 纯 DTO/文件 fixture 可从现有烟测复用；只复用所需的数据构造，不运行其项目引用插件和状态准备。
- 不用 RandomDtoFactory 对默认加载上下文 EventLogger 的静态操作准备新 ZIP 状态。需要会话前置状态时，通过新 ZIP 实例的真实注册回调建立。
- 允许测试通过反射读取新 ZIP 实例或其实际程序集中的状态作结果断言；不得因此添加生产 setter、测试专用 API 或把默认程序集静态状态当成结果。

### 5.3 七项固定案例

依下表顺序执行，每项单独输出插件名称、调用数和结果。数据构造以表内既有 fixture 为事实来源，保留其输入字段；不要重新猜测协议字段。

| 插件 | 输入及现有 fixture 来源 | 必须断言的结果 |
|---|---|---|
| GamePacketCollector | /umamusume/gacha/exec，request/response 各 [0x80]；沿用合成 headers | loopback 收到 PUT，endpoint、SID、request/response 字节准确；返回 204 后 pending 文件消失 |
| EventLoggerPlugin | /umamusume/single_mode/exec_command；沿用 EventLoggerRuntimeSmoke.Run 的训练失败响应：result_state=1、chara_info/unchecked_event_array 为空 | 事件记录 workspace 中出现“训练失败！”及 WARN；切回 Bootstrap 后没有同内容的全局日志 |
| EventResponseAnalyzer | /umamusume/single_mode_legend/exec_command；沿用 AssertEventResponseAnalyzerLegendExecCommandRendersEventPanel 的 CreateExecCommandResponse(830241003) 和数据库 fixture | 新 ZIP 创建/更新的 events panel 显示该既有案例的事件、选项文本；不能只断言 panel 存在 |
| AIRedirector | /umamusume/single_mode_legend/check_event，连续输入同一 charaId 的 turn=31、32，使用共享 Gallop chara 字段 | 读取实际加载实例的 legendOutput/targetId，确认目标从指定 charaId/31 变为 charaId/32；只读结果，不注入状态，不启动进程 |
| LegendScenarioAnalyzer | /umamusume/single_mode_legend/check_event；沿用 EventLoggerRuntimeSmoke.CreateLegendResponse 和 WriteLegendBuffCsv 的纯 fixture，charaId=14、turn=31 | LegendScenarioAnalyzer workspace 的 training panel 显示该 fixture 的回合及非空训练属性内容；字段值逐项对照输入，不以 workspace 存在替代渲染结果 |
| RamenScenarioAnalyzer | /umamusume/single_mode_ramen/exec_command；沿用 EventLoggerRuntimeSmoke.CreateRamenResponse 的纯 fixture，charaId=813、turn=2 | RamenScenarioAnalyzer workspace 的 training panel 显示输入的回合、总属性、PT 及训练内容；断言使用 fixture 的具体值 |
| SendGameStatusPlugin | 沿用 SendGameStatusPluginSmoke.CreateWritableOnsenResponse，通过对应 Onsen check_event endpoint 的真实注册项执行 | PluginData/SendGameStatusPlugin/GameStatusSend_Onsen 下生成 thisTurn.json、turn7.json；解析内容并对照 fixture 的回合和状态字段，不能只查文件存在 |

现有 fixture 的数据构造若位于局部函数中，将所需的纯数据构造移到可复用测试代码；不整段复制原业务执行 harness。默认复用现有依赖，不为这七项建立另一套插件模型。

loopback 接收端默认使用已有 Watson.Lite；捕获请求并返回 204，等待完成上限 10 秒，使用完成信号和有界条件等待，不固定长 sleep。真实账号数据、用户配置及外部服务不参与本模式。

## 6. 验收矩阵、顺序和命令

### 6.1 基线与执行顺序

前序只读基线仅包含相关测试 155 通过、1 跳过、0 失败；跳过项为缺少带 canonical URL 请求语料的 DispatchRequest_DeliversRequestCorpusDtos。这不是全套结果。

执行顺序：

1. 准备隔离基线，保存原文件、index、随机生成器和性能比较基线。
2. 在改动生产代码前运行最终会使用的既有 Host 全套和跨仓 smoke，分别记录失败、环境条件和跳过；新增加的 ZIP 模式等没有旧实现，不伪造其改前通过结果。
3. 完成 A–C，运行分析器、生命周期、热重载和热键相关测试。
4. 完成 D–F，运行配置、TurnInfo、诊断及程序集回收测试。
5. 完成 G–H，运行合成消费者、随机生成对照和全目录 DTO 测试。
6. 完成包升级、构建脚本、源码定位及新 ZIP 模式；执行完整隔离验收和性能比较。
7. 更新文档，准备可审查的回填差异，回填并执行原目录验收。

### 6.2 行为矩阵

| 修改 | 必须证明 |
|---|---|
| A、B | 跨 await、嵌套状态正确；回调内 self-unload/shutdown 快速失败；外部卸载正常 |
| C | 方向隔离、raw/DTO 顺序、同优先级顺序、原子注册、注销和热重载 |
| C 租约 | 首个回调阻塞时后续选中插件不能卸载；释放后完成；异常不遗留租约 |
| D | 输入表逐项覆盖；直接 null、null 别名、列表 null 不漏检；未知字段保存后消失 |
| E | 当前 DTO 可见、重复键失败、溢出语义保持、新包消费者实际读取属性 |
| F | 插件名、原始路径、消息、堆栈及多个失败可诊断；普通失败隔离；ALC 可回收 |
| G、H | 合成消费者真实运行；两个 profile 改前/改后一致；全部既有随机和场景烟测保留 |
| 包与隔离 | 23 插件用本轮包编译；七个新 ZIP 各自业务通过；无输出路径覆盖；源码定位在本轮目录 |
| UI | 既有首帧、Workspace、输入、日志复制及关闭测试通过 |
| 回填 | 只回填本次增量；保留已有工作和 index；原 Integration 的 Host 同步并实际通过验收 |

保留并通过以下关键测试：

- AnalyzerSnapshotLeasesLaterGenerationUntilBlockedCallbackCompletes
- SamePriorityDispatchPreservesPackageAndDeclarationOrder
- FailedInitializePublishesNeitherAnalyzerNorBackgroundOperation
- FailedStartedCallbackPublishesNeitherAnalyzerNorBackgroundOperation
- UnloadCancelsAndDrainsBackgroundBeforeDispose
- PluginOwnedHotkeyInFlightBlocksUnloadAndCloseRejectsNewCallback
- AnalyzerCallbackAwaitingSelfUnloadFailsFastWithoutDeadlock
- AnalyzerCallbackAwaitingShutdownFailsFastWithoutDeadlock

### 6.3 构建和运行

以下每个 native 命令后立即检查 LASTEXITCODE；非零即该项失败，不能让后续命令覆盖错误状态。变量按 3.1、4.1 定义。运行原目录验收时按第 7 节替换根目录变量，继续使用保留的本地包源。

Host 全套：

```powershell
dotnet restore "$HostRoot\UmamusumeResponseAnalyzer.sln"
dotnet build "$HostRoot\UmamusumeResponseAnalyzer.sln" -c Debug --no-restore
dotnet test "$HostRoot\UmamusumeResponseAnalyzer.Tests\UmamusumeResponseAnalyzer.Tests.csproj" -c Debug --no-build
dotnet run --project "$HostRoot\UmamusumeResponseAnalyzer\UmamusumeResponseAnalyzer.csproj" -c Debug --no-build -- --version
```

先构建当前 Host 的 Release 实现并打包到 NugetFeed，再消费新包：

```powershell
dotnet build "$HostRoot\UmamusumeResponseAnalyzer\UmamusumeResponseAnalyzer.csproj" -c Release -m:1 @noPackageArgs
if ($LASTEXITCODE -ne 0) { throw 'Host Release build failed.' }
dotnet pack "$HostRoot\UmamusumeResponseAnalyzer\UmamusumeResponseAnalyzer.csproj" `
    -c Release --no-build --output $NugetFeed @noPackageArgs
if ($LASTEXITCODE -ne 0) { throw 'Host package creation failed.' }
```

普通跨仓构建：

```powershell
dotnet build "$IntegrationRoot\URA-Plugins.Integration.slnx" -c Release -m:1 @restoreArgs @noPackageArgs
```

跨仓 EXE smoke 名称固定如下，逐项运行；PluginReplaySmoke 只走后面的显式语料命令，不在普通循环重复运行：

```powershell
$crossTests = @(
    'AIRedirectorSmoke', 'AnalyzerHistoryConfigSmoke', 'LegendScenarioAnalyzerSmoke',
    'PluginRuntimeSmoke', 'PluginWorkspaceLifecycleSmoke', 'RamenScenarioAnalyzerSmoke',
    'SendGameStatusPluginSmoke', 'SkillEffectPluginSmoke',
    'SkillTipsResponseAnalyzerSmoke', 'WinSaddleAnalyzerSmoke'
)
Push-Location $IntegrationRoot
try {
    foreach ($name in $crossTests) {
        dotnet run --project "$IntegrationRoot\tests\$name\$name.csproj" -c Release --no-build --no-restore
        if ($LASTEXITCODE -ne 0) { throw "Smoke failed: $name" }
    }
    dotnet run --project "$IntegrationRoot\tests\PluginReplaySmoke\PluginReplaySmoke.csproj" `
        -c Release --no-build --no-restore -- --corpus 'F:\Desktop\ramen_full_game'
    if ($LASTEXITCODE -ne 0) { throw 'PluginReplaySmoke failed.' }
}
finally { Pop-Location }
```

Minimal Release 项目固定为 AIRedirector、EventLoggerPlugin、EventResponseAnalyzer、GamePacketCollector、LegendScenarioAnalyzer、RamenScenarioAnalyzer、SendGameStatusPlugin，项目路径均为 plugins/<名称>/<名称>.csproj。

逐项目构建 Release、win-x64、SelfContained=false、PlatformTarget=AnyCPU；显式 GenerateUraPluginManifestOnBuild=true、PackageUraPluginOnBuild=true、DeployUraPluginToLocalAppDataOnBuild=false，同时传 restoreArgs，不传共享 ArtifactsPath。

每个顶层项目完成后，用同一构建参数读取 UraPluginPackagePath，核实文件存在和 manifest 的 InternalName，再复制到本轮 PluginPackageRoot。只收集这七个顶层包，不递归扫描依赖副本并择一。收集完成检查恰好七个。

PluginSmokeTests 用相同 Release/RID/Platform 参数构建，传 restoreArgs 和 noPackageArgs。通过单属性求值取得真实 TargetPath：

```powershell
$PluginSmokeProject = Join-Path $IntegrationRoot 'tests\PluginSmokeTests\PluginSmokeTests.csproj'
$PluginSmokeDll = dotnet msbuild $PluginSmokeProject -nologo -getProperty:TargetPath `
    -p:Configuration=Release -p:RuntimeIdentifier=win-x64 -p:SelfContained=false `
    -p:PlatformTarget=AnyCPU @restoreArgs @noPackageArgs
if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $PluginSmokeDll)) {
    throw 'Cannot resolve the freshly built PluginSmokeTests TargetPath.'
}
$previousPackageRoot = $env:URA_PLUGIN_SMOKE_PACKAGE_ROOT
Push-Location $IntegrationRoot
try {
    $env:URA_PLUGIN_SMOKE_PACKAGE_ROOT = $PluginPackageRoot
    dotnet $PluginSmokeDll
    if ($LASTEXITCODE -ne 0) { throw 'Source/package-structure smoke failed.' }
    dotnet $PluginSmokeDll --package-load
    if ($LASTEXITCODE -ne 0) { throw 'ZIP business smoke failed.' }
}
finally {
    $env:URA_PLUGIN_SMOKE_PACKAGE_ROOT = $previousPackageRoot
    Pop-Location
}
```

MouseDebugger 必须使用本轮 Release 实现程序集，不能使用 NuGet reference assembly或错误目录 DLL：

```powershell
$HostDll = Join-Path $HostRoot 'UmamusumeResponseAnalyzer\bin\Release\net10.0\UmamusumeResponseAnalyzer.dll'
$MouseTest = Join-Path $MouseRoot 'tests\MouseDebugger.Tests.csproj'
if (-not (Test-Path -LiteralPath $HostDll)) { throw 'Host Release runtime DLL is missing.' }
dotnet build $MouseTest -c Release -m:1 "-p:UraHostAssembly=$HostDll" @restoreArgs @noPackageArgs
if ($LASTEXITCODE -ne 0) { throw 'MouseDebugger build failed.' }
dotnet run --project $MouseTest -c Release --no-build --no-restore "-p:UraHostAssembly=$HostDll"
if ($LASTEXITCODE -ne 0) { throw 'MouseDebugger self-check failed.' }
```

同时执行 VerifyPublicationBoundary.ps1 和改造后的 VerifyUraPluginBuildTargets.ps1。以上 EXE 项目不能用 dotnet test 代替运行。

Host 请求语料由 PacketCorpus/URA_PACKET_CORPUS 定位，Integration 回放使用上面的 --corpus；两者分别报告解析和实际执行数量。缺失请求语料单独记录，不把响应回放当作请求验证。

### 6.4 性能验收

- 使用相同固定输入，比较改前/改后的完整 SnapshotAnalyzerRegistrations + Dispose，包含 endpoint 查找、准入和释放。
- 匹配注册项固定，分别加入零个、一半目录、全部目录的无关 endpoint 注册项；索引构造不混入逐包测量，构造时间及存储成本单独报告。
- 增加 B 的同步初次进入、同一 owner 嵌套、不同 owner 嵌套上下文样本；跨 await 的正确性由行为测试验证，不把跨线程分配计入当前线程探针。
- TotalStats 改后 getter 分配为零；两个集合 getter 分别测空数组与六项非空数组。
- 预热后记录五次耗时与分配的中位数；同步分配默认用 GC.GetAllocatedBytesForCurrentThread()。输出结果相同，允许分配持平，不允许上述样本出现分配回退。
- 不设提速百分比，不把墙钟时间写成 CI 断言，不把一次噪声当作性能退化；怀疑退化时在相同条件下复核并定位。
- 默认使用小型临时比较程序。若引入现成基准工具能按 1.1 显著降低自维护代码或测量复杂度，允许采用并说明替代关系。

新增失败必须解决；不得删断言、缩小语料、换回旧包或增加 fallback 取得通过。与改前相同的外部环境问题单独列明，未执行的检查不能标记通过。

## 7. 原工作区回填与验收

### 7.1 精确映射

| 隔离结果 | 回填目标 |
|---|---|
| integration/host/UmamusumeResponseAnalyzer | K:\repos\UmamusumeResponseAnalyzer，以及 K:\repos\URA-Plugins\URA-Plugins.Integration\host\UmamusumeResponseAnalyzer |
| integration 外层文件 | K:\repos\URA-Plugins\URA-Plugins.Integration 外层对应文件 |
| integration/plugins/<名称> | K:\repos\URA-Plugins\<名称> 和原 Integration/plugins/<名称> |
| 对应递归依赖副本 | 两侧原仓的同一 deps 相对路径；逐副本保留其原始基线 |
| MouseDebugger | K:\repos\URA-Plugins\MouseDebugger |

普通文件只回填“保存的初始工作状态 → 隔离最终状态”的本次差异，不能以 HEAD → 隔离最终状态混入初始脏改动，更不能整目录覆盖。原 index 默认不改，新增修改保持未暂存，原未跟踪文件保持未跟踪。

唯一额外基线同步是原 Integration 内 Host：它需要从 3ff2b413 升到 e8b78772，并带入独立 Host 已有工作区增量、本次修改和对应文档，才能与独立 Host 行为一致。

### 7.2 回填前检查和危险操作确认

1. 完成全部隔离修改与验收，先给出逐仓、逐路径回填清单和差异。
2. 再读原工作区 HEAD/index/文件，与准备时保存的状态比较。没有重叠的后续修改自动保留；存在重叠时用“初始文件、隔离结果、当前原文件”三方比较合并。不能唯一确定语义的冲突让用户决定，不能覆盖用户的新工作。
3. 保留可恢复的原文件副本和原 index/gitlink 记录。对实际 checkout 或覆盖风险操作，按仓库规则列出精确命令、路径、变化、远端影响及恢复办法，再做必要确认；不要把整个实现阶段停在提前的笼统许可上。
4. 对当前干净的原 Integration Host，默认基线同步操作为切到 e8b78772 的 detached checkout。若 commit 对象缺失，只从独立 Host 的本地路径取该对象，不访问或改写远端。执行时若源状态变化，重新计算可审查操作，不强制套用此步骤。

默认基线同步命令的作用目录为 K:\repos\URA-Plugins\URA-Plugins.Integration\host\UmamusumeResponseAnalyzer；是否需要本地 fetch 由 git cat-file -e '<SHA>^{commit}' 检查决定：

```powershell
$OriginalIntegration = 'K:\repos\URA-Plugins\URA-Plugins.Integration'
$OriginalIntegrationHost = Join-Path $OriginalIntegration 'host\UmamusumeResponseAnalyzer'
# 仅在目标仓缺少该 commit 对象时执行：
git -C $OriginalIntegrationHost fetch --no-tags 'K:\repos\UmamusumeResponseAnalyzer' e8b78772b136b837060b89b3df8022ca99724c21
# 完成针对实际差异的必要确认后，且已保护该子仓工作状态：
git -C $OriginalIntegrationHost switch --detach e8b78772b136b837060b89b3df8022ca99724c21
```

完成代码回填后，只对原 Integration 的 Host gitlink 作本次需要的 index 更新：

```powershell
git -C $OriginalIntegration update-index --cacheinfo `
  160000,e8b78772b136b837060b89b3df8022ca99724c21,host/UmamusumeResponseAnalyzer
```

这一条 index 变化是已明确的回填例外，报告中单列。gitlink 只能记录 HEAD，不能记录未提交修改；本次代码仍在各工作区，不能声称该 gitlink 已固定本次未提交成果。其他 index 状态不因回填被统一暂存。

### 7.3 回填后验收

- 独立原 Host 运行 Host 全套测试及 --version。
- 把 IntegrationRoot 改为原 Integration 路径、HostRoot 改为其 Host 子模块路径、MouseRoot 改为原 MouseDebugger 路径，按第 6 节重新构建并执行跨仓 smoke 和 MouseDebugger 自测。
- 原独立 22 插件也逐项目编译，确认两侧包引用同步。项目相对路径取 Integration.slnx 中 plugins/ 下的既有项目条目，再映射到原独立插件父目录；不能假设 csproj 都在仓根，例如 PioneerScenarioAnalyzer 的项目还有一层同名子目录。原递归副本同样核对，无需发布或提交。
- 七个 ZIP 从回填后的顶层源项目重新构建，收集到新的唯一验证目录，再执行默认烟测和 --package-load；不能只复用隔离阶段的旧 ZIP。
- 核对原 Integration gitlink 与实际子仓 HEAD、原有 staged/unstaged/未跟踪工作的保留情况。
- 本地未发布的 Host 包及临时 NuGet 源/缓存保留用于复现。正式 NuGet.Config 不写入机器路径；交付提供本轮源参数命令，不能假定未发布的 2026.9.8 已可从正式源恢复。

## 8. 文档与完成标准

文档随实际实现更新：

- README：配置默认值、未知字段读写规则、错误定位方式。
- MODULES：责任变化、共享测试文件及有效链接。
- Host/Integration 构建说明：新版本、本地包源参数、默认输出隔离、源码烟测与 ZIP 业务模式的区别。
- MouseDebugger 说明：真实 Host DLL 参数、EXE 自测及无首个提交仓的当前交付状态，避免写入与实际无关的历史说明。
- AGENTS 仅修正与代码事实冲突的命令或版本，不把本次实现手段授权扩写成其他项目的全局规则。

完成报告必须包含：

- A–H 完成状态及删除的维护负担；若按 1.1 改用依赖、框架或其他实现，说明替代内容和实际净收益。
- 公开返回类型、nullable 标注和版本变化。
- 23 插件编译结果，七个新 ZIP 各自业务结果；源码行为、包结构、ZIP 业务分别报告。
- 随机生成对照、语料覆盖/跳过及 UI/生命周期验收。
- 分配和耗时比较、索引构造成本及任何限制。
- 隔离路径、分支、基线、MouseDebugger 副本、逐仓回填结果、Host gitlink 例外，以及已有工作保留情况。
- 回填后真正执行的检查、未解决失败或环境限制，以及保留的本地包源和复现命令。

完成标准是指定功能简化落地、约定行为验收通过、文档一致、原工作区正确回填，且没有范围外功能改造或未经请求的提交、推送、发布、部署。代码行数和测试数量不是单独的完成标准。
