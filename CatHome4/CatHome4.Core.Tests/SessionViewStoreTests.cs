using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Mau.Runtime;
using Xunit;

namespace CatHome4.Core.Tests
{
    /// <summary>
    /// SessionViewStore 测试（A13 视图层补测）——前文钩子 / 工具配对 / 重建 / 归并 / 辅助块 / 落盘往返。
    /// 覆盖：块生成与索引携带 · tool_calls 配对与孤立丢弃 · 并发序号与名称回退 · Rebuild 重置且保留非前文派生块 ·
    /// 时间戳归并排序 · 注入报告合成首块 · roundsum/gap/error/retry 四类辅助块 · Clear 与 ClearRoundSums 语义 ·
    /// Save/Load 往返 · 内容哈希稳定性与敏感性 · 块 ID 形态。
    /// </summary>
    public sealed class SessionViewStoreTests : IDisposable
    {
        /// <summary>临时目录——Guid 防并发撞车</summary>
        private readonly string _dir;

        /// <summary>视图文件路径</summary>
        private readonly string _path;

        /// <summary>被测视图存储</summary>
        private readonly CH4.SessionViewStore _store;

        /// <summary>建立夹具——临时目录 + 视图文件路径 + 被测实例</summary>
        public SessionViewStoreTests()
        {
            _dir = Path.Combine(Path.GetTempPath(), "cat4view_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
            _path = Path.Combine(_dir, "view.json");
            _store = new CH4.SessionViewStore(_path);
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
                Console.WriteLine("[测试清理] 视图临时目录删除失败: " + ex.Message);
            }
        }

        // ── 辅助构造 ────────────────────────────────────────────────

        /// <summary>
        /// 重放辅助——按消息序调用视图钩子（A156：事件直落块取代从前文重建；测试内构造前文派生块用）。
        /// </summary>
        /// <param name="store">目标视图存储</param>
        /// <param name="messages">真实前文消息数组</param>
        private static void ReplayAll(CH4.SessionViewStore store, LlmMessage[] messages)
        {
            for (int i = 0; i < messages.Length; i++)
            {
                LlmMessage m = messages[i];
                if (m.Role == LlmRole.User)
                {
                    store.OnUserMessage(m, i);
                }
                else if (m.Role == LlmRole.Assistant)
                {
                    if ((m.ToolCallsJson ?? "").Length > 0)
                    {
                        store.OnAssistantToolCalls(m, i);
                    }
                    else
                    {
                        store.OnAssistantText(m, i);
                    }
                }
                else if (m.Role == LlmRole.Tool)
                {
                    store.OnToolResult(m, i, -1);
                }
            }
        }

        /// <summary>构造用户消息</summary>
        private static LlmMessage User(string text, long ts)
        {
            LlmMessage m = new LlmMessage();
            m.Role = LlmRole.User;
            m.Content = text;
            m.CreatedAt = ts;
            return m;
        }

        /// <summary>构造 assistant 纯文本消息</summary>
        private static LlmMessage Assistant(string text, long ts)
        {
            LlmMessage m = new LlmMessage();
            m.Role = LlmRole.Assistant;
            m.Content = text;
            m.CreatedAt = ts;
            return m;
        }

        /// <summary>构造 assistant 工具调用消息</summary>
        private static LlmMessage AssistantWithTools(string reasoning, string toolCallsJson, long ts)
        {
            LlmMessage m = new LlmMessage();
            m.Role = LlmRole.Assistant;
            m.ReasoningContent = reasoning;
            m.ToolCallsJson = toolCallsJson;
            m.CreatedAt = ts;
            return m;
        }

        /// <summary>构造工具结果消息</summary>
        private static LlmMessage ToolResult(string callId, string toolName, string content, long ts)
        {
            LlmMessage m = new LlmMessage();
            m.Role = LlmRole.Tool;
            m.ToolCallId = callId;
            m.ToolName = toolName;
            m.Content = content;
            m.CreatedAt = ts;
            return m;
        }

        /// <summary>构造 system 消息</summary>
        private static LlmMessage SystemMsg(string text, long ts)
        {
            LlmMessage m = new LlmMessage();
            m.Role = LlmRole.System;
            m.Content = text;
            m.CreatedAt = ts;
            return m;
        }

        /// <summary>构造 tool_calls JSON——单条（参数文本转义进 JSON 字符串：C# 值 → JSON 文本 → 解析后值）</summary>
        private static string OneToolCall(string id, string name, string args)
        {
            string escaped = args.Replace("\\", "\\\\").Replace("\"", "\\\"");
            return "[{\"id\":\"" + id + "\",\"type\":\"function\",\"function\":{\"name\":\"" + name + "\",\"arguments\":\"" + escaped + "\"}}]";
        }

        /// <summary>构造 tool_calls JSON——两条并发</summary>
        private static string TwoToolCalls(string id1, string name1, string id2, string name2)
        {
            return "[{\"id\":\"" + id1 + "\",\"type\":\"function\",\"function\":{\"name\":\"" + name1 + "\",\"arguments\":\"{}\"}},"
                + "{\"id\":\"" + id2 + "\",\"type\":\"function\",\"function\":{\"name\":\"" + name2 + "\",\"arguments\":\"{}\"}}]";
        }

        /// <summary>解析块载荷 JSON（Clone 脱离 JsonDocument 生命周期）</summary>
        private static JsonElement ParsePayload(CH4.ViewBlock block)
        {
            JsonDocument doc = JsonDocument.Parse(block.Payload);
            return doc.RootElement.Clone();
        }

        // ── 空态与钩子 ──────────────────────────────────────────────

        /// <summary>空存储——无块无报告</summary>
        [Fact]
        public void EmptyStore_NoBlocks()
        {
            Assert.Empty(_store.GetBlocks());
            Assert.Equal("", _store.GetInjectReport());
        }

        /// <summary>用户消息钩子——user 块：渲染类型 / 索引 / 时间戳 / 载荷内容</summary>
        [Fact]
        public void OnUserMessage_AppendsUserBlock_CarriesIndexAndPayload()
        {
            _store.OnUserMessage(User("你好", 100L), 3);
            CH4.ViewBlock[] blocks = _store.GetBlocks();
            Assert.Single(blocks);
            Assert.Equal("user", blocks[0].RenderType);
            Assert.Equal(3, blocks[0].Origin.MsgIndex);
            Assert.Equal(100L, blocks[0].Timestamp);
            Assert.Equal("你好", ParsePayload(blocks[0]).GetProperty("content").GetString());
        }

        /// <summary>assistant 纯文本钩子——text 块</summary>
        [Fact]
        public void OnAssistantText_AppendsTextBlock()
        {
            _store.OnAssistantText(Assistant("回复", 200L), 4);
            CH4.ViewBlock[] blocks = _store.GetBlocks();
            Assert.Single(blocks);
            Assert.Equal("text", blocks[0].RenderType);
            Assert.Equal("回复", ParsePayload(blocks[0]).GetProperty("content").GetString());
        }

        /// <summary>工具调用钩子（有思考）——reason 块（思考只为工具轮投影）</summary>
        [Fact]
        public void OnAssistantToolCalls_WithReasoning_AppendsReasonBlock()
        {
            _store.OnAssistantToolCalls(AssistantWithTools("想想", OneToolCall("call_1", "Note", "{}"), 200L), 4);
            CH4.ViewBlock[] blocks = _store.GetBlocks();
            Assert.Single(blocks);
            Assert.Equal("reason", blocks[0].RenderType);
            Assert.Equal("想想", ParsePayload(blocks[0]).GetProperty("content").GetString());
        }

        /// <summary>工具调用钩子（无思考）——不产 reason 块（视图不留空块）</summary>
        [Fact]
        public void OnAssistantToolCalls_WithoutReasoning_NoBlock()
        {
            _store.OnAssistantToolCalls(AssistantWithTools("", OneToolCall("call_1", "Note", "{}"), 200L), 4);
            Assert.Empty(_store.GetBlocks());
        }

        // ── 工具配对 ────────────────────────────────────────────────

        /// <summary>工具配对——声明与结果按 call_id 配对生成 toolcard（名称 / 参数 / 结果 / 序号；Z8 起载荷不含摘要字段）</summary>
        [Fact]
        public void OnToolResult_PairedByCallId_AppendsToolCard()
        {
            _store.OnAssistantToolCalls(AssistantWithTools("", OneToolCall("call_1", "note.set", "{\"k\":1}"), 200L), 4);
            _store.OnToolResult(ToolResult("call_1", "note.set", "已写入", 300L), 5, -1);
            CH4.ViewBlock[] blocks = _store.GetBlocks();
            Assert.Single(blocks);
            Assert.Equal("toolcard", blocks[0].RenderType);
            Assert.Equal(5, blocks[0].Origin.MsgIndex);
            JsonElement payload = ParsePayload(blocks[0]);
            Assert.Equal("note.set", payload.GetProperty("name").GetString());
            Assert.Equal("{\"k\":1}", payload.GetProperty("arguments").GetString());
            Assert.Equal("已写入", payload.GetProperty("result").GetString());
            Assert.False(payload.TryGetProperty("summary", out _));
            Assert.Equal(1, payload.GetProperty("toolIndex").GetInt32());
            Assert.Equal(1, payload.GetProperty("toolTotal").GetInt32());
        }
        /// <summary>工具卡载荷带执行序注记——视图块字段与实时推送同源取表（重建路径不再丢前端徽标）</summary>
        [Fact]
        public void OnToolResult_CarriesOrderFromTable()
        {
            _store.OnAssistantToolCalls(AssistantWithTools("", OneToolCall("call_1", "cs-patch", "{}"), 200L), 4);
            _store.OnToolResult(ToolResult("call_1", "cs-patch", "结果", 300L), 5, -1);
            CH4.ViewBlock[] blocks = _store.GetBlocks();
            Assert.Single(blocks);
            Assert.Equal(CH4.ToolOrderTable.OrderText("cs-patch"), ParsePayload(blocks[0]).GetProperty("order").GetString());
        }

        /// <summary>孤立工具结果——无声明配对则丢弃（视图容错，不产块）</summary>
        [Fact]
        public void OnToolResult_Orphan_Dropped()
        {
            _store.OnToolResult(ToolResult("call_x", "note.set", "结果", 300L), 5, -1);
            Assert.Empty(_store.GetBlocks());
        }

        /// <summary>并发批——两工具各生成一卡，序号与总数随批携带</summary>
        [Fact]
        public void OnToolResult_ConcurrentBatch_CarriesIndexAndTotal()
        {
            string batch = TwoToolCalls("call_1", "note.set", "call_2", "time");
            _store.OnAssistantToolCalls(AssistantWithTools("", batch, 200L), 4);
            _store.OnToolResult(ToolResult("call_1", "note.set", "甲", 300L), 5, -1);
            _store.OnToolResult(ToolResult("call_2", "time", "乙", 301L), 6, -1);
            CH4.ViewBlock[] blocks = _store.GetBlocks();
            Assert.Equal(2, blocks.Length);
            Assert.Equal(1, ParsePayload(blocks[0]).GetProperty("toolIndex").GetInt32());
            Assert.Equal(2, ParsePayload(blocks[1]).GetProperty("toolIndex").GetInt32());
            Assert.Equal(2, ParsePayload(blocks[0]).GetProperty("toolTotal").GetInt32());
            Assert.Equal(2, ParsePayload(blocks[1]).GetProperty("toolTotal").GetInt32());
        }

        /// <summary>回退链——声明面名称为空时取工具结果消息的 ToolName</summary>
        [Fact]
        public void OnToolResult_EmptyDeclaredName_FallsBackToMessageToolName()
        {
            _store.OnAssistantToolCalls(AssistantWithTools("", OneToolCall("call_1", "", "{}"), 200L), 4);
            _store.OnToolResult(ToolResult("call_1", "fallback.name", "结果", 300L), 5, -1);
            CH4.ViewBlock[] blocks = _store.GetBlocks();
            Assert.Single(blocks);
            Assert.Equal("fallback.name", ParsePayload(blocks[0]).GetProperty("name").GetString());
        }

        /// <summary>坏 tool_calls JSON——登记静默为空（不抛异常；后续结果视作孤立丢弃）</summary>
        [Fact]
        public void OnAssistantToolCalls_BrokenJson_RegistersNothing()
        {
            _store.OnAssistantToolCalls(AssistantWithTools("", "not-json", 200L), 4);
            _store.OnToolResult(ToolResult("call_1", "note.set", "结果", 300L), 5, -1);
            Assert.Empty(_store.GetBlocks());
        }

        // ── 事件直落与载入 ──────────────────────────────────────────

        /// <summary>事件重放——跳过 system，user / assistant / tool 三类按消息序生成块并携带前文来源索引</summary>
        [Fact]
        public void Replay_SkipsSystem_AppendsAllRoles()
        {
            LlmMessage[] messages = new LlmMessage[]
            {
                SystemMsg("系统", 10L),
                User("问题", 100L),
                AssistantWithTools("", OneToolCall("call_1", "note.set", "{}"), 200L),
                ToolResult("call_1", "note.set", "结果", 300L),
                Assistant("回复", 400L)
            };
            ReplayAll(_store, messages);
            CH4.ViewBlock[] blocks = _store.GetBlocks();
            Assert.Equal(3, blocks.Length);
            Assert.Equal("user", blocks[0].RenderType);
            Assert.Equal(1, blocks[0].Origin.MsgIndex);
            Assert.Equal("toolcard", blocks[1].RenderType);
            Assert.Equal(3, blocks[1].Origin.MsgIndex);
            Assert.Equal("text", blocks[2].RenderType);
            Assert.Equal(4, blocks[2].Origin.MsgIndex);
            // A156 块契约——前文派生块：src=front / state=final / 键带前文坐标 / 自哈希 ID 已定稿
            Assert.Equal("front", blocks[0].Src);
            Assert.Equal("final", blocks[0].State);
            Assert.Equal("msg:1:user", blocks[0].Key);
            Assert.Equal(64, blocks[0].Id.Length);
        }

        /// <summary>移出块不随载入复活——timeback 回收后从块序移除 + 落盘，重新载入以文件为准（前文不动：tool_calls 须配对）</summary>
        [Fact]
        public void Load_AfterVoid_MovedBlocksDoNotReturn()
        {
            LlmMessage[] messages = new LlmMessage[]
            {
                        User("问题", 100L),
                        AssistantWithTools("", OneToolCall("call_1", "text-read", "{}"), 200L),
                        ToolResult("call_1", "text-read", "内容", 300L),
                        Assistant("回复", 400L)
            };
            ReplayAll(_store, messages);
            Assert.Equal(3, _store.GetBlocks().Length);
            // 前文不动（tool_calls 须配对）——仅视图层移出（用户消息 + sibling 工具卡）
            int moved = _store.ConvertRangeToVoid(0, 2, "");
            Assert.Equal(2, moved);
            // 废弃段留档——hashes 必须是 JSON 数组（片段值须走 Raw 包裹，否则落成转义字符串）
            CH4.ViewBlock[] afterMove = _store.GetBlocks();
            Assert.Equal(2, afterMove.Length);
            JsonElement voidPayload = ParsePayload(afterMove[0]);
            JsonElement hashesEl = voidPayload.GetProperty("hashes");
            Assert.Equal(JsonValueKind.Array, hashesEl.ValueKind);
            Assert.Equal(2, hashesEl.GetArrayLength());
            // A156：载入以落盘文件为准——已移出块不复活（旧架构靠重建重放 + 哈希过滤，本版不再重建）
            CH4.SessionViewStore reloaded = new CH4.SessionViewStore(_path);
            reloaded.Load();
            CH4.ViewBlock[] blocks = reloaded.GetBlocks();
            Assert.Equal(2, blocks.Length);
            Assert.Equal("void", blocks[0].RenderType);
            Assert.Equal("text", blocks[1].RenderType);
            Assert.Equal("回复", ParsePayload(blocks[1]).GetProperty("content").GetString());
        }

        /// <summary>载入——文件缺失即为空视图（不从前文重建：旧数据不迁移）</summary>
        [Fact]
        public void Load_MissingFile_EmptyView()
        {
            _store.OnUserMessage(User("旧", 100L), 0);
            CH4.SessionViewStore fresh = new CH4.SessionViewStore(Path.Combine(_dir, "absent.view.json"));
            fresh.Load();
            Assert.Empty(fresh.GetBlocks());
            Assert.Single(_store.GetBlocks());
        }

        /// <summary>独立块不受消息重放影响（gap / roundsum / error / retry / 注入报告均为 independent）</summary>
        [Fact]
        public void Replay_KeepsIndependentBlocks()
        {
            _store.AppendGapText("间隙", 110L);
            _store.AppendRoundSummary("{\"type\":\"roundsum\"}", 120L);
            _store.AppendError("ERR|TEST|坏", 130L);
            _store.AppendRetry("{\"state\":\"wait\"}", 140L);
            _store.SetInjectReport("{\"file\":\"a.md\"}");
            ReplayAll(_store, new LlmMessage[] { SystemMsg("系统", 10L) });
            CH4.ViewBlock[] blocks = _store.GetBlocks();
            // 四类独立块 + 合成首块（注入报告）
            Assert.Equal(5, blocks.Length);
            Assert.Equal("inject_report", blocks[0].RenderType);
            Assert.Equal("independent", blocks[1].Src);
        }

        /// <summary>事件重放——块时间戳取消息 CreatedAt（消息块创建时刻）</summary>
        [Fact]
        public void Replay_BlockTimestamp_UsesMessageCreatedAt()
        {
            ReplayAll(_store, new LlmMessage[] { User("问题", 777L) });
            CH4.ViewBlock[] blocks = _store.GetBlocks();
            Assert.Single(blocks);
            Assert.Equal(777L, blocks[0].Timestamp);
        }

        /// <summary>时间戳单调化——同刻多块由写入侧补差，保证块序 = 时间戳序（A156 I5）</summary>
        [Fact]
        public void Replay_SameTimestamp_MonotonicAdjusted()
        {
            _store.OnUserMessage(User("甲", 100L), 0);
            _store.OnUserMessage(User("乙", 100L), 1);
            _store.OnUserMessage(User("丙", 100L), 2);
            CH4.ViewBlock[] blocks = _store.GetBlocks();
            Assert.Equal(100L, blocks[0].Timestamp);
            Assert.Equal(101L, blocks[1].Timestamp);
            Assert.Equal(102L, blocks[2].Timestamp);
        }

        // ── 归并与合成块 ────────────────────────────────────────────

        /// <summary>归并——五类块源按时间戳升序交错排列（同一坐标系）</summary>
        [Fact]
        public void GetBlocks_MergesByTimestampAscending()
        {
            _store.OnUserMessage(User("问题", 100L), 0);
            _store.AppendGapText("间隙", 150L);
            _store.AppendError("ERR|TEST|坏", 200L);
            _store.AppendRoundSummary("{\"type\":\"roundsum\"}", 250L);
            CH4.ViewBlock[] blocks = _store.GetBlocks();
            Assert.Equal(4, blocks.Length);
            Assert.Equal(100L, blocks[0].Timestamp);
            Assert.Equal(150L, blocks[1].Timestamp);
            Assert.Equal(200L, blocks[2].Timestamp);
            Assert.Equal(250L, blocks[3].Timestamp);
        }

        /// <summary>注入报告——合成渲染首块（时间戳 0 / 固定哈希 / 索引 -1）</summary>
        [Fact]
        public void GetBlocks_InjectReport_PrependsSyntheticBlock()
        {
            _store.OnUserMessage(User("问题", 100L), 0);
            _store.SetInjectReport("{\"file\":\"a.md\"}");
            CH4.ViewBlock[] blocks = _store.GetBlocks();
            Assert.Equal(2, blocks.Length);
            Assert.Equal("inject_report", blocks[0].RenderType);
            Assert.Equal("inject_report", blocks[0].Key);
            Assert.Null(blocks[0].Origin);
            Assert.Equal(0L, blocks[0].Timestamp);
            Assert.Equal("{\"file\":\"a.md\"}", blocks[0].Payload);
        }

        /// <summary>注入报告清空——空串不合成首块</summary>
        [Fact]
        public void SetInjectReport_Empty_NoSyntheticBlock()
        {
            _store.SetInjectReport("{\"file\":\"a.md\"}");
            _store.SetInjectReport("");
            _store.OnUserMessage(User("问题", 100L), 0);
            CH4.ViewBlock[] blocks = _store.GetBlocks();
            Assert.Single(blocks);
            Assert.NotEqual("inject_report", blocks[0].RenderType);
        }

        // ── 辅助块 ──────────────────────────────────────────────────

        /// <summary>轮末统计块——渲染类型 / 哈希序 / 索引 -1 / 载荷原样存储</summary>
        [Fact]
        public void AppendRoundSummary_FieldsAndHash()
        {
            _store.AppendRoundSummary("{\"type\":\"roundsum\"}", 250L);
            _store.AppendRoundSummary("{\"type\":\"roundsum\"}", 260L);
            CH4.ViewBlock[] blocks = _store.GetBlocks();
            Assert.Equal(2, blocks.Length);
            Assert.Equal("roundsum", blocks[0].RenderType);
            Assert.Equal("roundsum:0", blocks[0].Key);
            Assert.Equal("roundsum:1", blocks[1].Key);
            Assert.Null(blocks[0].Origin);
            Assert.Equal("{\"type\":\"roundsum\"}", blocks[0].Payload);
        }

        /// <summary>间隙文本块——空内容（null / 空串）不建块</summary>
        [Fact]
        public void AppendGapText_Empty_NoBlock()
        {
            _store.AppendGapText(null, 100L);
            _store.AppendGapText("", 100L);
            Assert.Empty(_store.GetBlocks());
        }

        /// <summary>间隙文本块——渲染类型 text（前端与转发统一消费）+ 内容载荷</summary>
        [Fact]
        public void AppendGapText_AppendsTextBlock()
        {
            _store.AppendGapText("我这就去查", 150L);
            CH4.ViewBlock[] blocks = _store.GetBlocks();
            Assert.Single(blocks);
            Assert.Equal("text", blocks[0].RenderType);
            Assert.Null(blocks[0].Origin);
            Assert.Equal("我这就去查", ParsePayload(blocks[0]).GetProperty("content").GetString());
        }

        /// <summary>错误块——空文本不建块</summary>
        [Fact]
        public void AppendError_Empty_NoBlock()
        {
            _store.AppendError(null, 100L);
            _store.AppendError("", 100L);
            Assert.Empty(_store.GetBlocks());
        }

        /// <summary>错误块——渲染类型 error + 类型/文本载荷</summary>
        [Fact]
        public void AppendError_AppendsErrorBlock()
        {
            _store.AppendError("ERR|LLM|超时", 200L);
            CH4.ViewBlock[] blocks = _store.GetBlocks();
            Assert.Single(blocks);
            Assert.Equal("error", blocks[0].RenderType);
            JsonElement payload = ParsePayload(blocks[0]);
            Assert.Equal("error", payload.GetProperty("type").GetString());
            Assert.Equal("ERR|LLM|超时", payload.GetProperty("text").GetString());
        }

        /// <summary>重试块——首次写入追加一块（只增不改）</summary>
        [Fact]
        public void AppendRetry_FirstCall_CreatesBlock()
        {
            _store.AppendRetry("{\"state\":\"wait\"}", 300L);
            CH4.ViewBlock[] blocks = _store.GetBlocks();
            Assert.Single(blocks);
            Assert.Equal("retry", blocks[0].RenderType);
            Assert.Equal("retry:0", blocks[0].Key);
        }

        /// <summary>重试块——只增不改：重试中 / 结果各追加一块，不原位更新（莎 2026-10-02 红线）</summary>
        [Fact]
        public void AppendRetry_KeepsEveryState()
        {
            _store.AppendRetry("{\"state\":\"wait\"}", 300L);
            _store.AppendRetry("{\"state\":\"resolved\"}", 500L);
            CH4.ViewBlock[] blocks = _store.GetBlocks();
            Assert.Equal(2, blocks.Length);
            Assert.Equal("retry:0", blocks[0].Key);
            Assert.Equal("retry:1", blocks[1].Key);
            Assert.Equal("{\"state\":\"wait\"}", blocks[0].Payload);
            Assert.Equal("{\"state\":\"resolved\"}", blocks[1].Payload);
        }

        // ── 清理语义 ────────────────────────────────────────────────

        /// <summary>清空——五类块与注入报告全部归零（新会话不保留旧外观）</summary>
        [Fact]
        public void Clear_RemovesAllIncludingInjectReport()
        {
            _store.OnUserMessage(User("问题", 100L), 0);
            _store.AppendGapText("间隙", 110L);
            _store.AppendRoundSummary("{\"type\":\"roundsum\"}", 120L);
            _store.AppendError("ERR|TEST|坏", 130L);
            _store.AppendRetry("{\"state\":\"wait\"}", 140L);
            _store.SetInjectReport("{\"file\":\"a.md\"}");
            _store.Clear();
            Assert.Empty(_store.GetBlocks());
            Assert.Equal("", _store.GetInjectReport());
        }

        /// <summary>清轮末统计——只清 roundsum（消息块与间隙文本保留）</summary>
        [Fact]
        public void ClearRoundSums_KeepsMessageAndGapBlocks()
        {
            _store.OnUserMessage(User("问题", 100L), 0);
            _store.AppendGapText("间隙", 110L);
            _store.AppendRoundSummary("{\"type\":\"roundsum\"}", 120L);
            _store.ClearRoundSums();
            CH4.ViewBlock[] blocks = _store.GetBlocks();
            Assert.Equal(2, blocks.Length);
            Assert.Equal("user", blocks[0].RenderType);
            Assert.Equal("text", blocks[1].RenderType);
        }

        // ── 落盘与往返 ──────────────────────────────────────────────

        /// <summary>落盘——目标目录不存在时自动创建</summary>
        [Fact]
        public void Save_CreatesMissingDirectory()
        {
            string nested = Path.Combine(_dir, "sessions", "s1", "s1.view.json");
            CH4.SessionViewStore store = new CH4.SessionViewStore(nested);
            store.AppendGapText("间隙", 100L);
            Assert.True(File.Exists(nested));
        }

        /// <summary>落盘往返——辅助块（gap / roundsum / error / retry）与注入报告经新实例读回</summary>
        [Fact]
        public void SaveThenLoad_RestoresHelperBlocksAndReport()
        {
            _store.AppendGapText("间隙", 110L);
            _store.AppendRoundSummary("{\"type\":\"roundsum\"}", 120L);
            _store.AppendError("ERR|TEST|坏", 130L);
            _store.AppendRetry("{\"state\":\"wait\"}", 140L);
            _store.SetInjectReport("{\"file\":\"a.md\"}");
            _store.Save();
            CH4.SessionViewStore reloaded = new CH4.SessionViewStore(_path);
            reloaded.Load();
            CH4.ViewBlock[] blocks = reloaded.GetBlocks();
            Assert.Equal(5, blocks.Length);
            Assert.Equal("inject_report", blocks[0].RenderType);
            Assert.Equal("{\"file\":\"a.md\"}", reloaded.GetInjectReport());
        }

        /// <summary>加载——文件缺失时静默保持空报告（视图可重建，不阻断启动）</summary>
        [Fact]
        public void Load_MissingFile_KeepsEmpty()
        {
            CH4.SessionViewStore store = new CH4.SessionViewStore(Path.Combine(_dir, "absent.view.json"));
            store.Load();
            Assert.Equal("", store.GetInjectReport());
            Assert.Empty(store.GetBlocks());
        }

        /// <summary>加载——文件损坏时静默降级（JSON 不可解析不抛异常）</summary>
        [Fact]
        public void Load_BrokenFile_DegradesSilently()
        {
            File.WriteAllText(_path, "{ broken");
            CH4.SessionViewStore store = new CH4.SessionViewStore(_path);
            store.Load();
            Assert.Equal("", store.GetInjectReport());
        }

        // ── 哈希与块 ID ─────────────────────────────────────────────

        /// <summary>内容哈希——同内容稳定、内容不同则不同（时空双索引的"空间"维）</summary>
        [Fact]
        public void ContentHash_Stable_AndContentSensitive()
        {
            _store.OnUserMessage(User("甲", 100L), 0);
            _store.OnUserMessage(User("甲", 100L), 1);
            _store.OnUserMessage(User("乙", 100L), 2);
            CH4.ViewBlock[] blocks = _store.GetBlocks();
            Assert.Equal(blocks[0].Origin.Hash, blocks[1].Origin.Hash);
            Assert.NotEqual(blocks[0].Origin.Hash, blocks[2].Origin.Hash);
        }

        /// <summary>块 ID——块自哈希（64 位十六进制；同内容不同键 / 时刻可区分，A156）</summary>
        [Fact]
        public void BlockId_SelfHash_UniquePerBlock()
        {
            _store.OnUserMessage(User("甲", 100L), 0);
            _store.OnUserMessage(User("甲", 100L), 1);
            CH4.ViewBlock[] blocks = _store.GetBlocks();
            Assert.Equal(64, blocks[0].Id.Length);
            Assert.NotEqual(blocks[0].Id, blocks[1].Id);
        }

        // ── A87 旧会话留档 ──────────────────────────────────────────

        /// <summary>旧会话留档——四部分落档（user / 正式回复 / 加载报告 / 轮结算），思考与工具卡不进档</summary>
        [Fact]
        public void ArchiveLegacy_WritesFourPartsOnly()
        {
            string sessionDir = Path.Combine(_dir, "sessions", "cat_a");
            Directory.CreateDirectory(sessionDir);
            string viewPath = Path.Combine(sessionDir, "cat_a.view.json");
            CH4.SessionViewStore store = new CH4.SessionViewStore(viewPath);
            store.OnUserMessage(User("你好", 1000L), 1);
            store.OnAssistantToolCalls(AssistantWithTools("思考内容", OneToolCall("call_1", "Note", "{}"), 2000L), 2);
            store.OnToolResult(ToolResult("call_1", "Note", "工具结果", 3000L), 3, -1);
            store.OnAssistantText(Assistant("正式回复", 4000L), 4);
            store.AppendRoundSummary("{\"type\":\"roundsum\",\"data\":{\"prompt\":100,\"completion\":50,\"cacheHit\":10,\"toolCount\":1,\"requests\":2,\"elapsedMs\":1234}}", 5000L);
            store.SetInjectReport("{\"files\":[{\"file\":\"CCBP:SOUL.md\",\"status\":\"ok\",\"chars\":12}],\"total\":1,\"ok\":1,\"missing\":0,\"failed\":0,\"toolGroups\":[{\"group\":\"TextCat\",\"tools\":[{\"name\":\"text-read\",\"desc\":\"读\"}]}]}");
            string path = store.ArchiveLegacy("cat_a", "小A");
            Assert.Equal(Path.Combine(_dir, "sessions_old", Path.GetFileName(path)), path);
            Assert.True(File.Exists(path));
            string text = File.ReadAllText(path);
            Assert.Contains("你好", text);
            Assert.Contains("正式回复", text);
            Assert.Contains("轮结算", text);
            Assert.Contains("Token 上 100 / 下 50", text);
            Assert.Contains("新会话加载报告", text);
            Assert.Contains("CCBP:SOUL.md", text);
            Assert.Contains("TextCat", text);
            Assert.DoesNotContain("思考内容", text);
            Assert.DoesNotContain("工具结果", text);
            Assert.StartsWith("小A_", Path.GetFileName(path));
            Assert.EndsWith(".md", path);
        }

        /// <summary>旧会话留档——四部分全空仍出档（档案面留痕优先于体积）</summary>
        [Fact]
        public void ArchiveLegacy_EmptyView_StillWritesFile()
        {
            string sessionDir = Path.Combine(_dir, "sessions", "cat_b");
            Directory.CreateDirectory(sessionDir);
            CH4.SessionViewStore store = new CH4.SessionViewStore(Path.Combine(sessionDir, "cat_b.view.json"));
            string path = store.ArchiveLegacy("cat_b", "小B");
            Assert.True(File.Exists(path));
            string text = File.ReadAllText(path);
            Assert.Contains("无（旧会话未生成注入报告）", text);
            Assert.Contains("user 0", text);
        }

        /// <summary>旧会话留档——视图路径不满足 sessions 层级形态时不落盘并返回空串（不猜落点）</summary>
        [Fact]
        public void ArchiveLegacy_UnexpectedPathShape_ReturnsEmpty()
        {
            CH4.SessionViewStore store = new CH4.SessionViewStore("bare.view.json");
            Assert.Equal("", store.ArchiveLegacy("cat_c", "小C"));
            Assert.False(Directory.Exists(Path.Combine(_dir, "sessions_old")));
        }

        /// <summary>旧会话留档——显示名缺失时文件名前缀回落猫 key</summary>
        [Fact]
        public void ArchiveLegacy_EmptyDisplayName_FallsBackToCatKey()
        {
            string sessionDir = Path.Combine(_dir, "sessions", "cat_d");
            Directory.CreateDirectory(sessionDir);
            CH4.SessionViewStore store = new CH4.SessionViewStore(Path.Combine(sessionDir, "cat_d.view.json"));
            string path = store.ArchiveLegacy("cat_d", "");
            Assert.StartsWith("cat_d_", Path.GetFileName(path));
        }
    }
}
