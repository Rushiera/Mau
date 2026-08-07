// ═══════════════════════════════════════════════════
// 积木: tool.claim_next
// ID:   BRIK-TOOL-003
// 类别: TOOL
// 作用: 游标式认领工具单——从 Open TOOL 单逐个尝试认领
// 依赖: 无
// 引用: Mau.Runtime
// 原理: ListOpen（TOOL 候选）→ 逐个 ClaimBatch 尝试——已认领的跳过
// 常用: ToolExec 语料工具执行轮询（CH4 P2.3）
// ═══════════════════════════════════════════════════
using Mau.Runtime;

namespace Mau.Bricks
{
    /// <summary>
    /// 工具积木——tool.claim_next 游标式认领（依赖 OaBridge）
    /// </summary>
    public static class ToolClaimNextBrick
    {
        /// <summary>
        /// 游标式认领工具单——从 Open TOOL 单逐个尝试认领（file.read/file.write 候选）
        /// </summary>
        /// <param name="catId">执行方 LongId</param>
        /// <param name="officeId">认领的 OfficeId</param>
        /// <param name="toolName">工具名</param>
        /// <returns>true=认领成功</returns>
        public static bool ClaimNext(long catId, out long officeId, out string toolName)
        {
            officeId = 0;
            toolName = "";
            IOA? oa;
            DataBox.TryResolve<IOA>(out oa);
            if (oa == null)
            {
                return false;
            }
            Office[] open = oa.ListOpen("TOOL",
                new string[] { "file.read", "file.write", "shell.exec" }).ToArray();
            for (int i = 0; i < open.Length; i = i + 1)
            {
                System.Collections.Generic.List<Office> claimed = oa.ClaimBatch(catId,
                    new long[] { open[i].OfficeId });
                if (claimed.Count > 0)
                {
                    officeId = claimed[0].OfficeId;
                    toolName = claimed[0].OfficeName;
                    return true;
                }
            }
            return false;
        }
    }
}
// #MAU_CHECKSUM:SHA256:7C060E8D9BFCA3ED31A4B1827C66D0B64FC9394A7CCC99EEDF6F9EDD1A053F09
