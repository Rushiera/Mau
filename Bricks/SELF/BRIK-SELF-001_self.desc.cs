// ═══════════════════════════════════════════════════
// 积木: self.desc
// ID:   BRIK-SELF-001
// 类别: SELF
// 作用: 覆盖写实体自述——DataBox scope=flowId key=self_desc（多行：每参一行，空参跳过）
// 依赖: 无
// 引用: Mau.Runtime（DataBox + FlowContext）
// 原理: 3 参 string 拼接为 string[] 写 DataBox（scope=CurrentFlowId, key=self_desc）——实体自述 R0.1
// 常用: CH4 实体自述——导线动作写模块自己的状态表述（观测面；自述是输出不是输入）
// ═══════════════════════════════════════════════════
using System.Collections.Generic;
using Mau.Runtime;

namespace Mau.Bricks
{
    /// <summary>
    /// 自述积木——self.desc 覆盖写实体自述（多行：每参一行，空参跳过）
    /// </summary>
    public static class SelfDescBrick
    {
        /// <summary>
        /// 覆盖写实体自述——DataBox scope=flowId key=self_desc
        /// </summary>
        /// <param name="line1">第一行（空串跳过）</param>
        /// <param name="line2">第二行（空串跳过）</param>
        /// <param name="line3">第三行（空串跳过）</param>
        /// <returns>true=已写入</returns>
        public static bool Desc(string line1, string line2, string line3)
        {
            string scope = FlowContext.CurrentFlowId.ToString();
            List<string> lines = new List<string>();
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
// #MAU_CHECKSUM:SHA256:38E898D183C6B43F5A5482FDD985CB0A3C8ECB8A6167586109A768145D502813
