// ═══════════════════════════════════════════════════
// 积木: math.is_all_digits
// ID:   BRIK-MATH-001
// 类别: MATH
// 作用: 判断非空文本是否全部由 ASCII 数字组成
// 依赖: 无
// 引用: System
// 原理: 逐字符 ASCII 数字范围检查（'0'~'9'）
// 常用: 工具系统参数校验
// ═══════════════════════════════════════════════════
using System;

namespace Mau.Bricks
{
    /// <summary>
    /// 数学积木——math.is_all_digits 判断非空文本是否全部由 ASCII 数字组成
    /// </summary>
    public static class MathIsAllDigitsBrick
    {
        /// <summary>
        /// 判断非空文本是否全部由 ASCII 数字组成
        /// </summary>
        /// <param name="text">待检查文本</param>
        /// <returns>是否全为数字</returns>
        public static bool IsAllDigits(string? text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return false;
            }
            for (int i = 0; i < text.Length; i = i + 1)
            {
                if (text[i] < '0' || text[i] > '9')
                {
                    return false;
                }
            }
            return true;
        }
    }
}
// #MAU_CHECKSUM:SHA256:4D9B98B20EF3CA81AC77F2CE58559CF763E725AFF7AB8B33F0B43273A2670D60
