// ═══════════════════════════════════════════════════
// 积木: tool.run_shell_exec
// ID:   BRIK-TOOL-008
// 类别: TOOL
// 作用: Shell 工具适配器——读单展平参数（args.command/args.timeoutSeconds）→ shell.exec
//       → 写回执 content → Complete（ToolExec 语料执行分支用）
// 依赖: shell.exec
// 引用: Mau.Runtime（IOA/OfficeData）
// 原理: OA 单参数展平读取 → ShellExecBrick.Exec → 回执写入 → Complete
// 常用: CH4 ToolExec / shell 工具执行链路
// ═══════════════════════════════════════════════════
using Mau.Runtime;

namespace Mau.Bricks
{
    /// <summary>
    /// 工具适配器——tool.run_shell_exec 积木（ToolExec 语料 shell 分支执行入口）
    /// </summary>
    public static class ToolRunShellExecBrick
    {
        /// <summary>
        /// 工具适配器——shell.exec：读单展平参数（args.command/args.timeoutSeconds）
        /// → ShellExecBrick.Exec → 写回执 content → Complete
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
            IOA? oa;
            DataBox.TryResolve<IOA>(out oa);
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
            bool ok = ShellExecBrick.Exec(command, timeoutSeconds, out output);
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
}
// #MAU_CHECKSUM:SHA256:FE988ED6ACFF0DA7BD59FC0F9FA54E2892FA74BDE449D50FDC3907656E24DE02
