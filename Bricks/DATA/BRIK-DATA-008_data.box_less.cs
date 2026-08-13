// ═══════════════════════════════════════════════════
// 积木: data.box_less
// ID:   BRIK-DATA-008
// 类别: DATA
// 作用: 比较判断——box[boxId, key] < expected 为真
// 依赖: 无
// 引用: 无
// 原理: BoxStore.Get 读值后比较（判断积木 is_ 语义——bool 承载判断结果）
// 常用: 低库存/容量守卫/阈值告警（T4 模块谱新增——box 范围比较补齐）
// ═══════════════════════════════════════════════════

using Mau.Runtime;

namespace Mau.Bricks
{
    /// <summary>
    /// 数据积木——data.box_less box 值小于判断（判断积木语义）
    /// </summary>
    public static class DataBoxLessBrick
    {
        /// <summary>
        /// 小于判断——box[boxId, key] < expected
        /// </summary>
        /// <param name="boxId">作用域 ID</param>
        /// <param name="key">键</param>
        /// <param name="expected">比较右值</param>
        /// <returns>true=box值小于expected</returns>
        public static bool Less(string boxId, string key, int expected)
        {
            int value;
            if (!BoxStore.Get(boxId, key, 0, out value))
            {
                return 0 < expected;
            }
            return value < expected;
        }
    }
}
// #MAU_CHECKSUM:SHA256:351042606552105268CC6812357FE2D59BA19EECBA70A3E2E828BA0C177C16B0
