using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace Mau.Runtime
{
    /// <summary>
    /// 受控文件系统服务——file.* 积木的底层安全边界。
    /// 将文件能力限制在显式根目录集合内，所有操作拒绝穿过现有重解析点，
    /// 避免符号链接绕过字符串前缀检查。
    /// </summary>
    public sealed class FileSystemService
    {
        /// <summary>
        /// 规范化允许根目录
        /// </summary>
        private readonly string[] _roots;

        /// <summary>
        /// 软删除回收目录
        /// </summary>
        private readonly string _recycleRoot;

        /// <summary>
        /// 串行保护变更文件的复合操作
        /// </summary>
        private readonly object _writeGate;

        /// <summary>
        /// 只读根标志——与 _roots 对齐；true 的根拒绝写操作（writable=false 语义）
        /// </summary>
        private readonly bool[] _readOnly;

        /// <summary>
        /// 根标识数组——与 _roots 对齐（命名空间寻址 id:relative——P8.5b）
        /// </summary>
        private readonly string[] _rootIds;

        /// <summary>
        /// 建立明确根目录和位于其中的回收站
        /// </summary>
        /// <param name="roots">允许根目录</param>
        /// <param name="recycleRoot">回收目录</param>
        public FileSystemService(string[] roots, string recycleRoot)
        {
            if (roots == null || roots.Length == 0)
            {
                throw new ArgumentException("At least one filesystem root is required.", "roots");
            }
            _roots = new string[roots.Length];
            _readOnly = new bool[roots.Length];
            _rootIds = new string[roots.Length];
            for (int i = 0; i < roots.Length; i = i + 1)
            {
                if (string.IsNullOrWhiteSpace(roots[i]))
                {
                    throw new ArgumentException("Filesystem root is empty.", "roots");
                }
                _roots[i] = PathBoundary.NormalizeRoot(roots[i]);
                _rootIds[i] = "root" + i.ToString();
            }
            _recycleRoot = Resolve(recycleRoot, true);
            Directory.CreateDirectory(_recycleRoot);
            _writeGate = new object();
        }

        /// <summary>
        /// 共享读打开——FileShare.ReadWrite（容许读取正在被其他进程写入的文件：活跃日志 / 热文件）。
        /// File.ReadAllBytes 默认 FileShare.Read——与已存在的写句柄不兼容，读活跃日志必然失败（判例 2026-09-15）。
        /// </summary>
        /// <param name="path">规范绝对路径</param>
        /// <returns>文件全部字节</returns>
        private static byte[] ReadAllBytesShared(string path)
        {
            using (System.IO.FileStream fs = new System.IO.FileStream(path, System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.ReadWrite))
            {
                using (System.IO.MemoryStream ms = new System.IO.MemoryStream())
                {
                    fs.CopyTo(ms);
                    return ms.ToArray();
                }
            }
        }

        /// <summary>
        /// 共享读文本——StreamReader 保真（BOM 检测 + 编码契约与 File.ReadAllText 一致）
        /// </summary>
        /// <param name="path">规范绝对路径</param>
        /// <param name="encoding">编码</param>
        /// <returns>完整文本</returns>
        private static string ReadTextShared(string path, System.Text.Encoding encoding)
        {
            using (System.IO.FileStream fs = new System.IO.FileStream(path, System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.ReadWrite))
            {
                using (System.IO.StreamReader sr = new System.IO.StreamReader(fs, encoding, true))
                {
                    return sr.ReadToEnd();
                }
            }
        }

        /// <summary>
        /// 共享读文本行——StreamReader.ReadLine 语义与 File.ReadAllLines 对齐（无尾空行）
        /// </summary>
        /// <param name="path">规范绝对路径</param>
        /// <param name="encoding">编码</param>
        /// <returns>行数组</returns>
        private static string[] ReadAllLinesShared(string path, System.Text.Encoding encoding)
        {
            using (System.IO.FileStream fs = new System.IO.FileStream(path, System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.ReadWrite))
            {
                using (System.IO.StreamReader sr = new System.IO.StreamReader(fs, encoding, true))
                {
                    List<string> collected = new List<string>();
                    string? line = sr.ReadLine();
                    while (line != null)
                    {
                        collected.Add(line);
                        line = sr.ReadLine();
                    }
                    return collected.ToArray();
                }
            }
        }

        /// <summary>
        /// 读取 UTF-8 文本（共享读——正在写入的活跃文件同样可读）
        /// </summary>
        /// <param name="path">受控路径</param>
        /// <returns>完整文本</returns>
        public string ReadText(string path)
        {
            string resolved = Resolve(path, false);
            return ReadTextShared(resolved, Encoding.UTF8);
        }

        /// <summary>
        /// 原子覆写 UTF-8 无 BOM 文本
        /// </summary>
        /// <param name="path">受控路径</param>
        /// <param name="content">完整正文</param>
        public void WriteText(string path, string content)
        {
            string resolved = Resolve(path, true);
            lock (_writeGate)
            {
                WriteAtomic(resolved, SafeText(content));
            }
        }
        /// <summary>
        /// 自动编码读取——BOM 优先探测，无 BOM 按类型契约（P1 编码内建）
        /// </summary>
        /// <param name="path">受控路径</param>
        /// <returns>完整文本（编码已按契约解析）</returns>
        public string ReadTextAuto(string path)
        {
            string resolved = Resolve(path, false);
            byte[] raw = ReadAllBytesShared(resolved);
            System.Text.Encoding enc = TextFileCodec.DetectReadEncoding(path, raw);
            // 去掉 BOM 头避免首字符 \uFEFF 混入（UTF8Encoding(true) 解码会吞掉 BOM 自身）
            int offset = (enc is UTF8Encoding utf8 && utf8.GetPreamble().Length > 0 && raw.Length >= 3 && raw[0] == 0xEF && raw[1] == 0xBB && raw[2] == 0xBF) ? 3 : 0;
            return enc.GetString(raw, offset, raw.Length - offset);
        }
        /// <summary>
        /// 锚点区间读取——str1 空=文件头 / str2 空=文件尾 / 双空=全文；非空锚点必须全文唯一（P1 区间读 + 锚点契约）
        /// </summary>
        /// <param name="path">受控路径</param>
        /// <param name="str1">起始锚点（空=文件头）</param>
        /// <param name="str2">结束锚点（空=文件尾）</param>
        /// <returns>两锚点之间内容（不含锚点自身；锚点歧义/缺失返回 ERR 前缀文本）</returns>
        public string ReadBetweenAuto(string path, string str1, string str2)
        {
            string resolved = Resolve(path, false);
            byte[] raw = ReadAllBytesShared(resolved);
            System.Text.Encoding enc = TextFileCodec.DetectReadEncoding(path, raw);
            int bom = (enc is UTF8Encoding u && u.GetPreamble().Length > 0 && raw.Length >= 3 && raw[0] == 0xEF && raw[1] == 0xBB && raw[2] == 0xBF) ? 3 : 0;
            string content = enc.GetString(raw, bom, raw.Length - bom);
            string unified = content.Replace("\r\n", "\n");
            string a1 = (str1 ?? "").Replace("\r\n", "\n");
            string a2 = (str2 ?? "").Replace("\r\n", "\n");
            int start = 0;
            if (a1.Length > 0)
            {
                int[] idx1 = FindAll(unified, a1, "exact");
                if (idx1.Length == 0)
                {
                    return "ERR|ANCHOR_NOT_FOUND|起始锚点未找到: " + a1;
                }

                if (idx1.Length > 1)
                {
                    return "ERR|ANCHOR_AMBIGUOUS|起始锚点多次出现: " + a1;
                }

                start = idx1[0] + a1.Length;
            }

            int end = unified.Length;
            if (a2.Length > 0)
            {
                int[] idx2 = FindAll(unified, a2, "exact");
                if (idx2.Length == 0)
                {
                    return "ERR|ANCHOR_NOT_FOUND|结束锚点未找到: " + a2;
                }

                if (idx2.Length > 1)
                {
                    return "ERR|ANCHOR_AMBIGUOUS|结束锚点多次出现: " + a2;
                }

                end = idx2[0];
            }

            if (end < start)
            {
                return "ERR|ANCHOR_ORDER|结束锚点位于起始锚点之前";
            }

            return unified.Substring(start, end - start);
        }
        /// <summary>自动编码覆写——既有文件的 BOM/换行保真，新建文件按类型契约（P1/P2；A25）。</summary>
        /// <param name="path">受控路径</param>
        /// <param name="content">完整正文（换行自动归一为目标风格）</param>
        public void WriteTextAuto(string path, string content) { string resolved = Resolve(path, true); lock (_writeGate) { WriteAutoCore(resolved, path, SafeText(content)); } }
        /// <summary>自动编码追加——既有文件的 BOM/换行保真，新建文件按类型契约（P1/P2；A25）。</summary>
        /// <param name="path">受控路径</param>
        /// <param name="content">追加正文（换行自动归一为目标风格）</param>
        public void AppendTextAuto(string path, string content)
        {
            string resolved = Resolve(path, true);
            lock (_writeGate)
            {
                EnsureParentDirectory(resolved);
                bool bom = false;
                string newline = ResolveWriteStyle(resolved, path, out bom);
                string body = TextFileCodec.NormalizeNewlines(SafeText(content), newline);
                File.AppendAllText(resolved, body, TextFileCodec.WriteEncoding(path, bom));
            }
        }
        /// <summary>
        /// 锚点三态替换——P3 核心（design-ch4-text-tools §六）：exact/ignore_case 要求唯一（0→NotFound+差异定位 / 1→替换 / >1→Ambiguous+候选）；
        /// regex/all 模式替换全部匹配（0→NotFound）；exact/ignore_case 要求唯一（>1→Ambiguous+候选行）。编码 + 换行保真（P1/P2），绝不静默写入。
        /// </summary>
        /// <param name="path">受控路径</param>
        /// <param name="oldText">锚点文本</param>
        /// <param name="newText">替换文本（regex 模式支持 $1 捕获组）</param>
        /// <param name="mode">exact（默认）/ ignore_case / all（字面量全部替换）/ regex</param>
        /// <returns>三态诊断结果</returns>
        public TextReplaceOutcome ReplaceTextAuto(string path, string oldText, string newText, string mode)
        {
            if (string.IsNullOrEmpty(oldText))
            {
                throw new ArgumentException("Replacement target is empty.", "oldText");
            }
            string resolved = Resolve(path, true);
            TextReplaceOutcome outcome = new TextReplaceOutcome();
            lock (_writeGate)
            {
                byte[] raw = ReadAllBytesShared(resolved);
                System.Text.Encoding enc = TextFileCodec.DetectReadEncoding(path, raw);
                int bom = (enc is UTF8Encoding u && u.GetPreamble().Length > 0
                    && raw.Length >= 3 && raw[0] == 0xEF && raw[1] == 0xBB && raw[2] == 0xBF) ? 3 : 0;
                string content = enc.GetString(raw, bom, raw.Length - bom);
                string newline = TextFileCodec.DetectNewline(raw);
                string unified = content.Replace("\r\n", "\n");
                string target = oldText.Replace("\r\n", "\n");
                int[] indexes = FindAll(unified, target, mode);
                if (indexes.Length == 0)
                {
                    outcome.Status = TextReplaceStatus.NotFound;
                    LocateBestDiff(unified, target, outcome);
                    return outcome;
                }
                if (mode != "regex" && mode != "all" && indexes.Length > 1)
                {
                    outcome.Status = TextReplaceStatus.Ambiguous;
                    outcome.Count = indexes.Length;
                    outcome.CandidateLines = LineNumbersOf(unified, indexes);
                    return outcome;
                }
                string replaced;
                if (mode == "regex")
                {
                    replaced = Regex.Replace(unified, target, newText, RegexOptions.None, TimeSpan.FromSeconds(5));
                }
                else if (mode == "ignore_case")
                {
                    replaced = unified.Replace(target, newText, StringComparison.OrdinalIgnoreCase);
                }
                else
                {
                    replaced = unified.Replace(target, newText, StringComparison.Ordinal);
                }
                outcome.Status = TextReplaceStatus.Ok;
                outcome.Count = indexes.Length;
                // 换行保真写回（新行归一为目标风格）+ 原子写
                string body = TextFileCodec.NormalizeNewlines(replaced, newline);
                ConfigStore.AtomicWrite(resolved, body, enc);
                // 写后验证——返回替换后目标段读回（P5）
                outcome.Snippet = SnippetAround(replaced, indexes[0]);
                return outcome;
            }
        }
        /// <summary>
        /// 查找全部锚点命中位置（exact/ignore_case 用 IndexOf 循环；regex 用 Matches）
        /// </summary>
        /// <param name="content">归一化正文</param>
        /// <param name="target">归一化锚点</param>
        /// <param name="mode">匹配模式</param>
        /// <returns>命中起始索引数组</returns>
        private static int[] FindAll(string content, string target, string mode)
        {
            if (mode == "regex")
            {
                MatchCollection matches = Regex.Matches(content, target, RegexOptions.None, TimeSpan.FromSeconds(5));
                int[] result = new int[matches.Count];
                for (int i = 0; i < matches.Count; i = i + 1)
                {
                    result[i] = matches[i].Index;
                }

                return result;
            }

            StringComparison cmp = mode == "ignore_case" ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            List<int> list = new List<int>();
            int start = 0;
            while (start <= content.Length - target.Length)
            {
                int found = content.IndexOf(target, start, cmp);
                if (found < 0)
                {
                    break;
                }

                list.Add(found);
                start = found + target.Length;
            }

            return list.ToArray();
        }
        /// <summary>
        /// 未找到时差异字节定位——取内容中与锚点最长公共前缀的位置，输出 第N字节 期望/实际（P3 诊断）
        /// </summary>
        /// <param name="content">归一化正文</param>
        /// <param name="target">归一化锚点</param>
        /// <param name="outcome">结果载荷（写差异字段）</param>
        private static void LocateBestDiff(string content, string target, TextReplaceOutcome outcome) { string first = target.Length > 0 ? target.Substring(0, 1) : ""; int bestPos = 0; int bestCommon = -1; int scan = content.IndexOf(first, StringComparison.Ordinal); while (scan >= 0) { int common = 0; while (common < target.Length && scan + common < content.Length && content[scan + common] == target[common]) { common = common + 1; } if (common > bestCommon) { bestCommon = common; bestPos = scan; } if (common >= target.Length) { break; } scan = content.IndexOf(first, scan + 1, StringComparison.Ordinal); } if (bestCommon < 0) { bestCommon = 0; } int diff = bestPos + bestCommon; outcome.DiffByteIndex = diff; outcome.Expected = Slice(target, bestCommon, 10); outcome.Actual = Slice(content, diff, 10); }
        /// <summary>
        /// 截取片段（越界截断）
        /// </summary>
        /// <param name="text">原文</param>
        /// <param name="start">起始索引</param>
        /// <param name="count">最大长度</param>
        /// <returns>片段</returns>
        private static string Slice(string text, int start, int count) { if (start < 0) { start = 0; } if (start >= text.Length) { return ""; } int take = Math.Min(count, text.Length - start); return text.Substring(start, take); }
        /// <summary>
        /// 命中位置 → 行号（1 起，前 5 个——P3 多次出现候选）
        /// </summary>
        /// <param name="content">归一化正文</param>
        /// <param name="indexes">命中起始索引</param>
        /// <returns>行号字符串数组</returns>
        private static string[] LineNumbersOf(string content, int[] indexes) { List<string> lines = new List<string>(); for (int i = 0; i < indexes.Length && lines.Count < 5; i = i + 1) { int line = 1; int limit = Math.Min(indexes[i], content.Length); for (int j = 0; j < limit; j = j + 1) { if (content[j] == '\n') { line = line + 1; } } lines.Add(line.ToString()); } return lines.ToArray(); }
        /// <summary>
        /// 替换点附近 ±3 行摘要（P5 写后验证——replace 返回目标段读回）
        /// </summary>
        /// <param name="content">归一化正文</param>
        /// <param name="index">命中起始索引</param>
        /// <returns>行摘要文本</returns>
        private static string SnippetAround(string content, int index) { int lineStart = index; int lineEnd = index; while (lineStart > 0 && content[lineStart - 1] != '\n') { lineStart = lineStart - 1; } while (lineEnd < content.Length && content[lineEnd] != '\n') { lineEnd = lineEnd + 1; } int start = lineStart; for (int i = 0; i < 3 && start > 0; i = i + 1) { int prev = content.LastIndexOf('\n', start - 1); if (prev < 0) { start = 0; break; } start = prev + 1; } int end = lineEnd; for (int i = 0; i < 3 && end < content.Length; i = i + 1) { int next = content.IndexOf('\n', end); if (next < 0) { end = content.Length; break; } end = next + 1; } return content.Substring(start, end - start); }
        /// <summary>
        /// 写侧风格解析——既有文件探测实际 BOM + 换行并保真；新建文件按类型契约（P1/P2；A25）。
        /// 单次读取同时产出两者（BOM 保真 + 换行保真共用一份字节）。
        /// </summary>
        /// <param name="resolved">规范绝对路径</param>
        /// <param name="path">用户路径（契约判定用）</param>
        /// <param name="bom">输出——写入是否带 BOM（既有文件=实际值 / 新建=DefaultBom）</param>
        /// <returns>换行串（既有文件=实际风格 / 新建=DefaultNewline）</returns>
        private string ResolveWriteStyle(string resolved, string path, out bool bom)
        {
            if (File.Exists(resolved))
            {
                byte[] raw = ReadAllBytesShared(resolved);
                bom = TextFileCodec.DetectBom(raw);
                // 文件无任何换行 → 按类型默认（契约在无换行时仍生效）
                for (int i = 0; i < raw.Length; i = i + 1)
                {
                    if (raw[i] == (byte)'\n')
                    {
                        return TextFileCodec.DetectNewline(raw);
                    }
                }
                return TextFileCodec.DefaultNewline(path);
            }
            bom = TextFileCodec.DefaultBom(path);
            return TextFileCodec.DefaultNewline(path);
        }

        /// <summary>编码内建写核心——两态风格（既有文件 BOM/换行保真；新建文件按类型契约）+ 原子写。</summary>
        /// <param name="resolved">规范绝对路径</param>
        /// <param name="path">用户路径（契约判定用）</param>
        /// <param name="content">正文</param>
        private void WriteAutoCore(string resolved, string path, string content)
        {
            bool bom = false;
            string newline = ResolveWriteStyle(resolved, path, out bom);
            System.Text.Encoding enc = TextFileCodec.WriteEncoding(path, bom);
            string body = TextFileCodec.NormalizeNewlines(content, newline);
            // 原子写——编码 + BOM（含 BOM 的 UTF8Encoding 由 WriteAllText 自动前置）
            string? dir = Path.GetDirectoryName(resolved);
            if (dir != null && dir.Length > 0)
            {
                Directory.CreateDirectory(dir);
            }
            string tmp = resolved + ".tmp";
            File.WriteAllText(tmp, body, enc);
            File.Move(tmp, resolved, true);
        }

        /// <summary>
        /// 追加 UTF-8 文本并创建父目录
        /// </summary>
        /// <param name="path">受控路径</param>
        /// <param name="content">追加正文</param>
        public void AppendText(string path, string content)
        {
            string resolved = Resolve(path, true);
            lock (_writeGate)
            {
                EnsureParentDirectory(resolved);
                File.AppendAllText(resolved, SafeText(content), new UTF8Encoding(false));
            }
        }

        /// <summary>
        /// 替换全部精确文本并原子写回
        /// </summary>
        /// <param name="path">受控路径</param>
        /// <param name="oldText">非空目标</param>
        /// <param name="newText">新文本</param>
        /// <returns>替换数量</returns>
        public int ReplaceText(string path, string oldText, string newText)
        {
            if (string.IsNullOrEmpty(oldText))
            {
                throw new ArgumentException("Replacement target is empty.", "oldText");
            }
            string resolved = Resolve(path, true);
            lock (_writeGate)
            {
                string content = ReadTextShared(resolved, Encoding.UTF8);
                int count = CountOccurrences(content, oldText);
                if (count > 0)
                {
                    WriteAtomic(resolved, content.Replace(oldText, SafeText(newText), StringComparison.Ordinal));
                }
                return count;
            }
        }

        /// <summary>
        /// 按一基闭区间读取文本行
        /// </summary>
        /// <param name="path">受控路径</param>
        /// <param name="startLine">起始行</param>
        /// <param name="endLine">结束行；零表示文件尾</param>
        /// <returns>带行号文本</returns>
        public string ReadLines(string path, int startLine, int endLine)
        {
            if (startLine < 1 || endLine < 0)
            {
                throw new ArgumentOutOfRangeException("startLine");
            }
            string[] lines = ReadAllLinesShared(Resolve(path, false), Encoding.UTF8);
            int end = endLine;
            if (end == 0 || end > lines.Length)
            {
                end = lines.Length;
            }
            if (startLine > end && lines.Length > 0)
            {
                throw new ArgumentOutOfRangeException("startLine");
            }
            StringBuilder builder = new StringBuilder();
            for (int line = startLine; line <= end; line = line + 1)
            {
                if (builder.Length > 0)
                {
                    builder.Append('\n');
                }
                builder.Append(line.ToString());
                builder.Append(": ");
                builder.Append(lines[line - 1]);
            }
            return builder.ToString();
        }
        /// <summary>
        /// 自动编码行号区间读取——BOM 优先探测，无 BOM 按类型契约（P5 编码契约补全；规格 design-ch4-text-tools §四）
        /// </summary>
        /// <param name="path">受控路径</param>
        /// <param name="startLine">起始行（1 起）</param>
        /// <param name="endLine">结束行；零表示文件尾</param>
        /// <returns>带行号文本</returns>
        public string ReadLinesAuto(string path, int startLine, int endLine)
        {
            if (startLine < 1 || endLine < 0)
            {
                throw new ArgumentOutOfRangeException("startLine");
            }

            string resolved = Resolve(path, false);
            byte[] raw = ReadAllBytesShared(resolved);
            System.Text.Encoding enc = TextFileCodec.DetectReadEncoding(path, raw);
            int bom = (enc is UTF8Encoding u && u.GetPreamble().Length > 0 && raw.Length >= 3 && raw[0] == 0xEF && raw[1] == 0xBB && raw[2] == 0xBF) ? 3 : 0;
            string content = enc.GetString(raw, bom, raw.Length - bom);
            string[] lines = content.Replace("\r\n", "\n").Split('\n');
            // [段1] 尾空行修剪——以 \n 结尾时 Split 产生尾空串，与 File.ReadAllLines 语义对齐
            if (lines.Length > 0 && lines[lines.Length - 1].Length == 0)
            {
                string[] trimmed = new string[lines.Length - 1];
                for (int i = 0; i < trimmed.Length; i = i + 1)
                {
                    trimmed[i] = lines[i];
                }

                lines = trimmed;
            }

            // [段2] 区间裁剪——end 零=文件尾，超尾截断
            int end = endLine;
            if (end == 0 || end > lines.Length)
            {
                end = lines.Length;
            }

            if (startLine > end && lines.Length > 0)
            {
                throw new ArgumentOutOfRangeException("startLine");
            }

            // [段3] 行号格式化输出——与 ReadLines 同格式（行号: 内容）
            StringBuilder builder = new StringBuilder();
            for (int line = startLine; line <= end; line = line + 1)
            {
                if (builder.Length > 0)
                {
                    builder.Append('\n');
                }

                builder.Append(line.ToString());
                builder.Append(": ");
                builder.Append(lines[line - 1]);
            }

            return builder.ToString();
        }
        /// <summary>
        /// <summary>忽略目录名单——递归遍历时跳过（.git/bin/obj 等版本控制/编译产物；精确匹配目录名忽略大小写；显式以忽略名为根的查询不受影响——Q4 2026-09-08）</summary>
        private static readonly string[] IgnoredDirNames = new string[]
        {
            ".git", "bin", "obj", "node_modules", ".vs", "dist", "build", "out", ".idea"
        };

        /// <summary>
        /// 目录名是否在忽略名单——递归遍历跳过判断（根目录本身不校验）。
        /// </summary>
        /// <param name="name">目录名</param>
        /// <returns>true=应跳过</returns>
        private static bool IsIgnoredDir(string name)
        {
            for (int i = 0; i < IgnoredDirNames.Length; i = i + 1)
            {
                if (string.Equals(name, IgnoredDirNames[i], StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// 收集 .git 特征行——HEAD 指向 + 本地分支 commit（零外部依赖：直读 HEAD + refs/ 文件；detached HEAD 回退 HEAD 原文）。
        /// </summary>
        /// <param name="rel">相对根路径（含 .git）</param>
        /// <param name="gitDir">.git 绝对路径</param>
        /// <returns>[git] 特征行——SummarizeFileTree 识别前缀不计入文件统计</returns>
        private static string CollectGitInfo(string rel, string gitDir)
        {
            string head = "";
            try
            {
                head = ReadTextShared(Path.Combine(gitDir, "HEAD"), Encoding.UTF8).Trim();
            }
            catch (Exception)
            {
                // HEAD 不可读——按空处理（无特征可报）
            }
            if (head.StartsWith("ref:", StringComparison.Ordinal))
            {
                string branch = head.Substring(4).Trim();
                string commit = "";
                string refPath = Path.Combine(gitDir, branch.Replace('/', Path.DirectorySeparatorChar));
                try
                {
                    commit = ReadTextShared(refPath, Encoding.UTF8).Trim();
                }
                catch (Exception)
                {
                    // ref 文件缺失（packed-refs 场景）——仅报 HEAD 指向
                }
                if (commit.Length > 7)
                {
                    commit = commit.Substring(0, 7);
                }
                return "[git] 存在 .git（" + rel + "——HEAD: " + branch + (commit.Length > 0 ? " → " + commit : "") + "）";
            }
            // detached HEAD——HEAD 即 commit
            if (head.Length > 7)
            {
                head = head.Substring(0, 7);
            }
            return "[git] 存在 .git（" + rel + "——HEAD: " + head + "）";
        }

        /// <summary>
        /// 按深度和数量上限列出稳定排序目录树
        /// </summary>
        /// <param name="path">受控目录</param>
        /// <param name="depth">零到十层</param>
        /// <param name="limit">最大条数</param>
        /// <returns>相对目录路径</returns>
        public string[] Tree(string path, int depth, int limit)
        {
            if (depth < 0 || depth > 10 || limit < 1 || limit > 10000)
            {
                throw new ArgumentOutOfRangeException("depth");
            }
            string root = Resolve(path, false);
            if (!Directory.Exists(root))
            {
                throw new DirectoryNotFoundException("Tree root was not found.");
            }
            List<string> output = new List<string>();
            List<string> gitLines = new List<string>();
            AppendTree(root, root, depth, limit, output, gitLines);
            // Q4 git 特征提示——[git] 前缀行追加末尾（SummarizeFileTree 识别前缀不计入文件统计；多 .git 多行）
            for (int i = 0; i < gitLines.Count && output.Count < limit; i = i + 1)
            {
                output.Add(gitLines[i]);
            }
            return output.ToArray();
        }

        /// <summary>
        /// 按文件名通配符搜索并稳定排序
        /// </summary>
        /// <param name="directory">受控目录</param>
        /// <param name="pattern">文件名模式</param>
        /// <param name="recursive">是否递归</param>
        /// <param name="limit">最大结果</param>
        /// <returns>相对搜索根的路径</returns>
        public string[] Find(string directory, string pattern, bool recursive, int limit)
        {
            if (string.IsNullOrWhiteSpace(pattern) || limit < 1 || limit > 10000)
            {
                throw new ArgumentException("Find parameters are invalid.", "pattern");
            }
            string root = Resolve(directory, false);
            List<string> files = new List<string>();
            int ignoredDirs = 0;
            AppendFind(root, root, pattern, recursive, limit, files, ref ignoredDirs);
            files.Sort(StringComparer.OrdinalIgnoreCase);
            int extra = ignoredDirs > 0 ? 1 : 0;
            string[] result = new string[files.Count + extra];
            for (int i = 0; i < files.Count; i = i + 1)
            {
                result[i] = Path.GetRelativePath(root, files[i]);
            }
            if (ignoredDirs > 0)
            {
                // Q4 忽略目录提示——不静默（find 与 tree 一致：忽略目录内条目不列出但计数可见）
                result[files.Count] = "[skip] " + ignoredDirs.ToString() + " 个忽略目录（.git/bin/obj/node_modules 等）未扫描——条目在其内已跳过";
            }
            return result;
        }
        /// <summary>
        /// 内容关键词搜索——受控根内递归扫描文本文件，返回 相对路径:行号:上下文（前后 ≤10 字符）
        /// </summary>
        /// <param name="directory">受控目录</param>
        /// <param name="keyword">关键词（大小写敏感）</param>
        /// <param name="pattern">文件名过滤（默认 *）</param>
        /// <param name="limit">最大结果</param>
        /// <returns>匹配行（相对搜索根的路径:行号:上下文）</returns>
        public string[] Grep(string directory, string keyword, string pattern, int limit)
        {
            if (string.IsNullOrWhiteSpace(keyword) || limit < 1 || limit > 10000)
            {
                throw new ArgumentException("Grep parameters are invalid.", "keyword");
            }

            string root = Resolve(directory, false);
            string filter = (string.IsNullOrWhiteSpace(pattern) || pattern == "*") ? "*" : pattern;
            List<string> files = new List<string>();
            int ignoredDirs = 0;
            AppendFind(root, root, filter, true, 10000, files, ref ignoredDirs);
            List<string> hits = new List<string>();
            List<string> skipped = new List<string>();
            for (int i = 0; i < files.Count && hits.Count < limit; i = i + 1)
            {
                string file = files[i];
                if (PathBoundary.IsReparsePoint(file))
                {
                    continue;
                }

                string relative = Path.GetRelativePath(root, file);
                try
                {
                    byte[] raw = ReadAllBytesShared(file);
                    System.Text.Encoding enc = TextFileCodec.DetectReadEncoding(file, raw);
                    int bom = (enc is UTF8Encoding u && u.GetPreamble().Length > 0 && raw.Length >= 3 && raw[0] == 0xEF && raw[1] == 0xBB && raw[2] == 0xBF) ? 3 : 0;
                    string[] lines = enc.GetString(raw, bom, raw.Length - bom).Replace("\r\n", "\n").Split('\n');
                    for (int n = 0; n < lines.Length && hits.Count < limit; n = n + 1)
                    {
                        string line = lines[n];
                        int idx = line.IndexOf(keyword, StringComparison.Ordinal);
                        if (idx >= 0)
                        {
                            int start = Math.Max(0, idx - 10);
                            int len = Math.Min(10 + keyword.Length + 10, line.Length - start);
                            string ctx = line.Substring(start, len);
                            hits.Add(relative + ":" + (n + 1).ToString() + ":" + ctx);
                        }
                    }
                }
                catch (Exception ex)
                {
                    // 不可读文件（二进制/权限/被独占）跳过——不静默：具名告警随结果返回（失败可见——判例 2026-09-15）
                    skipped.Add("[skip-file] " + relative + " | " + ex.GetType().Name);
                }
            }

            if (ignoredDirs > 0)
            {
                // Q4 忽略目录提示——不静默（grep 与 tree/find 一致：忽略目录内不扫描但计数可见）
                hits.Add("[skip] " + ignoredDirs.ToString() + " 个忽略目录（.git/bin/obj/node_modules 等）未扫描——匹配条目在其内已跳过");
            }

            for (int i = 0; i < skipped.Count; i = i + 1)
            {
                hits.Add(skipped[i]);
            }

            return hits.ToArray();
        }
        /// <summary>
        /// 移动文件且拒绝覆盖目标
        /// </summary>
        /// <param name="source">源路径</param>
        /// <param name="destination">目标路径</param>
        public void Move(string source, string destination)
        {
            string resolvedSource = Resolve(source, true);
            string resolvedDestination = Resolve(destination, true);
            lock (_writeGate)
            {
                if (File.Exists(resolvedDestination) || Directory.Exists(resolvedDestination))
                {
                    throw new IOException("Move destination already exists.");
                }
                EnsureParentDirectory(resolvedDestination);
                // P1 文件与目录双支持——目录整棵移动（含非空子目录）；自动建父目录（EnsureParentDirectory）
                if (Directory.Exists(resolvedSource))
                {
                    Directory.Move(resolvedSource, resolvedDestination);
                    return;
                }
                if (File.Exists(resolvedSource))
                {
                    File.Move(resolvedSource, resolvedDestination, false);
                    return;
                }
                throw new FileNotFoundException("Move source was not found.", resolvedSource);
            }
        }
        /// <summary>
        /// 将文件或目录（含非空目录——整棵子树）移动到受控回收站，并统计被移动内容
        /// </summary>
        /// <param name="path">目标路径</param>
        /// <returns>回收结果（落点 + 文件数 / 子目录数 / 字节数）</returns>
        public RecycleOutcome Recycle(string path)
        {
            string resolved = Resolve(path, true);
            string trimmed = resolved.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            for (int i = 0; i < _roots.Length; i = i + 1)
            {
                string root = _roots[i].TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                if (string.Equals(trimmed, root, StringComparison.OrdinalIgnoreCase))
                {
                    // 受控根本身不可回收——软删可恢复，但根被移走等于运行面整体失踪
                    throw new InvalidOperationException("Refusing to recycle a controlled root: " + resolved);
                }
            }
            lock (_writeGate)
            {
                string leaf = Path.GetFileName(trimmed);
                if (leaf.Length == 0)
                {
                    leaf = "root";
                }
                RecycleOutcome outcome = new RecycleOutcome();
                outcome.Target = Path.Combine(_recycleRoot,
                    DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffffffZ") + "-"
                    + Guid.NewGuid().ToString("N") + "-" + leaf);
                if (File.Exists(resolved))
                {
                    outcome.FileCount = 1;
                    outcome.TotalBytes = new FileInfo(resolved).Length;
                    File.Move(resolved, outcome.Target, false);
                    return outcome;
                }
                if (Directory.Exists(resolved))
                {
                    outcome.IsDirectory = true;
                    CollectRecycleStats(resolved, outcome);
                    Directory.Move(resolved, outcome.Target);
                    return outcome;
                }
                throw new FileNotFoundException("Recycle target was not found.", resolved);
            }
        }

        /// <summary>
        /// 回收统计采集——迁移前递归统计文件数 / 子目录数 / 字节数
        /// </summary>
        /// <param name="directory">目录</param>
        /// <param name="outcome">统计载体</param>
        private static void CollectRecycleStats(string directory, RecycleOutcome outcome)
        {
            foreach (string file in Directory.EnumerateFiles(directory))
            {
                outcome.FileCount = outcome.FileCount + 1;
                outcome.TotalBytes = outcome.TotalBytes + new FileInfo(file).Length;
            }
            foreach (string sub in Directory.EnumerateDirectories(directory))
            {
                outcome.DirectoryCount = outcome.DirectoryCount + 1;
                CollectRecycleStats(sub, outcome);
            }
        }

        /// <summary>
        /// 解析路径并验证归属和重解析点边界
        /// </summary>
        /// <param name="path">用户路径</param>
        /// <param name="forWrite">是否为写入</param>
        /// <returns>规范绝对路径</returns>
        public string Resolve(string path, bool forWrite)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                // v3 机制纯净——空路径是调用方错误（参数校验，不做产品级回落；LLM 适配归自举层语料）
                throw new ArgumentException("Path is empty.", "path");
            }
            // P8.5b 命名空间寻址——id:relative（受控根 id 前缀；LLM 工具路径语义——同 WorkspaceConfig.ResolveInjectFile；Windows 盘符如 C:\ 前缀不匹配则跳过）
            int nsSep = path.IndexOf(':');
            if (nsSep > 0)
            {
                string nsId = path.Substring(0, nsSep);
                string nsRel = path.Substring(nsSep + 1);
                for (int i = 0; i < _roots.Length; i = i + 1)
                {
                    if (string.Equals(_rootIds[i], nsId, StringComparison.OrdinalIgnoreCase))
                    {
                        path = Path.Combine(_roots[i], nsRel);
                        break;
                    }
                }
            }
            string resolved;
            if (Path.IsPathFullyQualified(path))
            {
                resolved = Path.GetFullPath(path);
            }
            else
            {
                resolved = Path.GetFullPath(Path.Combine(_roots[0], path));
            }
            string? owningRoot = FindOwningRoot(resolved);
            if (owningRoot == null)
            {
                throw new UnauthorizedAccessException("Path is outside allowed roots.");
            }
            // P8.5 只读根校验——forWrite 且归属根只读 → 拒绝（ccbp 等知识根默认只读）
            if (forWrite && IsReadOnlyRoot(owningRoot))
            {
                throw new UnauthorizedAccessException("Path is in a read-only root: " + owningRoot);
            }
            return PathBoundary.ResolveOwnedPath(owningRoot, resolved);
        }        /// <summary>
                 /// 显式遍历搜索目录，并拒绝进入或返回重解析点。
                 /// </summary>
                 /// <param name="root">搜索相对根</param>
                 /// <param name="current">当前目录</param>
                 /// <param name="pattern">文件名模式</param>
                 /// <param name="recursive">是否递归</param>
                 /// <param name="limit">结果上限</param>
                 /// <param name="output">绝对文件路径</param>
        private void AppendFind(string root, string current, string pattern,
            bool recursive, int limit, List<string> output, ref int ignoredDirs)
        {
            // [段0] **/ 目录通配前缀——剥前缀 + 强制递归（text-find 的 **/*.txt 语义；连续段剥净；剥空回退 *）
            string dirWild = "**/";
            while (pattern.StartsWith(dirWild, StringComparison.Ordinal))
            {
                pattern = pattern.Substring(dirWild.Length);
                recursive = true;
            }
            if (pattern.Length == 0)
            {
                pattern = "*";
            }
            if (output.Count >= limit)
            {
                return;
            }
            // [段1] 当前目录文件按稳定顺序加入，文件链接本身也不暴露
            string[] files = Directory.GetFiles(current, pattern, SearchOption.TopDirectoryOnly);
            Array.Sort(files, StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < files.Length && output.Count < limit; i = i + 1)
            {
                if (!PathBoundary.IsReparsePoint(files[i]))
                {
                    output.Add(files[i]);
                }
            }
            if (!recursive || output.Count >= limit)
            {
                return;
            }
            // [段2] 逐目录验证后递归，避免 AllDirectories 隐式穿过链接
            string[] directories = Directory.GetDirectories(current, "*", SearchOption.TopDirectoryOnly);
            Array.Sort(directories, StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < directories.Length && output.Count < limit; i = i + 1)
            {
                if (PathBoundary.IsReparsePoint(directories[i]))
                {
                    continue;
                }
                // Q4 忽略目录——跳过（.git/bin/obj 等；计数提示——不静默）
                if (IsIgnoredDir(Path.GetFileName(directories[i])))
                {
                    ignoredDirs = ignoredDirs + 1;
                    continue;
                }
                PathBoundary.ResolveOwnedPath(root, directories[i]);
                AppendFind(root, directories[i], pattern, true, limit, output, ref ignoredDirs);
            }
        }
        /// <summary>
        /// 递归追加目录树
        /// </summary>
        /// <param name="root">输出相对根</param>
        /// <param name="current">当前目录</param>
        /// <param name="depth">剩余深度</param>
        /// <param name="limit">条数上限</param>
        /// <param name="output">输出列表</param>
        private void AppendTree(string root, string current, int depth, int limit, List<string> output, List<string> gitLines)
        {
            if (output.Count >= limit)
            {
                return;
            }
            string[] entries = Directory.GetFileSystemEntries(current);
            Array.Sort(entries, StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < entries.Length && output.Count < limit; i = i + 1)
            {
                string rel = Path.GetRelativePath(root, entries[i]);
                bool isDir = Directory.Exists(entries[i]);
                // Q4 忽略目录——.git 记录特征行（不列出），其余忽略名单目录静默跳过（子项不展开；显式以忽略名为根不受影响）
                if (isDir && IsIgnoredDir(Path.GetFileName(entries[i])))
                {
                    if (string.Equals(Path.GetFileName(entries[i]), ".git", StringComparison.OrdinalIgnoreCase))
                    {
                        gitLines.Add(CollectGitInfo(rel, entries[i]));
                    }
                    continue;
                }
                // 目录行尾加 "/" 标记——消费面（ToolSummaryFormatter.SummarizeFileTree）按尾斜杠区分目录/文件
                if (isDir)
                {
                    rel = rel + "/";
                }
                output.Add(rel);
                if (depth > 0 && isDir
                    && !PathBoundary.IsReparsePoint(entries[i]))
                {
                    AppendTree(root, entries[i], depth - 1, limit, output, gitLines);
                }
            }
        }
        /// <summary>
        /// 寻找包含目标的最长允许根
        /// </summary>
        /// <param name="path">绝对路径</param>
        /// <returns>允许根或 null</returns>
        private string? FindOwningRoot(string path)
        {
            string? owner = null;
            for (int i = 0; i < _roots.Length; i = i + 1)
            {
                string root = _roots[i];
                if (string.Equals(path, root, StringComparison.OrdinalIgnoreCase)
                    || path.StartsWith(root + Path.DirectorySeparatorChar.ToString(), StringComparison.OrdinalIgnoreCase))
                {
                    if (owner == null || root.Length > owner.Length)
                    {
                        owner = root;
                    }
                }
            }
            return owner;
        }

        /// <summary>
        /// 统计非重叠精确子串数量
        /// </summary>
        /// <param name="content">正文</param>
        /// <param name="target">目标</param>
        /// <returns>数量</returns>
        private int CountOccurrences(string content, string target)
        {
            int count = 0;
            int index = 0;
            while (index <= content.Length - target.Length)
            {
                int found = content.IndexOf(target, index, StringComparison.Ordinal);
                if (found < 0)
                {
                    return count;
                }
                count = count + 1;
                index = found + target.Length;
            }
            return count;
        }

        /// <summary>
        /// 同目录临时文件原子替换
        /// </summary>
        /// <param name="path">目标路径</param>
        /// <param name="content">正文</param>
        private void WriteAtomic(string path, string content)
        {
            EnsureParentDirectory(path);
            // 原子写——统一实现 ConfigStore.AtomicWrite（审查修复轮 2026-08-11 决策3）
            ConfigStore.AtomicWrite(path, content);
        }
        /// <summary>
        /// 创建目标父目录
        /// </summary>
        /// <param name="path">文件路径</param>
        private void EnsureParentDirectory(string path)
        {
            string? directory = Path.GetDirectoryName(path);
            if (directory == null)
            {
                throw new InvalidOperationException("Parent directory is unavailable.");
            }
            Directory.CreateDirectory(directory);
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

        /// <summary>
        /// 建立受控根条目版构造——含只读标志（writable=false 根拒绝写操作；design-ch4-workspace §三）
        /// </summary>
        /// <param name="rootEntries">受控根条目（id + 路径 + 可写标志）</param>
        /// <param name="recycleRoot">回收目录</param>
        public FileSystemService(WorkspaceConfig.RootEntry[] rootEntries, string recycleRoot)
        {
            if (rootEntries == null || rootEntries.Length == 0)
            {
                throw new ArgumentException("At least one filesystem root is required.", "rootEntries");
            }
            _roots = new string[rootEntries.Length];
            _readOnly = new bool[rootEntries.Length];
            _rootIds = new string[rootEntries.Length];
            for (int i = 0; i < rootEntries.Length; i = i + 1)
            {
                WorkspaceConfig.RootEntry entry = rootEntries[i];
                if (entry == null || string.IsNullOrWhiteSpace(entry.Path))
                {
                    throw new ArgumentException("Filesystem root is empty.", "rootEntries");
                }
                _roots[i] = PathBoundary.NormalizeRoot(entry.Path);
                _readOnly[i] = !entry.Writable;
                string entryId = entry.Id;
                if (entryId == null || entryId.Length == 0)
                {
                    entryId = "root" + i.ToString();
                }
                _rootIds[i] = entryId;
            }
            _recycleRoot = Resolve(recycleRoot, true);
            Directory.CreateDirectory(_recycleRoot);
            _writeGate = new object();
        }

        /// <summary>
        /// 查询根是否只读——按归属根路径匹配（Resolve forWrite 校验用）
        /// </summary>
        /// <param name="rootPath">归属根路径</param>
        /// <returns>true=只读（拒绝写操作）</returns>
        private bool IsReadOnlyRoot(string rootPath)
        {
            for (int i = 0; i < _roots.Length; i = i + 1)
            {
                if (string.Equals(_roots[i], rootPath, StringComparison.OrdinalIgnoreCase))
                {
                    return _readOnly[i];
                }
            }
            return false;
        }
    }

    /// <summary>
    /// 路径边界证明——根归属 + 重解析点防护
    /// </summary>
    internal static class PathBoundary
    {
        /// <summary>
        /// 规范并创建受控根，同时拒绝根本身为重解析点。
        /// </summary>
        /// <param name="root">受控根路径</param>
        /// <returns>不带多余末尾分隔符的绝对路径</returns>
        internal static string NormalizeRoot(string root)
        {
            if (string.IsNullOrWhiteSpace(root))
            {
                throw new ArgumentException("Controlled root is empty.", "root");
            }
            string normalized = TrimEndingSeparator(Path.GetFullPath(root));
            Directory.CreateDirectory(normalized);
            if (IsReparsePoint(normalized))
            {
                throw new UnauthorizedAccessException("Controlled root cannot be a reparse point.");
            }
            return normalized;
        }

        /// <summary>
        /// 证明目标仍在受控根内且现存路径没有穿过重解析点。
        /// </summary>
        /// <param name="root">已规范的受控根</param>
        /// <param name="path">目标绝对或相对路径</param>
        /// <returns>规范目标绝对路径</returns>
        internal static string ResolveOwnedPath(string root, string path)
        {
            string normalized = Path.GetFullPath(path);
            string prefix = root + Path.DirectorySeparatorChar.ToString();
            if (!string.Equals(normalized, root, StringComparison.OrdinalIgnoreCase)
                && !normalized.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                throw new UnauthorizedAccessException("Controlled path escaped its owned root.");
            }
            RejectExistingReparsePoints(root, normalized);
            return normalized;
        }

        /// <summary>
        /// 判断现存文件或目录是否为重解析点。
        /// </summary>
        /// <param name="path">现存路径</param>
        /// <returns>是否为重解析点</returns>
        internal static bool IsReparsePoint(string path)
        {
            return (File.GetAttributes(path) & FileAttributes.ReparsePoint) == FileAttributes.ReparsePoint;
        }

        /// <summary>
        /// 拒绝从受控根到目标的任一现存重解析点。
        /// </summary>
        /// <param name="root">受控根</param>
        /// <param name="path">根内目标</param>
        private static void RejectExistingReparsePoints(string root, string path)
        {
            if (IsReparsePoint(root))
            {
                throw new UnauthorizedAccessException("Controlled root cannot be a reparse point.");
            }
            string relative = Path.GetRelativePath(root, path);
            string[] segments = relative.Split(new char[]
            {
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar
            }, StringSplitOptions.RemoveEmptyEntries);
            string current = root;
            for (int i = 0; i < segments.Length; i = i + 1)
            {
                current = Path.Combine(current, segments[i]);
                if ((File.Exists(current) || Directory.Exists(current)) && IsReparsePoint(current))
                {
                    throw new UnauthorizedAccessException("Controlled path crosses a reparse point.");
                }
            }
        }

        /// <summary>
        /// 移除普通目录末尾分隔符但保留卷根。
        /// </summary>
        /// <param name="path">绝对目录</param>
        /// <returns>规范目录</returns>
        private static string TrimEndingSeparator(string path)
        {
            string? volumeRoot = Path.GetPathRoot(path);
            if (volumeRoot != null && string.Equals(path, volumeRoot, StringComparison.OrdinalIgnoreCase))
            {
                return path;
            }
            return path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
    }
}

