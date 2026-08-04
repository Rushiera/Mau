using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Mau.Development
{
    /// <summary>
    /// 在一个固定源码根内提供 Roslyn 语法索引、成员读取、引用定位、注释检查和方法体替换。
    /// 项目级编译仍由 mau build / dotnet build 门禁负责，避免伪造不完整 MSBuild 语义。
    /// </summary>
    public sealed class MauRoslynSourceWorkspace
    {
        /// <summary>
        /// 规范源码根
        /// </summary>
        private readonly string MauRoslynSourceWorkspace_Root;

        /// <summary>
        /// 复合读取/写入锁
        /// </summary>
        private readonly object MauRoslynSourceWorkspace_Gate;

        /// <summary>
        /// 当前源码根
        /// </summary>
        public string Root
        {
            get { return MauRoslynSourceWorkspace_Root; }
        }

        /// <summary>
        /// 绑定一个固定源码根
        /// </summary>
        /// <param name="root">源码根</param>
        public MauRoslynSourceWorkspace(string root)
        {
            if (string.IsNullOrWhiteSpace(root))
            {
                throw new ArgumentException("Roslyn workspace root is empty.", "root");
            }
            MauRoslynSourceWorkspace_Root = Path.GetFullPath(root);
            if (!Directory.Exists(MauRoslynSourceWorkspace_Root))
            {
                throw new DirectoryNotFoundException("Roslyn workspace root was not found.");
            }
            MauRoslynSourceWorkspace_Gate = new object();
        }

        /// <summary>
        /// 列出全部类型和直接成员签名
        /// </summary>
        /// <returns>按文件和位置排序的索引行</returns>
        public string[] ListMembers()
        {
            lock (MauRoslynSourceWorkspace_Gate)
            {
                List<string> result = new List<string>();
                string[] files = GetSourceFiles();
                for (int i = 0; i < files.Length; i = i + 1)
                {
                    SyntaxTree tree = ParseFile(files[i]);
                    CompilationUnitSyntax root = tree.GetCompilationUnitRoot();
                    foreach (SyntaxNode node in root.DescendantNodes())
                    {
                        BaseTypeDeclarationSyntax? type =
                            node as BaseTypeDeclarationSyntax;
                        if (type == null)
                        {
                            continue;
                        }
                        result.Add(Relative(files[i]) + " | type | "
                            + type.Identifier.ValueText);
                        TypeDeclarationSyntax? declaration = type as TypeDeclarationSyntax;
                        if (declaration != null)
                        {
                            for (int m = 0; m < declaration.Members.Count; m = m + 1)
                            {
                                result.Add(Relative(files[i]) + " | member | "
                                    + type.Identifier.ValueText + "."
                                    + MemberName(declaration.Members[m]));
                            }
                        }
                    }
                }
                return result.ToArray();
            }
        }

        /// <summary>
        /// 读取指定类型或成员的完整源码
        /// </summary>
        /// <param name="typeName">类型名</param>
        /// <param name="memberName">空串读取类型</param>
        /// <returns>文件位置和源码</returns>
        public string ReadMember(string typeName, string memberName)
        {
            lock (MauRoslynSourceWorkspace_Gate)
            {
                MauSourceLocation location = Find(typeName, memberName);
                FileLinePositionSpan span = location.Node.SyntaxTree.GetLineSpan(
                    location.Node.FullSpan);
                return Relative(location.FilePath) + ":"
                    + (span.StartLinePosition.Line + 1).ToString() + "\n"
                    + location.Node.ToFullString();
            }
        }

        /// <summary>
        /// 收集所有文件的 Roslyn 语法诊断
        /// </summary>
        /// <returns>稳定排序的诊断行</returns>
        public string[] GetSyntaxDiagnostics()
        {
            lock (MauRoslynSourceWorkspace_Gate)
            {
                List<string> result = new List<string>();
                string[] files = GetSourceFiles();
                for (int i = 0; i < files.Length; i = i + 1)
                {
                    SyntaxTree tree = ParseFile(files[i]);
                    foreach (Diagnostic diagnostic in tree.GetDiagnostics())
                    {
                        FileLinePositionSpan span = diagnostic.Location.GetLineSpan();
                        result.Add(Relative(files[i]) + ":"
                            + (span.StartLinePosition.Line + 1).ToString() + ":"
                            + diagnostic.Id + ":" + diagnostic.GetMessage());
                    }
                }
                result.Sort(StringComparer.Ordinal);
                return result.ToArray();
            }
        }

        /// <summary>
        /// 以 Roslyn 标识符 Token 定位精确引用
        /// </summary>
        /// <param name="identifier">标识符</param>
        /// <returns>文件、行号和该行文本</returns>
        public string[] FindIdentifierReferences(string identifier)
        {
            if (string.IsNullOrWhiteSpace(identifier))
            {
                throw new ArgumentException("Identifier is empty.", "identifier");
            }
            lock (MauRoslynSourceWorkspace_Gate)
            {
                List<string> result = new List<string>();
                string[] files = GetSourceFiles();
                for (int i = 0; i < files.Length; i = i + 1)
                {
                    SyntaxTree tree = ParseFile(files[i]);
                    SourceText text = tree.GetText();
                    foreach (SyntaxToken token in tree.GetRoot().DescendantTokens())
                    {
                        if (token.IsKind(SyntaxKind.IdentifierToken)
                            && token.ValueText == identifier)
                        {
                            FileLinePositionSpan span = tree.GetLineSpan(token.Span);
                            int line = span.StartLinePosition.Line;
                            result.Add(Relative(files[i]) + ":" + (line + 1).ToString()
                                + ":" + text.Lines[line].ToString().Trim());
                        }
                    }
                }
                return result.ToArray();
            }
        }

        /// <summary>
        /// 检查类型、方法、属性和字段是否具有 summary XML 注释
        /// </summary>
        /// <returns>缺失项</returns>
        public string[] CheckXmlComments()
        {
            lock (MauRoslynSourceWorkspace_Gate)
            {
                List<string> result = new List<string>();
                string[] files = GetSourceFiles();
                for (int i = 0; i < files.Length; i = i + 1)
                {
                    SyntaxTree tree = ParseFile(files[i]);
                    foreach (SyntaxNode node in tree.GetRoot().DescendantNodes())
                    {
                        MemberDeclarationSyntax? member =
                            node as MemberDeclarationSyntax;
                        if (member == null)
                        {
                            continue;
                        }
                        if (RequiresSummary(member) && !HasSummary(member))
                        {
                            FileLinePositionSpan span = tree.GetLineSpan(member.Span);
                            result.Add(Relative(files[i]) + ":"
                                + (span.StartLinePosition.Line + 1).ToString() + ":"
                                + MemberName(member));
                        }
                    }
                }
                return result.ToArray();
            }
        }

        /// <summary>
        /// 以已解析 Block 替换唯一方法体并原子写回
        /// </summary>
        /// <param name="typeName">类型名</param>
        /// <param name="methodName">方法名</param>
        /// <param name="newBody">包含花括号的完整方法体</param>
        public void ReplaceMethodBody(string typeName, string methodName, string newBody)
        {
            StatementSyntax parsed = SyntaxFactory.ParseStatement(newBody);
            BlockSyntax? block = parsed as BlockSyntax;
            if (block == null || parsed.ContainsDiagnostics)
            {
                throw new ArgumentException("New method body is invalid.", "newBody");
            }
            lock (MauRoslynSourceWorkspace_Gate)
            {
                MauSourceLocation location = Find(typeName, methodName);
                MethodDeclarationSyntax? method = location.Node
                    as MethodDeclarationSyntax;
                if (method == null)
                {
                    throw new InvalidOperationException("Target is not a method.");
                }
                SyntaxNode triviaSource = method;
                if (method.Body != null)
                {
                    triviaSource = method.Body;
                }
                MethodDeclarationSyntax replacement = method.WithBody(block
                    .WithTriviaFrom(triviaSource))
                    .WithExpressionBody(null).WithSemicolonToken(default);
                SyntaxNode root = method.SyntaxTree.GetRoot();
                SyntaxNode updated = root.ReplaceNode(method, replacement);
                WriteAtomic(location.FilePath, updated.ToFullString());
            }
        }

        /// <summary>
        /// 查找唯一类型或成员
        /// </summary>
        /// <param name="typeName">类型名</param>
        /// <param name="memberName">成员名</param>
        /// <returns>位置</returns>
        private MauSourceLocation Find(string typeName, string memberName)
        {
            List<MauSourceLocation> matches = new List<MauSourceLocation>();
            string[] files = GetSourceFiles();
            for (int i = 0; i < files.Length; i = i + 1)
            {
                SyntaxTree tree = ParseFile(files[i]);
                foreach (SyntaxNode node in tree.GetRoot().DescendantNodes())
                {
                    BaseTypeDeclarationSyntax? type =
                        node as BaseTypeDeclarationSyntax;
                    if (type == null)
                    {
                        continue;
                    }
                    if (type.Identifier.ValueText != typeName)
                    {
                        continue;
                    }
                    if (memberName.Length == 0)
                    {
                        matches.Add(new MauSourceLocation(files[i], type));
                        continue;
                    }
                    TypeDeclarationSyntax? declaration = type as TypeDeclarationSyntax;
                    if (declaration != null)
                    {
                        for (int m = 0; m < declaration.Members.Count; m = m + 1)
                        {
                            if (MemberName(declaration.Members[m]) == memberName)
                            {
                                matches.Add(new MauSourceLocation(files[i],
                                    declaration.Members[m]));
                            }
                        }
                    }
                }
            }
            if (matches.Count != 1)
            {
                throw new InvalidOperationException("Expected one source match but found "
                    + matches.Count.ToString() + ".");
            }
            return matches[0];
        }

        /// <summary>
        /// 解析文件并保留文档注释
        /// </summary>
        /// <param name="filePath">文件</param>
        /// <returns>语法树</returns>
        private SyntaxTree ParseFile(string filePath)
        {
            CSharpParseOptions options = new CSharpParseOptions(
                LanguageVersion.Latest, DocumentationMode.Parse);
            return CSharpSyntaxTree.ParseText(File.ReadAllText(filePath,
                Encoding.UTF8), options, filePath, Encoding.UTF8);
        }

        /// <summary>
        /// 枚举排除构建产物的 C# 文件
        /// </summary>
        /// <returns>稳定排序文件</returns>
        private string[] GetSourceFiles()
        {
            string[] candidates = Directory.GetFiles(MauRoslynSourceWorkspace_Root,
                "*.cs", SearchOption.AllDirectories);
            List<string> files = new List<string>();
            for (int i = 0; i < candidates.Length; i = i + 1)
            {
                string relative = Relative(candidates[i]);
                if (!relative.StartsWith("bin" + Path.DirectorySeparatorChar,
                        StringComparison.OrdinalIgnoreCase)
                    && !relative.StartsWith("obj" + Path.DirectorySeparatorChar,
                        StringComparison.OrdinalIgnoreCase)
                    && relative.IndexOf(Path.DirectorySeparatorChar + "bin"
                        + Path.DirectorySeparatorChar,
                        StringComparison.OrdinalIgnoreCase) < 0
                    && relative.IndexOf(Path.DirectorySeparatorChar + "obj"
                        + Path.DirectorySeparatorChar,
                        StringComparison.OrdinalIgnoreCase) < 0
                    && relative.IndexOf(Path.DirectorySeparatorChar + ".godot"
                        + Path.DirectorySeparatorChar,
                        StringComparison.OrdinalIgnoreCase) < 0)
                {
                    files.Add(candidates[i]);
                }
            }
            files.Sort(StringComparer.OrdinalIgnoreCase);
            return files.ToArray();
        }

        /// <summary>
        /// 读取声明成员名
        /// </summary>
        /// <param name="member">成员</param>
        /// <returns>名称或种类</returns>
        private string MemberName(MemberDeclarationSyntax member)
        {
            MethodDeclarationSyntax? method = member as MethodDeclarationSyntax;
            if (method != null)
            {
                return method.Identifier.ValueText;
            }
            PropertyDeclarationSyntax? property = member as PropertyDeclarationSyntax;
            if (property != null)
            {
                return property.Identifier.ValueText;
            }
            FieldDeclarationSyntax? field = member as FieldDeclarationSyntax;
            if (field != null && field.Declaration.Variables.Count > 0)
            {
                return field.Declaration.Variables[0].Identifier.ValueText;
            }
            ConstructorDeclarationSyntax? constructor =
                member as ConstructorDeclarationSyntax;
            if (constructor != null)
            {
                return constructor.Identifier.ValueText;
            }
            BaseTypeDeclarationSyntax? type = member as BaseTypeDeclarationSyntax;
            if (type != null)
            {
                return type.Identifier.ValueText;
            }
            return member.Kind().ToString();
        }

        /// <summary>
        /// 判断声明是否属于注释门禁范围
        /// </summary>
        /// <param name="member">声明</param>
        /// <returns>是否要求 summary</returns>
        private bool RequiresSummary(MemberDeclarationSyntax member)
        {
            return member is BaseTypeDeclarationSyntax
                || member is MethodDeclarationSyntax
                || member is ConstructorDeclarationSyntax
                || member is PropertyDeclarationSyntax
                || member is FieldDeclarationSyntax;
        }

        /// <summary>
        /// 判断前导文档 Trivia 是否包含 summary 元素
        /// </summary>
        /// <param name="member">声明</param>
        /// <returns>是否存在</returns>
        private bool HasSummary(MemberDeclarationSyntax member)
        {
            foreach (SyntaxTrivia trivia in member.GetLeadingTrivia())
            {
                DocumentationCommentTriviaSyntax? documentation =
                    trivia.GetStructure() as DocumentationCommentTriviaSyntax;
                if (documentation != null)
                {
                    foreach (XmlNodeSyntax node in documentation.Content)
                    {
                        XmlElementSyntax? element = node as XmlElementSyntax;
                        if (element != null
                            && element.StartTag.Name.LocalName.ValueText == "summary")
                        {
                            return true;
                        }
                    }
                }
            }
            return false;
        }

        /// <summary>
        /// 同目录临时文件原子替换
        /// </summary>
        /// <param name="path">目标</param>
        /// <param name="content">源码</param>
        private void WriteAtomic(string path, string content)
        {
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temporary, content, new UTF8Encoding(false));
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
        /// 转换为源码根相对路径
        /// </summary>
        /// <param name="path">绝对路径</param>
        /// <returns>相对路径</returns>
        private string Relative(string path)
        {
            return Path.GetRelativePath(MauRoslynSourceWorkspace_Root, path);
        }

        /// <summary>
        /// 源码节点及文件位置
        /// </summary>
        private sealed class MauSourceLocation
        {
            /// <summary>
            /// 文件绝对路径
            /// </summary>
            internal readonly string FilePath;

            /// <summary>
            /// 语法节点
            /// </summary>
            internal readonly SyntaxNode Node;

            /// <summary>
            /// 创建位置
            /// </summary>
            /// <param name="filePath">文件</param>
            /// <param name="node">节点</param>
            internal MauSourceLocation(string filePath, SyntaxNode node)
            {
                FilePath = filePath;
                Node = node;
            }
        }
    }
}
