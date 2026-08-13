// ═══════════════════════════════════════════════════
// 积木: math.mul
// ID:   BRIK-MATH-007
// 类别: MATH
// 作用: 整数乘法——a × b
// 依赖: 无
// 引用: 无
// 原理: 纯函数返回 a * b
// 常用: 加权/倍率/心算出题（T4 模块谱新增——基础算术补齐）
// ═══════════════════════════════════════════════════

namespace Mau.Bricks
{
    /// <summary>
    /// 数学积木——math.mul 整数乘法
    /// </summary>
    public static class MathMulBrick
    {
        /// <summary>
        /// 乘法——a * b
        /// </summary>
        /// <param name="a">乘数</param>
        /// <param name="b">乘数</param>
        /// <param name="product">积</param>
        /// <returns>true=成功</returns>
        public static bool Mul(int a, int b, out int product)
        {
            product = a * b;
            return true;
        }
    }
}
// #MAU_CHECKSUM:SHA256:B0B044A8712CBE2CE34E1202ED6DE1C6D06E221A3E6982619E50D69387BF2F56
