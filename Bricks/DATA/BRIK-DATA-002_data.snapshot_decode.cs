// ═══════════════════════════════════════════════════
// 积木: data.snapshot_decode
// ID:   BRIK-DATA-002
// 类别: DATA
// 作用: 把分节文本解码为独立字典和行列表
// 依赖: 无
// 引用: System · System.Collections.Generic
// 原理: 换行归一化 → 逐行解析（节标题切换/内容转义还原）→ 重复节拒绝
// 常用: 结构化文本读取 / 快照恢复
// ═══════════════════════════════════════════════════
using System;
using System.Collections.Generic;

using Mau.Runtime;
namespace Mau.Bricks
{
    /// <summary>
    /// 数据积木——data.snapshot_decode 解码分节文本（依赖 SnapshotFormat）
    /// </summary>
    public static class DataSnapshotDecodeBrick
    {
        /// <summary>
        /// 把分节文本解码为独立字典和行列表
        /// </summary>
        /// <param name="raw">分节文本</param>
        /// <param name="sections">解码结果——空输入返回空字典</param>
        /// <returns>true=成功</returns>
        public static bool SnapshotDecode(string? raw, out Dictionary<string, List<string>> sections)
        {
            Dictionary<string, List<string>> result =
                new Dictionary<string, List<string>>(StringComparer.Ordinal);
            sections = result;
            if (string.IsNullOrEmpty(raw))
            {
                return true;
            }
            string normalized = raw.Replace("\r\n", "\n").Replace('\r', '\n');
            string[] lines = normalized.Split('\n');
            string currentSection = "";
            List<string>? currentLines = null;
            for (int i = 0; i < lines.Length; i = i + 1)
            {
                string line = lines[i];
                if (SnapshotFormat.IsSectionHeader(line))
                {
                    if (currentLines != null)
                    {
                        result.Add(currentSection, currentLines);
                    }
                    currentSection = line.Substring(1, line.Length - 2);
                    SnapshotFormat.ValidateSectionName(currentSection);
                    if (result.ContainsKey(currentSection))
                    {
                        throw new FormatException("Duplicate snapshot section: " + currentSection);
                    }
                    currentLines = new List<string>();
                }
                else
                {
                    if (currentLines == null)
                    {
                        throw new FormatException("Snapshot content appears before the first section.");
                    }
                    currentLines.Add(SnapshotFormat.UnescapeLine(line));
                }
            }
            if (currentLines != null)
            {
                result.Add(currentSection, currentLines);
            }
            sections = result;
            return true;
        }
    }
}
// #MAU_CHECKSUM:SHA256:2516B097C68B076EA71987C367C2A9844110B12B77A8433A7E3F84CC39788A15
