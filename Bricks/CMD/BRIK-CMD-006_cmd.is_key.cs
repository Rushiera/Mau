// ═══════════════════════════════════════════════════
// 积木: cmd.is_key
// ID:   BRIK-CMD-006
// 类别: CMD
// 作用: 指令分派判断——最近消费的指令文本是否等于目标 key（返回=判断结果）
// 依赖: 无
// 引用: System
// 原理: 主值端口 text 与 target 精确相等（Ordinal）
// 常用: cat_lifecycle.mau 生命周期指令分派——disable/unload 按指令名路由
// ═══════════════════════════════════════════════════
using System;

namespace Mau.Bricks
{
    /// <summary>
    /// 指令积木——cmd.is_key 指令文本判断（纯函数——无宿主依赖）
    /// </summary>
    public static class CmdIsKeyBrick
    {
        /// <summary>
        /// 指令分派判断——最近消费的指令文本是否等于目标 key
        /// </summary>
        /// <param name="text">已消费指令文本（cmd.consume 主值端口）</param>
        /// <param name="target">目标指令 key</param>
        /// <returns>true=匹配（返回=判断结果，非查询成功）</returns>
        public static bool IsKey(string text, string target)
        {
            return string.Equals(text, target, StringComparison.Ordinal);
        }
    }
}
// #MAU_CHECKSUM:SHA256:82D1B631881E0B1119786CD3E1B13D4833C57BE0CEA4D7E1F700AE6DDAE35E43
