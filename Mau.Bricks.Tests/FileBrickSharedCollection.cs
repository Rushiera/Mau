using Xunit;

namespace Mau.Bricks.Tests
{
    /// <summary>
    /// FileBrick 共享静态根测试集合——FileBrick._roots 是进程级静态，
    /// ConfigureRoots 的测试必须串行（与 ToolBrick.RunFileRead 等依赖 FileBrick 根的测试隔离）。
    /// </summary>
    [CollectionDefinition("FileBrickShared", DisableParallelization = true)]
    public class FileBrickSharedCollection
    {
    }
}
