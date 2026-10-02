using System;
using System.Collections.Generic;
using System.Text.Json;
using CatHome4.Contracts;
using Mau.Runtime;
using Xunit;

namespace CatHome4.Core.Tests
{
    /// <summary>
    /// 会话视图出口测试（A155 步二 2a / A157 期二）——实时区状态与块元数据：
    /// 流式容器键与序号复用 / 整块 replace 指向 / retry 气泡原位更新 / 工具卡槽位定稿与幂等 /
    /// 块元数据（key / ts / durMs / state）随事件下发 / 推送面未就绪静默。
    /// </summary>
    public sealed class ViewBusTests
    {
        /// <summary>捕获型推送面——记录每次 PushView 的事件形状（renderType / replaceSeq / seq / payload / meta）</summary>
        private sealed class RecordingHost : IHostPush
        {
            /// <summary>事件渲染类型序列（按发出序）</summary>
            public readonly List<string> RenderTypes = new List<string>();

            /// <summary>事件替换序号序列（-1=新建）</summary>
            public readonly List<long> ReplaceSeqs = new List<long>();

            /// <summary>事件序号序列</summary>
            public readonly List<long> Seqs = new List<long>();

            /// <summary>事件载荷序列</summary>
            public readonly List<string> Payloads = new List<string>();

            /// <summary>事件块元数据序列（A157：key / ts / durMs / state）</summary>
            public readonly List<string> Metas = new List<string>();

            /// <summary>自增序号源——与宿主 PushView 分配语义同构（初值区分于测试断言值）</summary>
            private int _seq = 100;

            /// <summary>会话完成事件（不捕获）</summary>
            /// <param name="count">会话消息数</param>
            public void PushChatDone(int count) { }

            /// <summary>Note 状态事件（不捕获）</summary>
            /// <param name="json">Note 状态 JSON</param>
            public void PushNoteState(string json) { }

            /// <summary>视图事件捕获——seqHint &gt; 0 复用，否则分配新序号（与宿主实现同构）</summary>
            /// <param name="renderType">渲染类型</param>
            /// <param name="payload">载荷 JSON</param>
            /// <param name="replaceSeq">被替换块序号</param>
            /// <param name="seqHint">流式增量带序号</param>
            /// <param name="meta">块元数据 JSON（A157）</param>
            /// <returns>事件序号</returns>
            public int PushView(string renderType, string payload, long replaceSeq, long seqHint, string meta)
            {
                int seq;
                if (seqHint > 0)
                {
                    seq = (int)seqHint;
                }
                else
                {
                    _seq = _seq + 1;
                    seq = _seq;
                }
                RenderTypes.Add(renderType);
                ReplaceSeqs.Add(replaceSeq);
                Seqs.Add(seq);
                Payloads.Add(payload);
                Metas.Add(meta);
                return seq;
            }
        }

        /// <summary>取块元数据字段串——测试内轻量解析（A157；测试面不受统一入口约束）</summary>
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

        /// <summary>流式文本增量——首个分配容器键与序号，后续复用；整块沿用同键、以该序号替换；复位后不再替换</summary>
        [Fact]
        public void TextStreamSeqReusedUntilReset()
        {
            RecordingHost host = new RecordingHost();
            CH4.ViewBus bus = new CH4.ViewBus();
            bus.Attach(host);

            bus.PushTextStream("{\"kind\":\"text\",\"text\":\"a\"}");
            bus.PushTextStream("{\"kind\":\"text\",\"text\":\"b\"}");

            Assert.Equal(2, host.RenderTypes.Count);
            Assert.Equal("stream", host.RenderTypes[0]);
            Assert.Equal(-1L, host.ReplaceSeqs[0]);
            Assert.Equal(host.Seqs[0], host.Seqs[1]);
            Assert.Equal("pending", MetaField(host.Metas[0], "state"));

            bus.PushTextBlock("{\"content\":\"ab\"}", "", 1000);
            Assert.Equal("text", host.RenderTypes[2]);
            Assert.Equal(host.Seqs[0], host.ReplaceSeqs[2]);
            Assert.Equal(MetaField(host.Metas[0], "key"), MetaField(host.Metas[2], "key"));
            Assert.Equal("final", MetaField(host.Metas[2], "state"));

            bus.ResetTextStream();
            bus.PushTextBlock("{\"content\":\"next\"}", "", 2000);
            Assert.Equal(0L, host.ReplaceSeqs[3]);
            Assert.Equal("2000", MetaField(host.Metas[3], "ts"));
        }

        /// <summary>流式思考增量——与文本流各自独立容器（互不干扰）；整块沿用自身容器键</summary>
        [Fact]
        public void ReasonStreamHasOwnContainer()
        {
            RecordingHost host = new RecordingHost();
            CH4.ViewBus bus = new CH4.ViewBus();
            bus.Attach(host);

            bus.PushTextStream("{\"kind\":\"text\",\"text\":\"a\"}");
            bus.PushReasonStream("{\"kind\":\"reasoning\",\"text\":\"r\"}");
            bus.PushTextStream("{\"kind\":\"text\",\"text\":\"b\"}");

            Assert.NotEqual(host.Seqs[0], host.Seqs[1]);
            Assert.Equal(host.Seqs[0], host.Seqs[2]);
            Assert.NotEqual(MetaField(host.Metas[0], "key"), MetaField(host.Metas[1], "key"));

            bus.PushReasonBlock("{\"content\":\"r\"}", host.Seqs[1], "", 1500);
            Assert.Equal("reason", host.RenderTypes[3]);
            Assert.Equal(host.Seqs[1], host.ReplaceSeqs[3]);
            Assert.Equal(MetaField(host.Metas[1], "key"), MetaField(host.Metas[3], "key"));
        }

        /// <summary>retry 气泡——新建分配序号，原位更新指向该序号且沿用同键，复位后在途标志归零</summary>
        [Fact]
        public void RetryBubbleReusesSeqAndResets()
        {
            RecordingHost host = new RecordingHost();
            CH4.ViewBus bus = new CH4.ViewBus();
            bus.Attach(host);

            Assert.False(bus.RetryActive);
            bus.PushRetryNew("{\"state\":\"retrying\"}", 700);
            Assert.True(bus.RetryActive);
            Assert.Equal(-1L, host.ReplaceSeqs[0]);
            Assert.Equal("pending", MetaField(host.Metas[0], "state"));
            Assert.Equal("700", MetaField(host.Metas[0], "ts"));

            bus.PushRetryUpdate("{\"state\":\"resolved\"}", 800);
            Assert.Equal(host.Seqs[0], host.ReplaceSeqs[1]);
            Assert.Equal(MetaField(host.Metas[0], "key"), MetaField(host.Metas[1], "key"));
            Assert.Equal("final", MetaField(host.Metas[1], "state"));

            bus.ResetRetry();
            Assert.False(bus.RetryActive);
        }

        /// <summary>工具卡——先行卡入槽位（pending / durMs = -1），终态同键原位替换并带时长与 final 态；终态幂等</summary>
        [Fact]
        public void ToolCardPendingThenFinalReusesSeq()
        {
            RecordingHost host = new RecordingHost();
            CH4.ViewBus bus = new CH4.ViewBus();
            bus.Attach(host);

            Assert.False(bus.IsToolCardPending("tool:c1"));
            bus.PushToolCardPending("tool:c1", "{\"name\":\"t\"}", 100);
            Assert.True(bus.IsToolCardPending("tool:c1"));
            Assert.Equal(-1L, host.ReplaceSeqs[0]);
            Assert.Equal("tool:c1", MetaField(host.Metas[0], "key"));
            Assert.Equal("pending", MetaField(host.Metas[0], "state"));
            Assert.Equal("-1", MetaField(host.Metas[0], "durMs"));

            bus.PushToolCardFinal("tool:c1", "{\"name\":\"t\",\"result\":\"ok\"}", 250, 100);
            Assert.Equal(host.Seqs[0], host.ReplaceSeqs[1]);
            Assert.Equal("tool:c1", MetaField(host.Metas[1], "key"));
            Assert.Equal("final", MetaField(host.Metas[1], "state"));
            Assert.Equal("250", MetaField(host.Metas[1], "durMs"));
            Assert.Equal("100", MetaField(host.Metas[1], "ts"));
            Assert.False(bus.IsToolCardPending("tool:c1"));
            Assert.True(bus.IsToolCardFinaled("tool:c1"));

            bus.PushToolCardFinal("tool:c1", "{\"name\":\"t\",\"result\":\"ok2\"}", 300, 100);
            Assert.Equal(2, host.RenderTypes.Count);
        }

        /// <summary>工具卡终态无先行卡——新建推送（被拦工具 / 直执路径），仍带时长与 final 态；复位后记录清空</summary>
        [Fact]
        public void ToolCardFinalWithoutPendingCreatesNew()
        {
            RecordingHost host = new RecordingHost();
            CH4.ViewBus bus = new CH4.ViewBus();
            bus.Attach(host);

            bus.PushToolCardFinal("tool:c9", "{\"name\":\"t\",\"result\":\"err\"}", 120, 900);

            Assert.Equal(-1L, host.ReplaceSeqs[0]);
            Assert.Equal("final", MetaField(host.Metas[0], "state"));
            Assert.Equal("120", MetaField(host.Metas[0], "durMs"));
            Assert.Equal("900", MetaField(host.Metas[0], "ts"));
            Assert.True(bus.IsToolCardFinaled("tool:c9"));

            bus.ResetToolCards();
            Assert.False(bus.IsToolCardFinaled("tool:c9"));
        }

        /// <summary>固化镜像块——user / control / error / roundsum 均为新建事件（replaceSeq = -1），键随调用方给定</summary>
        [Fact]
        public void FixedMirrorBlocksAreNewEvents()
        {
            RecordingHost host = new RecordingHost();
            CH4.ViewBus bus = new CH4.ViewBus();
            bus.Attach(host);

            bus.PushUser("{\"content\":\"hi\"}", "msg:3:user", 1000);
            bus.PushControl("{\"type\":\"chatdone\"}");
            bus.PushError("{\"type\":\"error\",\"text\":\"x\"}", "error:0", 1100);
            bus.PushRoundSum("{\"total\":1}", "roundsum:0", 1200);

            Assert.Equal(4, host.RenderTypes.Count);
            Assert.Equal("user", host.RenderTypes[0]);
            Assert.Equal("control", host.RenderTypes[1]);
            Assert.Equal("error", host.RenderTypes[2]);
            Assert.Equal("roundsum", host.RenderTypes[3]);
            Assert.Equal(-1L, host.ReplaceSeqs[0]);
            Assert.Equal(-1L, host.ReplaceSeqs[1]);
            Assert.Equal(-1L, host.ReplaceSeqs[2]);
            Assert.Equal(-1L, host.ReplaceSeqs[3]);
            Assert.Equal("msg:3:user", MetaField(host.Metas[0], "key"));
            Assert.Equal("error:0", MetaField(host.Metas[2], "key"));
            Assert.Equal("roundsum:0", MetaField(host.Metas[3], "key"));
            Assert.Equal("1000", MetaField(host.Metas[0], "ts"));
        }

        /// <summary>推送面未就绪——全部入口静默（与拆分前会话侧判空语义一致），工具卡槽位不落</summary>
        [Fact]
        public void DetachedBusIsSilent()
        {
            CH4.ViewBus bus = new CH4.ViewBus();

            Assert.False(bus.Ready);
            bus.PushUser("{\"content\":\"hi\"}", "msg:0:user", 1);
            bus.PushTextStream("{\"kind\":\"text\",\"text\":\"a\"}");
            bus.PushReasonStream("{\"kind\":\"reasoning\",\"text\":\"r\"}");
            bus.PushTextBlock("{\"content\":\"a\"}", "", 2);
            bus.PushReasonBlock("{\"content\":\"r\"}", 0, "", 3);
            bus.PushControl("{\"type\":\"chatdone\"}");
            bus.PushError("{\"type\":\"error\"}", "", 4);
            bus.PushRoundSum("{\"total\":1}", "", 5);
            bus.PushRetryNew("{\"state\":\"retrying\"}", 6);
            bus.PushRetryUpdate("{\"state\":\"resolved\"}", 7);
            bus.ResetRetry();
            bus.ResetTextStream();
            bus.ResetReasonStream();
            bus.PushToolCardPending("tool:c1", "{\"name\":\"t\"}", 8);
            bus.PushToolCardFinal("tool:c1", "{\"name\":\"t\"}", 7, 9);

            Assert.False(bus.RetryActive);
            Assert.False(bus.IsToolCardPending("tool:c1"));
            Assert.False(bus.IsToolCardFinaled("tool:c1"));

            RecordingHost host = new RecordingHost();
            bus.Attach(host);
            Assert.True(bus.Ready);
            Assert.Empty(host.RenderTypes);
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
