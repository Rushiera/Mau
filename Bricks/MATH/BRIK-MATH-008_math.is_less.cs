// ═══════════════════════════════════════════════════
// 积木: math.is_less
// ID:   BRIK-MATH-008
// 类别: MATH
// 作用: 比较判断——a < b 为真
// 依赖: 无
// 引用: 无
// 原理: 纯函数返回 a < b（判断积木 is_ 命名——bool 承载语义）
// 常用: 超时判定/阈值比较/范围守卫（T4 模块谱新增——比较能力补齐）
// ═══════════════════════════════════════════════════

namespace Mau.Bricks
{
    /// <summary>
    /// 数学积木——math.is_less 小于判断（判断积木语义：返回 = 判断结果）
    /// </summary>
    public static class MathIsLessBrick
    {
        /// <summary>
        /// 小于判断——a < b
        /// </summary>
        /// <param name="a">左值</param>
        /// <param name="b">右值</param>
        /// <returns>true=a小于b</returns>
        public static bool IsLess(int a, int b)
        {
            return a < b;
        }
    }
}
// #MAU_CHECKSUM:SHA256:4CAFB167E443251B6C4A3AD65A19827B41914EEA2CDE3129888B851CA56A92D9
