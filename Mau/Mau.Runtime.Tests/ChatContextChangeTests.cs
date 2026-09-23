using System;
using Mau.Runtime;
using Xunit;

namespace Mau.Runtime.Tests
{
    /// <summary>
    /// 前文变动时刻测试——ChatContext.LastChangeAt（A89：最后一次前文变动，含工具结果写入；恢复导入不刷新）。
    /// </summary>
    public sealed class ChatContextChangeTests
    {
        /// <summary>初始态——从未变动时为 0（展示层据此不显示「距今」）。</summary>
        [Fact]
        public void LastChangeAt_ZeroOnNew()
        {
            ChatContext ctx = new ChatContext();
            Assert.Equal(0, ctx.LastChangeAt);
        }

        /// <summary>追加刷新——用户消息 / 工具声明 / 工具结果都算前文变动，时间戳落在调用前后区间内。</summary>
        [Fact]
        public void LastChangeAt_RefreshOnAppend()
        {
            ChatContext ctx = new ChatContext();
            long before = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            ctx.AddUserMessage("你好");
            long now1 = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            Assert.InRange(ctx.LastChangeAt, before, now1);
            long userStamp = ctx.LastChangeAt;

            ctx.AddAssistantToolCalls("[{\"id\":\"c1\",\"function\":{\"name\":\"time\"}}]", "");
            ctx.AddToolResult("c1", "time", "2026-09-23 18:00:00");
            // 工具结果写入同样算前文变动（A89 拍板）——时间戳不倒退
            Assert.True(ctx.LastChangeAt >= userStamp);
            Assert.True(ctx.LastChangeAt > 0);
        }

        /// <summary>恢复不刷新——ReplaceMessages（重启恢复）保留原值 0（恢复不是活跃）。</summary>
        [Fact]
        public void LastChangeAt_KeepOnReplaceMessages()
        {
            ChatContext ctx = new ChatContext();
            LlmMessage msg = new LlmMessage();
            msg.Role = LlmRole.User;
            msg.Content = "历史";
            msg.ToolCallId = "";
            msg.ToolName = "";
            msg.ToolCallsJson = "";
            msg.ReasoningContent = "";
            msg.CreatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            ctx.ReplaceMessages(new LlmMessage[] { msg });
            Assert.Equal(1, ctx.GetMessageCount());
            Assert.Equal(0, ctx.LastChangeAt);
        }
    }
}
