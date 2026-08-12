// ═══════════════════════════════════════════════════
// 积木: cmd.match
// ID:   BRIK-CMD-009
// 类别: CMD
// 作用: 指令多路匹配——指令文本命中候选列表返回候选名（名称返回积木——多路 | switch 分发源）
// 依赖: 无
// 引用: System
// 原理: text 与候选逐个 Ordinal 比较，命中返回该候选名；全部未命中返回空串（default 兜底）
// 常用: UiPet 多路路由——'cmd.match'['activeKey', ["Open","Toggle","Select"]]
// ═══════════════════════════════════════════════════
using System;

namespace Mau.Bricks
{
    /// <summary>
    /// 指令积木——cmd.match 指令多路匹配（名称返回积木——多路 | 按名分发）
    /// </summary>
    public static class CmdMatchBrick
    {
        /// <summary>
        /// 指令多路匹配——text 命中候选返回候选名（空串 = 未命中，走 default 兜底）
        /// </summary>
        /// <param name="text">已消费指令文本（cmd.consume 主值端口）</param>
        /// <param name="candidates">候选名称列表（按声明顺序匹配）</param>
        /// <returns>命中候选名；未命中返回空串</returns>
        public static string Match(string text, string[] candidates)
        {
            if (string.IsNullOrEmpty(text) || candidates == null)
            {
                return "";
            }
            for (int i = 0; i < candidates.Length; i = i + 1)
            {
                if (string.Equals(text, candidates[i], StringComparison.Ordinal))
                {
                    return candidates[i];
                }
            }
            return "";
        }
    }
}
// #MAU_CHECKSUM:SHA256:47ACE336480C2DEA96E31285698AF0F4F807394218CFA36447B74BC94296130B
