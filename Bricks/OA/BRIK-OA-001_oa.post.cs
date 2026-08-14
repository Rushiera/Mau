// ═══════════════════════════════════════════════════
// 积木: oa.post
// ID:   BRIK-OA-001
// 类别: OA
// 作用: 上架工单——空双字典载荷随单生成，挂单方 set_str 逐 Key 写参数
// 依赖: 无
// 引用: Mau.Runtime（IOA/DataBox）
// 原理: DataBox.TryResolve<IOA> → Post(ownerId, officeType, officeName, timeoutTicks)
// 常用: CH4 第一轮 tool_test_cat 语料——ReadText/QuickCat 建单
// ═══════════════════════════════════════════════════
using Mau.Runtime;

namespace Mau.Bricks
{
    /// <summary>
    /// OA 积木——oa.post 上架工单
    /// </summary>
    public static class OaPostBrick
    {
        /// <summary>
        /// 上架工单——空双字典载荷随单生成
        /// </summary>
        /// <param name="officeType">工单大类</param>
        /// <param name="officeName">能力词汇——执行方据此判断能不能干</param>
        /// <param name="timeoutTicks">超时帧数</param>
        /// <param name="officeId">新 Office 的 ID</param>
        /// <returns>true=上架成功</returns>
        public static bool Post(string officeType, string officeName, long timeoutTicks, out long officeId)
        {
            IOA? oa;
            DataBox.TryResolve<IOA>(out oa);
            if (oa == null)
            {
                officeId = 0;
                return false;
            }
            officeId = oa.Post(FlowContext.CurrentFlowId, officeType, officeName, timeoutTicks);
            return true;
        }
    }
}
// #MAU_CHECKSUM:SHA256:B7415BDC461103310B6A12863CC17D0CED8350D6C2131C14E75F702B677EC71D
