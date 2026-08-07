// ═══════════════════════════════════════════════════
// 积木: oa.post
// ID:   BRIK-OA-001
// 类别: OA
// 作用: 上架工单——空双字典载荷随单生成，挂单方 set_* 逐 Key 写
// 依赖: 无
// 引用: Mau.Runtime
// 原理: 经 OaBridge 实例调用 IOA.Post
// 常用: oa_flow.mau 工单撮合拓扑——CH4 P1.3 核心
// ═══════════════════════════════════════════════════
using Mau.Runtime;

namespace Mau.Bricks
{
    /// <summary>
    /// OA 积木——oa.post 上架工单（依赖 OaBridge）
    /// </summary>
    public static class OaPostBrick
    {
        /// <summary>
        /// 上架工单——空双字典载荷随单生成，挂单方 set_* 逐 Key 写
        /// </summary>
        /// <param name="dogId">所有者 LongId</param>
        /// <param name="officeType">工单大类</param>
        /// <param name="officeName">固定词汇——执行方据此判断能不能干</param>
        /// <param name="timeoutTicks">超时帧数</param>
        /// <param name="officeId">新 Office 的 ID</param>
        /// <returns>true=上架成功</returns>
        public static bool Post(long dogId, string officeType, string officeName, long timeoutTicks, out long officeId)
        {
            IOA? oa;
            DataBox.TryResolve<IOA>(out oa);
            if (oa == null)
            {
                officeId = 0;
                return false;
            }
            officeId = oa.Post(dogId, officeType, officeName, timeoutTicks);
            return true;
        }
    }
}
// #MAU_CHECKSUM:SHA256:8EF2A9287BD68972C6B80EE63D24EA11A0CFF0661D5E72011A19725B6153B448
