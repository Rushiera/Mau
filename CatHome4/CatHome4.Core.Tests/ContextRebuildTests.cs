using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Mau.Runtime;
using Xunit;

namespace CatHome4.Core.Tests
{
    /// <summary>
    /// 前文自动重建测试（design-ch4-ctx-rebuild）——巡检判据 / 重建还原与尾部续接 / 留档头部守卫 / 失败可见。
    /// 留档文本在测试内独立拼装（复述格式契约——不调用被测私有构建器，契约漂移即红）。
    /// </summary>
    public sealed class ContextRebuildTests : IDisposable
    {
        /// <summary>临时目录——Guid 防并发撞车</summary>
        private readonly string _dir;

        /// <summary>消息序列——被测采集源（测试内可变，模拟前文丢头与增长）</summary>
        private readonly List<LlmMessage> _msgs = new List<LlmMessage>();

        /// <summary>被测存储（份数 3）</summary>
        private readonly CH4.FullContextStore _store;

        /// <summary>建立夹具——临时目录 + 被测实例</summary>
        public ContextRebuildTests()
        {
            _dir = Path.Combine(Path.GetTempPath(), "cat4rebuild_" + Guid.NewGuid().ToString("N"));
            _store = new CH4.FullContextStore(_dir, "cat_r", () => _msgs.ToArray(), () => 3);
        }

        /// <summary>释放夹具——尽力删除临时目录（清理失败不影响断言结论）</summary>
        public void Dispose()
        {
            try
            {
                Directory.Delete(_dir, true);
            }
            catch (Exception ex)
            {
                Console.WriteLine("[测试清理] 前文重建临时目录删除失败: " + ex.Message);
            }
        }

        // ── 辅助构造 ────────────────────────────────────────────────

        /// <summary>构造消息（全字段）</summary>
        private static LlmMessage Msg(LlmRole role, string content, string toolCallId, string toolName, string toolCalls, string reasoning, string images, long ts)
        {
            LlmMessage m = new LlmMessage();
            m.Role = role;
            m.Content = content;
            m.ToolCallId = toolCallId;
            m.ToolName = toolName;
            m.ToolCallsJson = toolCalls;
            m.ReasoningContent = reasoning;
            m.ImagesJson = images;
            m.CreatedAt = ts;
            return m;
        }

        /// <summary>构造系统消息（长正文——覆盖 400 字符判据）</summary>
        private static LlmMessage Sys(string text, long ts)
        {
            return Msg(LlmRole.System, text, "", "", "", "", "", ts);
        }

        /// <summary>构造用户消息</summary>
        private static LlmMessage User(string text, long ts)
        {
            return Msg(LlmRole.User, text, "", "", "", "", "", ts);
        }

        /// <summary>当前会话文件——目录内唯一（多份时断言失败）</summary>
        private string CurrentFile()
        {
            string[] files = Directory.GetFiles(_dir, "cat_r_*.txt");
            Assert.Single(files);
            return files[0];
        }

        // ── 巡检判据 ────────────────────────────────────────────────

        /// <summary>判据——头部一致：不触发重建</summary>
        [Fact]
        public void HeadMatches_SameHead_True()
        {
            _msgs.Add(Sys(new string('注', 500), 1000L));
            _msgs.Add(User("你好", 2000L));
            _store.Capture(true);
            Assert.True(_store.HeadMatches(_msgs.ToArray()));
        }

        /// <summary>判据——首段丢失（system 段缺失）：触发重建</summary>
        [Fact]
        public void HeadMatches_HeadLost_False()
        {
            _msgs.Add(Sys(new string('注', 500), 1000L));
            _msgs.Add(User("你好", 2000L));
            _store.Capture(true);
            _msgs.RemoveAt(0);
            Assert.False(_store.HeadMatches(_msgs.ToArray()));
        }

        /// <summary>判据——无留档 = 无判据（不动作，不误报）</summary>
        [Fact]
        public void HeadMatches_NoArchive_True()
        {
            _msgs.Add(User("你好", 1000L));
            Assert.True(_store.HeadMatches(_msgs.ToArray()));
        }

        // ── 重建还原 ────────────────────────────────────────────────

        /// <summary>重建——还原丢头段 + 留档末条之后的前文消息续接（节流窗口内新消息不丢）</summary>
        [Fact]
        public void TryRebuild_RestoresHeadAndKeepsTail()
        {
            _msgs.Add(Sys(new string('注', 500), 1000L));
            _msgs.Add(User("第一条", 2000L));
            _store.Capture(true);
            // 丢头 + 新消息（尚未入留档）
            _msgs.RemoveAt(0);
            _msgs.Add(User("第二条", 3000L));
            LlmMessage[] rebuilt;
            string error;
            Assert.True(_store.TryRebuild(_msgs.ToArray(), out rebuilt, out error), error);
            Assert.Equal(3, rebuilt.Length);
            Assert.Equal(LlmRole.System, rebuilt[0].Role);
            Assert.Equal("第一条", rebuilt[1].Content);
            Assert.Equal("第二条", rebuilt[2].Content);
            Assert.Equal(3000L, rebuilt[2].CreatedAt);
        }

        /// <summary>重建——留档末条与现前文无交集（两端不同源）：只还原留档部分，不续接</summary>
        [Fact]
        public void TryRebuild_TailNotMatched_ArchiveOnly()
        {
            _msgs.Add(Sys(new string('注', 500), 1000L));
            _msgs.Add(User("第一条", 2000L));
            _store.Capture(true);
            List<LlmMessage> live = new List<LlmMessage>();
            live.Add(User("别的会话消息", 5000L));
            LlmMessage[] rebuilt;
            string error;
            Assert.True(_store.TryRebuild(live.ToArray(), out rebuilt, out error), error);
            Assert.Equal(2, rebuilt.Length);
            Assert.Equal(LlmRole.System, rebuilt[0].Role);
            Assert.Equal("第一条", rebuilt[1].Content);
        }

        /// <summary>重建——四字段 / 配对 ID / 时刻全字段还原</summary>
        [Fact]
        public void TryRebuild_AllFields_RoundTrip()
        {
            _msgs.Add(Sys(new string('注', 500), 1000L));
            _msgs.Add(Msg(LlmRole.Assistant, "正文", "call_1", "Note", "[{\"id\":\"call_1\"}]", "思考", "", 2000L));
            _msgs.Add(Msg(LlmRole.Tool, "结果", "call_1", "Note", "", "", "", 3000L));
            _msgs.Add(Msg(LlmRole.User, "", "", "", "", "", "[\"D:/a.png\"]", 4000L));
            _store.Capture(true);
            LlmMessage[] rebuilt;
            string error;
            Assert.True(_store.TryRebuild(new LlmMessage[0], out rebuilt, out error), error);
            Assert.Equal(4, rebuilt.Length);
            Assert.Equal(LlmRole.Assistant, rebuilt[1].Role);
            Assert.Equal("call_1", rebuilt[1].ToolCallId);
            Assert.Equal("Note", rebuilt[1].ToolName);
            Assert.Equal("[{\"id\":\"call_1\"}]", rebuilt[1].ToolCallsJson);
            Assert.Equal("思考", rebuilt[1].ReasoningContent);
            Assert.Equal("结果", rebuilt[2].Content);
            Assert.Equal("[\"D:/a.png\"]", rebuilt[3].ImagesJson);
            Assert.Equal(4000L, rebuilt[3].CreatedAt);
        }

        /// <summary>重建——留档对齐后判据回到一致（重建 + 采集后头部重新匹配）</summary>
        [Fact]
        public void HeadMatches_AfterRebuildAligned()
        {
            _msgs.Add(Sys(new string('注', 500), 1000L));
            _msgs.Add(User("第一条", 2000L));
            _store.Capture(true);
            _msgs.RemoveAt(0);
            Assert.False(_store.HeadMatches(_msgs.ToArray()));
            LlmMessage[] rebuilt;
            string error;
            Assert.True(_store.TryRebuild(_msgs.ToArray(), out rebuilt, out error), error);
            // 模拟会话侧动作——以重建结果替换前文 + 留档对齐采集
            _msgs.Clear();
            for (int i = 0; i < rebuilt.Length; i = i + 1)
            {
                _msgs.Add(rebuilt[i]);
            }
            _store.Capture(true);
            Assert.True(_store.HeadMatches(_msgs.ToArray()));
        }

        // ── 失败可见 ────────────────────────────────────────────────

        /// <summary>重建——无留档即失败出声（不静默降级）</summary>
        [Fact]
        public void TryRebuild_NoArchive_FailsLoud()
        {
            LlmMessage[] rebuilt;
            string error;
            Assert.False(_store.TryRebuild(_msgs.ToArray(), out rebuilt, out error));
            Assert.Equal("本会话无完整前文留档", error);
            Assert.Null(rebuilt);
        }

        /// <summary>重建——留档角色非法即失败出声</summary>
        [Fact]
        public void TryRebuild_UnknownRole_FailsLoud()
        {
            _msgs.Add(Sys(new string('注', 500), 1000L));
            _store.Capture(true);
            string path = CurrentFile();
            File.WriteAllText(path, "# CH4-FULLCTX v1\n[[CH4-CTX role=robot time=1 toolcallid= tool=]]\n[[content 2]]\nhi\n[[/content]]\n[[/CH4-CTX]]\n", new UTF8Encoding(true));
            LlmMessage[] rebuilt;
            string error;
            Assert.False(_store.TryRebuild(_msgs.ToArray(), out rebuilt, out error));
            Assert.StartsWith("留档角色非法", error, StringComparison.Ordinal);
        }

        // ── 留档守卫 ────────────────────────────────────────────────

        /// <summary>守卫——前文丢头期间采集不得覆写留档（留档是重建唯一源）</summary>
        [Fact]
        public void Capture_HeadDrift_RefusesRewrite()
        {
            _msgs.Add(Sys(new string('注', 500), 1000L));
            _msgs.Add(User("第一条", 2000L));
            _store.Capture(true);
            string path = CurrentFile();
            string before = File.ReadAllText(path);
            _msgs.RemoveAt(0);
            _store.Capture(true);
            Assert.Equal(before, File.ReadAllText(path));
            Assert.False(_store.HeadMatches(_msgs.ToArray()));
        }
    }
}
