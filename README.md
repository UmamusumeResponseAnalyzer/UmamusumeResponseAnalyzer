# UmamusumeResponseAnalyzer

UmamusumeResponseAnalyzer 是本地运行的 TUI 宿主。它接收游戏请求/响应的 MessagePack payload，按 Gallop endpoint catalog 分发给已安装插件，并把插件输出、日志和通知显示在同一个 LiveDisplay 界面中。

# 前置 Prerequisite

* 任意可以把游戏请求/响应 MessagePack payload 发送到宿主 `/notify/request` / `/notify/response` 的 sender。请求必须带 `X-Hachimi-Game-Url` header，值为游戏原始 canonical URL；该 URL 的 path 必须命中 Gallop endpoint catalog。推荐 [ura-core](https://github.com/UmamusumeResponseAnalyzer/ura-core)。
* sender 的目标地址默认设置为 `http://127.0.0.1:4693`。如果游戏在手机或其他设备上运行，首次运行向导可把监听地址改为 `0.0.0.0`；启动时按控制台提示放行防火墙。
* Windows 版主菜单提供 `自动安装ura-core`。该入口会查找本机游戏目录，选择安装 Hachimi 或 umamusume-localify，并在启用 DLL redirection 时请求管理员权限。
* (可选，如果需要脱离 DMM 启动游戏) DMM Game Player β 及 HTTPS proxy，比如 [Fiddler](https://www.telerik.com/fiddler/fiddler-classic) 或 [mitmproxy](https://mitmproxy.org/)。

# 安装 Installation

* 在 [Release](https://github.com/EtherealAO/UmamusumeResponseAnalyzer/releases) 页面下载最新版本程序。
* 将程序放在任意位置，运行 `UmamusumeResponseAnalyzer.exe`。
* 首次运行按向导选择运行设备、服务器目标(日服 Cygames / 繁中服 Komoe)、事件数据语言和训练员性别。
* 返回主菜单后先选择 `更新数据文件`。数据文件用于事件、技能、名称等本地解析；缺失或损坏时程序会保留空集合并提示更新。
* 进入 `插件仓库`，安装需要的功能插件。没有插件时宿主仍会启动，但只提供日志、通知和基础分发能力。
* 选择 `启动！` 后程序会启动内置 HTTP server，并进入 LiveDisplay 界面；启动 workspace 显示配置摘要、数据加载、插件扫描/初始化和监听状态。

# 运行与文件位置

* 默认工作目录为 `%LocalAppData%\UmamusumeResponseAnalyzer`。启动时如果当前目录存在 `.portable` 文件夹，工作目录会改为 `./.portable`。
* `config.yaml`、数据文件、`Plugins/` 和 debug `packets/` 都写在工作目录下。
* 默认监听 `http://127.0.0.1:4693`。`/notify/ping` 返回 `pong`，可作为 smoke test。
* 启动后按 `/` 或 `Enter` 打开命令输入栏，按 `Ctrl+B` 查看启动信息，按 `P` 查看已加载插件列表，按 `Ctrl+C` 退出程序。
* 程序启动后会检查已加载插件是否有新版本；发现更新时只通知，不自动安装。更新插件需要进入 `插件仓库` 手动选择。
* 更新数据文件时会先写入临时文件，下载成功后替换目标文件；失败时清理临时文件并保留已有文件。
* 开启 debug packet 保存后，请求写为 `Q`、响应写为 `R` 的 `.msgpack` 文件，文件名包含 canonical URL；`DEBUG` 构建额外写 `.json`。`packets/` 中超过一天的旧文件会在下次保存时清理。

# 检查安装 Checking

* 浏览器或命令行访问 `http://127.0.0.1:4693/notify/ping`，返回 `pong` 说明宿主 HTTP server 已启动。
* 启动游戏后，前往殿堂马列表、竞技场选择对手或查看好友信息。若 LiveDisplay 中出现插件输出或相关日志，说明 sender、header 和插件分发配置正确。

# 插件仓库与 URACloud

* `插件仓库` 从 `https://ura.shuise.net/api/Plugins` 拉取插件目录，并按配置中的服务器目标过滤插件。插件自身未声明 `Targets` 时视为所有目标可用。
* 仓库安装会下载 ZIP 到 `Plugins/<InternalName>.zip`，随后尝试热重载。标记为 `[LoadInHostContext]` 或进入宿主上下文的插件需要重启才能生效。
* 同一 `InternalName` 的不同作者 fork 会在仓库列表中同时保留；本地同一时间只能安装其中一个来源。
* URACloud 网页集成挂在本地 `/uracloud/*`。`/uracloud/status` 返回当前 URA 版本和已加载插件；`/uracloud/install` 只接受 `{author, internalName, version}`，下载源固定为 URACloud 插件仓库，并且安装前必须在本机控制台确认。

# 插件开发 Plugin Development

* 插件直接引用宿主程序集。宿主公开 `IPlugin`、`AnalyzerAttribute`、LiveDisplay contract 和 Gallop DTO/endpoint catalog；插件源码使用 `UmamusumeResponseAnalyzer.Plugin`、`UmamusumeResponseAnalyzer.LiveDisplay`、`Gallop`、`Gallop.Endpoints` 命名空间。
* 插件可以放在 `Plugins/` 下：DLL 会递归扫描，ZIP 只扫描 `Plugins/` 顶层。ZIP 主插件 DLL 放在压缩包根目录，程序集名等于 ZIP 文件名；同级其它 DLL 作为依赖加载；卫星资源 DLL 可放在对应 culture 子目录。
* 宿主加载插件时按程序集名作为 internal name。插件 `Targets` 为空、命中配置的 repository targets，或配置未设置 targets 时，才会注册 analyzer 和路由。
* 宿主提供基础 `TurnInfo` / `CommandInfo` 领域视图。UAF、L'Arc、Cook、Mecha、Legend、Pioneer、Onsen、Breeders 等场景专用聚合模型由场景插件基于 Gallop DTO 派生。
* 请求/响应 analyzer 使用 endpoint attribute，例如 `[ResponseAnalyzer<GameApi.Account.Index>] ValueTask Analyze(DataLinkIndexResponse response)`；唯一参数为 `byte[]` 时收到原始 MessagePack payload，其他参数类型必须精确匹配 Gallop descriptor 的 request/response DTO。同一个方法可以挂多个 analyzer attribute，但这些 attribute 必须要求同一个 payload 参数类型。
* 插件也可以在 `Initialize(IPluginContext context)` 中通过 `context.Analyzers.RegisterRequest(...)` / `context.Analyzers.RegisterResponse(...)` 程序化注册 analyzer；raw handler 使用单泛型 overload，DTO handler 使用双泛型 overload。
* analyzer handler 返回 `ValueTask`。动态注册返回的 `IDisposable` 可注销对应 analyzer；注销只影响后续分发。
* 宿主按 `X-Hachimi-Game-Url` header 中的 canonical game URL 解析 path，并要求 path 精确命中 `GameEndpointCatalog.ByPath`。raw analyzer 收到的是原始 MessagePack payload bytes。
* 分发以 raw payload 为基础；DTO analyzer 在执行点按 Gallop descriptor 反序列化，同一分发中的 DTO analyzer 共享反序列化结果。raw analyzer 和 DTO analyzer 都按 priority 顺序执行。
* 插件 HTTP route 使用 `[Route]` attribute。方法签名必须是 `Task Handler(HttpContextBase ctx)`，最终路径为 `/<PluginName>/<RoutePath>`。
* 插件必须实现 `Initialize(IPluginContext context)`。`context.LiveDisplay` 提供 LiveDisplay 输出，`context.Events.OnStarted(...)` 用于订阅宿主启动事件；宿主按当前 ABI 调用初始化入口。`Initialize` 抛异常时，宿主记录插件失败并清理该插件的 analyzer、route、事件订阅和快捷键归属；热重载或卸载插件时也会做同样清理。
* 热重载按 internal name 应用；共享上下文插件会按上下文组一起重载。进入宿主上下文的插件不能卸载，只能通过重启应用新版本。
* 插件配置由插件自行维护。宿主不预创建插件数据目录，不自动读写配置文件，也不提供通用属性编辑器；菜单入口只调用插件自己的 `ConfigPromptAsync()`。插件需要配置或数据目录时，应在插件代码中创建目录、读取/校验/迁移自己的文件，错误直接抛出明确异常。
