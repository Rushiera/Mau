using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using Mau.Runtime;
using Xunit;

namespace CatHome4.Core.Tests
{
    /// <summary>
    /// FullContextStore 测试（design-ch4-fullctx）——完整前文留档：格式契约 / 尾部锚点增量 / 非前缀重写 /
    /// 份数轮转 / 剥离器往返 / 视图 JSON 与失败可见。
    /// 期望文本在测试内独立拼装（复述格式契约——不调用被测私有构建器，契约漂移即红）。
    /// </summary>
    public sealed class FullContextStoreTests : IDisposable
    {
        /// <summary>临时目录——Guid 防并发撞车</summary>
        private readonly string _dir;

        /// <summary>消息序列——被测采集源（测试内可变，模拟前文增长 / 回卷）</summary>
        private readonly List<LlmMessage> _msgs = new List<LlmMessage>();

        /// <summary>被测存储（份数 3）</summary>
        private readonly CH4.FullContextStore _store;

        /// <summary>建立夹具——临时目录 + 份数 3 的被测实例</summary>
        public FullContextStoreTests()
        {
            _dir = Path.Combine(Path.GetTempPath(), "cat4full_" + Guid.NewGuid().ToString("N"));
            _store = new CH4.FullContextStore(_dir, "cat_a", () => _msgs.ToArray(), () => 3);
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
                // 测试夹具清理——尽力删除（目录被占用等不影响断言结论）
                Console.WriteLine("[测试清理] 完整前文临时目录删除失败: " + ex.Message);
            }
        }

        // ── 辅助构造 ────────────────────────────────────────────────

        /// <summary>构造消息</summary>
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

        /// <summary>构造用户消息</summary>
        private static LlmMessage User(string text, long ts)
        {
            return Msg(LlmRole.User, text, "", "", "", "", "", ts);
        }

        /// <summary>构造字段块文本（复述格式契约）</summary>
        private static string Field(string name, string body)
        {
            return "[[" + name + " " + body.Length.ToString() + "]]\n" + body + "\n[[/" + name + "]]\n";
        }

        /// <summary>构造消息段文本（复述格式契约）</summary>
        private static string Section(string role, long time, string toolCallId, string tool, string content, string toolCalls, string reasoning, string images)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("[[CH4-CTX role=").Append(role).Append(" time=").Append(time.ToString())
                .Append(" toolcallid=").Append(toolCallId).Append(" tool=").Append(tool).Append("]]\n");
            if (content.Length > 0)
            {
                sb.Append(Field("content", content));
            }
            if (toolCalls.Length > 0)
            {
                sb.Append(Field("tool_calls", toolCalls));
            }
            if (reasoning.Length > 0)
            {
                sb.Append(Field("reasoning", reasoning));
            }
            if (images.Length > 0)
            {
                sb.Append(Field("images", images));
            }
            sb.Append("[[/CH4-CTX]]\n");
            return sb.ToString();
        }

        /// <summary>当前会话文件——目录内唯一（多份时断言失败）</summary>
        private string CurrentFile()
        {
            string[] files = Directory.GetFiles(_dir, "cat_a_*.txt");
            Assert.Single(files);
            return files[0];
        }

        // ── 格式契约与落盘 ──────────────────────────────────────────

        /// <summary>采集——文件头两行 + 逐消息段（四属性恒全写；空字段整块不出现）</summary>
        [Fact]
        public void Capture_WritesHeaderAndSections()
        {
            _msgs.Add(User("你好", 2000L));
            _store.Capture(true);
            string text = File.ReadAllText(CurrentFile());
            Assert.StartsWith("# CH4-FULLCTX v1\n# cat=cat_a session=", text);
            Assert.Contains(" start=", text);
            int idx = text.IndexOf("[[CH4-CTX", StringComparison.Ordinal);
            Assert.True(idx > 0);
            Assert.Equal(Section("user", 2000L, "", "", "你好", "", "", ""), text.Substring(idx));
        }

        /// <summary>采集——消息未入前文（空序列）不落盘</summary>
        [Fact]
        public void Capture_EmptyMessages_NoFile()
        {
            _store.Capture(true);
            Assert.False(Directory.Exists(_dir));
        }

        /// <summary>采集——全字段消息四字段块齐备（正文 / 工具调用 / 思考 / 图片）</summary>
        [Fact]
        public void Capture_AllFields_WrittenInOrder()
        {
            _msgs.Add(Msg(LlmRole.Assistant, "正文", "call_1", "Note", "{\"id\":\"call_1\"}", "思考", "[\"D:/a.png\"]", 3000L));
            _store.Capture(true);
            string text = File.ReadAllText(CurrentFile());
            int idx = text.IndexOf("[[CH4-CTX", StringComparison.Ordinal);
            Assert.Equal(Section("assistant", 3000L, "call_1", "Note", "正文", "{\"id\":\"call_1\"}", "思考", "[\"D:/a.png\"]"), text.Substring(idx));
        }

        // ── 增量（尾部 400 字符锚点） ───────────────────────────────

        /// <summary>增量——第二轮采集只追加新增消息段（前缀逐字符不变）</summary>
        [Fact]
        public void Capture_SecondRound_AppendsOnly()
        {
            _msgs.Add(User(new string('甲', 600), 1000L));
            _store.Capture(true);
            string path = CurrentFile();
            string first = File.ReadAllText(path);
            Assert.True(first.Length > 400);
            _msgs.Add(User("第二条", 2000L));
            _store.Capture(true);
            Assert.Equal(first + Section("user", 2000L, "", "", "第二条", "", "", ""), File.ReadAllText(path));
        }

        /// <summary>增量——追加后剥离器仍可还原全部消息（结构自洽）</summary>
        [Fact]
        public void Capture_AfterAppend_ParsesAllMessages()
        {
            _msgs.Add(User(new string('乙', 500), 1000L));
            _store.Capture(true);
            _msgs.Add(User("第三条", 2000L));
            _store.Capture(true);
            List<CH4.FullContextEntry> entries;
            string error;
            Assert.True(CH4.FullContextStore.TryParse(File.ReadAllText(CurrentFile()), out entries, out error), error);
            Assert.Equal(2, entries.Count);
            Assert.Equal("第三条", entries[1].Content);
        }

        /// <summary>非前缀（回卷 / 结构修复改写既有文本）——全量重写，不留旧内容</summary>
        [Fact]
        public void Capture_NonPrefix_RewritesWhole()
        {
            _msgs.Add(User(new string('丙', 500), 1000L));
            _msgs.Add(User("将被回收", 2000L));
            _store.Capture(true);
            string path = CurrentFile();
            Assert.Contains("将被回收", File.ReadAllText(path));
            _msgs.RemoveAt(1);
            _store.Capture(true);
            string text = File.ReadAllText(path);
            Assert.DoesNotContain("将被回收", text);
            List<CH4.FullContextEntry> entries;
            string error;
            Assert.True(CH4.FullContextStore.TryParse(text, out entries, out error), error);
            Assert.Single(entries);
        }

        /// <summary>节流——窗口内触发不采集（force 忽略窗口）</summary>
        [Fact]
        public void Capture_ThrottledWithinWindow()
        {
            _msgs.Add(User("首条", 1000L));
            _store.Capture(true);
            string path = CurrentFile();
            string first = File.ReadAllText(path);
            _msgs.Add(User("窗口内", 2000L));
            _store.Capture(false);
            Assert.Equal(first, File.ReadAllText(path));
            _store.Capture(true);
            Assert.Contains("窗口内", File.ReadAllText(path));
        }

        // ── 份数轮转 ────────────────────────────────────────────────

        /// <summary>定稿轮转——保留 chat.full_ctx_keep 份，最旧份销毁（当前份转历史）</summary>
        [Fact]
        public void FinalizeSession_RotatesOldestOut()
        {
            CH4.FullContextStore store = new CH4.FullContextStore(_dir, "cat_a", () => _msgs.ToArray(), () => 2);
            _msgs.Add(User("第一会话", 1000L));
            store.Capture(true);
            string first = CurrentFile();
            store.FinalizeSession();
            Assert.True(File.Exists(first));
            _msgs.Clear();
            _msgs.Add(User("第二会话", 2000L));
            store.Capture(true);
            store.FinalizeSession();
            _msgs.Clear();
            _msgs.Add(User("第三会话", 3000L));
            store.Capture(true);
            store.FinalizeSession();
            string[] files = Directory.GetFiles(_dir, "cat_a_*.txt");
            Assert.Equal(2, files.Length);
            Assert.False(File.Exists(first));
            bool hasThird = false;
            for (int i = 0; i < files.Length; i = i + 1)
            {
                if (File.ReadAllText(files[i]).Contains("第三会话"))
                {
                    hasThird = true;
                }
            }
            Assert.True(hasThird);
        }

        /// <summary>定稿——无落盘会话（从未采集）不报错、不产文件</summary>
        [Fact]
        public void FinalizeSession_NoCapture_NoFile()
        {
            Assert.Equal("", _store.FinalizeSession());
            Assert.False(Directory.Exists(_dir));
        }

        // ── 剥离器 ──────────────────────────────────────────────────

        /// <summary>剥离器——落盘文本往返还原四字段（重建前文入口）</summary>
        [Fact]
        public void TryParse_RoundTripAllFields()
        {
            _msgs.Add(Msg(LlmRole.Tool, "工具结果", "call_9", "text-read", "", "", "", 4000L));
            _msgs.Add(Msg(LlmRole.User, "带图", "", "", "", "", "[\"D:/b.png\"]", 5000L));
            _store.Capture(true);
            List<CH4.FullContextEntry> entries;
            string error;
            Assert.True(CH4.FullContextStore.TryParse(File.ReadAllText(CurrentFile()), out entries, out error), error);
            Assert.Equal(2, entries.Count);
            Assert.Equal("tool", entries[0].Role);
            Assert.Equal("call_9", entries[0].ToolCallId);
            Assert.Equal("text-read", entries[0].ToolName);
            Assert.Equal("工具结果", entries[0].Content);
            Assert.Equal(4000L, entries[0].Time);
            Assert.Equal("[\"D:/b.png\"]", entries[1].Images);
        }

        /// <summary>剥离器——正文含换行与标记文本仍按声明长度精确切片（不靠分隔符猜测）</summary>
        [Fact]
        public void TryParse_BodyWithNewlinesAndMarkers()
        {
            string body = "第一行\n[[content 3]]\n第二行\n[[/CH4-CTX]]\n末行";
            _msgs.Add(User(body, 1000L));
            _store.Capture(true);
            List<CH4.FullContextEntry> entries;
            string error;
            Assert.True(CH4.FullContextStore.TryParse(File.ReadAllText(CurrentFile()), out entries, out error), error);
            Assert.Single(entries);
            Assert.Equal(body, entries[0].Content);
        }

        /// <summary>剥离器——字符数声明越界即报错（不静默降级）</summary>
        [Fact]
        public void TryParse_LengthOverflow_FailsLoud()
        {
            string text = "[[CH4-CTX role=user time=1 toolcallid= tool=]]\n[[content 99]]\n短\n[[/content]]\n[[/CH4-CTX]]\n";
            List<CH4.FullContextEntry> entries;
            string error;
            Assert.False(CH4.FullContextStore.TryParse(text, out entries, out error));
            Assert.Contains("越界", error);
        }

        /// <summary>剥离器——未知字段名即报错（契约外内容不静默吞掉）</summary>
        [Fact]
        public void TryParse_UnknownField_FailsLoud()
        {
            string text = "[[CH4-CTX role=user time=1 toolcallid= tool=]]\n[[content 1]]\n甲\n[[/content]]\n[[extra 1]]\n乙\n[[/extra]]\n[[/CH4-CTX]]\n";
            List<CH4.FullContextEntry> entries;
            string error;
            Assert.False(CH4.FullContextStore.TryParse(text, out entries, out error));
            Assert.Contains("未知字段", error);
        }

        // ── 视图 JSON ───────────────────────────────────────────────

        /// <summary>视图——空态 ok=true 零条目（前端空态文案自持，不静默留白）</summary>
        [Fact]
        public void BuildView_Empty_OkZeroItems()
        {
            using (JsonDocument doc = JsonDocument.Parse(_store.BuildView(200)))
            {
                Assert.True(doc.RootElement.GetProperty("ok").GetBoolean());
                Assert.Equal(0, doc.RootElement.GetProperty("count").GetInt32());
                Assert.Equal(0, doc.RootElement.GetProperty("items").GetArrayLength());
            }
        }

        /// <summary>视图——条目数与消息数一致；max 取尾部窗口且恒含首条（2026-10-02 判例）；空正文回落工具调用正文</summary>
        [Fact]
        public void BuildView_ItemsAndTailWindow()
        {
            _msgs.Add(User("第一条", 1000L));
            _msgs.Add(Msg(LlmRole.Assistant, "", "call_2", "Note", "{\"id\":\"call_2\"}", "", "", 2000L));
            _msgs.Add(User("第三条", 3000L));
            using (JsonDocument doc = JsonDocument.Parse(_store.BuildView(2)))
            {
                JsonElement root = doc.RootElement;
                Assert.True(root.GetProperty("ok").GetBoolean());
                Assert.Equal(3, root.GetProperty("count").GetInt32());
                // 窗口形态——首条恒在窗口内 + 尾部 max-1 条
                Assert.Equal(3, root.GetProperty("shown").GetInt32());
                Assert.Equal(2, root.GetProperty("start").GetInt32());
                Assert.Equal(1, root.GetProperty("items")[0].GetProperty("i").GetInt32());
                Assert.Equal("第一条", root.GetProperty("items")[0].GetProperty("content").GetString());
                Assert.Equal(2, root.GetProperty("items")[1].GetProperty("i").GetInt32());
                Assert.Equal("assistant", root.GetProperty("items")[1].GetProperty("role").GetString());
                Assert.Equal("{\"id\":\"call_2\"}", root.GetProperty("items")[1].GetProperty("content").GetString());
                Assert.Equal("Note", root.GetProperty("items")[1].GetProperty("tool").GetString());
                Assert.Equal(3, root.GetProperty("items")[2].GetProperty("i").GetInt32());
            }
        }
        /// <summary>视图窗口——条目超上限时恒含首条（system 段不因尾部窗口而不可见；判例 2026-10-02）</summary>
        [Fact]
        public void BuildView_OverWindow_KeepsHead()
        {
            _msgs.Add(Msg(LlmRole.System, new string('注', 500), "", "", "", "", "", 1000L));
            _msgs.Add(User("第一条", 2000L));
            _msgs.Add(User("第二条", 3000L));
            _store.Capture(true);
            string json = _store.BuildView(1);
            using (JsonDocument doc = JsonDocument.Parse(json))
            {
                JsonElement root = doc.RootElement;
                Assert.Equal(3, root.GetProperty("count").GetInt32());
                JsonElement items = root.GetProperty("items");
                Assert.Equal(2, items.GetArrayLength());
                Assert.Equal(1, items[0].GetProperty("i").GetInt32());
                Assert.Equal("system", items[0].GetProperty("role").GetString());
                Assert.Equal(3, items[1].GetProperty("i").GetInt32());
            }
        }

        /// <summary>载荷正文合并——四字段按序拼接、空字段跳过（单点实现：前文条目与完整前文条目共用同一口径）</summary>
        [Fact]
        public void MergeBody_FourFields_SkipsEmpty()
        {
            Assert.Equal("a\nb\nc\nd", CH4.FullContextStore.MergeBody("a", "b", "c", "d"));
            Assert.Equal("a\nc", CH4.FullContextStore.MergeBody("a", "", "c", ""));
            Assert.Equal("b", CH4.FullContextStore.MergeBody("", "b", "", ""));
            Assert.Equal("", CH4.FullContextStore.MergeBody("", "", "", ""));
            Assert.Equal("a", CH4.FullContextStore.MergeBody("a", null, null, null));
        }

        /// <summary>视图——单条正文四字段合并（content / tool_calls / reasoning / images 按序，空字段跳过）；
        /// 旧行为只取首个非空字段，assistant 的 tool_calls 被 content 吞掉，与前文条目口径不一致（本次修复点）</summary>
        [Fact]
        public void BuildView_BodyMergesAllFields()
        {
            _msgs.Add(Msg(LlmRole.Assistant, "正文", "", "", "{\"id\":\"c1\"}", "思考", "", 1000L));
            _msgs.Add(Msg(LlmRole.User, "图片消息", "", "", "", "", "[\"D:/a.png\"]", 2000L));
            using (JsonDocument doc = JsonDocument.Parse(_store.BuildView(200)))
            {
                JsonElement items = doc.RootElement.GetProperty("items");
                Assert.Equal("正文\n{\"id\":\"c1\"}\n思考", items[0].GetProperty("content").GetString());
                Assert.Equal("图片消息\n[\"D:/a.png\"]", items[1].GetProperty("content").GetString());
            }
        }
    }
}
