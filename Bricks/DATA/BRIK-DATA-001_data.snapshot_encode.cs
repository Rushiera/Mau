// ═══════════════════════════════════════════════════
// 积木: data.snapshot_encode
// ID:   BRIK-DATA-001
// 类别: DATA
// 作用: 把有序字典编码为 `[Section]` 分节文本
// 依赖: 无
// 引用: System · System.Collections.Generic · System.Text
// 原理: 遍历分节字典 → 节名校验 → 行转义（标题形态/前导反斜线/空行）→ 拼接
// 常用: Console 快照序列化 / 结构化文本持久化
// ═══════════════════════════════════════════════════
using System;
using System.Collections.Generic;
using System.Text;

using Mau.Runtime;
namespace Mau.Bricks
{
    /// <summary>
    /// 数据积木——data.snapshot_encode 编码分节文本（依赖 SnapshotFormat）
    /// </summary>
    public static class DataSnapshotEncodeBrick
    {
        /// <summary>
        /// 把有序字典编码为 `[Section]` 分节文本
        /// </summary>
        /// <param name="sections">分节和行列表</param>
        /// <param name="encoded">分节文本；空字典返回空字符串</param>
        /// <returns>true=成功</returns>
        public static bool SnapshotEncode(Dictionary<string, List<string>>? sections, out string encoded)
        {
            if (sections == null || sections.Count == 0)
            {
                encoded = "";
                return true;
            }
            StringBuilder builder = new StringBuilder();
            int sectionIndex = 0;
            foreach (KeyValuePair<string, List<string>> pair in sections)
            {
                SnapshotFormat.ValidateSectionName(pair.Key);
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
                        string line = SnapshotFormat.SafeText(lines[i]);
                        if (line.IndexOf('\n') >= 0 || line.IndexOf('\r') >= 0)
                        {
                            throw new ArgumentException("Snapshot line cannot contain a newline.", "sections");
                        }
                        builder.Append('\n');
                        SnapshotFormat.AppendEscapedLine(builder, line);
                    }
                }
                sectionIndex = sectionIndex + 1;
            }
            encoded = builder.ToString();
            return true;
        }
    }
}
// #MAU_CHECKSUM:SHA256:540A235C81FE4994D823825C9E157F62515B62523C59B00FFACC6508213B2211
