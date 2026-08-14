// ═══════════════════════════════════════════════════
// 积木: oa.get_str
// ID:   BRIK-OA-005
// 类别: OA
// 作用: 读取工单载荷/回执 str 值——执行方消费载荷，挂单方收结果
// 依赖: 无
// 引用: Mau.Runtime（IOA/DataBox）
// 原理: DataBox.TryResolve<IOA> → GetStr(officeId, key, out value)
// 常用: CH4 第一轮三 Cat 语料——参数读取/结果收集
// ═══════════════════════════════════════════════════
using Mau.Runtime;

namespace Mau.Bricks
{
    /// <summary>
    /// OA 积木——oa.get_str 读工单载荷
    /// </summary>
    public static class OaGetStrBrick
    {
        /// <summary>
        /// 读取工单载荷/回执 str 值
        /// </summary>
        /// <param name="officeId">Office ID</param>
        /// <param name="key">载荷 Key</param>
        /// <param name="value">str 值——Key 不存在为空串</param>
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
// #MAU_CHECKSUM:SHA256:03400F67EA64E63CB2F7D66735FDAFD8C8C8A7BF5EE148309F547FA8E85BA849
