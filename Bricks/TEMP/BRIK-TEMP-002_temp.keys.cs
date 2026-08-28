// ═══════════════════════════════════════════════════
// 积木: temp.keys
// ID:   BRIK-TEMP-002
// 类别: TEMP
// 作用: 临时工具 Key 组枚举——返回当前 TempRegistry 全部可用 Key（逗号分隔）
// 依赖: 无
// 引用: 无
// 原理: 读 TempRegistry.ListKeys()（临时工具注册表——LLM 可改区）→ out keys
// 常用: TempToolCat 认领线——'temp.keys'[] > @keys（temp-info 工具消费面）
// ═══════════════════════════════════════════════════
#nullable disable warnings
using System;

namespace Mau.Bricks
{
    /// <summary>
    /// 临时积木——temp.keys 枚举当前可用临时工具 Key 组（temp-info 消费面）
    /// </summary>
    public static class TempKeysBrick
    {
        /// <summary>
        /// Key 组枚举——逗号分隔
        /// </summary>
        /// <param name="keys">Key 组文本（逗号分隔；空 = 暂无临时工具）</param>
        /// <returns>true=已输出</returns>
        public static bool Keys(out string keys)
        {
            keys = TempRegistry.ListKeys();
            return true;
        }
    }
}
// #MAU_CHECKSUM:SHA256:9A39EA03638E3159997A51B99FC75211D4681212CA960FC5C2660DCD3B88D8AD
