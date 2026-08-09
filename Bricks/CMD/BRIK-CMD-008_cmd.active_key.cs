// ═══════════════════════════════════════════════════
// 积木: cmd.active_key
// ID:   BRIK-CMD-008
// 类别: CMD
// 作用: 提取活跃指令 key——cmd.consume 输出的三数组中对齐查第一个有值（text/value）的 key
// 依赖: 无
// 引用: System
// 原理: CmdKeys 模板数组（全量注册 key）与 CmdTexts/CmdValues 对齐——有值位置即本帧活跃 key
// 常用: UiPet 指令路由——consume 后先取活跃 key 再分派（key 数组路由的真相源）
// 包: 无
// ═══════════════════════════════════════════════════
using System;

namespace Mau.Bricks
{
    /// <summary>
    /// 指令积木——cmd.active_key 提取活跃指令 key（纯函数——无宿主依赖）
    /// </summary>
    public static class CmdActiveKeyBrick
    {
        /// <summary>
        /// 提取活跃指令 key——三数组对齐，第一个有值（text 非空或 value 非 0）的 key
        /// </summary>
        /// <param name="cmdKeys">指令 key 数组（注册模板）</param>
        /// <param name="cmdValues">int 载荷数组</param>
        /// <param name="cmdTexts">string 载荷数组</param>
        /// <param name="activeKey">活跃 key；无返回空串</param>
        /// <returns>true=找到活跃 key</returns>
        public static bool ActiveKey(string[] cmdKeys, int[] cmdValues, string[] cmdTexts, out string activeKey)
        {
            activeKey = "";
            if (cmdKeys == null)
            {
                return false;
            }
            for (int i = 0; i < cmdKeys.Length; i = i + 1)
            {
                if (cmdKeys[i] == null || cmdKeys[i].Length == 0)
                {
                    continue;
                }
                bool hasText = cmdTexts != null && i < cmdTexts.Length
                    && cmdTexts[i] != null && cmdTexts[i].Length > 0;
                bool hasValue = cmdValues != null && i < cmdValues.Length && cmdValues[i] != 0;
                if (hasText || hasValue)
                {
                    activeKey = cmdKeys[i];
                    return true;
                }
            }
            return false;
        }
    }
}
// #MAU_CHECKSUM:SHA256:A313F78725B7DFE3DF08F33C4276B5517377528D7620DE6D6EC6B9A18367EA5D
