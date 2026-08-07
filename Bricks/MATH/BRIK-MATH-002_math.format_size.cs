// ═══════════════════════════════════════════════════
// 积木: math.format_size
// ID:   BRIK-MATH-002
// 类别: MATH
// 作用: 按十进制 B/K/M 阈值格式化数量
// 依赖: 无
// 引用: System · System.Globalization
// 原理: 阈值比较（≥1000000→M / ≥1000→K / 其余 B）+ InvariantCulture 格式化
// 常用: 日志大小展示 / 结果摘要
// ═══════════════════════════════════════════════════
using System;
using System.Globalization;

namespace Mau.Bricks
{
    /// <summary>
    /// 数学积木——math.format_size 按十进制 B/K/M 阈值格式化数量
    /// </summary>
    public static class MathFormatSizeBrick
    {
        /// <summary>
        /// 按十进制 B/K/M 阈值格式化数量
        /// </summary>
        /// <param name="bytes">数量</param>
        /// <param name="formatted">格式文本</param>
        /// <returns>true=成功</returns>
        public static bool FormatSize(long bytes, out string formatted)
        {
            if (bytes >= 1000000)
            {
                formatted = (bytes / 1000000.0).ToString("F2", CultureInfo.InvariantCulture) + " M";
                return true;
            }
            if (bytes >= 1000)
            {
                formatted = (bytes / 1000.0).ToString("F2", CultureInfo.InvariantCulture) + " K";
                return true;
            }
            formatted = bytes.ToString(CultureInfo.InvariantCulture) + " B";
            return true;
        }
    }
}
// #MAU_CHECKSUM:SHA256:B9E8A0716A08C4E91CE6CA40438502E42D634B0F48FB2258A09637B0A0BFC13A
