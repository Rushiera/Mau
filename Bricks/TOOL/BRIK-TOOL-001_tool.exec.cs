// ═══════════════════════════════════════════════════
// 积木: tool.exec
// ID:   BRIK-TOOL-001
// 类别: TOOL
// 作用: 工具执行——按工具名分发，返回结果文本
// 依赖: 无
// 引用: System
// 原理: 经 ToolBridge 执行器委托分发——工具名+参数 → 结果文本
// 常用: tool_dispatch.mau 工具分发拓扑
// ═══════════════════════════════════════════════════
using System;

namespace Mau.Bricks
{
    /// <summary>
    /// 工具积木——tool.exec 按工具名分发执行（依赖 ToolBridge）
    /// </summary>
    public static class ToolExecBrick
    {
        /// <summary>
        /// 执行工具——按工具名分发，返回结果文本
        /// </summary>
        /// <param name="toolName">工具名</param>
        /// <param name="args">参数数组</param>
        /// <param name="resultTexts">结果文本数组</param>
        /// <returns>true=执行成功</returns>
        public static bool Exec(string toolName, string[] args, out string[] resultTexts)
        {
            Func<string, string[], string[]>? executor;
            DataBox.TryResolve<Func<string, string[], string[]>>(out executor);
            if (executor == null)
            {
                resultTexts = new string[0];
                return false;
            }
            resultTexts = executor(toolName, args);
            return true;
        }
    }
}
// #MAU_CHECKSUM:SHA256:D0DACE3241F7D961E47F026EABC4234D4179E556AFA0F851B726B905C67A5018
