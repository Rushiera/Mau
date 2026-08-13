using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Mau.Contracts;
using Mau.Translator;
using Mau.Runtime;
using Mau.Development;

namespace Mau.Cli
{
    /// <summary>
    /// 积木管理命令——list / index --verify / index --update / index --check-license / test
    /// T1 积木索引机制（design-mau-brickindex.md）：编号规范 v2 + 五维索引 + 全局跑测出口
    /// </summary>
    public static class CommandBricks
    {
        /// <summary>
        /// 注册表积木契约条目——含从文件头扫描的索引信息
        /// </summary>
        public sealed class BrickIndexEntry
        {
            /// <summary>
            /// 唯一 ID——BRIK-{类别}-{三位序号}
            /// </summary>
            public string Id = "";

            /// <summary>
            /// 积木名——类别.函数（BrickRegistry.Name）
            /// </summary>
            public string Name = "";

            /// <summary>
            /// 类别码
            /// </summary>
            public string Category = "";

            /// <summary>
            /// 实现文件相对路径（仓库根起）
            /// </summary>
            public string Path = "";

            /// <summary>
            /// 依赖声明——L1 基座/L2 叶子积木/L3 外部库，逗号分隔
            /// </summary>
            public string Dependencies = "";

            /// <summary>
            /// 外部包声明——包名@版本 列表（文件头 `包:` 字段，分号分隔；无则空）
            /// </summary>
            public string Packages = "";

            /// <summary>
            /// 状态——active / deprecated
            /// </summary>
            public string Status = "";

            /// <summary>
            /// 来源——人工维护列（CH3/CH2/CH4 等）
            /// </summary>
            public string Source = "";

            /// <summary>
            /// 文件头 ID 原始声明——单 ID 或范围（001 ~ 011），check-license 用
            /// </summary>
            public string FileIdText = "";

            /// <summary>
            /// 契约——index.json 镜像字段
            /// </summary>
            public BrickContract? Contract;
        }

        /// <summary>
        /// bricks 命令入口
        /// </summary>
        /// <param name="args">参数——list | index --verify | index --update | index --check-license | test</param>
        /// <returns>退出码</returns>
        public static int Execute(string[] args)
        {
            // [段0] 积木索引——R1：BrickIndex 文件索引（枚举前置；bricks 命令不经过 MauCompiler.Compile）
            if (!EnsureIndexLoaded())
            {
                Console.WriteLine("FAIL: 积木索引不可用——未找到 Bricks/index.json（环境变量 MAU_BRICKS_ROOT 或仓库根）");
                return 1;
            }
            if (args.Length == 0)
            {
                Console.WriteLine("用法: mau bricks list | mau bricks index --verify | mau bricks index --update | mau bricks index --check-license | mau bricks test | mau bricks reseal");
                return 1;
            }
            string sub = args[0];
            if (sub == "list")
            {
                return ListBricks();
            }
            if (sub == "index")
            {
                if (args.Length < 2)
                {
                    Console.WriteLine("用法: mau bricks index --verify | --update | --check-license");
                    return 1;
                }
                if (args[1] == "--verify")
                {
                    return VerifyIndex();
                }
                if (args[1] == "--update")
                {
                    // --bricks <path> = 产品积木目录（项目自持；默认 = Mau 仓库根 Bricks）
                    string? productDir = null;
                    if (args.Length > 3 && args[2] == "--bricks")
                    {
                        productDir = args[3];
                    }
                    return UpdateIndex(productDir);
                }
                if (args[1] == "--check-license")
                {
                    return CheckLicense();
                }
                Console.WriteLine("未知 index 子命令: " + args[1]);
                return 1;
            }
            if (sub == "test")
            {
                return RunGlobalTest();
            }
            if (sub == "reseal")
            {
                return ResealAll();
            }
            Console.WriteLine("未知 bricks 子命令: " + sub);
            return 1;
        }

        /// <summary>
        /// 重算全部积木文件 SHA256 校验尾——文件头/实现变更后调用（工具链全 C#：不依赖外部脚本）
        /// </summary>
        /// <returns>退出码</returns>
        public static int ResealAll()
{
            string? root = CliSupport.FindWorkspaceRoot();
            if (root == null)
            {
                Console.WriteLine("FAIL: 未找到 Mau.sln");
                return 1;
            }
            string catalogDir = Path.Combine(root, "Bricks");
            // 目录即清单——扫描 Bricks/ 全部子目录（新增类别自动进，不维护硬编码清单）
            string[] dirs = Directory.GetDirectories(catalogDir);
            System.Array.Sort(dirs, StringComparer.Ordinal);
            int resealed = 0;
            for (int d = 0; d < dirs.Length; d++)
            {
                string[] files = Directory.GetFiles(dirs[d], "*.cs");
                for (int f = 0; f < files.Length; f++)
                {
                    if (ResealOneFile(files[f]))
                    {
                        resealed = resealed + 1;
                    }
                }
            }
            Console.WriteLine("BRICKS_RESEAL_OK (" + resealed + " 文件校验尾已更新)");
            return 0;
        }
        /// <summary>
        /// 单文件重算校验尾——去旧校验尾行 → 计算正文 SHA256 → 追加新校验尾
        /// </summary>
        /// <param name="file">积木文件</param>
        /// <returns>是否更新</returns>
        private static bool ResealOneFile(string file)
{
            try
            {
                string original = File.ReadAllText(file);
                // 行尾感知——保持原文件行尾风格（autocrlf 工作区 CRLF 不重写为 LF，防伪变更）
                bool crlf = original.Contains("\r\n");
                string full = original.Replace("\r\n", "\n");
                string[] lines = full.Split('\n');
                int bodyEnd = lines.Length;
                while (bodyEnd > 0)
                {
                    string last = lines[bodyEnd - 1].Trim();
                    if (last.Length == 0)
                    {
                        bodyEnd = bodyEnd - 1;
                        continue;
                    }
                    if (last.StartsWith(Mau.Runtime.HashUtil.ChecksumPrefix, StringComparison.Ordinal))
                    {
                        bodyEnd = bodyEnd - 1;
                        continue;
                    }
                    break;
                }
                StringBuilder sb = new StringBuilder();
                for (int i = 0; i < bodyEnd; i++)
                {
                    if (i > 0)
                    {
                        sb.Append('\n');
                    }
                    sb.Append(lines[i]);
                }
                string body = sb.ToString();
                string hash = Mau.Runtime.HashUtil.ComputeSha256(body);
                string content = body + "\n" + Mau.Runtime.HashUtil.ChecksumPrefix + hash + "\n";
                // 伪变更检测——归一化比较（LF 基准）：重写结果与原文一致则不落盘（防 autocrlf 全 M 伪变更）
                string originalNormalized = original.Replace("\r\n", "\n");
                if (string.Equals(originalNormalized, content, StringComparison.Ordinal))
                {
                    return false;
                }
                // 行尾还原——检测到原文件 CRLF 则写入 CRLF（保持 git 工作区行尾一致）
                if (crlf)
                {
                    content = content.Replace("\n", "\r\n");
                }
                // 无 BOM 写入——与现有积木文件编码一致（带 BOM 会导致全部文件被标记修改）
                File.WriteAllText(file, content, new UTF8Encoding(false));
                return true;
            }
            catch
            {
                return false;
            }
        }/// <summary>
        /// 注册表实时枚举——全部积木契约
        /// </summary>
        /// <returns>退出码</returns>
        public static int ListBricks()
        {
            int count = 0;
            foreach (Mau.Translator.BrickIndexEntry entry in BrickIndex.All)
            {
                Console.WriteLine(entry.Name + " → " + entry.Contract.Implementation);
                count = count + 1;
            }
            Console.WriteLine("合计: " + count + " 积木");
            return 0;
        }

        /// <summary>
        /// 扫描仓库 .cs 文件头注释块——提取 ID/积木/引用/依赖
        /// </summary>
        /// <param name="root">仓库根</param>
        /// <returns>索引条目（按文件头块展开为单积木）</returns>
        private static List<BrickIndexEntry> ScanBrickHeaders(string root)
{
            List<BrickIndexEntry> entries = new List<BrickIndexEntry>();
            // 目录即清单——扫描 Bricks/ 全部子目录（新增类别自动进，不维护硬编码清单）
            string[] dirs = Directory.GetDirectories(root);
            System.Array.Sort(dirs, StringComparer.Ordinal);
            for (int d = 0; d < dirs.Length; d++)
            {
                string dirName = Path.GetFileName(dirs[d]);
                string[] files = Directory.GetFiles(dirs[d], "*.cs");
                for (int f = 0; f < files.Length; f++)
                {
                    ScanOneFile(files[f], dirName, root, entries);
                }
            }
            return entries;
        }
        /// <summary>
        /// 扫描单个 .cs 文件头注释块
        /// </summary>
        /// <param name="file">文件路径</param>
        /// <param name="dir">工程目录名</param>
        /// <param name="root">仓库根</param>
        /// <param name="entries">输出集合</param>
        private static void ScanOneFile(string file, string dir, string root, List<BrickIndexEntry> entries)
        {
            string[] lines;
            try
            {
                lines = File.ReadAllLines(file);
            }
            catch
            {
                return;
            }
            string relPath = dir + "/" + Path.GetFileName(file);
            bool inHeader = false;
            string brickName = "";
            string ids = "";
            string category = "";
            string deps = "";
            string packages = "";
            string duration = "";
            string thread = "";
            for (int i = 0; i < lines.Length && i < 16; i++)
            {
                string line = lines[i].Trim();
                if (line.StartsWith("// ═"))
                {
                    inHeader = true;
                    continue;
                }
                if (!inHeader)
                {
                    continue;
                }
                if (line.StartsWith("// 积木:"))
                {
                    brickName = line.Substring(6).Trim();
                }
                else if (line.StartsWith("// ID:"))
                {
                    ids = line.Substring(6).Trim();
                }
                else if (line.StartsWith("// 类别:"))
                {
                    category = line.Substring(6).Trim();
                }
                else if (line.StartsWith("// 依赖:"))
                {
                    deps = line.Substring(7).Trim();
                }
                else if (line.StartsWith("// 包:"))
                {
                    packages = line.Substring(5).Trim();
                }
                else if (line.StartsWith("// 时长:"))
                {
                    duration = line.Substring(6).Trim();
                }
                else if (line.StartsWith("// 线程:"))
                {
                    thread = line.Substring(6).Trim();
                }
            }
            if (brickName.Length == 0)
            {
                return;
            }
            // 一积木一文件——R1 文本库形态，无多积木展开
            BrickIndexEntry entry = new BrickIndexEntry();
            entry.Name = brickName;
            entry.Path = relPath;
            entry.Dependencies = deps;
            entry.Packages = packages;
            entry.Status = "active";
            entry.Source = "";
            entry.FileIdText = ids;
            entry.Id = ids;
            // 类别以文件头 类别: 字段为权威（docx.* 属 OFFICE 类，名前缀推断会错位）
            entry.Category = category.Length > 0 ? category : CategoryFromName(brickName);
            entry.Contract = ExtractContract(file, brickName, duration, thread);
            entries.Add(entry);
        }

        /// <summary>
        /// Roslyn 提取积木静态方法签名——类名/方法名/参数（out 判定）/返回类型 → BrickContract
        /// 文件头 时长:/线程: 为契约元数据（缺省 Sync/main）
        /// </summary>
        /// <param name="file">积木 .cs 文件</param>
        /// <param name="name">积木名</param>
        /// <param name="durationText">时长声明（空=Sync）</param>
        /// <param name="threadText">线程声明（空=main）</param>
        /// <returns>契约（提取失败返回 null）</returns>
        private static BrickContract? ExtractContract(string file, string name, string durationText, string threadText)
        {
            try
            {
                string code = File.ReadAllText(file);
                Microsoft.CodeAnalysis.SyntaxTree tree = Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(code);
                Microsoft.CodeAnalysis.SyntaxNode root = tree.GetRoot();
                foreach (Microsoft.CodeAnalysis.CSharp.Syntax.ClassDeclarationSyntax cls in
                    root.DescendantNodes().OfType<Microsoft.CodeAnalysis.CSharp.Syntax.ClassDeclarationSyntax>())
                {
                    // 契约方法选择——首个 public static 且返回 bool 的方法（辅助方法如 Configure/Classify 返回 void/int 自动跳过）；
                    // 无 bool 方法时回退首个 public static void（排除 Configure 命名辅助）
                    Microsoft.CodeAnalysis.CSharp.Syntax.MethodDeclarationSyntax? pick = null;
                    foreach (Microsoft.CodeAnalysis.CSharp.Syntax.MethodDeclarationSyntax m in
                        cls.Members.OfType<Microsoft.CodeAnalysis.CSharp.Syntax.MethodDeclarationSyntax>())
                    {
                        if (!m.Modifiers.Any(Microsoft.CodeAnalysis.CSharp.SyntaxKind.PublicKeyword)
                            || !m.Modifiers.Any(Microsoft.CodeAnalysis.CSharp.SyntaxKind.StaticKeyword))
                        {
                            continue;
                        }
                        string ret = m.ReturnType.ToString();
                        if (ret == "bool")
                        {
                            pick = m;
                            break;
                        }
                        if (ret == "string" && pick == null)
                        {
                            // 名称返回积木——多路匹配（cmd.match 等）
                            pick = m;
                            break;
                        }
                        if (ret == "void" && pick == null
                            && !m.Identifier.Text.StartsWith("Configure", StringComparison.Ordinal))
                        {
                            pick = m;
                        }
                    }
                    if (pick == null)
                    {
                        continue;
                    }
                    string implementation = "Mau.Bricks." + cls.Identifier.Text + "." + pick.Identifier.Text;
                    BrickContract contract = new BrickContract(name, implementation);
                    foreach (Microsoft.CodeAnalysis.CSharp.Syntax.ParameterSyntax p in pick.ParameterList.Parameters)
                    {
                        bool isOut = p.Modifiers.Any(Microsoft.CodeAnalysis.CSharp.SyntaxKind.OutKeyword);
                        string typeText = p.Type != null ? p.Type.ToString() : "string";
                        if (isOut)
                        {
                            contract.Outputs.Add(new BrickPort(p.Identifier.Text, TypeFromName(typeText)));
                        }
                        else
                        {
                            contract.Inputs.Add(new BrickPort(p.Identifier.Text, TypeFromName(typeText)));
                        }
                    }
                    string retType = pick.ReturnType.ToString();
                    contract.Return = retType == "void"
                        ? BrickReturnKind.Void
                        : (retType == "string" ? BrickReturnKind.String : BrickReturnKind.Bool);
                    contract.Duration = durationText == "Streaming" ? BrickDuration.Streaming
                        : durationText == "Async" ? BrickDuration.Async : BrickDuration.Sync;
                    contract.Thread = threadText.Length > 0 ? threadText : "main";
                    return contract;
                }
            }
            catch
            {
                return null;
            }
            return null;
        }

        /// <summary>
        /// C# 类型文本 → Type——覆盖积木契约全量类型（与 BrickIndex.MapType 同表）
        /// </summary>
        /// <param name="typeText">类型文本</param>
        /// <returns>Type（未知回退 string）</returns>
        private static Type TypeFromName(string typeText)
{
            // 统一映射——TypeMap（审查修复轮 2026-08-11：与 BrickIndex.MapType 同表收拢；TypeMap 已处理 ? 后缀与泛型两种格式）
            return Mau.Runtime.TypeMap.Map(typeText);
        }
        /// <summary>
        /// 类别码推断——积木名前缀
        /// </summary>
        /// <param name="name">积木名</param>
        /// <returns>类别码</returns>
        private static string CategoryFromName(string name)
        {
            int dot = name.IndexOf('.');
            if (dot < 0)
            {
                return "?";
            }
            return name.Substring(0, dot).ToUpperInvariant();
        }

        /// <summary>
        /// 读取 INDEX.md 表格行——ID/名字/类别/路径/来源
        /// </summary>
        /// <param name="indexPath">INDEX.md 路径</param>
        /// <returns>解析行列表（名称 → 条目）</returns>
        private static Dictionary<string, BrickIndexEntry> ReadIndexTable(string indexPath)
        {
            Dictionary<string, BrickIndexEntry> table = new Dictionary<string, BrickIndexEntry>(StringComparer.Ordinal);
            string[] lines;
            try
            {
                lines = File.ReadAllLines(indexPath);
            }
            catch
            {
                return table;
            }
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (!line.StartsWith("| BRIK-"))
                {
                    continue;
                }
                string[] cells = line.Split('|');
                if (cells.Length < 7)
                {
                    continue;
                }
                BrickIndexEntry entry = new BrickIndexEntry();
                entry.Id = cells[1].Trim();
                entry.Name = cells[2].Trim();
                entry.Category = cells[3].Trim();
                entry.Path = cells[4].Trim();
                entry.Dependencies = cells[5].Trim();
                entry.Status = cells[6].Trim();
                entry.Source = cells[7].Trim();
                if (!table.ContainsKey(entry.Name))
                {
                    table[entry.Name] = entry;
                }
            }
            return table;
        }

        /// <summary>
        /// index --verify——V1-V9 一致性校验
        /// </summary>
        /// <returns>退出码</returns>
        public static int VerifyIndex()
        {
            string? root = CliSupport.FindWorkspaceRoot();
            if (root == null)
            {
                Console.WriteLine("FAIL: 未找到 Mau.sln");
                return 1;
            }
            string indexPath = Path.Combine(root, "Bricks", "INDEX.md");
            if (!File.Exists(indexPath))
            {
                Console.WriteLine("FAIL: INDEX.md 不存在——" + indexPath);
                return 1;
            }
            Dictionary<string, BrickIndexEntry> table = ReadIndexTable(indexPath);
            int errors = 0;

            // V1: 积木索引全部登记
            List<string> registryNames = new List<string>();
            foreach (Mau.Translator.BrickIndexEntry brickEntry in BrickIndex.All)
            {
                registryNames.Add(brickEntry.Name);
                if (!table.ContainsKey(brickEntry.Name))
                {
                    Console.WriteLine("V1 FAIL: 积木未登记——" + brickEntry.Name);
                    errors = errors + 1;
                }
            }
            // V2: INDEX 无幽灵积木
            foreach (KeyValuePair<string, BrickIndexEntry> kv in table)
            {
                bool found = false;
                for (int i = 0; i < registryNames.Count; i++)
                {
                    if (registryNames[i] == kv.Key)
                    {
                        found = true;
                        break;
                    }
                }
                if (!found)
                {
                    Console.WriteLine("V2 FAIL: INDEX 幽灵积木——" + kv.Key + "（注册表无此积木）");
                    errors = errors + 1;
                }
            }
            // V3: ID 唯一性
            Dictionary<string, string> idToName = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, BrickIndexEntry> kv in table)
            {
                string id = kv.Value.Id;
                if (idToName.ContainsKey(id))
                {
                    Console.WriteLine("V3 FAIL: ID 重复——" + id + "（" + idToName[id] + " 与 " + kv.Key + "）");
                    errors = errors + 1;
                }
                else
                {
                    idToName[id] = kv.Key;
                }
            }
            // V4: 名称全系统唯一（BrickIndex 内不重复）
            Dictionary<string, string> nameToId = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (Mau.Translator.BrickIndexEntry brickEntry in BrickIndex.All)
            {
                if (nameToId.ContainsKey(brickEntry.Name))
                {
                    Console.WriteLine("V4 FAIL: 名称重复——" + brickEntry.Name + "（" + nameToId[brickEntry.Name] + " 与 " + brickEntry.Id + "）");
                    errors = errors + 1;
                }
                else
                {
                    nameToId[brickEntry.Name] = brickEntry.Id;
                }
            }
            // V5: ID 格式——BRIK-{类别}-{三位序号}
            foreach (KeyValuePair<string, BrickIndexEntry> kv in table)
            {
                string id = kv.Value.Id;
                if (!IsValidIdFormat(id))
                {
                    Console.WriteLine("V5 FAIL: ID 格式非法——" + id + "（" + kv.Key + "）——需要 BRIK-{类别}-{三位序号}");
                    errors = errors + 1;
                }
            }
            // V6: 路径存在
            foreach (KeyValuePair<string, BrickIndexEntry> kv in table)
            {
                string path = kv.Value.Path;
                if (path.Length > 0)
                {
                    string full = Path.Combine(root, "Bricks", path.Replace('/', Path.DirectorySeparatorChar));
                    if (!File.Exists(full))
                    {
                        Console.WriteLine("V6 FAIL: 路径不存在——" + path + "（" + kv.Key + "）");
                        errors = errors + 1;
                    }
                }
            }
            // V7: 依赖声明一致——INDEX 依赖列 vs 文件头（文件头权威）+ 声明 BRIK-ID 存在性
            List<BrickIndexEntry> headerEntries = ScanBrickHeaders(Path.Combine(root, "Bricks"));
            for (int h = 0; h < headerEntries.Count; h++)
            {
                BrickIndexEntry he = headerEntries[h];
                BrickIndexEntry? indexEntry;
                if (table.TryGetValue(he.Name, out indexEntry))
                {
                    string indexDeps = indexEntry.Dependencies ?? "";
                    if (indexDeps != he.Dependencies)
                    {
                        Console.WriteLine("V7 FAIL: 依赖列不一致——" + he.Name + "（INDEX='" + indexDeps + "' 文件头='" + he.Dependencies + "'）");
                        errors = errors + 1;
                    }
                }
                string[] depParts = he.Dependencies.Split(',');
                for (int d = 0; d < depParts.Length; d++)
                {
                    string dep = depParts[d].Trim();
                    if (dep.StartsWith("BRIK-", StringComparison.Ordinal) && !idToName.ContainsKey(dep))
                    {
                        Console.WriteLine("V7 FAIL: 依赖 ID 不存在——" + he.Name + " 声明 " + dep);
                        errors = errors + 1;
                    }
                }
            }
            // V9: 序号连续且不重用——按类别检查
            Dictionary<string, SortedSet<int>> categorySeqs = new Dictionary<string, SortedSet<int>>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, BrickIndexEntry> kv in table)
            {
                string id = kv.Value.Id;
                if (!IsValidIdFormat(id))
                {
                    continue;
                }
                // 废弃积木保留 ID 但跳号豁免——不参与序号连续性检查（注释与实现对齐）
                if (kv.Value.Status == "Deprecated")
                {
                    continue;
                }
                string cat = kv.Value.Category;
                string seqStr = id.Substring(id.LastIndexOf('-') + 1);
                int seq;
                if (int.TryParse(seqStr, out seq))
                {
                    if (!categorySeqs.ContainsKey(cat))
                    {
                        categorySeqs[cat] = new SortedSet<int>();
                    }
                    if (categorySeqs[cat].Contains(seq))
                    {
                        Console.WriteLine("V9 FAIL: 序号重复——" + id + "（" + kv.Key + "）——已使用编号不可再赋值");
                        errors = errors + 1;
                    }
                    else
                    {
                        categorySeqs[cat].Add(seq);
                    }
                }
            }
            // V9 递增检查——类别内序号严格递增（允许退役删除跳号；重复 = 已使用编号不可再赋值）
            foreach (KeyValuePair<string, SortedSet<int>> kv in categorySeqs)
            {
                int prev = -1;
                foreach (int seq in kv.Value)
                {
                    if (seq <= prev)
                    {
                        Console.WriteLine("V9 FAIL: 类别 " + kv.Key + " 序号异常——期望递增，实际 " + prev + " → " + seq);
                        errors = errors + 1;
                        break;
                    }
                    prev = seq;
                }
            }
            // V10: 外部包声明一致——文件头 `包:` vs Mau.Cli.csproj PackageReference（文件头权威）
            string cliProj = Path.Combine(root, "Mau.Cli", "Mau.Cli.csproj");
            string csprojText = "";
            try
            {
                csprojText = File.ReadAllText(cliProj);
            }
            catch
            {
                Console.WriteLine("V10 FAIL: 无法读取 Mau.Cli.csproj——" + cliProj);
                errors = errors + 1;
            }
            if (csprojText.Length > 0)
            {
                for (int p = 0; p < headerEntries.Count; p++)
                {
                    BrickIndexEntry he = headerEntries[p];
                    string[] pkgParts = (he.Packages ?? "").Split(';');
                    for (int q = 0; q < pkgParts.Length; q++)
                    {
                        string part = pkgParts[q].Trim();
                        if (part.Length == 0 || part == "无")
                        {
                            continue;
                        }
                        // 格式：包名@版本
                        int at = part.LastIndexOf('@');
                        if (at <= 0)
                        {
                            Console.WriteLine("V10 FAIL: 包声明格式非法——" + he.Name + " '" + part + "'（需要 包名@版本）");
                            errors = errors + 1;
                            continue;
                        }
                        string pkgName = part.Substring(0, at).Trim();
                        string pkgVersion = part.Substring(at + 1).Trim();
                        if (pkgVersion == "local")
                        {
                            // local = 本地项目程序集（Mau.Runtime 等）——跳过 csproj 校验
                            continue;
                        }
                        // 校验 Mau.Cli.csproj 包含该包且版本一致
                        string needle = "PackageReference Include=\"" + pkgName + "\"";
                        int incIdx = csprojText.IndexOf(needle, StringComparison.Ordinal);
                        if (incIdx < 0)
                        {
                            Console.WriteLine("V10 FAIL: 包未引用——" + he.Name + " 声明 " + part + "——Mau.Cli.csproj 缺 <PackageReference Include=\"" + pkgName + "\" />");
                            errors = errors + 1;
                            continue;
                        }
                        string verNeedle = "Version=\"" + pkgVersion + "\"";
                        int verIdx = csprojText.IndexOf(verNeedle, incIdx, StringComparison.Ordinal);
                        if (verIdx < 0)
                        {
                            Console.WriteLine("V10 FAIL: 包版本不符——" + he.Name + " 声明 " + part + "——Mau.Cli.csproj 的 " + pkgName + " 版本 ≠ " + pkgVersion);
                            errors = errors + 1;
                        }
                    }
                }
            }

            // V11: PACK 契约 schema 校验——调用方 Invoke(method) + argsJson 参数名 vs PACK 文件头 方法: 声明（漂移即 FAIL，--verify 阶段抓不等编译期）
            Dictionary<string, List<string>> packMethods = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            for (int ph = 0; ph < headerEntries.Count; ph++)
            {
                BrickIndexEntry phe = headerEntries[ph];
                if (!phe.Id.StartsWith("BRIK-PACK-", StringComparison.Ordinal))
                {
                    continue;
                }
                string packPath = Path.Combine(root, "Bricks", phe.Path.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(packPath))
                {
                    continue;
                }
                string[] packLines = File.ReadAllLines(packPath);
                bool inMethodBlock = false;
                for (int pl = 0; pl < packLines.Length; pl++)
                {
                    string pLine = packLines[pl].Trim();
                    if (pLine.StartsWith("// 方法:", StringComparison.Ordinal))
                    {
                        inMethodBlock = true;
                        ParsePackMethodLine(pLine.Substring("// 方法:".Length).Trim(), packMethods);
                        continue;
                    }
                    if (!inMethodBlock)
                    {
                        continue;
                    }
                    if (!pLine.StartsWith("//", StringComparison.Ordinal))
                    {
                        inMethodBlock = false;
                        continue;
                    }
                    string pRest = pLine.Substring(2).Trim();
                    if (pRest.Length == 0 || pRest.StartsWith("作用:", StringComparison.Ordinal) || pRest.StartsWith("依赖:", StringComparison.Ordinal) || pRest.StartsWith("包:", StringComparison.Ordinal) || pRest.StartsWith("引用:", StringComparison.Ordinal) || pRest.StartsWith("原理:", StringComparison.Ordinal) || pRest.StartsWith("常用:", StringComparison.Ordinal) || pRest.StartsWith("══", StringComparison.Ordinal))
                    {
                        inMethodBlock = false;
                        continue;
                    }
                    ParsePackMethodLine(pRest, packMethods);
                }
            }
            // 扫描调用方积木——Invoke("X") 字符串字面量 + JSON key 对照声明
            for (int ph = 0; ph < headerEntries.Count; ph++)
            {
                BrickIndexEntry he = headerEntries[ph];
                if (he.Id.StartsWith("BRIK-PACK-", StringComparison.Ordinal))
                {
                    continue;
                }
                string brickPath = Path.Combine(root, "Bricks", he.Path.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(brickPath))
                {
                    continue;
                }
                string brickText = File.ReadAllText(brickPath);
                // 提取 Invoke("method" 调用
                List<string> usedMethods = new List<string>();
                int pos = 0;
                while (true)
                {
                    int idx = brickText.IndexOf("Invoke(\"", pos, StringComparison.Ordinal);
                    if (idx < 0)
                    {
                        break;
                    }
                    int qStart = idx + "Invoke(\"".Length;
                    int qEnd = brickText.IndexOf('"', qStart);
                    if (qEnd < 0)
                    {
                        break;
                    }
                    string methodName = brickText.Substring(qStart, qEnd - qStart);
                    if (!usedMethods.Contains(methodName))
                    {
                        usedMethods.Add(methodName);
                    }
                    pos = qEnd + 1;
                }
                if (usedMethods.Count == 0)
                {
                    continue;
                }
                // 提取 JSON key（逐行去注释）
                List<string> jsonKeys = new List<string>();
                string[] brickLines = brickText.Split('\n');
                for (int bl = 0; bl < brickLines.Length; bl++)
                {
                    string code = brickLines[bl];
                    int cIdx = code.IndexOf("//", StringComparison.Ordinal);
                    if (cIdx >= 0)
                    {
                        code = code.Substring(0, cIdx);
                    }
                    int kPos = 0;
                    while (true)
                    {
                        // 匹配转义 JSON 键 "key":——C# 源码字符串中的 JSON 键闭合（开引号向前回溯）
                        int qb = code.IndexOf("\\\":", kPos, StringComparison.Ordinal);
                        if (qb < 0)
                        {
                            break;
                        }
                        int closeQ = qb + 1;
                        int openQ = code.LastIndexOf('"', closeQ - 1);
                        if (openQ > 0 && code[openQ - 1] == '\\' && closeQ - openQ - 2 <= 64)
                        {
                            string key = code.Substring(openQ + 1, closeQ - openQ - 2);
                            if (IsJsonKeyName(key) && !jsonKeys.Contains(key))
                            {
                                jsonKeys.Add(key);
                            }
                        }
                        kPos = qb + 3;
                    }
                }
                // 校验——method 白名单 + 参数 schema 漂移
                for (int m = 0; m < usedMethods.Count; m++)
                {
                    string methodName = usedMethods[m];
                    if (!packMethods.ContainsKey(methodName))
                    {
                        Console.WriteLine("V11 FAIL: 未声明 PACK 方法——" + he.Name + " 调用 Invoke(\"" + methodName + "\")——PACK 文件头 方法: 缺声明");
                        errors = errors + 1;
                        continue;
                    }
                    List<string> declaredParams = packMethods[methodName];
                    for (int k = 0; k < jsonKeys.Count; k++)
                    {
                        if (!declaredParams.Contains(jsonKeys[k]))
                        {
                            Console.WriteLine("V11 FAIL: 参数漂移——" + he.Name + " Invoke(\"" + methodName + "\") 参数 '" + jsonKeys[k] + "' 未声明（" + methodName + " → " + string.Join(",", declaredParams) + "）");
                            errors = errors + 1;
                        }
                    }
                }
            }

            if (errors == 0)
            {
                Console.WriteLine("BRICKS_INDEX_OK (" + table.Count + " 积木)");
                return 0;
            }
            Console.WriteLine("BRICKS_INDEX_FAIL: " + errors + " 处错误");
            return 1;
        }

        /// <summary>
        /// ID 格式校验——BRIK-{类别}-{三位序号}
        /// </summary>
        /// <param name="id">ID</param>
        /// <returns>合法为真</returns>
        private static bool IsValidIdFormat(string id)
{
            if (!id.StartsWith("BRIK-"))
            {
                return false;
            }
            int lastDash = id.LastIndexOf('-');
            if (lastDash <= 5)
            {
                return false;
            }
            // 类别段——大写字母 2-8 位（BRIK-{类别}-{三位序号}）
            string cat = id.Substring(5, lastDash - 5);
            if (cat.Length < 2 || cat.Length > 8)
            {
                return false;
            }
            for (int c = 0; c < cat.Length; c++)
            {
                if (cat[c] < 'A' || cat[c] > 'Z')
                {
                    return false;
                }
            }
            string seq = id.Substring(lastDash + 1);
            if (seq.Length != 3)
            {
                return false;
            }
            for (int i = 0; i < seq.Length; i++)
            {
                if (seq[i] < '0' || seq[i] > '9')
                {
                    return false;
                }
            }
            return true;
        }
        /// <summary>
        /// index --update——生成 INDEX.md + index.json（含 contract 镜像）
        /// 产品积木目录（--bricks <path>）与基座同工具链——索引写到目标目录，不碰 Mau 仓库
        /// </summary>
        /// <param name="productDir">产品积木目录（null=基座仓库根 Bricks）</param>
        /// <returns>退出码</returns>
        public static int UpdateIndex(string? productDir = null)
{
            string catalogDir;
            if (productDir != null)
            {
                catalogDir = Path.GetFullPath(productDir);
                if (!Directory.Exists(catalogDir))
                {
                    Console.WriteLine("FAIL: 产品积木目录不存在——" + catalogDir);
                    return 1;
                }
            }
            else
            {
                string? root = CliSupport.FindWorkspaceRoot();
                if (root == null)
                {
                    Console.WriteLine("FAIL: 未找到 Mau.sln");
                    return 1;
                }
                catalogDir = Path.Combine(root, "Bricks");
            }
            string indexPath = Path.Combine(catalogDir, "INDEX.md");

            // [段1] 源码扫描——文件头八字段 + 时长/线程 + Roslyn 静态签名 = 契约唯一真相源
            // （index.json 不再自引用重建：新积木/契约变更后 --update 从 Bricks/*.cs 全量提取）
            List<BrickIndexEntry> entries = ScanBrickHeaders(catalogDir);
            entries.Sort(delegate (BrickIndexEntry a, BrickIndexEntry b)
            {
                int c = string.CompareOrdinal(a.Category, b.Category);
                if (c != 0)
                {
                    return c;
                }
                return string.CompareOrdinal(a.Id, b.Id);
            });

            // [段2] INDEX.md——五维表 + 依赖列（文件头权威）
            StringBuilder md = new StringBuilder();
            md.AppendLine("# Mau 积木索引 — INDEX");
            md.AppendLine();
            md.AppendLine("> 版本：v3.1 | 创建：2026-08-04 | 更新：2026-08-07（v3.1：`mau bricks index --update` 源码驱动——文件头 + 静态签名 = 契约唯一真相源）");
            md.AppendLine("> 全量积木登记——一行一条。ID 永不重用。");
            md.AppendLine();
            md.AppendLine("## 全部积木");
            md.AppendLine();
            md.AppendLine("| ID | 名字 | 类别 | 工程路径 | 依赖 | 状态 | 来源 |");
            md.AppendLine("|:--|:--|:--|:--|:--|:--|:--|");

            int count = entries.Count;
            for (int i = 0; i < count; i++)
            {
                BrickIndexEntry e = entries[i];
                string deps = e.Dependencies.Length > 0 ? e.Dependencies : "";
                md.Append("| " + e.Id + " | " + e.Name + " | " + e.Category + " | " + e.Path + " | " + deps + " | " + e.Status + " | " + e.Source + " |");
                md.AppendLine();
            }
            md.AppendLine();
            md.AppendLine("---");
            md.AppendLine();
            md.AppendLine("_版本：v3.1 | 2026-08-07 | 自动生成——`mau bricks index --update`（源码唯一真相源：文件头 + 静态签名；来源列为人工维护区）_");
            File.WriteAllText(indexPath, md.ToString(), new UTF8Encoding(true));

            // [段3] index.json——version 3 + contract 全量（inputs/outputs/return/duration/thread 从源码提取）
            StringBuilder json = new StringBuilder();
            json.AppendLine("{");
            json.AppendLine("  \"version\": 3,");
            json.AppendLine("  \"generatedAt\": \"" + DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ") + "\",");
            // [段3a] 全部引用包列表——汇总所有积木 `包:` 声明（去重保序）
            List<string> allPackages = new List<string>();
            for (int p = 0; p < count; p++)
            {
                string[] pkgParts = entries[p].Packages.Split(';');
                for (int q = 0; q < pkgParts.Length; q++)
                {
                    string part = pkgParts[q].Trim();
                    if (part.Length == 0 || part == "无")
                    {
                        continue;
                    }
                    bool exists = false;
                    for (int r = 0; r < allPackages.Count; r++)
                    {
                        if (allPackages[r] == part)
                        {
                            exists = true;
                            break;
                        }
                    }
                    if (!exists)
                    {
                        allPackages.Add(part);
                    }
                }
            }
            json.Append("  \"packages\": [");
            for (int p = 0; p < allPackages.Count; p++)
            {
                if (p > 0)
                {
                    json.Append(", ");
                }
                json.Append("\"" + CliSupport.JsonEscape(allPackages[p]) + "\"");
            }
            json.AppendLine("],");
            json.AppendLine("  \"bricks\": [");
            for (int i = 0; i < count; i++)
            {
                BrickIndexEntry e = entries[i];
                string depText = e.Dependencies.Length > 0 ? e.Dependencies : "";
                string[] depParts = depText.Split(',');
                List<string> depList = new List<string>();
                for (int d = 0; d < depParts.Length; d = d + 1)
                {
                    string part = depParts[d].Trim();
                    if (part.Length > 0 && part != "无")
                    {
                        depList.Add(part);
                    }
                }
                // 外部包声明——文件头 `包:` 字段（包名@版本，分号分隔）
                string[] pkgParts = e.Packages.Split(';');
                List<string> pkgList = new List<string>();
                for (int p = 0; p < pkgParts.Length; p = p + 1)
                {
                    string part = pkgParts[p].Trim();
                    if (part.Length > 0 && part != "无")
                    {
                        pkgList.Add(part);
                    }
                }
                json.Append("    {\"id\":\"" + CliSupport.JsonEscape(e.Id) + "\",\"name\":\"" + CliSupport.JsonEscape(e.Name) + "\",\"category\":\"" + CliSupport.JsonEscape(e.Category) + "\",\"path\":\"" + CliSupport.JsonEscape(e.Path) + "\",\"dependencies\":[");
                for (int d = 0; d < depList.Count; d = d + 1)
                {
                    if (d > 0)
                    {
                        json.Append(",");
                    }
                    json.Append("\"" + CliSupport.JsonEscape(depList[d]) + "\"");
                }
                json.Append("],\"packages\":[");
                for (int p = 0; p < pkgList.Count; p = p + 1)
                {
                    if (p > 0)
                    {
                        json.Append(",");
                    }
                    json.Append("\"" + CliSupport.JsonEscape(pkgList[p]) + "\"");
                }
                json.Append("],\"status\":\"" + CliSupport.JsonEscape(e.Status) + "\"");
                if (e.Contract != null)
                {
                    json.Append(",\"contract\":{\"implementation\":\"" + CliSupport.JsonEscape(e.Contract.Implementation) + "\",\"inputs\":[");
                    for (int p = 0; p < e.Contract.Inputs.Count; p++)
                    {
                        if (p > 0)
                        {
                            json.Append(",");
                        }
                        json.Append("{\"name\":\"" + CliSupport.JsonEscape(e.Contract.Inputs[p].Name) + "\",\"type\":\"" + CliSupport.JsonEscape(TypeName(e.Contract.Inputs[p].Type)) + "\"}");
                    }
                    json.Append("],\"outputs\":[");
                    for (int p = 0; p < e.Contract.Outputs.Count; p++)
                    {
                        if (p > 0)
                        {
                            json.Append(",");
                        }
                        json.Append("{\"name\":\"" + CliSupport.JsonEscape(e.Contract.Outputs[p].Name) + "\",\"type\":\"" + CliSupport.JsonEscape(TypeName(e.Contract.Outputs[p].Type)) + "\"}");
                    }
                    json.Append("],\"return\":\"" + e.Contract.Return.ToString() + "\",\"duration\":\"" + e.Contract.Duration.ToString() + "\",\"thread\":\"" + CliSupport.JsonEscape(e.Contract.Thread) + "\"}");
                }
                json.Append("}");
                if (i < count - 1)
                {
                    json.Append(",");
                }
                json.AppendLine();
            }
            json.AppendLine("  ]");
            json.AppendLine("}");
            File.WriteAllText(Path.Combine(catalogDir, "index.json"), json.ToString(), new UTF8Encoding(true));

            Console.WriteLine("已生成: " + indexPath + "（" + count + " 积木）");
            Console.WriteLine("已生成: " + Path.Combine(catalogDir, "index.json") + "（v3 源码驱动）");
            return 0;
        }
        /// <summary>
        /// Type 到类型名字符串
        /// </summary>
        /// <param name="type">类型</param>
        /// <returns>类型名</returns>
        private static string TypeName(Type type)
        {
            if (type.IsArray)
            {
                return TypeName(type.GetElementType()!) + "[]";
            }
            if (type == typeof(string))
            {
                return "string";
            }
            if (type == typeof(bool))
            {
                return "bool";
            }
            if (type == typeof(int))
            {
                return "int";
            }
            if (type == typeof(long))
            {
                return "long";
            }
            if (type == typeof(double))
            {
                return "double";
            }
            return type.Name;
        }

        /// <summary>
        /// index --check-license——文件头注释块完整性
        /// </summary>
        /// <returns>退出码</returns>
        public static int CheckLicense()
        {
            string? root = CliSupport.FindWorkspaceRoot();
            if (root == null)
            {
                Console.WriteLine("FAIL: 未找到 Mau.sln");
                return 1;
            }
            List<BrickIndexEntry> entries = ScanBrickHeaders(Path.Combine(root, "Bricks"));
            int errors = 0;
            Dictionary<string, bool> seen = new Dictionary<string, bool>(StringComparer.Ordinal);
            for (int i = 0; i < entries.Count; i++)
            {
                BrickIndexEntry e = entries[i];
                if (e.FileIdText.Length == 0)
                {
                    Console.WriteLine("V8 FAIL: 文件头缺 ID 行——" + e.Name + "（" + e.Path + "）");
                    errors = errors + 1;
                }
                if (e.Dependencies.Length == 0)
                {
                    Console.WriteLine("V8 FAIL: 文件头缺 依赖: 行——" + e.Name + "（" + e.Path + "）");
                    errors = errors + 1;
                }
                if (seen.ContainsKey(e.Name))
                {
                    Console.WriteLine("V8 FAIL: 文件头积木名重复——" + e.Name);
                    errors = errors + 1;
                }
                else
                {
                    seen[e.Name] = true;
                }
            }
            if (errors == 0)
            {
                Console.WriteLine("BRICKS_LICENSE_OK (" + entries.Count + " 文件头块)");
                return 0;
            }
            Console.WriteLine("BRICKS_LICENSE_FAIL: " + errors + " 处缺失");
            return 1;
        }

        /// <summary>
        /// bricks test——全局一次性跑测（枚举→语料生成→构筑→跑测）
        /// 完整管道：T1 设计 §五——本版实现：枚举 + 最小语料生成 + 编译 + ALC 加载 + Fire/Tick + 必然结果断言
        /// 宿主桥积木（OaBrick/ToolBrick/CmdBrick/Approval/Llm/Context）需要 Configure 注入 → 全局夹具未注入 → SKIP
        /// </summary>
        /// <returns>退出码</returns>
        public static int RunGlobalTest()
{
            // [段0] 积木索引——mau test 直接调用本方法（不经 Execute）——枚举前置
            if (!EnsureIndexLoaded())
            {
                Console.WriteLine("FAIL: 积木索引不可用——未找到 Bricks/index.json");
                return 1;
            }
            int total = 0;
            int pass = 0;
            int skip = 0;
            int fail = 0;
            List<BrickContract> contracts = new List<BrickContract>();
            foreach (Mau.Translator.BrickIndexEntry brickEntry in BrickIndex.All)
            {
                contracts.Add(brickEntry.Contract);
            }
            for (int i = 0; i < contracts.Count; i++)
            {
                BrickContract c = contracts[i];
                total = total + 1;
                string result = RunOneBrick(c);
                if (result == "PASS")
                {
                    pass = pass + 1;
                    CliSupport.Detail("PASS: " + c.Name);
                }
                else if (result == "SKIP")
                {
                    skip = skip + 1;
                    CliSupport.Detail("SKIP: " + c.Name + "（" + skipReason + "）");
                }
                else
                {
                    fail = fail + 1;
                    CliSupport.Info("FAIL: " + c.Name + "（" + result + "）");
                }
            }
            CliSupport.Info("=== BRICKS_TEST 汇总 ===");
            CliSupport.Info("总计 " + total + "  通过 " + pass + "  跳过 " + skip + "  失败 " + fail);
            return fail == 0 ? 0 : 1;
        }
        /// <summary>
        /// 当前跳过原因——RunOneBrick 设置
        /// </summary>
        private static string skipReason = "";

        /// <summary>
        /// 是否宿主桥积木——实现位于 Configure 注入型积木类
        /// </summary>
        /// <param name="c">契约</param>
        /// <returns>是宿主桥为真</returns>
        private static bool IsHostBridge(BrickContract c)
{
            // 真宿主桥——实现依赖 Configure 注入的基座服务（LlmBridge 端点/密钥——无 Key 无法真跑）
            // 类名精确匹配，避免误伤静态读取积木（LlmIsToolBrick/CtxPushToolBrick 含 "ToolBrick" 子串但无 Configure 依赖）
            string impl = c.Implementation;
            return impl.Contains("LlmChatBrick.") || impl.Contains("LlmStreamBrick.")
                || impl.Contains("LlmCompletionsBrick.");
        }
        /// <summary>
        /// 跑测单个积木——生成最小 .mau 语料 → 翻译 → 编译 → ALC 加载 → Fire/Tick → 断言
        /// </summary>
        /// <param name="c">积木契约</param>
        /// <returns>PASS / FAIL(原因) / SKIP(原因)</returns>
        private static string RunOneBrick(BrickContract c)
{
            skipReason = "";
            // 宿主桥积木——需要 Configure 注入，全局夹具未注入 → SKIP
            if (IsHostBridge(c))
            {
                skipReason = "宿主桥积木——需 Configure 注入";
                return "SKIP";
            }
            // 复杂输入端口类型（非基元）——CLI 无法构造最小样例 → SKIP
            for (int p = 0; p < c.Inputs.Count; p++)
            {
                Type t = c.Inputs[p].Type;
                if (!IsPrimitivePort(t))
                {
                    skipReason = "复杂端口类型 " + t.Name + "——CLI 无法构造最小样例";
                    return "SKIP";
                }
            }
            // v2 最小语料——全符号语法 + 按端口类型生成参数字面量（信号 P_Go → S_Run Done/Failed）
            string flowName = "BrickTest" + c.Name.Replace(".", "");
            string source = CliSupport.BuildMinimalCorpusV2(c);

            // [1] 翻译（v2 门面——词法/解析/糖展开/验证/分析 + 内嵌 BRIKGROUP）
            CompileResultV2 compileResult = MauCompilerV2.Compile(source, flowName);
            if (!compileResult.Success)
            {
                return "翻译失败: " + (compileResult.Diagnostics.Count > 0 ? compileResult.Diagnostics[0].Code + ": " + compileResult.Diagnostics[0].Message : "未知");
            }

            // [2] 编译——Roslyn Emit（单文件——生成物自带内嵌积木段）
            string className = flowName;
            string pocketRoot = Path.Combine(Path.GetTempPath(), "mau_bricks_test_" + Guid.NewGuid().ToString("N").Substring(0, 8));
            try
            {
                MauPocketCompiler compiler = new MauPocketCompiler(pocketRoot);
                MauPocketCompileResult pocketResult = compiler.Compile(compileResult.GeneratedCode, className);
                if (!pocketResult.Success)
                {
                    if (pocketResult.Diagnostics.Length == 0)
                    {
                        return "编译失败: 未知";
                    }
                    string diagJoined = "";
                    for (int d = 0; d < pocketResult.Diagnostics.Length && d < 10; d = d + 1)
                    {
                        if (d > 0)
                        {
                            diagJoined = diagJoined + " | ";
                        }
                        diagJoined = diagJoined + pocketResult.Diagnostics[d];
                    }
                    return "编译失败: " + diagJoined;
                }

                // [3] 反射加载（v2 生成物——枚举状态机，不经 FlowHandle/IObservableFlow）
                System.Reflection.Assembly asm;
                object flow;
                Type flowType;
                try
                {
                    asm = System.Reflection.Assembly.LoadFrom(pocketResult.AssemblyPath)!;
                    flowType = asm.GetType("Mau.Generated." + className)!;
                    if (flowType == null)
                    {
                        return "加载失败: 未找到生成物类型 Mau.Generated." + className;
                    }
                    flow = Activator.CreateInstance(flowType)!;
                }
                catch (Exception ex)
                {
                    return "加载失败: " + ex.Message;
                }

                // [4] Fire——按信号名反射调用（FireGo），基元参数生成最小样例
                System.Reflection.MethodInfo? fire = flowType.GetMethod("FireGo");
                if (fire == null)
                {
                    return "FireGo 未生成";
                }
                System.Reflection.ParameterInfo[] ps = fire.GetParameters();
                object?[] args = new object?[ps.Length];
                for (int a = 0; a < ps.Length; a++)
                {
                    args[a] = DefaultValue(ps[a].ParameterType);
                }
                try
                {
                    fire.Invoke(flow, args);
                }
                catch (Exception ex)
                {
                    return "Fire 调用失败: " + ex.Message;
                }

                // [5] Tick 60 帧——结构冒烟断言（IsRunDone 或 IsRunFailed 任一成立 = 控制律执行、积木被调用）
                // 行为正确性归 L4 测试——全局跑测只验证"能被语料调用 + 不崩溃"
                System.Reflection.MethodInfo? tick = flowType.GetMethod("Tick");
                System.Reflection.MethodInfo? isDone = flowType.GetMethod("IsRunDone");
                System.Reflection.MethodInfo? isFailed = flowType.GetMethod("IsRunFailed");
                if (tick == null || isDone == null || isFailed == null)
                {
                    return "生成物缺 Tick/IsRunDone/IsRunFailed";
                }
                bool settled = false;
                for (int t = 0; t < 60; t = t + 1)
                {
                    tick.Invoke(flow, new object[] { t });
                    if ((bool)isDone.Invoke(flow, null)! || (bool)isFailed.Invoke(flow, null)!)
                    {
                        settled = true;
                        break;
                    }
                }
                if (!settled)
                {
                    return "变迁未结算（前置未触发或 Cube 未完成）";
                }
                return "PASS";
            }
            catch (Exception ex)
            {
                return "运行时异常: " + ex.Message;
            }
            finally
            {
                try
                {
                    if (Directory.Exists(pocketRoot))
                    {
                        Directory.Delete(pocketRoot, true);
                    }
                }
                catch
                {
                    // 清理失败不影响结果
                }
            }
        }
        /// <summary>
        /// 是否基元端口——全局跑测可构造最小样例的类型
        /// </summary>
        /// <param name="t">端口类型</param>
        /// <returns>基元为真</returns>
        private static bool IsPrimitivePort(Type t)
{
    if (t == typeof(string))
    {
        return true;
    }
    if (t == typeof(int))
    {
        return true;
    }
    if (t == typeof(long))
    {
        return true;
    }
    if (t == typeof(bool))
    {
        return true;
    }
    if (t == typeof(double))
    {
        return true;
    }
    // 数组端口——元素基元即可构造（B2 数组字面量已落地——E2 解锁 cmd.register/oa.claim 等）
    if (t.IsArray)
    {
        return IsPrimitivePort(t.GetElementType()!);
    }
    return false;
}
        /// <summary>
        /// 生成基元默认值——最小样例
        /// </summary>
        /// <param name="t">参数类型</param>
        /// <returns>默认值对象</returns>
        private static object? DefaultValue(Type t)
{
    if (t == typeof(string))
    {
        // 唯一值——隔离共享参数状态（判例：ctx_push_assistant_tool_calls 写 ToolCallsJson="t" → ctx_build_messages_json 解析失败）
        // 路径参数（file.write 等）落系统临时目录——不污染仓库根（判例：v0.52 误提交 8 个 t-* 文件）
        return Path.Combine(Path.GetTempPath(), "t-" + Guid.NewGuid().ToString("N").Substring(0, 8));
    }
    if (t == typeof(int))
    {
        return 1;
    }
    if (t == typeof(long))
    {
        return 1L;
    }
    if (t == typeof(bool))
    {
        return true;
    }
    if (t == typeof(double))
    {
        return 1.0;
    }
    // 数组端口——单元素数组（元素用基元默认值）——E2 数组跑测解锁
    if (t.IsArray)
    {
        Type elemType = t.GetElementType()!;
        Array arr = Array.CreateInstance(elemType, 1);
        arr.SetValue(DefaultValue(elemType), 0);
        return arr;
    }
    if (t.IsValueType)
    {
        return Activator.CreateInstance(t);
    }
    return null;
}/// <summary>
/// 确保积木索引已加载——探测顺序：环境变量 MAU_BRICKS_ROOT → 当前目录向上 → 程序集目录向上
/// </summary>
/// <returns>索引可用</returns>
public static bool EnsureIndexLoaded()
{
    if (BrickIndex.Count > 0)
    {
        return true;
    }

    string? probe = Environment.GetEnvironmentVariable("MAU_BRICKS_ROOT");
    if (!string.IsNullOrWhiteSpace(probe) && BrickIndex.Load(probe))
    {
        return true;
    }

    string? dir = Directory.GetCurrentDirectory();
    while (dir != null)
    {
        if (BrickIndex.Load(Path.Combine(dir, "Bricks")))
        {
            return true;
        }

        dir = Path.GetDirectoryName(dir);
    }

    dir = AppContext.BaseDirectory;
    while (dir != null)
    {
        if (BrickIndex.Load(Path.Combine(dir, "Bricks")))
        {
            return true;
        }

        dir = Path.GetDirectoryName(dir);
    }

    return false;
}    /// <summary>
/// 解析 PACK 方法声明行——"方法: excel.read → path,sheet,format"（V11 校验）
/// </summary>
/// <param name = "line">方法声明行（不含 方法: 前缀）</param>
/// <param name = "result">方法→参数列表 映射</param>
private static void ParsePackMethodLine(string line, Dictionary<string, List<string>> result)
{
    int arrow = line.IndexOf("→", StringComparison.Ordinal);
    if (arrow <= 0)
    {
        return;
    }

    string method = line.Substring(0, arrow).Trim();
    string paramsText = line.Substring(arrow + 1).Trim();
    List<string> ps = new List<string>();
    if (paramsText.Length > 0)
    {
        string[] parts = paramsText.Split(',');
        for (int i = 0; i < parts.Length; i++)
        {
            string p = parts[i].Trim();
            if (p.Length > 0)
            {
                ps.Add(p);
            }
        }
    }

    result[method] = ps;
} 
/// <summary>
/// 是否合法 JSON 键名——首字符字母/下划线，其余字母数字/下划线（V11 校验）
/// </summary>
/// <param name="key">键名</param>
/// <returns>合法为真</returns>
private static bool IsJsonKeyName(string key)
{
    if (key.Length == 0)
    {
        return false;
    }
    char first = key[0];
    if (!(first >= 'a' && first <= 'z') && !(first >= 'A' && first <= 'Z') && first != '_')
    {
        return false;
    }
    for (int i = 1; i < key.Length; i++)
    {
        char c = key[i];
        if (!(c >= 'a' && c <= 'z') && !(c >= 'A' && c <= 'Z') && !(c >= '0' && c <= '9') && c != '_')
        {
            return false;
        }
    }
    return true;
}

}
}
