using Xunit;

namespace CatHome4.Core.Tests
{
    /// <summary>
    /// xUnit 集合定义——共享全局工具注册面（ToolRegistry / ToolPool）的测试类串行执行。
    /// 依据：单向数据流原则——全局单例的测试访问面收敛为串行排队，避免跨类并行 Init 互相覆盖（2026-09-21 并发写实证）。
    /// </summary>
    [CollectionDefinition("GlobalToolState")]
    public class GlobalToolStateCollection
    {
    }
}
