// ═══════════════════════════════════════════════════
// 积木: oa.get_str
// ID:   BRIK-OA-009
// 类别: OA
// 作用: 读取请求载荷 str 值——执行方消费
// 依赖: 无
// 引用: Mau.Runtime
// 原理: 经 OaBridge 实例调用 IOA.GetStr
// 常用: 执行方读请求参数（ID/时间戳/JSON）
// ═══════════════════════════════════════════════════
using Mau.Runtime;

namespace Mau.Bricks
{
    /// <summary>
    /// OA 积木——oa.get_str 读请求载荷 str 值（依赖 OaBridge）
    /// </summary>
    public static class OaGetStrBrick
    {
        /// <summary>
        /// 读取请求载荷 str 值——执行方消费
        /// </summary>
        /// <param name="officeId">Office ID</param>
        /// <param name="key">Key</param>
        /// <param name="value">str 值</param>
        /// <returns>true=Key 存在</returns>
        public static bool GetStr(long officeId, string key, out string value)
        {
            IOA? oa;
            DataBox.TryResolve<IOA>(out oa);
            if (oa == null)
            {
                value = "";
                return false;
            }
            return oa.GetStr(officeId, key, out value);
        }
    }
}
// #MAU_CHECKSUM:SHA256:2E0ECBBB5AB65190380E76BD53E306C7B674FF64605C9CF8D21278BC931A3F75
