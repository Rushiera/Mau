using System;
using System.Diagnostics;
using System.IO;
using Mau.Translator;

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
        public static int Run()
        {
            string? root = FindWorkspaceRoot();
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
                return 1;
            }
            if (!RunProcess("dotnet", "build \"" + Path.Combine(root, "Mau.Runtime.Tests", "Mau.Runtime.Tests.csproj") + "\" -v q --nologo"))
            {
                Console.WriteLine("FAIL: 运行时测试项目构建失败");
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

            // [段3] 生成物真实运行（L5）
            Console.WriteLine("[3/4] 生成物真实运行（L5）");
            string runtimeExe = Path.Combine(root, "Mau.Runtime.Tests", "bin", "Debug", "net8.0", "Mau.Runtime.Tests.exe");
            if (!RunProcess(runtimeExe, ""))
            {
                Console.WriteLine("FAIL: 运行时测试失败");
                return 1;
            }

            // [段4] 黄金文件对比（L3）——纯 C# 内存对比
            Console.WriteLine("[4/4] 黄金文件对比（L3）");
            string caseFile = Path.Combine(root, "Mau.Snapshots", "cases", "file_convert.mau");
            string expectedFile = Path.Combine(root, "Mau.Snapshots", "expected", "FL_FileConvert.cs");
            string source;
            try
            {
                source = File.ReadAllText(caseFile);
            }
            catch
            {
                Console.WriteLine("FAIL: 黄金文件源缺失——" + caseFile);
                return 1;
            }
            CompileResult result = MauCompiler.Compile(source, "FileConvert");
            if (!result.Success)
            {
                Console.WriteLine("FAIL: 黄金文件源编译失败");
                return 1;
            }
            string expected;
            try
            {
                expected = File.ReadAllText(expectedFile).Replace("\r\n", "\n");
            }
            catch
            {
                Console.WriteLine("FAIL: 黄金文件缺失——" + expectedFile);
                return 1;
            }
            string actual = result.GeneratedCode.Replace("\r\n", "\n");
            if (expected != actual)
            {
                Console.WriteLine("FAIL: 生成漂移——黄金文件不一致");
                return 1;
            }

            Console.WriteLine("MAU_CHECKS_OK");
            return 0;
        }

        /// <summary>
        /// 查找 workspace 根——含 Mau.sln 的目录；当前目录向上优先，程序集位置兜底
        /// </summary>
        /// <returns>workspace 根或空</returns>
        private static string? FindWorkspaceRoot()
        {
            DirectoryInfo? dir = new DirectoryInfo(Directory.GetCurrentDirectory());
            while (dir != null)
            {
                string sln = Path.Combine(dir.FullName, "Mau.sln");
                if (File.Exists(sln))
                {
                    return dir.FullName;
                }
                dir = dir.Parent;
            }

            DirectoryInfo? exeDir = new DirectoryInfo(AppContext.BaseDirectory);
            while (exeDir != null)
            {
                string sln = Path.Combine(exeDir.FullName, "Mau.sln");
                if (File.Exists(sln))
                {
                    return exeDir.FullName;
                }
                exeDir = exeDir.Parent;
            }
            return null;
        }

        /// <summary>
        /// 运行子进程并检查退出码——输出直接继承控制台
        /// </summary>
        /// <param name="fileName">可执行文件</param>
        /// <param name="arguments">参数</param>
        /// <returns>退出码为 0</returns>
        private static bool RunProcess(string fileName, string arguments)
        {
            ProcessStartInfo psi = new ProcessStartInfo();
            psi.FileName = fileName;
            psi.Arguments = arguments;
            psi.UseShellExecute = false;
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
            p.WaitForExit();
            return p.ExitCode == 0;
        }
    }
}
