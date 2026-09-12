using System;
using System.IO;

namespace Mau.Runtime.Tests
{
    /// <summary>
    /// 热重载 fixture 构建器——动态生成（自给自足，干净克隆零残留）。
    /// valid：fixtures/valid/valid.mau 语料 → MauCompilerV3 → PocketCompiler → dll；
    /// nointerface：内嵌源码（不实现接口）→ PocketCompiler → dll。
    /// 测试类共享入口——非测试类（无 xUnit 约束）。
    /// </summary>
    internal static class FixtureBuilder
    {
        /// <summary>
        /// Ensure 构建串行化锁——并行测试类 check-then-build 非原子（判例：FL_TickThrows 引入后 FlowWatch×HotReload 并行撞击）
        /// </summary>
        private static readonly object _ensureLock = new object();        /// <summary>
                                                                          /// fixture 输出目录——fixtures/bin/
                                                                          /// </summary>
        internal static string FixtureDir
        {
            get
            {
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                return Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "fixtures", "bin"));
            }
        }

        /// <summary>
        /// 确保 fixture DLL 已生成——缺失时动态构建
        /// </summary>
        internal static void Ensure()
        {
            // 构建串行化——并行测试类同时 Ensure 时，check-then-build 非原子导致 File.Copy 覆盖双击
            lock (_ensureLock)
            {
                Directory.CreateDirectory(FixtureDir);
                string validDll = Path.Combine(FixtureDir, "FL_ValidFlow.dll");
                string noInterfaceDll = Path.Combine(FixtureDir, "FL_NoInterface.dll");
                string tickThrowsDll = Path.Combine(FixtureDir, "FL_TickThrows.dll");
                // [段1] 正常生成物——语料动态生成
                if (!File.Exists(validDll))
                {
                    string repoRoot = FindRepoRoot();
                    string mauPath = Path.Combine(repoRoot, "Mau", "Mau.Runtime.Tests", "fixtures", "valid", "valid.mau");
                    string mau = File.ReadAllText(mauPath);
                    Mau.Translator.CompileResultV3 cr = Mau.Translator.MauCompilerV3.Compile(mau, "ValidFlow");
                    if (!cr.Success)
                    {
                        throw new InvalidOperationException("fixture 语料编译失败: " + mauPath + " — " + cr.Diagnostics[0].Code + ":" + cr.Diagnostics[0].Message);
                    }
                    Build(cr.GeneratedCode, "FL_ValidFlow", validDll);
                }
                // [段2] 无接口生成物——内嵌源码（不实现 IObservableFlow）
                if (!File.Exists(noInterfaceDll))
                {
                    string source =
                        "namespace Mau.TestFixtures\n" +
                        "{\n" +
                        "    public sealed class FL_NoInterface\n" +
                        "    {\n" +
                        "        public void Tick(int frame)\n" +
                        "        {\n" +
                        "        }\n" +
                        "    }\n" +
                        "}\n";
                    Build(source, "FL_NoInterface", noInterfaceDll);
                }
                // [段3] Tick 抛异常生成物——内嵌源码（实现 IObservableFlow，Tick 必抛——热重载验证失败路径 ALC 泄漏断言）
                if (!File.Exists(tickThrowsDll))
                {
                    string source =
                        "using Mau.Runtime;\n" +
                        "namespace Mau.TestFixtures\n" +
                        "{\n" +
                        "    public sealed class FL_TickThrows : IObservableFlow\n" +
                        "    {\n" +
                        "        public void Tick(int frame)\n" +
                        "        {\n" +
                        "            throw new System.InvalidOperationException(\"fixture tick throws by design\");\n" +
                        "        }\n" +
                        "        public FlowStatusV3 GetStatus()\n" +
                        "        {\n" +
                        "            return new FlowStatusV3();\n" +
                        "        }\n" +
                        "        public string[] GetSelfDesc()\n" +
                        "        {\n" +
                        "            return new string[0];\n" +
                        "        }\n" +
                        "        public string GetMetaJson()\n" +
                        "        {\n" +
                        "            return \"{\\\"group\\\":\\\"TickThrows\\\",\\\"claims\\\":[]}\";\n" +
                        "        }\n" +
                        "        public string GetToolsJson()\n" +
                        "        {\n" +
                        "            return \"{\\\"group\\\":\\\"TickThrows\\\",\\\"tools\\\":[]}\";\n" +
                        "        }\n" +
                        "    }\n" +
                        "}\n";
                    Build(source, "FL_TickThrows", tickThrowsDll);
                }
                // [段4] LLM 盒桥 Key 段隔离生成物——P9.2 双实例集成测试（镜像 quick_cat 流式线——无 OA）
                string llmSegDll = Path.Combine(FixtureDir, "FL_LlmSeg.dll");
                if (!File.Exists(llmSegDll))
                {
                    string repoRoot = FindRepoRoot();
                    string mauPath = Path.Combine(repoRoot, "Mau", "Mau.Runtime.Tests", "fixtures", "llmseg", "llmseg.mau");
                    string mau = File.ReadAllText(mauPath);
                    Mau.Translator.CompileResultV3 cr = Mau.Translator.MauCompilerV3.Compile(mau, "LlmSeg");
                    if (!cr.Success)
                    {
                        throw new InvalidOperationException("fixture 语料编译失败: " + mauPath + " — " + cr.Diagnostics[0].Code + ":" + cr.Diagnostics[0].Message);
                    }
                    Build(cr.GeneratedCode, "FL_LlmSeg", llmSegDll);
                }
            }
        }        /// <summary>
                 /// fixture 构建——PocketCompiler Emit + 拷贝到目标路径
                 /// </summary>
                 /// <param name="source">C# 源码</param>
                 /// <param name="className">类名（= assembly 名）</param>
                 /// <param name="targetDll">目标 dll 路径</param>
        private static void Build(string source, string className, string targetDll)
        {
            string pocketRoot = Path.Combine(Path.GetTempPath(), "mau_fixture_" + Guid.NewGuid().ToString("N").Substring(0, 8));
            try
            {
                Mau.Development.MauPocketCompiler compiler = new Mau.Development.MauPocketCompiler(pocketRoot);
                Mau.Development.MauPocketCompileResult pr = compiler.Compile(source, className);
                if (!pr.Success)
                {
                    throw new InvalidOperationException("fixture Emit 失败: " + string.Join("\n", pr.Diagnostics));
                }
                File.Copy(pr.AssemblyPath, targetDll, true);
            }
            finally
            {
                if (Directory.Exists(pocketRoot))
                {
                    try
                    {
                        Directory.Delete(pocketRoot, true);
                    }
                    catch (Exception)
                    {
                        // 清理失败不影响
                    }
                }
            }
        }

        /// <summary>
        /// 仓库根探测——Mau.sln 锚点向上找
        /// </summary>
        /// <returns>仓库根</returns>
        private static string FindRepoRoot()
        {
            DirectoryInfo? dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Mau.sln")))
            {
                dir = dir.Parent;
            }
            if (dir == null)
            {
                throw new DirectoryNotFoundException("未找到 Mau.sln——fixture 语料定位失败");
            }
            return dir.FullName;
        }
    }
}
