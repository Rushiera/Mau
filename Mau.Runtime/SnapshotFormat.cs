using System;
using System.Text;

namespace Mau.Runtime
{
    /// <summary>
    /// 分节文本（Snapshot）格式辅助——data.snapshot_* 积木共享（程序级）
    /// </summary>
    public static class SnapshotFormat
    {
        /// <summary>
        /// 追加一行并保护标题形态、前导反斜线和空行
        /// </summary>
        /// <param name="builder">输出构造器</param>
        /// <param name="line">原始行</param>
        public static void AppendEscapedLine(StringBuilder builder, string line)
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
        public static string UnescapeLine(string line)
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
        public static bool IsSectionHeader(string line)
        {
            return line.Length >= 3 && line[0] == '[' && line[line.Length - 1] == ']';
        }

        /// <summary>
        /// 拒绝会破坏格式边界的节名
        /// </summary>
        /// <param name="sectionName">节名</param>
        public static void ValidateSectionName(string sectionName)
        {
            if (string.IsNullOrWhiteSpace(sectionName)
                || sectionName.IndexOf('[') >= 0 || sectionName.IndexOf(']') >= 0
                || sectionName.IndexOf('\n') >= 0 || sectionName.IndexOf('\r') >= 0)
            {
                throw new ArgumentException("Snapshot section name is invalid.", "sectionName");
            }
        }
/// <summary>
/// 把可空文本规范为空字符串——积木文本兼容入口（转发 BrickText.SafeText）
/// </summary>
/// <param name = "value">输入</param>
/// <returns>非空文本</returns>
public static string SafeText(string? value)
{
    return BrickText.SafeText(value);
}    }
}
