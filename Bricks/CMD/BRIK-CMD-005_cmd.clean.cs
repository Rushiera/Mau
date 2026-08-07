// ═══════════════════════════════════════════════════
// 积木: cmd.clean
// ID:   BRIK-CMD-005
// 类别: CMD
// 作用: 清空指定模块在池中的所有残留指令
// 依赖: 无
// 引用: Mau.Runtime
// 原理: 经 CmdBridge 实例调用 CommandBus.Clean
// 常用: 会话结束清理 / 测试隔离
// ═══════════════════════════════════════════════════
using Mau.Runtime;

namespace Mau.Bricks
{
    /// <summary>
    /// 指令积木——cmd.clean 清空残留指令（依赖 CmdBridge）
    /// </summary>
    public static class CmdCleanBrick
    {
        /// <summary>
        /// 清空指定模块在池中的所有残留指令
        /// </summary>
        /// <param name="ownerId">模块全局 ID</param>
        /// <returns>true=清理成功</returns>
        public static bool Clean(long ownerId)
        {
            ICommandBus? bus;
            DataBox.TryResolve<ICommandBus>(out bus);
            if (bus == null)
            {
                return false;
            }
            bus.Clean(ownerId);
            return true;
        }
    }
}
// #MAU_CHECKSUM:SHA256:6B3F8D5D2B953E758FDF3A849BA4A75F55B1E7DF5ABA8AC4273D8B125EAA2859
