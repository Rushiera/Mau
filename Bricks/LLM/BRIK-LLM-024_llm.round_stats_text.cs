// ═══════════════════════════════════════════════════
// 积木: llm.round_stats_text
// ID:   BRIK-LLM-024
// 类别: LLM
// 作用: 轮次统计行文本——usage + 耗时 + 费用格式化（CH2 tokenInfo 移植）
// 依赖: 无
// 包: 无
// 引用: Mau.Runtime
// 原理: LlmStreamSession.Usage*（usage 解析）+ FinishedTimestamp → 文本 "↑K ↓K [miss: K (🎯% ⌛秒 💰¥)]"
// 常用: TalkCat 语料 T_StatsBuild——正常完成后生成统计行入上下文（UI ColStats 着色）
// ═══════════════════════════════════════════════════
using Mau.Runtime;

namespace Mau.Bricks
{
    /// <summary>
    /// LLM 积木——llm.round_stats_text 轮次统计行（依赖 LlmSession；CH2 tokenInfo 移植 2026-08-10）
    /// </summary>
    public static class LlmRoundStatsTextBrick
    {
        /// <summary>
        /// 生成轮次统计行——"↑prompt ↓completion [miss: X (🎯Y% ⌛Z 💰W)]"
        /// </summary>
        /// <param name="requestId">会话 ID</param>
        /// <param name="sessionKey">会话 Key（未使用——保留对称签名）</param>
        /// <param name="text">统计行文本（会话不存在=空——T_Finish 已移除会话，统计跳过不失败）</param>
        /// <returns>true=成功（会话不存在也返回 true——统计缺失不阻塞流程）</returns>
        public static bool RoundStatsText(string requestId, string sessionKey, out string text)
        {
            text = "";
            LlmStreamSession? session = LlmSession.FindSession(requestId);
            if (session == null)
            {
                // 会话已被 finish 移除——统计缺失，不失败（空文本由 ctx_push_system 跳过）
                return true;
            }
            long prompt = session.UsagePrompt;
            long completion = session.UsageCompletion;
            long cacheHit = session.UsageCacheHit;
            long miss = prompt - cacheHit;
            if (miss < 0) { miss = 0; }
            double hitRate = prompt > 0 ? (double)cacheHit / (double)prompt * 100.0 : 0.0;
            double elapsed = 0.0;
            if (session.FinishedTimestamp > 0)
            {
                elapsed = (double)(session.FinishedTimestamp - session.StartTimestamp) / System.Diagnostics.Stopwatch.Frequency;
            }
            string stats = "↑" + FormatTokenCount(prompt)
                + "  ↓" + FormatTokenCount(completion)
                + "   [ miss: " + FormatTokenCount(miss)
                + " (🎯" + hitRate.ToString("F1") + "%"
                + "  ⌛" + FormatDuration(elapsed)
                + "  💰" + CalcBilling(prompt, completion, cacheHit)
                + " ) ]";
            text = stats;
            return true;
        }

        /// <summary>
        /// 格式化 token 数——≥1M→M，≥1K→K，保留一位小数
        /// </summary>
        private static string FormatTokenCount(long tokens)
        {
            if (tokens >= 1000000) { return (tokens / 1000000.0).ToString("F1") + " M"; }
            if (tokens >= 1000) { return (tokens / 1000.0).ToString("F1") + " K"; }
            return tokens.ToString();
        }

        /// <summary>
        /// 格式化耗时——≥60 秒→x分x.x秒，否则 x.x 秒
        /// </summary>
        private static string FormatDuration(double seconds)
        {
            if (seconds >= 60.0)
            {
                int mins = (int)(seconds / 60.0);
                double secs = seconds % 60.0;
                return mins.ToString() + "分" + secs.ToString("F1") + "秒";
            }
            return seconds.ToString("F1") + "秒";
        }

        /// <summary>
        /// DeepSeek 计费（CNY）——flash：输入 1 元/百万、命中 0.02、输出 2；pro/默认：3/0.025/6
        /// </summary>
        private static string CalcBilling(long promptTokens, long completionTokens, long cacheHitTokens)
        {
            double priceMiss = 1.0;
            double priceHit = 0.02;
            double priceOutput = 2.0;
            long missed = promptTokens - cacheHitTokens;
            if (missed < 0) { missed = 0; }
            double cost = (missed / 1000000.0) * priceMiss
                + (cacheHitTokens / 1000000.0) * priceHit
                + (completionTokens / 1000000.0) * priceOutput;
            if (cost >= 0.01) { return "¥ " + cost.ToString("F3"); }
            if (cost >= 0.0001) { return "¥ " + cost.ToString("F5"); }
            return " ¥ <0.0001";
        }
    }
}
// #MAU_CHECKSUM:SHA256:2FCC7BC60F307C11B10EE47187602E4FD62A14D36A1C5BC3C73C4867D0ECE52C
