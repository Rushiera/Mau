using System;
using System.Reflection;
using Mau.Bricks;
using Mau.Development;
using Mau.Runtime;
using Mau.Translator;
using Xunit;

namespace Mau.E2E
{
    /// <summary>
    /// 展开式重试 + 块格式命题端到端测试（语言哲学 D1）——链式推进 / 显式重置 / 轮末重置
    /// </summary>
    public sealed class RetryExpandFlowTests
    {
        /// <summary>
        /// 积木注册锁——进程内幂等
        /// </summary>
        private static readonly object Sync = new object();

        /// <summary>
        /// 积木是否已注册
        /// </summary>
        private static bool _ready;

        /// <summary>
        /// 展开式重试验证语料——两段一样逻辑的变迁（T_Attempt1/T_Attempt2）+ 块格式命题 + 显式/轮末重置
        /// </summary>
        private const string RetryExpandSource = "Mau 0.1\n" +
            "基座: Mau.Runtime/v0.1\n" +
            "\n" +
            "命题:\n" +
            "  P_Start   信号\n" +
            "  P_Pulse   信号\n" +
            "  P_Done    事实\n" +
            "  P_Failed  事实\n" +
            "\n" +
            "命题 P_Attempt1Done:\n" +
            "  类型: 事实\n" +
            "  重置: 显式\n" +
            "\n" +
            "命题 P_Attempt2Done:\n" +
            "  类型: 事实\n" +
            "\n" +
            "命题 P_TickPulse:\n" +
            "  类型: 事实\n" +
            "  重置: 轮末\n" +
            "\n" +
            "变迁 T_Pulse:\n" +
            "  前置: P_Pulse\n" +
            "  动作: log.write\n" +
            "  参数: module, level, message\n" +
            "  后置: P_TickPulse / P_Failed\n" +
            "\n" +
            "变迁 T_Attempt1:\n" +
            "  前置: P_Start\n" +
            "  动作: log.write\n" +
            "  参数: module, level, message\n" +
            "  后置: P_Attempt1Done / P_Failed\n" +
            "\n" +
            "变迁 T_Attempt2:\n" +
            "  前置: P_Attempt1Done\n" +
            "  动作: log.write\n" +
            "  参数: module, level, message\n" +
            "  后置: P_Attempt2Done / P_Failed\n" +
            "\n" +
            "变迁 T_Finish:\n" +
            "  前置: P_Attempt2Done\n" +
            "  动作: log.write\n" +
            "  参数: module, level, message\n" +
            "  后置: P_Done / P_Failed\n";

        /// <summary>
        /// 积木注册——进程内幂等
        /// </summary>
        private static void EnsureBricksRegistered()
        {
            lock (Sync)
            {
                if (_ready)
                {
                    return;
                }
                if (!Mau.Contracts.BrickRegistry.TryGet("file.convert", out _))
                {
                    StandardBrickRegistration.RegisterAll();
                }
                if (!Mau.Contracts.BrickRegistry.TryGet("log.write", out _))
                {
                    LogBrickRegistration.RegisterAll();
                }
                _ready = true;
            }
        }

        /// <summary>
        /// 构筑语料并加载——返回 FlowHandle
        /// </summary>
        /// <param name="source">语料源码</param>
        /// <param name="flowName">流程名</param>
        /// <returns>加载句柄</returns>
        private static FlowHandle BuildAndLoad(string source, string flowName)
        {
            EnsureBricksRegistered();
            string pocketRoot = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "mau_e2e_retry_" + Guid.NewGuid().ToString("N").Substring(0, 8));
            CompileResult compile = MauCompiler.Compile(source, flowName);
            Assert.True(compile.Success);
            MauPocketCompiler compiler = new MauPocketCompiler(pocketRoot);
            MauPocketCompileResult pocket = compiler.Compile(compile.GeneratedCode, "FL_" + flowName);
            Assert.True(pocket.Success);
            return FlowHandle.Load(pocket.AssemblyPath);
        }

        /// <summary>
        /// 反射调用生成物方法
        /// </summary>
        /// <param name="flow">生成物实例</param>
        /// <param name="method">方法名</param>
        /// <param name="args">参数</param>
        /// <returns>返回值</returns>
        private static object Invoke(IObservableFlow flow, string method, params object[] args)
        {
            MethodInfo? mi = flow.GetType().GetMethod(method);
            if (mi == null)
            {
                throw new InvalidOperationException("生成物缺少方法: " + method);
            }
            object? result = mi.Invoke(flow, args);
            return result!;
        }

        /// <summary>
        /// 展开式链式推进——两段一样逻辑的变迁按序完成，终态 Done
        /// </summary>
        [Fact]
        public void ExpandedAttempts_ChainToDone()
        {
            using (FlowHandle handle = BuildAndLoad(RetryExpandSource, "RetryExpand"))
            {
                Assert.False(handle.IsFaulted);
                Invoke(handle.Flow, "FireStart", "test", 0, "hello");
                for (int i = 0; i < 10; i = i + 1)
                {
                    handle.Flow.Tick();
                }
                Assert.True((bool)Invoke(handle.Flow, "IsAttempt1Done"));
                Assert.True((bool)Invoke(handle.Flow, "IsAttempt2Done"));
                Assert.True((bool)Invoke(handle.Flow, "IsDone"));
                Assert.False((bool)Invoke(handle.Flow, "IsFailed"));
            }
        }

        /// <summary>
        /// 显式重置——ResetAttempt1Done 只清目标事实，不影响其他链上命题
        /// </summary>
        [Fact]
        public void ExplicitReset_ClearsOnlyTarget()
        {
            using (FlowHandle handle = BuildAndLoad(RetryExpandSource, "RetryExpand"))
            {
                Invoke(handle.Flow, "FireStart", "test", 0, "hello");
                for (int i = 0; i < 10; i = i + 1)
                {
                    handle.Flow.Tick();
                }
                Assert.True((bool)Invoke(handle.Flow, "IsAttempt1Done"));

                Invoke(handle.Flow, "ResetAttempt1Done");
                Assert.False((bool)Invoke(handle.Flow, "IsAttempt1Done"));
                Assert.True((bool)Invoke(handle.Flow, "IsAttempt2Done"));
                Assert.True((bool)Invoke(handle.Flow, "IsDone"));
            }
        }

        /// <summary>
        /// 轮末重置——P_TickPulse 在 Tick 末尾自动清，不生成 Reset 方法
        /// </summary>
        [Fact]
        public void EndOfFrameReset_AutoClears()
        {
            using (FlowHandle handle = BuildAndLoad(RetryExpandSource, "RetryExpand"))
            {
                // 轮末命题不生成 Reset 方法——无 ResetTickPulse
                MethodInfo? resetPulse = handle.Flow.GetType().GetMethod("ResetTickPulse");
                Assert.Null(resetPulse);

                // 显式命题生成 Reset 方法
                MethodInfo? resetAttempt = handle.Flow.GetType().GetMethod("ResetAttempt1Done");
                Assert.NotNull(resetAttempt);

                // Tick 后轮末命题恒假（帧末已清）
                Invoke(handle.Flow, "FirePulse", "test", 0, "pulse");
                handle.Flow.Tick();
                Assert.False((bool)Invoke(handle.Flow, "IsTickPulse"));
            }
        }
    }
}
