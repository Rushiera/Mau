using Xunit;

namespace CatHome4.Core.Tests
{
    /// <summary>
    /// xUnit 集合定义——**全局静态现场**（工具注册面 ToolRegistry / ToolPool，Admin 数据根 AdminService._dataRoot）的测试类串行执行。
    /// 依据：单向数据流原则——全局单例的测试访问面收敛为串行排队，避免跨类并行改写互相覆盖（2026-09-21 工具注册面并发写实证；2026-09-30 Admin._dataRoot 跨类互踩实证——CmdUnknownTests / ChatImagesTests 归入）。
    /// </summary>
    [CollectionDefinition("GlobalToolState")]
    public class GlobalToolStateCollection
    {
    }
}
