using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Mau.Runtime;
using Xunit;

namespace Mau.Runtime.Tests
{
    /// <summary>
    /// LLM 盒桥 Key 段隔离测试——P9.2 双实例并发流式互不覆盖（design-llm-streaming §7.4 验收）。
    /// 双实例同 dll（FL_LlmSeg——fixture 语料镜像 quick_cat 流式线，无 OA）；Stub LLM 按调用序分流：
    /// 实例 A（注册序先）慢流三分块 / 实例 B 快流单分块——两后台流交错存在，隔离靠 flowId 段键。
    /// 主干串行 Tick 驱动——无工单并发语义（莎拍板：主干单线程逐个处理，复杂工作抛线程拿结果）。
    /// 串行集：依赖 DataBox 静态面（服务绑定/信号注册）——与其他静态状态测试同集（并行类 Reset 干扰判例：全 sln 跑 A 未 Done=Stub 被清）。
    /// </summary>
    [Collection("AuditSerial")]
    public sealed class LlmSegmentTests
    {
        /// <summary>
        /// Stub LLM 运行时——按调用序分流（1=实例 A 慢流三分块 / 其他=实例 B 快流单分块）
        /// </summary>
        private sealed class StubLlmRuntime : ILlmRuntime
        {
            /// <summary>
            /// 调用序号——Interlocked 递增（多后台线程安全）
            /// </summary>
            private int _seq;

            /// <summary>
            /// 流式对话完成——固定事件序列（不分内容，按调用序分流——测试侧控制实例先后）
            /// </summary>
            /// <param name="messages">消息序列</param>
            /// <param name="tools">工具定义</param>
            /// <param name="ct">取消令牌</param>
            /// <returns>流式事件序列（async iterator 为接口形态必需）</returns>
            public async IAsyncEnumerable<LlmStreamEvent> ChatStream(LlmMessage[] messages, ToolSpec[] tools, [EnumeratorCancellation] CancellationToken ct = default)
            {
                int seq = System.Threading.Interlocked.Increment(ref _seq);
                if (seq == 1)
                {
                    // 实例 A——慢流三分块（延迟制造交错窗口：B 在 A 未完成前先结算）
                    await Task.Delay(60);
                    yield return new LlmStreamEvent(LlmStreamKind.Text, "REPLY_A_PART1");
                    await Task.Delay(40);
                    yield return new LlmStreamEvent(LlmStreamKind.Text, "REPLY_A_PART2");
                    await Task.Delay(40);
                    yield return new LlmStreamEvent(LlmStreamKind.Text, "REPLY_A_END");
                    yield return new LlmStreamEvent(LlmStreamKind.Done, "");
                }
                else
                {
                    // 实例 B——快流单分块（A 流式期间先完成——交错竞争段键窗口）
                    yield return new LlmStreamEvent(LlmStreamKind.Text, "REPLY_B_ONLY");
                    yield return new LlmStreamEvent(LlmStreamKind.Done, "");
                }
            }
        }

        /// <summary>
        /// Flow 是否 Done——状态行含 "S_Flow=Done"
        /// </summary>
        /// <param name="handle">Flow 句柄</param>
        /// <returns>true=Done</returns>
        private static bool IsDone(FlowHandle handle)
        {
            if (handle.Flow == null)
            {
                return false;
            }
            FlowStatusV3 status = handle.Flow.GetStatus();
            if (status == null || status.StateLines == null)
            {
                return false;
            }
            return Array.IndexOf(status.StateLines, "S_Flow=Done") >= 0;
        }

        /// <summary>
        /// 双实例流式隔离——两条后台流交错，各实例段键/私有盒互不覆盖（验收：quick 双实例并发流式互不覆盖）
        /// </summary>
        [Fact]
        public void DualInstances_StreamsIsolated()
        {
            FixtureBuilder.Ensure();
            string dll = Path.Combine(FixtureBuilder.FixtureDir, "FL_LlmSeg.dll");
            Assert.True(File.Exists(dll), "FL_LlmSeg.dll 缺失——FixtureBuilder 构建失败");
            DataBox.Reset();
            try
            {
                // [段1] 机制组装——FlowRunner 双实例注册（同 dll 双 FlowHandle）
                ThreadGuard guard = new ThreadGuard();
                OA oa = new OA(guard);
                CommandBus bus = new CommandBus(guard);
                IdAllocator ids = new IdAllocator();
                FlowRunner runner = new FlowRunner(guard, oa, bus, ids);
                StubLlmRuntime stub = new StubLlmRuntime();
                DataBox.Bind<ILlmRuntime>(stub);
                using (FlowHandle h1 = FlowHandle.Load(dll))
                using (FlowHandle h2 = FlowHandle.Load(dll))
                {
                    long id1 = runner.RegisterFlow(h1.Flow, "LlmSegA");
                    long id2 = runner.RegisterFlow(h2.Flow, "LlmSegB");
                    Assert.NotEqual(id1, id2);
                    // [段2] 预热帧——生成物构造注册信号（P_Go）
                    for (int w = 0; w < 3; w = w + 1)
                    {
                        runner.Tick();
                    }
                    // [段3] 双沿启动——P_Go 信号沿单消费语义：第一沿 A 消费（fire T_Exec 转 Streaming）；间隔数帧再发第二沿——A 已非 Idle 不响、B 消费启动（错峰保证 Stub 调用序：A 先 B 后）
                    DataBox.Signal("P_Go");
                    for (int g = 0; g < 3; g = g + 1)
                    {
                        runner.Tick();
                        Thread.Sleep(5);
                    }
                    DataBox.Signal("P_Go");
                    // [段4] 泵循环——主干串行 Tick 驱动，等待两实例 Done（帧上限 300 × 10ms = 3s）
                    bool aDone = false;
                    bool bDone = false;
                    for (int f = 3; f < 300; f = f + 1)
                    {
                        runner.Tick();
                        Thread.Sleep(10);
                        aDone = IsDone(h1);
                        bDone = IsDone(h2);
                        if (aDone && bDone)
                        {
                            break;
                        }
                    }
                    Assert.True(aDone, "实例 A 未 Done——流式链未闭环");
                    Assert.True(bDone, "实例 B 未 Done——流式链未闭环");
                    // [段5] 隔离断言——全局段键各读各的（互不覆盖核心断言）
                    string replyA;
                    string replyB;
                    Assert.True(DataBox.TryGet<string>("global", "llm_reply:" + id1.ToString(), out replyA), "实例 A 段键缺失");
                    Assert.True(DataBox.TryGet<string>("global", "llm_reply:" + id2.ToString(), out replyB), "实例 B 段键缺失");
                    Assert.Equal("REPLY_A_PART1REPLY_A_PART2REPLY_A_END", replyA);
                    Assert.Equal("REPLY_B_ONLY", replyB);
                    // 私有盒隔离（@reply by flowId scope——翻译器拼接）
                    string boxA;
                    string boxB;
                    Assert.True(DataBox.TryGet<string>(id1.ToString(), "reply", out boxA), "实例 A 私有盒缺失");
                    Assert.True(DataBox.TryGet<string>(id2.ToString(), "reply", out boxB), "实例 B 私有盒缺失");
                    Assert.Equal(replyA, boxA);
                    Assert.Equal(replyB, boxB);
                    // chunk 段键——A 最后分块 / B 单分块（交错后各自独立）
                    string chunkA;
                    string chunkB;
                    Assert.True(DataBox.TryGet<string>("global", "llm_chunk:" + id1.ToString(), out chunkA), "实例 A chunk 段缺失");
                    Assert.True(DataBox.TryGet<string>("global", "llm_chunk:" + id2.ToString(), out chunkB), "实例 B chunk 段缺失");
                    Assert.Equal("REPLY_A_END", chunkA);
                    Assert.Equal("REPLY_B_ONLY", chunkB);
                    // done 段键——两实例独立置位
                    string doneA;
                    string doneB;
                    Assert.True(DataBox.TryGet<string>("global", "llm_done:" + id1.ToString(), out doneA), "实例 A done 段缺失");
                    Assert.True(DataBox.TryGet<string>("global", "llm_done:" + id2.ToString(), out doneB), "实例 B done 段缺失");
                    Assert.Equal("1", doneA);
                    Assert.Equal("1", doneB);
                    // seen 段键——A 消费 3 分块（chunk_ready 盒子化后的消费计数）
                    long seenA;
                    Assert.True(DataBox.TryGet<long>("global", "llm_chunk_seen:" + id1.ToString(), out seenA), "实例 A seen 段缺失");
                    Assert.Equal(3, seenA);
                }
            }
            finally
            {
                DataBox.Reset();
            }
        }
    }
}