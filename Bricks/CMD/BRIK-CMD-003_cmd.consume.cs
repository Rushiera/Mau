// ═══════════════════════════════════════════════════
// 积木: cmd.consume
// ID:   BRIK-CMD-003
// 类别: CMD
// 作用: 消费指定模块的指令邮件——无新指令返回 false（等待分支）
// 依赖: 无
// 引用: Mau.Runtime
// 原理: 经 CmdBridge 实例取 CommandPack 邮件，解析主值端口 text
// 常用: talkcat_fsm.mau 指令轮询——每帧消费
// ═══════════════════════════════════════════════════
using Mau.Runtime;

namespace Mau.Bricks
{
    /// <summary>
    /// 指令积木——cmd.consume 消费指令邮件（依赖 CmdBridge）
    /// </summary>
    public static class CmdConsumeBrick
    {
        /// <summary>
        /// 消费指定模块的指令邮件——无新指令（模板邮件）返回 false，让语料进入等待分支
        /// </summary>
        /// <param name="ownerId">模块全局 ID</param>
        /// <param name="hasCommands">是否有实际写入的指令</param>
        /// <param name="cmdKeys">指令 key 数组（与注册对齐）</param>
        /// <param name="cmdValues">int payload 数组</param>
        /// <param name="cmdTexts">string payload 数组</param>
        /// <param name="text">主值端口——第一个非空 CmdText（无则空串），数组→标量绑定用</param>
        /// <returns>true=有实际指令；false=无新指令（模板邮件）或未注入</returns>
        public static bool Consume(long ownerId, out bool hasCommands,
            out string[] cmdKeys, out int[] cmdValues, out string[] cmdTexts,
            out string text)
        {
            hasCommands = false;
            cmdKeys = new string[0];
            cmdValues = new int[0];
            cmdTexts = new string[0];
            text = "";
            ICommandBus? bus;
            DataBox.TryResolve<ICommandBus>(out bus);
            if (bus == null)
            {
                return false;
            }
            CommandPack mail = bus.GetCommandEmail(ownerId);
            hasCommands = mail.HasCommands;
            cmdKeys = mail.CmdKeys;
            cmdValues = mail.CmdValues;
            cmdTexts = mail.CmdTexts;
            if (!hasCommands)
            {
                return false;
            }
            if (cmdTexts != null)
            {
                for (int i = 0; i < cmdTexts.Length; i = i + 1)
                {
                    if (!string.IsNullOrEmpty(cmdTexts[i]))
                    {
                        text = cmdTexts[i];
                        break;
                    }
                }
            }
            return true;
        }
    }
}
// #MAU_CHECKSUM:SHA256:DED43743FE81C0F24AA1F94102B75AF340E701945EF66365EEFA3AFE474BEF68
