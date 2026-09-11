using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Mau.Development
{
#pragma warning disable CS8600 // 环境 SDK 对 ConcurrentDictionary/Dictionary TryXXX(out TValue) 的 MaybeNullWhen 分析异常误报——out 实参非空变量也报警（最小复现验证）
    /// <summary>
    /// MauRoslynBridge 核心——项目加载 / 快照监管 / 全扫对账 / 引用集 / 编译构建（partial 分部）。
    /// </summary>
    public sealed partial class MauRoslynBridge
    {
        /// <summary>
        /// 获取项目缓存——命中 touch LRU；未命中串行加载（Parse 全树 10 秒级一次）
        /// </summary>
        /// <param name="csproj">csproj 绝对路径</param>
        /// <returns>项目缓存</returns>
        private ProjectCache EnsureProject(string csproj)
        {
            ProjectCache cache = null!;
            if (_pool.TryGetValue(csproj, out cache))
            {
                cache.LastAccess = Environment.TickCount64;
                return cache;
            }
            lock (_poolGate)
            {
                if (_pool.TryGetValue(csproj, out cache))
                {
                    cache.LastAccess = Environment.TickCount64;
                    return cache;
                }
                ProjectCache created = LoadProject(csproj);
                _pool[csproj] = created;
                EvictIfNeeded(csproj);
                return created;
            }
        }

        /// <summary>
        /// 加载项目——解析 csproj + 收集源文件 + Parse 全树 + 构建引用集 + 编译
        /// </summary>
        /// <param name="csproj">csproj 绝对路径</param>
        /// <returns>项目缓存（已就绪）</returns>
        private ProjectCache LoadProject(string csproj)
        {
            ProjectCache cache = new ProjectCache();
            cache.CsprojPath = csproj;
            cache.ProjectDir = Path.GetDirectoryName(csproj) ?? "";
            ParseCsproj(cache);
            cache.SourceFiles = CollectSources(cache);
            for (int i = 0; i < cache.SourceFiles.Length; i = i + 1)
            {
                string filePath = cache.SourceFiles[i];
                SyntaxTree tree = ParseFile(filePath);
                cache.Trees[filePath] = tree;
                cache.Stamps[filePath] = SnapshotOf(filePath);
            }
            cache.References = BuildReferences(cache);
            cache.Compilation = BuildCompilation(cache);
            return cache;
        }

        /// <summary>
        /// 解析 csproj——AssemblyName / Tfm / Nullable / OutputType（XML 直接读取，缺省默认）
        /// </summary>
        /// <param name="cache">缓存条目（写属性）</param>
        private void ParseCsproj(ProjectCache cache)
        {
            cache.AssemblyName = Path.GetFileNameWithoutExtension(cache.CsprojPath);
            cache.Tfm = "net8.0";
            cache.NullableEnable = false;
            cache.IsExe = false;
            cache.DefaultExcludes = new List<string>();
            try
            {
                XDocument doc = XDocument.Load(cache.CsprojPath);
                foreach (XElement property in doc.Descendants("PropertyGroup"))
                {
                    XElement? assemblyName = property.Element("AssemblyName");
                    if (assemblyName != null && !string.IsNullOrWhiteSpace(assemblyName.Value))
                    {
                        cache.AssemblyName = assemblyName.Value.Trim();
                    }
                    XElement? tfm = property.Element("TargetFramework");
                    if (tfm != null && !string.IsNullOrWhiteSpace(tfm.Value))
                    {
                        cache.Tfm = tfm.Value.Trim();
                    }
                    XElement? nullable = property.Element("Nullable");
                    if (nullable != null)
                    {
                        string value = nullable.Value.Trim();
                        if (value == "enable" || value == "annotations")
                        {
                            cache.NullableEnable = true;
                        }
                    }
                    XElement? outputType = property.Element("OutputType");
                    if (outputType != null)
                    {
                        string value = outputType.Value.Trim();
                        if (value == "Exe" || value == "WinExe")
                        {
                            cache.IsExe = true;
                        }
                    }
                    // DefaultItemExcludes——SDK 默认编译排除（子项目目录）；分号分割条目
                    XElement? excludes = property.Element("DefaultItemExcludes");
                    if (excludes != null && !string.IsNullOrWhiteSpace(excludes.Value))
                    {
                        string[] parts = excludes.Value.Split(';');
                        for (int e = 0; e < parts.Length; e = e + 1)
                        {
                            string item = parts[e].Trim();
                            if (item.Length > 0 && !cache.DefaultExcludes.Contains(item))
                            {
                                cache.DefaultExcludes.Add(item);
                            }
                        }
                    }
                }
            }
            catch (Exception)
            {
                // 解析失败保留默认——语义诊断可能噪音，cs.build 权威兜底
            }
        }

        /// <summary>
        /// 收集源文件——全部 .cs（排除 obj/bin 目录 + csproj DefaultItemExcludes 子项目目录）
        /// </summary>
        /// <param name="cache">缓存条目（ProjectDir + DefaultExcludes）</param>
        /// <returns>绝对路径数组</returns>
        private string[] CollectSources(ProjectCache cache)
        {
            List<string> result = new List<string>();
            string[] all = Directory.GetFiles(cache.ProjectDir, "*.cs", SearchOption.AllDirectories);
            // DefaultExcludes 预编译——目录前缀排除（X\** → X\ 前缀；$( 展开引用跳过）
            List<string> dirPrefixes = new List<string>();
            for (int i = 0; i < cache.DefaultExcludes.Count; i = i + 1)
            {
                string item = cache.DefaultExcludes[i];
                if (item.StartsWith("$(", StringComparison.Ordinal))
                {
                    continue;
                }
                string norm = item.Replace('/', '\\');
                if (norm.EndsWith("\\**", StringComparison.Ordinal))
                {
                    dirPrefixes.Add(norm.Substring(0, norm.Length - 2));
                }
            }
            for (int i = 0; i < all.Length; i = i + 1)
            {
                string path = all[i];
                if (path.IndexOf("\\obj\\", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    path.IndexOf("/obj/", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    continue;
                }
                if (path.IndexOf("\\bin\\", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    path.IndexOf("/bin/", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    continue;
                }
                string rel = path.Substring(cache.ProjectDir.Length).TrimStart('\\', '/');
                bool excluded = false;
                for (int d = 0; d < dirPrefixes.Count; d = d + 1)
                {
                    if (rel.StartsWith(dirPrefixes[d], StringComparison.OrdinalIgnoreCase))
                    {
                        excluded = true;
                        break;
                    }
                }
                if (excluded)
                {
                    continue;
                }
                result.Add(Path.GetFullPath(path));
            }
            result.Sort(StringComparer.OrdinalIgnoreCase);
            return result.ToArray();
        }

        /// <summary>
        /// 从旧树创建带新根的树——CSharpSyntaxTree.Create（WithRoot 在工具编译器 Roslyn 集不可用的替代）
        /// </summary>
        /// <param name="source">源树（路径/选项继承）</param>
        /// <param name="newRoot">新根（CompilationUnit）</param>
        /// <returns>新树</returns>
        private static SyntaxTree CreateTreeFromRoot(SyntaxTree source, SyntaxNode newRoot)
        {
            CompilationUnitSyntax? unit = newRoot as CompilationUnitSyntax;
            if (unit == null)
            {
                throw new InvalidOperationException("newRoot 不是 CompilationUnitSyntax——结构异常");
            }
            return CSharpSyntaxTree.Create(unit, ParseOptions(), source.FilePath, null);
        }

        /// <summary>
        /// 单文件 ParseText——UTF-8 读取 + 路径伴随
        /// </summary>
        /// <param name="filePath">文件绝对路径</param>
        /// <returns>语法树</returns>
        private static SyntaxTree ParseFile(string filePath)
        {
            string text = File.ReadAllText(filePath);
            return CSharpSyntaxTree.ParseText(text, ParseOptions(), filePath);
        }

        /// <summary>
        /// 解析选项——C# 12（net8 默认语言版本）
        /// </summary>
        /// <returns>解析选项</returns>
        private static CSharpParseOptions ParseOptions()
        {
            return new CSharpParseOptions(LanguageVersion.CSharp12);
        }

        /// <summary>
        /// 文件快照采集
        /// </summary>
        /// <param name="filePath">文件路径</param>
        /// <returns>签名</returns>
        private static ProjectSnapshot SnapshotOf(string filePath)
        {
            ProjectSnapshot snapshot;
            snapshot.Utc = File.GetLastWriteTimeUtc(filePath);
            snapshot.Length = new FileInfo(filePath).Length;
            return snapshot;
        }

        /// <summary>
        /// 项目级全扫——重扫源文件集合（增删检测）+ 全文件快照对账（批量刷新）
        /// 语义操作（check/find_ref/dead/rename）前调用；树级工具也调用（低成本，百文件 &lt;2ms 元数据）
        /// </summary>
        /// <param name="cache">缓存</param>
        private void FullScan(ProjectCache cache)
        {
            lock (cache.Gate)
            {
                if (cache.Compilation == null || cache.References.Count == 0)
                {
                    // 轻量缓存（cs.build 直建未 Parse/未建引用集）——补真实引用集 + 编译（bin 产物已存在）
                    cache.References = BuildReferences(cache);
                    cache.Compilation = BuildCompilation(cache);
                }
                string[] currentSources = CollectSources(cache);
                HashSet<string> currentSet = new HashSet<string>(currentSources, StringComparer.OrdinalIgnoreCase);
                List<string> removedFiles = new List<string>();
                for (int i = 0; i < cache.SourceFiles.Length; i = i + 1)
                {
                    if (!currentSet.Contains(cache.SourceFiles[i]))
                    {
                        removedFiles.Add(cache.SourceFiles[i]);
                    }
                }
                for (int i = 0; i < removedFiles.Count; i = i + 1)
                {
                    SyntaxTree? removedTree;
                    cache.Trees.TryRemove(removedFiles[i], out removedTree);
                    ProjectSnapshot removedStamp;
                    cache.Stamps.TryRemove(removedFiles[i], out removedStamp);
                    SemanticModel removedSem = null!;
                    cache.Semantics.TryRemove(removedFiles[i], out removedSem);
                }
                List<SyntaxTree> addTrees = new List<SyntaxTree>();
                List<SyntaxTree> replaceOld = new List<SyntaxTree>();
                List<SyntaxTree> replaceNew = new List<SyntaxTree>();
                for (int i = 0; i < currentSources.Length; i = i + 1)
                {
                    string filePath = currentSources[i];
                    SyntaxTree existing;
                    if (!cache.Trees.TryGetValue(filePath, out existing))
                    {
                        // 新增文件
                        SyntaxTree fresh = ParseFile(filePath);
                        cache.Trees[filePath] = fresh;
                        cache.Stamps[filePath] = SnapshotOf(filePath);
                        addTrees.Add(fresh);
                    }
                    else
                    {
                        ProjectSnapshot currentSnapshot = SnapshotOf(filePath);
                        bool changed = true;
                        ProjectSnapshot known;
                        if (cache.Stamps.TryGetValue(filePath, out known))
                        {
                            // 快照对账——mtime+size 全等 = 未变（增量刷新 D3）；错位风险（mtime 异常/为 0）→ 宁可全量重解析
                            bool stampsValid = known.Utc > DateTime.MinValue && currentSnapshot.Utc > DateTime.MinValue;
                            if (stampsValid && known.Utc == currentSnapshot.Utc && known.Length == currentSnapshot.Length)
                            {
                                changed = false;
                            }
                        }
                        if (changed)
                        {
                            SyntaxTree fresh = ParseFile(filePath);
                            cache.Trees[filePath] = fresh;
                            cache.Stamps[filePath] = currentSnapshot;
                            SemanticModel removedSem = null!;
                            cache.Semantics.TryRemove(filePath, out removedSem);
                            replaceOld.Add(existing);
                            replaceNew.Add(fresh);
                        }
                    }
                }
                cache.SourceFiles = currentSources;
                bool anyChange = addTrees.Count > 0 || replaceOld.Count > 0 || removedFiles.Count > 0;
                if (anyChange)
                {
                    CSharpCompilation updated = cache.Compilation;
                    if (addTrees.Count > 0)
                    {
                        updated = (CSharpCompilation)updated.AddSyntaxTrees(addTrees);
                    }
                    for (int i = 0; i < replaceOld.Count; i = i + 1)
                    {
                        updated = (CSharpCompilation)updated.ReplaceSyntaxTree(replaceOld[i], replaceNew[i]);
                    }
                    cache.Compilation = updated;
                    cache.Semantics.Clear();
                }
            }
        }

        /// <summary>
        /// 构建引用集——bin 产物（莎拍板 A）+ TPA + 共享框架探测（AspNetCore/WindowsDesktop）；按名去重（bin 优先）
        /// </summary>
        /// <param name="cache">缓存</param>
        /// <returns>引用列表（bin 缺失时返回空列表——check 引导 build）</returns>
        private List<MetadataReference> BuildReferences(ProjectCache cache)
        {
            List<MetadataReference> references = new List<MetadataReference>();
            Dictionary<string, string> pathsByName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            // TPA——运行时平台程序集
            string? tpaRaw = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string;
            if (tpaRaw != null)
            {
                string[] parts = tpaRaw.Split(';');
                for (int i = 0; i < parts.Length; i = i + 1)
                {
                    if (parts[i].Length == 0)
                    {
                        continue;
                    }
                    string name = Path.GetFileNameWithoutExtension(parts[i]);
                    if (!pathsByName.ContainsKey(name))
                    {
                        pathsByName[name] = parts[i];
                    }
                }
            }
            // 共享框架探测——AspNetCore + WindowsDesktop（WinForms/WPF 程序集所在；按项目 TFM 主版本匹配 + 跳过 native dll）
            string? runtimeDir = Path.GetDirectoryName(typeof(object).Assembly.Location);
            if (runtimeDir != null)
            {
                string? netCoreAppDir = Path.GetDirectoryName(runtimeDir);
                if (netCoreAppDir != null)
                {
                    string sharedDir = Path.GetDirectoryName(netCoreAppDir) ?? "";
                    ProbeSharedFramework(pathsByName, sharedDir, "Microsoft.AspNetCore.App", cache.Tfm);
                    ProbeSharedFramework(pathsByName, sharedDir, "Microsoft.WindowsDesktop.App", cache.Tfm);
                }
            }
            // bin 产物——目标项目已 build 输出（优先于 TPA/框架）
            string? outputDir = FindOutputDir(cache);
            if (outputDir != null)
            {
                string[] dlls = Directory.GetFiles(outputDir, "*.dll", SearchOption.TopDirectoryOnly);
                Array.Sort(dlls, StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < dlls.Length; i = i + 1)
                {
                    string name = Path.GetFileNameWithoutExtension(dlls[i]);
                    if (string.Equals(name, cache.AssemblyName, StringComparison.OrdinalIgnoreCase))
                    {
                        continue; // 排除目标程序集自身——避免 CS0436 源/引用冲突
                    }
                    pathsByName[name] = dlls[i]; // bin 优先覆盖
                }
            }
            foreach (KeyValuePair<string, string> pair in pathsByName)
            {
                try
                {
                    references.Add(MetadataReference.CreateFromFile(pair.Value));
                }
                catch (Exception)
                {
                    // 单个引用失败跳过——编译器会报缺失引用而非崩溃
                }
            }
            return references;
        }

        /// <summary>
        /// 共享框架探测——dotnet/shared/&lt;框架名&gt;/ 按项目 TFM 主版本匹配（net8.0-windows → 8.0.x）的托管 dll 并入引用集（按名去重）
        /// </summary>
        /// <param name="pathsByName">按名路径表（写）</param>
        /// <param name="sharedDir">shared 根目录</param>
        /// <param name="frameworkName">框架目录名（Microsoft.AspNetCore.App / Microsoft.WindowsDesktop.App）</param>
        /// <param name="tfm">项目 TFM（如 net8.0-windows）——主版本不匹配则跳过该框架</param>
        private static void ProbeSharedFramework(Dictionary<string, string> pathsByName, string sharedDir, string frameworkName, string tfm)
        {
            string frameworkDir = Path.Combine(sharedDir, frameworkName);
            if (!Directory.Exists(frameworkDir))
            {
                return;
            }
            // TFM 主版本提取——net8.0-windows → "8.0"；解析失败跳过（宁缺勿错——引入错误版本 = CS1705 洪水）
            int dash = tfm.IndexOf('-');
            string baseTfm = dash >= 0 ? tfm.Substring(0, dash) : tfm;
            string netPart = baseTfm.StartsWith("net", StringComparison.OrdinalIgnoreCase) ? baseTfm.Substring(3) : "";
            if (netPart.Length == 0)
            {
                return;
            }
            // 匹配框架目录前缀（8.0. 匹配 8.0.x；9.0. 匹配 9.0.x）——取匹配中最高补丁
            string[] versions = Directory.GetDirectories(frameworkDir);
            string? best = null;
            for (int i = 0; i < versions.Length; i = i + 1)
            {
                string v = Path.GetFileName(versions[i]);
                if (v.StartsWith(netPart + ".", StringComparison.OrdinalIgnoreCase))
                {
                    if (best == null || string.CompareOrdinal(v, Path.GetFileName(best)) > 0)
                    {
                        best = versions[i];
                    }
                }
            }
            if (best == null)
            {
                return;
            }
            string[] dlls = Directory.GetFiles(best, "*.dll", SearchOption.TopDirectoryOnly);
            for (int i = 0; i < dlls.Length; i = i + 1)
            {
                string name = Path.GetFileNameWithoutExtension(dlls[i]);
                if (!pathsByName.ContainsKey(name))
                {
                    // 跳过 native dll（wpfgfx_cor3 等无托管元数据——CS0009）
                    if (!IsManagedAssembly(dlls[i]))
                    {
                        continue;
                    }
                    pathsByName[name] = dlls[i];
                }
            }
        }

        /// <summary>
        /// 托管程序集判定——PE 文件含 CLI 元数据（跳过 native dll）
        /// </summary>
        /// <param name="path">dll 路径</param>
        /// <returns>true=托管程序集</returns>
        private static bool IsManagedAssembly(string path)
        {
            try
            {
                using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (System.Reflection.PortableExecutable.PEReader pe = new System.Reflection.PortableExecutable.PEReader(fs))
                {
                    return pe.HasMetadata;
                }
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// 查找目标项目 bin 输出目录——扫描 bin/ 下含 &lt;AssemblyName&gt;.dll/.exe 的目录，取最新写入
        /// </summary>
        /// <param name="cache">缓存</param>
        /// <returns>输出目录绝对路径（无产物返回 null）</returns>
        private string? FindOutputDir(ProjectCache cache)
        {
            string binRoot = Path.Combine(cache.ProjectDir, "bin");
            if (!Directory.Exists(binRoot))
            {
                return null;
            }
            string[] candidateDirs = Directory.GetDirectories(binRoot, "*", SearchOption.AllDirectories);
            string? bestDir = null;
            DateTime bestTime = DateTime.MinValue;
            for (int i = 0; i < candidateDirs.Length; i = i + 1)
            {
                string dir = candidateDirs[i];
                string dllPath = Path.Combine(dir, cache.AssemblyName + ".dll");
                if (File.Exists(dllPath))
                {
                    DateTime t = File.GetLastWriteTimeUtc(dllPath);
                    if (t > bestTime)
                    {
                        bestTime = t;
                        bestDir = dir;
                    }
                    continue;
                }
                string exePath = Path.Combine(dir, cache.AssemblyName + ".exe");
                if (File.Exists(exePath))
                {
                    DateTime t = File.GetLastWriteTimeUtc(exePath);
                    if (t > bestTime)
                    {
                        bestTime = t;
                        bestDir = dir;
                    }
                }
            }
            return bestDir;
        }

        /// <summary>
        /// 构建编译——树集合 + 引用集 + 项目选项（OutputKind / Nullable）
        /// </summary>
        /// <param name="cache">缓存</param>
        /// <returns>编译对象</returns>
        private static CSharpCompilation BuildCompilation(ProjectCache cache)
        {
            OutputKind kind = cache.IsExe ? OutputKind.ConsoleApplication : OutputKind.DynamicallyLinkedLibrary;
            CSharpCompilationOptions options = new CSharpCompilationOptions(kind);
            if (cache.NullableEnable)
            {
                options = options.WithNullableContextOptions(NullableContextOptions.Enable);
            }
            else
            {
                options = options.WithNullableContextOptions(NullableContextOptions.Disable);
            }
            return CSharpCompilation.Create(cache.AssemblyName, cache.Trees.Values, cache.References, options);
        }

        /// <summary>
        /// 语义准备——引用集脏时重建编译；确保语义模型可用（惰性生成 + 缓存）
        /// </summary>
        /// <param name="cache">缓存</param>
        /// <param name="filePath">文件路径</param>
        /// <returns>语义模型</returns>
        private SemanticModel EnsureSemantics(ProjectCache cache, string filePath)
        {
            SemanticModel? model;
            if (cache.Semantics.TryGetValue(filePath, out model))
            {
                return model;
            }
            lock (cache.Gate)
            {
                if (cache.ReferencesDirty)
                {
                    cache.References = BuildReferences(cache);
                    cache.Compilation = BuildCompilation(cache);
                    cache.Semantics.Clear();
                    cache.ReferencesDirty = false;
                }
                if (cache.Semantics.TryGetValue(filePath, out model))
                {
                    return model;
                }
                SyntaxTree tree;
                cache.Trees.TryGetValue(filePath, out tree);
                if (tree == null)
                {
                    tree = ParseFile(filePath);
                    cache.Trees[filePath] = tree;
                    cache.Stamps[filePath] = SnapshotOf(filePath);
                }
                SemanticModel created = cache.Compilation.GetSemanticModel(tree);
                cache.Semantics[filePath] = created;
                return created;
            }
        }

        /// <summary>
        /// 按类名定位类声明——全部树搜索（partial 合并：返回第一个匹配 + 同名类列表）
        /// </summary>
        /// <param name="cache">缓存</param>
        /// <param name="className">类名</param>
        /// <param name="foundTree">命中树</param>
        /// <param name="foundNode">命中声明节点</param>
        /// <param name="total">全部同名声明数（partial 提示）</param>
        /// <returns>是否找到</returns>
        private static bool FindClassNode(ProjectCache cache, string className, out SyntaxTree foundTree, out ClassDeclarationSyntax foundNode, out int total)
        {
            foundTree = null!;
            foundNode = null!;
            total = 0;
            foreach (KeyValuePair<string, SyntaxTree> pair in cache.Trees)
            {
                SyntaxNode root = pair.Value.GetRoot();
                foreach (SyntaxNode node in root.DescendantNodes())
                {
                    ClassDeclarationSyntax? decl = node as ClassDeclarationSyntax;
                    if (decl != null && decl.Identifier.Text == className)
                    {
                        total = total + 1;
                        if (foundNode == null)
                        {
                            foundTree = pair.Value;
                            foundNode = decl;
                        }
                    }
                }
            }
            return foundNode != null;
        }

        /// <summary>
        /// 定位方法声明——类内按名；重载歧义检测（返回全部签名供 LLM 区分）
        /// </summary>
        /// <param name="classNode">类声明</param>
        /// <param name="methodName">方法名</param>
        /// <param name="found">命中方法</param>
        /// <param name="signatures">全部同名签名（歧义时输出）</param>
        /// <returns>0=唯一命中 1=无 2=歧义</returns>
        private static int FindMethodInClass(ClassDeclarationSyntax classNode, string methodName, out MethodDeclarationSyntax found, out List<string> signatures)
        {
            found = null!;
            signatures = new List<string>();
            List<MethodDeclarationSyntax> matches = new List<MethodDeclarationSyntax>();
            foreach (SyntaxNode node in classNode.DescendantNodes())
            {
                MethodDeclarationSyntax? method = node as MethodDeclarationSyntax;
                if (method != null && method.Identifier.Text == methodName)
                {
                    matches.Add(method);
                }
            }
            for (int i = 0; i < matches.Count; i = i + 1)
            {
                signatures.Add(matches[i].ParameterList.ToString());
            }
            if (matches.Count == 0)
            {
                return 1;
            }
            if (matches.Count > 1)
            {
                return 2;
            }
            found = matches[0];
            return 0;
        }

        /// <summary>
        /// 定位成员节点——字段/属性/方法/构造函数（按名）；memberName 支持签名后缀（如 SubmitChoice(int)）区分重载；
        /// 构造函数 = ".ctor" 或类名；歧义返回候选签名列表
        /// </summary>
        /// <param name="classNode">类声明</param>
        /// <param name="memberName">成员名（签名后缀可选；.ctor/类名=构造函数）</param>
        /// <param name="found">命中节点</param>
        /// <param name="foundKind">命中类别（方法/字段/属性/构造函数）</param>
        /// <param name="count">同名数量</param>
        /// <param name="candidates">候选签名列表（歧义时填充——AMBIGUOUS 提示用）</param>
        /// <returns>是否唯一命中</returns>
        private static bool FindMemberInClass(ClassDeclarationSyntax classNode, string memberName, out SyntaxNode found, out string foundKind, out int count, out List<string> candidates)
        {
            found = null!;
            foundKind = "";
            count = 0;
            candidates = new List<string>();
            string pureName = memberName;
            string signature = "";
            int paren = memberName.IndexOf('(');
            if (paren >= 0)
            {
                pureName = memberName.Substring(0, paren).Trim();
                signature = memberName.Substring(paren);
            }
            bool ctorRequest = pureName == ".ctor" || pureName == classNode.Identifier.Text;
            List<SyntaxNode> matches = new List<SyntaxNode>();
            foreach (SyntaxNode node in classNode.DescendantNodes())
            {
                MethodDeclarationSyntax? method = node as MethodDeclarationSyntax;
                if (method != null && method.Identifier.Text == pureName)
                {
                    if (signature.Length == 0 || SignatureMatches(method.ParameterList.ToString(), signature))
                    {
                        matches.Add(method);
                    }
                    continue;
                }
                ConstructorDeclarationSyntax? ctor = node as ConstructorDeclarationSyntax;
                if (ctor != null && ctorRequest)
                {
                    if (signature.Length == 0 || SignatureMatches(ctor.ParameterList.ToString(), signature))
                    {
                        matches.Add(ctor);
                    }
                    continue;
                }
                FieldDeclarationSyntax? field = node as FieldDeclarationSyntax;
                if (field != null)
                {
                    foreach (VariableDeclaratorSyntax variable in field.Declaration.Variables)
                    {
                        if (variable.Identifier.Text == memberName)
                        {
                            matches.Add(field);
                        }
                    }
                    continue;
                }
                PropertyDeclarationSyntax? property = node as PropertyDeclarationSyntax;
                if (property != null && property.Identifier.Text == memberName)
                {
                    matches.Add(property);
                }
            }
            count = matches.Count;
            if (matches.Count == 0)
            {
                return false;
            }
            if (matches.Count > 1)
            {
                for (int i = 0; i < matches.Count; i = i + 1)
                {
                    MethodDeclarationSyntax? overloadMethod = matches[i] as MethodDeclarationSyntax;
                    if (overloadMethod != null)
                    {
                        candidates.Add(overloadMethod.Identifier.Text + overloadMethod.ParameterList.ToString());
                        continue;
                    }
                    ConstructorDeclarationSyntax? overloadCtor = matches[i] as ConstructorDeclarationSyntax;
                    if (overloadCtor != null)
                    {
                        candidates.Add(".ctor" + overloadCtor.ParameterList.ToString());
                        continue;
                    }
                    candidates.Add(FindMemberLabel(matches[i]));
                }
                return false;
            }
            found = matches[0];
            if (found is MethodDeclarationSyntax)
            {
                foundKind = "method";
            }
            else if (found is ConstructorDeclarationSyntax)
            {
                foundKind = "constructor";
            }
            else if (found is FieldDeclarationSyntax)
            {
                foundKind = "field";
            }
            else
            {
                foundKind = "property";
            }
            return true;
        }

        /// <summary>
        /// 签名匹配——按参数类型序列比较（"(int, string)" 与 "(int choice, string name)" 等价；泛型/修饰符/数组保形）
        /// </summary>
        /// <param name="parameterListText">实际参数列表文本（ParameterList.ToString()）</param>
        /// <param name="requestedSignature">请求签名（member 后缀，如 "(int, string)"）</param>
        /// <returns>类型序列是否一致</returns>
        private static bool SignatureMatches(string parameterListText, string requestedSignature)
        {
            return ExtractParameterTypes(parameterListText) == ExtractParameterTypes(requestedSignature);
        }

        /// <summary>
        /// 提取参数类型序列——剥参数名/默认值/空白，保留类型与修饰符（ref/out/params）
        /// </summary>
        /// <param name="paramListText">参数列表文本（含括号）</param>
        /// <returns>逗号连接的类型序列（空参返回空串）</returns>
        private static string ExtractParameterTypes(string paramListText)
        {
            string inner = paramListText.Trim();
            if (inner.StartsWith("(", StringComparison.Ordinal))
            {
                inner = inner.Substring(1);
            }
            if (inner.EndsWith(")", StringComparison.Ordinal))
            {
                inner = inner.Substring(0, inner.Length - 1);
            }
            if (inner.Trim().Length == 0)
            {
                return "";
            }
            List<string> parts = SplitTopLevelArgs(inner);
            List<string> types = new List<string>();
            for (int i = 0; i < parts.Count; i = i + 1)
            {
                string p = parts[i].Trim();
                int eq = p.IndexOf('=');
                if (eq >= 0)
                {
                    p = p.Substring(0, eq).Trim();
                }
                int lastSpace = p.LastIndexOf(' ');
                if (lastSpace > 0)
                {
                    string candidate = p.Substring(lastSpace + 1).Trim();
                    if (candidate.Length > 0 && (char.IsLetter(candidate[0]) || candidate[0] == '_'))
                    {
                        p = p.Substring(0, lastSpace).Trim();
                    }
                }
                types.Add(p.Replace(" ", "").Replace("\t", ""));
            }
            return string.Join(",", types);
        }

        /// <summary>
        /// 顶层逗号分割——感知泛型尖括号/元组圆括号嵌套（Func&lt;int, string&gt; 与 (int, string) 内逗号不分隔）
        /// </summary>
        /// <param name="text">参数列表内文本（已剥括号）</param>
        /// <returns>顶层片段列表</returns>
        private static List<string> SplitTopLevelArgs(string text)
        {
            List<string> parts = new List<string>();
            int angle = 0;
            int paren = 0;
            int start = 0;
            for (int i = 0; i < text.Length; i = i + 1)
            {
                char c = text[i];
                if (c == '<')
                {
                    angle = angle + 1;
                }
                else if (c == '>')
                {
                    angle = angle - 1;
                }
                else if (c == '(')
                {
                    paren = paren + 1;
                }
                else if (c == ')')
                {
                    paren = paren - 1;
                }
                else if (c == ',' && angle == 0 && paren == 0)
                {
                    parts.Add(text.Substring(start, i - start));
                    start = i + 1;
                }
            }
            parts.Add(text.Substring(start));
            return parts;
        }

        /// <summary>
        /// 成员标签——字段/属性歧义时的候选描述（方法/构造函数走签名路径）
        /// </summary>
        /// <param name="node">成员节点</param>
        /// <returns>标签文本</returns>
        private static string FindMemberLabel(SyntaxNode node)
        {
            PropertyDeclarationSyntax? property = node as PropertyDeclarationSyntax;
            if (property != null)
            {
                return "属性 " + property.Identifier.Text;
            }
            FieldDeclarationSyntax? field = node as FieldDeclarationSyntax;
            if (field != null && field.Declaration.Variables.Count > 0)
            {
                return "字段 " + field.Declaration.Variables[0].Identifier.Text;
            }
            return node.GetType().Name;
        }

        /// <summary>
        /// 原子落盘——临时文件 + Move 覆盖（防半截写入）
        /// </summary>
        /// <param name="path">目标文件</param>
        /// <param name="text">完整新内容</param>
        private static void WriteAtomicText(string path, string text)
        {
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temporary, text, new System.Text.UTF8Encoding(false));
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
        /// 编译验证——新增错误检测（基线对比：改后新增的 error 才算坏——项目既有错误不误伤）
        /// </summary>
        /// <param name="before">改动前编译</param>
        /// <param name="after">改动后编译</param>
        /// <param name="newErrors">新增错误诊断文本行</param>
        /// <returns>true=无新增错误（可落盘）</returns>
        private static bool ValidateNoNewErrors(CSharpCompilation before, CSharpCompilation after, out List<string> newErrors)
        {
            newErrors = new List<string>();
            HashSet<string> beforeErrors = new HashSet<string>(StringComparer.Ordinal);
            ImmutableArrayHelper.CollectErrorKeys(before, beforeErrors);
            foreach (Diagnostic diagnostic in after.GetDiagnostics())
            {
                if (diagnostic.Severity != DiagnosticSeverity.Error)
                {
                    continue;
                }
                string key = diagnostic.Id + "|" + diagnostic.Location.GetLineSpan().Path + "|" + diagnostic.GetMessage();
                if (!beforeErrors.Contains(key))
                {
                    newErrors.Add(diagnostic.ToString());
                }
            }
            return newErrors.Count == 0;
        }
    }

    /// <summary>
    /// ImmutableArray 助手——编译诊断键收集（避免 lambda 的直接使用点集中管理）
    /// </summary>
    internal static class ImmutableArrayHelper
    {
        /// <summary>
        /// 收集编译错误键集合（Id|路径|行|列|消息）
        /// </summary>
        /// <param name="compilation">编译</param>
        /// <param name="target">目标集合</param>
        public static void CollectErrorKeys(CSharpCompilation compilation, HashSet<string> target)
        {
            System.Collections.Immutable.ImmutableArray<Diagnostic> diagnostics = compilation.GetDiagnostics();
            for (int i = 0; i < diagnostics.Length; i = i + 1)
            {
                Diagnostic diagnostic = diagnostics[i];
                if (diagnostic.Severity != DiagnosticSeverity.Error)
                {
                    continue;
                }
                FileLinePositionSpan span = diagnostic.Location.GetLineSpan();
                target.Add(diagnostic.Id + "|" + span.Path + "|" + diagnostic.GetMessage());
            }
        }
    }
}