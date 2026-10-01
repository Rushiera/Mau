using System.Collections.Generic;

namespace CatHome4.QQ
{
    /// <summary>
    /// /last 发送计划（A112）——正文末尾优先取段 + 文本段与文件发送共用同一被动调用预算。
    /// 动机：`/last` 原实现只切分文本、不扫描文件标记——标记原文被当普通文本发出（判例 2026-09-30 00:06:37）；
    /// 官方被动回复上限按消息计（4 次），文本段与文件发送须共用同一预算（与转发路同口径）。
    /// 纯函数——零状态零 IO。
    /// </summary>
    internal static class QqLastReplyPlanner
    {
        /// <summary>
        /// 生成发送计划——正文按 MD 结构切分（QqTextSplitter），段数超上限取末尾段（丢弃开头段）；
        /// 剩余额度分配给文件（超出部分丢弃）。执行序：文本段在前、文件在后。
        /// </summary>
        /// <param name="body">正文（已剥离文件标记；空=无文本段）</param>
        /// <param name="files">待发送文件路径（已剥离；空=无文件）</param>
        /// <param name="maxChunkChars">单段字符上限</param>
        /// <param name="maxCalls">被动调用上限（文本段 + 文件共用）</param>
        /// <returns>发送计划——段 / 文件 / 丢弃计数</returns>
        internal static QqLastReplyPlan Plan(string body, List<string> files, int maxChunkChars, int maxCalls)
        {
            QqLastReplyPlan plan = new QqLastReplyPlan();
            // [段1] 正文切分——超上限取末尾段（/last 语义：补感知，尾部最新）
            if (body != null && body.Length > 0)
            {
                List<string> parts = QqTextSplitter.Split(body, maxChunkChars);
                int from = 0;
                if (parts.Count > maxCalls)
                {
                    from = parts.Count - maxCalls;
                    plan.DroppedHeadSegments = from;
                }
                for (int i = from; i < parts.Count; i = i + 1)
                {
                    plan.Segments.Add(parts[i]);
                }
            }
            // [段2] 文件用剩余额度——文本段与文件共用被动调用上限（官方按消息计 4 次）
            if (files != null && files.Count > 0)
            {
                int remain = maxCalls - plan.Segments.Count;
                if (remain < 0)
                {
                    remain = 0;
                }
                for (int i = 0; i < files.Count; i = i + 1)
                {
                    if (i < remain)
                    {
                        plan.Files.Add(files[i]);
                    }
                    else
                    {
                        plan.DroppedFiles = plan.DroppedFiles + 1;
                    }
                }
            }
            return plan;
        }
    }

    /// <summary>
    /// /last 发送计划——文本段（末尾优先）+ 文件（剩余额度）+ 丢弃计数（L2 留痕用；A112）。
    /// </summary>
    internal sealed class QqLastReplyPlan
    {
        /// <summary>待发文本段——按发送顺序（末尾优先策略已应用）</summary>
        public List<string> Segments = new List<string>();

        /// <summary>待发文件路径——按出现顺序（受剩余额度约束）</summary>
        public List<string> Files = new List<string>();

        /// <summary>因段数超上限丢弃的开头段数（0=未丢）</summary>
        public int DroppedHeadSegments;

        /// <summary>因预算耗尽丢弃的文件数（0=未丢）</summary>
        public int DroppedFiles;
    }
}
