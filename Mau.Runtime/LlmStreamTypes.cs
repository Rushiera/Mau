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
        /// 构造带超时的会话
        /// </summary>
        /// <param name="timeout">请求超时</param>
        public LlmStreamSession(System.TimeSpan timeout)
        {
            Cancel = new CancellationTokenSource(timeout);
            Worker = null;
            IsTerminal = 0;
            LastChunk = null;
        }
    }

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
