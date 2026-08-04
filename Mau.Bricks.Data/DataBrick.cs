// ═══════════════════════════════════════════════
// 积木: data.snapshot_encode / data.snapshot_decode
// ID:   BRIK-DATA-001 ~ 002
// 作用: 分节文本（Snapshot 格式）编码/解码——`[Section]` 结构的有序持久化
// 引用: Mau.Bricks.Data → Mau.Contracts（BrickRegistry）
// 原理: 手写分节解析器——标题形态转义（\ 前缀）、空行保护、节名校验、重复节拒绝
// 常用: Console 快照序列化 / 结构化文本持久化 / 调试面板数据
// ═══════════════════════════════════════════════
using System;
using System.Collections.Generic;
using System.Text;
using Mau.Contracts;

namespace Mau.Bricks
{
    /// <summary>
    /// 数据积木——标准积木库数据类。分节文本（Snapshot）编码/解码。
    /// </summary>
    public static class DataBrick
    {
        /// <summary>
        /// 把有序字典编码为 `[Section]` 分节文本
        /// </summary>
        /// <param name="sections">分节和行列表</param>
        /// <returns>分节文本；空字典返回空字符串</returns>
        public static string SnapshotEncode(Dictionary<string, List<string>>? sections)
        {
            if (sections == null || sections.Count == 0)
            {
                return "";
            }
            StringBuilder builder = new StringBuilder();
            int sectionIndex = 0;
            foreach (KeyValuePair<string, List<string>> pair in sections)
            {
                ValidateSectionName(pair.Key);
                if (sectionIndex > 0)
                {
                    builder.Append('\n');
                }
                builder.Append('[');
                builder.Append(pair.Key);
                builder.Append(']');
                List<string>? lines = pair.Value;
                if (lines != null)
                {
                    for (int i = 0; i < lines.Count; i = i + 1)
                    {
                        string line = SafeText(lines[i]);
                        if (line.IndexOf('\n') >= 0 || line.IndexOf('\r') >= 0)
                        {
                            throw new ArgumentException("Snapshot line cannot contain a newline.", "sections");
                        }
                        builder.Append('\n');
                        AppendEscapedLine(builder, line);
                    }
                }
                sectionIndex = sectionIndex + 1;
            }
            return builder.ToString();
        }

        /// <summary>
        /// 把分节文本解码为独立字典和行列表
        /// </summary>
        /// <param name="raw">分节文本</param>
        /// <returns>解码结果</returns>
        public static Dictionary<string, List<string>> SnapshotDecode(string? raw)
        {
            Dictionary<string, List<string>> result =
                new Dictionary<string, List<string>>(StringComparer.Ordinal);
            if (string.IsNullOrEmpty(raw))
            {
                return result;
            }
            string normalized = raw.Replace("\r\n", "\n").Replace('\r', '\n');
            string[] lines = normalized.Split('\n');
            string currentSection = "";
            List<string>? currentLines = null;
            for (int i = 0; i < lines.Length; i = i + 1)
            {
                string line = lines[i];
                if (IsSectionHeader(line))
                {
                    if (currentLines != null)
                    {
                        result.Add(currentSection, currentLines);
                    }
                    currentSection = line.Substring(1, line.Length - 2);
                    ValidateSectionName(currentSection);
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
                    currentLines.Add(UnescapeLine(line));
                }
            }
            if (currentLines != null)
            {
                result.Add(currentSection, currentLines);
            }
            return result;
        }

        /// <summary>
        /// 追加一行并保护标题形态、前导反斜线和空行
        /// </summary>
        /// <param name="builder">输出构造器</param>
        /// <param name="line">原始行</param>
        private static void AppendEscapedLine(StringBuilder builder, string line)
        {
            if (line.Length == 0)
            {
                builder.Append('\\');
                return;
            }
            if (line[0] == '\\' || IsSectionHeader(line))
            {
                builder.Append('\\');
            }
            builder.Append(line);
        }

        /// <summary>
        /// 移除由序列化器添加的一层反斜线
        /// </summary>
        /// <param name="line">编码行</param>
        /// <returns>原始行</returns>
        private static string UnescapeLine(string line)
        {
            if (line == "\\")
            {
                return "";
            }
            if (line.Length > 1 && line[0] == '\\')
            {
                string remainder = line.Substring(1);
                if (remainder[0] == '\\' || IsSectionHeader(remainder))
                {
                    return remainder;
                }
            }
            return line;
        }

        /// <summary>
        /// 判断整行是否为节标题
        /// </summary>
        /// <param name="line">文本行</param>
        /// <returns>是否为标题</returns>
        private static bool IsSectionHeader(string line)
        {
            return line.Length >= 3 && line[0] == '[' && line[line.Length - 1] == ']';
        }

        /// <summary>
        /// 拒绝会破坏格式边界的节名
        /// </summary>
        /// <param name="sectionName">节名</param>
        private static void ValidateSectionName(string sectionName)
        {
            if (string.IsNullOrWhiteSpace(sectionName)
                || sectionName.IndexOf('[') >= 0 || sectionName.IndexOf(']') >= 0
                || sectionName.IndexOf('\n') >= 0 || sectionName.IndexOf('\r') >= 0)
            {
                throw new ArgumentException("Snapshot section name is invalid.", "sectionName");
            }
        }

        /// <summary>
        /// 把可空文本规范为空字符串
        /// </summary>
        /// <param name="value">输入</param>
        /// <returns>非空文本</returns>
        private static string SafeText(string? value)
        {
            if (value == null)
            {
                return "";
            }
            return value;
        }
    }

    /// <summary>
    /// 数据积木注册——进程启动时调用一次
    /// </summary>
    public static class DataBrickRegistration
    {
        /// <summary>
        /// 注册全部数据积木
        /// </summary>
        public static void RegisterAll()
        {
            RegisterSnapshotEncode();
            RegisterSnapshotDecode();
        }

        /// <summary>
        /// 注册 data.snapshot_encode
        /// </summary>
        private static void RegisterSnapshotEncode()
        {
            BrickContract contract = new BrickContract("data.snapshot_encode", "Mau.Bricks.DataBrick.SnapshotEncode");
            contract.Inputs.Add(new BrickPort("sections", typeof(Dictionary<string, List<string>>), "分节字典"));
            contract.Outputs.Add(new BrickPort("encoded", typeof(string), "分节文本"));
            contract.Return = BrickReturnKind.Bool;
            contract.Duration = BrickDuration.Sync;
            contract.Thread = "main";
            BrickRegistry.Register(contract);
        }

        /// <summary>
        /// 注册 data.snapshot_decode
        /// </summary>
        private static void RegisterSnapshotDecode()
        {
            BrickContract contract = new BrickContract("data.snapshot_decode", "Mau.Bricks.DataBrick.SnapshotDecode");
            contract.Inputs.Add(new BrickPort("raw", typeof(string), "分节文本"));
            contract.Outputs.Add(new BrickPort("sections", typeof(Dictionary<string, List<string>>), "分节字典"));
            contract.Return = BrickReturnKind.Bool;
            contract.Duration = BrickDuration.Sync;
            contract.Thread = "main";
            BrickRegistry.Register(contract);
        }
    }
}
