using System;
using System.Collections.Generic;
using System.Text.Json;
using CatHome4.Contracts;
using Mau.Runtime;
using CH4;
using Xunit;

namespace CatHome4.Core.Tests
{
    /// <summary>
    /// 会话视图出口测试（A162 批次二）——状态推送语义：
    /// 状态表（当前全部块）· 变更集增量（TryTakeDelta：增改块 + 移除键，取走即清）·
    /// 全量载荷（BuildFull）· 同键换手（持久块覆盖流式块，不产生移除）· 瞬时事件（control 仍即时推送）·
    /// 推送面未就绪静默。
    /// </summary>
    public sealed class ViewBusTests
    {
        /// <summary>捕获型推送面——记录每次 PushView 的 op / payload / meta 三件（新模型下仅 control 走此出口）</summary>
        private sealed class RecordingHost : IHostPush
        {
            /// <summary>事件 op 序列（按发出序）</summary>
            public readonly List<string> Ops = new List<string>();

            /// <summary>事件载荷序列</summary>
            public readonly List<string> Payloads = new List<string>();

            /// <summary>事件块元数据序列</summary>
            public readonly List<string> Metas = new List<string>();

            /// <summary>会话完成事件（不捕获）</summary>
            /// <param name="count">会话消息数</param>
            public void PushChatDone(int count) { }

            /// <summary>Note 状态事件（不捕获）</summary>
            /// <param name="json">Note 状态 JSON</param>
            public void PushNoteState(string json) { }

            /// <summary>视图事件捕获——op / payload / meta</summary>
            /// <param name="op">事件操作（control 为唯一在用的即时事件）</param>
            /// <param name="payload">载荷 JSON</param>
            /// <param name="meta">块元数据 JSON</param>
            public void PushView(string op, string payload, string meta)
            {
                Ops.Add(op);
                Payloads.Add(payload);
                Metas.Add(meta);
            }
        }

        /// <summary>取块元数据字段串——测试内轻量解析（测试面不受统一入口约束）</summary>
        /// <param name="meta">块元数据 JSON</param>
        /// <param name="field">字段名</param>
        /// <returns>字段值串（缺失 = 空串）</returns>
        private static string MetaField(string meta, string field)
        {
            using (JsonDocument doc = JsonDocument.Parse(meta))
            {
                JsonElement value;
                if (doc.RootElement.TryGetProperty(field, out value))
                {
                    return value.ToString();
                }
            }
            return "";
        }

        /// <summary>取增量载荷——无变化返回 null</summary>
        /// <param name="bus">视图出口</param>
        /// <returns>载荷 JSON 或 null</returns>
        private static string TakeDelta(CH4.ViewBus bus)
        {
            string json;
            if (bus.TryTakeDelta(out json))
            {
                return json;
            }
            return null;
        }

        /// <summary>取载荷块字段——blocks[index] 的字段值串（缺失 = 空串）</summary>
        /// <param name="payloadJson">载荷 JSON（full / delta）</param>
        /// <param name="index">块下标</param>
        /// <param name="field">字段名</param>
        /// <returns>字段值串</returns>
        private static string BlockField(string payloadJson, int index, string field)
        {
            using (JsonDocument doc = JsonDocument.Parse(payloadJson))
            {
                JsonElement block = doc.RootElement.GetProperty("blocks")[index];
                JsonElement value;
                if (block.TryGetProperty(field, out value))
                {
                    return value.ToString();
                }
            }
            return "";
        }

        /// <summary>取载荷块数</summary>
        /// <param name="payloadJson">载荷 JSON（full / delta）</param>
        /// <returns>块数</returns>
        private static int BlockCount(string payloadJson)
        {
            using (JsonDocument doc = JsonDocument.Parse(payloadJson))
            {
                return doc.RootElement.GetProperty("blocks").GetArrayLength();
            }
        }

        /// <summary>取载荷移除键数</summary>
        /// <param name="payloadJson">载荷 JSON（delta）</param>
        /// <returns>移除键数</returns>
        private static int RemoveCount(string payloadJson)
        {
            using (JsonDocument doc = JsonDocument.Parse(payloadJson))
            {
                return doc.RootElement.GetProperty("remove").GetArrayLength();
            }
        }

        /// <summary>取载荷移除键——remove[index]</summary>
        /// <param name="payloadJson">载荷 JSON（delta）</param>
        /// <param name="index">下标</param>
        /// <returns>块键</returns>
        private static string RemoveKey(string payloadJson, int index)
        {
            using (JsonDocument doc = JsonDocument.Parse(payloadJson))
            {
                return doc.RootElement.GetProperty("remove")[index].GetString();
            }
        }

        /// <summary>流式文本——首个增量入状态表，后续增量同键更新，复位记移除；取走后再复位无变化</summary>
        [Fact]
        public void TextStreamAddThenUpdateThenRemove()
        {
            RecordingHost host = new RecordingHost();
            CH4.ViewBus bus = new CH4.ViewBus();
            bus.Attach(host);

            bus.PushTextStream("{\"kind\":\"text\",\"text\":\"a\"}");
            string first = TakeDelta(bus);
            Assert.NotNull(first);
            Assert.Equal(1, BlockCount(first));
            Assert.Equal("stream", BlockField(first, 0, "renderType"));
            string key = BlockField(first, 0, "key");

            bus.PushTextStream("{\"kind\":\"text\",\"text\":\"b\"}");
            string second = TakeDelta(bus);
            Assert.Equal(1, BlockCount(second));
            Assert.Equal(key, BlockField(second, 0, "key"));

            bus.ResetTextStream();
            string third = TakeDelta(bus);
            Assert.Equal(0, BlockCount(third));
            Assert.Equal(1, RemoveCount(third));
            Assert.Equal(key, RemoveKey(third, 0));

            bus.ResetTextStream();
            Assert.Null(TakeDelta(bus));
        }

        /// <summary>流式思考与文本各自独立容器（互不干扰），键在区内存续期内复用</summary>
        [Fact]
        public void ReasonStreamHasOwnContainer()
        {
            RecordingHost host = new RecordingHost();
            CH4.ViewBus bus = new CH4.ViewBus();
            bus.Attach(host);

            bus.PushTextStream("{\"kind\":\"text\",\"text\":\"a\"}");
            bus.PushReasonStream("{\"kind\":\"reasoning\",\"text\":\"r\"}");
            string both = TakeDelta(bus);
            Assert.Equal(2, BlockCount(both));
            string textKey = BlockField(both, 0, "key");
            string reasonKey = BlockField(both, 1, "key");
            Assert.NotEqual(textKey, reasonKey);

            bus.PushTextStream("{\"kind\":\"text\",\"text\":\"b\"}");
            string third = TakeDelta(bus);
            Assert.Equal(1, BlockCount(third));
            Assert.Equal(textKey, BlockField(third, 0, "key"));
        }

        /// <summary>工具卡先行 → 终态——同键原位更新（仍留在流式区等持久块换手）；同键重复先行不堆叠</summary>
        [Fact]
        public void ToolCardPendingThenDoneStaysLive()
        {
            RecordingHost host = new RecordingHost();
            CH4.ViewBus bus = new CH4.ViewBus();
            bus.Attach(host);

            Assert.False(bus.IsToolCardPending("tool:c1"));
            bus.PushToolCardPending("tool:c1", "{\"name\":\"t\"}", 100);
            Assert.True(bus.IsToolCardPending("tool:c1"));
            Assert.False(bus.IsToolCardFinaled("tool:c1"));
            string pending = TakeDelta(bus);
            Assert.Equal(1, BlockCount(pending));
            Assert.Equal("tool:c1", BlockField(pending, 0, "key"));
            Assert.Equal("pending", BlockField(pending, 0, "state"));
            Assert.Equal("100", BlockField(pending, 0, "ts"));
            Assert.Equal("-1", BlockField(pending, 0, "durMs"));

            bus.PushToolCardPending("tool:c1", "{\"name\":\"t\"}", 100);
            string again = TakeDelta(bus);
            Assert.Equal(1, BlockCount(again));
            Assert.Equal("tool:c1", BlockField(again, 0, "key"));

            bus.PushToolCardDone("tool:c1", "{\"name\":\"t\",\"result\":\"ok\"}", 250);
            string done = TakeDelta(bus);
            Assert.Equal("final", BlockField(done, 0, "state"));
            Assert.Equal("250", BlockField(done, 0, "durMs"));
            Assert.Equal("100", BlockField(done, 0, "ts"));
            Assert.True(bus.IsToolCardPending("tool:c1"));
            Assert.True(bus.IsToolCardFinaled("tool:c1"));

            Assert.Null(TakeDelta(bus));
        }

        /// <summary>工具卡终态无先行卡——先入区再定稿，同一次取走只见最终态一块（被拦工具 / 直执路径）</summary>
        [Fact]
        public void ToolCardDoneWithoutPendingCreatesLive()
        {
            RecordingHost host = new RecordingHost();
            CH4.ViewBus bus = new CH4.ViewBus();
            bus.Attach(host);

            bus.PushToolCardDone("tool:c9", "{\"name\":\"t\",\"result\":\"err\"}", 120);

            string delta = TakeDelta(bus);
            Assert.Equal(1, BlockCount(delta));
            Assert.Equal("tool:c9", BlockField(delta, 0, "key"));
            Assert.Equal("final", BlockField(delta, 0, "state"));
            Assert.Equal("120", BlockField(delta, 0, "durMs"));
            Assert.True(bus.IsToolCardFinaled("tool:c9"));
        }

        /// <summary>持久块记入——同键覆盖流式块（不产生移除指令）· 载荷带块元数据（key / id / durMs / state）· 同键二次记入幂等</summary>
        [Fact]
        public void PersistAppendEvictsLiveAndIsIdempotent()
        {
            RecordingHost host = new RecordingHost();
            CH4.ViewBus bus = new CH4.ViewBus();
            bus.Attach(host);

            bus.PushToolCardPending("tool:c1", "{\"name\":\"t\"}", 100);
            Assert.NotNull(TakeDelta(bus));

            ViewBlock block = ViewBlock.BuildPending("tool:c1", "toolcard", "{\"name\":\"t\",\"result\":\"ok\"}", 100, null, "independent");
            block.Finalize(250);
            bus.PushPersist(block);

            string delta = TakeDelta(bus);
            Assert.Equal(1, BlockCount(delta));
            Assert.Equal(0, RemoveCount(delta));
            Assert.Equal("tool:c1", BlockField(delta, 0, "key"));
            Assert.Equal(block.Id, BlockField(delta, 0, "id"));
            Assert.Equal("250", BlockField(delta, 0, "durMs"));
            Assert.Equal("final", BlockField(delta, 0, "state"));
            Assert.False(bus.IsToolCardPending("tool:c1"));

            bus.PushPersist(block);
            Assert.Null(TakeDelta(bus));
        }

        /// <summary>持久块记入——前文派生块随带 origin 关系字段（src=front）；无 origin 独立块写 null</summary>
        [Fact]
        public void PersistCarriesOrigin()
        {
            RecordingHost host = new RecordingHost();
            CH4.ViewBus bus = new CH4.ViewBus();
            bus.Attach(host);

            ViewOrigin origin = new ViewOrigin();
            origin.MsgIndex = 7;
            origin.Hash = "abc";
            ViewBlock block = ViewBlock.BuildPending("msg:7:text", "text", "{\"content\":\"hi\"}", 1000, origin, "front");
            block.Finalize(-1);
            bus.PushPersist(block);

            string delta = TakeDelta(bus);
            Assert.Equal("front", BlockField(delta, 0, "src"));
            Assert.Contains("\"msgIndex\":7", delta);
            Assert.Contains("\"hash\":\"abc\"", delta);
        }

        /// <summary>全量载荷——当前状态全部块（按时间戳升序），供连接建立时取一次</summary>
        [Fact]
        public void BuildFullCarriesAllBlocks()
        {
            RecordingHost host = new RecordingHost();
            CH4.ViewBus bus = new CH4.ViewBus();
            bus.Attach(host);

            ViewBlock first = ViewBlock.BuildPending("msg:1:text", "text", "{\"content\":\"a\"}", 1000, null, "front");
            first.Finalize(-1);
            bus.PushPersist(first);
            bus.PushTextStream("{\"kind\":\"text\",\"text\":\"live\"}");

            string full = bus.BuildFull();
            Assert.Equal(2, BlockCount(full));
            Assert.Equal("msg:1:text", BlockField(full, 0, "key"));
            Assert.Equal("stream", BlockField(full, 1, "renderType"));

            // 全量取用不动变更集——随后增量仍能取到未取走的变更
            Assert.NotNull(TakeDelta(bus));
        }

        /// <summary>瞬时事件——control 仍即时推送（非块：前端按事件处理，不渲气泡）</summary>
        [Fact]
        public void ControlIsEventNotBlock()
        {
            RecordingHost host = new RecordingHost();
            CH4.ViewBus bus = new CH4.ViewBus();
            bus.Attach(host);

            bus.PushControl("{\"type\":\"chatdone\"}");

            Assert.Single(host.Ops);
            Assert.Equal("control", host.Ops[0]);
            Assert.Equal("control", MetaField(host.Metas[0], "renderType"));
        }

        /// <summary>状态清空——流式块逐个记移除；状态表与幂等记录一并作废（同键可再次记入）</summary>
        [Fact]
        public void ResetLiveRemovesAllAndClearsPersist()
        {
            RecordingHost host = new RecordingHost();
            CH4.ViewBus bus = new CH4.ViewBus();
            bus.Attach(host);

            bus.PushTextStream("{\"kind\":\"text\",\"text\":\"a\"}");
            Assert.NotNull(TakeDelta(bus));
            ViewBlock block = ViewBlock.BuildPending("tool:c1", "toolcard", "{}", 100, null, "independent");
            block.Finalize(10);
            bus.PushPersist(block);
            Assert.NotNull(TakeDelta(bus));

            bus.ResetLive();
            string delta = TakeDelta(bus);
            Assert.Equal(1, RemoveCount(delta));
            Assert.False(bus.IsToolCardPending("tool:c1"));
            Assert.Equal(0, BlockCount(bus.BuildFull()));

            bus.PushPersist(block);
            Assert.NotNull(TakeDelta(bus));
        }

        /// <summary>推送面未就绪——全部入口静默（与拆分前会话侧判空语义一致），状态表不落块</summary>
        [Fact]
        public void DetachedBusIsSilent()
        {
            CH4.ViewBus bus = new CH4.ViewBus();

            Assert.False(bus.Ready);
            bus.PushTextStream("{\"kind\":\"text\",\"text\":\"a\"}");
            bus.PushReasonStream("{\"kind\":\"reasoning\",\"text\":\"r\"}");
            bus.PushControl("{\"type\":\"chatdone\"}");
            bus.PushToolCardPending("tool:c1", "{\"name\":\"t\"}", 8);
            bus.PushToolCardDone("tool:c1", "{\"name\":\"t\"}", 7);
            bus.ResetTextStream();
            bus.ResetReasonStream();
            bus.ResetLive();
            ViewBlock block = ViewBlock.BuildPending("tool:c1", "toolcard", "{}", 8, null, "independent");
            block.Finalize(-1);
            bus.PushPersist(block);

            Assert.False(bus.IsToolCardPending("tool:c1"));
            Assert.False(bus.IsToolCardFinaled("tool:c1"));
            Assert.Equal(0, BlockCount(bus.BuildFull()));

            RecordingHost host = new RecordingHost();
            bus.Attach(host);
            Assert.True(bus.Ready);
            Assert.Empty(host.Ops);
        }

        /// <summary>
        /// 元数据并入的生命周期契约（A157）——JsonElement 是 JsonDocument 的引用视图，
        /// document 释放后再序列化**必须失败**：这是 HttpHost.MergeViewMeta 必须 Clone 的根据。
        /// </summary>
        [Fact]
        public void MetaElementEscapingDocumentThrows()
        {
            Dictionary<string, object> ev = new Dictionary<string, object>();
            using (JsonDocument doc = JsonDocument.Parse("{\"key\":\"tool:c1\",\"ts\":1000}"))
            {
                foreach (JsonProperty prop in doc.RootElement.EnumerateObject())
                {
                    ev[prop.Name] = prop.Value;
                }
            }

            Assert.Throws<ObjectDisposedException>(() => JsonUtil.Serialize(ev));
        }

        /// <summary>
        /// 元数据并入的正确写法（A157）——Clone 后的值节点独立于 document，释放后仍可序列化。
        /// </summary>
        [Fact]
        public void MetaElementClonedSurvivesDispose()
        {
            Dictionary<string, object> ev = new Dictionary<string, object>();
            ev["seq"] = 1L;
            using (JsonDocument doc = JsonDocument.Parse("{\"key\":\"tool:c1\",\"ts\":1000,\"durMs\":-1,\"state\":\"pending\"}"))
            {
                foreach (JsonProperty prop in doc.RootElement.EnumerateObject())
                {
                    ev[prop.Name] = prop.Value.Clone();
                }
            }

            string json = JsonUtil.Serialize(ev);
            Assert.Contains("\"key\":\"tool:c1\"", json);
            Assert.Contains("\"durMs\":-1", json);
        }
    }
}
