using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Mau.Development
{
    /// <summary>
    /// MauRoslynBridge 符号查找面（partial 分部）——cs.find：按名字反查声明位置（类 / 方法 / 构造函数 / 属性 / 字段 / 事件）。
    /// 与 cs.find_ref 互补：find_ref 查「谁引用了它」（引用方向），find 查「它定义在哪」（定义方向）。
    /// 匹配口径 = 名字包含（不区分大小写）；精确匹配优先输出。
    /// </summary>
    public sealed partial class MauRoslynBridge
    {
        /// <summary>
        /// 单次查找输出上限——回传上下文防爆（meta.hits 仍给全量计数）
        /// </summary>
        private const int MaxFindHits = 60;

        /// <summary>
        /// cs.find——按名字在多项目入口内定位声明（类 / 方法 / 构造函数 / 属性 / 字段 / 事件）；
        /// 输出「种类 + 全名 + 所属项目 + 文件:行」。
        /// </summary>
        /// <param name="args">参数</param>
        /// <param name="result">结果</param>
        /// <returns>调用完成</returns>
        private bool ToolFind(JsonElement args, out string result)
        {
            string path = Arg(args, "path");
            string name = Arg(args, "name");
            string resolveError;
            List<string> projects = ResolveProjects(path, out resolveError);
            if (projects.Count == 0)
            {
                result = resolveError;
                return false;
            }
            List<string> exact = new List<string>();
            List<string> partial = new List<string>();
            int total = 0;
            for (int i = 0; i < projects.Count; i = i + 1)
            {
                ProjectCache cache = EnsureProject(projects[i]);
                FullScan(cache);
                CollectDeclarations(cache, name, exact, partial, ref total);
            }
            if (total == 0)
            {
                result = "ERR|SYMBOL_NOT_FOUND|未找到匹配的声明: " + name + "（类 / 方法 / 构造函数 / 属性 / 字段 / 事件；包含匹配，不区分大小写）";
                return false;
            }
            Dictionary<string, object> meta = new Dictionary<string, object>();
            meta["name"] = name;
            meta["hits"] = total;
            meta["projects"] = projects.Count;
            StringBuilder sb = new StringBuilder();
            sb.Append(MetaHead("cs-find", true, meta));
            int written = AppendFindLines(exact, sb, 0);
            written = AppendFindLines(partial, sb, written);
            if (total > written)
            {
                sb.Append(Environment.NewLine + "…[截断: 共 " + total + " 条，仅列前 " + written + " 条]");
            }
            result = TrimResult(sb.ToString(), MaxResultChars);
            return true;
        }

        /// <summary>
        /// 单项目声明收集——精确匹配进 exact 组、包含匹配进 partial 组（每组保持 项目序 → 文件序 → 文档序）
        /// </summary>
        /// <param name="cache">项目缓存</param>
        /// <param name="query">查找值</param>
        /// <param name="exact">精确匹配行</param>
        /// <param name="partial">包含匹配行</param>
        /// <param name="total">全部命中计数</param>
        private void CollectDeclarations(ProjectCache cache, string query, List<string> exact, List<string> partial, ref int total)
        {
            List<string> keys = new List<string>(cache.Trees.Keys);
            keys.Sort(StringComparer.OrdinalIgnoreCase);
            for (int k = 0; k < keys.Count; k = k + 1)
            {
                SyntaxTree tree = cache.Trees[keys[k]];
                foreach (SyntaxNode node in tree.GetRoot().DescendantNodes())
                {
                    ClassDeclarationSyntax? classDecl = node as ClassDeclarationSyntax;
                    if (classDecl != null)
                    {
                        AddFindHit(cache, tree, classDecl, "类", classDecl.Identifier.Text, "", query, exact, partial, ref total);
                        continue;
                    }
                    MethodDeclarationSyntax? methodDecl = node as MethodDeclarationSyntax;
                    if (methodDecl != null)
                    {
                        AddFindHit(cache, tree, methodDecl, "方法", methodDecl.Identifier.Text, OwnerName(methodDecl), query, exact, partial, ref total);
                        continue;
                    }
                    ConstructorDeclarationSyntax? ctorDecl = node as ConstructorDeclarationSyntax;
                    if (ctorDecl != null)
                    {
                        AddFindHit(cache, tree, ctorDecl, "构造函数", ctorDecl.Identifier.Text, ctorDecl.Identifier.Text, query, exact, partial, ref total);
                        continue;
                    }
                    PropertyDeclarationSyntax? propertyDecl = node as PropertyDeclarationSyntax;
                    if (propertyDecl != null)
                    {
                        AddFindHit(cache, tree, propertyDecl, "属性", propertyDecl.Identifier.Text, OwnerName(propertyDecl), query, exact, partial, ref total);
                        continue;
                    }
                    EventDeclarationSyntax? eventDecl = node as EventDeclarationSyntax;
                    if (eventDecl != null)
                    {
                        AddFindHit(cache, tree, eventDecl, "事件", eventDecl.Identifier.Text, OwnerName(eventDecl), query, exact, partial, ref total);
                        continue;
                    }
                    EventFieldDeclarationSyntax? eventFieldDecl = node as EventFieldDeclarationSyntax;
                    if (eventFieldDecl != null)
                    {
                        foreach (VariableDeclaratorSyntax variable in eventFieldDecl.Declaration.Variables)
                        {
                            AddFindHit(cache, tree, variable, "事件", variable.Identifier.Text, OwnerName(eventFieldDecl), query, exact, partial, ref total);
                        }
                        continue;
                    }
                    FieldDeclarationSyntax? fieldDecl = node as FieldDeclarationSyntax;
                    if (fieldDecl != null)
                    {
                        foreach (VariableDeclaratorSyntax variable in fieldDecl.Declaration.Variables)
                        {
                            AddFindHit(cache, tree, variable, "字段", variable.Identifier.Text, OwnerName(fieldDecl), query, exact, partial, ref total);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// 命中登记——名字包含匹配即记录（精确匹配进 exact，其余进 partial）
        /// </summary>
        /// <param name="cache">项目缓存（取程序集名与相对路径）</param>
        /// <param name="tree">命中树</param>
        /// <param name="node">命中节点（决定行号）</param>
        /// <param name="kind">种类标签</param>
        /// <param name="simpleName">声明名</param>
        /// <param name="owner">所属类型名（类本身传空串）</param>
        /// <param name="query">查找值</param>
        /// <param name="exact">精确匹配行</param>
        /// <param name="partial">包含匹配行</param>
        /// <param name="total">全部命中计数</param>
        private void AddFindHit(ProjectCache cache, SyntaxTree tree, SyntaxNode node, string kind, string simpleName, string owner, string query, List<string> exact, List<string> partial, ref int total)
        {
            if (simpleName.Length == 0 || simpleName.IndexOf(query, StringComparison.OrdinalIgnoreCase) < 0)
            {
                return;
            }
            string fullName = simpleName;
            if (owner.Length > 0)
            {
                fullName = owner + "." + simpleName;
            }
            FileLinePositionSpan span = node.GetLocation().GetLineSpan();
            string line = "[" + cache.AssemblyName + "] " + RelativeToProject(cache, tree.FilePath) + ":" + (span.StartLinePosition.Line + 1).ToString() + ": " + kind + " " + fullName;
            total = total + 1;
            if (string.Equals(simpleName, query, StringComparison.OrdinalIgnoreCase))
            {
                exact.Add(line);
            }
            else
            {
                partial.Add(line);
            }
        }

        /// <summary>
        /// 命中行追加——受 MaxFindHits 上限约束，返回累计写入数
        /// </summary>
        /// <param name="lines">命中行</param>
        /// <param name="sb">输出缓冲</param>
        /// <param name="written">已写入数</param>
        /// <returns>累计写入数</returns>
        private static int AppendFindLines(List<string> lines, StringBuilder sb, int written)
        {
            for (int i = 0; i < lines.Count; i = i + 1)
            {
                if (written >= MaxFindHits)
                {
                    return written;
                }
                sb.Append(Environment.NewLine + lines[i]);
                written = written + 1;
            }
            return written;
        }

        /// <summary>
        /// 成员所属类型名——向上取最近的类型声明标识符（类 / 结构 / 接口 / 记录；无则空串）
        /// </summary>
        /// <param name="node">成员节点</param>
        /// <returns>类型名</returns>
        private static string OwnerName(SyntaxNode node)
        {
            SyntaxNode? current = node.Parent;
            while (current != null)
            {
                ClassDeclarationSyntax? classDecl = current as ClassDeclarationSyntax;
                if (classDecl != null)
                {
                    return classDecl.Identifier.Text;
                }
                StructDeclarationSyntax? structDecl = current as StructDeclarationSyntax;
                if (structDecl != null)
                {
                    return structDecl.Identifier.Text;
                }
                InterfaceDeclarationSyntax? interfaceDecl = current as InterfaceDeclarationSyntax;
                if (interfaceDecl != null)
                {
                    return interfaceDecl.Identifier.Text;
                }
                RecordDeclarationSyntax? recordDecl = current as RecordDeclarationSyntax;
                if (recordDecl != null)
                {
                    return recordDecl.Identifier.Text;
                }
                current = current.Parent;
            }
            return "";
        }
    }
}
