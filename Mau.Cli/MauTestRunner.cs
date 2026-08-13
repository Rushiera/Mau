using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using Mau.Translator;
using Mau.Runtime;
using Mau.Development;
using System.Security.Cryptography;
using System.Text;

namespace Mau.Cli
{
    /// <summary>
    /// 门禁执行器——纯 C# 实现的分层门禁（L2 翻译器测试 + L5 生成物真实运行 + L3 黄金文件对比）
    /// 纲领三：工具链全 C#——不用 ps1/py 脚本承载工程逻辑
    /// </summary>
    public static class MauTestRunner
    {
        /// <summary>
        /// 执行门禁——成功输出 MAU_CHECKS_OK
        /// </summary>
        /// <returns>退出码——0 成功</returns>
        public static int Run(bool update)
        {
            string? root = CliSupport.FindWorkspaceRoot();
            if (root == null)
            {
                Console.WriteLine("FAIL: 未找到 Mau.sln——请在 Mau workspace 下运行 mau test");
                return 1;
            }

            // [段1] 构建测试项目
            Console.WriteLine("[1/4] 构建测试项目");
            if (!RunProcess("dotnet", "build \"" + Path.Combine(root, "Mau.Translator.Tests", "Mau.Translator.Tests.csproj") + "\" -v q --nologo"))
            {
                Console.WriteLine("FAIL: 翻译器测试项目构建失败");
                DiagnoseBuildLock(Path.Combine(root, "Mau.Translator.Tests", "bin", "Debug", "net8.0", "Mau.Translator.Tests.exe"));
                return 1;
            }
            if (!RunProcess("dotnet", "build \"" + Path.Combine(root, "Mau.Runtime.Tests", "Mau.Runtime.Tests.csproj") + "\" -v q --nologo"))
            {
                Console.WriteLine("FAIL: 运行时测试项目构建失败");
                DiagnoseBuildLock(Path.Combine(root, "Mau.Runtime.Tests", "bin", "Debug", "net8.0", "Mau.Runtime.Tests.exe"));
                return 1;
            }

            // [段1b] 热重载 fixture 构建——干净克隆自给自足（HotReloadTests 依赖 fixtures/bin/*.dll，bin 不入库；2026-08-13 外部审查漏洞 1 修复）
            Console.WriteLine("[1b/4] 热重载 fixture 构建");
            string fixtureOut = Path.Combine(root, "Mau.Runtime.Tests", "fixtures", "bin");
            if (!RunProcess("dotnet", "build \"" + Path.Combine(root, "Mau.Runtime.Tests", "fixtures", "valid", "FL_ValidFlow.csproj") + "\" -c Release -o \"" + fixtureOut + "\" -v q --nologo"))
            {
                Console.WriteLine("FAIL: 热重载 fixture 构建失败（fixtures/valid/FL_ValidFlow.csproj）");
                return 1;
            }
            if (!RunProcess("dotnet", "build \"" + Path.Combine(root, "Mau.Runtime.Tests", "fixtures", "nointerface", "FL_NoInterface.csproj") + "\" -c Release -o \"" + fixtureOut + "\" -v q --nologo"))
            {
                Console.WriteLine("FAIL: 热重载 fixture 构建失败（fixtures/nointerface/FL_NoInterface.csproj）");
                return 1;
            }

            if (!RunProcess("dotnet", "build \"" + Path.Combine(root, "Mau.Development.Tests", "Mau.Development.Tests.csproj") + "\" -v q --nologo"))
            {
                Console.WriteLine("FAIL: 开发工具测试项目构建失败");
                DiagnoseBuildLock(Path.Combine(root, "Mau.Development.Tests", "bin", "Debug", "net8.0", "Mau.Development.Tests.exe"));
                return 1;
            }

            if (!RunProcess("dotnet", "build \"" + Path.Combine(root, "Mau.E2E", "Mau.E2E.csproj") + "\" -v q --nologo"))
            {
                Console.WriteLine("FAIL: 端到端测试项目构建失败");
                DiagnoseBuildLock(Path.Combine(root, "Mau.E2E", "bin", "Debug", "net8.0", "Mau.E2E.exe"));
                return 1;
            }
            if (!RunProcess("dotnet", "build \"" + Path.Combine(root, "Mau.Cli", "Mau.Cli.csproj") + "\" -v q --nologo"))
            {
                Console.WriteLine("FAIL: Mau.Cli 构建失败（serve 进程测试依赖 Mau.exe）");
                DiagnoseBuildLock(Path.Combine(root, "Mau.Cli", "bin", "Debug", "net8.0", "Mau.exe"));
                return 1;
            }



            // [段2] 翻译器测试（L2）
            Console.WriteLine("[2/4] 翻译器测试（L2）");
            string translatorExe = Path.Combine(root, "Mau.Translator.Tests", "bin", "Debug", "net8.0", "Mau.Translator.Tests.exe");
            if (!RunProcess(translatorExe, ""))
            {
                Console.WriteLine("FAIL: 翻译器测试失败");
                return 1;
            }

            // [段2b] 开发工具测试（PocketCompiler）
            Console.WriteLine("[2b/4] 开发工具测试（L2.5）");
            string developmentExe = Path.Combine(root, "Mau.Development.Tests", "bin", "Debug", "net8.0", "Mau.Development.Tests.exe");
            if (!RunProcess(developmentExe, ""))
            {
                Console.WriteLine("FAIL: 开发工具测试失败");
                return 1;
            }

            // [段3] 生成物真实运行（L5）
            Console.WriteLine("[3/4] 生成物真实运行（L5）");
            string runtimeExe = Path.Combine(root, "Mau.Runtime.Tests", "bin", "Debug", "net8.0", "Mau.Runtime.Tests.exe");
            if (!RunProcess(runtimeExe, ""))
            {
                Console.WriteLine("FAIL: 运行时测试失败");
                return 1;
            }

            // [段3b] 积木库测试（L4——积木谱全局跑测；Mau.Bricks 程序集已退役，积木验证 = 文本库内嵌编译跑测）
            Console.WriteLine("[3b/4] 积木库测试（L4）");
            if (CommandBricks.RunGlobalTest() != 0)
            {
                Console.WriteLine("FAIL: 积木库测试失败");
                return 1;
            }

            // [段3c] 积木索引校验（L4.5）——注册表与 INDEX.md 一致性
            Console.WriteLine("[3c/4] 积木索引校验（L4.5）");
            if (CommandBricks.VerifyIndex() != 0)
            {
                Console.WriteLine("FAIL: 积木索引校验失败");
                return 1;
            }
            if (CommandBricks.CheckLicense() != 0)
            {
                Console.WriteLine("FAIL: 积木文件头校验失败");
                return 1;
            }

            // [段4] 黄金哈希对比（L3）——哈希清单反证改动范围（黄金哈希化 2026-08-13：全文退役，一颗哈希一行）
            Console.WriteLine("[4/4] 黄金哈希对比（L3）");
            // 目录即清单——扫描 cases/*.mau → golden-sha.txt 哈希对照
            System.Collections.Generic.Dictionary<string, string> goldenHashes = LoadGoldenHashes(root);
            string casesDir = Path.Combine(root, "Mau.Snapshots", "cases");
            string[] caseFiles = Directory.GetFiles(casesDir, "*.mau");
            System.Array.Sort(caseFiles, StringComparer.Ordinal);
            for (int g = 0; g < caseFiles.Length; g++)
            {
                string caseName = Path.GetFileName(caseFiles[g]);
                string flowName = Program.FlowNameFromPath(caseFiles[g]);
                string expectedName = "FL_" + flowName + ".cs";
                if (!VerifyGoldenHash(root, caseName, expectedName, flowName, update, goldenHashes))
                {
                    return 1;
                }
            }
            if (update)
            {
                SaveGoldenHashes(root, goldenHashes);
                Console.WriteLine("黄金哈希清单已更新: golden-sha.txt（" + goldenHashes.Count.ToString() + " 份）");
            }

            // [段5] build 闭环验证（L6——Roslyn Emit + ALC 加载 + 真实运行）
            Console.WriteLine("[5/5] build 闭环验证（L6）");
            if (!VerifyBuildLoop(root))
            {
                Console.WriteLine("FAIL: build 闭环验证失败");
                return 1;
            }

            // [段5b] 端到端测试（L6——.mau → Roslyn Emit → ALC 加载 → 行为断言）
            Console.WriteLine("[5b/5] 端到端测试（L6）");
            string e2eExe = Path.Combine(root, "Mau.E2E", "bin", "Debug", "net8.0", "Mau.E2E.exe");
            if (!RunProcess(e2eExe, ""))
            {
                Console.WriteLine("FAIL: 端到端测试失败");
                return 1;
            }

            Console.WriteLine("MAU_CHECKS_OK");
            return 0;
        }

        /// <summary>
        /// build 闭环验证——.mau → Roslyn Emit → ALC 加载 → Fire/Tick → 断言
        /// </summary>
        /// <param name="root">workspace 根</param>
        /// <returns>通过</returns>
        private static bool VerifyBuildLoop(string root)
{
            // [1] 编译 file_convert 生成物（v2 翻译器——Roslyn Emit，无 SDK）
            string caseFile = Path.Combine(root, "Mau.Snapshots", "cases", "file_convert.mau");
            string source;
            try
            {
                source = File.ReadAllText(caseFile);
            }
            catch
            {
                Console.WriteLine("FAIL: build 闭环用例缺失——" + caseFile);
                return false;
            }
            CompileResultV2 compileResult = MauCompilerV2.Compile(source, "FileConvert");
            if (!compileResult.Success)
            {
                Console.WriteLine("FAIL: build 闭环——翻译失败");
                return false;
            }
            string pocketRoot = Path.Combine(Path.GetTempPath(), "mau_gate_pocket_" + Guid.NewGuid().ToString("N").Substring(0, 8));
            MauPocketCompiler compiler = new MauPocketCompiler(pocketRoot);
            MauPocketCompileResult pocketResult = compiler.Compile(compileResult.GeneratedCode, "FileConvert");
            if (!pocketResult.Success)
            {
                Console.WriteLine("FAIL: build 闭环——Roslyn Emit 失败");
                for (int i = 0; i < pocketResult.Diagnostics.Length; i = i + 1)
                {
                    Console.WriteLine("  " + pocketResult.Diagnostics[i]);
                }
                return false;
            }

            // [2] 反射加载 + Fire + Tick + 断言（v2 生成物——枚举状态机，不经 FlowHandle）
            string inputFile = Path.Combine(Path.GetTempPath(), "mau_gate_input_" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".txt");
            string outputFile = Path.Combine(Path.GetTempPath(), "mau_gate_output_" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".txt");
            System.Reflection.Assembly? asm = null;
            try
            {
                File.WriteAllText(inputFile, "mau-gate-check");
                asm = System.Reflection.Assembly.LoadFrom(pocketResult.AssemblyPath);
                Type? flowType = asm.GetType("Mau.Generated.FileConvert");
                if (flowType == null)
                {
                    Console.WriteLine("FAIL: build 闭环——未找到生成物类型 Mau.Generated.FileConvert");
                    return false;
                }
                object? flow = Activator.CreateInstance(flowType);
                if (flow == null)
                {
                    Console.WriteLine("FAIL: build 闭环——无法实例化生成物");
                    return false;
                }
                MethodInfo? fire = flowType.GetMethod("FireInput");
                if (fire == null)
                {
                    Console.WriteLine("FAIL: build 闭环——未找到 FireInput");
                    return false;
                }
                fire.Invoke(flow, new object[] { inputFile, outputFile });
                MethodInfo? tick = flowType.GetMethod("Tick");
                MethodInfo? isDone = flowType.GetMethod("IsConvDone");
                if (tick == null || isDone == null)
                {
                    Console.WriteLine("FAIL: build 闭环——未找到 Tick/IsConvDone");
                    return false;
                }
                bool done = false;
                for (int i = 0; i < 400; i = i + 1)
                {
                    tick.Invoke(flow, new object[] { i });
                    done = (bool)isDone.Invoke(flow, null)!;
                    if (done)
                    {
                        break;
                    }
                }
                if (!done)
                {
                    Console.WriteLine("FAIL: build 闭环——S_Conv 未达 Done（文件转换未完成）");
                    return false;
                }
                if (!File.Exists(outputFile))
                {
                    Console.WriteLine("FAIL: build 闭环——输出文件不存在");
                    return false;
                }
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine("FAIL: build 闭环——运行时异常: " + ex.Message);
                return false;
            }
            finally
            {
                try
                {
                    if (File.Exists(inputFile))
                    {
                        File.Delete(inputFile);
                    }
                    if (File.Exists(outputFile))
                    {
                        File.Delete(outputFile);
                    }
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
        /// 运行子进程并检查退出码——输出直接继承控制台
        /// </summary>
        /// <param name="fileName">可执行文件</param>
        /// <param name="arguments">参数</param>
        /// <returns>退出码为 0</returns>
        private static bool RunProcess(string fileName, string arguments)
{
            // RT.5（2026-08-13）——测试进程级 watchdog：默认 120 秒预算，卡死测试不再无限挂起
            int timeoutMs = 120000;
            ProcessStartInfo psi = new ProcessStartInfo();
            psi.FileName = fileName;
            psi.Arguments = arguments;
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
            Process? p;
            try
            {
                p = Process.Start(psi);
            }
            catch
            {
                Console.WriteLine("FAIL: 无法启动进程——" + fileName);
                return false;
            }
            if (p == null)
            {
                Console.WriteLine("FAIL: 进程启动失败——" + fileName);
                return false;
            }
            // [段1] 超时等待——先等退出再收输出（RT.4 进程树强杀：子进程一并终结，杜绝残留锁文件）
            bool exited = p.WaitForExit(timeoutMs);
            if (!exited)
            {
                try
                {
                    p.Kill(true);
                }
                catch (InvalidOperationException)
                {
                    // 进程已在强杀前自行退出——正常路径
                }
                try
                {
                    p.WaitForExit(5000);
                }
                catch (Exception)
                {
                    // 强杀等待失败不阻塞主流程
                }
            }
            // [段2] 收集输出——成功静默 / 失败尾部保留（D19：错误/结论在尾部）/ --verbose 全透传
            string stdout = p.StandardOutput.ReadToEnd();
            string stderr = p.StandardError.ReadToEnd();
            if (!exited)
            {
                // RT.5 卡死 dump——超时预算透明 + 卡死点输出留证（尾部 20 行）
                Console.WriteLine("FAIL: 测试进程超时（" + (timeoutMs / 1000).ToString()
                    + "s 未退出）已强杀进程树: " + fileName);
                Console.WriteLine(CliSupport.TailLines(stdout + "\n" + stderr, 20));
                return false;
            }
            if (p.ExitCode == 0)
            {
                // 成功——默认静默（环节标题已是关键节点）；--verbose 输出全部细节
                CliSupport.Detail(stdout);
                if (stderr.Trim().Length > 0)
                {
                    CliSupport.Detail(stderr);
                }
                return true;
            }
            // 失败——尾部保留输出（错误/结论在尾部）+ 错误流
            Console.WriteLine(CliSupport.TailLines(stdout + "\n" + stderr, 20));
            return false;
        }/// <summary>
/// build 失败文件锁诊断——探测目标 exe 是否被残留进程占用（RT.4——2026-08-13）
/// </summary>
/// <param name = "exePath">构建产物可执行文件路径</param>
private static void DiagnoseBuildLock(string exePath)
{
    string locked = CliSupport.FindLockedFile(new string[] { exePath });
    if (locked.Length > 0)
    {
        Console.WriteLine("  诊断: 构建产物被进程占用（残留测试进程未退出）——" + locked);
        Console.WriteLine("  对策: 结束残留 dotnet/测试进程后重试（mau kill --clean 清理僵尸注册）");
    }
}/// <summary>
/// 加载黄金哈希清单——golden-sha.txt（每行：文件名 空格 64 位哈希）
/// </summary>
/// <param name = "root">workspace 根</param>
/// <returns>文件名 → 哈希字典</returns>
private static System.Collections.Generic.Dictionary<string, string> LoadGoldenHashes(string root)
{
    System.Collections.Generic.Dictionary<string, string> hashes = new System.Collections.Generic.Dictionary<string, string>(StringComparer.Ordinal);
    string path = Path.Combine(root, "Mau.Snapshots", "golden-sha.txt");
    if (!File.Exists(path))
    {
        return hashes;
    }

    string[] lines = File.ReadAllLines(path);
    for (int i = 0; i < lines.Length; i++)
    {
        string trimmed = lines[i].Trim();
        if (trimmed.Length == 0)
        {
            continue;
        }

        int space = trimmed.IndexOf(' ');
        if (space < 0)
        {
            continue;
        }

        hashes[trimmed.Substring(0, space)] = trimmed.Substring(space + 1);
    }

    return hashes;
} 
/// <summary>
/// 保存黄金哈希清单——按名排序，每份一行
/// </summary>
/// <param name = "root">workspace 根</param>
/// <param name = "hashes">文件名 → 哈希字典</param>
 private  static  void  SaveGoldenHashes ( string  root ,  System . Collections . Generic . Dictionary < string ,  string > hashes ) { System . Collections . Generic . List < string > names  =  new  System . Collections . Generic . List < string > ( hashes . Keys ) ;  names . Sort ( StringComparer . Ordinal ) ;  StringBuilder  sb  =  new  StringBuilder ( ) ;  for  ( int  i  =  0 ;  i < names . Count ;  i ++ ) { if  ( i > 0 ) { sb . Append ( '\n' ) ;  } sb . Append ( names [ i ] ) ;  sb . Append ( ' ' ) ;  sb . Append ( hashes [ names [ i ] ] ) ;  } File . WriteAllText ( Path . Combine ( root ,  "Mau.Snapshots" ,  "golden-sha.txt" ) ,  sb . ToString ( ) ) ;  } 
/// <summary>
/// 单份黄金哈希验证——编译 Mau 源 → 剥离内嵌段 → SHA256 → 与清单哈希对比（哈希哨兵：反证改动范围）
/// </summary>
/// <param name = "root">workspace 根</param>
/// <param name = "caseName">cases/ 下的 .mau 文件名</param>
/// <param name = "expectedName">清单键名（FL_ 前缀黄金文件名）</param>
/// <param name = "flowName">流程名——PascalCase</param>
/// <param name = "update">true=更新哈希（不对比）</param>
/// <param name = "hashes">哈希清单（更新时写入）</param>
/// <returns>通过</returns>
 private  static  bool  VerifyGoldenHash ( string  root ,  string  caseName ,  string  expectedName ,  string  flowName ,  bool  update ,  System . Collections . Generic . Dictionary < string ,  string > hashes ) {
            string caseFile = Path.Combine(root, "Mau.Snapshots", "cases", caseName);
            string source;
            try
            {
                source = File.ReadAllText(caseFile);
            }
            catch
            {
                Console.WriteLine("FAIL: 黄金哈希源缺失——" + caseFile);
                return false;
            }

            CompileResultV2 result = MauCompilerV2.Compile(source, flowName);
            if (!result.Success)
            {
                Console.WriteLine("FAIL: 黄金哈希源编译失败——" + caseName);
                return false;
            }

            // 哈希对象 = 剥离内嵌积木段后的语料骨架（积木改动不漂移；语料/生成器改动漂移 = 改动影响面反证）
            string stripped = MauCompilerV2.StripBrickSectionsV2(result.GeneratedCode);
            string hash = CliSupport.ComputeSha256(stripped);

            if (update)
            {
                hashes[expectedName] = hash;
                Console.WriteLine("UPDATED: " + expectedName + " → " + hash);
                return true;
            }

            string? recorded;
            if (!hashes.TryGetValue(expectedName, out recorded) || recorded == null)
            {
                Console.WriteLine("FAIL: 黄金哈希缺失——" + expectedName + "（mau test --update 生成）");
                return false;
            }
            if (recorded != hash)
            {
                Console.WriteLine("FAIL: 生成漂移——哈希不一致 (" + caseName + ")");
                Console.WriteLine("  清单: " + recorded);
                Console.WriteLine("  实际: " + hash);
                return false;
            }
            return true;
        }
}
}
