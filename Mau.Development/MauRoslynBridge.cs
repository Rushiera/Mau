#nullable disable warnings // Roslyn API 返回值可空假阳性——GetSyntaxTreeAsync 等方法签名声明可空但项目验证后永不返回 null
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using Microsoft.CodeAnalysis.MSBuild;
using Microsoft.CodeAnalysis.Rename;
using Microsoft.CodeAnalysis.FindSymbols;
using Mau.Runtime;

namespace Mau.Development
{
    /// <summary>
    /// Roslyn 工具桥——ICSharpBridge 实现（多树隔离 + 编译与提交分离）。
    /// 移植自 CH2 CH_Tool_Roslyn（80KB 完整引擎）——每 csproj 一棵工作区树，互不干扰。
    /// 设计：MauRuntime 只暴露 ICSharpBridge 纯接口（string 基元），本类持有 Roslyn 全量。
    /// </summary>
    public sealed class MauRoslynBridge : ICSharpBridge
    {
        // ═══════════════════════════════════════════
        // 树隔离——csproj 路径 → 工作区状态
        // ═══════════════════════════════════════════

        /// <summary>
        /// 工作区状态——每 csproj 一棵树
        /// </summary>
        private sealed class WorkspaceState
        {
            /// <summary>
            /// MSBuild 工作区
            /// </summary>
            internal MSBuildWorkspace Workspace = null!;

            /// <summary>
            /// 当前 Solution 快照
            /// </summary>
            internal Solution Solution = null!;

            /// <summary>
            /// 当前 Project
            /// </summary>
            internal Project Project = null!;

            /// <summary>
            /// 类名 → Document 索引
            /// </summary>
            internal Dictionary<string, Document> ClassIndex = null!;

            /// <summary>
            /// 最近编译错误数
            /// </summary>
            internal int LastErrorCount;

            /// <summary>
            /// 最近编译警告数
            /// </summary>
            internal int LastWarningCount;

            /// <summary>
            /// csproj 路径
            /// </summary>
            internal string CsprojPath = "";
        }

        /// <summary>
        /// 树表锁——多树并发隔离
        /// </summary>
        private readonly object _gate = new object();

        /// <summary>
        /// 树表——csproj 绝对路径 → 工作区状态
        /// </summary>
        private readonly Dictionary<string, WorkspaceState> _trees = new Dictionary<string, WorkspaceState>(StringComparer.Ordinal);

        /// <summary>
        /// 当前活动树 key（最近 Init 的 csproj）
        /// </summary>
        private string _activeKey = "";

        /// <summary>
        /// 日志输出——可选注入
        /// </summary>
        public Action<string> Log = null!;

        /// <summary>
        /// 内部日志
        /// </summary>
        /// <param name="msg">消息</param>
        private void RLog(string msg)
        {
            if (Log != null)
            {
                Log(msg);
            }
        }

        /// <summary>
        /// 取当前活动树——未绑定返回 null
        /// </summary>
        /// <returns>状态或 null</returns>
        private WorkspaceState GetActive()
        {
            lock (_gate)
            {
                if (_activeKey.Length == 0)
                {
                    return null;
                }
                WorkspaceState state;
                if (_trees.TryGetValue(_activeKey, out state))
                {
                    return state;
                }
                return null;
            }
        }

        /// <summary>
        /// 未绑定错误 JSON
        /// </summary>
        /// <returns>错误文本</returns>
        private static string NotInit()
        {
            return "{\"ok\":false, \"error\":\"NOT_INIT\", \"detail\":\"C# 工具组未绑定项目\", \"tip\":\"请先调用 csharpcode_init 绑定 .csproj\"}";
        }

        /// <summary>
        /// 绑定 csproj 项目——加载所有 .cs 文件并建立语法树索引。成功返回项目名+文件数+初始编译诊断。
        /// </summary>
        /// <param name="csprojPath">csproj 路径</param>
        /// <returns>JSON</returns>
        public string Init(string csprojPath)
        {
            if (string.IsNullOrEmpty(csprojPath))
            {
                return "{\"ok\":false, \"error\":\"INVALID_PATH\", \"detail\":\"csproj 路径为空\"}";
            }
            string absPath = Path.GetFullPath(csprojPath);
            if (!File.Exists(absPath))
            {
                return "{\"ok\":false, \"error\":\"FILE_NOT_FOUND\", \"detail\":\"csproj 不存在: " + absPath + "\"}";
            }
            if (!absPath.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
            {
                return "{\"ok\":false, \"error\":\"NOT_CSPROJ\", \"detail\":\"不是 .csproj 文件: " + absPath + "\"}";
            }

            WorkspaceState state;
            lock (_gate)
            {
                // 已存在则复用（树隔离——不重复加载）
                if (_trees.TryGetValue(absPath, out state))
                {
                    _activeKey = absPath;
                    return "{\"ok\":true, \"reused\":true, \"project\":\"" + state.Project.AssemblyName
                        + "\", \"documents\":" + state.ClassIndex.Count + "}";
                }
                state = new WorkspaceState();
                state.CsprojPath = absPath;
                _trees[absPath] = state;
                _activeKey = absPath;
            }

            try
            {
                RLog("[RoslynBridge] 创建 MSBuildWorkspace: " + absPath);
                MSBuildWorkspace workspace = MSBuildWorkspace.Create();
                workspace.WorkspaceFailed += delegate (object? sender, WorkspaceDiagnosticEventArgs e)
                {
                    if (e.Diagnostic.Kind == WorkspaceDiagnosticKind.Failure)
                    {
                        RLog("[Workspace] " + e.Diagnostic.Message);
                    }
                };
                state.Workspace = workspace;
                Project project = workspace.OpenProjectAsync(absPath).GetAwaiter().GetResult();
                if (project == null)
                {
                    lock (_gate)
                    {
                        _trees.Remove(absPath);
                        if (_activeKey == absPath) { _activeKey = ""; }
                    }
                    return "{\"ok\":false, \"error\":\"PROJECT_LOAD_FAILED\", \"detail\":\"OpenProjectAsync 返回 null——csproj 可能损坏或缺少 SDK\"}";
                }
                state.Project = project;
                state.Solution = project.Solution;

                // 建立类名索引
                state.ClassIndex = new Dictionary<string, Document>();
                int docCount = 0;
                foreach (Document doc in project.Documents)
                {
                    if (doc.FilePath == null) { continue; }
                    if (!doc.FilePath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)) { continue; }
                    docCount = docCount + 1;
                    SyntaxTree tree = doc.GetSyntaxTreeAsync().GetAwaiter().GetResult();
                    if (tree == null) { continue; }
                    CompilationUnitSyntax root = tree.GetCompilationUnitRoot();
                    foreach (BaseTypeDeclarationSyntax type in root.DescendantNodes().OfType<BaseTypeDeclarationSyntax>())
                    {
                        state.ClassIndex[type.Identifier.Text] = doc;
                    }
                }

                // 初始编译
                Compilation compilation = project.GetCompilationAsync().GetAwaiter().GetResult();
                int errorCount = 0;
                int warningCount = 0;
                if (compilation != null)
                {
                    CountDiagnostics(state, compilation, out errorCount, out warningCount);
                }
                state.LastErrorCount = errorCount;
                state.LastWarningCount = warningCount;
                RLog("[RoslynBridge] 索引完成: " + state.ClassIndex.Count + " 类, " + docCount + " 文档, 错误 " + errorCount + " 警告 " + warningCount);
                return "{\"ok\":true, \"project\":\"" + project.AssemblyName
                    + "\", \"documents\":" + docCount
                    + ", \"classes\":" + state.ClassIndex.Count
                    + ", \"errors\":" + errorCount
                    + ", \"warnings\":" + warningCount + "}";
            }
            catch (Exception ex)
            {
                lock (_gate)
                {
                    _trees.Remove(absPath);
                    if (_activeKey == absPath) { _activeKey = ""; }
                }
                return "{\"ok\":false, \"error\":\"INIT_FAILED\", \"detail\":\"" + ex.Message + "\"}";
            }
        }

        /// <summary>
        /// PACK 单方法调度——method 白名单 + argsJson 展平参数（积木统一入口）
        /// </summary>
        /// <param name="method">操作名</param>
        /// <param name="argsJson">展平参数 JSON</param>
        /// <param name="result">结果文本</param>
        /// <returns>true=调用成功</returns>
        public bool Invoke(string method, string argsJson, out string result)
        {
            result = "";
            if (string.IsNullOrEmpty(method))
            {
                result = "{\"ok\":false, \"error\":\"PACK_NO_METHOD\", \"detail\":\"csharp.bridge 缺 method\"}";
                return false;
            }
            string m = method.Trim();
            try
            {
                if (m == "init") { result = Init(ReadArg(argsJson, "csproj")); return true; }
                if (m == "info") { result = GetInfo(); return true; }
                if (m == "list") { result = ListMembers(ReadArg(argsJson, "class")); return true; }
                if (m == "read") { result = ReadMember(ReadArg(argsJson, "class"), ReadArg(argsJson, "member")); return true; }
                if (m == "compile")
                {
                    bool full = false;
                    string rawFull = ReadArg(argsJson, "full");
                    if (rawFull.Length > 0 && bool.TryParse(rawFull, out full)) { }
                    result = GetDiagnostics(full);
                    return true;
                }
                if (m == "body_replace") { result = ReplaceMethodBody(ReadArg(argsJson, "class"), ReadArg(argsJson, "method"), ReadArg(argsJson, "body")); return true; }
                if (m == "line_patch") { result = LinePatch(ReadArg(argsJson, "class"), ReadArg(argsJson, "method"), ReadInt(argsJson, "startLine"), ReadInt(argsJson, "endLine"), ReadArg(argsJson, "newText")); return true; }
                if (m == "line_insert") { result = LineInsert(ReadArg(argsJson, "class"), ReadArg(argsJson, "method"), ReadInt(argsJson, "afterLine"), ReadArg(argsJson, "newText")); return true; }
                if (m == "member_insert") { result = InsertMember(ReadArg(argsJson, "class"), ReadArg(argsJson, "position"), ReadArg(argsJson, "anchor"), ReadArg(argsJson, "code")); return true; }
                if (m == "member_delete") { result = DeleteMember(ReadArg(argsJson, "class"), ReadArg(argsJson, "member")); return true; }
                if (m == "comment_set") { result = SetComment(ReadArg(argsJson, "class"), ReadArg(argsJson, "member"), ReadArg(argsJson, "type"), ReadArg(argsJson, "text"), ReadArg(argsJson, "param")); return true; }
                if (m == "comment_check") { result = CommentCheck(); return true; }
                if (m == "member_rename") { result = RenameMember(ReadArg(argsJson, "class"), ReadArg(argsJson, "oldName"), ReadArg(argsJson, "newName")); return true; }
                if (m == "dead") { result = DeadCode(); return true; }
                if (m == "find_ref") { result = FindReferences(ReadArg(argsJson, "class"), ReadArg(argsJson, "member")); return true; }
                result = "{\"ok\":false, \"error\":\"PACK_UNKNOWN_METHOD\", \"detail\":\"" + m + "\"}";
                return false;
            }
            catch (Exception ex)
            {
                result = "{\"ok\":false, \"error\":\"PACK_INVOKE_FAILED\", \"detail\":\"" + ex.Message + "\"}";
                return false;
            }
        }

        /// <summary>
        /// 读取 argsJson 字符串参数
        /// </summary>
        /// <param name="argsJson">参数 JSON</param>
        /// <param name="key">参数名</param>
        /// <returns>值或空串</returns>
        private static string ReadArg(string argsJson, string key)
        {
            if (string.IsNullOrEmpty(argsJson))
            {
                return "";
            }
            try
            {
                using System.Text.Json.JsonDocument doc = System.Text.Json.JsonDocument.Parse(argsJson);
                System.Text.Json.JsonElement value;
                if (doc.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object
                    && doc.RootElement.TryGetProperty(key, out value))
                {
                    if (value.ValueKind == System.Text.Json.JsonValueKind.String)
                    {
                        return value.GetString() ?? "";
                    }
                    return value.GetRawText();
                }
            }
            catch
            {
                // 解析失败返回空串
            }
            return "";
        }

        /// <summary>
        /// 读取 argsJson int 参数
        /// </summary>
        /// <param name="argsJson">参数 JSON</param>
        /// <param name="key">参数名</param>
        /// <returns>int 值（解析失败 0）</returns>
        private static int ReadInt(string argsJson, string key)
        {
            string raw = ReadArg(argsJson, key);
            int value;
            if (int.TryParse(raw, out value))
            {
                return value;
            }
            return 0;
        }

        /// <summary>
        /// 查询绑定状态
        /// </summary>
        /// <returns>JSON</returns>
        public string GetInfo()
        {
            WorkspaceState state = GetActive();
            if (state == null)
            {
                return NotInit();
            }
            return "{\"ok\":true, \"project\":\"" + state.Project.AssemblyName
                + "\", \"csproj\":\"" + state.CsprojPath
                + "\", \"documents\":" + state.Project.Documents.Count()
                + ", \"classes\":" + state.ClassIndex.Count + "}";
        }

        /// <summary>
        /// 列出类成员
        /// </summary>
        /// <param name="className">类名（空=全项目）</param>
        /// <returns>清单</returns>
        public string ListMembers(string className)
        {
            WorkspaceState state = GetActive();
            if (state == null) { return NotInit(); }
            if (string.IsNullOrEmpty(className))
            {
                System.Text.StringBuilder sb = new System.Text.StringBuilder();
                List<string> names = new List<string>(state.ClassIndex.Keys);
                names.Sort(StringComparer.Ordinal);
                foreach (string name in names)
                {
                    sb.Append("'");
                    sb.Append(name);
                    sb.Append("'\n");
                }
                sb.Append("---\n共 ");
                sb.Append(names.Count);
                sb.Append(" 类。用 class 参数指定类名查看成员详情。");
                return sb.ToString();
            }
            Document doc;
            if (!state.ClassIndex.TryGetValue(className, out doc))
            {
                return "{\"ok\":false, \"error\":\"CLASS_NOT_FOUND\", \"detail\":\"未找到类: " + className + "\", \"tip\":\"类名拼写错误？用 csharpcode_list 查看全项目类名\"}";
            }
            try
            {
                SyntaxTree tree = doc.GetSyntaxTreeAsync().GetAwaiter().GetResult();
                CompilationUnitSyntax root = tree.GetCompilationUnitRoot();
                TypeDeclarationSyntax typeDecl = root.DescendantNodes().OfType<TypeDeclarationSyntax>()
                    .FirstOrDefault(c => c.Identifier.Text == className);
                if (typeDecl == null)
                {
                    return "{\"ok\":false, \"error\":\"TYPE_NOT_FOUND\", \"detail\":\"类型 " + className + " 在语法树中未找到\"}";
                }
                System.Text.StringBuilder sb = new System.Text.StringBuilder();
                sb.Append(className);
                string classComment = GetSummaryText(typeDecl);
                if (classComment.Length > 0)
                {
                    sb.Append(" // ");
                    sb.Append(classComment);
                }
                sb.Append("\n");
                sb.Append(typeDecl.Members.Count);
                sb.Append(" 成员\n");
                foreach (MemberDeclarationSyntax member in typeDecl.Members)
                {
                    sb.Append("  ");
                    sb.Append(GetMemberKind(member));
                    sb.Append(" '");
                    sb.Append(GetMemberName(member));
                    sb.Append("'");
                    if (member is MethodDeclarationSyntax m)
                    {
                        sb.Append("(");
                        sb.Append(m.ParameterList.Parameters.ToString());
                        sb.Append(")");
                    }
                    string comment = GetSummaryText(member);
                    if (comment.Length > 0)
                    {
                        sb.Append(" // ");
                        sb.Append(comment);
                    }
                    sb.Append("\n");
                }
                sb.Append("---\n");
                sb.Append(typeDecl.Members.Count);
                sb.Append(" 成员");
                return sb.ToString();
            }
            catch (Exception ex)
            {
                return "{\"ok\":false, \"error\":\"LIST_FAILED\", \"detail\":\"" + ex.Message + "\"}";
            }
        }

        /// <summary>
        /// 读取成员源码
        /// </summary>
        /// <param name="className">类名</param>
        /// <param name="memberName">成员名（空=类概览）</param>
        /// <returns>源码</returns>
        public string ReadMember(string className, string memberName)
        {
            WorkspaceState state = GetActive();
            if (state == null) { return NotInit(); }
            Document doc;
            if (!state.ClassIndex.TryGetValue(className, out doc))
            {
                return "{\"ok\":false, \"error\":\"CLASS_NOT_FOUND\", \"detail\":\"未找到类: " + className + "\", \"tip\":\"类名拼写错误？用 csharpcode_list 查看全项目类名\"}";
            }
            try
            {
                SyntaxTree tree = doc.GetSyntaxTreeAsync().GetAwaiter().GetResult();
                CompilationUnitSyntax root = tree.GetCompilationUnitRoot();
                TypeDeclarationSyntax typeDecl = root.DescendantNodes().OfType<TypeDeclarationSyntax>()
                    .FirstOrDefault(c => c.Identifier.Text == className);
                if (typeDecl == null)
                {
                    return "{\"ok\":false, \"error\":\"CLASS_NOT_FOUND\", \"detail\":\"未找到类: " + className + "\", \"tip\":\"类名拼写错误？用 csharpcode_list 查看全项目类名\"}";
                }
                if (string.IsNullOrEmpty(memberName))
                {
                    return BuildClassOverview(doc, typeDecl);
                }
                MemberDeclarationSyntax member = FindMember(typeDecl, memberName);
                if (member == null)
                {
                    return "{\"ok\":false, \"error\":\"MEMBER_NOT_FOUND\", \"detail\":\"未找到成员: " + className + "." + memberName + "\", \"tip\":\"成员名拼写错误？用 csharpcode_list " + className + " 查看该类成员\"}";
                }
                return BuildMemberReadout(doc, tree, member);
            }
            catch (Exception ex)
            {
                return "{\"ok\":false, \"error\":\"READ_FAILED\", \"detail\":\"" + ex.Message + "\"}";
            }
        }

        /// <summary>
        /// 编译项目
        /// </summary>
        /// <param name="full">true=完整诊断</param>
        /// <returns>JSON</returns>
        public string GetDiagnostics(bool full)
        {
            WorkspaceState state = GetActive();
            if (state == null) { return NotInit(); }
            try
            {
                Compilation compilation = state.Project.GetCompilationAsync().GetAwaiter().GetResult();
                if (compilation == null)
                {
                    return "{\"ok\":false, \"error\":\"COMPILE_FAILED\"}";
                }
                int errCount = 0;
                int warnCount = 0;
                List<string> diags = new List<string>();
                foreach (Diagnostic diag in compilation.GetDiagnostics())
                {
                    if (diag.Severity == DiagnosticSeverity.Error)
                    {
                        errCount = errCount + 1;
                        if (full) { diags.Add(FormatDiagnostic(state, diag)); }
                    }
                    else if (diag.Severity == DiagnosticSeverity.Warning)
                    {
                        warnCount = warnCount + 1;
                        if (full) { diags.Add(FormatDiagnostic(state, diag)); }
                    }
                }
                using MemoryStream emitStream = new MemoryStream();
                Microsoft.CodeAnalysis.Emit.EmitResult emitResult = compilation.Emit(emitStream);
                if (!emitResult.Success)
                {
                    foreach (Diagnostic diag in emitResult.Diagnostics)
                    {
                        if (diag.Severity == DiagnosticSeverity.Error)
                        {
                            errCount = errCount + 1;
                            if (full) { diags.Add(FormatDiagnostic(state, diag)); }
                        }
                        else if (diag.Severity == DiagnosticSeverity.Warning)
                        {
                            warnCount = warnCount + 1;
                            if (full) { diags.Add(FormatDiagnostic(state, diag)); }
                        }
                    }
                }
                state.LastErrorCount = errCount;
                state.LastWarningCount = warnCount;
                if (!full)
                {
                    return "{\"ok\":true, \"errors\":" + errCount + ", \"warnings\":" + warnCount + "}";
                }
                System.Text.StringBuilder sb = new System.Text.StringBuilder();
                sb.Append("{\"ok\":true, \"errors\":");
                sb.Append(errCount);
                sb.Append(", \"warnings\":");
                sb.Append(warnCount);
                sb.Append(", \"diagnostics\":[");
                for (int i = 0; i < diags.Count; i = i + 1)
                {
                    if (i > 0) { sb.Append(","); }
                    sb.Append("\n  \"");
                    sb.Append(diags[i].Replace("\\", "\\\\").Replace("\"", "\\\""));
                    sb.Append("\"");
                }
                sb.Append("\n]}");
                return sb.ToString();
            }
            catch (Exception ex)
            {
                return "{\"ok\":false, \"error\":\"COMPILE_FAILED\", \"detail\":\"" + ex.Message + "\"}";
            }
        }

        /// <summary>
        /// 替换整个方法体
        /// </summary>
        /// <param name="className">类名</param>
        /// <param name="methodName">方法名</param>
        /// <param name="newBody">新方法体</param>
        /// <returns>JSON</returns>
        public string ReplaceMethodBody(string className, string methodName, string newBody)
        {
            WorkspaceState state = GetActive();
            if (state == null) { return NotInit(); }
            MethodLoc loc = LocateMethod(state, className, methodName);
            if (loc.Error != null) { return loc.Error; }
            try
            {
                string trimmedBody = newBody.Trim();
                if (!trimmedBody.StartsWith("{"))
                {
                    trimmedBody = "{" + newBody + "}";
                }
                BlockSyntax newBlock = SyntaxFactory.ParseStatement(trimmedBody) as BlockSyntax;
                if (newBlock == null)
                {
                    return "{\"ok\":false, \"error\":\"PARSE_ERROR\", \"detail\":\"新方法体解析失败\"}";
                }
                BaseMethodDeclarationSyntax newMethod = loc.Method.WithBody(newBlock);
                return ApplyAndCompile(state, loc.Doc, loc.Method, newMethod, className, className + "." + methodName);
            }
            catch (Exception ex)
            {
                return "{\"ok\":false, \"error\":\"REPLACE_FAILED\", \"detail\":\"" + ex.Message + "\"}";
            }
        }

        /// <summary>
        /// 替换方法内指定行范围
        /// </summary>
        /// <param name="className">类名</param>
        /// <param name="methodName">方法名</param>
        /// <param name="startLine">起始行（方法内）</param>
        /// <param name="endLine">结束行（方法内）</param>
        /// <param name="newText">替换文本</param>
        /// <returns>JSON</returns>
        public string LinePatch(string className, string methodName, int startLine, int endLine, string newText)
        {
            WorkspaceState state = GetActive();
            if (state == null) { return NotInit(); }
            MethodLoc loc = LocateMethod(state, className, methodName);
            if (loc.Error != null) { return loc.Error; }
            try
            {
                BlockSyntax body = loc.Method.Body;
                if (body == null)
                {
                    return "{\"ok\":false, \"error\":\"NO_BODY\", \"detail\":\"该方法没有 body（可能是 abstract/extern/表达式体），无法进行行级操作\", \"tip\":\"用 csharpcode_read 确认方法结构\", \"note\":\"文件未被修改\"}";
                }
                SourceText sourceText = loc.Doc.GetTextAsync().GetAwaiter().GetResult();
                SyntaxTree tree = loc.Method.SyntaxTree;
                FileLinePositionSpan fullSpan = tree.GetLineSpan(loc.Method.FullSpan);
                int memberStartLine = fullSpan.StartLinePosition.Line;
                int absStart = memberStartLine + startLine - 1;
                int absEnd = memberStartLine + endLine - 1;
                FileLinePositionSpan bodySpan = body.GetLocation().GetLineSpan();
                int bodyAbsStart = bodySpan.StartLinePosition.Line;
                int bodyAbsEnd = bodySpan.EndLinePosition.Line;
                if (absStart <= bodyAbsStart || absEnd >= bodyAbsEnd)
                {
                    return "{\"ok\":false, \"error\":\"LINE_OUT_OF_RANGE\", \"detail\":\"行号 " + startLine + "-" + endLine + " 超出方法体范围\", \"tip\":\"用 csharpcode_read 确认方法内行号（// LN标注），注意不能覆盖 body 首尾大括号行\", \"note\":\"文件未被修改\"}";
                }
                int totalLines = sourceText.Lines.Count;
                if (absStart < 0 || absStart >= totalLines || absEnd < 0 || absEnd >= totalLines)
                {
                    return "{\"ok\":false, \"error\":\"LINE_OUT_OF_RANGE\", \"detail\":\"绝对行号越界：start=" + absStart + " end=" + absEnd + "，文件共" + totalLines + "行。可能原因：linepatch 后未重新 csharpcode_read 确认行号\", \"tip\":\"先 csharpcode_read 重新读取方法确认当前行号\", \"note\":\"文件未被修改\"}";
                }
                TextSpan span = TextSpan.FromBounds(sourceText.Lines[absStart].Start, sourceText.Lines[absEnd].EndIncludingLineBreak);
                string replacedText = sourceText.GetSubText(span).ToString();
                string safeNewText = newText;
                if (sourceText.Lines[absEnd].EndIncludingLineBreak > sourceText.Lines[absEnd].End && !safeNewText.EndsWith("\n"))
                {
                    safeNewText = safeNewText + "\n";
                }
                SourceText newSource = sourceText.Replace(span, safeNewText);
                Document newDoc = loc.Doc.WithText(newSource);
                string result = ApplyDocChange(state, newDoc, className, className + "." + methodName);
                if (result.StartsWith("{\"ok\":true"))
                {
                    string esc = replacedText.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "");
                    result = result.Insert(result.Length - 1, ", \"replaced\":\"" + esc + "\"");
                }
                return result;
            }
            catch (Exception ex)
            {
                return "{\"ok\":false, \"error\":\"LINEPATCH_FAILED\", \"detail\":\"" + ex.Message + "\"}";
            }
        }

        /// <summary>
        /// 在方法内指定行后插入
        /// </summary>
        /// <param name="className">类名</param>
        /// <param name="methodName">方法名</param>
        /// <param name="afterLine">插入位置（0=body头）</param>
        /// <param name="newText">插入文本</param>
        /// <returns>JSON</returns>
        public string LineInsert(string className, string methodName, int afterLine, string newText)
        {
            WorkspaceState state = GetActive();
            if (state == null) { return NotInit(); }
            MethodLoc loc = LocateMethod(state, className, methodName);
            if (loc.Error != null) { return loc.Error; }
            try
            {
                BlockSyntax body = loc.Method.Body;
                if (body == null)
                {
                    return "{\"ok\":false, \"error\":\"NO_BODY\", \"detail\":\"该方法没有 body（可能是 abstract/extern/表达式体），无法进行行级操作\", \"tip\":\"用 csharpcode_read 确认方法结构\", \"note\":\"文件未被修改\"}";
                }
                SourceText sourceText = loc.Doc.GetTextAsync().GetAwaiter().GetResult();
                SyntaxTree tree = loc.Method.SyntaxTree;
                FileLinePositionSpan fullSpan = tree.GetLineSpan(loc.Method.FullSpan);
                int memberStartLine = fullSpan.StartLinePosition.Line;
                FileLinePositionSpan bodySpan = body.GetLocation().GetLineSpan();
                int bodyAbsStart = bodySpan.StartLinePosition.Line;
                TextSpan insertSpan;
                string insertContent;
                if (afterLine == 0)
                {
                    TextLine line = sourceText.Lines[bodyAbsStart];
                    int afterBrace = line.ToString().IndexOf('{') + 1;
                    insertSpan = new TextSpan(line.Start + afterBrace, 0);
                    insertContent = "\n" + newText;
                }
                else
                {
                    int insertAbsLine = memberStartLine + afterLine - 1;
                    int totalLines = sourceText.Lines.Count;
                    if (insertAbsLine < 0 || insertAbsLine >= totalLines)
                    {
                        return "{\"ok\":false, \"error\":\"LINE_OUT_OF_RANGE\", \"detail\":\"绝对行号越界：line=" + insertAbsLine + "，文件共" + totalLines + "行。可能原因：lineinsert 后未重新 csharpcode_read 确认行号\", \"tip\":\"先 csharpcode_read 重新读取方法确认当前行号\", \"note\":\"文件未被修改\"}";
                    }
                    TextLine line = sourceText.Lines[insertAbsLine];
                    insertSpan = new TextSpan(line.EndIncludingLineBreak, 0);
                    insertContent = newText + "\n";
                }
                SourceText newSource = sourceText.Replace(insertSpan, insertContent);
                Document newDoc = loc.Doc.WithText(newSource);
                string result = ApplyDocChange(state, newDoc, className, className + "." + methodName);
                if (result.StartsWith("{\"ok\":true"))
                {
                    result = result.Insert(result.Length - 1, ", \"inserted_after_line\":" + afterLine);
                }
                return result;
            }
            catch (Exception ex)
            {
                return "{\"ok\":false, \"error\":\"LINEINSERT_FAILED\", \"detail\":\"" + ex.Message + "\"}";
            }
        }

        /// <summary>
        /// 在类中插入新成员
        /// </summary>
        /// <param name="className">类名</param>
        /// <param name="position">after/before/end/after_fields</param>
        /// <param name="anchor">锚点成员名</param>
        /// <param name="code">新成员源码</param>
        /// <returns>JSON</returns>
        public string InsertMember(string className, string position, string anchor, string code)
        {
            WorkspaceState state = GetActive();
            if (state == null) { return NotInit(); }
            Document doc;
            if (!state.ClassIndex.TryGetValue(className, out doc))
            {
                return "{\"ok\":false, \"error\":\"CLASS_NOT_FOUND\", \"detail\":\"未找到类: " + className + "\", \"tip\":\"类名拼写错误？用 csharpcode_list 查看全项目类名\"}";
            }
            try
            {
                SyntaxTree tree = doc.GetSyntaxTreeAsync().GetAwaiter().GetResult();
                CompilationUnitSyntax root = tree.GetCompilationUnitRoot();
                TypeDeclarationSyntax typeDecl = root.DescendantNodes().OfType<TypeDeclarationSyntax>()
                    .FirstOrDefault(c => c.Identifier.Text == className);
                if (typeDecl == null)
                {
                    return "{\"ok\":false, \"error\":\"CLASS_NOT_FOUND\", \"detail\":\"未找到类: " + className + "\", \"tip\":\"类名拼写错误？用 csharpcode_list 查看全项目类名\"}";
                }
                MemberDeclarationSyntax newMember;
                try
                {
                    newMember = SyntaxFactory.ParseMemberDeclaration(code);
                }
                catch
                {
                    return "{\"ok\":false, \"error\":\"PARSE_ERROR\", \"detail\":\"新成员解析失败\"}";
                }
                if (newMember == null)
                {
                    return "{\"ok\":false, \"error\":\"PARSE_ERROR\", \"detail\":\"新成员解析失败\"}";
                }
                newMember = newMember.NormalizeWhitespace();
                int insertIndex;
                if (position == "after" || position == "before")
                {
                    if (string.IsNullOrEmpty(anchor))
                    {
                        return "{\"ok\":false, \"error\":\"MISSING_ANCHOR\", \"detail\":\"after/before 需要 anchor 参数\"}";
                    }
                    int anchorIdx = -1;
                    for (int i = 0; i < typeDecl.Members.Count; i = i + 1)
                    {
                        if (GetMemberName(typeDecl.Members[i]) == anchor) { anchorIdx = i; break; }
                    }
                    if (anchorIdx < 0)
                    {
                        return "{\"ok\":false, \"error\":\"ANCHOR_NOT_FOUND\", \"detail\":\"锚点成员未找到: " + anchor + "\"}";
                    }
                    insertIndex = (position == "after") ? anchorIdx + 1 : anchorIdx;
                }
                else if (position == "after_fields")
                {
                    insertIndex = 0;
                    for (int i = 0; i < typeDecl.Members.Count; i = i + 1)
                    {
                        if (!(typeDecl.Members[i] is FieldDeclarationSyntax)) { insertIndex = i; break; }
                        insertIndex = i + 1;
                    }
                }
                else
                {
                    insertIndex = typeDecl.Members.Count;
                }
                List<MemberDeclarationSyntax> memList = typeDecl.Members.ToList();
                memList.Insert(insertIndex, newMember);
                TypeDeclarationSyntax newTypeDecl = typeDecl.WithMembers(SyntaxFactory.List(memList));
                CompilationUnitSyntax newRoot = root.ReplaceNode(typeDecl, newTypeDecl);
                Document newDoc = doc.WithSyntaxRoot(newRoot);
                return ApplyDocChange(state, newDoc, className, null);
            }
            catch (Exception ex)
            {
                return "{\"ok\":false, \"error\":\"INSERT_FAILED\", \"detail\":\"" + ex.Message + "\"}";
            }
        }

        /// <summary>
        /// 删除指定成员
        /// </summary>
        /// <param name="className">类名</param>
        /// <param name="memberName">成员名</param>
        /// <returns>JSON</returns>
        public string DeleteMember(string className, string memberName)
        {
            WorkspaceState state = GetActive();
            if (state == null) { return NotInit(); }
            Document doc;
            if (!state.ClassIndex.TryGetValue(className, out doc))
            {
                return "{\"ok\":false, \"error\":\"CLASS_NOT_FOUND\", \"detail\":\"未找到类: " + className + "\", \"tip\":\"类名拼写错误？用 csharpcode_list 查看全项目类名\"}";
            }
            try
            {
                SyntaxTree tree = doc.GetSyntaxTreeAsync().GetAwaiter().GetResult();
                CompilationUnitSyntax root = tree.GetCompilationUnitRoot();
                TypeDeclarationSyntax typeDecl = root.DescendantNodes().OfType<TypeDeclarationSyntax>()
                    .FirstOrDefault(c => c.Identifier.Text == className);
                if (typeDecl == null)
                {
                    return "{\"ok\":false, \"error\":\"CLASS_NOT_FOUND\", \"detail\":\"未找到类: " + className + "\", \"tip\":\"类名拼写错误？用 csharpcode_list 查看全项目类名\"}";
                }
                MemberDeclarationSyntax member = FindMember(typeDecl, memberName);
                if (member == null)
                {
                    return "{\"ok\":false, \"error\":\"MEMBER_NOT_FOUND\", \"detail\":\"未找到成员: " + className + "." + memberName + "\", \"tip\":\"成员名拼写错误？用 csharpcode_list " + className + " 查看该类成员\"}";
                }
                TypeDeclarationSyntax newTypeDecl = typeDecl.RemoveNode(member, SyntaxRemoveOptions.KeepNoTrivia);
                if (newTypeDecl == null)
                {
                    return "{\"ok\":false, \"error\":\"REMOVE_FAILED\"}";
                }
                CompilationUnitSyntax newRoot = root.ReplaceNode(typeDecl, newTypeDecl);
                Document newDoc = doc.WithSyntaxRoot(newRoot);
                return ApplyDocChange(state, newDoc, className, className + "." + memberName);
            }
            catch (Exception ex)
            {
                return "{\"ok\":false, \"error\":\"DELETE_FAILED\", \"detail\":\"" + ex.Message + "\"}";
            }
        }

        /// <summary>
        /// 设置 XML 注释
        /// </summary>
        /// <param name="className">类名</param>
        /// <param name="memberName">成员名（空=类）</param>
        /// <param name="commentType">summary/param/returns</param>
        /// <param name="text">注释文本</param>
        /// <param name="paramName">param 名</param>
        /// <returns>JSON</returns>
        public string SetComment(string className, string memberName, string commentType, string text, string paramName)
        {
            WorkspaceState state = GetActive();
            if (state == null) { return NotInit(); }
            Document doc;
            if (!state.ClassIndex.TryGetValue(className, out doc))
            {
                return "{\"ok\":false, \"error\":\"CLASS_NOT_FOUND\", \"detail\":\"未找到类: " + className + "\", \"tip\":\"类名拼写错误？用 csharpcode_list 查看全项目类名\"}";
            }
            try
            {
                SyntaxTree tree = doc.GetSyntaxTreeAsync().GetAwaiter().GetResult();
                CompilationUnitSyntax root = tree.GetCompilationUnitRoot();
                TypeDeclarationSyntax typeDecl = root.DescendantNodes().OfType<TypeDeclarationSyntax>()
                    .FirstOrDefault(c => c.Identifier.Text == className);
                if (typeDecl == null)
                {
                    return "{\"ok\":false, \"error\":\"CLASS_NOT_FOUND\", \"detail\":\"未找到类: " + className + "\", \"tip\":\"类名拼写错误？用 csharpcode_list 查看全项目类名\"}";
                }
                MemberDeclarationSyntax target;
                if (string.IsNullOrEmpty(memberName))
                {
                    target = typeDecl;
                }
                else
                {
                    target = FindMember(typeDecl, memberName);
                    if (target == null)
                    {
                        return "{\"ok\":false, \"error\":\"MEMBER_NOT_FOUND\", \"detail\":\"未找到成员: " + className + "." + memberName + "\", \"tip\":\"成员名拼写错误？用 csharpcode_list " + className + " 查看该类成员\"}";
                    }
                }
                MemberDeclarationSyntax newTarget = UpsertXmlComment(target, commentType, text, paramName);
                CompilationUnitSyntax newRoot;
                if (target == typeDecl)
                {
                    newRoot = root.ReplaceNode(typeDecl, (TypeDeclarationSyntax)newTarget);
                }
                else
                {
                    newRoot = root.ReplaceNode(typeDecl, typeDecl.ReplaceNode(target, newTarget));
                }
                Document newDoc = doc.WithSyntaxRoot(newRoot);
                string label = string.IsNullOrEmpty(memberName) ? className : className + "." + memberName;
                return ApplyDocChange(state, newDoc, className, label);
            }
            catch (Exception ex)
            {
                return "{\"ok\":false, \"error\":\"COMMENT_FAILED\", \"detail\":\"" + ex.Message + "\"}";
            }
        }

        /// <summary>
        /// 扫描缺 summary
        /// </summary>
        /// <returns>清单</returns>
        public string CommentCheck()
        {
            WorkspaceState state = GetActive();
            if (state == null) { return NotInit(); }
            try
            {
                System.Text.StringBuilder sb = new System.Text.StringBuilder();
                int missing = 0;
                List<string> names = new List<string>(state.ClassIndex.Keys);
                names.Sort(StringComparer.Ordinal);
                foreach (string className in names)
                {
                    Document doc = state.ClassIndex[className];
                    SyntaxTree tree = doc.GetSyntaxTreeAsync().GetAwaiter().GetResult();
                    CompilationUnitSyntax root = tree.GetCompilationUnitRoot();
                    ClassDeclarationSyntax cls = root.DescendantNodes().OfType<ClassDeclarationSyntax>()
                        .FirstOrDefault(c => c.Identifier.Text == className);
                    if (cls == null) { continue; }
                    if (string.IsNullOrEmpty(GetSummaryText(cls)))
                    {
                        missing = missing + 1;
                        sb.Append(className);
                        sb.Append(" // 缺 summary\n");
                    }
                    foreach (MemberDeclarationSyntax m in cls.Members)
                    {
                        if (string.IsNullOrEmpty(GetSummaryText(m)))
                        {
                            missing = missing + 1;
                            sb.Append("  ");
                            sb.Append(GetMemberKind(m));
                            sb.Append(" '");
                            sb.Append(GetMemberName(m));
                            sb.Append("' // 缺 summary\n");
                        }
                    }
                }
                sb.Append("---\n共 ");
                sb.Append(missing);
                sb.Append(" 项缺失 XML 注释");
                return sb.ToString();
            }
            catch (Exception ex)
            {
                return "{\"ok\":false, \"error\":\"COMMENT_CHECK_FAILED\", \"detail\":\"" + ex.Message + "\"}";
            }
        }

        /// <summary>
        /// 重命名成员——全项目引用同步
        /// </summary>
        /// <param name="className">类名</param>
        /// <param name="oldName">旧名</param>
        /// <param name="newName">新名</param>
        /// <returns>JSON</returns>
        public string RenameMember(string className, string oldName, string newName)
        {
            WorkspaceState state = GetActive();
            if (state == null) { return NotInit(); }
            int prevErrors = state.LastErrorCount;
            int prevWarnings = state.LastWarningCount;
            try
            {
                Document doc;
                if (!state.ClassIndex.TryGetValue(className, out doc))
                {
                    return "{\"ok\":false, \"error\":\"CLASS_NOT_FOUND\", \"detail\":\"未找到类: " + className + "\", \"tip\":\"类名拼写错误？用 csharpcode_list 查看全项目类名\"}";
                }
                if (doc.FilePath == null || !File.Exists(doc.FilePath))
                {
                    return "{\"ok\":false, \"error\":\"FILE_NOT_FOUND\"}";
                }
                string fileText = File.ReadAllText(doc.FilePath, System.Text.Encoding.UTF8);
                SyntaxTree tree = CSharpSyntaxTree.ParseText(fileText);
                CompilationUnitSyntax root = tree.GetCompilationUnitRoot();
                TypeDeclarationSyntax typeDecl = root.DescendantNodes().OfType<TypeDeclarationSyntax>()
                    .FirstOrDefault(c => c.Identifier.Text == className);
                if (typeDecl == null)
                {
                    return "{\"ok\":false, \"error\":\"CLASS_NOT_FOUND\", \"detail\":\"未找到类: " + className + "\", \"tip\":\"类名拼写错误？用 csharpcode_list 查看全项目类名\"}";
                }
                MemberDeclarationSyntax member = FindMember(typeDecl, oldName);
                if (member == null)
                {
                    return "{\"ok\":false, \"error\":\"MEMBER_NOT_FOUND\", \"detail\":\"未找到成员: " + className + "." + oldName + "\", \"tip\":\"成员名拼写错误？用 csharpcode_list " + className + " 查看该类成员\"}";
                }
                Compilation compilation = state.Project.GetCompilationAsync().GetAwaiter().GetResult();
                if (compilation == null)
                {
                    return "{\"ok\":false, \"error\":\"COMPILE_FAILED\"}";
                }
                ISymbol symbol = compilation.GetSymbolsWithName(oldName, SymbolFilter.Member)
                    .FirstOrDefault(s => s.ContainingType != null && s.ContainingType.Name == className);
                if (symbol == null)
                {
                    return "{\"ok\":false, \"error\":\"SYMBOL_ERROR\", \"detail\":\"无法获取符号，尝试 re-init 后重试\"}";
                }
#pragma warning disable CS0618
                Solution newSolution = Renamer.RenameSymbolAsync(state.Solution, symbol, newName, state.Solution.Options).GetAwaiter().GetResult();
#pragma warning restore CS0618
                Project newProject = newSolution.GetProject(state.Project.Id);
                Compilation newCompilation = newProject.GetCompilationAsync().GetAwaiter().GetResult();
                int errCount = 0;
                int warnCount = 0;
                List<string> newDiags = new List<string>();
                if (newCompilation != null)
                {
                    foreach (Diagnostic diag in newCompilation.GetDiagnostics())
                    {
                        if (diag.Severity == DiagnosticSeverity.Error)
                        {
                            errCount = errCount + 1;
                            if (newDiags.Count < 20) { newDiags.Add(FormatDiagnostic(state, diag)); }
                        }
                        else if (diag.Severity == DiagnosticSeverity.Warning)
                        {
                            warnCount = warnCount + 1;
                        }
                    }
                }
                if (errCount > prevErrors)
                {
                    int newErrors = errCount - prevErrors;
                    System.Text.StringBuilder errSb = new System.Text.StringBuilder();
                    errSb.Append("{\"ok\":false, \"error\":\"COMPILE_ERROR\", \"detail\":\"重命名引入了 " + newErrors + " 个新编译错误，未写盘\"");
                    errSb.Append(", \"errors\":" + errCount + ", \"error_delta\":" + newErrors);
                    if (newDiags.Count > 0)
                    {
                        errSb.Append(", \"new_diagnostics\":[");
                        for (int i = 0; i < newDiags.Count; i = i + 1)
                        {
                            if (i > 0) { errSb.Append(","); }
                            errSb.Append("\n    \"");
                            errSb.Append(newDiags[i].Replace("\\", "\\\\").Replace("\"", "\\\""));
                            errSb.Append("\"");
                        }
                        errSb.Append("]");
                    }
                    errSb.Append("}");
                    return errSb.ToString();
                }
                bool applied = state.Workspace.TryApplyChanges(newSolution);
                if (!applied)
                {
                    return "{\"ok\":false, \"error\":\"APPLY_FAILED\", \"detail\":\"TryApplyChanges 失败\"}";
                }
                state.Solution = newSolution;
                state.Project = newSolution.GetProject(state.Project.Id);
                RefreshIndex(state);
                state.LastErrorCount = errCount;
                state.LastWarningCount = warnCount;
                int errorDelta = errCount - prevErrors;
                int warningDelta = warnCount - prevWarnings;
                return "{\"ok\":true, \"label\":\"" + className + "." + oldName + "→" + newName + "\", \"errors\":" + errCount + ", \"warnings\":" + warnCount + ", \"prev_errors\":" + prevErrors + ", \"prev_warnings\":" + prevWarnings + ", \"error_delta\":" + errorDelta + ", \"warning_delta\":" + warningDelta + "}";
            }
            catch (Exception ex)
            {
                return "{\"ok\":false, \"error\":\"RENAME_FAILED\", \"detail\":\"" + ex.Message + "\"}";
            }
        }

        /// <summary>
        /// 扫描零引用 private/internal 成员
        /// </summary>
        /// <returns>清单</returns>
        public string DeadCode()
        {
            WorkspaceState state = GetActive();
            if (state == null) { return NotInit(); }
            try
            {
                Compilation compilation = state.Project.GetCompilationAsync().GetAwaiter().GetResult();
                if (compilation == null)
                {
                    return "{\"ok\":false, \"error\":\"COMPILE_FAILED\"}";
                }
                System.Text.StringBuilder sb = new System.Text.StringBuilder();
                int deadCount = 0;
                List<string> names = new List<string>(state.ClassIndex.Keys);
                names.Sort(StringComparer.Ordinal);
                foreach (string className in names)
                {
                    Document doc = state.ClassIndex[className];
                    SyntaxTree tree = doc.GetSyntaxTreeAsync().GetAwaiter().GetResult();
                    CompilationUnitSyntax root = tree.GetCompilationUnitRoot();
                    ClassDeclarationSyntax cls = root.DescendantNodes().OfType<ClassDeclarationSyntax>()
                        .FirstOrDefault(c => c.Identifier.Text == className);
                    if (cls == null) { continue; }
                    SemanticModel sem = compilation.GetSemanticModel(tree);
                    foreach (MemberDeclarationSyntax member in cls.Members)
                    {
                        ISymbol symbol = sem.GetDeclaredSymbol(member);
                        if (symbol == null) { continue; }
                        if (symbol.DeclaredAccessibility == Microsoft.CodeAnalysis.Accessibility.Public) { continue; }
                        if (symbol is IMethodSymbol ms && ms.MethodKind == MethodKind.Constructor) { continue; }
                        if (symbol is IMethodSymbol ms2 && ms2.MethodKind == MethodKind.StaticConstructor) { continue; }
                        IEnumerable<ReferencedSymbol> refs = SymbolFinder.FindReferencesAsync(symbol, state.Solution).GetAwaiter().GetResult();
                        bool hasRef = false;
                        foreach (ReferencedSymbol rs in refs)
                        {
                            if (rs.Locations.Any()) { hasRef = true; break; }
                        }
                        if (!hasRef)
                        {
                            deadCount = deadCount + 1;
                            sb.Append(className);
                            sb.Append(".");
                            sb.Append(GetMemberName(member));
                            sb.Append(" (");
                            sb.Append(GetMemberKind(member));
                            sb.Append(")\n");
                        }
                    }
                }
                if (deadCount == 0) { sb.Append("未发现零引用成员"); }
                else { sb.Append("---\n共 "); sb.Append(deadCount); sb.Append(" 项"); }
                return sb.ToString();
            }
            catch (Exception ex)
            {
                return "{\"ok\":false, \"error\":\"DEAD_FAILED\", \"detail\":\"" + ex.Message + "\"}";
            }
        }

        /// <summary>
        /// 查找成员引用
        /// </summary>
        /// <param name="className">类名</param>
        /// <param name="memberName">成员名</param>
        /// <returns>JSON</returns>
        public string FindReferences(string className, string memberName)
        {
            WorkspaceState state = GetActive();
            if (state == null) { return NotInit(); }
            Document doc;
            if (!state.ClassIndex.TryGetValue(className, out doc))
            {
                return "{\"ok\":false, \"error\":\"CLASS_NOT_FOUND\", \"detail\":\"未找到类: " + className + "\", \"tip\":\"类名拼写错误？用 csharpcode_list 查看全项目类名\"}";
            }
            try
            {
                SyntaxTree tree = doc.GetSyntaxTreeAsync().GetAwaiter().GetResult();
                CompilationUnitSyntax root = tree.GetCompilationUnitRoot();
                TypeDeclarationSyntax typeDecl = root.DescendantNodes().OfType<TypeDeclarationSyntax>()
                    .FirstOrDefault(c => c.Identifier.Text == className);
                if (typeDecl == null)
                {
                    return "{\"ok\":false, \"error\":\"CLASS_NOT_FOUND\", \"detail\":\"未找到类: " + className + "\", \"tip\":\"类名拼写错误？用 csharpcode_list 查看全项目类名\"}";
                }
                MemberDeclarationSyntax member = FindMember(typeDecl, memberName);
                if (member == null)
                {
                    return "{\"ok\":false, \"error\":\"MEMBER_NOT_FOUND\", \"detail\":\"未找到成员: " + className + "." + memberName + "\", \"tip\":\"成员名拼写错误？用 csharpcode_list " + className + " 查看该类成员\"}";
                }
                if (doc.FilePath != null)
                {
                    foreach (Document projDoc in state.Project.Documents)
                    {
                        if (projDoc.FilePath == doc.FilePath)
                        {
                            doc = projDoc;
                            break;
                        }
                    }
                }
                SemanticModel sem = doc.GetSemanticModelAsync().GetAwaiter().GetResult();
                if (sem == null)
                {
                    return "{\"ok\":false, \"error\":\"SEMANTIC_ERROR\"}";
                }
                ISymbol symbol = sem.GetDeclaredSymbol(member);
                if (symbol == null)
                {
                    return "{\"ok\":false, \"error\":\"SYMBOL_ERROR\", \"detail\":\"无法获取符号\"}";
                }
                IEnumerable<ReferencedSymbol> refs = SymbolFinder.FindReferencesAsync(symbol, state.Solution).GetAwaiter().GetResult();
                System.Text.StringBuilder sb = new System.Text.StringBuilder();
                sb.Append("{\"ok\":true, \"member\":\"");
                sb.Append(className);
                sb.Append(".");
                sb.Append(memberName);
                sb.Append("\", \"references\":[");
                bool first = true;
                foreach (ReferencedSymbol refSym in refs)
                {
                    foreach (ReferenceLocation refLoc in refSym.Locations)
                    {
                        if (!first) { sb.Append(","); }
                        first = false;
                        string filePath = refLoc.Document.FilePath ?? "";
                        int line = refLoc.Location.GetLineSpan().StartLinePosition.Line + 1;
                        SourceText text = refLoc.Document.GetTextAsync().GetAwaiter().GetResult();
                        TextLine lineSpan = text.Lines[line - 1];
                        string lineText = lineSpan.ToString().Trim();
                        sb.Append("\n    {\"file\":\"");
                        sb.Append(filePath.Replace("\\", "\\\\"));
                        sb.Append("\", \"line\":" + line);
                        sb.Append(", \"text\":\"");
                        sb.Append(lineText.Replace("\\", "\\\\").Replace("\"", "\\\""));
                        sb.Append("\"}");
                    }
                }
                sb.Append("]}");
                return sb.ToString();
            }
            catch (Exception ex)
            {
                return "{\"ok\":false, \"error\":\"FINDREF_FAILED\", \"detail\":\"" + ex.Message + "\"}";
            }
        }

        /// <summary>
        /// 释放全部工作区
        /// </summary>
        public void Shutdown()
        {
            lock (_gate)
            {
                foreach (WorkspaceState state in _trees.Values)
                {
                    try { state.Workspace.Dispose(); } catch { }
                }
                _trees.Clear();
                _activeKey = "";
            }
        }

        // ═══════════════════════════════════════════
        // 内部工具——移植 CH2
        // ═══════════════════════════════════════════

        /// <summary>
        /// 统计诊断
        /// </summary>
        /// <param name="state">工作区</param>
        /// <param name="compilation">编译</param>
        /// <param name="errorCount">错误数</param>
        /// <param name="warningCount">警告数</param>
        private static void CountDiagnostics(WorkspaceState state, Compilation compilation, out int errorCount, out int warningCount)
        {
            errorCount = 0;
            warningCount = 0;
            foreach (Diagnostic diag in compilation.GetDiagnostics())
            {
                if (diag.Severity == DiagnosticSeverity.Error)
                {
                    errorCount = errorCount + 1;
                }
                else if (diag.Severity == DiagnosticSeverity.Warning)
                {
                    warningCount = warningCount + 1;
                }
            }
        }

        /// <summary>
        /// 方法定位
        /// </summary>
        private struct MethodLoc
        {
            /// <summary>
            /// 文档
            /// </summary>
            internal Document Doc;

            /// <summary>
            /// 方法
            /// </summary>
            internal BaseMethodDeclarationSyntax Method;

            /// <summary>
            /// 错误（非空=失败）
            /// </summary>
            internal string Error;
        }

        /// <summary>
        /// 定位方法
        /// </summary>
        /// <param name="state">工作区</param>
        /// <param name="className">类名</param>
        /// <param name="methodName">方法名</param>
        /// <returns>定位结果</returns>
        private MethodLoc LocateMethod(WorkspaceState state, string className, string methodName)
        {
            MethodLoc loc = new MethodLoc();
            Document doc;
            if (!state.ClassIndex.TryGetValue(className, out doc))
            {
                loc.Error = "{\"ok\":false, \"error\":\"CLASS_NOT_FOUND\", \"detail\":\"未找到类: " + className + "\", \"tip\":\"类名拼写错误？用 csharpcode_list 查看全项目类名\"}";
                return loc;
            }
            SyntaxTree tree = doc.GetSyntaxTreeAsync().GetAwaiter().GetResult();
            if (tree == null)
            {
                loc.Error = "{\"ok\":false, \"error\":\"SYNTAX_ERROR\", \"detail\":\"语法树获取失败\", \"tip\":\"项目文件可能已损坏，重新 csharpcode_init 试试\"}";
                return loc;
            }
            CompilationUnitSyntax root = tree.GetCompilationUnitRoot();
            TypeDeclarationSyntax typeDecl = root.DescendantNodes().OfType<TypeDeclarationSyntax>()
                .FirstOrDefault(c => c.Identifier.Text == className);
            if (typeDecl == null)
            {
                loc.Error = "{\"ok\":false, \"error\":\"CLASS_NOT_FOUND\", \"detail\":\"未找到类: " + className + "\", \"tip\":\"类名拼写错误？用 csharpcode_list 查看全项目类名\"}";
                return loc;
            }
            MethodDeclarationSyntax method = typeDecl.Members.OfType<MethodDeclarationSyntax>()
                .FirstOrDefault(m => m.Identifier.Text == methodName);
            if (method != null)
            {
                loc.Doc = doc;
                loc.Method = method;
                return loc;
            }
            ConstructorDeclarationSyntax ctor = typeDecl.Members.OfType<ConstructorDeclarationSyntax>()
                .FirstOrDefault(c => c.Identifier.Text == methodName);
            if (ctor != null)
            {
                loc.Doc = doc;
                loc.Method = ctor;
                return loc;
            }
            loc.Error = "{\"ok\":false, \"error\":\"MEMBER_NOT_FOUND\", \"detail\":\"未找到成员: " + className + "." + methodName + "\", \"tip\":\"成员名拼写错误？用 csharpcode_list " + className + " 查看该类成员\"}";
            return loc;
        }

        /// <summary>
        /// ApplyDocChange——编译与提交分离
        /// </summary>
        /// <param name="state">工作区</param>
        /// <param name="newDoc">新文档</param>
        /// <param name="className">类名</param>
        /// <param name="label">标签</param>
        /// <returns>JSON</returns>
        private string ApplyDocChange(WorkspaceState state, Document newDoc, string className, string label)
        {
            int prevErrors = state.LastErrorCount;
            int prevWarnings = state.LastWarningCount;
            try
            {
                Solution newSolution = newDoc.Project.Solution;
                Project newProject = newSolution.GetProject(state.Project.Id);
                Compilation compilation = newProject.GetCompilationAsync().GetAwaiter().GetResult();
                int errCount = 0;
                int warnCount = 0;
                List<string> newDiags = new List<string>();
                if (compilation != null)
                {
                    foreach (Diagnostic diag in compilation.GetDiagnostics())
                    {
                        if (diag.Severity == DiagnosticSeverity.Error)
                        {
                            errCount = errCount + 1;
                            if (newDiags.Count < 20) { newDiags.Add(FormatDiagnostic(state, diag)); }
                        }
                        else if (diag.Severity == DiagnosticSeverity.Warning)
                        {
                            warnCount = warnCount + 1;
                        }
                    }
                }
                if (errCount > prevErrors)
                {
                    int newErrors = errCount - prevErrors;
                    RLog("[RoslynBridge] 本次修改引入 " + newErrors + " 个新错误 → 丢弃，未写盘");
                    System.Text.StringBuilder errSb = new System.Text.StringBuilder();
                    errSb.Append("{\"ok\":false, \"error\":\"COMPILE_ERROR\", \"detail\":\"操作引入了 " + newErrors + " 个新编译错误，未写盘。请检查：①语法是否正确（括号/分号）②linepatch/lineinsert 是否破坏了相邻 if/大括号结构 ③行号是否与 csharpcode_read 对齐\", \"tip\":\"用 csharpcode_compile(full=true) 查看具体错误\", \"note\":\"文件未被修改，请修正后重试\"");
                    errSb.Append(", \"errors\":" + errCount);
                    errSb.Append(", \"warnings\":" + warnCount);
                    errSb.Append(", \"prev_errors\":" + prevErrors);
                    errSb.Append(", \"prev_warnings\":" + prevWarnings);
                    errSb.Append(", \"error_delta\":" + newErrors);
                    if (newDiags.Count > 0)
                    {
                        errSb.Append(", \"new_diagnostics\":[");
                        for (int i = 0; i < newDiags.Count; i = i + 1)
                        {
                            if (i > 0) { errSb.Append(","); }
                            errSb.Append("\n    \"");
                            errSb.Append(newDiags[i].Replace("\\", "\\\\").Replace("\"", "\\\""));
                            errSb.Append("\"");
                        }
                        errSb.Append("]");
                    }
                    errSb.Append("}");
                    return errSb.ToString();
                }
                bool applied = state.Workspace.TryApplyChanges(newSolution);
                if (!applied)
                {
                    SourceText text = newDoc.GetTextAsync().GetAwaiter().GetResult();
                    string filePath = newDoc.FilePath;
                    if (filePath != null)
                    {
                        File.WriteAllText(filePath, text.ToString(), System.Text.Encoding.UTF8);
                    }
                    state.Workspace.Dispose();
                    state.Workspace = MSBuildWorkspace.Create();
                    state.Workspace.WorkspaceFailed += delegate (object? sender, WorkspaceDiagnosticEventArgs e)
                    {
                        if (e.Diagnostic.Kind == WorkspaceDiagnosticKind.Failure)
                        {
                            RLog("[Workspace] " + e.Diagnostic.Message);
                        }
                    };
                    state.Project = state.Workspace.OpenProjectAsync(state.CsprojPath).GetAwaiter().GetResult();
                    state.Solution = state.Project.Solution;
                    RLog("[RoslynBridge] TryApplyChanges 失败，已走 fallback 磁盘写+重建");
                }
                else
                {
                    state.Solution = state.Workspace.CurrentSolution;
                    state.Project = state.Solution.GetProject(state.Project.Id);
                }
                RefreshIndex(state);
                state.LastErrorCount = errCount;
                state.LastWarningCount = warnCount;
                int errorDelta = errCount - prevErrors;
                int warningDelta = warnCount - prevWarnings;
                System.Text.StringBuilder sb = new System.Text.StringBuilder();
                sb.Append("{\"ok\":true, \"label\":\"");
                sb.Append(label ?? className);
                sb.Append("\", \"errors\":" + errCount);
                sb.Append(", \"warnings\":" + warnCount);
                sb.Append(", \"prev_errors\":" + prevErrors);
                sb.Append(", \"prev_warnings\":" + prevWarnings);
                sb.Append(", \"error_delta\":" + errorDelta);
                sb.Append(", \"warning_delta\":" + warningDelta);
                if (newDiags.Count > 0)
                {
                    sb.Append(", \"new_diagnostics\":[");
                    for (int i = 0; i < newDiags.Count; i = i + 1)
                    {
                        if (i > 0) { sb.Append(","); }
                        sb.Append("\n    \"");
                        sb.Append(newDiags[i].Replace("\\", "\\\\").Replace("\"", "\\\""));
                        sb.Append("\"");
                    }
                    sb.Append("]");
                }
                sb.Append("}");
                return sb.ToString();
            }
            catch (Exception ex)
            {
                return "{\"ok\":false, \"error\":\"APPLY_FAILED\", \"detail\":\"" + ex.Message + "\"}";
            }
        }

        /// <summary>
        /// ApplyAndCompile——替换节点并提交
        /// </summary>
        /// <param name="state">工作区</param>
        /// <param name="doc">文档</param>
        /// <param name="oldNode">旧节点</param>
        /// <param name="newNode">新节点</param>
        /// <param name="className">类名</param>
        /// <param name="label">标签</param>
        /// <returns>JSON</returns>
        private string ApplyAndCompile(WorkspaceState state, Document doc, SyntaxNode oldNode, SyntaxNode newNode, string className, string label)
        {
            SyntaxTree tree = doc.GetSyntaxTreeAsync().GetAwaiter().GetResult();
            CompilationUnitSyntax root = tree.GetCompilationUnitRoot();
            CompilationUnitSyntax newRoot = root.ReplaceNode(oldNode, newNode);
            Document newDoc = doc.WithSyntaxRoot(newRoot);
            return ApplyDocChange(state, newDoc, className, label);
        }

        /// <summary>
        /// 刷新类索引
        /// </summary>
        /// <param name="state">工作区</param>
        private static void RefreshIndex(WorkspaceState state)
        {
            state.ClassIndex.Clear();
            foreach (Document doc in state.Project.Documents)
            {
                if (doc.FilePath == null || !doc.FilePath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)) { continue; }
                SyntaxTree tree = doc.GetSyntaxTreeAsync().GetAwaiter().GetResult();
                if (tree == null) { continue; }
                CompilationUnitSyntax root = tree.GetCompilationUnitRoot();
                foreach (ClassDeclarationSyntax cls in root.DescendantNodes().OfType<ClassDeclarationSyntax>())
                {
                    state.ClassIndex[cls.Identifier.Text] = doc;
                }
            }
        }

        /// <summary>
        /// 成员类型标签
        /// </summary>
        /// <param name="member">成员</param>
        /// <returns>标签</returns>
        private static string GetMemberKind(MemberDeclarationSyntax member)
        {
            if (member is MethodDeclarationSyntax) { return "方法"; }
            if (member is PropertyDeclarationSyntax) { return "属性"; }
            if (member is ConstructorDeclarationSyntax) { return "构造函数"; }
            if (member is FieldDeclarationSyntax) { return "字段"; }
            if (member is ClassDeclarationSyntax) { return "嵌套类"; }
            if (member is StructDeclarationSyntax) { return "结构"; }
            if (member is EnumDeclarationSyntax) { return "枚举"; }
            if (member is InterfaceDeclarationSyntax) { return "接口"; }
            return "成员";
        }

        /// <summary>
        /// 成员名
        /// </summary>
        /// <param name="member">成员</param>
        /// <returns>名称</returns>
        private static string GetMemberName(MemberDeclarationSyntax member)
        {
            if (member is MethodDeclarationSyntax m) { return m.Identifier.Text; }
            if (member is PropertyDeclarationSyntax p) { return p.Identifier.Text; }
            if (member is ConstructorDeclarationSyntax c) { return c.Identifier.Text; }
            if (member is FieldDeclarationSyntax f)
            {
                if (f.Declaration.Variables.Count > 0) { return f.Declaration.Variables[0].Identifier.Text; }
                return "?";
            }
            if (member is BaseTypeDeclarationSyntax t) { return t.Identifier.Text; }
            return "?";
        }

        /// <summary>
        /// 查找成员
        /// </summary>
        /// <param name="typeDecl">类型</param>
        /// <param name="memberName">成员名</param>
        /// <returns>成员或 null</returns>
        private static MemberDeclarationSyntax FindMember(TypeDeclarationSyntax typeDecl, string memberName)
        {
            foreach (MemberDeclarationSyntax member in typeDecl.Members)
            {
                if (member is MethodDeclarationSyntax m && m.Identifier.Text == memberName) { return m; }
                if (member is PropertyDeclarationSyntax p && p.Identifier.Text == memberName) { return p; }
                if (member is ConstructorDeclarationSyntax c && c.Identifier.Text == memberName) { return c; }
                if (member is FieldDeclarationSyntax f)
                {
                    foreach (VariableDeclaratorSyntax v in f.Declaration.Variables)
                    {
                        if (v.Identifier.Text == memberName) { return f; }
                    }
                }
                if (member is BaseTypeDeclarationSyntax t && t.Identifier.Text == memberName) { return t; }
            }
            return null;
        }

        /// <summary>
        /// 提取 summary 文本
        /// </summary>
        /// <param name="member">成员</param>
        /// <returns>文本</returns>
        private static string GetSummaryText(MemberDeclarationSyntax member)
        {
            foreach (SyntaxTrivia trivia in member.GetLeadingTrivia())
            {
                if (trivia.IsKind(SyntaxKind.SingleLineDocumentationCommentTrivia)
                    || trivia.IsKind(SyntaxKind.MultiLineDocumentationCommentTrivia))
                {
                    DocumentationCommentTriviaSyntax xmlComment = (DocumentationCommentTriviaSyntax)trivia.GetStructure();
                    if (xmlComment != null)
                    {
                        foreach (XmlNodeSyntax node in xmlComment.Content)
                        {
                            if (node is XmlElementSyntax el && el.StartTag.Name.ToString() == "summary")
                            {
                                string text = el.Content.ToString().Trim();
                                text = text.Replace("\r\n", " ").Replace("\n", " ");
                                if (text.Length > 80) { text = text.Substring(0, 77) + "…"; }
                                return text;
                            }
                        }
                    }
                }
            }
            return "";
        }

        /// <summary>
        /// 类型概览
        /// </summary>
        /// <param name="doc">文档</param>
        /// <param name="typeDecl">类型</param>
        /// <returns>概览文本</returns>
        private static string BuildClassOverview(Document doc, TypeDeclarationSyntax typeDecl)
        {
            FileLinePositionSpan span = typeDecl.GetLocation().GetLineSpan();
            string file = Path.GetFileName(doc.FilePath ?? "?");
            int start = span.StartLinePosition.Line + 1;
            int end = span.EndLinePosition.Line + 1;
            string header = "[" + file + ":" + start.ToString() + "-" + end.ToString() + "] " + typeDecl.Identifier.Text + "\n";
            string classComment = GetSummaryText(typeDecl);
            if (classComment.Length > 0) { header = header + "/// " + classComment + "\n"; }
            header = header + typeDecl.Members.Count + " 成员\n";
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            sb.Append(header);
            foreach (MemberDeclarationSyntax member in typeDecl.Members)
            {
                sb.Append("  ");
                sb.Append(GetMemberKind(member));
                sb.Append(" '");
                sb.Append(GetMemberName(member));
                sb.Append("'");
                if (member is MethodDeclarationSyntax m)
                {
                    sb.Append("(");
                    sb.Append(m.ParameterList.Parameters.ToString());
                    sb.Append(")");
                }
                string comment = GetSummaryText(member);
                if (comment.Length > 0) { sb.Append(" // "); sb.Append(comment); }
                sb.Append("\n");
            }
            sb.Append("---\n");
            sb.Append(typeDecl.Members.Count);
            sb.Append(" 成员");
            return sb.ToString();
        }

        /// <summary>
        /// 成员完整读本
        /// </summary>
        /// <param name="doc">文档</param>
        /// <param name="tree">语法树</param>
        /// <param name="member">成员</param>
        /// <returns>读本</returns>
        private static string BuildMemberReadout(Document doc, SyntaxTree tree, MemberDeclarationSyntax member)
        {
            string filePath = doc.FilePath ?? "?";
            FileLinePositionSpan span = member.GetLocation().GetLineSpan();
            int absStart = span.StartLinePosition.Line + 1;
            int absEnd = span.EndLinePosition.Line + 1;
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            sb.Append("[");
            sb.Append(Path.GetFileName(filePath));
            sb.Append(":");
            sb.Append(absStart);
            sb.Append("-");
            sb.Append(absEnd);
            sb.Append("]\n");
            string fullText = member.ToFullString();
            string[] lines = fullText.Split('\n');
            for (int i = 0; i < lines.Length; i = i + 1)
            {
                string line = lines[i].TrimEnd('\r');
                int localLine = i + 1;
                sb.Append(line);
                sb.Append(" // L");
                sb.Append(localLine);
                sb.Append("\n");
            }
            return sb.ToString();
        }

        /// <summary>
        /// 错误码建议
        /// </summary>
        /// <param name="errorCode">错误码</param>
        /// <returns>建议</returns>
        private static string GetErrorTip(string errorCode)
        {
            switch (errorCode)
            {
                case "CS0103": return "变量/类型/方法名拼写错误，或缺少 using 引用";
                case "CS0122": return "成员不可访问（private/internal），需改为 public 或添加访问器";
                case "CS1002": return "语法错误：缺少分号";
                case "CS0246": return "找不到类型或命名空间，检查 using 和拼写";
                case "CS1061": return "该类型不包含此方法/属性，检查类型是否正确";
                case "CS1503": return "方法参数类型不匹配";
                case "CS1591": return "缺少 XML 注释（summary）";
                case "CS0414": return "字段已赋值但从未使用";
                case "CS0649": return "字段从未赋值，将保持默认值";
                default: return "";
            }
        }

        /// <summary>
        /// 格式化诊断
        /// </summary>
        /// <param name="state">工作区</param>
        /// <param name="diag">诊断</param>
        /// <returns>文本</returns>
        private static string FormatDiagnostic(WorkspaceState state, Diagnostic diag)
        {
            string severity = diag.Severity == DiagnosticSeverity.Error ? "error" : "warning";
            FileLinePositionSpan span = diag.Location.GetLineSpan();
            int line = span.StartLinePosition.Line + 1;
            int col = span.StartLinePosition.Character + 1;
            string fileName = Path.GetFileName(span.Path);
            string msg = fileName + "(" + line + "," + col + "): " + severity + " " + diag.Id + ": " + diag.GetMessage();
            string tip = GetErrorTip(diag.Id);
            if (tip.Length > 0) { msg = msg + " [建议：" + tip + "]"; }
            return msg;
        }

        /// <summary>
        /// 追加或更新 XML 注释
        /// </summary>
        /// <param name="member">成员</param>
        /// <param name="commentType">类型</param>
        /// <param name="text">文本</param>
        /// <param name="paramName">参数名</param>
        /// <returns>新成员</returns>
        private static MemberDeclarationSyntax UpsertXmlComment(MemberDeclarationSyntax member, string commentType, string text, string paramName)
        {
            string newLine;
            if (commentType == "summary")
            {
                newLine = "/// <summary>\n/// " + text + "\n/// </summary>";
            }
            else if (commentType == "returns")
            {
                newLine = "/// <returns>" + text + "</returns>";
            }
            else
            {
                newLine = "/// <param name=\"" + paramName + "\">" + text + "</param>";
            }
            bool hasXml = false;
            SyntaxTrivia oldTrivia = default;
            foreach (SyntaxTrivia trivia in member.GetLeadingTrivia())
            {
                if (trivia.IsKind(SyntaxKind.SingleLineDocumentationCommentTrivia)
                    || trivia.IsKind(SyntaxKind.MultiLineDocumentationCommentTrivia))
                {
                    hasXml = true;
                    oldTrivia = trivia;
                    break;
                }
            }
            if (hasXml)
            {
                DocumentationCommentTriviaSyntax xml = (DocumentationCommentTriviaSyntax)oldTrivia.GetStructure();
                if (xml != null)
                {
                    List<string> keptTags = new List<string>();
                    foreach (XmlNodeSyntax node in xml.Content)
                    {
                        string nodeText = node.ToFullString().TrimEnd();
                        if (string.IsNullOrEmpty(nodeText)) { continue; }
                        bool isTarget = false;
                        if (node is XmlElementSyntax el)
                        {
                            string tagName = el.StartTag.Name.ToString();
                            if (commentType == "param") { isTarget = (tagName == "param"); }
                            else { isTarget = (tagName == commentType); }
                        }
                        if (!isTarget) { keptTags.Add(nodeText); }
                    }
                    string rebuilt = newLine;
                    foreach (string kt in keptTags) { rebuilt = rebuilt + "\n" + kt; }
                    SyntaxTriviaList newLeading = SyntaxFactory.ParseLeadingTrivia(rebuilt + "\n");
                    return member.WithLeadingTrivia(newLeading);
                }
            }
            SyntaxTriviaList oldLeading2 = member.GetLeadingTrivia();
            string insertComment;
            if (commentType == "summary") { insertComment = "/// <summary>\n/// " + text + "\n/// </summary>"; }
            else if (commentType == "returns") { insertComment = "/// <returns>" + text + "</returns>"; }
            else { insertComment = "/// <param name=\"" + paramName + "\">" + text + "</param>"; }
            SyntaxTriviaList insertList = SyntaxFactory.ParseLeadingTrivia(insertComment + "\n");
            return member.WithLeadingTrivia(insertList.AddRange(oldLeading2));
        }
    }
}
