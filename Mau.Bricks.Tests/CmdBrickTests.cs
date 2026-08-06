// ═══════════════════════════════════════════════
// 测试: Mau.Bricks.Standard——CmdBrick（指令机制积木）
// ID:   BRIK-CMD-001 ~ 005 配套
// 引用: Mau.Bricks.Tests → Mau.Bricks.Standard + Mau.Runtime + Mau.Contracts
// 原理: 注入真实 CommandBus → 注册/投递/消费闭环验证；未注入时拒绝
// 常用: cmd.* 积木正确性回归——talkcat_fsm 指令入口前置
// ═══════════════════════════════════════════════
using System;
using Mau.Bricks;
using Mau.Runtime;
using Xunit;

namespace Mau.Bricks.Tests
{
    /// <summary>
    /// 指令积木测试——未注入防护 + 注册/投递/消费闭环 + 模板邮件语义
    /// </summary>
    public sealed class CmdBrickTests
    {
        /// <summary>
        /// 未注入总线时全部返回 false——积木不可静默空转
        /// </summary>
        [Fact]
        public void CmdBrick_Unconfigured_ReturnsFalse()
        {
            bool hasCommands;
            string[] cmdKeys;
            int[] cmdValues;
            string[] cmdTexts;
            string text;
            Assert.False(CmdBrick.Register(1, new string[] { "chat_talk_msg" }));
            Assert.False(CmdBrick.Consume(1, out hasCommands, out cmdKeys,
                out cmdValues, out cmdTexts, out text));
            Assert.False(CmdBrick.Set("chat_talk_msg", 1, "hello"));
        }

        /// <summary>
        /// 注册 → 投递 → 冻结 → 消费——int 与 text 双轨完整传递
        /// </summary>
        [Fact]
        public void CmdBrick_RegisterSetConsume_RoundTrip()
        {
            ThreadGuard guard = new ThreadGuard();
            CommandBus bus = new CommandBus(guard);
            CmdBrick.Configure(bus);
            try
            {
                Assert.True(CmdBrick.Register(42, new string[] { "chat_talk_msg" }));
                Assert.True(CmdBrick.Set("chat_talk_msg", 7, "你好"));
                bus.BeginTickInput();
                bool hasCommands;
                string[] cmdKeys;
                int[] cmdValues;
                string[] cmdTexts;
                string text;
                Assert.True(CmdBrick.Consume(42, out hasCommands, out cmdKeys,
                    out cmdValues, out cmdTexts, out text));
                Assert.True(hasCommands);
                Assert.Single(cmdKeys);
                Assert.Equal("chat_talk_msg", cmdKeys[0]);
                Assert.Equal(7, cmdValues[0]);
                Assert.Equal("你好", cmdTexts[0]);
                Assert.Equal("你好", text);
            }
            finally
            {
                CmdBrick.Configure(null!);
            }
        }

        /// <summary>
        /// 未投递时——模板邮件 HasCommands=false
        /// </summary>
        [Fact]
        public void CmdBrick_NoInput_TemplateEmailHasNoCommands()
        {
            ThreadGuard guard = new ThreadGuard();
            CommandBus bus = new CommandBus(guard);
            CmdBrick.Configure(bus);
            try
            {
                Assert.True(CmdBrick.Register(42, new string[] { "chat_talk_msg" }));
                bus.BeginTickInput();
                bool hasCommands;
                string[] cmdKeys;
                int[] cmdValues;
                string[] cmdTexts;
                string text;
                // 无新指令——Consume 返回 false（语料进入等待分支）
                Assert.False(CmdBrick.Consume(42, out hasCommands, out cmdKeys,
                    out cmdValues, out cmdTexts, out text));
                Assert.False(hasCommands);
                Assert.Single(cmdKeys);
            }
            finally
            {
                CmdBrick.Configure(null!);
            }
        }

        /// <summary>
        /// 注销后消费——空邮件
        /// </summary>
        [Fact]
        public void CmdBrick_Unregister_ConsumeReturnsEmpty()
        {
            ThreadGuard guard = new ThreadGuard();
            CommandBus bus = new CommandBus(guard);
            CmdBrick.Configure(bus);
            try
            {
                Assert.True(CmdBrick.Register(42, new string[] { "chat_talk_msg" }));
                Assert.True(CmdBrick.Unregister(42));
                bool hasCommands;
                string[] cmdKeys;
                int[] cmdValues;
                string[] cmdTexts;
                string text;
                // 注销后无邮件——Consume 返回 false（无指令语义统一）
                Assert.False(CmdBrick.Consume(42, out hasCommands, out cmdKeys,
                    out cmdValues, out cmdTexts, out text));
                Assert.False(hasCommands);
                Assert.Empty(cmdKeys);
            }
            finally
            {
                CmdBrick.Configure(null!);
            }
        }
    }
}
