// ═══════════════════════════════════════════════════
// 积木: tool.run_file_write
// ID:   BRIK-TOOL-007
// 类别: TOOL
// 作用: file.write 工具适配器——读单展平参数（args.path/args.content）→ file.write → 写回执 → Complete
// 依赖: file.write
// 引用: Mau.Runtime
// 原理: OA 单参数展平读取 → FileWriteBrick.Write → 回执写入 → Complete
// 常用: ToolExec 语料 file.write 分支（CH4 P2.2）
// ═══════════════════════════════════════════════════
using Mau.Runtime;

namespace Mau.Bricks
{
    /// <summary>
    /// 工具适配器——tool.run_file_write 积木（ToolExec 语料 file.write 分支执行入口）
    /// </summary>
    public static class ToolRunFileWriteBrick
    {
        /// <summary>
        /// 工具适配器——file.write：读单展平参数（args.path/args.content）→ FileWriteBrick.Write → 写回执 → Complete
        /// </summary>
        /// <param name="officeId">Office ID（已认领）</param>
        /// <param name="catId">执行方 LongId</param>
        /// <param name="result">执行结果文本</param>
        /// <param name="callId">工具调用 ID</param>
        /// <param name="session">会话 Key</param>
        /// <returns>true=执行并完成</returns>
        public static bool RunFileWrite(long officeId, long catId, out string result,
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
            string path = "";
            string? rawPath;
            if (!oa.GetStr(officeId, "args.path", out rawPath) || rawPath == null)
            {
                return false;
            }
            path = rawPath;
            string content = "";
            string? rawContent;
            if (oa.GetStr(officeId, "args.content", out rawContent) && rawContent != null)
            {
                content = rawContent;
            }
            if (path.Length == 0)
            {
                return false;
            }
            if (!FileWriteBrick.Write(path, content))
            {
                result = "ERR|FILE_WRITE_FAILED";
                return false;
            }
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
            resultData.Strs["content"] = "写入成功";
            oa.Complete(officeId, catId, resultData);
            result = "写入成功";
            return true;
        }
    }
}
// #MAU_CHECKSUM:SHA256:9DC9C5CEB131ACF816DCB5710D177988710E1DC465EF7A198C8ECE906B6411F6
