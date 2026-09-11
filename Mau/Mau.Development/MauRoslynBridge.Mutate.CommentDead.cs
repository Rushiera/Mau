using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Mau.Development
{
#pragma warning disable CS8600 // 环境 SDK 对 ConcurrentDictionary/Dictionary TryXXX(out TValue) 的 MaybeNullWhen 分析异常误报——out 实参非空变量也报警（最小复现验证）
    /// <summary>
    /// MauRoslynBridge 元数据面分部——cs.comment（XML 注释增改）+ cs.dead（零引用扫描，partial 分部）。
    /// 写操作统一：编译验证 → 无新增错误才落盘；落盘后内存立即 = 磁盘（写后自刷新）。
    /// </summary>
    public sealed partial class MauRoslynBridge
    {
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
                    docText.Append("/// " + SerializeDocElement(element) + newline);
                }
                SyntaxTriviaList newDocTrivia = SyntaxFactory.ParseLeadingTrivia(docText.ToString());
                // 重建 leading——原 doc 块（doc trivia + 紧邻 EOL）整段替换为新 doc（其余 trivia 原样保留）
                SyntaxTriviaList oldTrivia = target.GetLeadingTrivia();
                List<SyntaxTrivia> rebuilt = new List<SyntaxTrivia>();
                bool replaced = false;
                for (int i = 0; i < oldTrivia.Count; i = i + 1)
                {
                    SyntaxTrivia trivia = oldTrivia[i];
                    bool isDoc = trivia.IsKind(SyntaxKind.SingleLineDocumentationCommentTrivia) ||
                                 trivia.IsKind(SyntaxKind.MultiLineDocumentationCommentTrivia);
                    if (isDoc && !replaced)
                    {
                        replaced = true;
                        for (int j = 0; j < newDocTrivia.Count; j = j + 1)
                        {
                            rebuilt.Add(newDocTrivia[j]);
                        }
                        // 吞掉旧 doc 后紧邻的 EOL/doc trivia——新 doc 自带行尾换行（防双换行空行）
                        while (i + 1 < oldTrivia.Count &&
                               (oldTrivia[i + 1].IsKind(SyntaxKind.EndOfLineTrivia) ||
                                oldTrivia[i + 1].IsKind(SyntaxKind.SingleLineDocumentationCommentTrivia) ||
                                oldTrivia[i + 1].IsKind(SyntaxKind.MultiLineDocumentationCommentTrivia)))
                        {
                            i = i + 1;
                        }
                    }
                    else
                    {
                        rebuilt.Add(trivia);
                    }
                }
                if (!replaced)
                {
                    // 原无 doc 注释——doc 追加到尾部 whitespace 之前，成员缩进保留在 doc 后（声明不顶格）
                    SyntaxTrivia indentTrivia = default(SyntaxTrivia);
                    bool hasIndent = false;
                    for (int j = rebuilt.Count - 1; j >= 0; j = j - 1)
                    {
                        if (rebuilt[j].IsKind(SyntaxKind.WhitespaceTrivia))
                        {
                            indentTrivia = rebuilt[j];
                            hasIndent = true;
                            break;
                        }
                    }
                    for (int j = 0; j < newDocTrivia.Count; j = j + 1)
                    {
                        rebuilt.Add(newDocTrivia[j]);
                    }
                    if (hasIndent)
                    {
                        rebuilt.Add(indentTrivia);
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
        /// doc 元素序列化——& 原样（防 &amp; 污染文件）、&lt;/&gt; 转义保 XML 结构、param name 属性转义
        /// </summary>
        /// <param name="element">doc 元素</param>
        /// <returns>doc 行文本（不含 /// 前缀）</returns>
        private static string SerializeDocElement(XElement element)
        {
            string name = element.Name.LocalName;
            string body = element.Value.Replace("<", "&lt;").Replace(">", "&gt;");
            if (name == "param")
            {
                string attr = (element.Attribute("name")?.Value ?? "").Replace("&", "&amp;").Replace("\"", "&quot;").Replace("<", "&lt;").Replace(">", "&gt;");
                return "<param name=\"" + attr + "\">" + body + "</param>";
            }
            return "<" + name + ">" + body + "</" + name + ">";
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
        /// cs-comment_check——缺 summary 注释扫描（类 + 成员；语法层——复用 FirstSummary）
        /// </summary>
        /// <param name="args">参数</param>
        /// <param name="result">结果</param>
        /// <returns>调用完成</returns>
        private bool ToolCommentCheck(JsonElement args, out string result)
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
            List<string> missingLines = new List<string>();
            int checkedCount = 0;
            foreach (KeyValuePair<string, SyntaxTree> pair in cache.Trees)
            {
                SyntaxNode root = pair.Value.GetRoot();
                foreach (SyntaxNode node in root.DescendantNodes())
                {
                    TypeDeclarationSyntax? typeDecl = node as TypeDeclarationSyntax;
                    if (typeDecl == null)
                    {
                        continue;
                    }
                    string typeName = typeDecl.Identifier.Text;
                    int typeLine = typeDecl.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
                    checkedCount = checkedCount + 1;
                    if (FirstSummary(typeDecl).Length == 0)
                    {
                        missingLines.Add(typeName + " // 缺 summary（L" + typeLine.ToString() + "）");
                    }
                    for (int i = 0; i < typeDecl.Members.Count; i = i + 1)
                    {
                        MemberDeclarationSyntax member = typeDecl.Members[i];
                        string memberName = DescribeMember(member);
                        if (memberName.Length == 0)
                        {
                            continue;
                        }
                        int memberLine = member.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
                        checkedCount = checkedCount + 1;
                        if (FirstSummary(member).Length == 0)
                        {
                            missingLines.Add("  " + typeName + "." + memberName + " // 缺 summary（L" + memberLine.ToString() + "）");
                        }
                    }
                }
            }
            missingLines.Sort(StringComparer.Ordinal);
            StringBuilder sb = new StringBuilder();
            if (missingLines.Count == 0)
            {
                sb.Append("OK 全项目 " + checkedCount.ToString() + " 个类型/成员均有 summary 注释");
            }
            else
            {
                sb.Append("缺 summary 注释 " + missingLines.Count.ToString() + " 项（已扫描 " + checkedCount.ToString() + " 个类型/成员）");
                for (int i = 0; i < missingLines.Count; i = i + 1)
                {
                    sb.Append(Environment.NewLine + "  " + missingLines[i]);
                }
            }
            result = TrimResult(sb.ToString(), MaxResultChars);
            return true;
        }

        /// <summary>
        /// 成员描述名——comment_check 缺注释清单行（方法/属性/字段/构造函数；其余空串跳过）
        /// </summary>
        /// <param name="member">成员声明</param>
        /// <returns>描述名</returns>
        private static string DescribeMember(MemberDeclarationSyntax member)
        {
            MethodDeclarationSyntax? method = member as MethodDeclarationSyntax;
            if (method != null)
            {
                return method.Identifier.Text + method.ParameterList.ToString();
            }
            PropertyDeclarationSyntax? property = member as PropertyDeclarationSyntax;
            if (property != null)
            {
                return property.Identifier.Text;
            }
            FieldDeclarationSyntax? field = member as FieldDeclarationSyntax;
            if (field != null)
            {
                return field.Declaration.ToString().Replace("\r\n", " ").Replace("\n", " ");
            }
            ConstructorDeclarationSyntax? ctor = member as ConstructorDeclarationSyntax;
            if (ctor != null)
            {
                return ctor.Identifier.Text + ctor.ParameterList.ToString();
            }
            return "";
        }
    }
}
