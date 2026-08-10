using System.Collections.Concurrent;
using System.Threading;

namespace Mau.Runtime
{
    /// <summary>
    /// 流式会话——单次 llm.stream/completions 的后台状态。后台线程只写队列，消费线程只读队列。
    /// </summary>
    public sealed class LlmStreamSession
    {
        /// <summary>
        /// 分片队列——后台写入，消费端读取
        /// </summary>
        public readonly ConcurrentQueue<LlmStreamChunk> Chunks = new ConcurrentQueue<LlmStreamChunk>();

        /// <summary>
        /// 取消与超时源
        /// </summary>
        public readonly CancellationTokenSource Cancel;

        /// <summary>
        /// 后台任务句柄——Finish 时等待
        /// </summary>
        public System.Threading.Tasks.Task? Worker;

        /// <summary>
        /// 是否已入队终态分片——保证只提交一个终态
        /// </summary>
        public int IsTerminal;

        /// <summary>
        /// 最后消费的分片——判断积木（is_end/is_tool）的查询源
        /// </summary>
        public LlmStreamChunk? LastChunk;

        /// <summary>
        /// 本轮累计正文（content）字符数——空回复检测源（llm.content_empty；2026-08-10 续传链路）
        /// </summary>
        public long ContentChars;

        /// <summary>
        /// 本轮 prompt token（usage 解析——轮次统计 llm.round_stats_text；2026-08-10）
        /// </summary>
        public long UsagePrompt;

        /// <summary>
        /// 本轮 completion token（usage 解析）
        /// </summary>
        public long UsageCompletion;

        /// <summary>
        /// 本轮缓存命中 token（usage 解析）
        /// </summary>
        public long UsageCacheHit;

        /// <summary>
        /// 请求起始时间戳（Stopwatch——耗时统计基准）
        /// </summary>
        public long StartTimestamp;

        /// <summary>
        /// 请求结束时间戳（Stopwatch——耗时统计）
        /// </summary>
        public long FinishedTimestamp;
/// <summary>
/// 创建时间（UTC）——活跃会话查询（GAP.7 sys.llm）的时间轴
/// </summary>
public readonly System.DateTime CreatedAt;
        /// <summary>
        /// 构造带超时的会话
        /// </summary>
        /// <param name="timeout">请求超时</param>
        public LlmStreamSession(System.TimeSpan timeout)
{
            Cancel = new CancellationTokenSource(timeout);
            Worker = null;
            IsTerminal = 0;
            LastChunk = null;
            CreatedAt = System.DateTime.UtcNow;
            ContentChars = 0;
            UsagePrompt = 0;
            UsageCompletion = 0;
            UsageCacheHit = 0;
            StartTimestamp = System.Diagnostics.Stopwatch.GetTimestamp();
            FinishedTimestamp = 0;
        }    }

    /// <summary>
    /// 流式分片——单次 read_chunk 的载荷
    /// </summary>
    public sealed class LlmStreamChunk
    {
        /// <summary>
        /// 正文增量
        /// </summary>
        public string ContentDelta;

        /// <summary>
        /// 推理增量
        /// </summary>
        public string ReasoningDelta;

        /// <summary>
        /// 工具调用 JSON（终态分片携带聚合结果）
        /// </summary>
        public string ToolCallsJson;

        /// <summary>
        /// 终态标志——true=流结束（成功或失败）
        /// </summary>
        public bool Finished;

        /// <summary>
        /// 错误码——终态且失败时非空
        /// </summary>
        public string ErrorCode;

        /// <summary>
        /// 错误摘要
        /// </summary>
        public string ErrorMessage;

        /// <summary>
        /// 构造分片
        /// </summary>
        public LlmStreamChunk()
        {
            ContentDelta = "";
            ReasoningDelta = "";
            ToolCallsJson = "";
            Finished = false;
            ErrorCode = "";
            ErrorMessage = "";
        }
    }
}
