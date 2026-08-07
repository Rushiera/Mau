using System.Collections.Generic;
using Mau.Runtime;

namespace Mau.Runtime
{
    /// <summary>
    /// 上下文存储（程序级）——会话表经 DataBox scope 映射（BRIK 唯一数据协议）
    /// 每会话独立 scope "ctx:{sessionKey}"，避免跨会话锁竞争
    /// </summary>
    public static class ContextStore
    {
        /// <summary>
        /// 获取或创建会话——按 sessionKey 隔离
        /// </summary>
        /// <param name="sessionKey">会话 Key</param>
        /// <returns>会话</returns>
        public static ContextSession GetOrCreate(string sessionKey)
        {
            return DataBox.GetOrCreate<ContextSession>("ctx:" + BrickText.SafeKey(sessionKey), "session");
        }

        /// <summary>
        /// 统计所有消息字段的 UTF-16 字符数
        /// </summary>
        /// <param name="session">会话</param>
        /// <returns>字符总数</returns>
        public static int CountAllChars(ContextSession session)
        {
            int total = 0;
            for (int i = 0; i < session.History.Count; i = i + 1)
            {
                total = total + BrickText.SafeText(session.History[i].Content).Length
                    + BrickText.SafeText(session.History[i].ToolCallId).Length
                    + BrickText.SafeText(session.History[i].ToolName).Length
                    + BrickText.SafeText(session.History[i].ToolCallsJson).Length;
            }
            return total;
        }

        /// <summary>
        /// 创建全部字符串字段非空的消息
        /// </summary>
        /// <param name="role">角色</param>
        /// <param name="content">正文</param>
        /// <returns>消息</returns>
        public static LlmMessage CreateMessage(string role, string content)
        {
            LlmMessage message;
            message.Role = role;
            message.Content = content;
            message.ToolCallId = "";
            message.ToolName = "";
            message.ToolCallsJson = "";
            return message;
        }
    }
}
