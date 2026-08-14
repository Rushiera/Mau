// ═══════════════════════════════════════════════════
// 积木: oa.set_str
// ID:   BRIK-OA-002
// 类别: OA
// 作用: 写入工单请求载荷 str 值——仅 Open 状态 + 挂单方本人可操作
// 依赖: 无
// 引用: Mau.Runtime（IOA/DataBox）
// 原理: DataBox.TryResolve<IOA> → SetStr(officeId, ownerId, key, value)
// 常用: CH4 第一轮 tool_test_cat 语料——建单后写 path/system/content 参数
// ═══════════════════════════════════════════════════
using Mau.Runtime;

namespace Mau.Bricks
{
    /// <summary>
    /// OA 积木——oa.set_str 写工单载荷
    /// </summary>
    public static class OaSetStrBrick
    {
        /// <summary>
        /// 写入工单请求载荷 str 值——仅 Open 状态 + 挂单方本人可操作
        /// </summary>
        /// <param name="officeId">Office ID</param>
        /// <param name="key">载荷 Key</param>
        /// <param name="value">str 值</param>
        /// <returns>true=写入成功</returns>
        public static bool SetStr(long officeId, string key, string value)
        {
            IOA? oa;
            DataBox.TryResolve<IOA>(out oa);
            if (oa == null)
            {
                return false;
            }
            return oa.SetStr(officeId, FlowContext.CurrentFlowId, key, value);
        }
    }
}
// #MAU_CHECKSUM:SHA256:0078C6DDBDBFB65B3C301BC570E5B857CB64A5E2401811903653DC671E23E3BE
