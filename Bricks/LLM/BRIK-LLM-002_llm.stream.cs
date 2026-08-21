// ═══════════════════════════════════════════════════
// 积木: llm.stream
// ID:   BRIK-LLM-002
// 类别: LLM
// 作用: 流式启动器——后台消费 ChatStream（单轮 messages），增量写本实例盒段（llm_chunk:<f>/llm_chunk_count:<f>/llm_reply:<f>/llm_done:<f>）+ LogStore 结算行落盘（D5：分片不进日志——实时数据面在盒）
// 依赖: 无
// 引用: Mau.Runtime（ILlmRuntime/DataBox/LogStore/LlmStreamEvent/FlowContext）
// 原理: TryResolve<ILlmRuntime> → 主线程捕获 FlowId（ThreadStatic 不跨后台线程）→ Task.Run 后台消费 → 增量/完成写本实例段盒（B1 豁免：全局盒写源在语料外）
// 盒子: 本实例段 llm_chunk:<f>(最新增量) / llm_chunk_count:<f>(分片序号) / llm_reply:<f>(完整回复或 ERR|) / llm_done:<f>("1"=完成)——P9.2 Key 段化（多实例隔离）
// 常用: CH4 P4 quick_cat 语料——流式执行体（开始信号 = 本积木调用成功）
// 注意: 后台线程写 DataBox（scope 级并发安全）；LogStore 结算行 = 后台写（帧号取当时全局帧）
// 注意: 启动时失效调用方语料的探测盒 @hasDone/@hasChunk（配套约定）——会话边界信号清理（Remove 而非 Set false）
// 注意: 启动清理本实例段——count=0 / done 清空 / seen Remove（chunk_ready 归零检测）；chunk/reply 覆盖写留存（历史最后状态观测可见）
// ═══════════════════════════════════════════════════
using System;
using System.Text;
using System.Threading.Tasks;
using Mau.Runtime;

namespace Mau.Bricks
{
    /// <summary>
    /// LLM 积木——llm.stream 流式启动器（薄壳转发 ILlmRuntime.ChatStream 单轮 + 全局盒桥）
    /// </summary>
    public static class LlmStreamBrick
    {
        /// <summary>
        /// 流式启动——后台消费流式增量写全局盒；立即返回（开始信号）
        /// </summary>
        /// <param name="system">系统提示词</param>
        /// <param name="content">用户内容</param>
        /// <param name="reply">占位（""）——完成文本经全局盒 llm_reply 取</param>
        /// <returns>true=启动成功</returns>
        public static bool StreamStart(string system, string content, out string reply)
        {
            reply = "";
            ILlmRuntime? runtime;
            if (!DataBox.TryResolve<ILlmRuntime>(out runtime) || runtime == null)
            {
                return false;
            }
            // P9.2 Key 段化——新流式会话重置本实例盒段（计数归零 + 完成标志清空 + seen 归零——llm.chunk_ready 探测以 0 为起点）
            long flowId = FlowContext.CurrentFlowId;
            if (flowId > 0)
            {
                string seg = flowId.ToString();
                DataBox.Set<long>("global", "llm_chunk_count:" + seg, 0);
                DataBox.Set<string>("global", "llm_done:" + seg, "");
                DataBox.Remove("global", "llm_chunk_seen:" + seg);
                // 失效语料探测盒（@hasDone/@hasChunk——llm.stream 配套语料约定）——防止上一会话残留信号在同帧误触发
                // 语义：Remove 而非 Set(false)——盒不存在 = 未采样 = 条件天然 false（DataBox 失效原语）
                DataBox.Remove(seg, "hasDone");
                DataBox.Remove(seg, "hasChunk");
            }
            // 主线程启动后台消费（fire-and-forget——内部全 try-catch 兜底；lambda 内返回值显式丢弃——CS4014 消警）
            System.Threading.Tasks.Task.Run(delegate
            {
                _ = ConsumeStream(runtime, system, content, flowId);
            });
            return true;
        }

        /// <summary>
        /// 后台消费流——增量写本实例段盒 + LogStore 结算行落盘（STREAM|chunks|t|r|chars）；完成/错误置 llm_done:<f>
        /// </summary>
        /// <param name="runtime">LLM 运行时</param>
        /// <param name="system">系统提示词</param>
        /// <param name="content">用户内容</param>
        /// <param name="flowId">实例 ID（入口主线程捕获——后台线程不依赖 FlowContext）</param>
        private static async System.Threading.Tasks.Task ConsumeStream(ILlmRuntime runtime, string system, string content, long flowId)
        {
            StringBuilder full = new StringBuilder();
            long count = 0;
            long textCount = 0;
            long reasonCount = 0;
            string seg = flowId.ToString();
            try
            {
                LlmMessage[] messages = new LlmMessage[2];
                messages[0] = new LlmMessage { Role = LlmRole.System, Content = system };
                messages[1] = new LlmMessage { Role = LlmRole.User, Content = content };
                await foreach (LlmStreamEvent ev in runtime.ChatStream(messages, new ToolSpec[0]))
                {
                    if (ev.Kind == LlmStreamKind.Text || ev.Kind == LlmStreamKind.Reasoning)
                    {
                        // [段1] 增量——累积 + 本实例段盒覆盖写（实时数据面在盒——分片不进日志，D5；P9.2 Key 段化隔离实例）
                        full.Append(ev.Text);
                        count = count + 1;
                        DataBox.Set<string>("global", "llm_chunk:" + seg, ev.Text);
                        DataBox.Set<long>("global", "llm_chunk_count:" + seg, count);
                        if (ev.Kind == LlmStreamKind.Text)
                        {
                            textCount = textCount + 1;
                        }
                        else
                        {
                            reasonCount = reasonCount + 1;
                        }
                    }
                    else if (ev.Kind == LlmStreamKind.Done)
                    {
                        // [段2] 完成——完整回复落段盒 + 完成标志
                        DataBox.Set<string>("global", "llm_reply:" + seg, full.ToString());
                        DataBox.Set<string>("global", "llm_done:" + seg, "1");
                        LogStore.Add("LLM", 0, "STREAM|chunks=" + count.ToString() + "|t=" + textCount.ToString() + "|r=" + reasonCount.ToString() + "|chars=" + full.Length.ToString(), "LLM");
                        return;
                    }
                    else if (ev.Kind == LlmStreamKind.Error)
                    {
                        // [段3] 失败——错误文本落段盒 + 完成标志（失败可见性：ERR| 前缀）
                        DataBox.Set<string>("global", "llm_reply:" + seg, ev.Text);
                        DataBox.Set<string>("global", "llm_done:" + seg, "1");
                        LogStore.Add("LLM", 3, "STREAM_ERROR|" + TrimText(ev.Text, 200), "LLM");
                        return;
                    }
                }
                // [段4] 流意外结束（translate 保证 Done/Error——兜底防御）
                DataBox.Set<string>("global", "llm_reply:" + seg, "ERR|STREAM_END|流意外结束");
                DataBox.Set<string>("global", "llm_done:" + seg, "1");
            }
            catch (Exception ex)
            {
                // [段5] 异常兜底——错误可见性
                DataBox.Set<string>("global", "llm_reply:" + seg, "ERR|" + ex.GetType().Name + "|" + ex.Message);
                DataBox.Set<string>("global", "llm_done:" + seg, "1");
                LogStore.Add("LLM", 3, "STREAM_EXCEPTION|" + ex.GetType().Name + "|" + TrimText(ex.Message, 200), "LLM");
            }
        }

        /// <summary>
        /// 截断日志文本——防刷屏
        /// </summary>
        /// <param name="text">原文</param>
        /// <param name="max">上限</param>
        /// <returns>截断文本</returns>
        private static string TrimText(string text, int max)
        {
            if (text == null)
            {
                return "";
            }
            if (text.Length <= max)
            {
                return text;
            }
            return text.Substring(0, max) + "...";
        }
    }
}
// #MAU_CHECKSUM:SHA256:2793F56960B01839280885EDE18C15636A49F08A2A63168E4DFB131CF6164043
