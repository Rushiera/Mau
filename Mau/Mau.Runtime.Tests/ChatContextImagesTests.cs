using System;
using Mau.Runtime;
using Xunit;

namespace Mau.Runtime.Tests
{
    /// <summary>
    /// 消息附件引用测试（design-ch4-chat-images §8.4-7）——AddUserMessage 带附件重载：
    /// 字段落位 / 无附件零变更 / 双空不入上下文。
    /// </summary>
    public sealed class ChatContextImagesTests
    {
        /// <summary>
        /// 带附件入参——ImagesJson 落位，Role 与 Content 保持。
        /// </summary>
        [Fact]
        public void AddUserMessage_WithImages_SetsImagesJson()
        {
            ChatContext ctx = new ChatContext();
            LlmMessage? msg = ctx.AddUserMessage("看图", "[\"C:/a.png\"]");
            Assert.NotNull(msg);
            Assert.Equal(LlmRole.User, msg.Value.Role);
            Assert.Equal("看图", msg.Value.Content);
            Assert.Equal("[\"C:/a.png\"]", msg.Value.ImagesJson);
            Assert.Equal(1, ctx.GetMessageCount());
        }

        /// <summary>
        /// 无附件入参——纯文本重载路径 ImagesJson 为空串（零回归）。
        /// </summary>
        [Fact]
        public void AddUserMessage_TextOnly_LeavesImagesJsonEmpty()
        {
            ChatContext ctx = new ChatContext();
            LlmMessage? msg = ctx.AddUserMessage("纯文本");
            Assert.NotNull(msg);
            Assert.Equal("", msg.Value.ImagesJson);
        }

        /// <summary>
        /// 文本与附件皆空——不入上下文（返回 null）。
        /// </summary>
        [Fact]
        public void AddUserMessage_BothEmpty_ReturnsNull()
        {
            ChatContext ctx = new ChatContext();
            LlmMessage? msg = ctx.AddUserMessage("", "");
            Assert.Null(msg);
            Assert.Equal(0, ctx.GetMessageCount());
        }

    }
}
