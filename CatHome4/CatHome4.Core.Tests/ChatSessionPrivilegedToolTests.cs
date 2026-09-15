using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Mau.Runtime;
using CatHome4.Contracts;
using Xunit;

namespace CatHome4.Core.Tests
{
    /// <summary>
    /// 特权面（majordomo-*）可见性测试——默认会话保留 / 非默认会话剔除（design-ch4-host-restart §二）。
    /// 回归判据：特权面授权基准 = 猫 key 或显示名，不取运行时会话 ID（时间戳）。
    /// </summary>
    public class ChatSessionPrivilegedToolTests
    {
        /// <summary>
        /// 捕获工具面的 Mock LLM——ChatStream 入参携带本会话工具面，固定回复纯文本。
        /// </summary>
        private sealed class CaptureLlm : ILlmRuntime
        {
            /// <summary>最近一次请求携带的工具面</summary>
            public ToolSpec[] LastTools;

            /// <summary>
            /// 流式对话——记录工具面后返回纯文本回复。
            /// </summary>
            /// <param name="messages">消息序列</param>
            /// <param name="tools">工具定义</param>
            /// <param name="userId">用户标识</param>
            /// <param name="ct">取消令牌</param>
            /// <returns>事件流</returns>
            public async IAsyncEnumerable<LlmStreamEvent> ChatStream(LlmMessage[] messages, ToolSpec[] tools, string userId = "", [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
            {
                LastTools = tools;
                yield return new LlmStreamEvent(LlmStreamKind.Text, "ok");
                await Task.Yield();
                yield return new LlmStreamEvent(LlmStreamKind.Done, "");
            }
        }

        /// <summary>
        /// 测试工具面——内置 Note + 特权工具 majordomo-restart。
        /// </summary>
        /// <returns>工具面数组</returns>
        private static ToolSpec[] BuildToolFace()
        {
            return new ToolSpec[]
            {
                new ToolSpec("Note", "Note 任务追踪", "{}"),
                new ToolSpec("majordomo-restart", "宿主自更新", "{}")
            };
        }

        /// <summary>
        /// 构造测试会话——临时前文文件 + Mock LLM + 指定工具面。
        /// </summary>
        /// <param name="displayName">显示名</param>
        /// <param name="tools">工具面</param>
        /// <param name="llm">Mock LLM（工具面捕获）</param>
        /// <returns>会话实体</returns>
        private static CH4.ChatSession CreateSession(string displayName, ToolSpec[] tools, CaptureLlm llm)
        {
            ChatContext ctx = new ChatContext();
            string tmp = Path.Combine(Path.GetTempPath(), "cat4test_" + Guid.NewGuid().ToString("N") + ".jsonl");
            SessionStore store = new SessionStore(tmp);
            OA oa = new OA(new ThreadGuard());
            CH4.SessionViewStore viewStore = new CH4.SessionViewStore(Path.Combine(Path.GetTempPath(), "cat4test_" + Guid.NewGuid().ToString("N") + ".view.json"));
            return new CH4.ChatSession(DateTime.Now.Ticks.ToString(), displayName, ctx, store, llm, oa, tools, delegate (string name, string args) { return "ERR|NO_TOOL|" + name; }, viewStore);
        }

        /// <summary>
        /// 泵会话直到 Idle——上限 500 帧（防死循环）。
        /// </summary>
        /// <param name="session">会话</param>
        private static void PumpUntilIdle(CH4.ChatSession session)
        {
            for (int i = 0; i < 500; i = i + 1)
            {
                session.Pump();
                Thread.Sleep(5);
                if (session.IsIdle && i >= 2)
                {
                    break;
                }
            }
        }

        /// <summary>
        /// 工具面是否含指定工具名。
        /// </summary>
        /// <param name="specs">工具面</param>
        /// <param name="name">工具名</param>
        /// <returns>true=含</returns>
        private static bool ContainsTool(ToolSpec[] specs, string name)
        {
            if (specs == null)
            {
                return false;
            }
            for (int i = 0; i < specs.Length; i = i + 1)
            {
                if (specs[i].Name == name)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// 默认会话（显示名 majordomo）——特权工具保留在工具面。
        /// </summary>
        [Fact]
        public void DefaultSession_KeepsPrivilegedTool()
        {
            CaptureLlm llm = new CaptureLlm();
            CH4.ChatSession session = CreateSession("majordomo", BuildToolFace(), llm);
            session.PostUserMessage("测试");
            PumpUntilIdle(session);
            Assert.True(ContainsTool(llm.LastTools, "majordomo-restart"), "默认会话应保留特权工具");
            Assert.True(ContainsTool(llm.LastTools, "Note"), "默认会话应保留普通工具");
        }

        /// <summary>
        /// 非默认会话（显示名 cat2）——特权工具被剔除，普通工具保留。
        /// </summary>
        [Fact]
        public void NonDefaultSession_DropsPrivilegedTool()
        {
            CaptureLlm llm = new CaptureLlm();
            CH4.ChatSession session = CreateSession("cat2", BuildToolFace(), llm);
            session.PostUserMessage("测试");
            PumpUntilIdle(session);
            Assert.False(ContainsTool(llm.LastTools, "majordomo-restart"), "非默认会话应剔除特权工具");
            Assert.True(ContainsTool(llm.LastTools, "Note"), "非默认会话应保留普通工具");
        }

        /// <summary>
        /// 猫 key 路径——非默认显示名 + SetCatKey("majordomo") + SetToolSpecs 重裁剪 → 特权工具可见
        /// （session.new 重裁剪路径：默认猫 displayName 可能与 key 不同）。
        /// </summary>
        [Fact]
        public void CatKeyDefault_AfterSetToolSpecs_KeepsPrivilegedTool()
        {
            CaptureLlm llm = new CaptureLlm();
            CH4.ChatSession session = CreateSession("默认猫", BuildToolFace(), llm);
            session.SetCatKey("majordomo");
            session.SetToolSpecs(BuildToolFace());
            session.PostUserMessage("测试");
            PumpUntilIdle(session);
            Assert.True(ContainsTool(llm.LastTools, "majordomo-restart"), "猫 key = majordomo 应保留特权工具");
        }
    }

}
