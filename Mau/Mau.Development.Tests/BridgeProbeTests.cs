using System;
using System.Collections.Generic;
using System.IO;
using Mau.Development;
using Mau.Runtime;
using Microsoft.CodeAnalysis;
using Xunit;

namespace Mau.Development.Tests
{
    /// <summary>
    /// 桥内部构造面回归——源收集（CollectSources）+ 引用集（BuildReferences）。
    /// 源收集：obj 下同名 AssemblyInfo 只收一套（多 Configuration / publish 产物并存 → CS0579 特性重复）。
    /// 引用集：共享框架探测按目标 TFM 判定——非 -windows 项目不得注入 WindowsDesktop.App
    /// （Accessibility.dll 与 Roslyn 的 Accessibility 枚举抢名 → CS0118/CS0234，A46）。
    /// 注：宿主进程 TPA 段（A46 根因②）需 -windows 同构进程方可验证，本工程（net10.0）不覆盖该段。
    /// </summary>
    public sealed class BridgeProbeTests
    {
        /// <summary>
        /// 仓库根（受控根）
        /// </summary>
        private readonly string _root;

        /// <summary>
        /// 编码桥实例（受控根 = 仓库根）
        /// </summary>
        private readonly MauRoslynBridge _bridge;

        /// <summary>
        /// 创建夹具——定位仓库根 + 建桥
        /// </summary>
        public BridgeProbeTests()
        {
            _root = RepoRoot();
            WorkspaceConfig.RootEntry entry = new WorkspaceConfig.RootEntry();
            entry.Id = "mau";
            entry.Path = _root;
            entry.Writable = true;
            _bridge = new MauRoslynBridge(new WorkspaceConfig.RootEntry[] { entry });
        }

        /// <summary>
        /// 仓库根探测——测试输出目录向上找含 Bricks 的层级
        /// </summary>
        /// <returns>含 Bricks 目录的层级路径</returns>
        private static string RepoRoot()
        {
            string dir = AppContext.BaseDirectory;
            while (dir.Length > 3 && !Directory.Exists(Path.Combine(dir, "Bricks")))
            {
                dir = Path.GetDirectoryName(dir) ?? "";
            }
            return dir;
        }

        /// <summary>
        /// 源收集去重——obj/Debug 与 obj/Release 同名 AssemblyInfo 并存时只收一套
        /// </summary>
        [Fact]
        public void CollectSourcesDeduplicatesAssemblyInfo()
        {
            string dir = Path.Combine(_root, "CatTemp", "DedupProbe");
            try
            {
                Directory.CreateDirectory(Path.Combine(dir, "obj", "Debug", "net10.0"));
                Directory.CreateDirectory(Path.Combine(dir, "obj", "Release", "net10.0", "win-x64"));
                File.WriteAllText(Path.Combine(dir, "Main.cs"), "/// <summary>\n/// 探针类\n/// </summary>\npublic class Main\n{\n}\n");
                File.WriteAllText(Path.Combine(dir, "obj", "Debug", "net10.0", "DedupProbe.AssemblyInfo.cs"), "// debug\n");
                File.WriteAllText(Path.Combine(dir, "obj", "Release", "net10.0", "win-x64", "DedupProbe.AssemblyInfo.cs"), "// release\n");
                ProjectCache cache = new ProjectCache();
                cache.ProjectDir = dir;
                cache.AssemblyName = "DedupProbe";
                string[] files = _bridge.CollectSources(cache);
                int assemblyInfoCount = 0;
                bool mainFound = false;
                for (int i = 0; i < files.Length; i = i + 1)
                {
                    string name = Path.GetFileName(files[i]);
                    if (name.EndsWith(".AssemblyInfo.cs", StringComparison.OrdinalIgnoreCase))
                    {
                        assemblyInfoCount = assemblyInfoCount + 1;
                    }
                    if (name == "Main.cs")
                    {
                        mainFound = true;
                    }
                }
                Assert.True(mainFound, "常规源文件未被收集");
                Assert.Equal(1, assemblyInfoCount);
            }
            finally
            {
                if (Directory.Exists(dir))
                {
                    Directory.Delete(dir, true);
                }
            }
        }

        /// <summary>
        /// 引用集 TFM 判定——非 -windows 项目不得注入 WindowsDesktop.App 的 Accessibility
        /// </summary>
        [Fact]
        public void NonWindowsProjectExcludesDesktopFramework()
        {
            ProjectCache cache = new ProjectCache();
            cache.ProjectDir = Path.Combine(_root, "Mau", "Mau.Development");
            cache.AssemblyName = "Mau.Development";
            cache.Tfm = "net10.0";
            cache.SourceFiles = new string[0];
            List<MetadataReference> references = _bridge.BuildReferences(cache);
            for (int i = 0; i < references.Count; i = i + 1)
            {
                string? raw = references[i].Display;
                string display;
                if (raw == null)
                {
                    display = "";
                }
                else
                {
                    display = raw;
                }
                Assert.False(display.StartsWith("Accessibility,", StringComparison.OrdinalIgnoreCase), "非 -windows 项目误注入 Accessibility: " + display);
            }
        }

        /// <summary>
        /// 目录入口只扫顶层——子目录有项目时错误信息给出可执行指引
        /// </summary>
        [Fact]
        public void DirectoryEntryReportsSubdirHint()
        {
            string dir = Path.Combine(_root, "CatTemp", "DirHintProbe");
            string sub = Path.Combine(dir, "SubProj");
            try
            {
                Directory.CreateDirectory(sub);
                File.WriteAllText(Path.Combine(sub, "SubProj.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\">\n  <PropertyGroup>\n    <TargetFramework>net10.0</TargetFramework>\n  </PropertyGroup>\n</Project>\n");
                string result;
                _bridge.Invoke("check", "{\"path\":\"" + dir.Replace("\\", "\\\\") + "\"}", out result);
                // 判据变更（2026-10-07 入口统一）：目录顶层无 csproj / .sln 时归 ENTRY_EMPTY（原笼统 BAD_PATH）
                Assert.StartsWith("ERR|ENTRY_EMPTY|", result);
                Assert.Contains("仅扫顶层", result);
                Assert.Contains("子目录发现", result);
            }
            finally
            {
                if (Directory.Exists(dir))
                {
                    Directory.Delete(dir, true);
                }
            }
        }
    }
}