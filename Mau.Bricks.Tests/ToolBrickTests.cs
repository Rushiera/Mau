// ═══════════════════════════════════════════════
// 测试: Mau.Bricks.Standard——ToolBrick（工具执行机制积木）
// 引用: Mau.Bricks.Tests → Mau.Bricks.Standard + Mau.Contracts
// 原理: 执行器注入 Configure → 分发验证；未注入时拒绝
// 常用: tool.exec 积木正确性回归——oa_flow/tool_dispatch 语料前置
// ═══════════════════════════════════════════════
using System;
using Mau.Bricks;
using Xunit;

namespace Mau.Bricks.Tests
{
    /// <summary>
    /// 工具执行积木测试——执行器注入分发 + 未注入防护
    /// </summary>
    public sealed class ToolBrickTests
    {
        /// <summary>
        /// 未注入执行器时返回 false——积木不可静默空转
        /// </summary>
        [Fact]
        public void ToolBrick_Unconfigured_ReturnsFalse()
        {
            string[] result;
            Assert.False(ToolBrick.Exec("file.read", new string[] { "x" }, out result));
        }

        /// <summary>
        /// 注入后按工具名分发——参数完整传递
        /// </summary>
        [Fact]
        public void ToolBrick_Exec_DispatchToExecutor()
        {
            ToolBrick.Configure(delegate (string name, string[] args)
            {
                string joined = "";
                for (int i = 0; i < args.Length; i++)
                {
                    if (i > 0)
                    {
                        joined = joined + ",";
                    }
                    joined = joined + args[i];
                }
                return new string[] { name + ":" + joined };
            });
            try
            {
                string[] result;
                Assert.True(ToolBrick.Exec("file.read", new string[] { "a.txt", "b.txt" }, out result));
                Assert.Equal("file.read:a.txt,b.txt", result[0]);
            }
            finally
            {
                ToolBrick.Configure(null!);
            }
        }
    }
}
