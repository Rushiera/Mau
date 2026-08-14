// ═══════════════════════════════════════════════════
// 积木: probe.source
// ID:   BRIK-TEST-001
// 类别: TEST
// 作用: 测试探针——固定输出源（数据流绑定验证用：主值文本 + 完整数组）
// 依赖: 无
// 引用: System
// 原理: ownerId 派生固定文本 + 双输出端口（text 主值 / texts 数组）
// 常用: 翻译器数据流测试（箭头/常量/数组绑定）
// ═══════════════════════════════════════════════════
using System;

namespace Mau.Bricks
{
    /// <summary>
    /// 测试探针积木——probe.source 固定输出源（数据流绑定验证）
    /// </summary>
    public static class ProbeSourceBrick
    {
        /// <summary>
        /// 固定输出——ownerId 派生文本 + 数组
        /// </summary>
        /// <param name="ownerId">所有者</param>
        /// <param name="text">主值文本</param>
        /// <param name="texts">完整数组</param>
        /// <returns>true=成功</returns>
        public static bool Source(long ownerId, out string text, out string[] texts)
        {
            text = "probe-" + ownerId.ToString();
            texts = new string[] { text, "extra" };
            return true;
        }
    }
}
// #MAU_CHECKSUM:SHA256:8FBAC399D17C0C799CFAF0E2BB01E0F2DD1E43CC02B3F8D81C53B148A4A3C9D2
