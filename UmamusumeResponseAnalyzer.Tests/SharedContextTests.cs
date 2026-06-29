using System.Collections.Generic;
using UmamusumeResponseAnalyzer.Plugin;
using Xunit;

namespace UmamusumeResponseAnalyzer.Tests
{
    /// <summary>
    /// 回归:加载一个 <c>[SharedContextWith]</c> 上下文组时,若组内有成员的元数据缺失——典型是声明的锚点插件
    /// (如 EventLoggerPlugin)未安装(剧本分析器 manifest 常漏报这条依赖,于是单装一个分析器时锚点不在场)——
    /// 旧 <see cref="PluginManager.LoadGroup"/> 会对 <c>Metadatas[缺失名]</c> 做索引抛 KeyNotFoundException、
    /// 无人接住而【崩掉整个程序】(noVNC 实测:插件仓库装「梦想杯剧本解析器」当场崩溃,插件没装上)。
    /// 修复后应优雅失败:在场成员记入 FailedPlugins、不建 ALC、不抛异常。
    /// 归入 PluginReload collection 串行(与 HotReloadTests 一样会 mutate PluginManager 静态状态)。
    /// </summary>
    [Collection("PluginReload")]
    public class SharedContextMissingAnchorTests
    {
        [Fact]
        public void LoadGroup_WithMissingAnchor_FailsGracefullyWithoutCrash()
        {
            const string member = "BreedersScenarioAnalyzer";
            const string anchor = "EventLoggerPlugin"; // 故意不放进 Metadatas,模拟锚点未安装
            var meta = new PluginManager.PluginMetadata(
                $"X:/nonexistent/{member}.zip|{member}.dll", member,
                loadInHost: false, shared: [anchor], isFromZip: true);

            PluginManager.Metadatas[member] = meta;
            var group = new HashSet<string> { member, anchor };
            var key = string.Join("&", group);
            try
            {
                var ex = Record.Exception(() => PluginManager.LoadGroup(group));

                Assert.Null(ex);                                              // 不崩(修复前抛 KeyNotFoundException)
                Assert.Contains(meta.FilePath, PluginManager.FailedPlugins);  // 在场成员被记为加载失败
                Assert.DoesNotContain(PluginManager.LoadedPlugins, p => p.Name == member); // 未被加载
                Assert.False(PluginManager.Contexts.ContainsKey(key));        // 没建出 ALC(无幽灵上下文)
            }
            finally
            {
                PluginManager.Metadatas.Remove(member);
                PluginManager.FailedPlugins.Remove(meta.FilePath);
                PluginManager.Contexts.Remove(key);
            }
        }
    }
}
