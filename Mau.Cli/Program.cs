using System;
using System.IO;
using System.Text;
using Mau.Translator;
using Mau.Development;
using System.Security.Cryptography;

namespace Mau.Cli
{
    /// <summary>
    /// Mau 编译器入口——独立编译进程
    /// </summary>
    public static class Program
    {
        /// <summary>
        /// 入口——命令路由转发（Dispatch 独立成公开方法：聚合器进程内调用复用，D22 不产生第二实例）
        /// </summary>
        /// <param name="args">命令行参数</param>
        /// <returns>退出码——0 成功，非 0 失败</returns>
        public static int Main(string[] args)
{
            // 时序报告——每个指令输出头尾两行（工具主动观测：指令名/起始/耗时/退出码；失败也报，直接可见）
            DateTime start = DateTime.Now;
            string commandName = args.Length > 0 ? args[0] : "(help)";
            Console.WriteLine("[mau " + commandName + "] 起始 " + start.ToString("HH:mm:ss.fff"));
            int exitCode;
            // ⑦ 单实例互斥（D22 + D31）——同一时刻仅一个交互式 Mau 进程，从时序上排除自锁（MSB3027 家族）。
            // 豁免：serve-work（服务工作进程——管道通讯常驻，与指令不竞争）；MAU_INNER_CHILD=1（父进程已协调的内部子进程——publish 冒烟等）
            if (!IsInnerProcess(args))
            {
                using (System.Threading.Mutex? mutex = AcquireCommandMutex())
                {
                    if (mutex == null)
                    {
                        exitCode = 1;
                    }
                    else
                    {
                        exitCode = Dispatch(args);
                    }
                }
            }
            else
            {
                exitCode = Dispatch(args);
            }

            DateTime end = DateTime.Now;
            long elapsedMs = (long)(end - start).TotalMilliseconds;
            Console.WriteLine("[mau " + commandName + "] 完成 " + end.ToString("HH:mm:ss.fff") + " | " + elapsedMs + "ms | exit " + exitCode);
            return exitCode;
        }        /// <summary>
        /// 命令路由——独立入口（Main 与聚合器进程内调用共用）
        /// </summary>
        /// <param name="args">命令行参数</param>
        /// <returns>退出码——0 成功，非 0 失败</returns>
        public static int Dispatch(string[] args)
{
            CliSupport.ParseVerbose(args);
            // [段1] 命令路由（积木注册表已退役——翻译器构筑期经 BrickIndex 查询 Bricks/index.json）
            if (args.Length == 0)
            {
                Console.WriteLine("Mau Translator " + Mau.Runtime.VersionInfo.GetEntryVersion());
                Console.WriteLine("构筑:   mau verify <file.mau> | mau gen <file.mau> -o <dir> | mau build <file.mau|组.mauproj> -o <dir> [--sdk] | mau publish -o <dir> | mau up [-f 清单] [--force]");
                Console.WriteLine("验证:   mau test [--update] | mau check [--update|--syntax|--bricks|--selftest]");
                Console.WriteLine("运行:   mau run <file.mau> [--fire Method key=val ...] [--ticks N] [--sdk] | mau debug <file.mau> [--ticks N] [--step] [--pause-on T_X|P_Y] [--trace] | mau serve <file.mau> [--port N] | mau serve spawn|stop|status|call ...");
                Console.WriteLine("进程:   mau ps | mau status <名> | mau snapshot <名> | mau kill <名|--all|--clean>");
                Console.WriteLine("积木:   mau bricks list|index|test|reseal");
                Console.WriteLine("组工程: mau export <组.mauproj> [-o <dir>] | mau import <组包目录> -o <目录> [--force]");
                return 0;
            }

            string command = args[0];
            if (command == "up")
            {
                return CommandUp.Execute(CliSupport.Tail(args));
            }
            if (command == "verify")
            {
                return CommandVerify(args);
            }
            if (command == "gen")
            {
                return CommandGen(args);
            }
            if (command == "build")
            {
                return CommandBuild(args);
            }
            if (command == "publish")
            {
                return CommandPublish.Execute(CliSupport.Tail(args));
            }
            if (command == "test")
            {
                bool update = args.Length > 1 && args[1] == "--update";
                return MauTestRunner.Run(update);
            }
            if (command == "run")
            {
                return CommandRun.Execute(CliSupport.Tail(args));
            }
            if (command == "debug")
            {
                return CommandDebug.Execute(CliSupport.Tail(args));
            }
            if (command == "serve")
            {
                string[] serveArgs = CliSupport.Tail(args);
                if (serveArgs.Length > 0 && IsServeSubCommand(serveArgs[0]))
                {
                    return CommandServeManager.Execute(serveArgs);
                }
                return CommandServe.Execute(serveArgs);
            }
            if (command == "serve-work")
            {
                return CommandServeWork.Execute(CliSupport.Tail(args));
            }
            if (command == "ps")
            {
                return CommandSupervisor.Ps();
            }
            if (command == "status")
            {
                return CommandSupervisor.Status(args.Length > 1 ? args[1] : "");
            }
            if (command == "snapshot")
            {
                return CommandSupervisor.Snapshot(args.Length > 1 ? args[1] : "");
            }
            if (command == "kill")
            {
                string? name = args.Length > 1 ? args[1] : null;
                bool clean = args.Length > 1 && args[1] == "--clean";
                bool all = args.Length > 1 && args[1] == "--all";
                if (all)
                {
                    return CommandSupervisor.KillAll();
                }
                return CommandSupervisor.Kill(name, clean);
            }
            if (command == "bricks")
            {
                return CommandBricks.Execute(CliSupport.Tail(args));
            }
            if (command == "export")
            {
                return CommandMauProj.ExecuteExport(CliSupport.Tail(args));
            }
            if (command == "import")
            {
                return CommandMauProj.ExecuteImport(CliSupport.Tail(args));
            }
            if (command == "check")
            {
                return CommandCheck.Execute(CliSupport.Tail(args));
            }

            Console.WriteLine("未知命令: " + command);
            return 1;
        }        /// <summary>
        /// verify 命令——只静态验证
        /// </summary>
        /// <param name="args">命令行参数</param>
        /// <returns>退出码</returns>
        private static int CommandVerify(string[] args)
{
            if (args.Length < 2)
            {
                Console.WriteLine("用法: mau verify <file.mau>");
                return 1;
            }
            string path = args[1];
            if (!File.Exists(path))
            {
                Console.WriteLine("文件不存在: " + path);
                return 1;
            }

            string source = File.ReadAllText(path);
            string flowName = FlowNameFromPath(path);
            CompileResultV2 result = MauCompilerV2.Compile(source, flowName);
            PrintDiagnostics(path, result.Diagnostics);

            if (result.Success)
            {
                Console.WriteLine("验证通过: " + flowName);
                return 0;
            }
            return 1;
        }
        /// <summary>
        /// gen 命令——只生成 C# 源码
        /// </summary>
        /// <param name="args">命令行参数</param>
        /// <returns>退出码</returns>
        private static int CommandGen(string[] args)
{
            if (args.Length < 2)
            {
                Console.WriteLine("用法: mau gen <file.mau> -o <dir>");
                return 1;
            }
            string path = args[1];
            if (!File.Exists(path))
            {
                Console.WriteLine("文件不存在: " + path);
                return 1;
            }

            string outDir = ".";
            for (int i = 2; i < args.Length - 1; i++)
            {
                if (args[i] == "-o")
                {
                    outDir = args[i + 1];
                }
            }
            if (!Directory.Exists(outDir))
            {
                Directory.CreateDirectory(outDir);
            }

            string source = File.ReadAllText(path);
            string flowName = FlowNameFromPath(path);
            CompileResultV2 result = MauCompilerV2.Compile(source, flowName);
            PrintDiagnostics(path, result.Diagnostics);
            if (!result.Success)
            {
                return 1;
            }

            string outFile = Path.Combine(outDir, "FL_" + flowName + ".cs");
            File.WriteAllText(outFile, result.GeneratedCode);
            Console.WriteLine("生成: " + outFile);
            return 0;
        }
        /// <summary>
        /// build 命令——验证 + 生成 + 编译（默认 Roslyn Emit 无 SDK；--sdk 走环境 dotnet build）
        /// </summary>
        /// <param name="args">命令行参数</param>
        /// <returns>退出码</returns>
        private static int CommandBuild(string[] args)
        {
            string? mauFile = null;
            string outDir = ".";
            bool useSdk = false;

            for (int i = 1; i < args.Length; i = i + 1)
            {
                if (args[i] == "-o" && i + 1 < args.Length)
                {
                    outDir = args[i + 1];
                    i = i + 1;
                }
                else if (args[i] == "--sdk")
                {
                    useSdk = true;
                }
                else if (mauFile == null)
                {
                    mauFile = args[i];
                }
            }
            if (mauFile == null)
            {
                Console.WriteLine("用法: mau build <file.mau> -o <dir> [--sdk]");
                Console.WriteLine("  默认: Roslyn 内存编译（无需 .NET SDK）");
                Console.WriteLine("  --sdk: 走环境 dotnet build（开发调试用）");
                return 1;
            }
            if (!File.Exists(mauFile))
            {
                Console.WriteLine("文件不存在: " + mauFile);
                return 1;
            }

            if (mauFile.EndsWith(".mauproj", StringComparison.OrdinalIgnoreCase))
            {
                return CommandMauProj.Build(mauFile, outDir, useSdk);
            }

            // [1] 解析 + 验证
            string source = File.ReadAllText(mauFile);
            string flowName = FlowNameFromPath(mauFile);
            CompileResultV2 result = MauCompilerV2.Compile(source, flowName);
            PrintDiagnostics(mauFile, result.Diagnostics);
            if (!result.Success)
            {
                Console.WriteLine("构建失败: 验证未通过——" + flowName);
                return 1;
            }

            // [2] 输出目录
            if (!Directory.Exists(outDir))
            {
                Directory.CreateDirectory(outDir);
            }
            string className = "FL_" + flowName;
            string dllPath = Path.Combine(outDir, className + ".dll");

            // [3] 编译
            if (useSdk)
            {
                string tempDir = Path.Combine(Path.GetTempPath(), "mau_build_sdk_" + Guid.NewGuid().ToString("N").Substring(0, 8));
                TempProjectBuilder.BuildResult buildResult = TempProjectBuilder.Build(result.GeneratedCode, className, tempDir);
                if (!buildResult.Success)
                {
                    Console.WriteLine("构建失败: C# 编译错误");
                    if (buildResult.BuildOutput != null)
                    {
                        Console.WriteLine(buildResult.BuildOutput);
                    }
                    return 2;
                }
                File.Copy(buildResult.DllPath!, dllPath, true);
                try
                {
                    if (Directory.Exists(tempDir))
                    {
                        Directory.Delete(tempDir, true);
                    }
                }
                catch
                {
                    // 临时目录清理失败不影响结果
                }
            }
            else
            {
                string pocketRoot = Path.Combine(Path.GetTempPath(), "mau_build_pocket_" + Guid.NewGuid().ToString("N").Substring(0, 8));
                MauPocketCompiler compiler = new MauPocketCompiler(pocketRoot);
                MauPocketCompileResult compileResult = compiler.Compile(result.GeneratedCode, className);
                if (!compileResult.Success)
                {
                    Console.WriteLine("构建失败: Roslyn 编译错误");
                    for (int i = 0; i < compileResult.Diagnostics.Length; i = i + 1)
                    {
                        Console.WriteLine("  " + compileResult.Diagnostics[i]);
                    }
                    return 2;
                }
                File.Copy(compileResult.AssemblyPath, dllPath, true);
                try
                {
                    if (Directory.Exists(pocketRoot))
                    {
                        Directory.Delete(pocketRoot, true);
                    }
                }
                catch
                {
                    // 临时目录清理失败不影响结果
                }
            }

            Console.WriteLine("构建成功: " + dllPath + "（" + (useSdk ? "dotnet build" : "Roslyn Emit") + "）");
            return 0;
        }

        /// <summary>
        /// 输出诊断——统一格式 文件:行号: 错误码: 消息
        /// </summary>
        /// <param name="path">源文件路径</param>
        /// <param name="diags">诊断列表</param>
        public static void PrintDiagnostics(string path, System.Collections.Generic.List<MauDiagnostic> diags)
        {
            for (int i = 0; i < diags.Count; i++)
            {
                MauDiagnostic d = diags[i];
                Console.WriteLine(path + ":" + d.Line + ": " + d.Code + ": " + d.Message);
            }
        }

        /// <summary>
        /// 从文件路径推导流程名——file_convert.mau → FileConvert
        /// </summary>
        /// <param name="path">文件路径</param>
        /// <returns>PascalCase 流程名</returns>
        public static string FlowNameFromPath(string path)
        {
            string baseName = Path.GetFileNameWithoutExtension(path);
            string[] parts = baseName.Split('_');
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < parts.Length; i++)
            {
                if (parts[i].Length == 0)
                {
                    continue;
                }
                string head = parts[i].Substring(0, 1).ToUpperInvariant();
                string tail = parts[i].Length > 1 ? parts[i].Substring(1) : "";
                sb.Append(head);
                sb.Append(tail);
            }
            return sb.ToString();
        }
    /// <summary>
    /// 判断 serve 子命令——spawn/stop/status/call 走服务管理，其余走 HTTP 面板
    /// </summary>
    /// <param name="first">第一个参数</param>
    /// <returns>是否服务管理子命令</returns>
    private static bool IsServeSubCommand(string first)
    {
        return first == "spawn" || first == "stop" || first == "status" || first == "call";
    }
/// <summary>
/// 内部子进程判定——serve-work 服务进程或 MAU_INNER_CHILD=1（父进程已协调）豁免单实例互斥
/// </summary>
/// <param name = "args">命令行参数</param>
/// <returns>true=豁免</returns>
private static bool IsInnerProcess(string[] args)
{
    if (args.Length > 0 && args[0] == "serve-work")
    {
        return true;
    }

    string? inner = Environment.GetEnvironmentVariable("MAU_INNER_CHILD");
    return inner != null && inner == "1";
} 
/// <summary>
/// 获取指令互斥——获取失败返回 null（另一 Mau.exe 运行中，输出残留 PID 诊断后立即失败）
/// </summary>
/// <returns>互斥句柄；null=获取失败</returns>
 private  static  System . Threading . Mutex ? AcquireCommandMutex ( ) { bool  createdNew  =  false ;  System . Threading . Mutex  mutex  =  new  System . Threading . Mutex ( false ,  "MauCmdMutex_v1" ,  out  createdNew ) ;  bool  acquired  =  false ;  try  { acquired  =  mutex . WaitOne ( 0 ) ;  } catch  ( System . Threading . AbandonedMutexException ) { // 原持有者已崩溃——视为可获取（残留互斥自动回收）
acquired  =  true ;  } catch  ( Exception  ex ) { Console . Error . WriteLine ( "FAIL: 指令互斥获取异常——" + ex . Message ) ;  mutex . Dispose ( ) ;  return  null ;  } if  ( ! acquired ) { Console . Error . WriteLine ( "FAIL: 另一 Mau.exe 运行中——单实例互斥（D22/D31），先结束它再执行指令:" ) ;  System . Diagnostics . Process [ ]  processes  =  System . Diagnostics . Process . GetProcessesByName ( "Mau" ) ;  for  ( int  i  =  0 ;  i < processes . Length ;  i  =  i + 1 ) { if  ( processes [ i ] . Id != System . Diagnostics . Process . GetCurrentProcess ( ) . Id ) { Console . Error . WriteLine ( "  PID " + processes [ i ] . Id + " | " + processes [ i ] . ProcessName ) ;  } } mutex . Dispose ( ) ;  return  null ;  } return  mutex ;  }

}
}
