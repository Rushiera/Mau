// ═══════════════════════════════════════════════
// 积木: tool.exec
// ID:   BRIK-TOOL-001
// 作用: 工具执行机制积木——语料声明工具分发拓扑，宿主注入执行器
// 引用: Mau.Bricks.Standard → Mau.Contracts
// 原理: 静态宿主桥 Configure(Func) 注入执行器；工具名+参数 → 结果文本
// 常用: tool_dispatch.mau 工具分发拓扑——CH4 P2 前置；oa_flow.mau T_Execute 动作
// ═══════════════════════════════════════════════
using System;
using Mau.Contracts;

namespace Mau.Bricks
{
    /// <summary>
    /// 工具执行积木——语料变迁动作的工具调用通道。宿主启动时注入执行器。
    /// 线程约束：worker 友好——执行器内部决定线程模型（同步/异步由调用方积木形态决定）。
    /// </summary>
    public static class ToolBrick
    {
        /// <summary>
        /// 宿主注入的执行器——工具名 + 参数数组 → 结果文本数组
        /// </summary>
        private static Func<string, string[], string[]>? _executor;

        /// <summary>
        /// 注入工具执行器——宿主启动时调用一次
        /// </summary>
        /// <param name="executor">工具执行委托</param>
        public static void Configure(Func<string, string[], string[]> executor)
        {
            _executor = executor;
        }

        /// <summary>
        /// 执行工具——按工具名分发，返回结果文本
        /// </summary>
        /// <param name="toolName">工具名</param>
        /// <param name="args">参数数组</param>
        /// <param name="resultTexts">结果文本数组</param>
        /// <returns>true=执行成功</returns>
        public static bool Exec(string toolName, string[] args, out string[] resultTexts)
        {
            Func<string, string[], string[]>? executor = _executor;
            if (executor == null)
            {
                resultTexts = new string[0];
                return false;
            }
            resultTexts = executor(toolName, args);
            return true;
        }
    }

    /// <summary>
    /// 工具积木注册——进程启动时调用一次
    /// </summary>
    public static class ToolBrickRegistration
    {
        /// <summary>
        /// 注册全部工具积木
        /// </summary>
        public static void RegisterAll()
        {
            RegisterExec();
        }

        /// <summary>
        /// 注册 tool.exec
        /// </summary>
        private static void RegisterExec()
        {
            BrickContract contract = new BrickContract("tool.exec", "Mau.Bricks.ToolBrick.Exec");
            contract.Inputs.Add(new BrickPort("toolName", typeof(string), "工具名"));
            contract.Inputs.Add(new BrickPort("args", typeof(string[]), "参数数组"));
            contract.Outputs.Add(new BrickPort("resultTexts", typeof(string[]), "结果文本数组"));
            contract.Return = BrickReturnKind.Bool;
            contract.Duration = BrickDuration.Sync;
            contract.Thread = "worker";
            BrickRegistry.Register(contract);
        }
    }
}
