# UmamusumeResponseAnalyzer
# 前置 Prerequisite
* 任意可以把游戏请求/响应 MessagePack payload 发送到宿主 `/notify/request` / `/notify/response` 的插件。请求必须带 `X-Hachimi-Game-Url` header，值为游戏原始 canonical URL；该 URL 的 path 必须命中 Gallop endpoint catalog。推荐 [ura-core](https://github.com/UmamusumeResponseAnalyzer/ura-core)。
* (可选，如果需要脱离DMM启动游戏)DMM Game Player β及HTTPS proxy，比如[Fiddler](https://www.telerik.com/fiddler/fiddler-classic)或[mitmproxy](https://mitmproxy.org/)

# 安装 Installation
* 在[Release](https://github.com/EtherealAO/UmamusumeResponseAnalyzer/releases)页面下载最新版本程序
* 将程序放在任意位置，运行其中的UmamusumeResponseAnalyzer.exe
* 使用↑↓切换选项，选中更新数据文件，并按下回车键确认。（**重要！**）
* 返回主页面后选择启动！，程序显示已启动后即完成UmamusumeResponseAnalyzer的安装及启动
* sender 插件的目标地址设置为 `http://127.0.0.1:4693`，并确认它会把 canonical URL 写入 `X-Hachimi-Game-Url` header。

# 使用 Usage
* 启动后程序会进入 LiveDisplay 界面，插件输出、日志和通知会在同一个 TUI 中刷新。
* 按 `P` 查看已加载插件列表，按 `Ctrl+C` 退出程序。
* 更新数据文件时会先写入临时文件，全部下载成功后才替换原文件；下载失败会保留已有数据文件并显示错误。

# 检查安装 Checking
* 启动umamusume后，前往殿堂马列表/竞技场选择对手/查看好友信息时若UmamusumeResponseAnalyzer有信息显示则证明配置正确。

# 插件开发 Plugin Development
* 插件 ABI 来自 `UmamusumeResponseAnalyzer.Plugin.Abstractions`，游戏接口 DTO/endpoint catalog 来自 `Gallop`。插件和宿主都应引用这两个程序集，不要在插件或宿主里复制 `IPlugin`、`AnalyzerAttribute`、`LiveDisplay` contract 或 `namespace Gallop` DTO。
* 宿主只保留基础 `TurnInfo` / `CommandInfo` 领域视图，不再提供 `SingleModeTurnData` 或 UAF、L'Arc、Cook、Mecha、Legend、Pioneer、Onsen、Breeders 等场景专用聚合模型。场景插件应直接消费 Gallop DTO，并在插件内实现自己的派生视图。
* 请求/响应 analyzer 使用 typed endpoint attribute，例如 `[ResponseAnalyzer<GameApi.Account.Index>] void Analyze(DataLinkIndexResponse response)`；raw analyzer 使用 `[RawResponseAnalyzer<GameApi.Account.Index>] void Analyze(byte[] payload)`。同一个方法可以挂多个 analyzer attribute，但这些 attribute 必须要求同一个 payload 参数类型。
* 宿主按 `X-Hachimi-Game-Url` header 中的 canonical game URL 解析路径，并要求路径精确命中 `GameEndpointCatalog.ByPath`。raw analyzer 收到的是原始 MessagePack payload bytes。
* 同一 endpoint/kind 存在 typed analyzer 时，宿主会先按 Gallop descriptor 反序列化一次 DTO；失败会 fast-fail，不会 fallback 到 raw analyzer。成功后 raw analyzer 和 typed analyzer 都按 priority 顺序执行。
* 插件必须实现 `Initialize(IPluginContext context)`。`context.LiveDisplay` 提供 LiveDisplay 输出，`context.Events.OnStarted(...)` 用于订阅宿主启动事件；宿主不会调用旧 `Initialize()` / `Initialize(ILiveDisplayOutput)` 签名，热重载或卸载插件时会清理对应订阅。热重载加载失败会直接报错，不会保留或恢复旧版本插件实例；需要回退时请安装旧版本插件文件。
* 插件配置由插件自行维护。宿主不扫描 `[PluginSetting]`，不预创建 `PluginData/<PluginName>`，不自动读写 `settings.yaml`，也不提供通用属性编辑器；菜单入口只调用插件自己的 `ConfigPromptAsync()`。插件需要配置或数据目录时，应在插件代码中创建目录、读取/校验/迁移自己的文件，错误直接抛出明确异常。
* 开启 debug packet 保存时，`.msgpack` 内容保持原始 payload 不变，canonical URL 写入文件名；DEBUG 构建额外写 `.json`，其中 `url` 字段保存 canonical URL。
