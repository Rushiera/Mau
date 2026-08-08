// ═══════════════════════════════════════════════════
// 积木: math.random_int
// ID:   BRIK-MATH-004
// 类别: MATH
// 作用: 生成 [min, max) 范围内的随机整数（对标 CH2 CH_Tool_Math.RandomInt）
// 依赖: 无
// 引用: System
// 原理: 静态 Random 实例 Next(min, max)；min >= max 返回 min（CH2 同语义）
// 常用: 投骰子/随机选择/参数随机化（M2c System 域随机数工具）
// ═══════════════════════════════════════════════════
using System;

namespace Mau.Bricks
{
    /// <summary>
    /// 数学积木——math.random_int 随机整数（对标 CH2 RandomInt）
    /// </summary>
    public static class MathRandomIntBrick
    {
        /// <summary>
        /// 全局随机数生成器（CH2 同款语义）
        /// </summary>
        private static readonly Random Random = new Random();

        /// <summary>
        /// 生成 [min, max) 范围内的随机整数——min 含下限，max 不含上限
        /// </summary>
        /// <param name="min">随机范围下限（含）</param>
        /// <param name="max">随机范围上限（不含）</param>
        /// <param name="value">随机整数（min >= max 时返回 min）</param>
        /// <returns>true=成功</returns>
        public static bool RandomInt(int min, int max, out int value)
        {
            if (min >= max)
            {
                value = min;
                return true;
            }
            value = Random.Next(min, max);
            return true;
        }
    }
}
// #MAU_CHECKSUM:SHA256:B4FF709C063DB632D37EFEB100B1EB430A597494CC4257C3D9BC49C6F3C50297
