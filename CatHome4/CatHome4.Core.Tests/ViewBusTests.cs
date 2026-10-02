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
    /// 会话视图出口测试（A158 期三）——两区镜像语义：
    /// 流式区三 op（live.add / live.update / live.remove）· 持久区推入（persist.append）与同键换手 ·
    /// 工具卡先行 → 终态 → 持久块换手 · 流式区清空 · 推送面未就绪静默。
    /// </summary>
    public sealed class ViewBusTests
    {
        /// <summary>捕获型推送面——记录每次 PushView 的 op / payload / meta 三件</summary>
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

            /// <summary>视图事件捕获——op / payload / meta（A158 期三：序号与会话侧序号一并退役）</summary>
            /// <param name="op">事件操作（live.add / live.update / live.remove / persist.append / control）</param>
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

        /// <summary>流式文本——首个增量入区（live.add），后续增量区内容变化（live.update），复位撤区（live.remove）；复位后不再有事件</summary>
        [Fact]
        public void TextStreamAddThenUpdateThenRemove()
        {
            RecordingHost host = new RecordingHost();
            CH4.ViewBus bus = new CH4.ViewBus();
            bus.Attach(host);

            bus.PushTextStream("{\"kind\":\"text\",\"text\":\"a\"}");
            bus.PushTextStream("{\"kind\":\"text\",\"text\":\"b\"}");

            Assert.Equal(2, host.Ops.Count);
            Assert.Equal("live.add", host.Ops[0]);
            Assert.Equal("live.update", host.Ops[1]);
            Assert.Equal("stream", MetaField(host.Metas[0], "renderType"));
            Assert.Equal(MetaField(host.Metas[0], "key"), MetaField(host.Metas[1], "key"));

            bus.ResetTextStream();
            Assert.Equal(3, host.Ops.Count);
            Assert.Equal("live.remove", host.Ops[2]);
            Assert.Equal("", host.Payloads[2]);

            bus.ResetTextStream();
            Assert.Equal(3, host.Ops.Count);
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
            bus.PushTextStream("{\"kind\":\"text\",\"text\":\"b\"}");

            Assert.Equal("live.add", host.Ops[0]);
            Assert.Equal("live.add", host.Ops[1]);
            Assert.Equal("live.update", host.Ops[2]);
            Assert.NotEqual(MetaField(host.Metas[0], "key"), MetaField(host.Metas[1], "key"));
            Assert.Equal(MetaField(host.Metas[0], "key"), MetaField(host.Metas[2], "key"));
        }

        /// <summary>工具卡先行 → 终态——终态为区内原位更新（live.update），块留流式区等持久块换手；同键重复先行不堆叠</summary>
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
            Assert.Equal("live.add", host.Ops[0]);
            Assert.Equal("tool:c1", MetaField(host.Metas[0], "key"));
            Assert.Equal("pending", MetaField(host.Metas[0], "state"));
            Assert.Equal("100", MetaField(host.Metas[0], "ts"));
            Assert.Equal("-1", MetaField(host.Metas[0], "durMs"));

            bus.PushToolCardPending("tool:c1", "{\"name\":\"t\"}", 100);
            Assert.Equal("live.update", host.Ops[1]);

            bus.PushToolCardDone("tool:c1", "{\"name\":\"t\",\"result\":\"ok\"}", 250);
            Assert.Equal("live.update", host.Ops[2]);
            Assert.Equal("final", MetaField(host.Metas[2], "state"));
            Assert.Equal("250", MetaField(host.Metas[2], "durMs"));
            Assert.Equal("100", MetaField(host.Metas[2], "ts"));
            Assert.True(bus.IsToolCardPending("tool:c1"));
            Assert.True(bus.IsToolCardFinaled("tool:c1"));
        }

        /// <summary>工具卡终态无先行卡——先入区（live.add）再定稿（live.update）；被拦工具 / 直执路径</summary>
        [Fact]
        public void ToolCardDoneWithoutPendingCreatesLive()
        {
            RecordingHost host = new RecordingHost();
            CH4.ViewBus bus = new CH4.ViewBus();
            bus.Attach(host);

            bus.PushToolCardDone("tool:c9", "{\"name\":\"t\",\"result\":\"err\"}", 120);

            Assert.Equal(2, host.Ops.Count);
            Assert.Equal("live.add", host.Ops[0]);
            Assert.Equal("live.update", host.Ops[1]);
            Assert.Equal("tool:c9", MetaField(host.Metas[1], "key"));
            Assert.Equal("final", MetaField(host.Metas[1], "state"));
            Assert.Equal("120", MetaField(host.Metas[1], "durMs"));
            Assert.True(bus.IsToolCardFinaled("tool:c9"));
        }

        /// <summary>持久块推入——persist.append 带块元数据（key / id / durMs / state）· 同键流式块换手出区（live.remove）· 同键二次推入幂等</summary>
        [Fact]
        public void PersistAppendEvictsLiveAndIsIdempotent()
        {
            RecordingHost host = new RecordingHost();
            CH4.ViewBus bus = new CH4.ViewBus();
            bus.Attach(host);

            bus.PushToolCardPending("tool:c1", "{\"name\":\"t\"}", 100);
            ViewBlock block = ViewBlock.BuildPending("tool:c1", "toolcard", "{\"name\":\"t\",\"result\":\"ok\"}", 100, null, "independent");
            block.Finalize(250);
            bus.PushPersist(block);

            Assert.Equal(3, host.Ops.Count);
            Assert.Equal("persist.append", host.Ops[1]);
            Assert.Equal("live.remove", host.Ops[2]);
            Assert.Equal("tool:c1", MetaField(host.Metas[1], "key"));
            Assert.Equal(block.Id, MetaField(host.Metas[1], "id"));
            Assert.Equal("250", MetaField(host.Metas[1], "durMs"));
            Assert.Equal("final", MetaField(host.Metas[1], "state"));
            Assert.False(bus.IsToolCardPending("tool:c1"));

            bus.PushPersist(block);
            Assert.Equal(3, host.Ops.Count);
        }

        /// <summary>持久块推入——前文派生块随带 origin 关系字段（src=front）；无 origin 独立块写 null</summary>
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

            Assert.Equal("persist.append", host.Ops[0]);
            Assert.Equal("front", MetaField(host.Metas[0], "src"));
            Assert.Contains("\"msgIndex\":7", host.Metas[0]);
            Assert.Contains("\"hash\":\"abc\"", host.Metas[0]);
        }

        /// <summary>瞬时事件——control op（非块：前端按事件处理，不渲气泡）</summary>
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

        /// <summary>流式区清空——逐块 live.remove；持久推入记录一并作废（同键可再次推入）</summary>
        [Fact]
        public void ResetLiveRemovesAllAndClearsPersist()
        {
            RecordingHost host = new RecordingHost();
            CH4.ViewBus bus = new CH4.ViewBus();
            bus.Attach(host);

            bus.PushTextStream("{\"kind\":\"text\",\"text\":\"a\"}");
            ViewBlock block = ViewBlock.BuildPending("tool:c1", "toolcard", "{}", 100, null, "independent");
            block.Finalize(10);
            bus.PushPersist(block);

            bus.ResetLive();
            Assert.Equal("live.remove", host.Ops[host.Ops.Count - 1]);
            Assert.False(bus.IsToolCardPending("tool:c1"));

            int before = host.Ops.Count;
            bus.PushPersist(block);
            Assert.Equal(before + 1, host.Ops.Count);
            Assert.Equal("persist.append", host.Ops[host.Ops.Count - 1]);
        }

        /// <summary>推送面未就绪——全部入口静默（与拆分前会话侧判空语义一致），流式区不落块</summary>
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
