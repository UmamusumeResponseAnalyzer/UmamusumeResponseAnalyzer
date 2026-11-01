# 游戏通知契约

转发器分别向 `/notify/request` 和 `/notify/response` 发送 HTTP POST，正文分别是压缩前请求和解压后响应的原始 MessagePack。Host 收到每条通知即分发，不等待另一半，也不合并正文。

| 请求头 | 内容 |
| --- | --- |
| `X-Hachimi-Request-Id` | 必填，非空 GUID 的 `N` 格式：32 位十六进制，无连字符。每次游戏发送尝试使用新 ID；同次请求与响应使用同一 ID。 |
| `X-Hachimi-Game-Url` | 必填，游戏接口 URL。 |
| `X-Hachimi-sid`、`X-Hachimi-app-ver`、`X-Hachimi-res-ver` | 游戏请求中的对应字段；响应通知沿用同次请求的值。 |
| `X-Hachimi-viewerid`、`X-Hachimi-device`、`X-Hachimi-device-subtype` | 游戏请求中的对应字段；响应通知沿用同次请求的值。 |

RequestId 缺失、格式错误或全零时返回带原因的 HTTP 400。Host 将校验后的 ID 作为 `GameHttpHeaders.RequestId` 提供给请求和响应分析器。协议不接受 FIFO 配对或缺失 ID 的旧通知；转发器、Host、相关插件必须同步升级。引用 `RequestId` 的插件使用 `UmamusumeResponseAnalyzer` SDK 2026.11.1.2 或后续版本。

Host 串行执行通知的完整插件分发。某个通知的 HTTP 客户端超时不会让仍在执行的插件与后续通知并发运行。HTTP 成功响应表示本次分发已结束，不代表另一半已经到达或业务上传成功。

通知可以丢失，孤立响应仍交给响应分析插件。GamePacketCollector 按 `(endpoint, RequestId)` 精确查找请求，每接口最多保存 8 条，未匹配时跳过上传而不消费其他请求；上传格式与幂等键算法不包含 RequestId。EventLogger 的训练回合只由相同 RequestId 的响应消费，缺失匹配时跳过依赖请求回合的标记并继续响应分析。Ramen 的地区选择仍在请求到达时更新。

调试 MessagePack 文件名为 `yy-MM-dd HH-mm-ss-fff-{RequestId:N}{Q|R}-{endpoint}.msgpack`，endpoint 中的 `/` 替换为 `-`。Q/R 的时间可以不同，同一尝试通过 RequestId 关联；重试必须使用不同 RequestId。

转发器使用有界内存队列和后台发送，不落盘、不补传、不自动重试。每次游戏请求固定其通知地址和超时配置，配置修改仅影响新尝试。缺包是允许的传输结果，错配不是。
