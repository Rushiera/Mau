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
        /// <summary>cs.check——语法层诊断（写完代码后的第一轮全量语法验证：逐树 SyntaxTree.GetDiagnostics，不触引用集 / 语义模型）；path 支持 csproj / .sln / 目录（聚合分组输出）；full=含语法警告；程序集引用与编译裁决以 cs-build 为唯一权威</summary>
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
                result = "ERR|BAD_PATH|" + resolveError;
                return false;
            }
            if (projects.Count == 1)
            {
                return CheckSingle(projects[0], full, out result);
            }
            // 结构化返回（2026-09-18）：首行 JSON 元数据头 + 正文按项目分节（节头 `── 相对路径 ──`）
            StringBuilder sb = new StringBuilder();
            List<string> sections = new List<string>();
            int failed = 0;
            for (int i = 0; i < projects.Count; i = i + 1)
            {
                string one;
                CheckSingle(projects[i], full, out one);
                if (one.StartsWith("{\"ok\":false", StringComparison.Ordinal))
                {
                    failed = failed + 1;
                }
                sections.Add("── " + RelativeToRoots(projects[i]) + " ──" + Environment.NewLine + one);
            }
            Dictionary<string, object> aggMeta = new Dictionary<string, object>();
            aggMeta["projects"] = projects.Count;
            aggMeta["failed"] = failed;
            sb.Append(MetaHead("cs-check", failed == 0, aggMeta));
            for (int i = 0; i < sections.Count; i = i + 1)
            {
                sb.Append(Environment.NewLine);
                sb.Append(sections[i]);
            }
            result = TrimResult(sb.ToString(), MaxResultChars);
            return true;
        }
        /// <summary>单项目 check——语法层诊断（逐树 SyntaxTree.GetDiagnostics；不构造引用集与语义模型，结论只反映编译单元文本自身）；full=含语法警告</summary>
        /// <param name="csproj">csproj 绝对路径</param>
        /// <param name="full">是否输出全部语法警告</param>
        /// <param name="result">结果文本</param>
        /// <returns>调用完成</returns>
        private bool CheckSingle(string csproj, bool full, out string result)
        {
            ProjectCache cache = EnsureProject(csproj);
            FullScan(cache);
            // 语法层诊断——逐树 SyntaxTree.GetDiagnostics（编译单元文本自身，不触引用集 / 语义模型）
            List<Diagnostic> errors = new List<Diagnostic>();
            List<Diagnostic> warnings = new List<Diagnostic>();
            int fileCount = 0;
            foreach (SyntaxTree tree in cache.Trees.Values)
            {
                fileCount = fileCount + 1;
                foreach (Diagnostic diagnostic in tree.GetDiagnostics())
                {
                    if (diagnostic.Severity == DiagnosticSeverity.Error)
                    {
                        errors.Add(diagnostic);
                    }
                    else if (diagnostic.Severity == DiagnosticSeverity.Warning)
                    {
                        warnings.Add(diagnostic);
                    }
                }
            }
            errors.Sort(DiagnosticComparer.Instance);
            warnings.Sort(DiagnosticComparer.Instance);
            // 空 catch 检测（约定检查——默认总开；块内无语句即报，注释不构成运行观测面）
            List<string> emptyCatches = CollectEmptyCatches(cache);
            // 结构化返回（2026-09-18）：首行 JSON 元数据头 + 正文定界诊断行（rel:line:col: id: 消息）
            Dictionary<string, object> meta = new Dictionary<string, object>();
            meta["project"] = cache.AssemblyName;
            meta["files"] = fileCount;
            meta["errors"] = errors.Count;
            meta["warnings"] = warnings.Count + emptyCatches.Count;
            meta["emptyCatch"] = emptyCatches.Count;
            StringBuilder sb = new StringBuilder();
            sb.Append(MetaHead("cs-check", errors.Count == 0, meta));
            for (int i = 0; i < errors.Count; i = i + 1)
            {
                sb.Append(Environment.NewLine);
                sb.Append(FormatDiagnostic(cache, errors[i]));
            }
            if (emptyCatches.Count > 0)
            {
                sb.Append(Environment.NewLine);
                sb.Append(CheckEmptyCatchSeparator);
                for (int i = 0; i < emptyCatches.Count; i = i + 1)
                {
                    sb.Append(Environment.NewLine);
                    sb.Append(emptyCatches[i]);
                }
            }
            if (full && warnings.Count > 0)
            {
                sb.Append(Environment.NewLine);
                sb.Append(CheckWarnSeparator);
                for (int i = 0; i < warnings.Count; i = i + 1)
                {
                    sb.Append(Environment.NewLine);
                    sb.Append(FormatDiagnostic(cache, warnings[i]));
                }
            }
            result = TrimResult(sb.ToString(), MaxResultChars);
            return true;
        }

        /// <summary>
        /// 空 catch 收集——块内无语句的 catch 子句（约定检查：禁止空块吞异常；注释不构成运行观测面，仅注释同样报）。
        /// 文件遍历按树路径序（文件内文档序）——输出天然按 路径 → 行 有序。
        /// </summary>
        /// <param name="cache">项目缓存</param>
        /// <returns>诊断行列表（rel:line:col: CS_EMPTY_CATCH: 消息）</returns>
        private static List<string> CollectEmptyCatches(ProjectCache cache)
        {
            List<string> lines = new List<string>();
            List<string> keys = new List<string>(cache.Trees.Keys);
            keys.Sort(StringComparer.OrdinalIgnoreCase);
            for (int k = 0; k < keys.Count; k = k + 1)
            {
                SyntaxTree tree = cache.Trees[keys[k]];
                foreach (SyntaxNode node in tree.GetRoot().DescendantNodes())
                {
                    CatchClauseSyntax? clause = node as CatchClauseSyntax;
                    if (clause == null)
                    {
                        continue;
                    }
                    if (clause.Block == null || clause.Block.Statements.Count > 0)
                    {
                        continue;
                    }
                    FileLinePositionSpan span = tree.GetLineSpan(clause.GetLocation().SourceSpan);
                    lines.Add(RelativeToProject(cache, tree.FilePath) + ":" + (span.StartLinePosition.Line + 1) + ":" + (span.StartLinePosition.Character + 1) + ": CS_EMPTY_CATCH: 空 catch 块——吞异常（补具名告警或日志语句；注释不构成运行观测面）");
                }
            }
            return lines;
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
                result = "ERR|BAD_PATH|" + resolveError;
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
            // 结构化返回（2026-09-18）：首行 JSON 元数据头 + 正文按项目分节
            StringBuilder sb = new StringBuilder();
            List<string> sections = new List<string>();
            int failed = 0;
            for (int i = 0; i < projects.Count; i = i + 1)
            {
                string one;
                BuildOne(projects[i], out one);
                if (one.StartsWith("{\"ok\":false", StringComparison.Ordinal))
                {
                    failed = failed + 1;
                }
                sections.Add("── " + RelativeToRoots(projects[i]) + " ──" + Environment.NewLine + one);
            }
            Dictionary<string, object> aggMeta = new Dictionary<string, object>();
            aggMeta["projects"] = projects.Count;
            aggMeta["failed"] = failed;
            sb.Append(MetaHead("cs-build", failed == 0, aggMeta));
            for (int i = 0; i < sections.Count; i = i + 1)
            {
                sb.Append(Environment.NewLine);
                sb.Append(sections[i]);
            }
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
            Stopwatch watch = Stopwatch.StartNew();
            ProcessRunResult run = ProcessRunner.RunAndCapture("dotnet", "build \"" + target + "\" --nologo", cache.ProjectDir, 120000);
            watch.Stop();
            long elapsedMs = watch.ElapsedMilliseconds;
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
            // 结构化返回（2026-09-18）：首行 JSON 元数据头 + 正文 MSBuild 输出摘要（尾部 20 行）
            string tail = TailLines(run.Stdout + run.Stderr, 20);
            string buildText = run.Stdout + run.Stderr;
            Dictionary<string, object> meta = new Dictionary<string, object>();
            meta["project"] = cache.AssemblyName;
            meta["exit"] = run.ExitCode;
            meta["errors"] = CountBuildMark(buildText, "个错误", "Error(s)");
            meta["warnings"] = CountBuildMark(buildText, "个警告", "Warning(s)");
            meta["ms"] = elapsedMs;
            if (run.ExitCode == 0)
            {
                cache.ReferencesDirty = true;
            }
            result = TrimResult(MetaHead("cs-build", run.ExitCode == 0, meta) + Environment.NewLine + tail, MaxResultChars);
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
                result = "ERR|BAD_PATH|" + resolveError;
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
                // 结构化返回（2026-09-18）：首行 JSON 元数据头 + 正文清单行（`类名（文件 Lnn） // summary`）
                Dictionary<string, object> listMeta = new Dictionary<string, object>();
                listMeta["project"] = cache.AssemblyName;
                listMeta["classes"] = classCount;
                sb.Insert(0, MetaHead("cs-list", true, listMeta) + Environment.NewLine);
                if (classCount == 0)
                {
                    sb.Append("（无类声明）");
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
                result = "ERR|BAD_PATH|项目路径无效或越界: " + path + ProjectPathDiagnostic(path);
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
                // 结构化返回（2026-09-18）：首行 JSON 元数据头（类 / 分部数）+ 正文逐分部（节头 `类 X（文件 Lnn-nn）`）
                Dictionary<string, object> classMeta = new Dictionary<string, object>();
                classMeta["class"] = className;
                classMeta["parts"] = parts.Count;
                sb.Append(MetaHead("cs-read", true, classMeta));
                for (int i = 0; i < parts.Count; i = i + 1)
                {
                    sb.Append(Environment.NewLine);
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
                // 结构化返回（2026-09-18）：首行 JSON 元数据头（文件 / 类 / 成员 / 行区间）+ 正文源码
                // 正文保留行尾 `// L{行号}` 标注（LLM 定位用）；前端行号列按 start + 行序计算，不解析行尾
                FileLinePositionSpan memberSpan = memberNode.GetLocation().GetLineSpan();
                string memberSource = memberNode.ToFullString();
                int memberStartLine = memberNode.SyntaxTree.GetText().Lines.GetLineFromPosition(memberNode.FullSpan.Start).LineNumber + 1;
                Dictionary<string, object> readMeta = new Dictionary<string, object>();
                readMeta["file"] = memberNode.SyntaxTree.FilePath;
                readMeta["class"] = className;
                readMeta["member"] = member;
                readMeta["start"] = memberStartLine;
                readMeta["end"] = memberSpan.EndLinePosition.Line + 1;
                sb.Append(MetaHead("cs-read", true, readMeta));
                sb.Append(Environment.NewLine);
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
        /// 结构化返回头——首行 JSON 元数据（ok / tool + 调用方字段；键序稳定 = 调用顺序）。
        /// 约定（2026-09-18）：工具返回体 = 首行 JSON 元数据头 + 正文定界行（正文不塞进 JSON——避免转义膨胀撞截断面）。
        /// </summary>
        /// <param name="tool">工具名（cs-check / cs-build / cs-read…）</param>
        /// <param name="ok">成败（正文是否含错误态）</param>
        /// <param name="fields">附加字段（按插入序输出）</param>
        /// <returns>单行 JSON</returns>
        private static string MetaHead(string tool, bool ok, Dictionary<string, object> fields)
        {
            Dictionary<string, object> head = new Dictionary<string, object>();
            head["ok"] = ok;
            head["tool"] = tool;
            foreach (KeyValuePair<string, object> kv in fields)
            {
                head[kv.Key] = kv.Value;
            }
            return JsonSerializer.Serialize(head);
        }

        /// <summary>check 警告段分隔行——正文里 errors 行与 warnings 行的定界标记</summary>
        private const string CheckWarnSeparator = "-- 警告 --";

        /// <summary>check 空 catch 段分隔行——约定检查（CS_EMPTY_CATCH）清单的定界标记</summary>
        private const string CheckEmptyCatchSeparator = "-- 空 catch --";

        /// <summary>MSBuild 摘要计数提取——中文「N 个错误」/ 英文「N Error(s)」两形态（提不到返回 0）</summary>
        /// <param name="text">构建输出全文</param>
        /// <param name="cnMark">中文标记（如「个错误」）</param>
        /// <param name="enMark">英文标记（如「Error(s)」）</param>
        /// <returns>计数（缺失 0）</returns>
        private static int CountBuildMark(string text, string cnMark, string enMark)
        {
            int v = ExtractCountBefore(text, cnMark);
            if (v >= 0)
            {
                return v;
            }
            v = ExtractCountBefore(text, enMark);
            return (v >= 0) ? v : 0;
        }

        /// <summary>提取标记前的数字——同一标记取最后一次出现（MSBuild 摘要行在输出尾部）</summary>
        /// <param name="text">全文</param>
        /// <param name="mark">标记文本</param>
        /// <returns>数字（提不到 -1）</returns>
        private static int ExtractCountBefore(string text, string mark)
        {
            int idx = text.LastIndexOf(mark, StringComparison.Ordinal);
            if (idx < 0)
            {
                return -1;
            }
            int end = idx;
            while (end > 0 && text[end - 1] == ' ')
            {
                end = end - 1;
            }
            int start = end;
            while (start > 0 && text[start - 1] >= '0' && text[start - 1] <= '9')
            {
                start = start - 1;
            }
            if (start == end)
            {
                return -1;
            }
            int v;
            if (int.TryParse(text.Substring(start, end - start), out v))
            {
                return v;
            }
            return -1;
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