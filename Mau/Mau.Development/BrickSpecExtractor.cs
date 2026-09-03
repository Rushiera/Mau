#nullable disable warnings
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Mau.Development
{
    /// <summary>
    /// 积木契约端口——参数名 + 契约类型文本
    /// </summary>
    public sealed class BrickPortSpec
    {
        /// <summary>
        /// 端口名
        /// </summary>
        public string Name = "";

        /// <summary>
        /// 契约类型文本（string/int/long/bool/string[]）
        /// </summary>
        public string Type = "";
    }

    /// <summary>
    /// 积木契约条目——文件头十字段 + Roslyn 静态签名提取产物
    /// </summary>
    public sealed class BrickSpecEntry
    {
        /// <summary>
        /// 积木 ID——BRIK-{类别}-{序号}
        /// </summary>
        public string Id = "";

        /// <summary>
        /// 积木名——Mau 文本调用名（probe.sink）
        /// </summary>
        public string Name = "";

        /// <summary>
        /// 类别码
        /// </summary>
        public string Category = "";

        /// <summary>
        /// 相对路径（Bricks/ 下）
        /// </summary>
        public string Path = "";

        /// <summary>
        /// 实现签名——命名空间.类.方法
        /// </summary>
        public string Implementation = "";

        /// <summary>
        /// 依赖 ID 列表
        /// </summary>
        public List<string> Dependencies = new List<string>();

        /// <summary>
        /// 外部包声明列表
        /// </summary>
        public List<string> Packages = new List<string>();

        /// <summary>
        /// 输入端口
        /// </summary>
        public List<BrickPortSpec> Inputs = new List<BrickPortSpec>();

        /// <summary>
        /// 输出端口
        /// </summary>
        public List<BrickPortSpec> Outputs = new List<BrickPortSpec>();

        /// <summary>
        /// 返回语义——Bool / Void
        /// </summary>
        public string Return = "Bool";

        /// <summary>
        /// 时长形态——Sync（缺省）
        /// </summary>
        public string Duration = "Sync";

        /// <summary>
        /// 线程约束——main（缺省）
        /// </summary>
        public string Thread = "main";
    }

    /// <summary>
    /// 积木契约提取器——v2 设计模式构筑（Bricks/README §三/§五）：
    /// 文件头十字段（文本解析）+ Roslyn 静态方法签名（首个 public static bool，无则回退首个 public static void，排除 Configure）。
    /// 源码是唯一真相源——index.json + INDEX.md 全部由本器重建。
    /// </summary>
    public static class BrickSpecExtractor
    {
        /// <summary>
        /// 扫描积木目录——Bricks/{类别}/BRIK-*.cs 全量提取
        /// </summary>
        /// <param name="bricksRoot">Bricks 目录绝对路径</param>
        /// <returns>契约条目列表（按 ID 排序）</returns>
        public static List<BrickSpecEntry> Scan(string bricksRoot)
        {
            List<BrickSpecEntry> entries = new List<BrickSpecEntry>();
            string[] categoryDirs = Directory.GetDirectories(bricksRoot);
            Array.Sort(categoryDirs, StringComparer.Ordinal);
            for (int c = 0; c < categoryDirs.Length; c++)
            {
                string[] files = Directory.GetFiles(categoryDirs[c], "BRIK-*.cs");
                Array.Sort(files, StringComparer.Ordinal);
                for (int f = 0; f < files.Length; f++)
                {
                    BrickSpecEntry entry = ParseFile(files[f], bricksRoot);
                    if (entry != null && entry.Name.Length > 0)
                    {
                        entries.Add(entry);
                    }
                }
            }
            entries.Sort(delegate (BrickSpecEntry a, BrickSpecEntry b)
            {
                return string.Compare(a.Id, b.Id, StringComparison.Ordinal);
            });
            return entries;
        }

        /// <summary>
        /// 单文件提取——文件头十字段 + Roslyn 签名
        /// </summary>
        /// <param name="filePath">积木源文件</param>
        /// <param name="bricksRoot">Bricks 根（相对路径计算用）</param>
        /// <returns>条目（失败返回 null）</returns>
        private static BrickSpecEntry ParseFile(string filePath, string bricksRoot)
        {
            string source = File.ReadAllText(filePath);
            BrickSpecEntry entry = new BrickSpecEntry();
            ParseHeader(source, entry);
            if (entry.Id.Length == 0 || entry.Name.Length == 0 || entry.Category.Length == 0)
            {
                return null;
            }
            entry.Path = filePath.Substring(bricksRoot.Length).TrimStart('\\', '/').Replace('\\', '/');
            ParseSignature(source, entry);
            return entry;
        }

        /// <summary>
        /// 文件头十字段解析——// 字段: 值 行
        /// </summary>
        /// <param name="source">源文本</param>
        /// <param name="entry">条目</param>
        private static void ParseHeader(string source, BrickSpecEntry entry)
        {
            string[] lines = source.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (!line.StartsWith("// ", StringComparison.Ordinal))
                {
                    continue;
                }
                int colon = line.IndexOf(':');
                if (colon < 0)
                {
                    continue;
                }
                string key = line.Substring(3, colon - 3).Trim();
                string value = line.Substring(colon + 1).Trim();
                if (key == "积木")
                {
                    entry.Name = value;
                }
                else if (key == "ID")
                {
                    entry.Id = value;
                }
                else if (key == "类别")
                {
                    entry.Category = value;
                }
                else if (key == "依赖")
                {
                    if (value != "无" && value.Length > 0)
                    {
                        string[] parts = value.Split(',');
                        for (int p = 0; p < parts.Length; p++)
                        {
                            entry.Dependencies.Add(parts[p].Trim());
                        }
                    }
                }
                else if (key == "包")
                {
                    if (value != "无" && value.Length > 0)
                    {
                        string[] parts = value.Split(';');
                        for (int p = 0; p < parts.Length; p++)
                        {
                            entry.Packages.Add(parts[p].Trim());
                        }
                    }
                }
                else if (key == "时长")
                {
                    entry.Duration = value;
                }
                else if (key == "线程")
                {
                    entry.Thread = value;
                }
            }
        }

        /// <summary>
        /// Roslyn 签名提取——命名空间 + 契约方法（inputs/outputs/return）
        /// </summary>
        /// <param name="source">源文本</param>
        /// <param name="entry">条目</param>
        private static void ParseSignature(string source, BrickSpecEntry entry)
        {
            SyntaxTree tree = CSharpSyntaxTree.ParseText(source);
            CompilationUnitSyntax root = (CompilationUnitSyntax)tree.GetRoot();
            string namespaceText = "Mau.Bricks";
            NamespaceDeclarationSyntax ns = root.Members.OfType<NamespaceDeclarationSyntax>().FirstOrDefault();
            if (ns != null)
            {
                namespaceText = ns.Name.ToString();
            }
            List<MethodDeclarationSyntax> boolMethods = new List<MethodDeclarationSyntax>();
            List<MethodDeclarationSyntax> voidMethods = new List<MethodDeclarationSyntax>();
            List<MethodDeclarationSyntax> stringMethods = new List<MethodDeclarationSyntax>();
            List<ClassDeclarationSyntax> classes = root.DescendantNodes().OfType<ClassDeclarationSyntax>().ToList();
            for (int c = 0; c < classes.Count; c++)
            {
                ClassDeclarationSyntax cls = classes[c];
                if (!cls.Modifiers.Any(SyntaxKind.StaticKeyword))
                {
                    continue;
                }
                List<MethodDeclarationSyntax> methods = cls.Members.OfType<MethodDeclarationSyntax>().ToList();
                for (int m = 0; m < methods.Count; m++)
                {
                    MethodDeclarationSyntax method = methods[m];
                    if (!method.Modifiers.Any(SyntaxKind.PublicKeyword) || !method.Modifiers.Any(SyntaxKind.StaticKeyword))
                    {
                        continue;
                    }
                    if (method.Identifier.Text.StartsWith("Configure", StringComparison.Ordinal))
                    {
                        continue;
                    }
                    PredefinedTypeSyntax retType = method.ReturnType as PredefinedTypeSyntax;
                    if (retType == null)
                    {
                        continue;
                    }
                    if (retType.Keyword.IsKind(SyntaxKind.BoolKeyword))
                    {
                        boolMethods.Add(method);
                    }
                    else if (retType.Keyword.IsKind(SyntaxKind.VoidKeyword))
                    {
                        voidMethods.Add(method);
                    }
                    else if (retType.Keyword.IsKind(SyntaxKind.StringKeyword))
                    {
                        stringMethods.Add(method);
                    }
                }
            }
            MethodDeclarationSyntax target = null;
            string returnKind = "Bool";
            if (boolMethods.Count > 0)
            {
                target = boolMethods[0];
                returnKind = "Bool";
            }
            else if (voidMethods.Count > 0)
            {
                target = voidMethods[0];
                returnKind = "Void";
            }
            else if (stringMethods.Count > 0)
            {
                target = stringMethods[0];
                returnKind = "String";
            }
            if (target == null)
            {
                return;
            }
            entry.Return = returnKind;
            entry.Implementation = namespaceText + "." + ((ClassDeclarationSyntax)target.Parent).Identifier.Text + "." + target.Identifier.Text;
            SeparatedSyntaxList<ParameterSyntax> parameters = target.ParameterList.Parameters;
            for (int p = 0; p < parameters.Count; p++)
            {
                ParameterSyntax param = parameters[p];
                BrickPortSpec port = new BrickPortSpec();
                port.Name = param.Identifier.Text;
                port.Type = TypeText(param.Type);
                if (param.Modifiers.Any(SyntaxKind.OutKeyword))
                {
                    entry.Outputs.Add(port);
                }
                else
                {
                    entry.Inputs.Add(port);
                }
            }
        }

        /// <summary>
        /// 契约类型文本——PredefinedType 关键字 / 数组元素[] / 其他原样
        /// </summary>
        /// <param name="type">类型语法节点</param>
        /// <returns>契约类型文本</returns>
        private static string TypeText(TypeSyntax type)
        {
            PredefinedTypeSyntax predefined = type as PredefinedTypeSyntax;
            if (predefined != null)
            {
                return predefined.Keyword.Text;
            }
            ArrayTypeSyntax array = type as ArrayTypeSyntax;
            if (array != null)
            {
                return TypeText(array.ElementType) + "[]";
            }
            return type.ToString();
        }

        /// <summary>
        /// 索引 JSON 构建——Bricks/index.json 格式（version 3）
        /// </summary>
        /// <param name="entries">条目列表</param>
        /// <param name="packages">包聚合清单</param>
        /// <returns>JSON 文本</returns>
        public static string BuildJson(List<BrickSpecEntry> entries, out List<string> packages)
        {
            packages = new List<string>();
            for (int i = 0; i < entries.Count; i++)
            {
                for (int p = 0; p < entries[i].Packages.Count; p++)
                {
                    if (!packages.Contains(entries[i].Packages[p]))
                    {
                        packages.Add(entries[i].Packages[p]);
                    }
                }
            }
            StringBuilder sb = new StringBuilder();
            sb.Append("{\n");
            sb.Append("  \"version\": 3,\n");
            sb.Append("  \"generatedAt\": \"" + DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ") + "\",\n");
            sb.Append("  \"packages\": [");
            for (int p = 0; p < packages.Count; p++)
            {
                if (p > 0)
                {
                    sb.Append(", ");
                }
                sb.Append("\"" + packages[p] + "\"");
            }
            sb.Append("],\n");
            sb.Append("  \"bricks\": [\n");
            for (int i = 0; i < entries.Count; i++)
            {
                if (i > 0)
                {
                    sb.Append(",\n");
                }
                sb.Append(EntryJson(entries[i]));
            }
            sb.Append("\n  ]\n");
            sb.Append("}\n");
            return sb.ToString();
        }

        /// <summary>
        /// 单条目 JSON
        /// </summary>
        /// <param name="entry">条目</param>
        /// <returns>JSON 片段</returns>
        private static string EntryJson(BrickSpecEntry entry)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("    {\"id\":\"" + entry.Id + "\",\"name\":\"" + entry.Name + "\",\"category\":\"" + entry.Category + "\",\"path\":\"" + entry.Path + "\",\"dependencies\":[");
            for (int d = 0; d < entry.Dependencies.Count; d++)
            {
                if (d > 0)
                {
                    sb.Append(",");
                }
                sb.Append("\"" + entry.Dependencies[d] + "\"");
            }
            sb.Append("],\"packages\":[");
            for (int p = 0; p < entry.Packages.Count; p++)
            {
                if (p > 0)
                {
                    sb.Append(",");
                }
                sb.Append("\"" + entry.Packages[p] + "\"");
            }
            sb.Append("],\"status\":\"active\",\"contract\":{\"implementation\":\"" + entry.Implementation + "\",\"inputs\":[");
            for (int i = 0; i < entry.Inputs.Count; i++)
            {
                if (i > 0)
                {
                    sb.Append(",");
                }
                sb.Append("{\"name\":\"" + entry.Inputs[i].Name + "\",\"type\":\"" + entry.Inputs[i].Type + "\"}");
            }
            sb.Append("],\"outputs\":[");
            for (int o = 0; o < entry.Outputs.Count; o++)
            {
                if (o > 0)
                {
                    sb.Append(",");
                }
                sb.Append("{\"name\":\"" + entry.Outputs[o].Name + "\",\"type\":\"" + entry.Outputs[o].Type + "\"}");
            }
            sb.Append("],\"return\":\"" + entry.Return + "\",\"duration\":\"" + entry.Duration + "\",\"thread\":\"" + entry.Thread + "\"}}");
            return sb.ToString();
        }

        /// <summary>
        /// INDEX.md 构建——人类可读五维表
        /// </summary>
        /// <param name="entries">条目列表</param>
        /// <returns>Markdown 文本</returns>
        public static string BuildMarkdown(List<BrickSpecEntry> entries)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("# Mau 积木索引 — INDEX\n\n");
            sb.Append("> 版本：v3.3 | 更新：" + DateTime.Now.ToString("yyyy-MM-dd") + "（mau bricks index --update 源码驱动重建）\n");
            sb.Append("> 全量积木登记——一行一条。ID 永不重用。\n\n");
            sb.Append("## 全部积木\n\n");
            sb.Append("| ID | 名字 | 类别 | 工程路径 | 依赖 | 状态 | 来源 |\n");
            sb.Append("|:--|:--|:--|:--|:--|:--|:--|\n");
            for (int i = 0; i < entries.Count; i++)
            {
                BrickSpecEntry entry = entries[i];
                string deps = entry.Dependencies.Count == 0 ? "无" : string.Join(",", entry.Dependencies);
                sb.Append("| " + entry.Id + " | " + entry.Name + " | " + entry.Category + " | " + entry.Path + " | " + deps + " | active |  |\n");
            }
            sb.Append("\n---\n\n");
            sb.Append("_版本：v3.3 | " + DateTime.Now.ToString("yyyy-MM-dd") + " | 自动生成——mau bricks index --update（源码唯一真相源：文件头 + 静态签名；来源列为人工维护区）_\n");
            return sb.ToString();
        }

        /// <summary>
        /// 校验尾重算——文件内容（去旧校验尾行）SHA256，追加 // #MAU_CHECKSUM:SHA256:{hex}
        /// </summary>
        /// <param name="filePath">积木源文件</param>
        public static void RewriteChecksum(string filePath)
        {
            string[] lines = File.ReadAllLines(filePath);
            StringBuilder content = new StringBuilder();
            for (int i = 0; i < lines.Length; i++)
            {
                if (lines[i].TrimStart().StartsWith("// #MAU_CHECKSUM:SHA256:", StringComparison.Ordinal))
                {
                    continue;
                }
                content.Append(lines[i]);
                content.Append('\n');
            }
            string body = content.ToString();
            string hash;
            using (SHA256 sha = SHA256.Create())
            {
                byte[] bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(body));
                StringBuilder hex = new StringBuilder();
                for (int b = 0; b < bytes.Length; b++)
                {
                    hex.Append(bytes[b].ToString("X2"));
                }
                hash = hex.ToString();
            }
            File.WriteAllText(filePath, body + "// #MAU_CHECKSUM:SHA256:" + hash + "\n", new UTF8Encoding(true));
        }
    }
}
