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
    /// 待处理消息队列落盘测试（A232）——入队即物化 / 消费即移除 / 启动加载（跨宿主重启保留）。
    /// 落盘面：目录下 pending_&lt;catKey&gt;.json（测试注入临时目录，不污染真实数据根）。
    /// </summary>
    [Collection("GlobalToolState")]
    public class ChatSessionPendingTests
    {
        /// <summary>纯文本 Mock LLM——无工具调用，回复即结束一轮。</summary>
        private sealed class NoopLlm : ILlmRuntime
        {
            /// <summary>
            /// 流式对话——返回固定纯文本 + 完成事件。
            /// </summary>
            /// <param name="messages">消息序列</param>
            /// <param name="tools">工具定义</param>
            /// <param name="userId">用户标识</param>
            /// <param name="ct">取消令牌</param>
            /// <returns>事件流</returns>
            public async IAsyncEnumerable<LlmStreamEvent> ChatStream(LlmMessage[] messages, ToolSpec[] tools, string userId = "", [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
            {
                yield return new LlmStreamEvent(LlmStreamKind.Text, "ok");
                await Task.Yield();
                yield return new LlmStreamEvent(LlmStreamKind.Done, "");
            }
        }

        /// <summary>建立临时落盘目录（每用例独立——防用例间串扰）。</summary>
        /// <returns>目录绝对路径</returns>
        private static string NewStoreDir()
        {
            return Path.Combine(Path.GetTempPath(), "cat4pending_" + Guid.NewGuid().ToString("N"));
        }

        /// <summary>落盘文件路径（与 ChatSession 落盘命名同构——测试读盘用）。</summary>
        /// <param name="dir">落盘目录</param>
        /// <param name="catKey">猫 key</param>
        /// <returns>文件绝对路径</returns>
        private static string PendingPath(string dir, string catKey)
        {
            return Path.Combine(dir, "pending_" + catKey + ".json");
        }

        /// <summary>构造测试会话——临时前文 / 视图文件 + 指定猫 key。</summary>
        /// <param name="catKey">猫 key</param>
        /// <returns>会话实体</returns>
        private static CH4.ChatSession CreateSession(string catKey)
        {
            CH4.ToolRegistry.Init(new ToolSpec[0], null, new Dictionary<string, bool>());
            ChatContext ctx = new ChatContext();
            string tmp = Path.Combine(Path.GetTempPath(), "cat4pending_" + Guid.NewGuid().ToString("N") + ".jsonl");
            SessionStore store = new SessionStore(tmp);
            OA oa = new OA(new ThreadGuard());
            CH4.SessionViewStore viewStore = new CH4.SessionViewStore(Path.Combine(Path.GetTempPath(), "cat4pending_" + Guid.NewGuid().ToString("N") + ".view.json"));
            CH4.ChatSession session = new CH4.ChatSession("sess", "test", ctx, store, new NoopLlm(), oa, new ToolSpec[0], delegate (string name, string args) { return "ERR|NO_TOOL|" + name; }, viewStore);
            session.SetCatKey(catKey);
            return session;
        }

        /// <summary>泵会话直到 Idle——上限 500 帧（防死循环）。</summary>
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

        /// <summary>入队即物化——PostUserMessage 受理后落盘文件出现且含内容。</summary>
        [Fact]
        public void PostUserMessage_PersistsToDisk()
        {
            string dir = NewStoreDir();
            CH4.ChatSession.ConfigurePendingStore(dir);
            string catKey = "pendtest_a";
            CH4.ChatSession session = CreateSession(catKey);
            bool accepted = session.PostUserMessage("持久化探针");
            Assert.True(accepted);
            string path = PendingPath(dir, catKey);
            Assert.True(File.Exists(path), "受理后应出现落盘文件");
            string json = File.ReadAllText(path);
            Assert.Contains("持久化探针", json);
            Directory.Delete(dir, true);
        }

        /// <summary>消费即移除——一轮跑完后队列清空，落盘面同步为空。</summary>
        [Fact]
        public void Consumed_PersistsEmpty()
        {
            string dir = NewStoreDir();
            CH4.ChatSession.ConfigurePendingStore(dir);
            string catKey = "pendtest_b";
            CH4.ChatSession session = CreateSession(catKey);
            session.PostUserMessage("消费探针");
            PumpUntilIdle(session);
            string json = File.ReadAllText(PendingPath(dir, catKey));
            Assert.DoesNotContain("消费探针", json);
            Directory.Delete(dir, true);
        }

        /// <summary>启动加载——同 catKey 新会话恢复落盘队列（跨宿主重启保留）。</summary>
        [Fact]
        public void Restart_LoadsFromDisk()
        {
            string dir = NewStoreDir();
            CH4.ChatSession.ConfigurePendingStore(dir);
            string catKey = "pendtest_c";
            CH4.ChatSession first = CreateSession(catKey);
            first.PostUserMessage("重启后应保留");
            Assert.Contains("重启后应保留", first.BuildPendingFragment());
            CH4.ChatSession second = CreateSession(catKey);
            Assert.Contains("重启后应保留", second.BuildPendingFragment());
            Directory.Delete(dir, true);
        }

        /// <summary>落盘关闭（空目录）——零落盘、零异常（仅内存兜底）。</summary>
        [Fact]
        public void NoStore_WorksInMemory()
        {
            CH4.ChatSession.ConfigurePendingStore("");
            CH4.ChatSession session = CreateSession("pendtest_d");
            bool accepted = session.PostUserMessage("仅内存");
            Assert.True(accepted);
            Assert.Contains("仅内存", session.BuildPendingFragment());
        }
    }
}
