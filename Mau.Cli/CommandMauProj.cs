using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using Mau.Contracts;
using Mau.Development;
using Mau.Translator;

namespace Mau.Cli
{
    /// <summary>
    /// mauproj 组工程命令——组模式构筑 / 导出组包 / 导入组包
    /// </summary>
    public static class CommandMauProj
    {
        /// <summary>
        /// 组模式构筑——全组 .mau 验证 + 生成 + 一次 Emit 编译为 FL_&lt;组名&gt;.dll
        /// </summary>
        /// <param name="mauprojPath">mauproj 文件路径</param>
        /// <param name="outDir">输出目录</param>
        /// <param name="useSdk">true=走环境 dotnet build（--sdk），false=Roslyn Emit</param>
        /// <returns>退出码——0 成功</returns>
        public static int Build(string mauprojPath, string outDir, bool useSdk)
{
            // [段1] 解析 mauproj
            MauProjParseResult parsed = MauProjFile.Load(mauprojPath);
            if (parsed.Error.Length > 0)
            {
                Console.WriteLine("FAIL: " + parsed.Error);
                return 1;
            }
            if (parsed.Warning.Length > 0)
            {
                Console.WriteLine("WARN: " + parsed.Warning);
            }
            MauProjFile proj = parsed.File!;
            string assemblyName = "FL_" + proj.Name;

            // [段1b] 引用确定性（D25）——解析引用: 声明 → 输出解析后引用清单；缺失即失败
            List<string> refErrors;
            List<string> refs = proj.ResolveReferences(AppDomain.CurrentDomain.BaseDirectory, out refErrors);
            if (refErrors.Count > 0)
            {
                for (int i = 0; i < refErrors.Count; i = i + 1)
                {
                    Console.WriteLine("FAIL: " + refErrors[i]);
                }
                return 1;
            }
            if (refs.Count > 0)
            {
                Console.WriteLine("引用解析: " + refs.Count + " 个");
                for (int i = 0; i < refs.Count; i = i + 1)
                {
                    Console.WriteLine("  " + Path.GetFileName(refs[i]) + " → " + refs[i]);
                }
            }
            else
            {
                Console.WriteLine("引用解析: 无（仅基座必需 Mau.Runtime/Mau.Contracts）");
            }

            // [段2] 收集成员文件
            List<string> files = proj.CollectFiles();
            if (files.Count == 0)
            {
                Console.WriteLine("FAIL: 组 " + proj.Name + " 无成员 .mau 文件");
                return 1;
            }
            Console.WriteLine("组 " + proj.Name + "：成员 " + files.Count + " 个");

            // [段2b] 产品积木上下文——bricks: 声明 → 绝对路径 → BrickContext（无声明 = null 仅基座）
            BrickContext? ctx = null;
            if (proj.Bricks.Count > 0)
            {
                List<string> brickDirs = new List<string>();
                for (int i = 0; i < proj.Bricks.Count; i++)
                {
                    string dir = Path.GetFullPath(Path.Combine(proj.DirectoryPath, proj.Bricks[i]));
                    brickDirs.Add(dir);
                }
                ctx = BrickContext.FromProductDirs(brickDirs);
                if (ctx.ProductError.Length > 0)
                {
                    Console.WriteLine("FAIL: " + ctx.ProductError);
                    return 1;
                }
                Console.WriteLine("产品积木: " + ctx.ProductCount + " 个（" + brickDirs.Count + " 目录）");
            }

            // [段3] 逐个验证 + 生成——任何失败整组拒绝（组编译：骨架数组 + 共享 BRIKGROUP）
            string[] texts = new string[files.Count];
            string[] names = new string[files.Count];
            for (int i = 0; i < files.Count; i++)
            {
                texts[i] = File.ReadAllText(files[i]);
                names[i] = Program.FlowNameFromPath(Path.GetFileNameWithoutExtension(files[i]));
            }
            GroupCompileResultV2 groupResult = MauCompilerV2.CompileGroupV2(texts, names, ctx);
            List<string> sources = new List<string>();
            List<string> classNames = new List<string>();
            for (int i = 0; i < groupResult.Results.Count; i++)
            {
                CompileResultV2 result = groupResult.Results[i];
                if (!result.Success)
                {
                    Program.PrintDiagnostics(files[i], result.Diagnostics);
                    Console.WriteLine("构建失败: " + Path.GetFileName(files[i]) + " 验证未通过——整组拒绝");
                    return 1;
                }
                sources.Add(result.GeneratedCode);
                classNames.Add("FL_" + names[i]);
                Console.WriteLine("  验证通过: " + Path.GetFileName(files[i]) + " → FL_" + names[i]);
            }
            if (groupResult.BrickGroupSource.Length > 0)
            {
                sources.Add(groupResult.BrickGroupSource);
                classNames.Add("FL_BRIKGROUP");
                Console.WriteLine("  内嵌闭包: " + (sources.Count - 1) + " 个 BRIK（共享 BRIKGROUP）");
            }

            // [段4] 输出目录
            if (!Directory.Exists(outDir))
            {
                Directory.CreateDirectory(outDir);
            }
            string dllPath = Path.Combine(outDir, assemblyName + ".dll");

            // [段5] 编译——默认 Roslyn Emit（CompileManyWithRefs 显式引用集）；--sdk 走临时项目 dotnet build
            if (useSdk)
            {
                if (!BuildSdkGroup(sources, classNames, assemblyName, dllPath, refs.ToArray()))
                {
                    return 2;
                }
            }
            else
            {
                string pocketRoot = Path.Combine(Path.GetTempPath(), "mau_build_group_" + Guid.NewGuid().ToString("N").Substring(0, 8));
                MauPocketCompiler compiler = new MauPocketCompiler(pocketRoot);
                MauPocketCompileResult compileResult = compiler.CompileManyWithRefs(sources.ToArray(), classNames.ToArray(), assemblyName, refs.ToArray());
                if (!compileResult.Success)
                {
                    Console.WriteLine("构建失败: Roslyn 编译错误");
                    for (int i = 0; i < compileResult.Diagnostics.Length; i++)
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

            Console.WriteLine("构建成功: " + dllPath + "（组模式 " + (useSdk ? "dotnet build" : "Roslyn Emit") + "）");
            return 0;
        }

        /// <summary>
        /// --sdk 组模式——临时项目 + dotnet build（引用宿主输出目录全部 dll）
        /// </summary>
        /// <param name="sources">C# 源码数组</param>
        /// <param name="classNames">类名数组</param>
        /// <param name="assemblyName">程序集名</param>
        /// <param name="dllPath">输出 DLL 路径</param>
        /// <returns>true=成功</returns>
        private static bool BuildSdkGroup(List<string> sources, List<string> classNames, string assemblyName, string dllPath, string[] refs)
{
            string tempDir = Path.Combine(Path.GetTempPath(), "mau_build_group_sdk_" + Guid.NewGuid().ToString("N").Substring(0, 8));
            try
            {
                Directory.CreateDirectory(tempDir);

                // [段1] 写全部 .cs 文件
                for (int i = 0; i < sources.Count; i++)
                {
                    File.WriteAllText(Path.Combine(tempDir, classNames[i] + ".cs"), sources[i], Encoding.UTF8);
                }

                // [段2] 写 .csproj——引用基座必需（宿主目录 Mau.Runtime/Mau.Contracts）+ 显式引用清单（D25 引用确定性；禁止隐式宿主目录全量）
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("<Project Sdk=\"Microsoft.NET.Sdk\">");
                sb.AppendLine("  <PropertyGroup>");
                sb.AppendLine("    <TargetFramework>net8.0</TargetFramework>");
                sb.AppendLine("    <OutputType>Library</OutputType>");
                sb.AppendLine("    <Nullable>enable</Nullable>");
                sb.AppendLine("    <ImplicitUsings>disable</ImplicitUsings>");
                sb.AppendLine("    <AssemblyName>" + assemblyName + "</AssemblyName>");
                sb.AppendLine("    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>");
                sb.AppendLine("  </PropertyGroup>");
                sb.AppendLine("  <ItemGroup>");
                for (int i = 0; i < classNames.Count; i++)
                {
                    sb.AppendLine("    <Compile Include=\"" + classNames[i] + ".cs\" />");
                }
                sb.AppendLine("  </ItemGroup>");
                sb.AppendLine("  <ItemGroup>");
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                List<string> sdkRefs = new List<string>();
                if (Directory.Exists(baseDir))
                {
                    string[] dlls = Directory.GetFiles(baseDir, "*.dll", SearchOption.TopDirectoryOnly);
                    Array.Sort(dlls, StringComparer.OrdinalIgnoreCase);
                    for (int i = 0; i < dlls.Length; i++)
                    {
                        string name = Path.GetFileName(dlls[i]);
                        if (name == "Mau.Runtime.dll" || name == "Mau.Contracts.dll")
                        {
                            sdkRefs.Add(dlls[i]);
                        }
                    }
                }
                for (int i = 0; i < refs.Length; i++)
                {
                    if (!sdkRefs.Contains(refs[i]))
                    {
                        sdkRefs.Add(refs[i]);
                    }
                }
                for (int i = 0; i < sdkRefs.Count; i++)
                {
                    string name = Path.GetFileNameWithoutExtension(sdkRefs[i]);
                    sb.AppendLine("    <Reference Include=\"" + name + "\">");
                    sb.AppendLine("      <HintPath>" + Path.GetFullPath(sdkRefs[i]) + "</HintPath>");
                    sb.AppendLine("    </Reference>");
                }
                sb.AppendLine("  </ItemGroup>");
                sb.AppendLine("</Project>");
                string csprojPath = Path.Combine(tempDir, assemblyName + ".csproj");
                File.WriteAllText(csprojPath, sb.ToString(), Encoding.UTF8);

                // [段3] dotnet build
                ProcessStartInfo psi = new ProcessStartInfo
                {
                    FileName = "dotnet",
                    Arguments = "build \"" + csprojPath + "\" -c Release -o \"" + tempDir + "\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };
                Process? process = null;
                string stdout = "";
                string stderr = "";
                try
                {
                    process = Process.Start(psi);
                    if (process == null)
                    {
                        Console.WriteLine("构建失败: 无法启动 dotnet build 进程");
                        return false;
                    }
                    stdout = process.StandardOutput.ReadToEnd();
                    stderr = process.StandardError.ReadToEnd();
                    process.WaitForExit(60000);
                    if (process.ExitCode != 0)
                    {
                        Console.WriteLine("构建失败: C# 编译错误");
                        Console.WriteLine(stdout);
                        Console.WriteLine(stderr);
                        return false;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine("构建失败: dotnet build 执行失败——" + ex.Message);
                    return false;
                }
                finally
                {
                    if (process != null)
                    {
                        process.Dispose();
                    }
                }

                // [段4] 复制产物
                string builtDll = Path.Combine(tempDir, assemblyName + ".dll");
                if (!File.Exists(builtDll))
                {
                    Console.WriteLine("构建失败: 未找到输出 DLL——" + builtDll);
                    return false;
                }
                File.Copy(builtDll, dllPath, true);
                return true;
            }
            finally
            {
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
        }
}
}
