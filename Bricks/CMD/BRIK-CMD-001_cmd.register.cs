// ═══════════════════════════════════════════════════
// 积木: cmd.register
// ID:   BRIK-CMD-001
// 类别: CMD
// 作用: 注册模块的指令 key 列表（key 必须三段式）
// 依赖: 无
// 引用: Mau.Runtime
// 原理: 经 CmdBridge 实例调用 CommandBus.Register
// 常用: talkcat_fsm.mau 指令入口——Cat 启动注册
// ═══════════════════════════════════════════════════
using Mau.Runtime;

namespace Mau.Bricks
{
    /// <summary>
    /// 指令积木——cmd.register 注册模块指令 key（依赖 CmdBridge）
    /// </summary>
    public static class CmdRegisterBrick
    {
        /// <summary>
        /// 注册模块的指令 key 列表——key 必须三段式（Category_Module_Name）
        /// </summary>
        /// <param name="ownerId">模块全局 ID</param>
        /// <param name="cmdKeys">指令 key 数组</param>
        /// <returns>true=注册成功</returns>
        public static bool Register(long ownerId, string[] cmdKeys)
        {
            ICommandBus? bus;
            DataBox.TryResolve<ICommandBus>(out bus);
            if (bus == null)
            {
                return false;
            }
            if (cmdKeys == null)
            {
                cmdKeys = new string[0];
            }
            bus.Register(ownerId, cmdKeys);
            return true;
        }
    }
}
// #MAU_CHECKSUM:SHA256:5054B1431B72485B2B8864E1F99E1D7238E7854DA4ECEAACBDA637C531DC7BB3
