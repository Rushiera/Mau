using System;
using System.IO;
using System.Reflection;
using Mau.Bricks;
using Mau.Development;
using Mau.Runtime;
using Mau.Translator;
using Xunit;

namespace Mau.E2E
{
    /// <summary>
    /// 端到端测试——.mau 源 → 翻译 → Roslyn Emit → ALC 加载 → Fire/Tick → 行为断言（L6 层）
    /// </summary>
    public sealed class EndToEndFlowTests
    {
        /// <summary>
        /// 成功路径——file_convert 全链路：翻译 → 口袋编译 → 加载 → 触发 → 完成
        /// </summary>
        [Fact]
        public void FileConvertFlowCompletesEndToEnd()
        {
            string pocketRoot = Path.Combine(Path.GetTempPath(), "mau_e2e_pocket_" + Guid.NewGuid().ToString("N").Substring(0, 8));
            string inputFile = Path.Combine(Path.GetTempPath(), "mau_e2e_input_" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".txt");
            string outputFile = Path.Combine(Path.GetTempPath(), "mau_e2e_output_" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".txt");
            FlowHandle? handle = null;
            try
            {
                // [段0] 注册标准积木——翻译静态验证需要积木契约存在
                EnsureBricksRegistered();

                // [段1] 翻译——内嵌最小 .mau 源
                CompileResult compile = MauCompiler.Compile(FileConvertSource, "FileConvert");
                Assert.True(compile.Success);
                Assert.NotEmpty(compile.GeneratedCode);

                // [段2] Roslyn Emit——口袋编译为 DLL
                MauPocketCompiler compiler = new MauPocketCompiler(pocketRoot);
                MauPocketCompileResult pocket = compiler.Compile(compile.GeneratedCode, "FL_FileConvert");
                Assert.True(pocket.Success);
                Assert.True(File.Exists(pocket.AssemblyPath));

                // [段3] ALC 加载 + 触发
                handle = FlowHandle.Load(pocket.AssemblyPath);
                File.WriteAllText(inputFile, "mau-e2e-check");
                Type flowType = handle.Flow.GetType();
                MethodInfo? fire = flowType.GetMethod("FireInput");
                Assert.NotNull(fire);
                fire.Invoke(handle.Flow, new object[] { inputFile, outputFile });

                // [段4] Tick 驱动——直到终态
                bool done = false;
                bool failed = false;
                for (int i = 0; i < 400; i = i + 1)
                {
                    handle.Flow.Tick();
                    RuntimeStatus status = handle.Flow.GetStatus();
                    for (int j = 0; j < status.Propositions.Length; j = j + 1)
                    {
                        if (status.Propositions[j].Name == "P_Done")
                        {
                            done = status.Propositions[j].Value;
                        }
                        if (status.Propositions[j].Name == "P_Failed")
                        {
                            failed = status.Propositions[j].Value;
                        }
                    }
                    if (done || failed)
                    {
                        break;
                    }
                }

                // [段5] 断言——完成且输出落盘
                Assert.True(done);
                Assert.False(failed);
                Assert.True(File.Exists(outputFile));
                Assert.Equal("mau-e2e-check", File.ReadAllText(outputFile));
            }
            finally
            {
                if (handle != null)
                {
                    handle.TryUnload(3);
                }
                Cleanup(inputFile);
                Cleanup(outputFile);
                CleanupDir(pocketRoot);
            }
        }

        /// <summary>
        /// 失败路径——输入文件缺失：翻译 → 编译 → 加载 → 触发 → 失败置位
        /// </summary>
        [Fact]
        public void FileConvertFlowMissingInputFails()
        {
            string pocketRoot = Path.Combine(Path.GetTempPath(), "mau_e2e_pocket_" + Guid.NewGuid().ToString("N").Substring(0, 8));
            string inputFile = Path.Combine(Path.GetTempPath(), "mau_e2e_missing_" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".txt");
            string outputFile = Path.Combine(Path.GetTempPath(), "mau_e2e_out_missing_" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".txt");
            FlowHandle? handle = null;
            try
            {
                // [段0] 注册标准积木——翻译静态验证需要积木契约存在
                EnsureBricksRegistered();

                // [段1] 翻译 + 口袋编译
                CompileResult compile = MauCompiler.Compile(FileConvertSource, "FileConvert");
                Assert.True(compile.Success);
                MauPocketCompiler compiler = new MauPocketCompiler(pocketRoot);
                MauPocketCompileResult pocket = compiler.Compile(compile.GeneratedCode, "FL_FileConvert");
                Assert.True(pocket.Success);

                // [段2] 加载 + 触发不存在的输入
                handle = FlowHandle.Load(pocket.AssemblyPath);
                Type flowType = handle.Flow.GetType();
                MethodInfo? fire = flowType.GetMethod("FireInput");
                Assert.NotNull(fire);
                fire.Invoke(handle.Flow, new object[] { inputFile, outputFile });

                // [段3] Tick 驱动——失败路径应置位 P_Failed
                bool done = false;
                bool failed = false;
                for (int i = 0; i < 400; i = i + 1)
                {
                    handle.Flow.Tick();
                    RuntimeStatus status = handle.Flow.GetStatus();
                    for (int j = 0; j < status.Propositions.Length; j = j + 1)
                    {
                        if (status.Propositions[j].Name == "P_Done")
                        {
                            done = status.Propositions[j].Value;
                        }
                        if (status.Propositions[j].Name == "P_Failed")
                        {
                            failed = status.Propositions[j].Value;
                        }
                    }
                    if (done || failed)
                    {
                        break;
                    }
                }

                // [段4] 断言——失败置位且无输出
                Assert.False(done);
                Assert.True(failed);
                Assert.False(File.Exists(outputFile));
            }
            finally
            {
                if (handle != null)
                {
                    handle.TryUnload(3);
                }
                Cleanup(inputFile);
                Cleanup(outputFile);
                CleanupDir(pocketRoot);
            }
        }

        /// <summary>
        /// 确保积木注册只执行一次——BrickRegistry 同名重复注册抛异常
        /// </summary>
        private static bool _registered;

        /// <summary>
        /// 幂等注册标准积木
        /// </summary>
        private static void EnsureBricksRegistered()
        {
            if (_registered)
            {
                return;
            }
            StandardBrickRegistration.RegisterAll();
            _registered = true;
        }

        /// <summary>
        /// 内嵌最小 .mau 源——file_convert（自包含，不依赖 workspace 布局）
        /// </summary>
        private const string FileConvertSource =
            "Mau 0.1\n" +
            "基座: Mau.Runtime/v0.1\n" +
            "\n" +
            "命题:\n" +
            "  P_Input   信号\n" +
            "  P_Done    事实\n" +
            "  P_Failed  事实\n" +
            "\n" +
            "变迁 T_Convert:\n" +
            "  前置: P_Input\n" +
            "  动作: file.convert\n" +
            "  参数: input, output\n" +
            "  时限: 300帧\n" +
            "  后置: P_Done / P_Failed\n";

        /// <summary>
        /// 删除文件——不存在时静默
        /// </summary>
        /// <param name="path">文件路径</param>
        private static void Cleanup(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
                // 清理失败不影响测试结果
            }
        }

        /// <summary>
        /// 删除目录——不存在时静默
        /// </summary>
        /// <param name="path">目录路径</param>
        private static void CleanupDir(string path)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, true);
                }
            }
            catch
            {
                // 清理失败不影响测试结果
            }
        }
    }
}
