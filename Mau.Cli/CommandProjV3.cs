using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using Mau.Translator;

namespace Mau.Cli
{
    /// <summary>
    /// mauproj 组工程命令——统一构筑链（design-ch4-deploy.md §三）。
    /// 组翻译：.mau → BRIKGROUP.cs + 各 FL_*.cs + csproj → 落盘 public/src/&lt;组&gt;/；--build 走 dotnet build → public/app/Flows/FL_&lt;组&gt;.dll。
    /// 单 mau 也建 mauproj（dll 粒度 = mauproj 粒度 = 热重载粒度）——无双轨。
    /// </summary>
    public static class CommandProjV3
    {
        /// <summary>
        /// proj 命令入口——mau proj &lt;组.mauproj&gt; [-o srcDir] [--build] [--out dllDir]
        /// </summary>
        /// <param name="args">命令参数（不含 proj）</param>
        /// <returns>退出码——0 成功</returns>
        public static int Run(string[] args)
        {
            string? mauprojPath = null;
            string srcDir = "";
            string dllDir = "";
            bool doBuild = false;
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "-o" && i + 1 < args.Length)
                {
                    srcDir = args[i + 1];
                    i = i + 1;
                }
                else if (args[i] == "--build")
                {
                    doBuild = true;
                }
                else if (args[i] == "--out" && i + 1 < args.Length)
                {
                    dllDir = args[i + 1];
                    i = i + 1;
                }
                else if (mauprojPath == null)
                {
                    mauprojPath = args[i];
                }
            }
            if (mauprojPath == null)
            {
                Console.WriteLine("用法: mau proj <组.mauproj> [-o <srcDir>] [--build] [--out <dllDir>]");
                return 1;
            }
            if (!File.Exists(mauprojPath))
            {
                Console.WriteLine("文件不存在: " + mauprojPath);
                return 1;
            }

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
            Console.WriteLine("组 " + proj.Name + " | 版本 " + (proj.Version.Length > 0 ? proj.Version : "（无）"));

            // [段2] 输出目录——默认 public/src/<组名>/（一组一文件夹）；--build 默认 public/app/Flows/
            string root = CliSupport.FindWorkspaceRoot() ?? Directory.GetCurrentDirectory();
            if (srcDir.Length == 0)
            {
                srcDir = Path.Combine(root, "public", "src", proj.Name);
            }
            if (dllDir.Length == 0)
            {
                dllDir = Path.Combine(root, "public", "app", "Flows");
            }
            Directory.CreateDirectory(srcDir);
            Directory.CreateDirectory(dllDir);

            // [段3] 收集成员文件 + 组翻译（embedBricks=false——积木进 BRIKGROUP.cs）
            List<string> files = proj.CollectFiles();
            if (files.Count == 0)
            {
                Console.WriteLine("FAIL: 组 " + proj.Name + " 无成员 .mau 文件");
                return 1;
            }
            Console.WriteLine("成员 " + files.Count + " 个");
            string[] sources = new string[files.Count];
            string[] flowNames = new string[files.Count];
            for (int i = 0; i < files.Count; i++)
            {
                sources[i] = File.ReadAllText(files[i]);
                flowNames[i] = Program.FlowNameFromPath(files[i]);
                Console.WriteLine("  " + Path.GetFileName(files[i]) + " → FL_" + flowNames[i]);
            }
            GroupCompileResultV3 group = MauCompilerV3.CompileGroup(sources, flowNames);
            for (int i = 0; i < group.Results.Count; i++)
            {
                if (!group.Results[i].Success)
                {
                    PrintGroupDiagnostics(files[i], group.Results[i]);
                    Console.WriteLine("构建失败: " + Path.GetFileName(files[i]) + " 验证未通过——整组拒绝");
                    return 1;
                }
            }

            // [段4] 落盘——csproj + BRIKGROUP.cs + 各 FL_*.cs（确定性全量重写——陈旧窗口 0）
            string csprojPath = Path.Combine(srcDir, proj.Name + ".csproj");
            WriteCsproj(csprojPath, proj, group.Results.Count);
            string brikgroupPath = Path.Combine(srcDir, "BRIKGROUP.cs");
            File.WriteAllText(brikgroupPath, group.BrickGroupSource, Encoding.UTF8);
            Console.WriteLine("BRIKGROUP.cs: " + (group.BrickGroupSource.Length > 0 ? "积木共享闭包已生成" : "（组无积木引用）"));
            for (int i = 0; i < group.Results.Count; i++)
            {
                string outFile = Path.Combine(srcDir, "FL_" + flowNames[i] + ".cs");
                File.WriteAllText(outFile, group.Results[i].GeneratedCode, Encoding.UTF8);
            }
            Console.WriteLine("中间产物落盘: " + srcDir + "（" + (group.Results.Count + 1) + " 文件）");

            // [段5] 可选 dotnet build——标准链（PDB/可调试）
            if (doBuild)
            {
                return BuildWithDotnet(csprojPath, dllDir, proj.Name);
            }
            return 0;
        }

        /// <summary>
        /// dotnet build 标准链——编译中间产物 → Flows/FL_&lt;组&gt;.dll
        /// </summary>
        /// <param name="csprojPath">组 csproj 路径</param>
        /// <param name="dllDir">dll 输出目录</param>
        /// <param name="groupName">组名</param>
        /// <returns>退出码——0 成功</returns>
        private static int BuildWithDotnet(string csprojPath, string dllDir, string groupName)
        {
            Console.WriteLine("dotnet build: " + Path.GetFileName(csprojPath));
            ProcessStartInfo psi = new ProcessStartInfo();
            psi.FileName = "dotnet";
            psi.Arguments = "build \"" + csprojPath + "\"";
            psi.WorkingDirectory = Path.GetDirectoryName(csprojPath) ?? ".";
            psi.UseShellExecute = false;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
            try
            {
                using (Process proc = Process.Start(psi)!)
                {
                    if (proc == null)
                    {
                        Console.WriteLine("FAIL: dotnet 进程启动失败");
                        return 2;
                    }
                    string stdout = proc.StandardOutput.ReadToEnd();
                    string stderr = proc.StandardError.ReadToEnd();
                    proc.WaitForExit();
                    string tail = CliSupport.TailLines(stdout + stderr, 15);
                    if (proc.ExitCode != 0)
                    {
                        Console.WriteLine(tail);
                        Console.WriteLine("构建失败: dotnet build exit " + proc.ExitCode);
                        return 2;
                    }
                    Console.WriteLine(tail);
                    string dllPath = Path.Combine(dllDir, "FL_" + groupName + ".dll");
                    if (File.Exists(dllPath))
                    {
                        Console.WriteLine("构建成功: " + dllPath);
                        return 0;
                    }
                    Console.WriteLine("构建完成但未找到输出: " + dllPath + "（检查 csproj OutputPath）");
                    return 2;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("FAIL: dotnet build 异常——" + ex.Message);
                return 2;
            }
        }

        /// <summary>
        /// 写组 csproj——SDK 项目头 + Reference Mau-public/ dll（引用源铁律 design-ch4-deploy.md §五）
        /// </summary>
        /// <param name="path">csproj 输出路径</param>
        /// <param name="proj">mauproj 组声明</param>
        /// <param name="fileCount">FL 生成物数量（不含 BRIKGROUP）</param>
        private static void WriteCsproj(string path, MauProjFile proj, int fileCount)
        {
            // [段1] 引用解析——mauproj 引用: 声明 → Mau-public/ dll（默认基座自动带；缺失报错）
            string root = CliSupport.FindWorkspaceRoot() ?? Directory.GetCurrentDirectory();
            string mauPublic = Path.Combine(root, "Mau-public");
            List<string> refErrors;
            List<string> refs = proj.ResolveReferences(mauPublic, out refErrors);
            if (refErrors.Count > 0)
            {
                for (int i = 0; i < refErrors.Count; i++)
                {
                    Console.WriteLine("WARN: " + refErrors[i]);
                }
            }
            refs.Add(Path.Combine(mauPublic, "Mau.Runtime.dll"));
            refs.Add(Path.Combine(mauPublic, "Mau.Contracts.dll"));

            // [段2] csproj 文本——相对 src/<组>/ 的 Mau-public/ 引用（../../.. 到仓库根）
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("<Project Sdk=\"Microsoft.NET.Sdk\">");
            sb.AppendLine("  <PropertyGroup>");
            sb.AppendLine("    <TargetFramework>net8.0</TargetFramework>");
            sb.AppendLine("    <Nullable>disable</Nullable>");
            sb.AppendLine("    <ImplicitUsings>disable</ImplicitUsings>");
            sb.AppendLine("    <AssemblyName>FL_" + proj.Name + "</AssemblyName>");
            sb.AppendLine("    <RootNamespace>Mau.Generated</RootNamespace>");
            sb.AppendLine("    <OutputPath>" + "../../app/Flows/" + "</OutputPath>");
            sb.AppendLine("    <AppendTargetFrameworkToOutputPath>false</AppendTargetFrameworkToOutputPath>");
            sb.AppendLine("    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>");
            sb.AppendLine("  </PropertyGroup>");
            sb.AppendLine("  <ItemGroup>");
            sb.AppendLine("    <Compile Include=\"BRIKGROUP.cs\" />");
            for (int i = 0; i < fileCount; i++)
            {
                // FL 文件名由调用方在落盘时生成——此处按 BRIKGROUP + 通配兜底
            }
            sb.AppendLine("    <Compile Include=\"FL_*.cs\" />");
            sb.AppendLine("  </ItemGroup>");
            sb.AppendLine("  <ItemGroup>");
            for (int i = 0; i < refs.Count; i++)
            {
                string name = Path.GetFileNameWithoutExtension(refs[i]);
                // HintPath 相对 csproj 所在目录（public/src/<组>/）——仓库根为 ../../..
                string hint = Path.Combine("../../..", "Mau-public", name + ".dll");
                sb.AppendLine("    <Reference Include=\"" + name + "\">");
                sb.AppendLine("      <HintPath>" + hint + "</HintPath>");
                sb.AppendLine("      <Private>false</Private>");
                sb.AppendLine("    </Reference>");
            }
            sb.AppendLine("  </ItemGroup>");
            sb.AppendLine("</Project>");
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
            Console.WriteLine("csproj 生成: " + path);
        }

        /// <summary>
        /// 组诊断输出——文件:行号: 错误码: 消息
        /// </summary>
        /// <param name="path">源文件路径</param>
        /// <param name="result">编译结果</param>
        private static void PrintGroupDiagnostics(string path, CompileResultV3 result)
        {
            for (int i = 0; i < result.Diagnostics.Count; i++)
            {
                MauDiagnostic d = result.Diagnostics[i];
                Console.WriteLine(path + ":" + d.Line + ": " + d.Code + ": " + d.Message);
            }
        }
    }
}