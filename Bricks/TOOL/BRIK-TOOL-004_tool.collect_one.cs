// ═══════════════════════════════════════════════════
// 积木: tool.collect_one
// ID:   BRIK-TOOL-004
// 类别: TOOL
// 作用: 单值收集工具结果——读回执 call_id + content（语料逐单回填用）
// 依赖: 无
// 引用: Mau.Runtime
// 原理: 单 OA 单读回执（call_id/content）——规避数组
// 常用: TalkCat 工具循环逐单收集（CH4 P2.2）
// ═══════════════════════════════════════════════════
using Mau.Runtime;

namespace Mau.Bricks
{
    /// <summary>
    /// 工具积木——tool.collect_one 单值收集（依赖 OaBridge）
    /// </summary>
    public static class ToolCollectOneBrick
    {
        /// <summary>
        /// 单值收集工具结果——读回执 call_id + content（语料逐单回填用，规避数组）
        /// </summary>
        /// <param name="officeId">Office ID</param>
        /// <param name="callId">工具调用 ID</param>
        /// <param name="content">结果内容</param>
        /// <returns>true=收集成功</returns>
        public static bool CollectOne(long officeId, out string callId, out string content)
        {
            callId = "";
            content = "";
            IOA? oa;
            DataBox.TryResolve<IOA>(out oa);
            if (oa == null)
            {
                return false;
            }
            Office office = oa.GetOffice(officeId);
            string? rawCall;
            if (office.Data.Strs.TryGetValue("call_id", out rawCall) && rawCall != null)
            {
                callId = rawCall;
            }
            string? rawContent;
            if (office.Result.Strs.TryGetValue("content", out rawContent) && rawContent != null)
            {
                content = rawContent;
            }
            return true;
        }
    }
}
// #MAU_CHECKSUM:SHA256:79F3D245F2BAB411D3DE20E92792D5F9981AA0B243BECC003AC976F6AE2D5708
