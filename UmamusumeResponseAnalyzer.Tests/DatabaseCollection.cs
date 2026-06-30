using Xunit;

namespace UmamusumeResponseAnalyzer.Tests
{
    /// <summary>
    /// 串行化标记：所有会 seed <c>Database.Names</c> 等全局静态状态的测试类都加 <c>[Collection("Database")]</c>，
    /// 归入此 collection 后彼此不并行（且本 collection 整体不与其它并行），避免对共享静态状态的并发读写竞争。
    /// 不带任何 fixture——纯粹用于关闭并行。
    /// </summary>
    [CollectionDefinition("Database", DisableParallelization = true)]
    public class DatabaseCollection { }
}
