using System;
using System.Collections.Generic;

namespace Mau.Development
{
    /// <summary>
    /// MauPocketCompiler 行号映射分部——生成物行号 → 语料/积木源码定位（D1 调试基建，partial 分部）。
    /// </summary>
    public sealed partial class MauPocketCompiler
    {
        /// <summary>
        /// 行号映射文本——当前编译的诊断反查表（D1：生成物行 → 语料行/积木源码行）
        /// </summary>
        private string? _mapText;

        /// <summary>
        /// 行号映射段——生成物行区间 → 语料/积木源码定位（D1）
        /// </summary>
        private sealed class MapSegment
        {
            /// <summary>
            /// 段名——变迁名或积木 ID
            /// </summary>
            internal string Name = "";

            /// <summary>
            /// 段起始生成物行（1-based）
            /// </summary>
            internal int Start;

            /// <summary>
            /// 段结束生成物行（1-based 含）——仅 brick 段有效
            /// </summary>
            internal int End;

            /// <summary>
            /// 映射源——transition: 语料行号; brick: 剥离行数
            /// </summary>
            internal int Source;

            /// <summary>
            /// 是否 brick 段——false = transition 段
            /// </summary>
            internal bool IsBrick;
        }

        /// <summary>
        /// 行号映射反查——生成物行号 → 语料行号/积木源码行号（D1 调试基建）
        /// </summary>
        /// <param name="generatedLine">生成物行号（1-based）</param>
        /// <param name="mapped">映射后行号——未命中返回原行号</param>
        /// <param name="note">映射说明——追加到诊断消息尾部（未命中为空）</param>
        private void ResolveMapLine(int generatedLine, out string mapped, out string note)
        {
            mapped = generatedLine.ToString();
            note = "";
            if (string.IsNullOrEmpty(_mapText))
            {
                return;
            }
            // [段1] 解析全部段——transition（语料行号）与 brick（剥离行数）
            List<MapSegment> segments = new List<MapSegment>();
            string[] lines = _mapText.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal))
                {
                    continue;
                }
                if (line.StartsWith("transition ", StringComparison.Ordinal))
                {
                    string[] parts = line.Split(' ');
                    if (parts.Length >= 6 && parts[2] == "generated" && parts[4] == "source")
                    {
                        MapSegment seg = new MapSegment();
                        seg.Name = parts[1].TrimEnd(':');
                        int genLine;
                        int srcLine;
                        if (!int.TryParse(parts[3], out genLine) || !int.TryParse(parts[5], out srcLine))
                        {
                            continue; // 行号非数——跳过该段（R2-P3：原 int.Parse 在格式异常时抛错，映射面不应崩）
                        }
                        seg.Start = genLine;
                        seg.Source = srcLine;
                        seg.IsBrick = false;
                        segments.Add(seg);
                    }
                }
                else if (line.StartsWith("brick ", StringComparison.Ordinal))
                {
                    int colon = line.IndexOf(' ');
                    string rest = line.Substring(colon + 1);
                    int genPos = rest.IndexOf(" generated ", StringComparison.Ordinal);
                    int stripPos = rest.IndexOf(" stripped ", StringComparison.Ordinal);
                    if (genPos > 0 && stripPos > genPos)
                    {
                        MapSegment seg = new MapSegment();
                        seg.Name = rest.Substring(0, genPos).TrimEnd(':');
                        string range = rest.Substring(genPos + 10, stripPos - genPos - 10).Trim();
                        int dash = range.IndexOf('-');
                        seg.Start = int.Parse(range.Substring(0, dash));
                        seg.End = int.Parse(range.Substring(dash + 1)) + 1;
                        seg.Source = int.Parse(rest.Substring(stripPos + 9).Trim());
                        seg.IsBrick = true;
                        segments.Add(seg);
                    }
                }
            }
            // [段2] 定位——段 i 的结束 = 段 i+1 的起始（transition 段不吞后续 brick 段）
            for (int i = 0; i < segments.Count; i++)
            {
                int segEnd = i + 1 < segments.Count ? segments[i + 1].Start : int.MaxValue;
                if (segments[i].IsBrick && segments[i].End < segEnd)
                {
                    segEnd = segments[i].End;
                }
                if (generatedLine >= segments[i].Start && generatedLine < segEnd)
                {
                    if (segments[i].IsBrick)
                    {
                        int srcLine = generatedLine - segments[i].Start + segments[i].Source;
                        mapped = srcLine.ToString();
                        note = " [生成物行" + generatedLine.ToString() + " → 积木 " + segments[i].Name + " 源码行" + srcLine.ToString() + "]";
                    }
                    else
                    {
                        mapped = segments[i].Source.ToString();
                        note = " [生成物行" + generatedLine.ToString() + " → 语料 " + segments[i].Name + " 行" + segments[i].Source.ToString() + "]";
                    }
                    return;
                }
            }
        }
    }
}
