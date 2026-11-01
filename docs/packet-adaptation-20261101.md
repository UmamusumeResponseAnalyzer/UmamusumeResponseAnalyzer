# 2026-11-01 实包适配验证

## 范围与实现

- Host：技能费用合并 Hint 与状态折扣，勤勉好学 `40` 为 4%，切者 `7` 为 10%，同组取切者；逐技能取整后累计未学习下位费用。HTTP 通知使用异步正文读取，带 `Content-Length` 的正文长度不符时返回 400，报告接口、预期及实际长度，并停止派发。
- TeamStadium：`start` 省略 `opponent_info_copy` 时保留已有结果；提供数据时更新。未知适性、距离和跑法继续明确报错。
- EventResponse：补齐 Festa `start`、`race_entry`、`race_out`、`attach_advice`、`check_point`、`festa_live`，共用事件解析与历史去重。
- SkillEffect / WinSaddle：分别以育成通配入口接收同构的 `check_event` / `start`，普通育成只注册一次。
- EventLogger：覆盖 Festa 开局、回合、事件、比赛、学技能；稽古 `5101–5105` 映射五种训练，以完整命令 ID 合并 home 与 Festa 参数，采用与 Festa 相同的命令选择规则。友人类型不参与连续事件统计，保留特殊卡规则。同一待处理事件及数值状态重发时等待事件推进后结算。
- Festa：增加按角色与回合标识的附加内容生产者接口，仅开放附加区域。EventLogger 通过源码依赖、manifest 软依赖和延迟桥接写入不可变文本快照；Host 未增加公共接口。接口与生命周期约定见 Festa README。

六个改动运行时代码的插件版本均为 `2026.11.1`。Festa 与 EventLogger 联动须成对更新；SkillTips 的折扣修正来自 Host，共享费用逻辑已经验证，插件自身运行时代码未改动。

## 实包回放

输入为本机 packets 目录中日期前缀 `26-11-01` 的 1,186 份文件。回放程序和断言位于宿主忽略目录 `artifacts/packet-adaptation-20261101/audit/`；运行目录及数据库副本隔离，未使用实际插件数据目录。未添加仓库测试项目或提交原始包。

| 项目 | 结果 |
| --- | --- |
| MessagePack 解码及端点 DTO 投影 | 1,184 份有效，70 个接口；2 份截断 |
| 受影响插件按注册优先级回放 | 全部有效包通过，无回调异常 |
| 两次实际技能消费 | 8,470、1,163；EventLogger 累计 9,633 |
| 技能条件边界 | 无状态、勤勉好学、切者、两者同时存在 × 6 个 Hint 等级 × 未学习/已学习下位技能，共 48 组断言通过 |
| 竞技场 | 5 份空对手 start 通过；初次空数据不创建面板，有对手时更新，空数据保留结果，未知值明确报错 |
| Festa 训练 | 423 份响应；55 次成功训练（普通 19、稽古 36），五围、技能点、体力预测全部吻合，EventLogger 记录与面板一致 |
| EventResponse 事件历史 | 232 条唯一事件，新增入口补齐其中 43 条；重复响应不增加条目 |
| EventLogger 连续事件 | 友人 30323 排除后总量 15；15 次连续事件完成、剩余 0，比赛记录 17 场 |
| 重复响应 | 每份响应再次派发，事件数仍为 127，技能消费、连续事件及继承收益与正常回放一致 |
| HTTP 正文边界 | 声明 600,000、实际 393,216 字节返回 400；等长及未声明长度的请求保持正常处理 |

EventLogger 的 127 条事件使用既有事件强度和排除规则，与 EventResponse 的 232 条唯一事件不是同一计数口径。

两份截断包均为 393,216 字节，分别属于 `load/index`、`trained_chara/load`；原始截断原因未确定，未尝试恢复或补发。

## 附加区域与生命周期

使用真实终端布局及隔离 UI 会话检查：

- 各回合内容隔离；更新后修改调用方行集合不改变已保存内容。
- 同键更新不增加历史或未读，浏览旧回合时不会跳到其他回合。
- 空生产者不显示标题或占位，释放句柄删除该生产者内容。
- 历史缩容至 3 条释放多余内容；关闭历史只保留当前画面；卸载后内容及生产者归零。
- 120×65 完整画面和 80×24 窄窗口滚动可显示 EventLogger 附加区域。
- EventLogger 在没有可选剧本程序集的隔离加载环境中初始化和卸载成功；Festa 单独接收训练响应成功。

## 构建与产物

Host Release 构建及本地 `win-x64 --self-contained false` 输出通过；受影响的六个插件和 SkillTips 编译通过。插件 ZIP 使用 `win-x64`、`AnyCPU`，关闭自动部署。Festa 及既有剧本渲染器有 `TextView` 弃用警告，没有编译错误。

公开 SDK 源访问受限，本次使用匹配源码生成的宿主 NuGet 包、已缓存依赖和隔离 NuGet 配置。生产项目的 NuGet 来源和引用方式未修改。

六份 ZIP 均通过宿主 `PluginPackageValidator`，包含正确 manifest、主程序集与允许的依赖/资源；未携带 Host、Terminal.Gui、调试文件或其他插件程序集。EventLogger manifest 包含 Festa 软依赖，Festa manifest 无插件依赖。

本地产物位于 `artifacts/packet-adaptation-20261101/delivery/`，包括 Host 输出、六份插件 ZIP 和 SHA-256 清单。回放日志为 `artifacts/packet-adaptation-20261101/replay-final.log`。

源码修改保持未提交。新增 `deps/FestaScenarioAnalyzer` 的工作区已同步本轮 Festa 源码；后续提交时须先提交 Festa，再将 EventLogger gitlink 固定到该提交。已有宿主 Build README 修改及其他 submodule 偏移保持原样。

## 验证边界

未连接游戏进行联机验收，未覆盖真实失败训练或全部教诲组合。未接入 `get_choice_reward`、AI 导出或其他面板，未重生成协议；未推送或对外发布。离线回放不证明截断包的原始成因或实际游戏联机行为。

## 本机部署

经用户确认，2026-11-01 08:10（Asia/Shanghai）完成宿主单文件 EXE 与六个 `2026.11.1` 插件 ZIP 的替换。宿主位于 `K:/builds/UmamusumeResponseAnalyzer/Bin/UmamusumeResponseAnalyzer/standalone/UmamusumeResponseAnalyzer.exe`，插件位于 `%LocalAppData%/UmamusumeResponseAnalyzer/Plugins/`。七个安装文件的 SHA-256 均与已验证产物一致，原文件备份已校验。

部署计划、结果、备份与恢复脚本保存在 `artifacts/packet-adaptation-20261101/deployment-20261101-075203/`。此次只替换这七个文件，未启动 URA 或游戏；配置、抓包和其他插件保持原样。
