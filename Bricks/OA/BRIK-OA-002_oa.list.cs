// ═══════════════════════════════════════════════════
// 积木: oa.list
// ID:   BRIK-OA-002
// 类别: OA
// 作用: 查单——某大类下、OfficeName 在候选列表中的 Open 单
// 依赖: 无
// 引用: Mau.Runtime
// 原理: 经 OaBridge 实例调用 IOA.ListOpen 转数组
// 常用: 执行方扫描可认领工单
// ═══════════════════════════════════════════════════
using Mau.Runtime;

namespace Mau.Bricks
{
    /// <summary>
    /// OA 积木——oa.list 查单（依赖 OaBridge）
    /// </summary>
    public static class OaListBrick
    {
        /// <summary>
        /// 查单——某大类下、OfficeName 在候选列表中的 Open 单
        /// </summary>
        /// <param name="officeType">工单大类</param>
        /// <param name="officeNames">执行方能干的 OfficeName 候选数组</param>
        /// <param name="offices">匹配的 Open 单列表</param>
        /// <returns>true=查询成功</returns>
        public static bool List(string officeType, string[] officeNames, out Office[] offices)
        {
            IOA? oa;
            DataBox.TryResolve<IOA>(out oa);
            if (oa == null)
            {
                offices = new Office[0];
                return false;
            }
            offices = oa.ListOpen(officeType, officeNames).ToArray();
            return true;
        }
    }
}
// #MAU_CHECKSUM:SHA256:04B3045B0A4C2BE9DAD5492A9F16D691F32A27CA26E92030B1E1FD03FFC4F1A0
