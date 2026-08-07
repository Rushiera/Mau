// ═══════════════════════════════════════════════════
// 积木: cmd.unregister
// ID:   BRIK-CMD-002
// 类别: CMD
// 作用: 注销模块并清理残留指令
// 依赖: 无
// 引用: Mau.Runtime
// 原理: 经 CmdBridge 实例调用 CommandBus.Unregister
// 常用: Cat 卸载清理
// ═══════════════════════════════════════════════════
using Mau.Runtime;

namespace Mau.Bricks
{
    /// <summary>
    /// 指令积木——cmd.unregister 注销模块（依赖 CmdBridge）
    /// </summary>
    public static class CmdUnregisterBrick
    {
        /// <summary>
        /// 注销模块并清理残留指令
        /// </summary>
        /// <param name="ownerId">模块全局 ID</param>
        /// <returns>true=注销成功</returns>
        public static bool Unregister(long ownerId)
        {
            ICommandBus? bus;
            DataBox.TryResolve<ICommandBus>(out bus);
            if (bus == null)
            {
                return false;
            }
            bus.Unregister(ownerId);
            return true;
        }
    }
}
// #MAU_CHECKSUM:SHA256:CFABF7F04BFCE96FF3D30E9CB19872316F13C555CE25EC23982BB0B5EF5ADF77
