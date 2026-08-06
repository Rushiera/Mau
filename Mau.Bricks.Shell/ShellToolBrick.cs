// ═══════════════════════════════════════════════
// 积木: tool.run_shell_exec
// ID:   BRIK-SHELL-002
// 作用: Shell 工具适配器——读单展平参数（args.command/args.timeoutSeconds）→ ShellBrick.Exec
//       → 写回执 content → Complete（ToolExec 语料执行分支用）
// 引用: Mau.Bricks.Shell → Mau.Contracts（BrickRegistry）· Mau.Runtime（OA/OfficeData）
// 依赖: Mau.Runtime.OA · BRIK-SHELL-001
// 原理: 与 ToolBrick.RunFileRead/Write 同款适配器模式——OA 单参数展平读取 → 积木执行 → 回执写入
// 常用: CH4 ToolExec / shell 工具执行链路
// ═══════════════════════════════════════════════
using System;
using Mau.Contracts;
using Mau.Runtime;

namespace Mau.Bricks
{
    /// <summary>
    /// Shell 工具适配器——tool.run_shell_exec 积木（ToolExec 语料分支执行入口）。
    /// 与 ToolBrick.RunFileRead/Write 同款：读 OA 单展平参数 → ShellBrick.Exec → 写回执 → Complete。
    /// </summary>
    public static class ShellToolBrick
    {
        /// <summary>
        /// 宿主注入的 OA 实例——发单与统合（OaBrick 同款宿主桥）
        /// </summary>
        private static IOA? _oa;

        /// <summary>
        /// 注入 OA 实例——宿主启动时调用一次
        /// </summary>
        /// <param name="oa">OA 工单平台</param>
        public static void ConfigureOA(IOA oa)
        {
            _oa = oa;
        }

        /// <summary>
        /// 工具适配器——shell.exec：读单展平参数（args.command/args.timeoutSeconds）
        /// → ShellBrick.Exec → 写回执 content → Complete
        /// </summary>
        /// <param name="officeId">Office ID（已认领）</param>
        /// <param name="catId">执行方 LongId</param>
        /// <param name="result">执行结果文本</param>
        /// <param name="callId">工具调用 ID</param>
        /// <param name="session">会话 Key</param>
        /// <returns>true=执行并完成</returns>
        public static bool RunShellExec(long officeId, long catId, out string result,
            out string callId, out string session)
        {
            result = "";
            callId = "";
            session = "";
            IOA? oa = _oa;
            if (oa == null)
            {
                return false;
            }
            string command = "";
            string? rawCommand;
            if (!oa.GetStr(officeId, "args.command", out rawCommand) || rawCommand == null)
            {
                return false;
            }
            command = rawCommand;
            if (command.Length == 0)
            {
                result = "ERR|SHELL_COMMAND_EMPTY";
                return false;
            }
            int timeoutSeconds = 20;
            string? rawTimeout;
            if (oa.GetStr(officeId, "args.timeoutSeconds", out rawTimeout) && rawTimeout != null)
            {
                int parsed;
                if (int.TryParse(rawTimeout, out parsed) && parsed > 0)
                {
                    timeoutSeconds = parsed;
                }
            }
            string output;
            bool ok = ShellBrick.Exec(command, timeoutSeconds, out output);
            // 附带输出——call_id/session（executor 结构化回填定位用）
            string? rawCall;
            if (oa.GetStr(officeId, "call_id", out rawCall) && rawCall != null)
            {
                callId = rawCall;
            }
            string? rawSession;
            if (oa.GetStr(officeId, "session", out rawSession) && rawSession != null)
            {
                session = rawSession;
            }
            OfficeData resultData = OfficeData.Empty();
            resultData.Strs["content"] = output;
            oa.Complete(officeId, catId, resultData);
            result = output;
            return ok;
        }
    }

    /// <summary>
    /// Shell 工具适配器注册——进程启动时调用一次
    /// </summary>
    public static class ShellToolBrickRegistration
    {
        /// <summary>
        /// 注册全部 Shell 工具适配器
        /// </summary>
        public static void RegisterAll()
        {
            RegisterRunShellExec();
        }

        /// <summary>
        /// 注册 tool.run_shell_exec——shell.exec 工具适配器
        /// </summary>
        private static void RegisterRunShellExec()
        {
            BrickContract contract = new BrickContract("tool.run_shell_exec", "Mau.Bricks.ShellToolBrick.RunShellExec");
            contract.Inputs.Add(new BrickPort("officeId", typeof(long), "Office ID（已认领）"));
            contract.Inputs.Add(new BrickPort("catId", typeof(long), "执行方 LongId"));
            contract.Outputs.Add(new BrickPort("result", typeof(string), "执行结果文本"));
            contract.Outputs.Add(new BrickPort("callId", typeof(string), "工具调用 ID"));
            contract.Outputs.Add(new BrickPort("session", typeof(string), "会话 Key"));
            contract.Return = BrickReturnKind.Bool;
            contract.Duration = BrickDuration.Sync;
            contract.Thread = "main";
            BrickRegistry.Register(contract);
        }
    }
}
