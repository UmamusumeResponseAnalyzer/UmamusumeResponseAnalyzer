# UmamusumeResponseAnalyzer

UmamusumeResponseAnalyzer 是基于 Terminal.Gui 的本地 TUI 宿主。它接收游戏请求/响应的 MessagePack payload，按 Gallop endpoint catalog 分发给已安装插件；插件 live state 显示在 workspace，notification 由 Host overlay 呈现，interactive Host session 中捕获的异常集中记录到启动 workspace。

# 前置 Prerequisite

* 任意可以把游戏请求/响应 MessagePack payload 发送到宿主 `/notify/request` / `/notify/response` 的 sender。请求必须带 `X-Hachimi-Game-Url` header，值为游戏原始 canonical URL；该 URL 的 path 必须命中 Gallop endpoint catalog，或能在带/不带 `/umamusume` 前缀两种形式之间切换后命中 catalog。Windows 安装入口使用 [Hachimi-Edge](https://github.com/kairusds/Hachimi-Edge) 和 [HTTP 转发插件](https://github.com/UmamusumeResponseAnalyzer/hachimi-httpforward-plugin)。
* sender 的目标地址默认设置为 `http://127.0.0.1:4693`。如果游戏在手机或其他设备上运行，首次运行向导可把监听地址改为 `0.0.0.0`；启动时按控制台提示放行防火墙。
* Windows 版主菜单提供 `安装 Hachimi-Edge`，支持 DMM 日服、Komoe 繁中、Steam 日服及国际服；可选择自动发现的目录或手动选择游戏 EXE。关闭目标游戏后操作，一次安装一个目录。
* (可选，如果需要脱离 DMM 启动游戏) DMM Game Player β 及 HTTPS proxy，比如 [Fiddler](https://www.telerik.com/fiddler/fiddler-classic) 或 [mitmproxy](https://mitmproxy.org/)。

# 安装 Installation

* 在 [Release](https://github.com/EtherealAO/UmamusumeResponseAnalyzer/releases) 页面下载最新版本程序。
* 将程序放在任意位置，运行 `UmamusumeResponseAnalyzer.exe`。
* 普通启动要求 stdin 和 stdout 连接到 interactive terminal；redirected stdin/stdout 会以明确错误退出。`--version`、`--update <savePath>`、`--enable-dll-redirection` 等 CLI-only 路径不启动 TUI。
* 首次运行按向导选择运行设备、服务器目标(日服 Cygames / 繁中服 Komoe)、事件数据语言和训练员性别。
* 返回主菜单后先选择 `更新数据文件`。数据文件用于事件、技能、名称等本地解析；不完整或损坏时数据库保持不可用，插件不会初始化，HTTP server 也不会启动，程序会提示更新全部数据文件后重启。技能进化条件 `Type=0/None` 仅使用服务器完成状态，不做本地预测。未定义的技能进化条件类型只记录 warning，并按条件未满足处理，不阻断完整数据快照加载。
* 进入 `插件仓库`，安装需要的功能插件。没有插件时宿主仍会启动，并提供启动信息、异常记录、通知和基础分发能力。
* 选择 `启动！` 后会立即进入 Terminal.Gui 的全屏启动 workspace；数据加载、插件初始化和 HTTP server 启动在后台推进。启动 workspace 在整个 Host session 内持续存在，显示运行环境、初始化结果、插件摘要和全局最近日志；最近日志收集宿主/插件日志和 Host 捕获的异常，最多保留 128 条。数据库加载警告、插件扫描/加载/安装诊断、程序更新文件损坏和请求分析异常还会显示 Host overlay notification；写入日志或显示 notification 均不会切换当前 workspace。异常行可右键打开 context menu，`复制完整 backtrace` 会将完整异常链和 stack trace 写入系统 clipboard；普通日志不提供该操作。四个区域的尺寸只随 viewport 变化；超出区域的表格与日志使用 Terminal.Gui 原生滚动条，内容增长不改变 panel 布局。启动状态以紧凑结果行实时更新，不显示额外 header 或 footer。

# 运行与文件位置

* 默认工作目录为 `%LocalAppData%\UmamusumeResponseAnalyzer`。启动时如果当前目录存在 `.portable` 文件夹，工作目录会改为 `./.portable`。
* `config.yaml`、数据文件、`Plugins/` 和 debug `packets/` 都写在工作目录下。
* 默认监听 `http://127.0.0.1:4693`。`/notify/ping` 返回 `pong`，可作为 smoke test。
* 启动、设置和插件设置使用占满 terminal 且无外边框的 Terminal.Gui 原生 `Menu` 页面；mouse hover 或 `Up` / `Down` 移动当前菜单项，click 或 `Enter` 直接激活。插件仓库、更新、安装 Mod 和字段编辑使用各自的 dialog，完成后返回对应的上级菜单。启动后按 `/`，或在 focused control 未使用 `Enter` 时，打开 Command Mode；内置命令需以 `/` 开头。Command Mode 使用 Terminal.Gui 原生 `TextField` 输入，支持 `Tab` 补全、`Up` / `Down` 浏览当前进程内提交过的命令、`Esc` 取消和 `Enter` 执行。底部全宽带框 overlay 显示 ` Command Mode ` 标题、补全候选及 `+N more`、`❯` 输入区和操作 footer；窄窗口或高度不足时使用紧凑布局。鼠标移到 terminal 最底部会显示覆盖在 workspace 上的居中 taskbar popup，不占用 workspace 布局空间；过长标题会随 terminal 宽度以 `…` 缩略，当前项和 hovered 项优先显示完整标题。click workspace title 直接切换，按住左键左右拖拽可调整并持久化 taskbar 顺序；该顺序只影响 taskbar。当前项和 hover 由背景属性区分。Command Mode 显示时 taskbar 被抑制，并拥有更高的图层优先级。`/workspace` 或 `/workspace switch` 打开 workspace 选择器，`/workspace switch <title>` 直接切换；标题也可用双引号包围，其中 `\"` 和 `\\` 分别表示双引号和反斜杠。`/workspace list` 列出 workspace。workspace 内容超出终端时默认显示底部；`Up` / `Down` 按行滚动，`PageUp` / `PageDown` 按页滚动，`Home` / `End` 跳到顶部或底部。`Left` / `Right` 不参与 workspace viewport 滚动；focused view 未处理时继续匹配已注册 hotkey。Terminal.Gui 主界面中，滚轮产生与无 modifier 的 `Up` / `Down` 相同的 workspace viewport 滚动效果；滚轮不触发快捷键，popup 或 Command Mode 存活时会被忽略。Host dialog 的 `TextField` 与 `ListView` 使用 Terminal.Gui 原生 whole-view hover；focus 视觉优先于 hover，hover 不改变 focus、selection 或 marked 状态。`Button`、菜单及插件提供的 `View` 使用各控件自身的 Terminal.Gui 默认行为。workspace/plugin popup 保持 mouse click-through。popup 或 Command Mode 存活时由其优先处理按键；workspace 已到边界时按键继续交给已注册 hotkey。`/plugin` 或 `/plugin list` 列出插件运行期状态，`/plugin load|unload|reload <InternalName>` 只改变当前进程内加载状态，不安装、不删除插件文件、不写禁用配置。Command 结果始终写入全局最近日志；只有 message 而没有 display 的结果同时显示 global notification，有 display 的结果改用 popup 呈现且不重复通知。插件更新 workspace panel 时默认会切到该 workspace；bootstrap 刷新和异常写入不会切换当前 workspace。按 `Ctrl+B` 返回启动 workspace，按 `P` 查看已加载插件列表，按 `Ctrl+C` 退出程序。
* 程序启动后会检查已加载插件是否有新版本；发现更新时只通知，不自动安装。更新插件需要进入 `插件仓库` 手动选择。
* 更新数据文件时会先写入临时文件，下载成功后替换目标文件；失败时清理临时文件并保留已有文件。
* 开启 debug packet 保存后，请求写为 `Q`、响应写为 `R` 的 `.msgpack` 文件，文件名包含时间戳、UUIDv7 和 API endpoint path（`/` 写为 `-`）；`DEBUG` 构建额外写 `.json`，文件名只包含时间戳和 `Q`/`R`，完整 canonical URL 写在 JSON 内容中。`packets/` 中超过一天的旧文件会在下次保存时清理，单个旧文件清理失败不会中断当前请求/响应分析。

# 检查安装 Checking

`安装 Hachimi-Edge` 每次按所选客户端取得 URACloud 当前生效组件，校验长度、SHA-256 和 Windows x64 PE 架构，再覆盖固定文件。安装、更新和同版本重装使用同一动作；该客户端尚无完整生效配置时不能安装。

| 客户端 | Hachimi-Edge 目标 | 额外文件 |
|---|---|---|
| DMM 日服 | `umamusume.exe.local/UnityPlayer.dll` | Cellar → `umamusume.exe.local/apphelp.dll` |
| Komoe 繁中 | `winhttp.dll` | — |
| Steam 国际服 | `cri_mana_vpx.dll` | — |
| Steam 日服 | `cri_mana_vpx.dll` | FunnyHoney → `UmamusumePrettyDerby_Jpn.exe` |

所有客户端均安装 `hachimi/hachimi_httpforward_plugin.dll`，将 `hachimi\hachimi_httpforward_plugin.dll` 加入 `hachimi/config.json` 顶层 `load_libraries`，并设置 `hachimi/httpforward.json` 的 `notifier_host`。目标地址自动取自当前 URA 监听设置，通配地址转换为回环地址。已有超时与其他设置保留；首次超时为 100 ms。损坏的 JSON 或字段类型错误会阻止安装。

选择游戏目录后自动准备组件并安装。固定列表之外的文件保留，其他加载器可能与本次组件冲突。只有目录权限或 DMM 的 `DevOverrideEnable` 设置需要时才请求提权；首次启用 DLL redirection 后须重启 Windows。

安装直接覆盖目标文件，不保留备份。单个文件写入完成后再替换目标；安装中途失败时，已覆盖的文件保留，排除错误后重新安装。目标文件的符号链接替换为普通文件，链接指向的文件保持原样；目标路径中的目录链接会阻止安装。

The Windows installer applies the selected client's active URACloud component set to one game directory and preserves unrelated files and settings. It overwrites target files without backups; files already replaced remain in place if installation fails. Fix the error and run the installation again. Installation success and actual game traffic reception are separate checks.

* 在 URA 中选择 `启动！`；浏览器或命令行访问 `http://127.0.0.1:4693/notify/ping`，返回 `pong` 只说明宿主 HTTP server 可达。
* 启动游戏后，前往殿堂马列表、竞技场选择对手或查看好友信息。若 workspace 中出现插件输出，说明 sender、header 和插件分发配置正确。

# 插件仓库与 URACloud

* `插件仓库` 从 `https://ura.shuise.net/api/Plugins` 读取正式版目录，按配置目标过滤并按分类排序。宿主使用 manifest 展示插件与版本，repository ID / Release ID 用于构造 URACloud 下载地址；单一版本直接安装，多个版本由用户选择。安装只处理所选插件，不自动增加 manifest `Dependencies`；未声明 `Targets` 时视为所有目标可用。
* 同名来源可并列展示，一次不能选择多个同名插件。本机同一 `InternalName`（OrdinalIgnoreCase）只能安装一个，ZIP 位于 `Plugins/<InternalName>.zip`；宿主只扫描 `Plugins/` 顶层 ZIP。
* 下载先写临时文件，核对 manifest 的 Author、InternalName、Version 和 ZIP 包契约后替换目标 ZIP，上限 64 MiB。下载或校验失败保留现有 ZIP。菜单安装结束后批量热重载。
* 更新检查仅处理已加载插件，按 `InternalName` 匹配并比较版本；同名多个来源使匹配不唯一时明确报错。发现更新只通知，安装由用户手动选择。
* 本地 `/uracloud/install` 接受白名单 Origin（`https://ura.shuise.net` 或 `http://localhost:5173`）的 `{repositoryId, releaseId}`，下载源固定为 URACloud。宿主获取 manifest 后在本机确认插件身份与版本，并提示插件将在本机执行代码。网页可指定预发行 Release。
* 安装响应 `{ok, loaded, installed, error}` 区分 ZIP 保存与加载结果：保存后加载失败返回 `ok: true, loaded: false`。`/uracloud/status` 返回 Host 版本和已加载插件，其中 `loaded: true, source: null`；网页会显示来源未知，不能标记已安装同一 Release。

Host **1.15.0.0** uses repository and release IDs to address URACloud downloads. Web requests require local confirmation of the plugin identity and version; downloads validate the package structure and manifest identity before replacing the ZIP. Status and version checks cover loaded plugins. Status reports no source identity; installation and loading results are separate.

# 插件开发 Plugin Development

* 插件直接引用宿主程序集与 Terminal.Gui。宿主公开 `IPlugin`、`AnalyzerAttribute`、workspace UI contract 和 Gallop DTO/endpoint catalog；插件源码使用 `UmamusumeResponseAnalyzer.Plugin`、`UmamusumeResponseAnalyzer.TerminalGui`、`Terminal.Gui.ViewBase`、`Terminal.Gui.Views`、`Gallop`、`Gallop.Endpoints` 命名空间。
* 插件包只能是 `Plugins/<InternalName>.zip`。ZIP 根目录必须且只能有一个 `manifest.json`，并且必须且只能有一个名为 `<InternalName>.dll` 的主程序集；根目录中的其它 managed DLL 是插件依赖程序集，卫星资源 DLL 放在对应 culture 子目录。标准打包产物只包含这些运行时文件，不包含 PDB、deps.json 或临时 metadata。ZIP 文件名必须等于 manifest `InternalName`；manifest `InternalName`、根主 DLL 文件名（不含扩展名）和主 DLL 内嵌 `AssemblyName` 必须一致。
* `manifest.json` 必须精确包含 `Author`、`InternalName`、`DisplayName`、`Description`、`Changelog`、`Version`、`Dependencies`、`Targets`、`RepositoryUrl`、`LastUpdate`、`Category`、`Homepage`。`Dependencies` 声明直接软联动的插件 internal name：目标未进入本轮运行期时忽略该边且 Consumer 独立加载；双方均进入本轮运行期时，宿主按声明顺序先初始化目标插件、按反向顺序卸载，并让依赖连通组共享 collectible load context。已安装图中的自依赖、重复名称和依赖环使插件加载失败。Consumer 通过 `IPluginContext.IsPluginAvailable` 判断本轮共享组中的声明目标；可选插件类型不得出现在 Consumer 导出类型签名或启动必经字段初始化中。`Targets` 为空、命中配置的 repository targets，或配置未设置 targets 时，宿主才创建插件实例并使其进入运行期；否则 ZIP 保持安装但插件不初始化。
* 宿主提供基础 `TurnInfo` / `CommandInfo` 领域视图。UAF、L'Arc、Cook、Mecha、Legend、Pioneer、Onsen、Breeders 等场景专用聚合模型由场景插件基于 Gallop DTO 派生。
* 精确请求/响应 analyzer 可以使用 endpoint attribute，例如 `[ResponseAnalyzer<GameApi.Account.Index>] ValueTask Analyze(DataLinkIndexResponse response)`。方法必须返回 `ValueTask`，并且只能有一个闭合具体 Gallop DTO 参数；DTO 类型必须精确匹配 endpoint descriptor 对应方向的 payload 类型。同一个方法可以挂多个 analyzer attribute，但这些 attribute 必须要求同一个 DTO 类型。
* 程序化 analyzer 统一使用 `context.Analyzers.Register<TPayload>(AnalyzerKind kind, IReadOnlyList<EndpointPattern> patterns, Func<AnalyzerInvocation<TPayload>, ValueTask> handler, int priority = 0)`。DTO 的 `TPayload` 必须是宿主当前 catalog 中该方向的闭合具体 Gallop DTO；raw analyzer 使用 `ReadOnlyMemory<byte>`。`AnalyzerInvocation<TPayload>` 提供匹配后的 descriptor、payload 和非 null 的 `GameHttpHeaders`，其中六个 header 值分别可为 `null`。
* `EndpointPattern.Exact`、`Wildcard` 和 `Regex` 都匹配 catalog 的 canonical path，区分大小写。wildcard 的 `*` 不跨 `/`；regex 以 `CultureInvariant`、`NonBacktracking` 和 100 ms timeout 匹配完整 path。每个 pattern 必须在注册时至少命中一个 catalog endpoint，多个 pattern 的命中结果会去重。
* attribute 和程序化 analyzer handler 都返回 `ValueTask`。程序化注册只能在 `Initialize` 或宿主调用的 `OnStarted` 回调中发生；同一回调内的 analyzer 与 background 注册在回调成功后原子生效，失败时不留下部分注册。
* 宿主按 `X-Hachimi-Game-Url` header 中的 canonical game URL 解析 path，并在带/不带 `/umamusume` 前缀两种形式之间查询 `GameEndpointCatalog.ByPath`；两种形式均未命中的数据会被静默丢弃。sender 可附带 `X-Hachimi-sid`、`X-Hachimi-app-ver`、`X-Hachimi-res-ver`、`X-Hachimi-viewerid`、`X-Hachimi-device`、`X-Hachimi-device-subtype`；raw analyzer 收到的是原始 MessagePack payload bytes。
* 分发以 raw payload 为基础；DTO analyzer 在执行点按 Gallop descriptor 反序列化，同一分发中的 DTO analyzer 共享反序列化结果。raw analyzer 和 DTO analyzer 都按 priority 顺序执行。
* Gallop DTO 的 `bool` 字段在反序列化时接受 MessagePack boolean 和 positive fixint `0` / `1`；其它整数编码会抛出 `MessagePackSerializationException`，序列化始终写入 MessagePack boolean。
* 插件必须实现 `Initialize(IPluginContext context)`；`Dispose()` 和 `ConfigPromptAsync(...)` 有默认空实现。`context.Application` 是宿主进程唯一的 `IApplication`，插件不得自行创建或释放 Terminal.Gui application。插件的配置与其它 dialog 直接使用该 application 和 Terminal.Gui 控件；宿主 modal helper 不属于插件 ABI。插件通过 `Workspace.Create(title)` 获取 workspace，`Workspace.Current` 读取当前 workspace；`SetPanel`、`RemovePanel`、`Notify`、`SwitchTo`、`BindHotkey` 和 `Remove` 均为 `Workspace` 实例方法。`SetPanel(key, title, content, fullBleed, switchToWorkspace)` 接受 `WorkspaceContent`，默认在 panel 更新时切到目标 workspace；静默刷新传 `switchToWorkspace: false`。`WorkspaceContent` 的 factory 每次返回一个未挂载的新 `Terminal.Gui.ViewBase.View`，View 的挂载和释放由宿主管理；纯文本可用 `WorkspaceContent.Text(...)`。`TerminalUi.Log(source, text, severity)` 写入启动 workspace 的全局“最近日志”，全局保留最新 128 条；`Workspace.Notify` 写入对应 workspace scope，`TerminalUi.Notify` 写入 global scope。普通 workspace 只显示 panel live state，notification 按现有 scope 由 Host overlay 显示。interactive Host session 中，`TerminalUi.LogException` 捕获的异常也写入全局“最近日志”，且不会切换 active workspace。插件 hotkey 通过 `HotkeyManager` 注册。`context.Events.OnStarted(Func<CancellationToken, ValueTask>)` 用于订阅宿主启动事件；`context.RunBackground(Func<CancellationToken, ValueTask>)` 把插件长期任务交给宿主管理。卸载时宿主先停止接收该 generation 的回调、取消并等待 background operation，再调用 `Dispose()` 和清理该插件的 analyzer、事件订阅及快捷键归属。`Initialize` 或 `OnStarted` 回调抛异常时不会提交该回调暂存的 analyzer/background 注册。
* `Notify(..., shortcuts: UiShortcut[])` 注册 notification TTL 内的临时快捷键；`HotkeyContext.BindShortcut(...)` 注册 popup 存活期间的临时快捷键。临时 handler 可重复触发，不关闭 popup、notification，也不延长 TTL；Command Mode 激活后由 focused `TextField` 处理输入，其他按键依次交给 popup shortcut、popup built-in、最新 TTL-live notification shortcut、workspace navigation 和持久 hotkey，仍未处理的 `/` 或 `Enter` 再打开 Command Mode。notification 未显示在 active workspace、窄屏或 overflow 中时，TTL 内的快捷键仍然有效。临时快捷键不写入持久 hotkey dictionary，也不自动渲染按键提示；workspace 删除、宿主停止和插件卸载会清理对应注册。
* `Workspace.Create(title)` 原样保留 title（包括边界空白），纯空白 title 非法；进程内以 `OrdinalIgnoreCase` 将 title canonicalize 为全局共享的 `Workspace`，所有调用方取得同一实例。`Workspace.Current` 始终返回当前 workspace；每个 Host session 都以不可移除的“启动” workspace 作为初始值和默认回退目标。普通 workspace 只显示各 panel 的当前 live state；同一 workspace 内的 panel key 以 `Ordinal` 区分并由所有调用方共享，`SetPanel(...)` 替换同 key panel，`RemovePanel(key)` 删除同 key panel并返回是否存在。`Workspace.Remove()` 清除普通 workspace 的 panel、滚动位置、notification/临时快捷键和 workspace hotkey；删除 active 普通 workspace 时回退到注册顺序首项，即“启动”。相同 title 此后再次 `Workspace.Create(...)` 会取得新的 generation；旧 handle 成为 tombstone，除重复 `Remove()` 外，`SetPanel`、`RemovePanel`、`Notify`、`SwitchTo` 和 `BindHotkey` 均立即抛出 `InvalidOperationException`；对“启动”调用 `Remove()` 会抛出 `InvalidOperationException`。
* Host 在面板或布局拆除前释放其中控件（含子控件及已有 adornment）的鼠标捕获。面板结构重建会结束其中正在进行的拖拽；普通重绘、滚动和尺寸调整保持捕获。其它窗口及任务栏的捕获不受影响；待移除控件的捕获若未能释放，Host 会报错并中止拆除，保留原界面层级。
* 热重载按 manifest `InternalName` 应用；热安装软联动目标时重载 Consumer 并合并共享组，热卸载目标时重建仍安装的关联插件，不把整组保持为卸载状态。
* 插件配置由插件自行维护。宿主不预创建插件数据目录，不自动读写配置文件，也不提供通用属性编辑器；菜单入口调用 `ConfigPromptAsync(IApplication application, CancellationToken cancellationToken = default)`，其中 `application` 与 `context.Application` 为同一个宿主实例。该方法可能在插件实例已加载、但 `Initialize` 尚未执行时调用，因此不得依赖 `Initialize` 产生的状态。插件需要配置或数据目录时，应在插件代码中创建目录、读取/校验自己的文件，用户取消时不得写入 draft，错误直接抛出明确异常。
