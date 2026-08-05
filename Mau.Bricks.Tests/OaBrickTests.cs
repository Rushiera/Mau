// ═══════════════════════════════════════════════
// 测试: Mau.Bricks.Standard——OaBrick（OA 机制积木）
// 引用: Mau.Bricks.Tests → Mau.Bricks.Standard + Mau.Runtime + Mau.Contracts
// 原理: 真实 OA 实例注入 Configure → 全操作闭环验证；未注入时全部拒绝
// 常用: OA 积木正确性回归——CH4 P1.3 oa_flow.mau 前置
// ═══════════════════════════════════════════════
using System;
using Mau.Bricks;
using Mau.Runtime;
using Xunit;

namespace Mau.Bricks.Tests
{
    /// <summary>
    /// OA 机制积木测试——真实 OA 实例全闭环 + 未注入防护
    /// </summary>
    public sealed class OaBrickTests
    {
        /// <summary>
        /// 未注入实例时全部操作返回 false——积木不可静默空转
        /// </summary>
        [Fact]
        public void OaBrick_Unconfigured_ReturnsFalse()
        {
            long officeId;
            Assert.False(OaBrick.Post(1, "chat", "hello", new string[] { "x" }, null!, 300, out officeId));
            Office[] offices;
            Assert.False(OaBrick.List("chat", new string[] { "hello" }, out offices));
            Assert.False(OaBrick.Claim(2, new long[] { 1 }, out offices));
            Assert.False(OaBrick.Complete(1, 2, null!, null!));
            Assert.False(OaBrick.Settle(1, 2));
        }

        /// <summary>
        /// 全闭环——Post → List → Claim → Complete，状态 Open→Work→Closed
        /// </summary>
        [Fact]
        public void OaBrick_PostListClaimComplete_FullCycle()
        {
            ThreadGuard guard = new ThreadGuard();
            OA oa = new OA(guard);
            OaBrick.Configure(oa);
            try
            {
                long officeId;
                Assert.True(OaBrick.Post(1, "chat", "hello", new string[] { "你好" }, null!, 300, out officeId));
                Assert.True(officeId > 0);

                Office[] open;
                Assert.True(OaBrick.List("chat", new string[] { "hello" }, out open));
                Assert.Single(open);
                Assert.Equal(officeId, open[0].OfficeId);

                Office[] claimed;
                Assert.True(OaBrick.Claim(2, new long[] { officeId }, out claimed));
                Assert.Single(claimed);
                Assert.Equal(OfficeState.Work, oa.GetStatus(officeId));

                Assert.True(OaBrick.Complete(officeId, 2, new string[] { "回执" }, null!));
                Assert.Equal(OfficeState.Closed, oa.GetStatus(officeId));
            }
            finally
            {
                OaBrick.Configure(null!);
            }
        }

        /// <summary>
        /// Settle——Work 单退回 Open（可重投）
        /// </summary>
        [Fact]
        public void OaBrick_Settle_RelistsWorkOrder()
        {
            ThreadGuard guard = new ThreadGuard();
            OA oa = new OA(guard);
            OaBrick.Configure(oa);
            try
            {
                long officeId;
                Assert.True(OaBrick.Post(1, "chat", "hello", null!, null!, 300, out officeId));
                Office[] claimed;
                Assert.True(OaBrick.Claim(2, new long[] { officeId }, out claimed));
                Assert.Equal(OfficeState.Work, oa.GetStatus(officeId));

                Assert.True(OaBrick.Settle(officeId, 2));
                Assert.Equal(OfficeState.Open, oa.GetStatus(officeId));
            }
            finally
            {
                OaBrick.Configure(null!);
            }
        }

        /// <summary>
        /// Settle——已终结单（Closed/TimeOut）确认返回 true，不重复操作
        /// </summary>
        [Fact]
        public void OaBrick_Settle_ConfirmsTerminatedOrder()
        {
            ThreadGuard guard = new ThreadGuard();
            OA oa = new OA(guard);
            OaBrick.Configure(oa);
            try
            {
                long officeId;
                Assert.True(OaBrick.Post(1, "chat", "hello", null!, null!, 3, out officeId));
                // 3 帧超时——驱动 OA.Tick 结算
                oa.Tick();
                oa.Tick();
                oa.Tick();
                oa.Tick();
                Assert.Equal(OfficeState.TimeOut, oa.GetStatus(officeId));
                Assert.True(OaBrick.Settle(officeId, 1));
                Assert.Equal(OfficeState.TimeOut, oa.GetStatus(officeId));
            }
            finally
            {
                OaBrick.Configure(null!);
            }
        }
    }
}
