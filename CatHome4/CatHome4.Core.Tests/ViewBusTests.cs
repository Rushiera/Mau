using System.Collections.Generic;
using CatHome4.Contracts;
using Xunit;

namespace CatHome4.Core.Tests
{
    /// <summary>
    /// 会话视图出口测试（A155 步二 2a）——实时区序号语义与载荷透传：
    /// 流式容器序号复用 / 整块 replace 指向 / retry 气泡原位更新 / 工具卡先行卡序号 / 推送面未就绪静默。
    /// </summary>
    public sealed class ViewBusTests
    {
        /// <summary>捕获型推送面——记录每次 PushView 的事件形状（renderType / replaceSeq / seq / payload）</summary>
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
            /// <returns>事件序号</returns>
            public int PushView(string renderType, string payload, long replaceSeq, long seqHint)
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
                return seq;
            }
        }

        /// <summary>流式文本增量——首个分配容器序号，后续复用；整块以该序号替换，复位后不再替换</summary>
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

            bus.PushTextBlock("{\"content\":\"ab\"}");
            Assert.Equal("text", host.RenderTypes[2]);
            Assert.Equal(host.Seqs[0], host.ReplaceSeqs[2]);

            bus.ResetTextStream();
            bus.PushTextBlock("{\"content\":\"next\"}");
            Assert.Equal(0L, host.ReplaceSeqs[3]);
        }

        /// <summary>流式思考增量——与文本流各自独立容器（互不干扰）</summary>
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

            bus.PushReasonBlock("{\"content\":\"r\"}", host.Seqs[1]);
            Assert.Equal("reason", host.RenderTypes[3]);
            Assert.Equal(host.Seqs[1], host.ReplaceSeqs[3]);
        }

        /// <summary>retry 气泡——新建分配序号，原位更新指向该序号，复位后在途标志归零</summary>
        [Fact]
        public void RetryBubbleReusesSeqAndResets()
        {
            RecordingHost host = new RecordingHost();
            CH4.ViewBus bus = new CH4.ViewBus();
            bus.Attach(host);

            Assert.False(bus.RetryActive);
            bus.PushRetryNew("{\"state\":\"retrying\"}");
            Assert.True(bus.RetryActive);
            Assert.Equal(-1L, host.ReplaceSeqs[0]);

            bus.PushRetryUpdate("{\"state\":\"resolved\"}");
            Assert.Equal(host.Seqs[0], host.ReplaceSeqs[1]);

            bus.ResetRetry();
            Assert.False(bus.RetryActive);
        }

        /// <summary>工具卡——先行卡分配独立序号，终态以该序号原位替换</summary>
        [Fact]
        public void ToolCardPendingThenFinalReusesSeq()
        {
            RecordingHost host = new RecordingHost();
            CH4.ViewBus bus = new CH4.ViewBus();
            bus.Attach(host);

            long cardSeq = bus.PushToolCardPending("{\"name\":\"t\"}");
            Assert.True(cardSeq > 0);
            Assert.Equal(-1L, host.ReplaceSeqs[0]);

            bus.PushToolCardFinal("{\"name\":\"t\",\"result\":\"ok\"}", cardSeq);
            Assert.Equal(host.Seqs[0], host.ReplaceSeqs[1]);
        }

        /// <summary>固化镜像块——user / control / error / roundsum 均为新建事件（replaceSeq = -1）</summary>
        [Fact]
        public void FixedMirrorBlocksAreNewEvents()
        {
            RecordingHost host = new RecordingHost();
            CH4.ViewBus bus = new CH4.ViewBus();
            bus.Attach(host);

            bus.PushUser("{\"content\":\"hi\"}");
            bus.PushControl("{\"type\":\"chatdone\"}");
            bus.PushError("{\"type\":\"error\",\"text\":\"x\"}");
            bus.PushRoundSum("{\"total\":1}");

            Assert.Equal(4, host.RenderTypes.Count);
            Assert.Equal("user", host.RenderTypes[0]);
            Assert.Equal("control", host.RenderTypes[1]);
            Assert.Equal("error", host.RenderTypes[2]);
            Assert.Equal("roundsum", host.RenderTypes[3]);
            Assert.Equal(-1L, host.ReplaceSeqs[0]);
            Assert.Equal(-1L, host.ReplaceSeqs[1]);
            Assert.Equal(-1L, host.ReplaceSeqs[2]);
            Assert.Equal(-1L, host.ReplaceSeqs[3]);
        }

        /// <summary>推送面未就绪——全部入口静默（与拆分前会话侧判空语义一致），序号状态可复位</summary>
        [Fact]
        public void DetachedBusIsSilent()
        {
            CH4.ViewBus bus = new CH4.ViewBus();

            Assert.False(bus.Ready);
            bus.PushUser("{\"content\":\"hi\"}");
            bus.PushTextStream("{\"kind\":\"text\",\"text\":\"a\"}");
            bus.PushReasonStream("{\"kind\":\"reasoning\",\"text\":\"r\"}");
            bus.PushTextBlock("{\"content\":\"a\"}");
            bus.PushReasonBlock("{\"content\":\"r\"}", 0);
            bus.PushControl("{\"type\":\"chatdone\"}");
            bus.PushError("{\"type\":\"error\"}");
            bus.PushRoundSum("{\"total\":1}");
            bus.PushRetryNew("{\"state\":\"retrying\"}");
            bus.PushRetryUpdate("{\"state\":\"resolved\"}");
            bus.ResetRetry();
            bus.ResetTextStream();
            bus.ResetReasonStream();

            Assert.False(bus.RetryActive);
            Assert.Equal(0L, bus.PushToolCardPending("{\"name\":\"t\"}"));
            bus.PushToolCardFinal("{\"name\":\"t\"}", 7);

            RecordingHost host = new RecordingHost();
            bus.Attach(host);
            Assert.True(bus.Ready);
            Assert.Empty(host.RenderTypes);
        }
    }
}
