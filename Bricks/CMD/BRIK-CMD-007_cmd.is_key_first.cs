// ═══════════════════════════════════════════════════
// 积木: cmd.is_key_first
// ID:   BRIK-CMD-007
// 类别: CMD
// 作用: 指令 key 数组首元素判断——cmd.consume 输出的 cmdKeys 路由（返回=判断结果）
// 依赖: 无
// 引用: System
// 原理: cmdKeys 数组首元素与 target 精确相等（Ordinal）——单指令投递时首元素即 key
// 常用: UiPet 指令路由——key 分派（text 留作载荷，与 cmd.is_key 的 text 分派互补）
// 包: 无
// ═══════════════════════════════════════════════════
using System;

namespace Mau.Bricks
{
    /// <summary>
    /// 指令积木——cmd.is_key_first 指令 key 数组首元素判断（纯函数——无宿主依赖）
    /// </summary>
    public static class CmdIsKeyFirstBrick
    {
        /// <summary>
        /// 指令 key 数组首元素判断——cmd.consume 输出的 cmdKeys[0] 是否等于目标 key
        /// </summary>
        /// <param name="cmdKeys">已消费指令 key 数组（cmd.consume 输出）</param>
        /// <param name="target">目标指令 key</param>
        /// <returns>true=匹配（返回=判断结果，非查询成功）</returns>
        public static bool IsKeyFirst(string[] cmdKeys, string target)
        {
            if (cmdKeys == null || cmdKeys.Length == 0)
            {
                return false;
            }
            return string.Equals(cmdKeys[0], target, StringComparison.Ordinal);
        }
    }
}
// #MAU_CHECKSUM:SHA256:C32C9CC0A46141E83016EB0585F0C44BAB09724C614769A23055AABD00BC47EE
