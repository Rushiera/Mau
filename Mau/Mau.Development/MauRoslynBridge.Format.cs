using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Formatting;
using Microsoft.CodeAnalysis.Options;
using Microsoft.CodeAnalysis.Text;

namespace Mau.Development
{
#pragma warning disable CS8600 // 环境 SDK 对 ConcurrentDictionary/Dictionary TryXXX(out TValue) 的 MaybeNullWhen 分析异常误报——out 实参非空变量也报警（最小复现验证）
    /// <summary>
    /// MauRoslynBridge 格式面——cs.format 空白 / 缩进规整（Roslyn Formatter，partial 分部）。
    /// 边界：只做空白与缩进规整（不做语法糖展开 / 单行块拆分 / 文档风格改写）——语义零变更由 token 流 + directive 双向校验保证。
    /// mode=check 干跑（差异清单，零写入——探针面）/ mode=apply 写盘（保真 BOM 与换行，原子替换）。
    /// </summary>
    public sealed partial class MauRoslynBridge
    {
        /// <summary>
        /// cs.format——空白 / 缩进规整。参数：path（.cs 文件 / 目录 / csproj，受控根内）+ mode（check|apply，默认 check）。
        /// </summary>
        /// <param name="args">参数</param>
        /// <param name="result">结果</param>
        /// <returns>调用完成</returns>
        private bool ToolFormat(JsonElement args, out string result)
        {
            // [段1] 参数面校验——零容忍（未知参数 / 缺值 / 非法值一律拒绝）
            string pathParam;
            string mode;
            string badArgs = ValidateFormatArgs(args, out pathParam, out mode);
            if (badArgs.Length > 0)
            {
                result = badArgs;
                return false;
            }

            // [段2] 入口路径解析——文件 / 目录 / csproj（受控根内）
            string entry = ResolveEntryPath(pathParam);
            if (entry.Length == 0)
            {
                result = "ERR|BAD_PATH|路径无效或越界（受控根内，支持 .cs 文件 / 目录 / csproj）: " + pathParam;
                return false;
            }

            // [段3] 目标文件收集——排除面内建（bin/obj/node_modules/Flows/Data/CatTemp/Mau-public + 生成物 FL_*/BRIKGROUP*）
            List<string> files = new List<string>();
            CollectCsFiles(entry, files);
            files.Sort(StringComparer.OrdinalIgnoreCase);
            if (files.Count == 0)
            {
                result = "OK|FORMAT_" + mode.ToUpperInvariant() + "|未找到目标 .cs 文件（排除 bin/obj/node_modules/Flows/Data/CatTemp/Mau-public 与生成物）: " + pathParam;
                return true;
            }

            // [段4] 逐文件规整——校验不通过即跳过（令牌流 / directive / 幂等三重校验）
            StringBuilder sb = new StringBuilder();
            int changedFiles = 0;
            int changedLines = 0;
            int writtenFiles = 0;
            int failedFiles = 0;
            for (int i = 0; i < files.Count; i = i + 1)
            {
                string file = files[i];
                string source = File.ReadAllText(file);
                string newline = TargetNewline();
                string formatted = FormatText(source, file, newline);
                if (string.Equals(formatted, source, StringComparison.Ordinal))
                {
                    continue;
                }
                string failure = VerifyFormat(source, formatted, file, newline);
                if (failure.Length > 0)
                {
                    failedFiles = failedFiles + 1;
                    sb.AppendLine("VERIFY_FAIL|" + RelativeToRoots(file) + "|" + failure);
                    continue;
                }
                int lines = CountChangedLines(source, formatted);
                changedFiles = changedFiles + 1;
                changedLines = changedLines + lines;
                if (mode == "apply")
                {
                    WriteFilePreserving(file, formatted, HasUtf8Bom(file));
                    writtenFiles = writtenFiles + 1;
                }
                sb.AppendLine(RelativeToRoots(file) + " | lines=" + lines.ToString());
            }

            // [段5] 结果拼装
            string head;
            if (mode == "apply")
            {
                head = "OK|FORMAT_APPLY|扫描 " + files.Count.ToString() + " 文件，规整 " + changedFiles.ToString() + " 文件 / " + changedLines.ToString() + " 行，写入 " + writtenFiles.ToString() + " 文件";
            }
            else
            {
                head = "OK|FORMAT_CHECK|扫描 " + files.Count.ToString() + " 文件，需规整 " + changedFiles.ToString() + " 文件 / " + changedLines.ToString() + " 行（零写入）";
            }
            if (failedFiles > 0)
            {
                head = head + "，校验未通过跳过 " + failedFiles.ToString() + " 文件";
            }
            StringBuilder output = new StringBuilder();
            output.AppendLine(head);
            output.Append(sb.ToString());
            if (mode == "check")
            {
                output.AppendLine("—— 干跑完成：mode=apply 执行写盘（保真 BOM / 换行，原子替换）");
            }
            result = TrimResult(output.ToString(), MaxResultChars);
            return true;
        }

        /// <summary>format 参数面校验——白名单字段（path / mode）+ 缺值 / 非法值拒绝；宿主注入保留键（catId，见 IsHostInjectedArg）放行——该键不进工具声明面，非 LLM 可见参数。</summary>
        /// <param name="args">参数对象</param>
        /// <param name="pathParam">出参：路径</param>
        /// <param name="mode">出参：模式</param>
        /// <returns>错误文本（空=通过）</returns>
        private static string ValidateFormatArgs(JsonElement args, out string pathParam, out string mode)
        {
            pathParam = "";
            mode = "check";
            if (args.ValueKind != JsonValueKind.Object)
            {
                return "ERR|BAD_ARGS|参数必须是 JSON 对象";
            }

            foreach (JsonProperty property in args.EnumerateObject())
            {
                if (property.Name != "path" && property.Name != "mode" && !IsHostInjectedArg(property.Name))
                {
                    return "ERR|BAD_ARGS|未知参数: " + property.Name + "（支持 path / mode）";
                }
            }

            pathParam = Arg(args, "path");
            if (pathParam.Length == 0)
            {
                return "ERR|BAD_ARGS|缺参数 path（.cs 文件 / 目录 / csproj，受控根内）";
            }

            string modeArg = Arg(args, "mode");
            if (modeArg.Length > 0)
            {
                if (modeArg != "check" && modeArg != "apply")
                {
                    return "ERR|BAD_ARGS|mode 非法值: " + modeArg + "（check|apply）";
                }

                mode = modeArg;
            }

            return "";
        }

        /// <summary>
        /// 入口路径解析——文件 / 目录 / csproj（受控根校验；对齐 ResolveProject 的 id: 命名空间寻址语义）。
        /// 与 ResolveProject 的分工：本方法不要求 csproj 存在（format 面向文件树），项目面解析仍走 ResolveProject。
        /// </summary>
        /// <param name="pathParam">路径参数</param>
        /// <returns>绝对路径（越界 / 缺失返回空串）</returns>
        private string ResolveEntryPath(string pathParam)
        {
            if (string.IsNullOrWhiteSpace(pathParam))
            {
                return "";
            }
            string path = pathParam;
            int nsSep = path.IndexOf(':');
            if (nsSep > 0)
            {
                string nsId = path.Substring(0, nsSep);
                string nsRel = path.Substring(nsSep + 1);
                for (int i = 0; i < _roots.Length; i = i + 1)
                {
                    if (string.Equals(_rootIds[i], nsId, StringComparison.Ordinal))
                    {
                        path = Path.Combine(_roots[i], nsRel);
                        break;
                    }
                }
            }
            string full;
            if (Path.IsPathFullyQualified(path))
            {
                full = Path.GetFullPath(path);
            }
            else
            {
                full = Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, path));
            }
            for (int i = 0; i < _roots.Length; i = i + 1)
            {
                if (full.StartsWith(_roots[i] + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(full, _roots[i], StringComparison.OrdinalIgnoreCase))
                {
                    return full;
                }
            }
            return "";
        }

        /// <summary>
        /// 目标文件收集——.cs 文件 / 目录递归 / csproj（取所在目录）；排除面内建。
        /// </summary>
        /// <param name="entry">入口绝对路径</param>
        /// <param name="files">收集结果</param>
        private static void CollectCsFiles(string entry, List<string> files)
        {
            if (File.Exists(entry))
            {
                if (entry.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
                {
                    string directory = Path.GetDirectoryName(entry) ?? "";
                    if (directory.Length > 0)
                    {
                        CollectDirectory(directory, files);
                    }
                    return;
                }
                if (entry.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
                {
                    files.Add(entry);
                }
                return;
            }
            if (Directory.Exists(entry))
            {
                CollectDirectory(entry, files);
            }
        }

        /// <summary>
        /// 目录递归收集——排除 bin/obj/node_modules/Flows/Data/CatTemp/Mau-public 与生成物（FL_* / BRIKGROUP*）。
        /// </summary>
        /// <param name="dir">目录</param>
        /// <param name="files">收集结果</param>
        private static void CollectDirectory(string dir, List<string> files)
        {
            string[] found = Directory.GetFiles(dir, "*.cs");
            for (int i = 0; i < found.Length; i = i + 1)
            {
                string name = Path.GetFileName(found[i]);
                if (name.StartsWith("FL_", StringComparison.Ordinal) || name.StartsWith("BRIKGROUP", StringComparison.Ordinal))
                {
                    continue;
                }
                files.Add(found[i]);
            }
            string[] subdirs = Directory.GetDirectories(dir);
            for (int i = 0; i < subdirs.Length; i = i + 1)
            {
                string name = Path.GetFileName(subdirs[i]);
                if (name.Length == 0 || name[0] == '.')
                {
                    continue;
                }
                string lower = name.ToLowerInvariant();
                if (lower == "bin" || lower == "obj" || lower == "node_modules" || lower == "flows" || lower == "data" || lower == "cattemp" || lower == "mau-public")
                {
                    continue;
                }
                CollectDirectory(subdirs[i], files);
            }
        }

        /// <summary>
        /// 相对受控根路径——诊断输出可读性（不在任何根内时回落绝对路径）。
        /// </summary>
        /// <param name="filePath">文件绝对路径</param>
        /// <returns>相对路径</returns>
        private string RelativeToRoots(string filePath)
        {
            for (int i = 0; i < _roots.Length; i = i + 1)
            {
                if (filePath.StartsWith(_roots[i] + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                {
                    return filePath.Substring(_roots[i].Length + 1);
                }
            }
            return filePath;
        }

        /// <summary>
        /// 文本规整——AdhocWorkspace + Formatter（4 空格缩进 / 显式换行；只调空白与缩进，不动 token）。
        /// </summary>
        /// <param name="text">源码文本</param>
        /// <param name="path">文件路径（诊断定位用）</param>
        /// <param name="newline">目标换行</param>
        /// <returns>规整后文本</returns>
        private static string FormatText(string text, string path, string newline)
        {
            using (AdhocWorkspace workspace = new AdhocWorkspace())
            {
                OptionSet options = FormatOptions(workspace, newline);
                SourceText sourceText = SourceText.From(text, Encoding.UTF8);
                Project project = workspace.AddProject("cs-format", LanguageNames.CSharp);
                Document document = project.AddDocument(path, sourceText);
                Document formatted = Formatter.FormatAsync(document, options).GetAwaiter().GetResult();
                return formatted.GetTextAsync().GetAwaiter().GetResult().ToString();
            }
        }

        /// <summary>
        /// 写前校验——token 流等价 + directive 等价 + 幂等（任一不满足即拒写）。
        /// 动机：Formatter 只应改空白；token 流等价 ⊆ 语义等价，且比编译验证便宜（零编译成本）。
        /// </summary>
        /// <param name="source">原文</param>
        /// <param name="formatted">规整后</param>
        /// <param name="path">文件路径</param>
        /// <param name="newline">目标换行</param>
        /// <returns>失败原因（空=通过）</returns>
        private static string VerifyFormat(string source, string formatted, string path, string newline)
        {
            if (!TokensEqual(source, formatted))
            {
                return "token 流不等价（规整动了代码而非空白）";
            }
            if (!DirectivesEqual(source, formatted))
            {
                return "预处理指令不等价（#if/#pragma 面变动）";
            }
            string again = FormatText(formatted, path, newline);
            if (!string.Equals(again, formatted, StringComparison.Ordinal))
            {
                return "非幂等（二次规整仍有变化）";
            }
            return "";
        }

        /// <summary>
        /// token 流等价——Kind + ValueText 序列（trivia 无关）。
        /// </summary>
        /// <param name="a">原文</param>
        /// <param name="b">对照文本</param>
        /// <returns>等价</returns>
        private static bool TokensEqual(string a, string b)
        {
            List<string> left = TokenList(a);
            List<string> right = TokenList(b);
            if (left.Count != right.Count)
            {
                return false;
            }
            for (int i = 0; i < left.Count; i = i + 1)
            {
                if (!string.Equals(left[i], right[i], StringComparison.Ordinal))
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// token 序列提取。
        /// </summary>
        /// <param name="text">文本</param>
        /// <returns>序列</returns>
        private static List<string> TokenList(string text)
        {
            List<string> list = new List<string>();
            SyntaxNode root = CSharpSyntaxTree.ParseText(text).GetRoot();
            foreach (SyntaxToken token in root.DescendantTokens())
            {
                list.Add(token.Kind().ToString() + "|" + token.ValueText);
            }
            return list;
        }

        /// <summary>
        /// directive 等价——预处理指令序列（trivia 面，token 流不含）。
        /// </summary>
        /// <param name="a">原文</param>
        /// <param name="b">对照文本</param>
        /// <returns>等价</returns>
        private static bool DirectivesEqual(string a, string b)
        {
            List<string> left = DirectiveList(a);
            List<string> right = DirectiveList(b);
            if (left.Count != right.Count)
            {
                return false;
            }
            for (int i = 0; i < left.Count; i = i + 1)
            {
                if (!string.Equals(left[i], right[i], StringComparison.Ordinal))
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// directive 序列提取——Kind + 去空白文本。
        /// </summary>
        /// <param name="text">文本</param>
        /// <returns>序列</returns>
        private static List<string> DirectiveList(string text)
        {
            List<string> list = new List<string>();
            SyntaxNode root = CSharpSyntaxTree.ParseText(text).GetRoot();
            foreach (SyntaxTrivia trivia in root.DescendantTrivia())
            {
                if (trivia.IsDirective)
                {
                    list.Add(trivia.Kind().ToString() + "|" + trivia.ToString().Trim());
                }
            }
            return list;
        }

        /// <summary>
        /// 目标换行——仓库 .cs 一律 CRLF（行尾归一是空白规整的一部分：新建文件的 LF 在此收敛）。
        /// </summary>
        /// <returns>换行串</returns>
        private static string TargetNewline()
        {
            return "\r\n";
        }

        /// <summary>
        /// UTF-8 BOM 探测——保真写回（有则保留，无则不添加）。
        /// </summary>
        /// <param name="path">文件路径</param>
        /// <returns>有 BOM</returns>
        private static bool HasUtf8Bom(string path)
        {
            using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                int b0 = stream.ReadByte();
                int b1 = stream.ReadByte();
                int b2 = stream.ReadByte();
                return b0 == 0xEF && b1 == 0xBB && b2 == 0xBF;
            }
        }

        /// <summary>
        /// 保真原子落盘——临时文件 + Move 覆盖（防半截写入）；编码 / BOM 按原文状态。
        /// </summary>
        /// <param name="path">目标文件</param>
        /// <param name="text">完整新内容</param>
        /// <param name="bom">是否写 UTF-8 BOM</param>
        private static void WriteFilePreserving(string path, string text, bool bom)
        {
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temporary, text, new UTF8Encoding(bom));
                File.Move(temporary, path, true);
            }
            finally
            {
                if (File.Exists(temporary))
                {
                    File.Delete(temporary);
                }
            }
        }

        /// <summary>
        /// 差异行数——按行对齐比较（行内空白差异计入）。
        /// </summary>
        /// <param name="a">原文</param>
        /// <param name="b">对照文本</param>
        /// <returns>不同行数</returns>
        private static int CountChangedLines(string a, string b)
        {
            string[] left = NormalizeNewLineText(a, "\n").Split('\n');
            string[] right = NormalizeNewLineText(b, "\n").Split('\n');
            int count = 0;
            int total = left.Length > right.Length ? left.Length : right.Length;
            for (int i = 0; i < total; i = i + 1)
            {
                string x = i < left.Length ? left[i] : "";
                string y = i < right.Length ? right[i] : "";
                if (!string.Equals(x, y, StringComparison.Ordinal))
                {
                    count = count + 1;
                }
            }
            return count;
        }
    }
}
