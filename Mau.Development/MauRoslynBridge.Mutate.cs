using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Mau.Development
{
#pragma warning disable CS8600 // 环境 SDK 对 ConcurrentDictionary/Dictionary TryXXX(out TValue) 的 MaybeNullWhen 分析异常误报——out 实参非空变量也报警（最小复现验证）
    /// <summary>
    /// MauRoslynBridge 变更面——cs.find_ref / cs.patch / cs.member / cs.comment / cs.dead（partial 分部）。
    /// 写操作统一：编译验证 → 无新增错误才落盘（三态 OK/ROLLED_BACK/ERR）；落盘后内存立即 = 磁盘（写后自刷新）。
    /// </summary>
    public sealed partial class MauRoslynBridge
    {
        /// <summary>
        /// cs.find_ref——全引用（含重载全匹配——CH2 find_ref 仅首重载痛点修复）
        /// </summary>
        /// <param name="args">参数</param>
        /// <param name="result">结果</param>
        /// <returns>调用完成</returns>
        private bool ToolFindRef(JsonElement args, out string result)
        {
            string path = Arg(args, "path");
            string className = Arg(args, "class");
            string member = Arg(args, "member");
            if (member.Length == 0)
            {
                result = "ERR|BAD_ARGS|缺少参数 member";
                return false;
            }
            string csproj = ResolveProject(path);
            if (csproj.Length == 0)
            {
                result = "ERR|BAD_PATH|项目路径无效或越界: " + path;
                return false;
            }
            ProjectCache cache = EnsureProject(csproj);
            FullScan(cache);
            SyntaxTree foundTree;
            ClassDeclarationSyntax classNode;
            int total;
            if (!FindClassNode(cache, className, out foundTree, out classNode, out total))
            {
                result = "ERR|CLASS_NOT_FOUND|类不存在: " + className;
                return false;
            }
            SemanticModel classModel = EnsureSemantics(cache, foundTree.FilePath);
            INamedTypeSymbol? classSymbol = classModel.GetDeclaredSymbol(classNode) as INamedTypeSymbol;
            if (classSymbol == null)
            {
                result = "ERR|SYMBOL_BIND|类符号解析失败: " + className;
                return false;
            }
            System.Collections.Immutable.ImmutableArray<ISymbol> targets = classSymbol.GetMembers(member);
            if (targets.Length == 0)
            {
                result = "ERR|SYMBOL_NOT_FOUND|成员不存在: " + className + "." + member;
                return false;
            }
            List<string> hits = new List<string>();
            foreach (KeyValuePair<string, SyntaxTree> pair in cache.Trees)
            {
                SemanticModel model = EnsureSemantics(cache, pair.Key);
                SyntaxNode root = pair.Value.GetRoot();
                foreach (SyntaxNode node in root.DescendantNodes())
                {
                    string name = "";
                    SyntaxNode? namedNode = null;
                    IdentifierNameSyntax? identifier = node as IdentifierNameSyntax;
                    if (identifier != null)
                    {
                        name = identifier.Identifier.Text;
                        namedNode = identifier;
                    }
                    else
                    {
                        GenericNameSyntax? generic = node as GenericNameSyntax;
                        if (generic != null)
                        {
                            name = generic.Identifier.Text;
                            namedNode = generic;
                        }
                    }
                    if (namedNode == null || name != member)
                    {
                        continue;
                    }
                    SymbolInfo info = model.GetSymbolInfo(namedNode);
                    bool matched = MatchesAny(info.Symbol, targets);
                    if (!matched)
                    {
                        for (int i = 0; i < info.CandidateSymbols.Length; i = i + 1)
                        {
                            if (MatchesAny(info.CandidateSymbols[i], targets))
                            {
                                matched = true;
                                break;
                            }
                        }
                    }
                    if (matched)
                    {
                        FileLinePositionSpan span = namedNode.GetLocation().GetLineSpan();
                        string lineText = ExtractLineText(pair.Key, span.StartLinePosition.Line);
                        hits.Add(RelativeToProject(cache, pair.Key) + ":" + (span.StartLinePosition.Line + 1) + ":" + (span.StartLinePosition.Character + 1) + ": " + lineText);
                    }
                }
            }
            StringBuilder sb = new StringBuilder();
            if (hits.Count == 0)
            {
                sb.Append("OK 无引用: " + className + "." + member + "（0 处）");
            }
            else
            {
                sb.Append("共 " + hits.Count + " 处引用: " + className + "." + member);
                for (int i = 0; i < hits.Count; i = i + 1)
                {
                    sb.Append(Environment.NewLine);
                    sb.Append("  " + hits[i]);
                }
            }
            result = TrimResult(sb.ToString(), MaxResultChars);
            return true;
        }

        /// <summary>
        /// 符号是否命中目标集（SymbolEqualityComparer）
        /// </summary>
        /// <param name="symbol">候选符号（可空）</param>
        /// <param name="targets">目标集</param>
        /// <returns>是否命中</returns>
        private static bool MatchesAny(ISymbol? symbol, System.Collections.Immutable.ImmutableArray<ISymbol> targets)
        {
            if (symbol == null)
            {
                return false;
            }
            for (int i = 0; i < targets.Length; i = i + 1)
            {
                if (SymbolEqualityComparer.Default.Equals(symbol, targets[i]))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// 读取文件指定行文本（引用点上下文展示）
        /// </summary>
        /// <param name="filePath">文件</param>
        /// <param name="lineZero">0-based 行号</param>
        /// <returns>行文本（trim 80）</returns>
        private static string ExtractLineText(string filePath, int lineZero)
        {
            try
            {
                string[] lines = File.ReadAllLines(filePath);
                if (lineZero < 0 || lineZero >= lines.Length)
                {
                    return "";
                }
                string text = lines[lineZero].Trim();
                if (text.Length > 80)
                {
                    text = text.Substring(0, 80) + "…";
                }
                return text;
            }
            catch (Exception)
            {
                return "";
            }
        }

        /// <summary>
        /// cs.patch——方法体级替换（锚点=类+方法名；弃行号）。三态：OK 落盘 / ROLLED_BACK 未落盘+诊断 / ERR
        /// </summary>
        /// <param name="args">参数</param>
        /// <param name="result">结果</param>
        /// <returns>调用完成</returns>
        private bool ToolPatch(JsonElement args, out string result)
        {
            string path = Arg(args, "path");
            string className = Arg(args, "class");
            string methodName = Arg(args, "method");
            string body = Arg(args, "body");
            if (className.Length == 0 || methodName.Length == 0 || body.Length == 0)
            {
                result = "ERR|BAD_ARGS|缺少参数 class/method/body";
                return false;
            }
            string csproj = ResolveProject(path);
            if (csproj.Length == 0)
            {
                result = "ERR|BAD_PATH|项目路径无效或越界: " + path;
                return false;
            }
            ProjectCache cache = EnsureProject(csproj);
            FullScan(cache);
            lock (cache.Gate)
            {
                SyntaxTree foundTree;
                ClassDeclarationSyntax classNode;
                int classTotal;
                if (!FindClassNode(cache, className, out foundTree, out classNode, out classTotal))
                {
                    result = "ERR|CLASS_NOT_FOUND|类不存在: " + className;
                    return true;
                }
                MethodDeclarationSyntax methodNode;
                List<string> signatures;
                int methodFind = FindMethodInClass(classNode, methodName, out methodNode, out signatures);
                if (methodFind == 1)
                {
                    result = "ERR|METHOD_NOT_FOUND|方法不存在: " + className + "." + methodName;
                    return true;
                }
                if (methodFind == 2)
                {
                    StringBuilder sb = new StringBuilder();
                    sb.Append("ERR|AMBIGUOUS|方法重载歧义——请用 find_ref 区分签名（当前 patch 锚点=类+方法名，重载需先唯一化）:");
                    for (int i = 0; i < signatures.Count; i = i + 1)
                    {
                        sb.Append(Environment.NewLine + "  " + methodName + signatures[i]);
                    }
                    result = sb.ToString();
                    return true;
                }
                if (methodNode.Body == null)
                {
                    result = "ERR|ARROW_BODY|表达式体方法请先展开为块体（语法糖展开铁律）: " + className + "." + methodName;
                    return true;
                }
                StatementSyntax parsed;
                try
                {
                    parsed = SyntaxFactory.ParseStatement(body);
                }
                catch (Exception)
                {
                    parsed = null!;
                }
                BlockSyntax? block = parsed as BlockSyntax;
                if (block == null)
                {
                    result = "ERR|BAD_BODY|body 必须是以 { 开头 } 结尾的完整块（SyntaxFactory 解析失败或非块语句）";
                    return true;
                }
                MethodDeclarationSyntax newMethod = methodNode.WithBody(block);
                SyntaxNode root = foundTree.GetRoot();
                SyntaxNode newRoot = root.ReplaceNode(methodNode, newMethod);
                SyntaxTree newTree = CreateTreeFromRoot(foundTree, newRoot);
                CSharpCompilation newCompilation = (CSharpCompilation)cache.Compilation.ReplaceSyntaxTree(foundTree, newTree);
                List<string> newErrors;
                if (!ValidateNoNewErrors(cache.Compilation, newCompilation, out newErrors))
                {
                    StringBuilder sb = new StringBuilder();
                    sb.Append("ROLLED_BACK|新增编译错误 " + newErrors.Count + " 条——未落盘:");
                    for (int i = 0; i < newErrors.Count; i = i + 1)
                    {
                        sb.Append(Environment.NewLine + "  " + newErrors[i]);
                    }
                    result = TrimResult(sb.ToString(), MaxResultChars);
                    return true;
                }
                string filePath = foundTree.FilePath;
                WriteAtomicText(filePath, newRoot.ToFullString());
                cache.Trees[filePath] = newTree;
                cache.Compilation = newCompilation;
                cache.Stamps[filePath] = SnapshotOf(filePath);
                SemanticModel removed = null!;
                cache.Semantics.TryRemove(filePath, out removed);
                // OK 返回 + 目标方法段源码（H28 强制校验链——patch 后立即可见落盘状态）
                StringBuilder ok = new StringBuilder();
                ok.Append("OK 已落盘 " + RelativeToProject(cache, filePath) + ":" + className + "." + methodName);
                ok.Append(Environment.NewLine + "——目标方法段（校验链）:");
                ok.Append(Environment.NewLine + NumberedSource(newMethod.ToFullString()));
                result = TrimResult(ok.ToString(), MaxResultChars);
                return true;
            }
        }

        /// <summary>
        /// cs.member——增/删（树级）/重命名（语义级——全项目引用同步）
        /// </summary>
        /// <param name="args">参数</param>
        /// <param name="result">结果</param>
        /// <returns>调用完成</returns>
        private bool ToolMember(JsonElement args, out string result)
        {
            string op = Arg(args, "op");
            if (op == "insert")
            {
                return MemberInsert(args, out result);
            }
            if (op == "delete")
            {
                return MemberDelete(args, out result);
            }
            if (op == "rename")
            {
                return MemberRename(args, out result);
            }
            result = "ERR|BAD_ARGS|op 必须是 insert/delete/rename";
            return false;
        }

        /// <summary>
        /// member insert——类中插入成员（position: end/before/after/after_fields）
        /// </summary>
        /// <param name="args">参数</param>
        /// <param name="result">结果</param>
        /// <returns>调用完成</returns>
        private bool MemberInsert(JsonElement args, out string result)
        {
            string path = Arg(args, "path");
            string className = Arg(args, "class");
            string position = Arg(args, "position");
            string anchor = Arg(args, "anchor");
            string code = Arg(args, "code");
            if (className.Length == 0 || code.Length == 0)
            {
                result = "ERR|BAD_ARGS|缺少参数 class/code";
                return false;
            }
            if (position.Length == 0)
            {
                position = "end";
            }
            string csproj = ResolveProject(path);
            if (csproj.Length == 0)
            {
                result = "ERR|BAD_PATH|项目路径无效或越界: " + path;
                return false;
            }
            ProjectCache cache = EnsureProject(csproj);
            FullScan(cache);
            lock (cache.Gate)
            {
                SyntaxTree foundTree;
                ClassDeclarationSyntax classNode;
                int classTotal;
                if (!FindClassNode(cache, className, out foundTree, out classNode, out classTotal))
                {
                    result = "ERR|CLASS_NOT_FOUND|类不存在: " + className;
                    return true;
                }
                SyntaxTree codeTree = CSharpSyntaxTree.ParseText(code, ParseOptions());
                SyntaxNode codeRoot = codeTree.GetRoot();
                MemberDeclarationSyntax? newMember = SyntaxFactory.ParseMemberDeclaration(code);
                if (newMember == null)
                {
                    result = "ERR|BAD_CODE|code 不是有效的成员声明";
                    return true;
                }
                int insertIndex = -1;
                if (position == "end")
                {
                    insertIndex = classNode.Members.Count;
                }
                else if (position == "after_fields")
                {
                    insertIndex = 0;
                    for (int i = 0; i < classNode.Members.Count; i = i + 1)
                    {
                        if (classNode.Members[i] is FieldDeclarationSyntax)
                        {
                            insertIndex = i + 1;
                        }
                    }
                }
                else if (position == "after" || position == "before")
                {
                    if (anchor.Length == 0)
                    {
                        result = "ERR|BAD_ARGS|position=after/before 需要 anchor";
                        return true;
                    }
                    int foundIndex = -1;
                    for (int i = 0; i < classNode.Members.Count; i = i + 1)
                    {
                        MemberDeclarationSyntax member = classNode.Members[i];
                        string memberName = MemberNameOf(member);
                        if (memberName == anchor)
                        {
                            foundIndex = i;
                            break;
                        }
                    }
                    if (foundIndex < 0)
                    {
                        result = "ERR|ANCHOR_NOT_FOUND|锚点成员不存在: " + anchor;
                        return true;
                    }
                    insertIndex = position == "after" ? foundIndex + 1 : foundIndex;
                }
                else
                {
                    result = "ERR|BAD_ARGS|position 必须是 end/before/after/after_fields";
                    return true;
                }
                SyntaxNode newClassNode = classNode.WithMembers(classNode.Members.Insert(insertIndex, newMember));
                SyntaxNode root = foundTree.GetRoot();
                SyntaxNode newRoot = root.ReplaceNode(classNode, newClassNode);
                SyntaxTree newTree = CreateTreeFromRoot(foundTree, newRoot);
                // 编译验证——有新增错误回滚
                List<string> newErrors;
                CSharpCompilation trial = (CSharpCompilation)cache.Compilation.ReplaceSyntaxTree(foundTree, newTree);
                if (!ValidateNoNewErrors(cache.Compilation, trial, out newErrors))
                {
                    StringBuilder sb = new StringBuilder();
                    sb.Append("ROLLED_BACK|新增编译错误 " + newErrors.Count + " 条——未落盘:");
                    for (int i = 0; i < newErrors.Count; i = i + 1)
                    {
                        sb.Append(Environment.NewLine + "  " + newErrors[i]);
                    }
                    result = TrimResult(sb.ToString(), MaxResultChars);
                    return true;
                }
                string filePath = foundTree.FilePath;
                WriteAtomicText(filePath, newRoot.ToFullString());
                cache.Trees[filePath] = newTree;
                cache.Compilation = trial;
                cache.Stamps[filePath] = SnapshotOf(filePath);
                SemanticModel removed = null!;
                cache.Semantics.TryRemove(filePath, out removed);
                result = "OK 已插入成员到 " + className + "（" + RelativeToProject(cache, filePath) + "）: " + newMember.GetType().Name;
                return true;
            }
        }

        /// <summary>
        /// member delete——删除成员（含注释）；编译验证回滚保护
        /// </summary>
        /// <param name="args">参数</param>
        /// <param name="result">结果</param>
        /// <returns>调用完成</returns>
        private bool MemberDelete(JsonElement args, out string result)
        {
            string path = Arg(args, "path");
            string className = Arg(args, "class");
            string member = Arg(args, "member");
            if (className.Length == 0 || member.Length == 0)
            {
                result = "ERR|BAD_ARGS|缺少参数 class/member";
                return false;
            }
            string csproj = ResolveProject(path);
            if (csproj.Length == 0)
            {
                result = "ERR|BAD_PATH|项目路径无效或越界: " + path;
                return false;
            }
            ProjectCache cache = EnsureProject(csproj);
            FullScan(cache);
            lock (cache.Gate)
            {
                SyntaxTree foundTree;
                ClassDeclarationSyntax classNode;
                int classTotal;
                if (!FindClassNode(cache, className, out foundTree, out classNode, out classTotal))
                {
                    result = "ERR|CLASS_NOT_FOUND|类不存在: " + className;
                    return true;
                }
                SyntaxNode memberNode;
                string memberKind;
                int memberCount;
                if (!FindMemberInClass(classNode, member, out memberNode, out memberKind, out memberCount))
                {
                    if (memberCount > 1)
                    {
                        result = "ERR|AMBIGUOUS|成员歧义——同名 " + memberCount + " 处（重载？），delete 不支持歧义，请先处理";
                        return true;
                    }
                    result = "ERR|MEMBER_NOT_FOUND|成员不存在: " + className + "." + member;
                    return true;
                }
                SyntaxNode root = foundTree.GetRoot();
                SyntaxNode newRoot = root.RemoveNode(memberNode, SyntaxRemoveOptions.KeepNoTrivia);
                if (newRoot == null)
                {
                    result = "ERR|REMOVE_FAIL|节点删除失败（结构异常）";
                    return true;
                }
                SyntaxTree newTree = CreateTreeFromRoot(foundTree, newRoot);
                CSharpCompilation trial = (CSharpCompilation)cache.Compilation.ReplaceSyntaxTree(foundTree, newTree);
                List<string> newErrors;
                if (!ValidateNoNewErrors(cache.Compilation, trial, out newErrors))
                {
                    StringBuilder sb = new StringBuilder();
                    sb.Append("ROLLED_BACK|删除引入 " + newErrors.Count + " 处新增错误——未落盘（成员仍被引用？）:");
                    for (int i = 0; i < newErrors.Count; i = i + 1)
                    {
                        sb.Append(Environment.NewLine + "  " + newErrors[i]);
                    }
                    result = TrimResult(sb.ToString(), MaxResultChars);
                    return true;
                }
                string filePath = foundTree.FilePath;
                WriteAtomicText(filePath, newRoot.ToFullString());
                cache.Trees[filePath] = newTree;
                cache.Compilation = trial;
                cache.Stamps[filePath] = SnapshotOf(filePath);
                SemanticModel removed = null!;
                cache.Semantics.TryRemove(filePath, out removed);
                result = "OK 已删除 " + className + "." + member + "（" + RelativeToProject(cache, filePath) + "）";
                return true;
            }
        }

        /// <summary>
        /// member rename——全项目引用同步（语义验证每处替换；编译验证回滚保护；多文件原子落盘）
        /// </summary>
        /// <param name="args">参数</param>
        /// <param name="result">结果</param>
        /// <returns>调用完成</returns>
        private bool MemberRename(JsonElement args, out string result)
        {
            string path = Arg(args, "path");
            string className = Arg(args, "class");
            string oldName = Arg(args, "oldName");
            string newName = Arg(args, "newName");
            if (className.Length == 0 || oldName.Length == 0 || newName.Length == 0)
            {
                result = "ERR|BAD_ARGS|缺少参数 class/oldName/newName";
                return false;
            }
            string csproj = ResolveProject(path);
            if (csproj.Length == 0)
            {
                result = "ERR|BAD_PATH|项目路径无效或越界: " + path;
                return false;
            }
            ProjectCache cache = EnsureProject(csproj);
            FullScan(cache);
            lock (cache.Gate)
            {
                SyntaxTree foundTree;
                ClassDeclarationSyntax classNode;
                int classTotal;
                if (!FindClassNode(cache, className, out foundTree, out classNode, out classTotal))
                {
                    result = "ERR|CLASS_NOT_FOUND|类不存在: " + className;
                    return true;
                }
                SemanticModel classModel = EnsureSemantics(cache, foundTree.FilePath);
                INamedTypeSymbol? classSymbol = classModel.GetDeclaredSymbol(classNode) as INamedTypeSymbol;
                if (classSymbol == null)
                {
                    result = "ERR|SYMBOL_BIND|类符号解析失败: " + className;
                    return true;
                }
                System.Collections.Immutable.ImmutableArray<ISymbol> targets = classSymbol.GetMembers(oldName);
                if (targets.Length == 0)
                {
                    result = "ERR|SYMBOL_NOT_FOUND|成员不存在: " + className + "." + oldName;
                    return true;
                }
                HashSet<ISymbol> targetSet = new HashSet<ISymbol>(targets, SymbolEqualityComparer.Default);
                List<string> changedFiles = new List<string>();
                List<SyntaxTree> changedTrees = new List<SyntaxTree>();
                List<SyntaxTree> changedOldTrees = new List<SyntaxTree>();
                foreach (KeyValuePair<string, SyntaxTree> pair in cache.Trees)
                {
                    SemanticModel model = EnsureSemantics(cache, pair.Key);
                    SymbolRenamingRewriter rewriter = new SymbolRenamingRewriter(model, targetSet, oldName, newName);
                    SyntaxNode newRoot = rewriter.Visit(pair.Value.GetRoot());
                    if (rewriter.Changed && newRoot != null)
                    {
                        changedFiles.Add(pair.Key);
                        changedOldTrees.Add(pair.Value);
                        changedTrees.Add(CreateTreeFromRoot(pair.Value, newRoot));
                    }
                }
                if (changedTrees.Count == 0)
                {
                    result = "OK 无引用可改（仅声明处已同步？）——检查后重试";
                    return true;
                }
                CSharpCompilation trial = cache.Compilation;
                for (int i = 0; i < changedTrees.Count; i = i + 1)
                {
                    trial = (CSharpCompilation)trial.ReplaceSyntaxTree(changedOldTrees[i], changedTrees[i]);
                }
                List<string> newErrors;
                if (!ValidateNoNewErrors(cache.Compilation, trial, out newErrors))
                {
                    StringBuilder sb = new StringBuilder();
                    sb.Append("ROLLED_BACK|重命名引入 " + newErrors.Count + " 处新增错误——全部未落盘:");
                    for (int i = 0; i < newErrors.Count; i = i + 1)
                    {
                        sb.Append(Environment.NewLine + "  " + newErrors[i]);
                    }
                    result = TrimResult(sb.ToString(), MaxResultChars);
                    return true;
                }
                for (int i = 0; i < changedFiles.Count; i = i + 1)
                {
                    string filePath = changedFiles[i];
                    SyntaxTree newTree = changedTrees[i];
                    WriteAtomicText(filePath, newTree.GetRoot().ToFullString());
                    cache.Trees[filePath] = newTree;
                    cache.Stamps[filePath] = SnapshotOf(filePath);
                    SemanticModel removed = null!;
                    cache.Semantics.TryRemove(filePath, out removed);
                }
                cache.Compilation = trial;
                result = "OK 已重命名 " + className + "." + oldName + " → " + newName + "（" + changedFiles.Count + " 文件）";
                return true;
            }
        }

        /// <summary>
        /// cs.comment——XML 注释增改（summary/param/returns；member 空=类）
        /// </summary>
        /// <param name="args">参数</param>
        /// <param name="result">结果</param>
        /// <returns>调用完成</returns>
        private bool ToolComment(JsonElement args, out string result)
        {
            string path = Arg(args, "path");
            string className = Arg(args, "class");
            string member = Arg(args, "member");
            string type = Arg(args, "type");
            string text = Arg(args, "text");
            string paramName = Arg(args, "param");
            if (className.Length == 0 || type.Length == 0 || text.Length == 0)
            {
                result = "ERR|BAD_ARGS|缺少参数 class/type/text";
                return false;
            }
            if (type != "summary" && type != "param" && type != "returns")
            {
                result = "ERR|BAD_ARGS|type 必须是 summary/param/returns";
                return false;
            }
            if (type == "param" && paramName.Length == 0)
            {
                result = "ERR|BAD_ARGS|type=param 需要 param 参数名";
                return false;
            }
            string csproj = ResolveProject(path);
            if (csproj.Length == 0)
            {
                result = "ERR|BAD_PATH|项目路径无效或越界: " + path;
                return false;
            }
            ProjectCache cache = EnsureProject(csproj);
            FullScan(cache);
            lock (cache.Gate)
            {
                SyntaxTree foundTree;
                ClassDeclarationSyntax classNode;
                int classTotal;
                if (!FindClassNode(cache, className, out foundTree, out classNode, out classTotal))
                {
                    result = "ERR|CLASS_NOT_FOUND|类不存在: " + className;
                    return true;
                }
                SyntaxNode target = classNode;
                if (member.Length > 0)
                {
                    SyntaxNode memberNode;
                    string memberKind;
                    int memberCount;
                    if (!FindMemberInClass(classNode, member, out memberNode, out memberKind, out memberCount))
                    {
                        if (memberCount > 1)
                        {
                            result = "ERR|AMBIGUOUS|成员歧义——同名 " + memberCount + " 处（重载？）";
                            return true;
                        }
                        result = "ERR|MEMBER_NOT_FOUND|成员不存在: " + className + "." + member;
                        return true;
                    }
                    target = memberNode;
                }
                XElement rootXml;
                try
                {
                    rootXml = ParseDocComments(target);
                }
                catch (Exception ex)
                {
                    result = "ERR|DOC_PARSE|doc 注释解析失败: " + ex.Message;
                    return true;
                }
                if (type == "summary")
                {
                    XElement? summary = rootXml.Element("summary");
                    if (summary == null)
                    {
                        summary = new XElement("summary");
                        rootXml.AddFirst(summary);
                    }
                    summary.Value = text;
                }
                else if (type == "param")
                {
                    XElement? param = null;
                    foreach (XElement element in rootXml.Elements("param"))
                    {
                        string? nameAttr = element.Attribute("name")?.Value;
                        if (nameAttr == paramName)
                        {
                            param = element;
                            break;
                        }
                    }
                    if (param == null)
                    {
                        param = new XElement("param", new XAttribute("name", paramName));
                        rootXml.Add(param);
                    }
                    param.Value = text;
                }
                else
                {
                    XElement? returns = rootXml.Element("returns");
                    if (returns == null)
                    {
                        returns = new XElement("returns");
                        rootXml.Add(returns);
                    }
                    returns.Value = text;
                }
                StringBuilder docText = new StringBuilder();
                // 换行风格——跟随目标文件（\r\n 或 \n——防止注释与声明同行语法崩坏 CS1022）
                string newline = "\r\n";
                string treeText = foundTree.GetText().ToString();
                int newlineIndex = treeText.IndexOf('\n');
                if (newlineIndex > 0 && treeText[newlineIndex - 1] != '\r')
                {
                    newline = "\n";
                }
                foreach (XElement element in rootXml.Elements())
                {
                    docText.Append("/// " + element.ToString(SaveOptions.DisableFormatting) + newline);
                }
                SyntaxTriviaList newDocTrivia = SyntaxFactory.ParseLeadingTrivia(docText.ToString());
                // 重建 leading——原 doc trivia 替换为新 doc（其余 trivia 原样保留）
                SyntaxTriviaList oldTrivia = target.GetLeadingTrivia();
                List<SyntaxTrivia> rebuilt = new List<SyntaxTrivia>();
                bool replaced = false;
                for (int i = 0; i < oldTrivia.Count; i = i + 1)
                {
                    SyntaxTrivia trivia = oldTrivia[i];
                    bool isDoc = trivia.IsKind(SyntaxKind.SingleLineDocumentationCommentTrivia) ||
                                 trivia.IsKind(SyntaxKind.MultiLineDocumentationCommentTrivia);
                    if (isDoc)
                    {
                        if (!replaced)
                        {
                            replaced = true;
                            for (int j = 0; j < newDocTrivia.Count; j = j + 1)
                            {
                                rebuilt.Add(newDocTrivia[j]);
                            }
                        }
                    }
                    else
                    {
                        rebuilt.Add(trivia);
                    }
                }
                if (!replaced)
                {
                    // 原无 doc 注释——追加首行
                    for (int j = 0; j < newDocTrivia.Count; j = j + 1)
                    {
                        rebuilt.Add(newDocTrivia[j]);
                    }
                }
                SyntaxNode newNode = target.WithLeadingTrivia(rebuilt);
                SyntaxNode root = foundTree.GetRoot();
                SyntaxNode newRoot = root.ReplaceNode(target, newNode);
                SyntaxTree newTree = CreateTreeFromRoot(foundTree, newRoot);
                string filePath = foundTree.FilePath;
                WriteAtomicText(filePath, newRoot.ToFullString());
                cache.Trees[filePath] = newTree;
                cache.Compilation = (CSharpCompilation)cache.Compilation.ReplaceSyntaxTree(foundTree, newTree);
                cache.Stamps[filePath] = SnapshotOf(filePath);
                SemanticModel removed = null!;
                cache.Semantics.TryRemove(filePath, out removed);
                result = "OK 注释已更新 " + className + (member.Length > 0 ? "." + member : "") + " <" + type + ">";
                return true;
            }
        }

        /// <summary>
        /// 解析节点 doc 注释为 XML 根元素（<root> 包裹）
        /// </summary>
        /// <param name="node">声明节点</param>
        /// <returns>根元素（含 summary/param/returns 子元素）</returns>
        private static XElement ParseDocComments(SyntaxNode node)
        {
            StringBuilder raw = new StringBuilder();
            foreach (SyntaxTrivia trivia in node.GetLeadingTrivia())
            {
                if (trivia.IsKind(SyntaxKind.SingleLineDocumentationCommentTrivia) ||
                    trivia.IsKind(SyntaxKind.MultiLineDocumentationCommentTrivia))
                {
                    raw.Append(trivia.ToString());
                }
            }
            XElement rootElement;
            if (raw.Length == 0)
            {
                rootElement = new XElement("root");
                return rootElement;
            }
            string xmlText = raw.ToString();
            int firstLt = xmlText.IndexOf('<');
            if (firstLt < 0)
            {
                rootElement = new XElement("root");
                return rootElement;
            }
            xmlText = xmlText.Substring(firstLt);
            xmlText = xmlText.Replace("///", "").Replace("/*", "").Replace("*/", "");
            XDocument doc = XDocument.Parse("<root>" + xmlText + "</root>", LoadOptions.None);
            rootElement = doc.Root!;
            return rootElement;
        }

        /// <summary>
        /// cs.dead——零引用成员（private/internal；public/override/构造跳过）
        /// </summary>
        /// <param name="args">参数</param>
        /// <param name="result">结果</param>
        /// <returns>调用完成</returns>
        private bool ToolDead(JsonElement args, out string result)
        {
            string path = Arg(args, "path");
            string csproj = ResolveProject(path);
            if (csproj.Length == 0)
            {
                result = "ERR|BAD_PATH|项目路径无效或越界: " + path;
                return false;
            }
            ProjectCache cache = EnsureProject(csproj);
            FullScan(cache);
            HashSet<ISymbol> targets = new HashSet<ISymbol>(SymbolEqualityComparer.Default);
            foreach (KeyValuePair<string, SyntaxTree> pair in cache.Trees)
            {
                SemanticModel model = EnsureSemantics(cache, pair.Key);
                SyntaxNode root = pair.Value.GetRoot();
                foreach (SyntaxNode node in root.DescendantNodes())
                {
                    TypeDeclarationSyntax? typeDecl = node as TypeDeclarationSyntax;
                    if (typeDecl == null)
                    {
                        continue;
                    }
                    INamedTypeSymbol? typeSymbol = model.GetDeclaredSymbol(typeDecl) as INamedTypeSymbol;
                    if (typeSymbol == null)
                    {
                        continue;
                    }
                    System.Collections.Immutable.ImmutableArray<ISymbol> members = typeSymbol.GetMembers();
                    for (int i = 0; i < members.Length; i = i + 1)
                    {
                        ISymbol member = members[i];
                        if (member.IsImplicitlyDeclared)
                        {
                            continue;
                        }
                        Accessibility access = member.DeclaredAccessibility;
                        if (access != Accessibility.Private && access != Accessibility.Internal)
                        {
                            continue;
                        }
                        IMethodSymbol? method = member as IMethodSymbol;
                        if (method != null)
                        {
                            if (method.MethodKind != MethodKind.Ordinary)
                            {
                                continue;
                            }
                            if (method.IsOverride)
                            {
                                continue;
                            }
                        }
                        IPropertySymbol? property = member as IPropertySymbol;
                        if (property != null && property.IsOverride)
                        {
                            continue;
                        }
                        IFieldSymbol? field = member as IFieldSymbol;
                        if (field != null && field.IsImplicitlyDeclared)
                        {
                            continue;
                        }
                        targets.Add(member);
                    }
                }
            }
            HashSet<ISymbol> referenced = new HashSet<ISymbol>(SymbolEqualityComparer.Default);
            foreach (KeyValuePair<string, SyntaxTree> pair in cache.Trees)
            {
                SemanticModel model = EnsureSemantics(cache, pair.Key);
                SyntaxNode root = pair.Value.GetRoot();
                foreach (SyntaxNode node in root.DescendantNodes())
                {
                    SyntaxNode? namedNode = null;
                    IdentifierNameSyntax? identifier = node as IdentifierNameSyntax;
                    if (identifier != null)
                    {
                        namedNode = identifier;
                    }
                    else
                    {
                        GenericNameSyntax? generic = node as GenericNameSyntax;
                        if (generic != null)
                        {
                            namedNode = generic;
                        }
                    }
                    if (namedNode == null)
                    {
                        continue;
                    }
                    SymbolInfo info = model.GetSymbolInfo(namedNode);
                    if (info.Symbol != null)
                    {
                        referenced.Add(info.Symbol);
                    }
                    for (int i = 0; i < info.CandidateSymbols.Length; i = i + 1)
                    {
                        referenced.Add(info.CandidateSymbols[i]);
                    }
                }
            }
            List<string> deadLines = new List<string>();
            foreach (ISymbol target in targets)
            {
                if (referenced.Contains(target))
                {
                    continue;
                }
                string typeName = target.ContainingType != null ? target.ContainingType.Name : "?";
                string lineText = "";
                if (target.DeclaringSyntaxReferences.Length > 0)
                {
                    FileLinePositionSpan span = target.DeclaringSyntaxReferences[0].GetSyntax().GetLocation().GetLineSpan();
                    lineText = span.Path + ":" + (span.StartLinePosition.Line + 1);
                }
                deadLines.Add(typeName + "." + target.Name + " // " + lineText);
            }
            deadLines.Sort(StringComparer.Ordinal);
            StringBuilder sb = new StringBuilder();
            if (deadLines.Count == 0)
            {
                sb.Append("OK 无零引用成员（private/internal " + targets.Count + " 个全部被引用）");
            }
            else
            {
                sb.Append("零引用成员 " + deadLines.Count + " 个（private/internal；public/override 跳过）");
                for (int i = 0; i < deadLines.Count; i = i + 1)
                {
                    sb.Append(Environment.NewLine + "  " + deadLines[i]);
                }
            }
            result = TrimResult(sb.ToString(), MaxResultChars);
            return true;
        }

        /// <summary>
        /// 成员声明名提取（anchor 匹配用）
        /// </summary>
        /// <param name="member">成员声明</param>
        /// <returns>成员名（无则空串）</returns>
        private static string MemberNameOf(MemberDeclarationSyntax member)
        {
            MethodDeclarationSyntax? method = member as MethodDeclarationSyntax;
            if (method != null)
            {
                return method.Identifier.Text;
            }
            PropertyDeclarationSyntax? property = member as PropertyDeclarationSyntax;
            if (property != null)
            {
                return property.Identifier.Text;
            }
            FieldDeclarationSyntax? field = member as FieldDeclarationSyntax;
            if (field != null && field.Declaration.Variables.Count > 0)
            {
                return field.Declaration.Variables[0].Identifier.Text;
            }
            ConstructorDeclarationSyntax? ctor = member as ConstructorDeclarationSyntax;
            if (ctor != null)
            {
                return ctor.Identifier.Text;
            }
            return "";
        }
    }

    /// <summary>
    /// 重命名重写器——语义验证的引用/声明替换（仅绑定符号 == 目标集才改；局部变量/同名异符零误伤）
    /// </summary>
    internal sealed class SymbolRenamingRewriter : CSharpSyntaxRewriter
    {
        /// <summary>
        /// 语义模型（当前文件）
        /// </summary>
        private readonly SemanticModel _model;

        /// <summary>
        /// 目标符号集（重载全匹配）
        /// </summary>
        private readonly HashSet<ISymbol> _targets;

        /// <summary>
        /// 旧名（快速过滤）
        /// </summary>
        private readonly string _oldName;

        /// <summary>
        /// 新名
        /// </summary>
        private readonly string _newName;

        /// <summary>
        /// 是否有变更
        /// </summary>
        public bool Changed;

        /// <summary>
        /// 创建重写器
        /// </summary>
        /// <param name="model">语义模型</param>
        /// <param name="targets">目标符号集</param>
        /// <param name="oldName">旧名</param>
        /// <param name="newName">新名</param>
        public SymbolRenamingRewriter(SemanticModel model, HashSet<ISymbol> targets, string oldName, string newName)
        {
            _model = model;
            _targets = targets;
            _oldName = oldName;
            _newName = newName;
            Changed = false;
        }

        /// <summary>
        /// 引用处——IdentifierName / GenericName（语义绑定验证——泛型调用 Bind&lt;T&gt; 也覆盖）
        /// </summary>
        public override SyntaxNode? VisitIdentifierName(IdentifierNameSyntax node)
        {
            return RewriteIfTarget(node, node.Identifier);
        }

        /// <summary>
        /// 泛型调用名——如 DataBox.Bind&lt;IOA&gt; 的 Bind
        /// </summary>
        public override SyntaxNode? VisitGenericName(GenericNameSyntax node)
        {
            if (node.Identifier.Text == _oldName)
            {
                SymbolInfo info = _model.GetSymbolInfo(node);
                if (info.Symbol != null && _targets.Contains(info.Symbol))
                {
                    Changed = true;
                    return node.WithIdentifier(SyntaxFactory.Identifier(_newName).WithTriviaFrom(node.Identifier));
                }
                for (int i = 0; i < info.CandidateSymbols.Length; i = i + 1)
                {
                    if (_targets.Contains(info.CandidateSymbols[i]))
                    {
                        Changed = true;
                        return node.WithIdentifier(SyntaxFactory.Identifier(_newName).WithTriviaFrom(node.Identifier));
                    }
                }
            }
            return base.VisitGenericName(node);
        }

        /// <summary>
        /// 目标符号替换判断——IdentifierName 共用逻辑
        /// </summary>
        /// <param name="node">节点</param>
        /// <param name="token">标识符</param>
        /// <returns>替换后节点</returns>
        private SyntaxNode? RewriteIfTarget(IdentifierNameSyntax node, SyntaxToken token)
        {
            if (token.Text == _oldName)
            {
                SymbolInfo info = _model.GetSymbolInfo(node);
                if (info.Symbol != null && _targets.Contains(info.Symbol))
                {
                    Changed = true;
                    return node.WithIdentifier(SyntaxFactory.Identifier(_newName).WithTriviaFrom(node.Identifier));
                }
                for (int i = 0; i < info.CandidateSymbols.Length; i = i + 1)
                {
                    if (_targets.Contains(info.CandidateSymbols[i]))
                    {
                        Changed = true;
                        return node.WithIdentifier(SyntaxFactory.Identifier(_newName).WithTriviaFrom(node.Identifier));
                    }
                }
            }
            return base.VisitIdentifierName(node);
        }

        /// <summary>
        /// 方法声明处
        /// </summary>
        public override SyntaxNode? VisitMethodDeclaration(MethodDeclarationSyntax node)
        {
            if (node.Identifier.Text == _oldName)
            {
                ISymbol? symbol = _model.GetDeclaredSymbol(node);
                if (symbol != null && _targets.Contains(symbol))
                {
                    Changed = true;
                    node = node.WithIdentifier(SyntaxFactory.Identifier(_newName).WithTriviaFrom(node.Identifier));
                }
            }
            return base.VisitMethodDeclaration(node);
        }

        /// <summary>
        /// 属性声明处
        /// </summary>
        public override SyntaxNode? VisitPropertyDeclaration(PropertyDeclarationSyntax node)
        {
            if (node.Identifier.Text == _oldName)
            {
                ISymbol? symbol = _model.GetDeclaredSymbol(node);
                if (symbol != null && _targets.Contains(symbol))
                {
                    Changed = true;
                    node = node.WithIdentifier(SyntaxFactory.Identifier(_newName).WithTriviaFrom(node.Identifier));
                }
            }
            return base.VisitPropertyDeclaration(node);
        }

        /// <summary>
        /// 变量声明处——字段（局部变量符号 ≠ 目标——自动安全）
        /// </summary>
        public override SyntaxNode? VisitVariableDeclarator(VariableDeclaratorSyntax node)
        {
            if (node.Identifier.Text == _oldName)
            {
                ISymbol? symbol = _model.GetDeclaredSymbol(node);
                if (symbol != null && _targets.Contains(symbol))
                {
                    Changed = true;
                    node = node.WithIdentifier(SyntaxFactory.Identifier(_newName).WithTriviaFrom(node.Identifier));
                }
            }
            return base.VisitVariableDeclarator(node);
        }
    }
}