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
    /// 会话视图出口测试（A165 阶段 1 · chat 快照协议 v2）——三段结构：
    /// state（后端权威业务态整段比对，变化才推）· persist（持久对话区只增：全量 / 追加两模式）·
    /// live（临时区全量镜像：stream.text / stream.reason / toolcard.pending）。
    /// 铁律：无事发生零字节 · 推送序即渲染序 · 同步标识面（块键 / 块 ID / 生命周期态 / 移除指令 / 两区换手）全部退役。
    /// </summary>
    public sealed class ViewBusTests
    {
        /// <summary>捕获型推送面——记录每次 PushView 的 op（v2 下视图面不再走此出口；保留以满足接口契约）</summary>
        private sealed class RecordingHost : IHostPush
        {
            /// <summary>事件 op 序列（按发出序）</summary>
            public readonly List<string> Ops = new List<string>();

            /// <summary>视图事件捕获——op</summary>
            /// <param name="op">事件操作</param>
            /// <param name="payload">载荷 JSON</param>
            /// <param name="meta">块元数据 JSON</param>
            public void PushView(string op, string payload, string meta)
            {
                Ops.Add(op);
            }
        }

        /// <summary>构造持久块——v2 五字段（测试面直赋）</summary>
        /// <param name="type">渲染类型</param>
        /// <param name="payload">载荷 JSON</param>
        /// <param name="ts">事件时刻</param>
        /// <param name="msgIndex">前文来源索引（独立块 -1）</param>
        /// <param name="round">所属轮次</param>
        /// <returns>视图块</returns>
        private static ViewBlock Block(string type, string payload, long ts, int msgIndex, int round)
        {
            ViewBlock block = new ViewBlock();
            block.RenderType = type;
            block.Payload = payload;
            block.Timestamp = ts;
            block.MsgIndex = msgIndex;
            block.Round = round;
            return block;
        }

        /// <summary>取帧——无变化返回 null</summary>
        /// <param name="bus">视图出口</param>
        /// <returns>帧 JSON 或 null</returns>
        private static string TakeFrame(CH4.ViewBus bus)
        {
            string json;
            if (bus.TryTakeFrame(out json))
            {
                return json;
            }
            return null;
        }

        /// <summary>
        /// 全量帧取自注入的数据源——单一真相源（视图存储），每次现取、按时间戳升序（前端零排序）。
        /// 判例 2026-10-03：输出侧持副本 → 载入的历史发不出去（刷新即丢视图面历史）。
        /// </summary>
        [Fact]
        public void FullFrame_TakesFromInjectedSource()
        {
            CH4.ViewBus bus = new CH4.ViewBus();
            RecordingHost host = new RecordingHost();
            bus.Attach(host);
            bus.AttachSource(delegate ()
            {
                return new ViewBlock[]
                {
                    Block("user", "{\"text\":\"旧消息\"}", 2000, 1, 0),
                    Block("reason", "{\"text\":\"旧思考\"}", 1000, 2, 0)
                };
            });
            JsonElement root = Root(bus.BuildFull());
            JsonElement items = root.GetProperty("persist").GetProperty("items");
            Assert.Equal(2, items.GetArrayLength());
            Assert.Equal("reason", items[0].GetProperty("type").GetString());
            Assert.Equal("user", items[1].GetProperty("type").GetString());
        }

        /// <summary>解析 JSON 根节点（独立副本——原 document 立刻释放）</summary>
        /// <param name="json">JSON 文本</param>
        /// <returns>根元素副本</returns>
        private static JsonElement Root(string json)
        {
            using (JsonDocument doc = JsonDocument.Parse(json))
            {
                return doc.RootElement.Clone();
            }
        }

        /// <summary>取临时区条目数组</summary>
        /// <param name="frame">帧 JSON</param>
        /// <returns>live items 数组</returns>
        private static JsonElement LiveItems(string frame)
        {
            return Root(frame).GetProperty("live").GetProperty("items");
        }

        /// <summary>全量帧——三段齐备；条目只带业务定位字段（无键 / 无 ID / 无生命周期态）</summary>
        [Fact]
        public void FullFrameCarriesThreeSegments()
        {
            RecordingHost host = new RecordingHost();
            CH4.ViewBus bus = new CH4.ViewBus();
            bus.Attach(host);
            bus.SetState("{\"runState\":\"idle\"}");
            bus.PushPersist(Block("text", "{\"text\":\"甲\"}", 1000, 0, 1));
            bus.PushTextStream("流");

            string full = bus.BuildFull();
            JsonElement root = Root(full);
            Assert.Equal(2, root.GetProperty("v").GetInt32());
            Assert.Equal("idle", root.GetProperty("state").GetProperty("runState").GetString());
            Assert.Equal("full", root.GetProperty("persist").GetProperty("mode").GetString());
            JsonElement items = root.GetProperty("persist").GetProperty("items");
            Assert.Equal(1, items.GetArrayLength());
            JsonElement item = items[0];
            Assert.Equal("text", item.GetProperty("type").GetString());
            Assert.Equal(1000L, item.GetProperty("ts").GetInt64());
            Assert.Equal(1, item.GetProperty("round").GetInt32());
            Assert.Equal(0, item.GetProperty("msgIndex").GetInt32());
            Assert.Equal("甲", item.GetProperty("payload").GetProperty("text").GetString());
            JsonElement unused;
            Assert.False(item.TryGetProperty("key", out unused));
            Assert.False(item.TryGetProperty("id", out unused));
            Assert.False(item.TryGetProperty("state", out unused));
            Assert.Equal("stream.text", LiveItems(full)[0].GetProperty("type").GetString());
        }

        /// <summary>追加帧——新持久条目按 append 模式推送（其余段省略）</summary>
        [Fact]
        public void AppendFrameCarriesPersistAppendOnly()
        {
            RecordingHost host = new RecordingHost();
            CH4.ViewBus bus = new CH4.ViewBus();
            bus.Attach(host);

            bus.PushPersist(Block("user", "{\"text\":\"问\"}", 1000, 0, 1));
            string frame = TakeFrame(bus);
            Assert.NotNull(frame);
            JsonElement root = Root(frame);
            Assert.Equal("append", root.GetProperty("persist").GetProperty("mode").GetString());
            Assert.Equal(1, root.GetProperty("persist").GetProperty("items").GetArrayLength());
            JsonElement unused;
            Assert.False(root.TryGetProperty("state", out unused));
            Assert.False(root.TryGetProperty("live", out unused));
        }

        /// <summary>无事发生零字节——取走后再取返回空</summary>
        [Fact]
        public void NoChangeYieldsNoFrame()
        {
            RecordingHost host = new RecordingHost();
            CH4.ViewBus bus = new CH4.ViewBus();
            bus.Attach(host);

            bus.PushPersist(Block("text", "{}", 1000, 0, 1));
            Assert.NotNull(TakeFrame(bus));
            Assert.Null(TakeFrame(bus));
        }

        /// <summary>状态段——整段比对：同值零帧，变化才入帧</summary>
        [Fact]
        public void StateSegmentPushesOnChangeOnly()
        {
            RecordingHost host = new RecordingHost();
            CH4.ViewBus bus = new CH4.ViewBus();
            bus.Attach(host);

            bus.SetState("{\"runState\":\"idle\"}");
            Assert.NotNull(TakeFrame(bus));

            bus.SetState("{\"runState\":\"idle\"}");
            Assert.Null(TakeFrame(bus));

            bus.SetState("{\"runState\":\"wait\"}");
            string frame = TakeFrame(bus);
            Assert.Equal("wait", Root(frame).GetProperty("state").GetProperty("runState").GetString());
        }

        /// <summary>临时区全量镜像——流式文本累积全文（非增量）；出区后条目消失</summary>
        [Fact]
        public void TextStreamMirrorsAccumulatedText()
        {
            RecordingHost host = new RecordingHost();
            CH4.ViewBus bus = new CH4.ViewBus();
            bus.Attach(host);

            bus.PushTextStream("甲");
            Assert.Equal("甲", LiveItems(TakeFrame(bus))[0].GetProperty("payload").GetProperty("text").GetString());

            bus.PushTextStream("乙");
            Assert.Equal("甲乙", LiveItems(TakeFrame(bus))[0].GetProperty("payload").GetProperty("text").GetString());

            bus.ResetTextStream();
            Assert.Equal(0, LiveItems(TakeFrame(bus)).GetArrayLength());
            Assert.Null(TakeFrame(bus));
        }

        /// <summary>临时区两类流式互不干扰——文本与思考各一容器，同帧全量镜像两条目</summary>
        [Fact]
        public void StreamTypesAreIndependent()
        {
            RecordingHost host = new RecordingHost();
            CH4.ViewBus bus = new CH4.ViewBus();
            bus.Attach(host);

            bus.PushTextStream("甲");
            bus.PushReasonStream("乙");
            JsonElement items = LiveItems(TakeFrame(bus));
            Assert.Equal(2, items.GetArrayLength());
            Assert.Equal("stream.text", items[0].GetProperty("type").GetString());
            Assert.Equal("stream.reason", items[1].GetProperty("type").GetString());

            bus.PushReasonStream("丙");
            JsonElement mirror = LiveItems(TakeFrame(bus));
            Assert.Equal("乙丙", mirror[1].GetProperty("payload").GetProperty("text").GetString());
        }

        /// <summary>工具卡交接——进行中入面板；完成态以同时间戳落地即清面板项（同一次调用两态不并存于两区）</summary>
        [Fact]
        public void ToolCardHandoverClearsLiveSlot()
        {
            RecordingHost host = new RecordingHost();
            CH4.ViewBus bus = new CH4.ViewBus();
            bus.Attach(host);

            Assert.False(bus.IsToolCardPending("tool:c1"));
            bus.PushToolCardPending("tool:c1", "{\"name\":\"t\"}", 100);
            Assert.True(bus.IsToolCardPending("tool:c1"));
            Assert.False(bus.IsToolCardFinaled("tool:c1"));
            JsonElement pending = LiveItems(TakeFrame(bus));
            Assert.Equal(1, pending.GetArrayLength());
            Assert.Equal("toolcard.pending", pending[0].GetProperty("type").GetString());
            Assert.Equal("t", pending[0].GetProperty("payload").GetProperty("name").GetString());

            bus.PushToolCardPending("tool:c1", "{\"name\":\"t\"}", 100);
            Assert.Equal(1, LiveItems(TakeFrame(bus)).GetArrayLength());

            bus.PushToolCardDone("tool:c1", "{\"name\":\"t\",\"result\":\"ok\"}");
            Assert.True(bus.IsToolCardFinaled("tool:c1"));
            Assert.NotNull(TakeFrame(bus));

            bus.PushPersist(Block("toolcard", "{\"name\":\"t\",\"result\":\"ok\"}", 100, -1, 3));
            string frame = TakeFrame(bus);
            Assert.Equal(1, Root(frame).GetProperty("persist").GetProperty("items").GetArrayLength());
            Assert.Equal(0, LiveItems(frame).GetArrayLength());
            Assert.False(bus.IsToolCardPending("tool:c1"));
            Assert.Null(TakeFrame(bus));
        }

        /// <summary>工具卡终态无先行卡——直接建面板项并登记交接（被拦工具 / 直执路径）</summary>
        [Fact]
        public void ToolCardDoneWithoutPendingCreatesLive()
        {
            RecordingHost host = new RecordingHost();
            CH4.ViewBus bus = new CH4.ViewBus();
            bus.Attach(host);

            bus.PushToolCardDone("tool:c9", "{\"name\":\"t\",\"result\":\"err\"}");
            Assert.True(bus.IsToolCardFinaled("tool:c9"));
            JsonElement items = LiveItems(TakeFrame(bus));
            Assert.Equal(1, items.GetArrayLength());
            Assert.Equal("err", items[0].GetProperty("payload").GetProperty("result").GetString());
        }

        /// <summary>重置——两区全清并置全量待发（帧轮广播空态全量帧，前端整体重绘）</summary>
        [Fact]
        public void ResetAllEmitsFullFrame()
        {
            RecordingHost host = new RecordingHost();
            CH4.ViewBus bus = new CH4.ViewBus();
            bus.Attach(host);

            bus.PushPersist(Block("text", "{}", 1000, 0, 1));
            bus.PushTextStream("甲");
            Assert.NotNull(TakeFrame(bus));

            bus.ResetAll();
            string frame = TakeFrame(bus);
            JsonElement root = Root(frame);
            Assert.Equal("full", root.GetProperty("persist").GetProperty("mode").GetString());
            Assert.Equal(0, root.GetProperty("persist").GetProperty("items").GetArrayLength());
            Assert.Equal(0, root.GetProperty("live").GetProperty("items").GetArrayLength());
            Assert.Null(TakeFrame(bus));
        }

        /// <summary>推送面未就绪——全部入口静默（不落块、不积帧、槽位不建）</summary>
        [Fact]
        public void DetachedBusIsSilent()
        {
            CH4.ViewBus bus = new CH4.ViewBus();

            Assert.False(bus.Ready);
            bus.SetState("{\"runState\":\"idle\"}");
            bus.PushTextStream("甲");
            bus.PushReasonStream("乙");
            bus.PushToolCardPending("tool:c1", "{\"name\":\"t\"}", 8);
            bus.PushToolCardDone("tool:c1", "{\"name\":\"t\"}");
            bus.ResetTextStream();
            bus.ResetReasonStream();
            bus.PushPersist(Block("text", "{}", 1000, -1, 1));

            Assert.Null(TakeFrame(bus));
            Assert.False(bus.IsToolCardPending("tool:c1"));
            Assert.False(bus.IsToolCardFinaled("tool:c1"));
            Assert.Empty(bus.GetLiveBlocks());

            RecordingHost host = new RecordingHost();
            bus.Attach(host);
            Assert.True(bus.Ready);
            Assert.Empty(host.Ops);
        }

        /// <summary>
        /// 元数据并入的生命周期契约（A157）——JsonElement 是 JsonDocument 的引用视图，
        /// document 释放后再序列化**必须失败**：这是宿主侧 MergeViewMeta 必须 Clone 的根据。
        /// </summary>
        [Fact]
        public void MetaElementEscapingDocumentThrows()
        {
            Dictionary<string, object> ev = new Dictionary<string, object>();
            using (JsonDocument doc = JsonDocument.Parse("{\"ts\":1000,\"round\":3}"))
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
            using (JsonDocument doc = JsonDocument.Parse("{\"ts\":1000,\"round\":3}"))
            {
                foreach (JsonProperty prop in doc.RootElement.EnumerateObject())
                {
                    ev[prop.Name] = prop.Value.Clone();
                }
            }

            string json = JsonUtil.Serialize(ev);
            Assert.Contains("\"ts\":1000", json);
            Assert.Contains("\"round\":3", json);
        }
    }
}
