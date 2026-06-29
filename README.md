# UmamusumeResponseAnalyzer
# 前置 Prerequisite
* 任意可以向指定URL发送数据包的插件，比如[ura-core(推荐)](https://github.com/UmamusumeResponseAnalyzer/ura-core)或[EXNOA-CarrotJuicer](https://github.com/CNA-Bld/EXNOA-CarrotJuicer)
* (可选，如果需要脱离DMM启动游戏)DMM Game Player β及HTTPS proxy，比如[Fiddler](https://www.telerik.com/fiddler/fiddler-classic)或[mitmproxy](https://mitmproxy.org/)

# 安装 Installation
* 在[Release](https://github.com/EtherealAO/UmamusumeResponseAnalyzer/releases)页面下载最新版本程序
* 将程序放在任意位置，运行其中的UmamusumeResponseAnalyzer.exe
* 使用↑↓切换选项，选中更新数据文件，并按下回车键确认。（**重要！**）
* 返回主页面后选择启动！，程序显示已启动后即完成UmamusumeResponseAnalyzer的安装及启动
* 若非使用修改版的[umamusume-localify](https://github.com/EtherealAO/umamusume-localify)，则需要在对应的插件配置中将URL设置为`http://127.0.0.1:4693`

# 使用 Usage
* 启动后程序会进入 LiveDisplay 界面，插件输出、日志和通知会在同一个 TUI 中刷新。
* 按 `P` 查看已加载插件列表，按 `Ctrl+C` 退出程序。
* 更新数据文件时会先写入临时文件，全部下载成功后才替换原文件；下载失败会保留已有数据文件并显示错误。

# 检查安装 Checking
* 启动umamusume后，前往殿堂马列表/竞技场选择对手/查看好友信息时若UmamusumeResponseAnalyzer有信息显示则证明配置正确。
