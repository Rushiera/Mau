using System;
using Xunit;

namespace Mau.Providers.Tests
{
    /// <summary>
    /// SseFrameProbe 结构摘要测试——usage 帧位置三形态判定（finish-frame / usage-only-frame / both / absent）+ 帧计数面。
    /// 覆盖：opencode zen v4.1 同帧 / DeepSeek 官方独立尾帧 / Foldin 双发 / 无 usage / 畸形帧 / [DONE] 余帧 / 思考与工具形态。
    /// </summary>
    public class SseFrameProbeTests
    {
        /// <summary>
        /// 同帧形态——delta 与 usage 同帧（opencode zen v4.1）→ finish-frame。
        /// </summary>
        [Fact]
        public void FinishFrameUsage_ReportsFinishFrame()
        {
            string[] frames = new string[] {
                "{\"choices\":[{\"index\":0,\"delta\":{\"content\":\"hi\"}}]}",
                "{\"choices\":[{\"index\":0,\"delta\":{},\"finish_reason\":\"stop\"}],\"usage\":{\"prompt_tokens\":11,\"completion_tokens\":7,\"prompt_cache_hit_tokens\":3}}",
                "[DONE]"
            };
            string json = SseFrameProbe.Describe(frames, 200, "endpoint", "model");
            Assert.Contains("\"usageLocation\":\"finish-frame\"", json, StringComparison.Ordinal);
            Assert.Contains("\"usageFrames\":1", json, StringComparison.Ordinal);
            Assert.Contains("\"usageValueSeen\":true", json, StringComparison.Ordinal);
            Assert.Contains("\"usagePrompt\":11", json, StringComparison.Ordinal);
            Assert.Contains("\"usageCompletion\":7", json, StringComparison.Ordinal);
        }

        /// <summary>
        /// 独立尾帧形态——choices 空数组 + usage 对象（DeepSeek 官方）→ usage-only-frame。
        /// </summary>
        [Fact]
        public void UsageOnlyFrame_ReportsUsageOnlyFrame()
        {
            string[] frames = new string[] {
                "{\"choices\":[{\"index\":0,\"delta\":{\"content\":\"hi\"}}]}",
                "{\"choices\":[],\"usage\":{\"prompt_tokens\":20,\"completion_tokens\":5}}",
                "[DONE]"
            };
            string json = SseFrameProbe.Describe(frames, 200, "endpoint", "model");
            Assert.Contains("\"usageLocation\":\"usage-only-frame\"", json, StringComparison.Ordinal);
            Assert.Contains("\"emptyChoicesFrames\":1", json, StringComparison.Ordinal);
            Assert.Contains("\"usageOnlyFrameCount\":1", json, StringComparison.Ordinal);
        }

        /// <summary>
        /// 双发形态——同帧与独立尾帧各带一份（Foldin）→ both。
        /// </summary>
        [Fact]
        public void BothFrameForms_ReportsBoth()
        {
            string[] frames = new string[] {
                "{\"choices\":[{\"index\":0,\"delta\":{},\"finish_reason\":\"stop\"}],\"usage\":{\"prompt_tokens\":30,\"completion_tokens\":9}}",
                "{\"choices\":[],\"usage\":{\"prompt_tokens\":30,\"completion_tokens\":9}}",
                "[DONE]"
            };
            string json = SseFrameProbe.Describe(frames, 200, "endpoint", "model");
            Assert.Contains("\"usageLocation\":\"both\"", json, StringComparison.Ordinal);
            Assert.Contains("\"usageFrames\":2", json, StringComparison.Ordinal);
            Assert.Contains("\"finishFrameUsageCount\":1", json, StringComparison.Ordinal);
            Assert.Contains("\"usageOnlyFrameCount\":1", json, StringComparison.Ordinal);
        }

        /// <summary>
        /// 无 usage 帧——整段无 usage 字段 → absent，数值面保持零。
        /// </summary>
        [Fact]
        public void NoUsage_ReportsAbsent()
        {
            string[] frames = new string[] {
                "{\"choices\":[{\"index\":0,\"delta\":{\"content\":\"hi\"}}]}",
                "[DONE]"
            };
            string json = SseFrameProbe.Describe(frames, 200, "endpoint", "model");
            Assert.Contains("\"usageLocation\":\"absent\"", json, StringComparison.Ordinal);
            Assert.Contains("\"usageFrames\":0", json, StringComparison.Ordinal);
            Assert.Contains("\"usageValueSeen\":false", json, StringComparison.Ordinal);
        }

        /// <summary>
        /// 畸形帧——非法 JSON 计入 malformedFrames，不抛异常。
        /// </summary>
        [Fact]
        public void MalformedFrame_CountedNotThrown()
        {
            string[] frames = new string[] {
                "{oops",
                "{\"choices\":[{\"index\":0,\"delta\":{\"content\":\"hi\"}}]}",
                "[DONE]"
            };
            string json = SseFrameProbe.Describe(frames, 200, "endpoint", "model");
            Assert.Contains("\"malformedFrames\":1", json, StringComparison.Ordinal);
            Assert.Contains("\"doneSeen\":true", json, StringComparison.Ordinal);
        }

        /// <summary>
        /// [DONE] 后余帧——计 trailingAfterDone（[DONE] 即终止语义的观测面）。
        /// </summary>
        [Fact]
        public void TrailingAfterDone_Counted()
        {
            string[] frames = new string[] {
                "[DONE]",
                "{\"choices\":[],\"usage\":{\"prompt_tokens\":1,\"completion_tokens\":1}}"
            };
            string json = SseFrameProbe.Describe(frames, 200, "endpoint", "model");
            Assert.Contains("\"trailingAfterDone\":1", json, StringComparison.Ordinal);
        }

        /// <summary>
        /// 思考与工具形态——reasoning_content 与 tool_calls 增量被标记。
        /// </summary>
        [Fact]
        public void ReasoningAndToolCalls_Flagged()
        {
            string[] frames = new string[] {
                "{\"choices\":[{\"index\":0,\"delta\":{\"reasoning_content\":\"想\"}}]}",
                "{\"choices\":[{\"index\":0,\"delta\":{\"tool_calls\":[{\"index\":0,\"id\":\"t1\",\"type\":\"function\",\"function\":{\"name\":\"f\",\"arguments\":\"{}\"}}]}}]}",
                "[DONE]"
            };
            string json = SseFrameProbe.Describe(frames, 200, "endpoint", "model");
            Assert.Contains("\"reasoningSeen\":true", json, StringComparison.Ordinal);
            Assert.Contains("\"toolCallsSeen\":true", json, StringComparison.Ordinal);
        }

        /// <summary>
        /// OpenAI 兼容缓存字段——prompt_tokens_details.cached_tokens 读为 cacheHit。
        /// </summary>
        [Fact]
        public void OpenAiCachedTokensDetails_ReadAsCacheHit()
        {
            string[] frames = new string[] {
                "{\"choices\":[],\"usage\":{\"prompt_tokens\":50,\"completion_tokens\":4,\"prompt_tokens_details\":{\"cached_tokens\":12}}}",
                "[DONE]"
            };
            string json = SseFrameProbe.Describe(frames, 200, "endpoint", "model");
            Assert.Contains("\"usageCacheHit\":12", json, StringComparison.Ordinal);
        }
    }
}
