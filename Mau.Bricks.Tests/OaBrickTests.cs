// ═══════════════════════════════════════════════
// 测试: Mau.Bricks.Standard——OaBrick（OA 机制积木·双字典）
// 引用: Mau.Bricks.Tests → Mau.Bricks.Standard + Mau.Runtime + Mau.Contracts
// 原理: 真实 OA 实例注入 Configure → 全操作闭环验证；未注入时全部拒绝
// 常用: OA 积木正确性回归——双字典载荷（design-mau-module §二）前置
// ═══════════════════════════════════════════════
using System;
using Mau.Bricks;
using Mau.Runtime;
using Xunit;

namespace Mau.Bricks.Tests
{
    /// <summary>
    /// OA 机制积木测试——真实 OA 实例全闭环（双字典载荷）+ 未注入防护
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
            Assert.False(OaBrick.Post(1, "chat", "hello", 300, out officeId));
            Office[] offices;
            Assert.False(OaBrick.List("chat", new string[] { "hello" }, out offices));
            Assert.False(OaBrick.Claim(2, new long[] { 1 }, out offices));
            Assert.False(OaBrick.Complete(1, 2, OfficeData.Empty()));
            Assert.False(OaBrick.Settle(1, 2));
            int intValue;
            string strValue;
            Assert.False(OaBrick.GetInt(1, "k", out intValue));
            Assert.False(OaBrick.GetStr(1, "k", out strValue));
        }

        /// <summary>
        /// 全闭环——Post → set_* 写载荷 → List → Claim → get_* 读载荷 → Complete，状态 Open→Work→Closed
        /// </summary>
        [Fact]
        public void OaBrick_PostSetListClaimGetComplete_FullCycle()
        {
            ThreadGuard guard = new ThreadGuard();
            OA oa = new OA(guard);
            OaBrick.Configure(oa);
            try
            {
                long officeId;
                Assert.True(OaBrick.Post(1, "chat", "hello", 300, out officeId));
                Assert.True(officeId > 0);

                // Dog 侧按 Key 写载荷
                Assert.True(OaBrick.SetStr(officeId, 1, "msg", "你好"));
                Assert.True(OaBrick.SetInt(officeId, 1, "count", 7));

                Office[] open;
                Assert.True(OaBrick.List("chat", new string[] { "hello" }, out open));
                Assert.Single(open);
                Assert.Equal(officeId, open[0].OfficeId);

                Office[] claimed;
                Assert.True(OaBrick.Claim(2, new long[] { officeId }, out claimed));
                Assert.Single(claimed);
                Assert.Equal(OfficeState.Work, oa.GetStatus(officeId));

                // Cat 侧按 Key 读载荷
                string msg;
                int count;
                Assert.True(OaBrick.GetStr(officeId, "msg", out msg));
                Assert.True(OaBrick.GetInt(officeId, "count", out count));
                Assert.Equal("你好", msg);
                Assert.Equal(7, count);

                // Cat 侧写回执并完成
                OfficeData result = OfficeData.Empty();
                result.Strs["reply"] = "回执";
                Assert.True(OaBrick.Complete(officeId, 2, result));
                Assert.Equal(OfficeState.Closed, oa.GetStatus(officeId));

                Office done = oa.GetOffice(officeId);
                Assert.Equal("回执", done.Result.Strs["reply"]);
            }
            finally
            {
                OaBrick.Configure(null!);
            }
        }

        /// <summary>
        /// Set 权限——非本人/非 Open 状态拒绝写入
        /// </summary>
        [Fact]
        public void OaBrick_Set_RejectsNonOwnerOrNonOpen()
        {
            ThreadGuard guard = new ThreadGuard();
            OA oa = new OA(guard);
            OaBrick.Configure(oa);
            try
            {
                long officeId;
                Assert.True(OaBrick.Post(1, "chat", "hello", 300, out officeId));

                // 非本人写拒绝
                Assert.False(OaBrick.SetStr(officeId, 99, "msg", "x"));
                // 本人写成功
                Assert.True(OaBrick.SetStr(officeId, 1, "msg", "x"));
                // Claim 后（Work 状态）非本人可读但不能再写
                Office[] claimed;
                Assert.True(OaBrick.Claim(2, new long[] { officeId }, out claimed));
                Assert.False(OaBrick.SetStr(officeId, 1, "msg2", "y"));
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
                Assert.True(OaBrick.Post(1, "chat", "hello", 300, out officeId));
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
                Assert.True(OaBrick.Post(1, "chat", "hello", 3, out officeId));
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
