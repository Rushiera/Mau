// ═══════════════════════════════════════════════════
// 积木: tool.run_file_read
// ID:   BRIK-TOOL-006
// 类别: TOOL
// 作用: file.read 工具适配器——读单展平参数（args.path）→ file.read → 写回执 content → Complete
// 依赖: file.read
// 引用: Mau.Runtime
// 原理: OA 单参数展平读取 → FileReadBrick.Read → 回执写入 → Complete
// 常用: ToolExec 语料 file.read 分支（CH4 P2.2）
// ═══════════════════════════════════════════════════
using Mau.Runtime;

namespace Mau.Bricks
{
    /// <summary>
    /// 工具适配器——tool.run_file_read 积木（ToolExec 语料 file.read 分支执行入口）
    /// </summary>
    public static class ToolRunFileReadBrick
    {
        /// <summary>
        /// 工具适配器——file.read：读单展平参数（args.path）→ FileReadBrick.Read → 写回执 content → Complete
        /// </summary>
        /// <param name="officeId">Office ID（已认领）</param>
        /// <param name="catId">执行方 LongId</param>
        /// <param name="result">执行结果文本</param>
        /// <param name="callId">工具调用 ID</param>
        /// <param name="session">会话 Key</param>
        /// <returns>true=执行并完成</returns>
        public static bool RunFileRead(long officeId, long catId, out string result,
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
            if (path.Length == 0)
            {
                return false;
            }
            string content;
            if (!FileReadBrick.Read(path, out content))
            {
                result = "ERR|FILE_READ_FAILED";
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
            resultData.Strs["content"] = content;
            oa.Complete(officeId, catId, resultData);
            result = content;
            return true;
        }
    }
}
// #MAU_CHECKSUM:SHA256:D18BEBB97A60AB6A6509994E27B6C6B3C9312D3F8879013BFD852F4CF2DFCEA8
