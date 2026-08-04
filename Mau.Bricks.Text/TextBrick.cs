// ═══════════════════════════════════════════════
// 积木: text.md_parse
// ID:   BRIK-TEXT-001
// 作用: Markdown 解析为纯数据段列表（标题/列表/引用/代码/表格/分隔线/字符画）
// 引用: Mau.Bricks.Text → Mau.Contracts（BrickRegistry）· MarkdownPart
// 原理: 单次顺序扫描——代码块/表格为跨行状态，内联标记（**/__/*/`）配对应答
// 常用: CH4 UI 渲染 / 工具输出格式化 / 文档预览
// ═══════════════════════════════════════════════
using System;
using System.Collections.Generic;
using System.Text;
using Mau.Contracts;

namespace Mau.Bricks
{
    /// <summary>
    /// 文本积木——标准积木库文本类。Markdown 解析为与渲染框架无关的纯数据段。
    /// 由 CH3 CH_Tool_MD 移植。
    /// </summary>
    public static class TextBrick
    {
        /// <summary>
        /// 解析标题、段落、列表、引用、代码、表格、分隔线和 Box Drawing
        /// </summary>
        /// <param name="markdown">Markdown 文本</param>
        /// <param name="parts">按输入顺序排列的独立段列表</param>
        /// <returns>true=成功</returns>
        public static bool MdParse(string? markdown, out List<MarkdownPart> parts)
        {
            parts = new List<MarkdownPart>();
            if (string.IsNullOrEmpty(markdown))
            {
                return true;
            }
            string normalized = markdown.Replace("\r\n", "\n").Replace('\r', '\n')
                .Replace("\t", "    ");
            string[] lines = normalized.Split('\n');
            bool inCodeBlock = false;
            bool nextTableRowIsHeader = true;
            List<string[]>? tableRows = null;
            bool tableHasHeader = true;

            // [段1] 单次顺序扫描；代码块和表格是仅有的跨行状态
            for (int i = 0; i < lines.Length; i = i + 1)
            {
                string line = lines[i];
                string trimmed = line.Trim();
                if (line.StartsWith("```", StringComparison.Ordinal))
                {
                    if (tableRows != null)
                    {
                        FlushTable(tableRows, tableHasHeader, parts);
                        tableRows = null;
                    }
                    inCodeBlock = !inCodeBlock;
                    continue;
                }
                if (inCodeBlock)
                {
                    parts.Add(CreateSegment(line + "\n", 0, false, false, true,
                        MarkdownColor.CodeBlock));
                    continue;
                }

                // [段2] 表格缓冲到首个非表格行后统一对齐输出
                if (IsTableLine(trimmed))
                {
                    if (IsTableSeparator(trimmed))
                    {
                        nextTableRowIsHeader = false;
                        continue;
                    }
                    if (tableRows == null)
                    {
                        tableRows = new List<string[]>();
                        tableHasHeader = nextTableRowIsHeader;
                    }
                    tableRows.Add(ParseTableColumns(trimmed));
                    nextTableRowIsHeader = false;
                    continue;
                }
                if (tableRows != null)
                {
                    FlushTable(tableRows, tableHasHeader, parts);
                    tableRows = null;
                }
                nextTableRowIsHeader = true;
                ParseLine(line, trimmed, parts);
            }
            if (tableRows != null)
            {
                FlushTable(tableRows, tableHasHeader, parts);
            }
            return true;
        }

        /// <summary>
        /// 检测文本是否包含 Unicode Box Drawing 字符
        /// </summary>
        /// <param name="text">输入文本</param>
        /// <returns>是否包含 U+2500～U+257F</returns>
        public static bool HasBoxDrawing(string? text)
        {
            if (text == null)
            {
                return false;
            }
            for (int i = 0; i < text.Length; i = i + 1)
            {
                if (text[i] >= '\u2500' && text[i] <= '\u257F')
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// 解析一个不属于代码块或表格的普通 Markdown 行
        /// </summary>
        /// <param name="line">原始行</param>
        /// <param name="trimmed">去除首尾空白的行</param>
        /// <param name="result">输出段列表</param>
        private static void ParseLine(string line, string trimmed,
            List<MarkdownPart> result)
        {
            if (HasBoxDrawing(line))
            {
                result.Add(CreateSegment(line + "\n", 0, false, false, true,
                    MarkdownColor.AsciiArt));
                return;
            }
            if (IsHorizontalRule(trimmed))
            {
                result.Add(CreateSegment("---\n", 0, false, false, false,
                    MarkdownColor.Quote));
                return;
            }
            int headingLevel = ReadHeadingLevel(line);
            if (headingLevel > 0)
            {
                int fontDelta = 0;
                bool bold = false;
                MarkdownColor color = MarkdownColor.Text;
                if (headingLevel == 1)
                {
                    fontDelta = 4;
                    bold = true;
                    color = MarkdownColor.H1;
                }
                else if (headingLevel == 2)
                {
                    fontDelta = 2;
                    bold = true;
                    color = MarkdownColor.H2;
                }
                else if (headingLevel == 3)
                {
                    bold = true;
                }
                ParseInline(line.Substring(headingLevel + 1), fontDelta, bold, color, result);
                result.Add(CreateSegment("\n", 0, false, false, false, color));
                return;
            }
            if (line.StartsWith(">", StringComparison.Ordinal))
            {
                result.Add(CreateSegment("  ▎ ", 0, false, false, false,
                    MarkdownColor.Quote));
                ParseInline(line.Substring(1).TrimStart(), 0, false,
                    MarkdownColor.Quote, result);
                result.Add(CreateSegment("\n", 0, false, false, false,
                    MarkdownColor.Quote));
                return;
            }
            if (TryParseList(line, result))
            {
                return;
            }
            ParseInline(line, 0, false, MarkdownColor.Text, result);
            result.Add(CreateSegment("\n", 0, false, false, false,
                MarkdownColor.Text));
        }

        /// <summary>
        /// 解析任务、无序和有序列表行
        /// </summary>
        /// <param name="line">原始行</param>
        /// <param name="result">输出段列表</param>
        /// <returns>是否为列表</returns>
        private static bool TryParseList(string line, List<MarkdownPart> result)
        {
            string prefix = "";
            int contentStart = 0;
            if (line.StartsWith("- [ ] ", StringComparison.Ordinal))
            {
                prefix = "  ☐ ";
                contentStart = 6;
            }
            else if (line.StartsWith("- [x] ", StringComparison.OrdinalIgnoreCase))
            {
                prefix = "  ☑ ";
                contentStart = 6;
            }
            else if (line.StartsWith("- ", StringComparison.Ordinal)
                || line.StartsWith("* ", StringComparison.Ordinal))
            {
                prefix = "  • ";
                contentStart = 2;
            }
            else
            {
                int digitEnd = 0;
                while (digitEnd < line.Length && line[digitEnd] >= '0'
                    && line[digitEnd] <= '9')
                {
                    digitEnd = digitEnd + 1;
                }
                if (digitEnd > 0 && digitEnd + 1 < line.Length
                    && (line[digitEnd] == '.' || line[digitEnd] == ')')
                    && line[digitEnd + 1] == ' ')
                {
                    prefix = " " + line.Substring(0, digitEnd + 1) + " ";
                    contentStart = digitEnd + 2;
                }
            }
            if (prefix.Length == 0)
            {
                return false;
            }
            result.Add(CreateSegment(prefix, 0, false, false, false, MarkdownColor.Text));
            ParseInline(line.Substring(contentStart), 0, false, MarkdownColor.Text, result);
            result.Add(CreateSegment("\n", 0, false, false, false, MarkdownColor.Text));
            return true;
        }

        /// <summary>
        /// 解析粗体、斜体和行内代码
        /// </summary>
        /// <param name="text">行内文本</param>
        /// <param name="fontDelta">字号偏移</param>
        /// <param name="forceBold">是否强制粗体</param>
        /// <param name="color">默认颜色</param>
        /// <param name="result">输出段列表</param>
        private static void ParseInline(string text, int fontDelta, bool forceBold,
            MarkdownColor color, List<MarkdownPart> result)
        {
            int position = 0;
            while (position < text.Length)
            {
                int next = -1;
                string marker = "";
                FindEarlierMarker(text, position, "**", ref next, ref marker);
                FindEarlierMarker(text, position, "__", ref next, ref marker);
                FindEarlierMarker(text, position, "*", ref next, ref marker);
                FindEarlierMarker(text, position, "`", ref next, ref marker);
                if (next < 0)
                {
                    AddTextIfPresent(text.Substring(position), fontDelta, forceBold, false,
                        false, color, result);
                    return;
                }
                if (next > position)
                {
                    AddTextIfPresent(text.Substring(position, next - position), fontDelta,
                        forceBold, false, false, color, result);
                }
                int end = text.IndexOf(marker, next + marker.Length,
                    StringComparison.Ordinal);
                if (end < 0)
                {
                    AddTextIfPresent(text.Substring(next), fontDelta, forceBold, false,
                        false, color, result);
                    return;
                }
                string inner = text.Substring(next + marker.Length,
                    end - next - marker.Length);
                if (marker == "`")
                {
                    AddTextIfPresent(inner, fontDelta, forceBold, false, true,
                        MarkdownColor.Code, result);
                }
                else if (marker == "**" || marker == "__")
                {
                    AddTextIfPresent(inner, fontDelta, true, false, false, color, result);
                }
                else
                {
                    AddTextIfPresent(inner, fontDelta, forceBold, true, false, color, result);
                }
                position = end + marker.Length;
            }
        }

        /// <summary>
        /// 在多个行内标记中保留最早出现者
        /// </summary>
        /// <param name="text">文本</param>
        /// <param name="start">搜索起点</param>
        /// <param name="candidateMarker">候选标记</param>
        /// <param name="next">当前最早位置</param>
        /// <param name="marker">当前最早标记</param>
        private static void FindEarlierMarker(string text, int start, string candidateMarker,
            ref int next, ref string marker)
        {
            int candidate = text.IndexOf(candidateMarker, start, StringComparison.Ordinal);
            if (candidate >= 0 && (next < 0 || candidate < next))
            {
                next = candidate;
                marker = candidateMarker;
            }
        }

        /// <summary>
        /// 把缓冲表格对齐为等宽纯文本段
        /// </summary>
        /// <param name="rows">表格行</param>
        /// <param name="hasHeader">首行是否为标题</param>
        /// <param name="result">输出段列表</param>
        private static void FlushTable(List<string[]> rows, bool hasHeader,
            List<MarkdownPart> result)
        {
            if (rows.Count == 0)
            {
                return;
            }
            int columnCount = 0;
            for (int row = 0; row < rows.Count; row = row + 1)
            {
                if (rows[row].Length > columnCount)
                {
                    columnCount = rows[row].Length;
                }
            }
            int[] widths = new int[columnCount];
            for (int column = 0; column < columnCount; column = column + 1)
            {
                for (int row = 0; row < rows.Count; row = row + 1)
                {
                    string cell = ReadCell(rows[row], column);
                    int width = GetDisplayWidth(StripInlineMarkers(cell));
                    if (width > widths[column])
                    {
                        widths[column] = width;
                    }
                }
            }
            int lineWidth = 1;
            for (int column = 0; column < widths.Length; column = column + 1)
            {
                lineWidth = lineWidth + widths[column] + 3;
            }
            string separator = new string('—', (lineWidth / 2) + 1) + "\n";
            result.Add(CreateSegment(separator, 0, false, false, true,
                MarkdownColor.Table));
            for (int row = 0; row < rows.Count; row = row + 1)
            {
                StringBuilder rowBuilder = new StringBuilder();
                rowBuilder.Append("| ");
                for (int column = 0; column < columnCount; column = column + 1)
                {
                    string cell = StripInlineMarkers(ReadCell(rows[row], column));
                    rowBuilder.Append(cell);
                    int padding = widths[column] - GetDisplayWidth(cell);
                    if (padding > 0)
                    {
                        rowBuilder.Append(' ', padding);
                    }
                    rowBuilder.Append(" | ");
                }
                rowBuilder.Append('\n');
                bool bold = hasHeader && row == 0;
                result.Add(CreateSegment(rowBuilder.ToString(), 0, bold, false, true,
                    MarkdownColor.Table));
            }
            result.Add(CreateSegment(separator, 0, false, false, true,
                MarkdownColor.Table));
        }

        /// <summary>
        /// 读取不存在列时返回空字符串
        /// </summary>
        /// <param name="row">行</param>
        /// <param name="column">列</param>
        /// <returns>单元格</returns>
        private static string ReadCell(string[] row, int column)
        {
            if (column < row.Length)
            {
                return row[column];
            }
            return "";
        }

        /// <summary>
        /// 移除用于展示的行内标记
        /// </summary>
        /// <param name="text">单元格文本</param>
        /// <returns>纯文本</returns>
        private static string StripInlineMarkers(string text)
        {
            return text.Replace("**", "").Replace("__", "").Replace("`", "")
                .Replace("*", "");
        }

        /// <summary>
        /// 按 ASCII 一格、非 ASCII 两格估算等宽显示宽度
        /// </summary>
        /// <param name="text">文本</param>
        /// <returns>显示宽度</returns>
        private static int GetDisplayWidth(string text)
        {
            int width = 0;
            for (int i = 0; i < text.Length; i = i + 1)
            {
                if (text[i] > 127)
                {
                    width = width + 2;
                }
                else
                {
                    width = width + 1;
                }
            }
            return width;
        }

        /// <summary>
        /// 判断行是否开始一个管道表格
        /// </summary>
        /// <param name="trimmed">已 Trim 行</param>
        /// <returns>是否为表格行</returns>
        private static bool IsTableLine(string trimmed)
        {
            return trimmed.Length > 0 && trimmed[0] == '|';
        }

        /// <summary>
        /// 判断管道行是否只包含分隔字符
        /// </summary>
        /// <param name="line">表格行</param>
        /// <returns>是否为分隔行</returns>
        private static bool IsTableSeparator(string line)
        {
            bool hasDash = false;
            for (int i = 0; i < line.Length; i = i + 1)
            {
                char current = line[i];
                if (current == '-')
                {
                    hasDash = true;
                }
                else if (current != '|' && current != ':' && current != ' ')
                {
                    return false;
                }
            }
            return hasDash;
        }

        /// <summary>
        /// 按管道拆分表格列并去除首尾空列
        /// </summary>
        /// <param name="line">表格行</param>
        /// <returns>列数组</returns>
        private static string[] ParseTableColumns(string line)
        {
            string[] parts = line.Split('|');
            int start = 0;
            int end = parts.Length;
            if (end > 0 && parts[0].Length == 0)
            {
                start = 1;
            }
            if (end > start && parts[end - 1].Length == 0)
            {
                end = end - 1;
            }
            string[] result = new string[end - start];
            for (int i = 0; i < result.Length; i = i + 1)
            {
                result[i] = parts[start + i].Trim();
            }
            return result;
        }

        /// <summary>
        /// 读取一到六级 ATX 标题，其他返回零
        /// </summary>
        /// <param name="line">原始行</param>
        /// <returns>标题级别</returns>
        private static int ReadHeadingLevel(string line)
        {
            int count = 0;
            while (count < line.Length && count < 6 && line[count] == '#')
            {
                count = count + 1;
            }
            if (count > 0 && count < line.Length && line[count] == ' ')
            {
                return count;
            }
            return 0;
        }

        /// <summary>
        /// 判断是否为三个以上同类 `-`、`*` 或 `_`
        /// </summary>
        /// <param name="line">Trim 后文本</param>
        /// <returns>是否为水平线</returns>
        private static bool IsHorizontalRule(string line)
        {
            if (line.Length < 3)
            {
                return false;
            }
            char marker = line[0];
            if (marker != '-' && marker != '*' && marker != '_')
            {
                return false;
            }
            int markerCount = 0;
            for (int i = 0; i < line.Length; i = i + 1)
            {
                if (line[i] == marker)
                {
                    markerCount = markerCount + 1;
                }
                else if (line[i] != ' ')
                {
                    return false;
                }
            }
            return markerCount >= 3;
        }

        /// <summary>
        /// 非空时追加文本段
        /// </summary>
        /// <param name="text">文本</param>
        /// <param name="fontDelta">字号偏移</param>
        /// <param name="bold">粗体</param>
        /// <param name="italic">斜体</param>
        /// <param name="mono">等宽</param>
        /// <param name="color">颜色类别</param>
        /// <param name="result">输出列表</param>
        private static void AddTextIfPresent(string text, int fontDelta, bool bold,
            bool italic, bool mono, MarkdownColor color,
            List<MarkdownPart> result)
        {
            if (text.Length > 0)
            {
                result.Add(CreateSegment(text, fontDelta, bold, italic, mono, color));
            }
        }

        /// <summary>
        /// 构造一个纯数据 Markdown 段
        /// </summary>
        /// <param name="text">文本</param>
        /// <param name="fontDelta">字号偏移</param>
        /// <param name="bold">粗体</param>
        /// <param name="italic">斜体</param>
        /// <param name="mono">等宽</param>
        /// <param name="color">颜色类别</param>
        /// <returns>Markdown 段</returns>
        private static MarkdownPart CreateSegment(string text, int fontDelta,
            bool bold, bool italic, bool mono, MarkdownColor color)
        {
            MarkdownPart segment;
            segment.Text = text;
            segment.FontDelta = fontDelta;
            segment.Bold = bold;
            segment.Italic = italic;
            segment.Mono = mono;
            segment.ColorType = color;
            return segment;
        }
    }

    /// <summary>
    /// 文本积木注册——进程启动时调用一次
    /// </summary>
    public static class TextBrickRegistration
    {
        /// <summary>
        /// 注册全部文本积木
        /// </summary>
        public static void RegisterAll()
        {
            RegisterMdParse();
        }

        /// <summary>
        /// 注册 text.md_parse
        /// </summary>
        private static void RegisterMdParse()
        {
            BrickContract contract = new BrickContract("text.md_parse", "Mau.Bricks.TextBrick.MdParse");
            contract.Inputs.Add(new BrickPort("markdown", typeof(string), "Markdown 文本"));
            contract.Outputs.Add(new BrickPort("parts", typeof(List<MarkdownPart>), "段列表"));
            contract.Return = BrickReturnKind.Bool;
            contract.Duration = BrickDuration.Sync;
            contract.Thread = "main";
            BrickRegistry.Register(contract);
        }
    }
}
