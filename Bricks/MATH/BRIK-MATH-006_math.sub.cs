// ═══════════════════════════════════════════════════
// 积木: math.sub
// ID:   BRIK-MATH-006
// 类别: MATH
// 作用: 整数减法——a - b
// 依赖: 无
// 引用: 无
// 原理: 纯函数返回 a - b
// 常用: 递减/库存扣减/心算出题（T4 模块谱新增——基础算术补齐）
// ═══════════════════════════════════════════════════

namespace Mau.Bricks
{
    /// <summary>
    /// 数学积木——math.sub 整数减法
    /// </summary>
    public static class MathSubBrick
    {
        /// <summary>
        /// 减法——a - b
        /// </summary>
        /// <param name="a">被减数</param>
        /// <param name="b">减数</param>
        /// <param name="difference">差</param>
        /// <returns>true=成功</returns>
        public static bool Sub(int a, int b, out int difference)
        {
            difference = a - b;
            return true;
        }
    }
}
// #MAU_CHECKSUM:SHA256:E5391256C0A9D19BC8D9FE606303F7D84E87C5506ADCD42016D327F970D524B4
