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
                    onlyBricks = true;
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
            string[] files = Directory.GetFiles(syntaxDir, "*.mau", SearchOption.TopDirectoryOnly);
            Array.Sort(files, StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < files.Length; i = i + 1)
            {
                string path = files[i];
                string fileName = Path.GetFileName(path);
                string flowName = Program.FlowNameFromPath(Path.GetFileNameWithoutExtension(path));
                string source = File.ReadAllText(path);

                // [A] 语法校验——编译必须成功
                sSyntaxTotal = sSyntaxTotal + 1;
                CompileResult result = MauCompiler.Compile(source, flowName);
                if (!result.Success)
                {
                    sSyntaxFail = sSyntaxFail + 1;
                    sErrors.Add("[A] " + fileName + ": 编译失败——" + FirstDiagnostic(result));
                    continue;
                }
                sSyntaxPass = sSyntaxPass + 1;

                // [B] 翻译器校验
                sGoldenTotal = sGoldenTotal + 1;
                // 1. 确定性——两次生成字节一致
                CompileResult result2 = MauCompiler.Compile(source, flowName);
                if (result.GeneratedCode != result2.GeneratedCode)
                {
                    sGoldenDrift = sGoldenDrift + 1;
                    sErrors.Add("[B] " + fileName + ": 确定性失败——两次生成不一致");
                    continue;
                }
                string className = "FL_" + flowName;
                string expectedDir = Path.Combine(syntaxDir, "expected");
                string expectedPath = Path.Combine(expectedDir, className + ".cs");
                if (update)
                {
                    Directory.CreateDirectory(expectedDir);
                    // E1：黄金只存纯生成内容——剥离内嵌积木段（BRIKGROUP 由积木谱独立验证）
                    WriteGolden(expectedPath, MauCompiler.StripBrickSections(result.GeneratedCode));
                    continue;
                }
                // 2. 黄金校验尾 + 逐字节对比
                if (!File.Exists(expectedPath))
                {
                    sGoldenMissing = sGoldenMissing + 1;
                    sErrors.Add("[B] " + fileName + ": 黄金缺失——" + className + ".cs（--update 生成）");
                    continue;
                }
                string golden = File.ReadAllText(expectedPath).Replace("\r\n", "\n");
                string goldenBody = StripChecksum(golden, out string goldenHash);
                if (goldenHash.Length > 0 && goldenHash != CliSupport.ComputeSha256(goldenBody))
                {
                    sGoldenDrift = sGoldenDrift + 1;
                    sErrors.Add("[B] " + fileName + ": 黄金被篡改——校验尾不匹配");
                    continue;
                }
                string generated = MauCompiler.StripBrickSections(result.GeneratedCode).Replace("\r\n", "\n").TrimEnd();
                if (goldenBody != generated)
                {
                    sGoldenDrift = sGoldenDrift + 1;
                    sErrors.Add("[B] " + fileName + ": 生成漂移——与黄金不一致");
                    continue;
                }
                // 3. 结构断言——类名/Tick/Fire
                if (!AssertStructure(result.GeneratedCode, className, source, out string structError))
                {
                    sGoldenStructFail = sGoldenStructFail + 1;
                    sErrors.Add("[B] " + fileName + ": 结构断言失败——" + structError);
                    continue;
                }
                sGoldenPass = sGoldenPass + 1;
            }
        }

        /// <summary>
        /// 积木谱——BrickIndex 枚举 → 契约生成最小语料 → 翻译 → CompileMany 一次组编译
        /// </summary>
        private static void RunBrickCorpus()
{
            // [段1] 枚举 + 契约生成最小语料文本（不编译——组模式统一构筑）
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
                StringBuilder paramLines = new StringBuilder();
                for (int p = 0; p < c.Inputs.Count; p = p + 1)
                {
                    if (p > 0)
                    {
                        paramLines.Append(", ");
                    }
                    paramLines.Append(c.Inputs[p].Name);
                }
                string flowName = "BrickCheck" + c.Name.Replace(".", "");
                string source = CliSupport.BuildMinimalCorpus(c.Name, paramLines.ToString(), "T_Run");
                sourceTexts.Add(source);
                flowNames.Add(flowName);
                brickNames.Add(c.Name);
            }
            if (sourceTexts.Count == 0)
            {
                return;
            }

            // [段2] 组编译——CompileGroup（骨架 + 共享 BRIKGROUP，闭包去重；R1 内嵌模型下禁止逐文件内嵌再组编译）
            GroupCompileResult group = MauCompiler.CompileGroup(sourceTexts.ToArray(), flowNames.ToArray());
            List<string> skeletonSources = new List<string>();
            List<string> skeletonNames = new List<string>();
            bool anyFail = false;
            for (int i = 0; i < group.Results.Length; i = i + 1)
            {
                if (!group.Results[i].Success)
                {
                    anyFail = true;
                    sBrickFail = sBrickFail + 1;
                    sErrors.Add("[C] " + brickNames[i] + ": 翻译失败——" + FirstDiagnostic(group.Results[i]));
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
        }        /// <summary>
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
                CompileResult result = MauCompiler.Compile(source, flowName);
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
                    sErrors.Add("[D] " + fileName + ": 期望 " + expectedCode + " 但诊断为 " + FirstDiagnostic(result));
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
                Console.WriteLine("[E] 模板库      " + sCorpusPass + "/" + sCorpusTotal + " verify 通过（" + sCorpusFail + " 失败）");
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
            if (generated.IndexOf("public void Tick") < 0)
            {
                error = "未找到 Tick 方法";
                return false;
            }
            if (source.IndexOf("信号") >= 0 && generated.IndexOf("Fire") < 0)
            {
                error = "信号命题存在但未生成 Fire 方法";
                return false;
            }
            return true;
        }

        /// <summary>
        /// 写黄金文件——正文 + SHA256 校验尾
        /// </summary>
        /// <param name="path">黄金路径</param>
        /// <param name="generated">生成物源码</param>
        private static void WriteGolden(string path, string generated)
        {
            string body = generated.Replace("\r\n", "\n").TrimEnd();
            string content = body + "\n" + Mau.Runtime.HashUtil.ChecksumPrefix + CliSupport.ComputeSha256(body) + "\n";
            File.WriteAllText(path, content, new UTF8Encoding(true));
        }

        /// <summary>
        /// 提取黄金校验尾——返回去掉校验行后的正文
        /// </summary>
        /// <param name="content">黄金全文</param>
        /// <param name="hash">校验尾哈希（无则空）</param>
        /// <returns>正文（TrimEnd）</returns>
        private static string StripChecksum(string content, out string hash)
        {
            hash = "";
            string[] lines = content.Split('\n');
            int bodyEnd = lines.Length;
            while (bodyEnd > 0)
            {
                string last = lines[bodyEnd - 1].Trim();
                if (last.Length == 0)
                {
                    bodyEnd = bodyEnd - 1;
                    continue;
                }
                if (last.StartsWith(Mau.Runtime.HashUtil.ChecksumPrefix))
                {
                    hash = last.Substring(Mau.Runtime.HashUtil.ChecksumPrefix.Length).Trim();
                    bodyEnd = bodyEnd - 1;
                    continue;
                }
                break;
            }
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < bodyEnd; i = i + 1)
            {
                if (i > 0)
                {
                    sb.Append('\n');
                }
                sb.Append(lines[i]);
            }
            return sb.ToString().TrimEnd();
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
        /// 首条诊断文本
        /// </summary>
        /// <param name="result">编译结果</param>
        /// <returns>诊断文本</returns>
        private static string FirstDiagnostic(CompileResult result)
        {
            if (result.Diagnostics.Count > 0)
            {
                MauDiagnostic d = result.Diagnostics[0];
                return d.Code + ": " + d.Message;
            }
            return "未知";
        }
/// <summary>
/// 模板库——遍历 Mau.Corpus/*.mau，逐个静态验证（目录即清单——模板漂移阻断门禁）
/// </summary>
/// <param name = "root">仓库根</param>
private static void RunCorpusTemplates(string root)
{
    string corpusDir = Path.Combine(root, "Mau.Corpus");
    if (!Directory.Exists(corpusDir))
    {
        Console.WriteLine("WARN: 模板库目录不存在——" + corpusDir);
        return;
    }

    string[] files = Directory.GetFiles(corpusDir, "*.mau", SearchOption.TopDirectoryOnly);
    Array.Sort(files, StringComparer.OrdinalIgnoreCase);
    for (int i = 0; i < files.Length; i = i + 1)
    {
        string path = files[i];
        string fileName = Path.GetFileName(path);
        string flowName = Program.FlowNameFromPath(Path.GetFileNameWithoutExtension(path));
        string source = File.ReadAllText(path);
        sCorpusTotal = sCorpusTotal + 1;
        CompileResult result = MauCompiler.Compile(source, flowName);
        if (!result.Success)
        {
            sCorpusFail = sCorpusFail + 1;
            sErrors.Add("[E] " + fileName + ": verify 失败——" + FirstDiagnostic(result));
            continue;
        }

        sCorpusPass = sCorpusPass + 1;
    }
}    }
}
