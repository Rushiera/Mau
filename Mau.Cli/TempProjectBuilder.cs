using System;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace Mau.Cli
{
    /// <summary>
    /// 临时项目构建器——.cs 源码 → dotnet build → DLL
    /// 需要环境 .NET 8 SDK
    /// </summary>
    public static class TempProjectBuilder
    {
        /// <summary>
        /// 构建结果
        /// </summary>
        public sealed class BuildResult
        {
            /// <summary>
            /// 是否成功
            /// </summary>
            public bool Success { get; set; }

            /// <summary>
            /// 成功时：DLL 完整路径；失败时：null
            /// </summary>
            public string? DllPath { get; set; }

            /// <summary>
            /// 失败时：dotnet build 的标准输出和错误输出
            /// </summary>
            public string? BuildOutput { get; set; }
        }

        /// <summary>
        /// 从 C# 源码构建 DLL
        /// </summary>
        /// <param name="csSource">C# 源码文本</param>
        /// <param name="className">类名——用于命名 .csproj 和 .cs 文件</param>
        /// <param name="outDir">输出目录——DLL 将放在此处</param>
        /// <returns>构建结果</returns>
        public static BuildResult Build(string csSource, string className, string outDir)
        {
            if (!Directory.Exists(outDir))
            {
                Directory.CreateDirectory(outDir);
            }

            // 写 .cs 文件
            string csPath = Path.Combine(outDir, className + ".cs");
            File.WriteAllText(csPath, csSource, Encoding.UTF8);

            // 写 .csproj 文件
            string csprojPath = Path.Combine(outDir, className + ".csproj");
            string csprojContent = GenerateCsproj(className);
            File.WriteAllText(csprojPath, csprojContent, Encoding.UTF8);

            // dotnet build
            ProcessStartInfo psi = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = "build \"" + csprojPath + "\" -c Release -o \"" + outDir + "\"",
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
                    return new BuildResult
                    {
                        Success = false,
                        BuildOutput = "无法启动 dotnet build 进程。请确认已安装 .NET 8 SDK。"
                    };
                }
                stdout = process.StandardOutput.ReadToEnd();
                stderr = process.StandardError.ReadToEnd();
                process.WaitForExit(60000);

                if (process.ExitCode != 0)
                {
                    return new BuildResult
                    {
                        Success = false,
                        BuildOutput = stdout + "\n" + stderr
                    };
                }
            }
            catch (Exception ex)
            {
                return new BuildResult
                {
                    Success = false,
                    BuildOutput = "dotnet build 执行失败: " + ex.Message
                };
            }
            finally
            {
                if (process != null)
                {
                    process.Dispose();
                }
            }

            string dllPath = Path.Combine(outDir, className + ".dll");
            if (!File.Exists(dllPath))
            {
                return new BuildResult
                {
                    Success = false,
                    BuildOutput = "构建完成但未找到 DLL: " + dllPath + "\n" + stdout
                };
            }

            return new BuildResult
            {
                Success = true,
                DllPath = dllPath
            };
        }

        /// <summary>
        /// 生成临时 .csproj——引用 Mau.Runtime + 全部积木项目
        /// </summary>
        /// <param name="className">类名</param>
        /// <returns>.csproj 内容</returns>
        private static string GenerateCsproj(string className)
        {
            // 找到 Mau 项目根目录——从 Mau.Cli.dll 的位置向上导航
            string mauRoot = FindMauRoot();

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("<Project Sdk=\"Microsoft.NET.Sdk\">");
            sb.AppendLine("  <PropertyGroup>");
            sb.AppendLine("    <TargetFramework>net8.0</TargetFramework>");
            sb.AppendLine("    <OutputType>Library</OutputType>");
            sb.AppendLine("    <Nullable>enable</Nullable>");
            sb.AppendLine("    <ImplicitUsings>disable</ImplicitUsings>");
            sb.AppendLine("    <AssemblyName>" + className + "</AssemblyName>");
            sb.AppendLine("  </PropertyGroup>");
            sb.AppendLine("  <ItemGroup>");
            sb.AppendLine("    <Compile Include=\"" + className + ".cs\" />");
            sb.AppendLine("  </ItemGroup>");
            sb.AppendLine("  <ItemGroup>");
            sb.AppendLine("    <ProjectReference Include=\"" + Path.Combine(mauRoot, "Mau.Runtime", "Mau.Runtime.csproj") + "\" />");
            sb.AppendLine("    <ProjectReference Include=\"" + Path.Combine(mauRoot, "Mau.Contracts", "Mau.Contracts.csproj") + "\" />");
            sb.AppendLine("    <ProjectReference Include=\"" + Path.Combine(mauRoot, "Mau.Bricks.Standard", "Mau.Bricks.Standard.csproj") + "\" />");
            sb.AppendLine("    <ProjectReference Include=\"" + Path.Combine(mauRoot, "Mau.Bricks.Data", "Mau.Bricks.Data.csproj") + "\" />");
            sb.AppendLine("    <ProjectReference Include=\"" + Path.Combine(mauRoot, "Mau.Bricks.Text", "Mau.Bricks.Text.csproj") + "\" />");
            sb.AppendLine("    <ProjectReference Include=\"" + Path.Combine(mauRoot, "Mau.Bricks.Shell", "Mau.Bricks.Shell.csproj") + "\" />");
            sb.AppendLine("    <ProjectReference Include=\"" + Path.Combine(mauRoot, "Mau.Bricks.LLM", "Mau.Bricks.LLM.csproj") + "\" />");
            sb.AppendLine("    <ProjectReference Include=\"" + Path.Combine(mauRoot, "Mau.Bricks.Approval", "Mau.Bricks.Approval.csproj") + "\" />");
            sb.AppendLine("    <ProjectReference Include=\"" + Path.Combine(mauRoot, "Mau.Bricks.Office", "Mau.Bricks.Office.csproj") + "\" />");
            sb.AppendLine("    <ProjectReference Include=\"" + Path.Combine(mauRoot, "Mau.Bricks.Log", "Mau.Bricks.Log.csproj") + "\" />");
            sb.AppendLine("  </ItemGroup>");
            sb.AppendLine("</Project>");
            return sb.ToString();
        }

        /// <summary>
        /// 查找 Mau 项目根目录——包含 Mau.sln 的目录
        /// </summary>
        /// <returns>Mau 根目录路径</returns>
        private static string FindMauRoot()
{
            // 统一探针——FindRepoRoot（审查修复轮 2026-08-11 决策2；程序集位置向上，兜底返回程序集目录）
            string? root = CliSupport.FindRepoRoot(AppDomain.CurrentDomain.BaseDirectory, new string[] { "Mau.sln" });
            return root ?? AppDomain.CurrentDomain.BaseDirectory;
        }    }
}
