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
    /// MauRoslynBridge 查询与补丁面分部——cs.find_ref（全引用）+ cs.patch（方法体级替换，partial 分部）。
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
                // 行缓存——同文件多次取行复用（R2-P3：原每次 ReadAllLines 全文件）
                if (_lineCachePath != filePath)
                {
                    _lineCache = File.ReadAllLines(filePath);
                    _lineCachePath = filePath;
                }
                if (lineZero < 0 || lineZero >= _lineCache.Length)
                {
                    return "";
                }
                string text = _lineCache[lineZero].Trim();
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
                List<ClassPart> parts = FindClassParts(cache, className);
                if (parts.Count == 0)
                {
                    result = "ERR|CLASS_NOT_FOUND|类不存在: " + className;
                    return true;
                }
                ClassPart methodPart;
                MethodDeclarationSyntax methodNode;
                List<string> signatures;
                int methodFind = FindMethodInParts(parts, methodName, out methodPart, out methodNode, out signatures);
                if (methodFind == 1)
                {
                    result = "ERR|METHOD_NOT_FOUND|方法不存在: " + className + "." + methodName + PartialHint(parts.Count);
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
                // 写落点 = 方法所在分部（跨分部聚合：foundTree 必须与命中节点同树）
                SyntaxTree foundTree = methodNode.SyntaxTree;
                if (methodNode.Body == null)
                {
                    result = "ERR|ARROW_BODY|表达式体方法请先展开为块体（语法糖展开铁律）: " + className + "." + methodName;
                    return true;
                }
                // 写侧规整——行尾归一 + 规范化布局 + 基准缩进平移（规范格式是写侧责任，不依赖事后格式重整器）
                string patchNewline = DetectNewLine(foundTree);
                string patchIndent = IndentAt(foundTree, methodNode);
                StatementSyntax parsed;
                try
                {
                    parsed = SyntaxFactory.ParseStatement(NormalizeNewLineText(body, patchNewline));
                }
                catch (Exception)
                {
                    parsed = null!;
                }
                BlockSyntax? block = parsed as BlockSyntax;
                if (block == null)
                {
                    // 容错——允许直接给完整方法声明（自动取方法体），省一轮「BAD_BODY 后重发」的往返
                    MemberDeclarationSyntax? fallback = SyntaxFactory.ParseMemberDeclaration(NormalizeNewLineText(body, patchNewline));
                    BaseMethodDeclarationSyntax? fallbackMethod = fallback as BaseMethodDeclarationSyntax;
                    if (fallbackMethod != null)
                    {
                        block = fallbackMethod.Body;
                    }
                }
                if (block == null)
                {
                    result = "ERR|BAD_BODY|body 必须是块（{ ... }）或完整方法声明（含方法体）";
                    return true;
                }
                BlockSyntax? finalBlock = SyntaxFactory.ParseStatement(FormatNodeText(block, patchIndent, patchNewline)) as BlockSyntax;
                if (finalBlock == null)
                {
                    result = "ERR|BAD_BODY|body 规整后无法重解析（结构异常）";
                    return true;
                }
                // 尾部补换行——后续 token 的缩进前导 trivia 依赖前一 token 以换行结尾（与 member insert 同规）
                MethodDeclarationSyntax newMethod = (MethodDeclarationSyntax)EnsureMemberTrailingNewLine(methodNode.WithBody(finalBlock), patchNewline);
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
                // OK 返回 + 目标方法段源码（H28 强制校验链——patch 后立即可见落盘状态；文件行号坐标系）
                StringBuilder ok = new StringBuilder();
                ok.Append("OK 已落盘 " + RelativeToProject(cache, filePath) + ":" + className + "." + methodName);
                ok.Append(Environment.NewLine + "——目标方法段（校验链）:");
                int methodStartLine = newRoot.GetText().Lines.GetLineFromPosition(newMethod.FullSpan.Start).LineNumber + 1;
                ok.Append(Environment.NewLine + NumberedSource(newMethod.ToFullString(), methodStartLine));
                result = TrimResult(ok.ToString(), MaxResultChars);
                return true;
            }
        }
    }
}
