// ═══════════════════════════════════════════════════
// 积木: self.desc.add
// ID:   BRIK-SELF-002
// 类别: SELF
// 作用: 追加实体自述行——DataBox scope=flowId key=self_desc（已有行保留，末尾追加；每参一行，空参跳过）
// 依赖: 无
// 引用: Mau.Runtime（DataBox + FlowContext）
// 原理: 读已有 string[] + 追加 3 参 string 后重写（scope=CurrentFlowId, key=self_desc）——实体自述 R0.1
// 常用: CH4 实体自述——动态行追加（参数引用 @盒 拼实际状态值）
// ═══════════════════════════════════════════════════
using System.Collections.Generic;
using Mau.Runtime;

namespace Mau.Bricks
{
    /// <summary>
    /// 自述积木——self.desc.add 追加实体自述行（已有行保留，末尾追加）
    /// </summary>
    public static class SelfDescAddBrick
    {
        /// <summary>
        /// 追加实体自述行——DataBox scope=flowId key=self_desc
        /// </summary>
        /// <param name="line1">第一行（空串跳过）</param>
        /// <param name="line2">第二行（空串跳过）</param>
        /// <param name="line3">第三行（空串跳过）</param>
        /// <returns>true=已写入</returns>
        public static bool Add(string line1, string line2, string line3)
        {
            string scope = FlowContext.CurrentFlowId.ToString();
            List<string> lines = new List<string>();
            string[] existing = null;
            if (DataBox.TryGet<string[]>(scope, "self_desc", out existing) && existing != null)
            {
                lines.AddRange(existing);
            }
            if (line1.Length > 0)
            {
                lines.Add(line1);
            }
            if (line2.Length > 0)
            {
                lines.Add(line2);
            }
            if (line3.Length > 0)
            {
                lines.Add(line3);
            }
            DataBox.Set<string[]>(scope, "self_desc", lines.ToArray());
            return true;
        }
    }
}
// #MAU_CHECKSUM:SHA256:7E52B63DA6DD7B13096B5758C8CB01E96F1CAA8AA0D45D631C2072548961CD91
