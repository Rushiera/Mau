using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Mau.Development
{
#pragma warning disable CS8600 // 环境 SDK 对 ConcurrentDictionary/Dictionary TryXXX(out TValue) 的 MaybeNullWhen 分析异常误报——out 实参非空变量也报警（最小复现验证）
    /// <summary>
    /// MauRoslynBridge 查询面——cs.check / cs.build / cs.list / cs.read（partial 分部）。
    /// </summary>
    public sealed partial class MauRoslynBridge
    {
        /// <summary>
        /// cs.check——语法树诊断（Compilation.GetDiagnostics 增量语义；full=含警告）
        /// </summary>
        /// <param name="args">参数</param>
        /// <param name="result">结果</param>
        /// <returns>调用完成</returns>
        private bool ToolCheck(JsonElement args, out string result)
        {
            string path = Arg(args, "path");
            bool full = Arg(args, "full") == "true";
            string resolveError;
            List<string> projects = ResolveProjects(path, out resolveError);
            if (projects.Count == 0)
            {
                result = "ERR|BAD_PATH|" + resolveError + ": " + path;
                return false;
            }
            if (projects.Count == 1)
            {
                return CheckSingle(projects[0], full, out result);
            }
            StringBuilder sb = new StringBuilder();
            sb.Append("聚合 " + projects.Count + " 个项目：" + Environment.NewLine);
            int failed = 0;
            for (int i = 0; i < projects.Count; i = i + 1)
            {
                sb.Append("── " + RelativeToRoots(projects[i]) + " ──" + Environment.NewLine);
                string one;
                CheckSingle(projects[i], full, out one);
                sb.Append(one + Environment.NewLine);
                if (one.StartsWith("FAIL|CHECK|", StringComparison.Ordinal))
                {
                    failed = failed + 1;
                }
            }
            sb.Append("—— 聚合结果：" + projects.Count + " 项目 / " + failed + " 项目有错误");
            result = TrimResult(sb.ToString(), MaxResultChars);
            return true;
        }
        /// <summary>
        /// 单项目 check——语法树诊断（Compilation.GetDiagnostics 增量语义；full=含警告）+ CS5001 假阳性单列标注。
        /// </summary>
        /// <param name="csproj">csproj 绝对路径</param>
        /// <param name="full">是否输出全部警告</param>
        /// <param name="result">结果文本</param>
        /// <returns>调用完成</returns>
        private bool CheckSingle(string csproj, bool full, out string result)
        {
            ProjectCache cache = EnsureProject(csproj);
            FullScan(cache);
            CSharpCompilation compilation = cache.Compilation;
            System.Collections.Immutable.ImmutableArray<Diagnostic> diagnostics = compilation.GetDiagnostics();
            List<Diagnostic> errors = new List<Diagnostic>();
            List<Diagnostic> warnings = new List<Diagnostic>();
            int missedEntryPoint = 0;
            for (int i = 0; i < diagnostics.Length; i = i + 1)
            {
                Diagnostic diagnostic = diagnostics[i];
                if (diagnostic.Severity == DiagnosticSeverity.Error)
                {
                    // CS5001（无入口点）——构建期 targets 生成的入口点在 Roslyn 快照不可见：单列标注，不占错误计数（裁决走 cs.build）
                    if (diagnostic.Id == "CS5001")
                    {
                        missedEntryPoint = missedEntryPoint + 1;
                        continue;
                    }
                    errors.Add(diagnostic);
                }
                else if (diagnostic.Severity == DiagnosticSeverity.Warning)
                {
                    warnings.Add(diagnostic);
                }
            }
            errors.Sort(DiagnosticComparer.Instance);
            warnings.Sort(DiagnosticComparer.Instance);
            StringBuilder sb = new StringBuilder();
            if (errors.Count == 0)
            {
                sb.Append("OK 项目 " + cache.AssemblyName + " 0 错误 " + warnings.Count + " 警告" + (full ? "" : "（全量诊断见 full=true）") + "——语义快查，实机裁决走 cs.build");
            }
            else
            {
                sb.Append("FAIL|CHECK|项目 " + cache.AssemblyName + " " + errors.Count + " 错误");
                for (int i = 0; i < errors.Count; i = i + 1)
                {
                    sb.Append(Environment.NewLine);
                    sb.Append(FormatDiagnostic(cache, errors[i]));
                }
            }
            if (full && warnings.Count > 0)
            {
                sb.Append(Environment.NewLine);
                sb.Append("—— " + warnings.Count + " 警告:");
                for (int i = 0; i < warnings.Count; i = i + 1)
                {
                    sb.Append(Environment.NewLine);
                    sb.Append(FormatDiagnostic(cache, warnings[i]));
                }
            }
            if (missedEntryPoint > 0)
            {
                sb.Append(Environment.NewLine);
                sb.Append("ⓘ CS5001 疑似假阳性 ×" + missedEntryPoint + "（入口点由构建期 targets 生成——Roslyn 快照不可见；裁决走 cs.build）——不占错误计数");
            }
            result = TrimResult(sb.ToString(), MaxResultChars);
            return true;
        }

        /// <summary>
        /// cs.build——实机裁决（dotnet build 子进程）；成功后引用集置脏（下次语义用新产物）
        /// </summary>
        /// <param name="args">参数</param>
        /// <param name="result">结果</param>
        /// <returns>调用完成</returns>
        private bool ToolBuild(JsonElement args, out string result)
        {
            string path = Arg(args, "path");
            string resolveError;
            List<string> projects = ResolveProjects(path, out resolveError);
            if (projects.Count == 0)
            {
                result = "ERR|BAD_PATH|" + resolveError + ": " + path;
                return false;
            }
            string slnPath = ResolveSolutionPath(path);
            if (slnPath.Length > 0)
            {
                return BuildOne(slnPath, out result);
            }
            if (projects.Count == 1)
            {
                return BuildOne(projects[0], out result);
            }
            StringBuilder sb = new StringBuilder();
            sb.Append("聚合 " + projects.Count + " 个项目：" + Environment.NewLine);
            int failed = 0;
            for (int i = 0; i < projects.Count; i = i + 1)
            {
                sb.Append("── " + RelativeToRoots(projects[i]) + " ──" + Environment.NewLine);
                string one;
                BuildOne(projects[i], out one);
                sb.Append(one + Environment.NewLine);
                if (one.StartsWith("FAIL|BUILD|", StringComparison.Ordinal))
                {
                    failed = failed + 1;
                }
            }
            sb.Append("—— 聚合结果：" + projects.Count + " 项目 / " + failed + " 失败");
            result = TrimResult(sb.ToString(), MaxResultChars);
            return true;
        }
        /// <summary>
        /// 单目标 build——dotnet build 子进程（csproj 或 .sln）；成功后引用集置脏（下次语义用新产物）。
        /// </summary>
        /// <param name="target">csproj / .sln 绝对路径</param>
        /// <param name="result">结果文本</param>
        /// <returns>调用完成</returns>
        private bool BuildOne(string target, out string result)
        {
            // 轻量缓存条目——不 Parse 全树（build 只裁决，不建编译态）
            ProjectCache cache = null!;
            if (!_pool.TryGetValue(target, out cache))
            {
                cache = new ProjectCache();
                cache.CsprojPath = target;
                cache.ProjectDir = Path.GetDirectoryName(target) ?? "";
                cache.AssemblyName = Path.GetFileNameWithoutExtension(target);
                cache.ReferencesDirty = true;
                _pool[target] = cache;
            }
            cache.LastAccess = Environment.TickCount64;
            // ProcessRunner 统一执行器——双流并行读 + watchdog 强杀（防顺序 ReadToEnd 管道死锁——Codex 审查 P1）
            ProcessRunResult run = ProcessRunner.RunAndCapture("dotnet", "build \"" + target + "\" --nologo", cache.ProjectDir, 120000);
            if (!run.Started)
            {
                result = "ERR|BUILD_START|dotnet 进程启动失败（PATH 中无 dotnet？）";
                return false;
            }
            if (!run.Exited)
            {
                result = "ERR|BUILD_TIMEOUT|dotnet build 超时（120s）——长首次还原可重试";
                return false;
            }
            string tail = TailLines(run.Stdout + run.Stderr, 20);
            if (run.ExitCode != 0)
            {
                result = TrimResult("FAIL|BUILD|dotnet build exit " + run.ExitCode + Environment.NewLine + tail, MaxResultChars);
                return true;
            }
            cache.ReferencesDirty = true;
            result = TrimResult("OK 构建成功: " + cache.AssemblyName + Environment.NewLine + tail, MaxResultChars);
            return true;
        }

        /// <summary>
        /// 解决方案入口探测——传 .sln 时返回绝对路径（否则空串）；用于 build 直达 sln（不经逐项目展开）。
        /// </summary>
        /// <param name="pathParam">路径参数</param>
        /// <returns>.sln 绝对路径（非 sln / 越界返回空串）</returns>
        private string ResolveSolutionPath(string pathParam)
        {
            string full = ResolveInRoots(pathParam);
            if (full.Length > 0 && File.Exists(full) && full.EndsWith(".sln", StringComparison.OrdinalIgnoreCase))
            {
                return full;
            }
            return "";
        }
        /// <summary>
        /// cs.list——类/成员签名（语法层提取，无需语义）
        /// </summary>
        /// <param name="args">参数</param>
        /// <param name="result">结果</param>
        /// <returns>调用完成</returns>
        private bool ToolList(JsonElement args, out string result)
        {
            string path = Arg(args, "path");
            string className = Arg(args, "class");
            string resolveError;
            List<string> projects = ResolveProjects(path, out resolveError);
            if (projects.Count == 0)
            {
                result = "ERR|BAD_PATH|" + resolveError + ": " + path;
                return false;
            }
            if (projects.Count == 1)
            {
                return ListSingle(projects[0], className, out result);
            }
            StringBuilder sb = new StringBuilder();
            sb.Append("聚合 " + projects.Count + " 个项目：" + Environment.NewLine);
            for (int i = 0; i < projects.Count; i = i + 1)
            {
                sb.Append("── " + RelativeToRoots(projects[i]) + " ──" + Environment.NewLine);
                string one;
                ListSingle(projects[i], className, out one);
                sb.Append(one + Environment.NewLine);
            }
            result = TrimResult(sb.ToString(), MaxResultChars);
            return true;
        }
        /// <summary>
        /// 单项目 list——类/成员签名清单（语法层提取，无需语义）。
        /// </summary>
        /// <param name="csproj">csproj 绝对路径</param>
        /// <param name="className">类名（空=全项目类清单）</param>
        /// <param name="result">结果文本</param>
        /// <returns>true=命中；false=类不存在（result 为 ERR 文本）</returns>
        private bool ListSingle(string csproj, string className, out string result)
        {
            ProjectCache cache = EnsureProject(csproj);
            FullScan(cache);
            StringBuilder sb = new StringBuilder();
            if (className.Length == 0)
            {
                // 全项目类清单
                int classCount = 0;
                foreach (KeyValuePair<string, SyntaxTree> pair in cache.Trees)
                {
                    SyntaxNode root = pair.Value.GetRoot();
                    foreach (SyntaxNode node in root.DescendantNodes())
                    {
                        ClassDeclarationSyntax? decl = node as ClassDeclarationSyntax;
                        if (decl != null)
                        {
                            classCount = classCount + 1;
                            int lineNumber = decl.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
                            sb.Append(decl.Identifier.Text + "（" + RelativeToProject(cache, pair.Key) + " L" + lineNumber + "）");
                            string summary = FirstSummary(decl);
                            if (summary.Length > 0)
                            {
                                sb.Append(" // " + summary);
                            }
                            sb.Append(Environment.NewLine);
                        }
                    }
                }
                if (classCount == 0)
                {
                    sb.Append("项目 " + cache.AssemblyName + " 无类声明");
                }
                else
                {
                    sb.Insert(0, "项目 " + cache.AssemblyName + " " + classCount + " 类" + Environment.NewLine);
                }
            }
            else
            {
                List<ClassPart> parts = FindClassParts(cache, className);
                if (parts.Count == 0)
                {
                    result = "ERR|CLASS_NOT_FOUND|类不存在: " + className;
                    return false;
                }
                for (int i = 0; i < parts.Count; i = i + 1)
                {
                    ClassDeclarationSyntax foundNode = parts[i].Node;
                    if (i > 0)
                    {
                        sb.Append(Environment.NewLine);
                    }
                    string lineText = (foundNode.GetLocation().GetLineSpan().StartLinePosition.Line + 1).ToString();
                    sb.Append("类 " + className + "（" + RelativeToProject(cache, parts[i].Tree.FilePath) + " L" + lineText + "）");
                    if (parts.Count > 1)
                    {
                        sb.Append("——partial 合并 " + parts.Count + " 处声明·第 " + (i + 1) + " 分部");
                    }
                    sb.Append(Environment.NewLine);
                    string summary = FirstSummary(foundNode);
                    if (summary.Length > 0)
                    {
                        sb.Append("/// " + summary + Environment.NewLine);
                    }
                    AppendMemberLines(foundNode, sb);
                }
            }
            result = TrimResult(sb.ToString(), MaxResultChars);
            return true;
        }

        /// <summary>
        /// cs.read——成员源码 + 文件行号标注（统一文件坐标系；补丁锚点依据）
        /// </summary>
        /// <param name="args">参数</param>
        /// <param name="result">结果</param>
        /// <returns>调用完成</returns>
        private bool ToolRead(JsonElement args, out string result)
        {
            string path = Arg(args, "path");
            string className = Arg(args, "class");
            string member = Arg(args, "member");
            string csproj = ResolveProject(path);
            if (csproj.Length == 0)
            {
                result = "ERR|BAD_PATH|项目路径无效或越界: " + path;
                return false;
            }
            ProjectCache cache = EnsureProject(csproj);
            FullScan(cache);
            List<ClassPart> parts = FindClassParts(cache, className);
            if (parts.Count == 0)
            {
                result = "ERR|CLASS_NOT_FOUND|类不存在: " + className;
                return false;
            }
            StringBuilder sb = new StringBuilder();
            if (member.Length == 0)
            {
                // 类概览——逐分部输出（单分部时输出与既往一致）
                for (int i = 0; i < parts.Count; i = i + 1)
                {
                    if (i > 0)
                    {
                        sb.Append(Environment.NewLine);
                    }
                    ClassDeclarationSyntax partNode = parts[i].Node;
                    FileLinePositionSpan span = partNode.GetLocation().GetLineSpan();
                    sb.Append("类 " + className + "（" + parts[i].Tree.FilePath + " L" + (span.StartLinePosition.Line + 1) + "-" + (span.EndLinePosition.Line + 1) + "）");
                    if (parts.Count > 1)
                    {
                        sb.Append("——partial 合并 " + parts.Count + " 处·第 " + (i + 1) + " 分部");
                    }
                    sb.Append(Environment.NewLine);
                    string summary = FirstSummary(partNode);
                    if (summary.Length > 0)
                    {
                        sb.Append("/// " + summary + Environment.NewLine);
                    }
                    AppendMemberLines(partNode, sb);
                }
            }
            else
            {
                ClassPart memberPart;
                SyntaxNode memberNode;
                string memberKind;
                int memberCount;
                List<string> memberCandidates;
                if (!FindMemberInParts(parts, member, out memberPart, out memberNode, out memberKind, out memberCount, out memberCandidates))
                {
                    if (memberCount > 1)
                    {
                        result = "ERR|AMBIGUOUS|成员歧义——同名 " + memberCount + " 处，候选签名: " + string.Join(" / ", memberCandidates) + "——member 传签名后缀区分（如 " + member + "(int)）";
                        return false;
                    }
                    result = "ERR|MEMBER_NOT_FOUND|成员不存在: " + className + "." + member + PartialHint(parts.Count);
                    return false;
                }
                FileLinePositionSpan memberSpan = memberNode.GetLocation().GetLineSpan();
                sb.Append("[文件: " + memberNode.SyntaxTree.FilePath + " L" + (memberSpan.StartLinePosition.Line + 1) + "-" + (memberSpan.EndLinePosition.Line + 1) + "]");
                sb.Append(Environment.NewLine);
                string memberSource = memberNode.ToFullString();
                int memberStartLine = memberNode.SyntaxTree.GetText().Lines.GetLineFromPosition(memberNode.FullSpan.Start).LineNumber + 1;
                sb.Append(NumberedSource(memberSource, memberStartLine));
            }
            result = TrimResult(sb.ToString(), MaxResultChars);
            return true;
        }

        /// <summary>
        /// 诊断格式化——rel:line:col: id: 消息
        /// </summary>
        /// <param name="cache">缓存</param>
        /// <param name="diagnostic">诊断</param>
        /// <returns>文本行</returns>
        private static string FormatDiagnostic(ProjectCache cache, Diagnostic diagnostic)
        {
            FileLinePositionSpan span = diagnostic.Location.GetLineSpan();
            string rel = span.Path ?? "";
            if (rel.Length > 0 && cache != null)
            {
                rel = RelativeToProject(cache, rel);
            }
            StringBuilder sb = new StringBuilder();
            sb.Append(rel + ":" + (span.StartLinePosition.Line + 1) + ":" + (span.StartLinePosition.Character + 1) + ": " + diagnostic.Id + ": " + diagnostic.GetMessage());
            return sb.ToString();
        }

        /// <summary>
        /// 诊断比较器——按 路径 → 行 → 列 排序
        /// </summary>
        private sealed class DiagnosticComparer : IComparer<Diagnostic>
        {
            /// <summary>
            /// 单例
            /// </summary>
            public static readonly DiagnosticComparer Instance = new DiagnosticComparer();

            /// <summary>比较</summary>
            public int Compare(Diagnostic? x, Diagnostic? y)
            {
                if (x == null)
                {
                    return -1;
                }
                if (y == null)
                {
                    return 1;
                }
                string xPath = x.Location.GetLineSpan().Path;
                string yPath = y.Location.GetLineSpan().Path;
                int byPath = string.CompareOrdinal(xPath, yPath);
                if (byPath != 0)
                {
                    return byPath;
                }
                int xLine = x.Location.GetLineSpan().StartLinePosition.Line;
                int yLine = y.Location.GetLineSpan().StartLinePosition.Line;
                if (xLine != yLine)
                {
                    return xLine.CompareTo(yLine);
                }
                int xCol = x.Location.GetLineSpan().StartLinePosition.Character;
                int yCol = y.Location.GetLineSpan().StartLinePosition.Character;
                return xCol.CompareTo(yCol);
            }
        }

        /// <summary>
        /// 类成员清单行——方法/字段/属性签名 + summary（纯语法）
        /// </summary>
        /// <param name="classNode">类声明</param>
        /// <param name="sb">输出</param>
        private static void AppendMemberLines(ClassDeclarationSyntax classNode, StringBuilder sb)
        {
            for (int i = 0; i < classNode.Members.Count; i = i + 1)
            {
                MemberDeclarationSyntax member = classNode.Members[i];
                string lineText = (member.GetLocation().GetLineSpan().StartLinePosition.Line + 1).ToString();
                string summary = FirstSummary(member);
                MethodDeclarationSyntax? method = member as MethodDeclarationSyntax;
                if (method != null)
                {
                    sb.Append("方法 '" + method.Identifier.Text + "'" + method.ParameterList.ToString());
                    if (method.TypeParameterList != null)
                    {
                        sb.Append(" 泛型" + method.TypeParameterList.ToString());
                    }
                    sb.Append(" // L" + lineText);
                    if (summary.Length > 0)
                    {
                        sb.Append(" /// " + summary);
                    }
                    sb.Append(Environment.NewLine);
                    continue;
                }
                PropertyDeclarationSyntax? property = member as PropertyDeclarationSyntax;
                if (property != null)
                {
                    sb.Append("属性 '" + property.Identifier.Text + "': " + property.Type.ToString() + " // L" + lineText);
                    if (summary.Length > 0)
                    {
                        sb.Append(" /// " + summary);
                    }
                    sb.Append(Environment.NewLine);
                    continue;
                }
                FieldDeclarationSyntax? field = member as FieldDeclarationSyntax;
                if (field != null)
                {
                    sb.Append("字段 '" + field.Declaration.ToString().Replace("\r\n", " ").Replace("\n", " ") + "' // L" + lineText);
                    if (summary.Length > 0)
                    {
                        sb.Append(" /// " + summary);
                    }
                    sb.Append(Environment.NewLine);
                    continue;
                }
                ConstructorDeclarationSyntax? ctor = member as ConstructorDeclarationSyntax;
                if (ctor != null)
                {
                    sb.Append("构造函数 '" + ctor.Identifier.Text + "'" + ctor.ParameterList.ToString() + " // L" + lineText);
                    if (summary.Length > 0)
                    {
                        sb.Append(" /// " + summary);
                    }
                    sb.Append(Environment.NewLine);
                    continue;
                }
                sb.Append("成员 '" + member.GetType().Name + "' // L" + lineText + Environment.NewLine);
            }
        }

        /// <summary>
        /// 提取成员首个 summary 注释（语法层——doc trivia 的 XML 片段）
        /// </summary>
        /// <param name="node">声明节点</param>
        /// <returns>summary 文本（无则空串）</returns>
        private static string FirstSummary(SyntaxNode node)
        {
            foreach (SyntaxTrivia trivia in node.GetLeadingTrivia())
            {
                if (trivia.IsKind(SyntaxKind.SingleLineDocumentationCommentTrivia) ||
                    trivia.IsKind(SyntaxKind.MultiLineDocumentationCommentTrivia))
                {
                    string content = trivia.ToString();
                    // XML 转义归一——&lt;summary&gt; 转义注释也能识别（先符号实体后 &amp;，防二次转义）
                    content = content.Replace("&lt;", "<").Replace("&gt;", ">").Replace("&quot;", "\"").Replace("&apos;", "'").Replace("&amp;", "&");
                    int index = content.IndexOf("<summary>", StringComparison.OrdinalIgnoreCase);
                    if (index < 0)
                    {
                        continue;
                    }
                    int end = content.IndexOf("</summary>", index + 9, StringComparison.OrdinalIgnoreCase);
                    if (end < 0)
                    {
                        continue;
                    }
                    string text = content.Substring(index + 9, end - index - 9);
                    text = text.Replace("\r\n", " ").Replace("\n", " ").Replace("\r", " ").Trim();
                    // 多行 doc 注释（/// <summary>↵/// 文本↵/// </summary>）提取后行内 /// 前缀剥离——防 "/// /// 文本" 双前缀污染
                    string[] summaryTokens = text.Split(' ');
                    StringBuilder cleanText = new StringBuilder();
                    for (int i = 0; i < summaryTokens.Length; i = i + 1)
                    {
                        string token = summaryTokens[i];
                        if (token.Length == 0 || token == "///")
                        {
                            continue;
                        }
                        cleanText.Append(token + " ");
                    }
                    text = cleanText.ToString().Trim();
                    if (text.Length > 60)
                    {
                        text = text.Substring(0, 60) + "…";
                    }
                    return text;
                }
            }
            return "";
        }

        /// <summary>
        /// 文件行号标注——每行源码尾部附加 // L{文件行号}（统一文件坐标系；起始行由调用方提供）
        /// </summary>
        /// <param name="source">成员 ToFullString</param>
        /// <param name="startLine">源码段起始文件行号（1-based）</param>
        /// <returns>标注文本</returns>
        private static string NumberedSource(string source, int startLine)
        {
            string[] lines = source.Replace("\r\n", "\n").Split('\n');
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < lines.Length; i = i + 1)
            {
                string line = lines[i];
                if (i == lines.Length - 1 && line.Length == 0)
                {
                    break;
                }
                sb.Append(line + " // L" + (startLine + i) + Environment.NewLine);
            }
            return sb.ToString();
        }

        /// <summary>
        /// 尾部行提取——build 输出摘要
        /// </summary>
        /// <param name="text">原文</param>
        /// <param name="count">行数</param>
        /// <returns>尾部行</returns>
        private static string TailLines(string text, int count)
        {
            if (string.IsNullOrEmpty(text))
            {
                return "(无输出)";
            }
            string[] lines = text.Replace("\r\n", "\n").Split('\n');
            StringBuilder sb = new StringBuilder();
            int start = lines.Length - count;
            if (start < 0)
            {
                start = 0;
            }
            for (int i = start; i < lines.Length; i = i + 1)
            {
                string line = lines[i].TrimEnd('\r');
                if (line.Length > 300)
                {
                    line = line.Substring(0, 300) + "…";
                }
                sb.Append(line + Environment.NewLine);
            }
            return sb.ToString().TrimEnd('\r', '\n');
        }
    }
}