// ═══════════════════════════════════════════════════
// 积木: math.add
// ID:   BRIK-MATH-005
// 类别: MATH
// 作用: 整数加法——a + b
// 依赖: 无
// 引用: 无
// 原理: 纯函数返回 a + b
// 常用: 计数/累加/心算出题（T4 模块谱新增——基础算术补齐）
// ═══════════════════════════════════════════════════

namespace Mau.Bricks
{
    /// <summary>
    /// 数学积木——math.add 整数加法
    /// </summary>
    public static class MathAddBrick
    {
        /// <summary>
        /// 加法——a + b
        /// </summary>
        /// <param name="a">加数</param>
        /// <param name="b">加数</param>
        /// <param name="sum">和</param>
        /// <returns>true=成功</returns>
        public static bool Add(int a, int b, out int sum)
        {
            sum = a + b;
            return true;
        }
    }
}
// #MAU_CHECKSUM:SHA256:B6C8B157D0AD97613F02FB99C8C855027BD6BA1CF4F122BCB084B52767D44D15
