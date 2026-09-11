using System;
using System.Collections.Generic;
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
    /// MauRoslynBridge 成员变更面分部——cs.member 三改法（insert/delete/rename，partial 分部）。
    /// 写操作统一：编译验证 → 无新增错误才落盘（三态 OK/ROLLED_BACK/ERR）；落盘后内存立即 = 磁盘（写后自刷新）。
    /// </summary>
    public sealed partial class MauRoslynBridge
    {
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
            string code = Arg(args, "code");
            string position = Arg(args, "position");
            string anchor = Arg(args, "anchor");
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
                ClassDeclarationSyntax newClassNode = classNode.WithMembers(classNode.Members.Insert(insertIndex, newMember));
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
                // P3-4：返回落盘行号区间——newClassNode 已在 newRoot 树中，GetLineSpan 解析新文件行号
                FileLinePositionSpan insertedSpan = newClassNode.Members[insertIndex].GetLocation().GetLineSpan();
                string range = "L" + (insertedSpan.StartLinePosition.Line + 1) + "-" + (insertedSpan.EndLinePosition.Line + 1);
                result = "OK 已插入成员到 " + className + "（" + RelativeToProject(cache, filePath) + " " + range + "）: " + newMember.GetType().Name;
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
                List<string> memberCandidates;
                if (!FindMemberInClass(classNode, member, out memberNode, out memberKind, out memberCount, out memberCandidates))
                {
                    if (memberCount > 1)
                    {
                        result = "ERR|AMBIGUOUS|成员歧义——同名 " + memberCount + " 处，候选签名: " + string.Join(" / ", memberCandidates) + "——member 传签名后缀区分（如 " + member + "(int)）";
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
            if (field != null && field.Declaration != null && field.Declaration.Variables.Count > 0)
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
