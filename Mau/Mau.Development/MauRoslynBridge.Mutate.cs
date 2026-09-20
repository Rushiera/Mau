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
            string resolveError;
            List<string> projects = ResolveProjects(path, out resolveError);
            if (projects.Count == 0)
            {
                result = "ERR|BAD_PATH|" + resolveError;
                return false;
            }
            // 定义所在项目定位——多项目入口（.sln / 目录）下逐项目查找类声明，命中即为 ownerCache
            List<ProjectCache> caches = new List<ProjectCache>();
            ProjectCache ownerCache = null;
            SyntaxTree foundTree = null;
            ClassDeclarationSyntax classNode = null;
            for (int i = 0; i < projects.Count; i = i + 1)
            {
                ProjectCache cache = EnsureProject(projects[i]);
                FullScan(cache);
                caches.Add(cache);
                SyntaxTree tree;
                ClassDeclarationSyntax node;
                int total;
                if (ownerCache == null && FindClassNode(cache, className, out tree, out node, out total))
                {
                    ownerCache = cache;
                    foundTree = tree;
                    classNode = node;
                }
            }
            if (ownerCache == null)
            {
                result = "ERR|CLASS_NOT_FOUND|类不存在: " + className;
                return false;
            }
            SemanticModel classModel = EnsureSemantics(ownerCache, foundTree!.FilePath);
            INamedTypeSymbol? classSymbol = classModel.GetDeclaredSymbol(classNode!) as INamedTypeSymbol;
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
            // 跨程序集匹配面——兄弟项目里该成员解析为元数据符号（对方 dll），与源码符号不可用符号相等比较，
            // 按引用键比对（判例 2026-09-16：入口壳对域成员的调用被漏报）
            HashSet<string> targetKeys = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < targets.Length; i = i + 1)
            {
                targetKeys.Add(RefKey(targets[i]));
            }
            List<string> hits = new List<string>();
            for (int c = 0; c < caches.Count; c = c + 1)
            {
                ProjectCache cache = caches[c];
                bool sameProject = cache == ownerCache;
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
                        if (!matched && info.Symbol != null)
                        {
                            matched = targetKeys.Contains(RefKey(info.Symbol));
                        }
                        if (!matched)
                        {
                            for (int i = 0; i < info.CandidateSymbols.Length; i = i + 1)
                            {
                                if (targetKeys.Contains(RefKey(info.CandidateSymbols[i])))
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
                            string mark = sameProject ? "" : "[跨程序集] ";
                            hits.Add(mark + RelativeToProject(cache, pair.Key) + ":" + (span.StartLinePosition.Line + 1) + ":" + (span.StartLinePosition.Character + 1) + ": " + lineText);
                        }
                    }
                }
            }
            // 结构化返回（2026-09-18）：首行 JSON 元数据头 + 正文命中行（`[跨程序集] 文件:行:列: 代码`）
            Dictionary<string, object> refMeta = new Dictionary<string, object>();
            refMeta["class"] = className;
            refMeta["member"] = member;
            refMeta["hits"] = hits.Count;
            refMeta["projects"] = projects.Count;
            StringBuilder sb = new StringBuilder();
            sb.Append(MetaHead("cs-find_ref", true, refMeta));
            for (int i = 0; i < hits.Count; i = i + 1)
            {
                sb.Append(Environment.NewLine + hits[i]);
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
                // 行号锚——新节点由 WithBody 生成、未挂载语法树，其 Span 从自身起点起算（行号恒为 1）
                SyntaxAnnotation patchMark = new SyntaxAnnotation();
                MethodDeclarationSyntax newMethod = (MethodDeclarationSyntax)EnsureMemberTrailingNewLine(methodNode.WithBody(finalBlock), patchNewline).WithAdditionalAnnotations(patchMark);
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
                string writeNote = WriteAtomicText(filePath, newRoot.ToFullString());
                cache.Trees[filePath] = newTree;
                cache.Compilation = newCompilation;
                cache.Stamps[filePath] = SnapshotOf(filePath);
                SemanticModel removed = null!;
                cache.Semantics.TryRemove(filePath, out removed);
                // 结构化返回（2026-09-18）：JSON 元数据头 + 正文方法段源码（H28 校验链；行尾行号给 LLM）
                // 行号基准 = 完整文件树上的 attached 节点——替换生成的新节点未挂载，位置从自身起点起算
                SyntaxNode patchedNode;
                if (!TryResolveAnnotated(newTree, patchMark, out patchedNode))
                {
                    result = "ERR|LINE_BASE|替换后节点定位失败——修改已落盘但返回行号不可信: " + className + "." + methodName;
                    return true;
                }
                int methodStartLine = newTree.GetText().Lines.GetLineFromPosition(patchedNode.FullSpan.Start).LineNumber + 1;
                int methodEndLine = patchedNode.GetLocation().GetLineSpan().EndLinePosition.Line + 1;
                Dictionary<string, object> ptMeta = new Dictionary<string, object>();
                ptMeta["state"] = "OK";
                ptMeta["file"] = RelativeToProject(cache, filePath);
                ptMeta["class"] = className;
                ptMeta["method"] = methodName;
                ptMeta["start"] = methodStartLine;
                ptMeta["end"] = methodEndLine;
                if (writeNote.Length > 0)
                {
                    ptMeta["writeNote"] = writeNote;
                }
                result = TrimResult(MetaHead("cs-patch", true, ptMeta) + Environment.NewLine + NumberedSource(patchedNode.ToFullString(), methodStartLine), MaxResultChars);
                return true;
            }
        }
        /// <summary>
        /// 取回替换后节点的 attached 实例——WithXxx / 片段节点派生的新节点未挂载语法树，
        /// 其 Span 从自身起点起算（行号归零或落到片段相对行）；替换前打 SyntaxAnnotation，
        /// 替换后在完整树上取回同节点，位置才是文件坐标。
        /// </summary>
        /// <param name="tree">替换后的完整语法树</param>
        /// <param name="mark">替换前打上的注解</param>
        /// <param name="node">取回的节点（失败为 null!）</param>
        /// <returns>取回成功</returns>
        private static bool TryResolveAnnotated(SyntaxTree tree, SyntaxAnnotation mark, out SyntaxNode node)
        {
            foreach (SyntaxNode candidate in tree.GetRoot().GetAnnotatedNodes(mark))
            {
                node = candidate;
                return true;
            }
            node = null!;
            return false;
        }

        /// <summary>
        /// 引用键——类型全名.成员名(参数类型序列)。跨编译匹配用：跨项目引用在本项目语义模型里解析为
        /// 元数据符号（对方 dll），与源码符号不共享 SymbolEqualityComparer，只能按字符串键比对。
        /// </summary>
        /// <param name="symbol">符号（源码或元数据）</param>
        /// <returns>引用键（跨编译稳定）</returns>
        private static string RefKey(ISymbol symbol)
        {
            ISymbol def = symbol.OriginalDefinition;
            INamedTypeSymbol? type = def.ContainingType;
            string typeName = type != null ? type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) : "?";
            IMethodSymbol? method = def as IMethodSymbol;
            if (method != null)
            {
                StringBuilder sb = new StringBuilder();
                sb.Append(typeName);
                sb.Append(".");
                sb.Append(def.Name);
                sb.Append("(");
                for (int i = 0; i < method.Parameters.Length; i = i + 1)
                {
                    if (i > 0)
                    {
                        sb.Append(",");
                    }
                    sb.Append(method.Parameters[i].Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
                }
                sb.Append(")");
                return sb.ToString();
            }
            return typeName + "." + def.Name;
        }
        /// <summary>
        /// 收集引用键集——遍历给定项目集的全部语法树，取每个标识符解析出的符号键（含候选符号）。
        /// 用于跨程序集复核：单项目语义只见本项目源码符号，兄弟项目对它的引用解析为元数据符号，
        /// 只能按引用键匹配（判例 2026-09-16：跨项目被引用成员被误报为死代码）。
        /// </summary>
        /// <param name="caches">项目缓存集（跨程序集复核范围）</param>
        /// <returns>引用键集合（RefKey 口径）</returns>
        private HashSet<string> CollectReferenceKeys(List<ProjectCache> caches)
        {
            HashSet<string> keys = new HashSet<string>(StringComparer.Ordinal);
            for (int c = 0; c < caches.Count; c = c + 1)
            {
                ProjectCache cache = caches[c];
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
                            keys.Add(RefKey(info.Symbol));
                        }
                        for (int i = 0; i < info.CandidateSymbols.Length; i = i + 1)
                        {
                            keys.Add(RefKey(info.CandidateSymbols[i]));
                        }
                    }
                }
            }
            return keys;
        }
        /// <summary>
        /// 单缓存零引用扫描——DeadSingle 主体（多项目聚合时携跨程序集引用键集，见 CollectReferenceKeys）。
        /// </summary>
        /// <param name="cache">项目缓存</param>
        /// <param name="crossKeys">跨程序集引用键集（null=仅项目内判定）</param>
        /// <returns>报告文本</returns>
        private string DeadInCache(ProjectCache cache, HashSet<string>? crossKeys)
        {
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
                        // OriginalDefinition 归一——泛型方法调用 GetSymbolInfo 返回构造符号，与声明原定义比较须归一到原定义（R2-P2-01 泛型漏匹配）
                        referenced.Add(info.Symbol.OriginalDefinition);
                    }
                    for (int i = 0; i < info.CandidateSymbols.Length; i = i + 1)
                    {
                        referenced.Add(info.CandidateSymbols[i].OriginalDefinition);
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
                // 跨程序集复核——兄弟项目引用在本项目语义里是元数据符号，靠引用键判定（否则误报死代码）
                if (crossKeys != null && crossKeys.Contains(RefKey(target)))
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
            // 结构化返回（2026-09-18）：首行 JSON 元数据头 + 正文零引用清单（`类型.成员 // 文件:行`）
            Dictionary<string, object> deadMeta = new Dictionary<string, object>();
            deadMeta["dead"] = deadLines.Count;
            deadMeta["scanned"] = targets.Count;
            StringBuilder sb = new StringBuilder();
            sb.Append(MetaHead("cs-dead", deadLines.Count == 0, deadMeta));
            for (int i = 0; i < deadLines.Count; i = i + 1)
            {
                sb.Append(Environment.NewLine + deadLines[i]);
            }
            return sb.ToString();
        }
    }
}
