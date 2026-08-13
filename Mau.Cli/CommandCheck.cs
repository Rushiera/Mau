using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Mau.Contracts;
using Mau.Development;
using Mau.Translator;

namespace Mau.Cli
{
    /// <summary>
    /// mau check 命令——全谱自查（语法谱/翻译器谱/积木谱/负例）聚合报告
    /// </summary>
    public static class CommandCheck
    {
        /// <summary>
        /// 黄金校验尾前缀——与门禁 L3 同格式
        /// </summary>
        // 黄金校验尾前缀——统一常量（HashUtil.ChecksumPrefix，审查修复轮 2026-08-11 收拢 6 处硬编码）

        /// <summary>
        /// 汇总计数——语法谱总数
        /// </summary>
        private static int sSyntaxTotal;
        /// <summary>
        /// 汇总计数——语法谱通过数
        /// </summary>
        private static int sSyntaxPass;
        /// <summary>
        /// 汇总计数——语法谱失败数
        /// </summary>
        private static int sSyntaxFail;
        /// <summary>
        /// 汇总计数——翻译器谱总数
        /// </summary>
        private static int sGoldenTotal;
        /// <summary>
        /// 汇总计数——翻译器谱通过数
        /// </summary>
        private static int sGoldenPass;
        /// <summary>
        /// 汇总计数——翻译器谱漂移数（黄金不一致）
        /// </summary>
        private static int sGoldenDrift;
        /// <summary>
        /// 汇总计数——翻译器谱黄金缺失数
        /// </summary>
        private static int sGoldenMissing;
        /// <summary>
        /// 汇总计数——翻译器谱结构断言失败数
        /// </summary>
        private static int sGoldenStructFail;
        /// <summary>
        /// 汇总计数——积木谱总数
        /// </summary>
        private static int sBrickTotal;
        /// <summary>
        /// 汇总计数——积木谱通过数
        /// </summary>
        private static int sBrickPass;
        /// <summary>
        /// 汇总计数——积木谱失败数
        /// </summary>
        private static int sBrickFail;
        /// <summary>
        /// 汇总计数——积木谱跳过数
        /// </summary>
        private static int sBrickSkip;
        /// <summary>
        /// 汇总计数——负例总数
        /// </summary>
        private static int sNegTotal;
        /// <summary>
        /// 汇总计数——负例通过数
        /// </summary>
        private static int sNegPass;
        /// <summary>
        /// 汇总计数——负例失败数
        /// </summary>
        private static int sNegFail;
        /// <summary>
        /// 错误清单——聚合报告输出
        /// </summary>
        private static List<string> sErrors = new List<string>();
        /// <summary>
        /// 跳过清单——聚合报告输出
        /// </summary>
        private static List<string> sSkips = new List<string>();
        /// <summary>
        /// 产品积木谱 [P] 统计——check --bricks <path> 项目自证段
        /// </summary>
        private static int sProductTotal = 0;
        private static int sProductPass = 0;
        private static int sProductFail = 0;
        private static int sProductSkip = 0;
/// <summary>
/// 汇总计数——模板库总数
/// </summary>
private static int sCorpusTotal = 0; 
/// <summary>
/// 汇总计数——模板库通过数
/// </summary>
 private  static  int  sCorpusPass  =  0 ;  
/// <summary>
/// 汇总计数——模板库失败数
/// </summary>
 private  static  int  sCorpusFail  =  0 ;
/// <summary>
/// check 命令入口——全谱遍历一次返回全部错误；--selftest = 发布包随行资源自检（不要求 Mau.sln）
/// </summary>
///

        ///
public static int Execute(string[] args)
{
            // [段1] 参数解析
            bool update = false;
            bool onlySyntax = false;
            bool onlyBricks = false;
            bool selftest = false;
            string? productBricks = null;
            for (int i = 0; i < args.Length; i = i + 1)
            {
                if (args[i] == "--update")
                {
                    update = true;
                }
                else if (args[i] == "--syntax")
                {
                    onlySyntax = true;
                }
                else if (args[i] == "--bricks")
                {
                    // 带值 = 产品积木目录（[P] 产品谱）；无值 = 只跑积木谱（旧语义）
                    if (i + 1 < args.Length && !args[i + 1].StartsWith("--"))
                    {
                        productBricks = args[i + 1];
                        i = i + 1;
                    }
                    else
                    {
                        onlyBricks = true;
                    }
                }
                else if (args[i] == "--selftest")
                {
                    selftest = true;
                }
            }

            // [段2] 定位谱目录——selftest 模式不要求 Mau.sln（发布包随行资源自检）
            if (!selftest)
            {
                string? root = CliSupport.FindWorkspaceRoot();
                if (root == null)
                {
                    Console.WriteLine("FAIL: 未找到 Mau.sln——请从仓库内运行");
                    return 2;
                }
                // --analyze 独立段已退役（2026-08-13 A 类清理——T7 有真实内容再立）
                string checksDir = Path.Combine(root, "Mau.Snapshots", "checks");
                string syntaxDir = Path.Combine(checksDir, "syntax");
                string negativeDir = Path.Combine(checksDir, "negative");
                if (!Directory.Exists(syntaxDir) && !Directory.Exists(negativeDir))
                {
                    Console.WriteLine("FAIL: checks 目录不存在——" + checksDir);
                    return 2;
                }

                // [段3] 初始化计数
                sSyntaxTotal = 0;
                sSyntaxPass = 0;
                sSyntaxFail = 0;
                sGoldenTotal = 0;
                sGoldenPass = 0;
                sGoldenDrift = 0;
                sGoldenMissing = 0;
                sGoldenStructFail = 0;
                sBrickTotal = 0;
                sBrickPass = 0;
                sBrickFail = 0;
                sBrickSkip = 0;
                sNegTotal = 0;
                sNegPass = 0;
                sNegFail = 0;
                sCorpusTotal = 0;
                sCorpusPass = 0;
                sCorpusFail = 0;
                sErrors = new List<string>();
                sSkips = new List<string>();

                // [段4] 执行谱——A+B（语法+翻译器）与 C+D（积木+负例）可独立
                    RunCorpusTemplates(root);
                if (!onlyBricks)
                {
                    RunSyntaxAndGolden(syntaxDir, update);
                }
                if (!onlySyntax)
                {
                    RunBrickCorpus();
                    RunNegative(negativeDir);
                }
                if (productBricks != null)
                {
                    // [段4b] 产品积木谱 [P]——--bricks <path> 项目自证段（产品积木机制 design-mau-boundary.md §五）
                    RunProductBrickCorpus(productBricks);
                }

                // [段5] 聚合报告 + 退出码
                return PrintReport(onlySyntax, onlyBricks, update);
            }

            // [段2b] selftest——发布包随行资源自检：Bricks 索引 + 积木谱 + 最小语料编译闭环（不要求 Mau.sln）
            if (!CommandBricks.EnsureIndexLoaded())
            {
                Console.WriteLine("FAIL: selftest——Bricks 索引不可用（发布包缺少随行 Bricks/）");
                return 2;
            }
            sBrickTotal = 0;
            sBrickPass = 0;
            sBrickFail = 0;
            sBrickSkip = 0;
            sErrors = new List<string>();
            sSkips = new List<string>();
            RunBrickCorpus();
            Console.WriteLine();
            Console.WriteLine("=== MAU_SELFTEST 汇总（发布包随行资源） ===");
            Console.WriteLine("[C] 积木谱      " + sBrickPass + "/" + sBrickTotal + " 通过（" + sBrickFail + " 失败 " + sBrickSkip + " 跳过）");
            for (int i = 0; i < sErrors.Count; i = i + 1)
            {
                Console.WriteLine("    ❌ " + sErrors[i]);
            }
            if (sBrickFail == 0)
            {
                Console.WriteLine("MAU_SELFTEST_OK");
                return 0;
            }
            Console.WriteLine("MAU_SELFTEST_FAIL");
            return 1;
        }
        /// <summary>
        /// 语法谱 + 翻译器谱——遍历 syntax/*.mau，编译 + 确定性 + 黄金对比 + 结构断言
        /// </summary>
        /// <param name="syntaxDir">语法谱目录</param>
        /// <param name="update">true=生成/更新黄金（不对比）</param>
        private static void RunSyntaxAndGolden(string syntaxDir, bool update)
{
            if (!Directory.Exists(syntaxDir))
            {
                Console.WriteLine("WARN: 语法谱目录不存在——" + syntaxDir);
                return;
            }
            // 黄金哈希清单——golden-sha.txt（黄金哈希化 2026-08-13：全文退役，一颗哈希一行反证改动范围）
            System.Collections.Generic.Dictionary<string, string> hashes =
                new System.Collections.Generic.Dictionary<string, string>(StringComparer.Ordinal);
            string hashListPath = Path.Combine(syntaxDir, "golden-sha.txt");
            if (File.Exists(hashListPath) && !update)
            {
                string[] hashLines = File.ReadAllLines(hashListPath);
                for (int h = 0; h < hashLines.Length; h++)
                {
                    string trimmed = hashLines[h].Trim();
                    if (trimmed.Length == 0)
                    {
                        continue;
                    }
                    int space = trimmed.IndexOf(' ');
                    if (space > 0)
                    {
                        hashes[trimmed.Substring(0, space)] = trimmed.Substring(space + 1);
                    }
                }
            }
            string[] files = Directory.GetFiles(syntaxDir, "*.mau", SearchOption.TopDirectoryOnly);
            Array.Sort(files, StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < files.Length; i = i + 1)
            {
                string path = files[i];
                string fileName = Path.GetFileName(path);
                string flowName = Program.FlowNameFromPath(Path.GetFileNameWithoutExtension(path));
                string source = File.ReadAllText(path);

                // [A] 语法校验——v2 门面编译必须成功
                sSyntaxTotal = sSyntaxTotal + 1;
                CompileResultV2 result = MauCompilerV2.Compile(source, flowName);
                if (!result.Success)
                {
                    sSyntaxFail = sSyntaxFail + 1;
                    sErrors.Add("[A] " + fileName + ": 编译失败——" + FirstDiagnosticV2(result));
                    continue;
                }
                sSyntaxPass = sSyntaxPass + 1;

                // [B] 翻译器校验
                sGoldenTotal = sGoldenTotal + 1;
                // 1. 确定性——两次生成字节一致
                CompileResultV2 result2 = MauCompilerV2.Compile(source, flowName);
                if (result.GeneratedCode != result2.GeneratedCode)
                {
                    sGoldenDrift = sGoldenDrift + 1;
                    sErrors.Add("[B] " + fileName + ": 确定性失败——两次生成不一致");
                    continue;
                }
                // 哈希对象 = 剥离内嵌积木段后的语料骨架（积木改动不漂移；语料/生成器改动漂移 = 改动影响面反证）
                string className = "FL_" + flowName;
                string stripped = MauCompilerV2.StripBrickSectionsV2(result.GeneratedCode);
                string hash = CliSupport.ComputeSha256(stripped);
                if (update)
                {
                    hashes[className] = hash;
                    Console.WriteLine("UPDATE: 黄金哈希已生成——" + className);
                    continue;
                }
                // 2. 黄金哈希对比
                string? recorded;
                if (!hashes.TryGetValue(className, out recorded) || recorded == null)
                {
                    sGoldenMissing = sGoldenMissing + 1;
                    sErrors.Add("[B] " + fileName + ": 黄金哈希缺失——" + className + "（--update 生成）");
                    continue;
                }
                if (recorded != hash)
                {
                    sGoldenDrift = sGoldenDrift + 1;
                    sErrors.Add("[B] " + fileName + ": 生成漂移——哈希不一致（清单 " + recorded.Substring(0, 8) + "… 实际 " + hash.Substring(0, 8) + "…）");
                    continue;
                }
                // 3. 结构断言——类名/Tick/Fire（v2 生成物）
                if (!AssertStructure(result.GeneratedCode, flowName, source, out string structError))
                {
                    sGoldenStructFail = sGoldenStructFail + 1;
                    sErrors.Add("[B] " + fileName + ": 结构断言失败——" + structError);
                    continue;
                }
                sGoldenPass = sGoldenPass + 1;
            }
            if (update)
            {
                System.Collections.Generic.List<string> names =
                    new System.Collections.Generic.List<string>(hashes.Keys);
                names.Sort(StringComparer.Ordinal);
                StringBuilder sb = new StringBuilder();
                for (int i = 0; i < names.Count; i++)
                {
                    if (i > 0)
                    {
                        sb.Append('\n');
                    }
                    sb.Append(names[i]);
                    sb.Append(' ');
                    sb.Append(hashes[names[i]]);
                }
                File.WriteAllText(hashListPath, sb.ToString());
                Console.WriteLine("黄金哈希清单已更新: golden-sha.txt（" + names.Count.ToString() + " 份）");
            }
        }        /// <summary>
/// 产品积木谱 [P]——check --bricks <path> 项目自证段（产品积木机制 design-mau-boundary.md §五）。
/// 翻译级验证：产品契约可构造 + 最小语料组编译通过（Roslyn 编译由项目侧构筑承担）。
/// </summary>
/// <param name = "productDir">产品积木目录</param>
private static void RunProductBrickCorpus(string productDir)
{
    // [段1] 产品上下文——目录无效 fail closed（E225 语义）
    BrickContext ctx = BrickContext.FromProductDirs(new List<string> { productDir });
    if (ctx.ProductError.Length > 0)
    {
        sProductTotal = sProductTotal + 1;
        sProductFail = sProductFail + 1;
        sErrors.Add("[P] " + ctx.ProductError);
        return;
    }

    // [段2] 枚举产品契约 → 最小语料 → 组编译（产品上下文）
    List<string> sourceTexts = new List<string>();
    List<string> flowNames = new List<string>();
    List<string> brickNames = new List<string>();
    List<BrickContract> all = new List<BrickContract>();
    for (int v = 0; v < ctx.Products.Count; v++)
    {
        foreach (BrickIndexEntry brickEntry in ctx.Products[v].All)
        {
            all.Add(brickEntry.Contract);
        }
    }

    all.Sort(delegate (BrickContract a, BrickContract b)
    {
        return string.CompareOrdinal(a.Name, b.Name);
    });
    for (int i = 0; i < all.Count; i = i + 1)
    {
        BrickContract c = all[i];
        sProductTotal = sProductTotal + 1;
        string flowName = "ProductCheck" + c.Name.Replace(".", "");
        sourceTexts.Add(CliSupport.BuildMinimalCorpusV2(c, "T_Run"));
        flowNames.Add(flowName);
        brickNames.Add(c.Name);
    }

    if (sourceTexts.Count == 0)
    {
        return;
    }

    // [段3] 组编译（产品上下文）——失败逐积木归因
    GroupCompileResultV2 group = MauCompilerV2.CompileGroupV2(sourceTexts.ToArray(), flowNames.ToArray(), ctx);
    for (int i = 0; i < group.Results.Count; i = i + 1)
    {
        if (!group.Results[i].Success)
        {
            sProductFail = sProductFail + 1;
            sErrors.Add("[P] " + brickNames[i] + ": 翻译失败——" + FirstDiagnosticV2(group.Results[i]));
        }
        else
        {
            sProductPass = sProductPass + 1;
        }
    }

    if (group.BrickGroupSource.Length > 0)
    {
        Console.WriteLine("[P] 产品积木闭包: " + group.BrickGroupSource.Length + " 字符（BRIKGROUP 内嵌段已生成）");
    }
}/// <summary>
        /// 积木谱——BrickIndex 枚举 → 契约生成最小语料 → 翻译 → CompileMany 一次组编译
        /// </summary>
        private static void RunBrickCorpus()
{
            // [段1] 枚举 + 契约生成最小语料文本（v2 全符号语法——按端口类型生成参数字面量）
            List<string> sourceTexts = new List<string>();
            List<string> flowNames = new List<string>();
            List<string> brickNames = new List<string>();
            List<BrickContract> all = new List<BrickContract>();
            foreach (BrickIndexEntry brickEntry in BrickIndex.All)
            {
                all.Add(brickEntry.Contract);
            }
            all.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
            for (int i = 0; i < all.Count; i = i + 1)
            {
                BrickContract c = all[i];
                sBrickTotal = sBrickTotal + 1;
                string flowName = "BrickCheck" + c.Name.Replace(".", "");
                string source = CliSupport.BuildMinimalCorpusV2(c, "T_Run");
                sourceTexts.Add(source);
                flowNames.Add(flowName);
                brickNames.Add(c.Name);
            }
            if (sourceTexts.Count == 0)
            {
                return;
            }

            // [段2] 组编译 v2——逐语料翻译（词法/解析/糖展开/验证/分析）+ 共享 BRIKGROUP 闭包去重
            GroupCompileResultV2 group = MauCompilerV2.CompileGroupV2(sourceTexts.ToArray(), flowNames.ToArray());
            List<string> skeletonSources = new List<string>();
            List<string> skeletonNames = new List<string>();
            bool anyFail = false;
            for (int i = 0; i < group.Results.Count; i = i + 1)
            {
                if (!group.Results[i].Success)
                {
                    anyFail = true;
                    sBrickFail = sBrickFail + 1;
                    sErrors.Add("[C] " + brickNames[i] + ": 翻译失败——" + FirstDiagnosticV2(group.Results[i]));
                }
                else
                {
                    skeletonSources.Add(group.Results[i].GeneratedCode);
                    skeletonNames.Add(flowNames[i]);
                }
            }
            if (anyFail || skeletonSources.Count == 0)
            {
                return;
            }
            if (group.BrickGroupSource.Length > 0)
            {
                skeletonSources.Add(group.BrickGroupSource);
                skeletonNames.Add("MauCheckBrickGroup");
            }

            // [段3] 组编译——失败按文件名归因到积木（仅 Error 级诊断；Warning/Info 不判失败）
            string pocketRoot = Path.Combine(Path.GetTempPath(), "mau_check_bricks_" + Guid.NewGuid().ToString("N").Substring(0, 8));
            try
            {
                MauPocketCompiler compiler = new MauPocketCompiler(pocketRoot);
                MauPocketCompileResult result = compiler.CompileMany(skeletonSources.ToArray(), skeletonNames.ToArray(), "MauCheckBricks");
                if (result.Success)
                {
                    for (int i = 0; i < brickNames.Count; i = i + 1)
                    {
                        sBrickPass = sBrickPass + 1;
                    }
                    return;
                }

                // 失败——诊断含文件名（FL_BrickCheckxxx.cs / MauCheckBrickGroup.cs），按文件归因
                Dictionary<string, List<string>> diagByFile = new Dictionary<string, List<string>>();
                for (int d = 0; d < result.Diagnostics.Length; d = d + 1)
                {
                    if (!result.Diagnostics[d].StartsWith("Error:", StringComparison.Ordinal))
                    {
                        continue;
                    }
                    string file = ExtractDiagFile(result.Diagnostics[d]);
                    if (file.Length > 0)
                    {
                        if (!diagByFile.ContainsKey(file))
                        {
                            diagByFile[file] = new List<string>();
                        }
                        diagByFile[file].Add(result.Diagnostics[d]);
                    }
                }
                for (int i = 0; i < brickNames.Count; i = i + 1)
                {
                    string fileKey = flowNames[i] + ".cs";
                    if (diagByFile.ContainsKey(fileKey))
                    {
                        sBrickFail = sBrickFail + 1;
                        List<string> diags = diagByFile[fileKey];
                        sErrors.Add("[C] " + brickNames[i] + ": 生成调用编译失败（" + diags.Count + " 错误）——" + diags[0]);
                    }
                    else
                    {
                        sBrickPass = sBrickPass + 1;
                    }
                }
            }
            finally
            {
                if (Directory.Exists(pocketRoot))
                {
                    try
                    {
                        Directory.Delete(pocketRoot, true);
                    }
                    catch
                    {
                        // 清理失败不影响结果
                    }
                }
            }
        }/// <summary>
        /// 负例——断言编译失败且含预期错误码（文件头 // 预期: E0xx）
        /// </summary>
        /// <param name="negativeDir">负例目录</param>
        private static void RunNegative(string negativeDir)
{
            if (!Directory.Exists(negativeDir))
            {
                return;
            }
            string[] files = Directory.GetFiles(negativeDir, "*.mau", SearchOption.TopDirectoryOnly);
            Array.Sort(files, StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < files.Length; i = i + 1)
            {
                string path = files[i];
                string fileName = Path.GetFileName(path);
                string source = File.ReadAllText(path);
                string expectedCode = ExpectedErrorCode(source);
                sNegTotal = sNegTotal + 1;
                if (expectedCode.Length == 0)
                {
                    sNegFail = sNegFail + 1;
                    sErrors.Add("[D] " + fileName + ": 缺少预期错误码注释（// 预期: E0xx）");
                    continue;
                }
                string flowName = Program.FlowNameFromPath(Path.GetFileNameWithoutExtension(path));
                CompileResultV2 result = MauCompilerV2.Compile(source, flowName);
                if (result.Success)
                {
                    sNegFail = sNegFail + 1;
                    sErrors.Add("[D] " + fileName + ": 期望 " + expectedCode + " 但编译通过");
                    continue;
                }
                bool found = false;
                for (int d = 0; d < result.Diagnostics.Count; d = d + 1)
                {
                    if (result.Diagnostics[d].Code == expectedCode)
                    {
                        found = true;
                        break;
                    }
                }
                if (!found)
                {
                    sNegFail = sNegFail + 1;
                    sErrors.Add("[D] " + fileName + ": 期望 " + expectedCode + " 但诊断为 " + FirstDiagnosticV2(result));
                    continue;
                }
                sNegPass = sNegPass + 1;
            }
        }
        /// <summary>
        /// 聚合报告——全谱汇总 + 错误清单 + 退出码
        /// </summary>
        /// <param name="onlySyntax">仅语法+翻译器谱</param>
        /// <param name="onlyBricks">仅积木谱</param>
        /// <param name="update">黄金更新模式</param>
        /// <returns>退出码</returns>
        private static int PrintReport(bool onlySyntax, bool onlyBricks, bool update)
        {
            int total = 0;
            int fail = 0;
            Console.WriteLine();
            Console.WriteLine("=== MAU_CHECK 汇总 ===");
            if (!onlyBricks)
            {
                Console.WriteLine("[A] 语法谱      " + sSyntaxPass + "/" + sSyntaxTotal + " 通过（" + sSyntaxFail + " 失败）");
                Console.WriteLine("[B] 翻译器谱    " + sGoldenPass + "/" + sGoldenTotal + " 黄金一致（" + sGoldenDrift + " 漂移 " + sGoldenMissing + " 缺失 " + sGoldenStructFail + " 结构断言失败）");
                Console.WriteLine("[E] CH4 案例谱  " + sCorpusPass + "/" + sCorpusTotal + " verify 通过（" + sCorpusFail + " 失败）");
                total = total + sSyntaxTotal + sGoldenTotal + sCorpusTotal;
                fail = fail + sSyntaxFail + sGoldenDrift + sGoldenMissing + sGoldenStructFail + sCorpusFail;
            }
            if (!onlySyntax)
            {
                Console.WriteLine("[C] 积木谱      " + sBrickPass + "/" + sBrickTotal + " 通过（" + sBrickFail + " 失败 " + sBrickSkip + " 跳过）");
                Console.WriteLine("[D] 负例        " + sNegPass + "/" + sNegTotal + " 正确拒绝（" + sNegFail + " 未达预期）");
                total = total + sBrickTotal + sNegTotal;
                fail = fail + sBrickFail + sNegFail;
            }
            if (sProductTotal > 0)
            {
                Console.WriteLine("[P] 产品积木谱  " + sProductPass + "/" + sProductTotal + " 通过（" + sProductFail + " 失败 " + sProductSkip + " 跳过）");
                total = total + sProductTotal;
                fail = fail + sProductFail;
            }
            for (int i = 0; i < sErrors.Count; i = i + 1)
            {
                Console.WriteLine("    ❌ " + sErrors[i]);
            }
            for (int i = 0; i < sSkips.Count; i = i + 1)
            {
                Console.WriteLine("    ⚠️  SKIP " + sSkips[i]);
            }
            Console.WriteLine("总计 " + total + " 项 | 失败 " + fail);
            if (update)
            {
                Console.WriteLine("黄金已更新（--update 模式）");
            }
            if (fail == 0)
            {
                Console.WriteLine("MAU_CHECK_OK");
                return 0;
            }
            Console.WriteLine("MAU_CHECK_FAIL");
            return 1;
        }

        /// <summary>
        /// 结构断言——类名 / Tick / Fire（源含信号命题时）
        /// </summary>
        /// <param name="generated">生成物源码</param>
        /// <param name="className">期望类名</param>
        /// <param name="source">源 .mau（判断是否含信号命题）</param>
        /// <param name="error">失败原因</param>
        /// <returns>true=通过</returns>
        private static bool AssertStructure(string generated, string className, string source, out string error)
{
            error = "";
            if (generated.IndexOf("class " + className) < 0)
            {
                error = "未找到类 " + className;
                return false;
            }
            if (generated.IndexOf("public void Tick(int frame)") < 0 && generated.IndexOf("public void Tick()") < 0)
            {
                error = "未找到 Tick 方法";
                return false;
            }
            // Fire 信号断言——仅 ⇐ 'P_X'（纯信号投递）生成 Fire 方法；OA 接收（⇐ 'msg' →）不生成
            if (source.IndexOf("⇐ 'P_") >= 0 && generated.IndexOf("Fire") < 0)
            {
                error = "信号命题存在但未生成 Fire 方法";
                return false;
            }
            return true;
        }

        /// <summary>
        /// 从诊断文本提取文件名——格式 Severity:文件:错误码:行:消息
        /// </summary>
        /// <param name="diag">诊断文本</param>
        /// <returns>文件名（无则空）</returns>
        private static string ExtractDiagFile(string diag)
        {
            int colon = diag.IndexOf(':');
            if (colon < 0)
            {
                return "";
            }
            string rest = diag.Substring(colon + 1);
            int colon2 = rest.IndexOf(':');
            if (colon2 <= 0)
            {
                return "";
            }
            string file = rest.Substring(0, colon2);
            if (file.EndsWith(".cs"))
            {
                return file;
            }
            return "";
        }

        /// <summary>
        /// 负例预期错误码——扫描 // 预期: E0xx
        /// </summary>
        /// <param name="source">负例源码</param>
        /// <returns>错误码（无则空）</returns>
        private static string ExpectedErrorCode(string source)
        {
            string[] lines = source.Split('\n');
            for (int i = 0; i < lines.Length; i = i + 1)
            {
                string line = lines[i].Trim();
                if (line.StartsWith("// 预期:"))
                {
                    string code = line.Substring("// 预期:".Length).Trim();
                    if (code.Length >= 4 && code.StartsWith("E"))
                    {
                        return code;
                    }
                }
            }
            return "";
        }
/// <summary>
/// 首条诊断文本（v2 编译结果）
/// </summary>
/// <param name = "result">编译结果 v2</param>
/// <returns>诊断文本</returns>
private static string FirstDiagnosticV2(CompileResultV2 result)
{
    if (result.Diagnostics.Count > 0)
    {
        MauDiagnostic d = result.Diagnostics[0];
        return d.Code + ": " + d.Message;
    }

    return "未知";
}/// <summary>
/// 模板库——遍历 Mau.Corpus/*.mau，逐个静态验证（目录即清单——模板漂移阻断门禁）
/// </summary>
/// <param name = "root">仓库根</param>
private static void RunCorpusTemplates(string root)
{
    // CH4 真实案例谱——Mau.Snapshots/cases/ch4_*.mau（V2.0.5 从 CH4 仓库提取的复杂案例 v2 语料）
    string corpusDir = Path.Combine(root, "Mau.Snapshots", "cases");
    if (!Directory.Exists(corpusDir))
    {
        Console.WriteLine("WARN: CH4 案例谱目录不存在——" + corpusDir);
        return;
    }

    string[] files = Directory.GetFiles(corpusDir, "ch4_*.mau", SearchOption.TopDirectoryOnly);
    Array.Sort(files, StringComparer.OrdinalIgnoreCase);
    for (int i = 0; i < files.Length; i = i + 1)
    {
        string path = files[i];
        string fileName = Path.GetFileName(path);
        string flowName = Program.FlowNameFromPath(Path.GetFileNameWithoutExtension(path));
        string source = File.ReadAllText(path);
        sCorpusTotal = sCorpusTotal + 1;
        CompileResultV2 result = MauCompilerV2.Compile(source, flowName);
        if (!result.Success)
        {
            sCorpusFail = sCorpusFail + 1;
            sErrors.Add("[E] " + fileName + ": verify 失败——" + FirstDiagnosticV2(result));
            continue;
        }

        sCorpusPass = sCorpusPass + 1;
    }
}
}
}
