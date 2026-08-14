#nullable disable
using System;
using System.IO;
using System.Reflection;
using Mau.Development;
using Mau.Runtime;
using Mau.Translator;
using Xunit;

namespace Mau.Translator.Tests
{
    /// <summary>
    /// v3 生成器测试——生成物编译 + 观测行为断言（P3：IObservableFlow/GetStatus/DataBox 事件/trace）
    /// </summary>
    [CollectionDefinition("GeneratorSerial", DisableParallelization = true)]
    public sealed class GeneratorSerialCollection
    {
    }

    /// <summary>
    /// 对话样例——三态机 + 被动传感器 + 动作导线
    /// </summary>
    [Collection("GeneratorSerial")]
    public sealed class MauGeneratorV3Tests
    {
        /// <summary>
        /// 对话样例——三态机 + 被动传感器 + 动作导线
        /// </summary>
        private const string TalkSample =
            "§ 'S_Talk' = { 'Idle', 'Thinking', 'Done' }\n" +
            "§ 'P_Go' ⇐\n" +
            "§ 'T_Start' : 'P_Go' & 'S_Talk' = 'Idle' → 'llm.chat'[] | 'S_Talk' = 'Thinking' | 'S_Talk' = 'Done'";

        /// <summary>
        /// 生成物文本断言——IObservableFlow 接口 + DataBox 事件 + GetStatus + trace 埋点
        /// </summary>
        [Fact]
        public void Generate_Structure_Complete()
        {
            CompileResultV3 result = MauCompilerV3.Compile(TalkSample, "Talk");
            Assert.True(result.Success);
            Assert.Contains("public sealed class FL_Talk : IObservableFlow", result.GeneratedCode);
            Assert.Contains("public enum STalkState", result.GeneratedCode);
            Assert.Contains("DataBox.RegisterSignal(\"P_Go\")", result.GeneratedCode);
            Assert.Contains("DataBox.TryPeek(\"P_Go\")", result.GeneratedCode);
            Assert.Contains("DataBox.TryPoll(\"P_Go\")", result.GeneratedCode);
            Assert.Contains("public bool IsIdle()", result.GeneratedCode);
            Assert.Contains("public FlowStatusV3 GetStatus()", result.GeneratedCode);
            Assert.Contains("trace.fire", result.GeneratedCode);
            Assert.Contains("trace.state", result.GeneratedCode);
            Assert.Contains("BrickRuntimeV3.TryInvoke(\"llm.chat\"", result.GeneratedCode);
        }

        /// <summary>
        /// 行为断言——DataBox.Signal 驱动：无积木注册 → 失败侧 Done；注册成功积木 → 成功侧 Thinking；GetStatus 四柱快照
        /// </summary>
        [Fact]
        public void Generate_Behavior_FailAndSuccess()
        {
            CompileResultV3 result = MauCompilerV3.Compile(TalkSample, "Talk");
            Assert.True(result.Success);
            string pocketRoot = Path.Combine(Path.GetTempPath(), "mau_v3_gen_" + Guid.NewGuid().ToString("N").Substring(0, 8));
            try
            {
                MauPocketCompiler compiler = new MauPocketCompiler(pocketRoot);
                MauPocketCompileResult pr = compiler.Compile(result.GeneratedCode, "FL_Talk");
                Assert.True(pr.Success, "Emit 失败: " + string.Join("\n", pr.Diagnostics));
                Assembly asm = Assembly.LoadFrom(pr.AssemblyPath);
                DataBox.ResetSignals();

                // 场景一——无积木注册：TryInvoke 返回 false → 失败侧 Done（接口驱动）
                using (FlowHandle handle1 = FlowHandle.Load(pr.AssemblyPath))
                {
                    IObservableFlow flow1 = handle1.Flow;
                    DataBox.Signal("P_Go");
                    flow1.Tick(1);
                    FlowStatusV3 status1 = flow1.GetStatus();
                    Assert.Contains("S_Talk=Done", status1.StateLines);
                    Assert.Equal(1L, status1.Frame);
                    Assert.Single(status1.WireStatuses);
                    Assert.Equal(1L, status1.WireStatuses[0].LastTriggerFrame);
                }

                // 场景二——注册成功积木：返回 true → 成功侧 Thinking。
                // 注册进当前 ALC 的 BrickRuntimeV3（静态字典 per-ALC——FlowHandle 每次 Load 独立 ALC）
                DataBox.ResetSignals();
                using (FlowHandle handle2 = FlowHandle.Load(pr.AssemblyPath))
                {
                    IObservableFlow flow2 = handle2.Flow;
                    Type runtime2 = flow2.GetType().Assembly.GetType("Mau.Generated.BrickRuntimeV3");
                    Assert.NotNull(runtime2);
                    runtime2.GetMethod("Register").Invoke(null, new object[] { "llm.chat", (Func<string[], bool>)(delegate (string[] args) { return true; }) });
                    DataBox.Signal("P_Go");
                    flow2.Tick(1);
                    FlowStatusV3 status2 = flow2.GetStatus();
                    Assert.Contains("S_Talk=Thinking", status2.StateLines);
                }
            }
            finally
            {
                DataBox.ResetSignals();
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
        /// 主动传感器行为——帧门控采样 + 导线条件消费 + 快照实测值
        /// </summary>
        [Fact]
        public void Generate_Behavior_ActiveSensor()
        {
            string sample =
                "§ 'S_Poll' = { 'Waiting', 'Got' }\n" +
                "§ 'P_Q' ↻ [2] 'data.box_is'[\"x\"]\n" +
                "§ 'T_Hit' : 'P_Q' & 'S_Poll' = 'Waiting' → | 'S_Poll' = 'Got' | 'S_Poll' = 'Got'";
            CompileResultV3 result = MauCompilerV3.Compile(sample, "Poll");
            Assert.True(result.Success);
            string pocketRoot = Path.Combine(Path.GetTempPath(), "mau_v3_poll_" + Guid.NewGuid().ToString("N").Substring(0, 8));
            try
            {
                MauPocketCompiler compiler = new MauPocketCompiler(pocketRoot);
                MauPocketCompileResult pr = compiler.Compile(result.GeneratedCode, "FL_Poll");
                Assert.True(pr.Success, "Emit 失败: " + string.Join("\n", pr.Diagnostics));
                Assembly asm = Assembly.LoadFrom(pr.AssemblyPath);
                long current = 0;
                using (FlowHandle handle = FlowHandle.Load(pr.AssemblyPath))
                {
                    IObservableFlow flow = handle.Flow;
                    // 注册进当前 ALC 的 BrickRuntimeV3
                    Type runtime = flow.GetType().Assembly.GetType("Mau.Generated.BrickRuntimeV3");
                    runtime.GetMethod("Register").Invoke(null, new object[] { "data.box_is", (Func<string[], bool>)(delegate (string[] args) { return current >= 5; }) });
                    for (int f = 0; f < 10; f++)
                    {
                        current = f;
                        flow.Tick(f);
                        FlowStatusV3 status = flow.GetStatus();
                        if (Array.IndexOf(status.StateLines, "S_Poll=Got") >= 0)
                        {
                            Assert.True(f >= 6, "帧 " + f + " 时采样命中——门控 2 帧 + 第 5 帧起积木 true，最早 6 帧");
                            Assert.Single(status.SensorValues);
                            Assert.True(status.SensorValues[0].Value);
                            return;
                        }
                    }
                    Assert.Fail("10 帧内未转移 Got——采样链路断裂");
                }
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
        /// trace 埋点——导线触发/状态转移进审计（帧号对齐）
        /// </summary>
        [Fact]
        public void Generate_TraceAuditEvents()
        {
            AuditStore audit = new AuditStore();
            AuditStore.Default = audit;
            try
            {
                CompileResultV3 result = MauCompilerV3.Compile(TalkSample, "TalkTrace");
                Assert.True(result.Success);
                string pocketRoot = Path.Combine(Path.GetTempPath(), "mau_v3_trace_" + Guid.NewGuid().ToString("N").Substring(0, 8));
                try
                {
                    MauPocketCompiler compiler = new MauPocketCompiler(pocketRoot);
                    MauPocketCompileResult pr = compiler.Compile(result.GeneratedCode, "FL_TalkTrace");
                    Assert.True(pr.Success);
                    Assembly asm = Assembly.LoadFrom(pr.AssemblyPath);
                    Type type = asm.GetType("Mau.Generated.FL_TalkTrace");
                    DataBox.ResetSignals();
                    object flow = Activator.CreateInstance(type);
                    type.GetMethod("Tick").Invoke(flow, new object[] { 7 });
                    DataBox.Signal("P_Go");
                    type.GetMethod("Tick").Invoke(flow, new object[] { 8 });
                    AuditEvent[] snap = audit.Snapshot();
                    // 4 事件：signal.post → signal.consume（消费先行）→ trace.fire → trace.state
                    Assert.Equal(4, snap.Length);
                    Assert.Equal("signal.post", snap[0].Category);
                    Assert.Equal("signal.consume", snap[1].Category);
                    Assert.Equal("trace.fire", snap[2].Category);
                    Assert.Equal("trace.state", snap[3].Category);
                    Assert.Equal("8", snap[2].Props[1].Value);
                }
                finally
                {
                    DataBox.ResetSignals();
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
            finally
            {
                AuditStore.Default = null;
                audit.Shutdown();
            }
        }

        /// <summary>
        /// par 后台行为——Busy 门在途可见 + Inbox 回投完成转移
        /// </summary>
        [Fact]
        public void Generate_Behavior_Parallel_BusyAndComplete()
        {
            string sample =
                "§ 'S_Job' = { 'Idle', 'Running', 'Done' }\n" +
                "§ 'P_Start' ⇐\n" +
                "§ 'T_Run' [par] : 'P_Start' & 'S_Job' = 'Idle' → 'slow.brick'[] | 'S_Job' = 'Running' | 'S_Job' = 'Done'";
            CompileResultV3 result = MauCompilerV3.Compile(sample, "ParJob");
            Assert.True(result.Success);
            string pocketRoot = Path.Combine(Path.GetTempPath(), "mau_v3_par_" + Guid.NewGuid().ToString("N").Substring(0, 8));
            try
            {
                MauPocketCompiler compiler = new MauPocketCompiler(pocketRoot);
                MauPocketCompileResult pr = compiler.Compile(result.GeneratedCode, "FL_ParJob");
                Assert.True(pr.Success, "Emit 失败: " + string.Join("\n", pr.Diagnostics));
                Assembly asm = Assembly.LoadFrom(pr.AssemblyPath);
                DataBox.ResetSignals();
                using (FlowHandle handle = FlowHandle.Load(pr.AssemblyPath))
                {
                    IObservableFlow flow = handle.Flow;
                    // 注册进当前 ALC 的 BrickRuntimeV3
                    Type runtime = flow.GetType().Assembly.GetType("Mau.Generated.BrickRuntimeV3");
                    runtime.GetMethod("Register").Invoke(null, new object[] { "slow.brick", (Func<string[], bool>)(delegate (string[] args) { System.Threading.Thread.Sleep(200); return true; }) });
                    // [段1] 触发——后台启动，主线程立即返回；转移在回投 Drain 时应用（此刻状态仍 Idle）
                    DataBox.Signal("P_Start");
                    flow.Tick(1);
                    FlowStatusV3 mid = flow.GetStatus();
                    Assert.True(mid.WireStatuses[0].Busy, "触发后后台动作应在途（Busy=true）");
                    Assert.Equal(1L, mid.WireStatuses[0].LastTriggerFrame);
                    // [段2] Busy 门——在途期间重触发被拒（条件满足但 Busy 门拦截，状态保持 Idle）
                    DataBox.Signal("P_Start");
                    flow.Tick(2);
                    Assert.False(Array.IndexOf(flow.GetStatus().StateLines, "S_Job=Running") >= 0, "Busy 门应拒绝重入——转移尚未应用");
                    Assert.False(Array.IndexOf(flow.GetStatus().StateLines, "S_Job=Done") >= 0);
                    // [段3] 回投完成——等待后台完成后主线程 Drain 应用转移（成功侧 Running）
                    System.Threading.Thread.Sleep(400);
                    flow.Tick(3);
                    FlowStatusV3 done = flow.GetStatus();
                    Assert.Contains("S_Job=Running", done.StateLines);
                    Assert.False(done.WireStatuses[0].Busy);
                }
            }
            finally
            {
                DataBox.ResetSignals();
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
        /// par 时限——[t=2] 超时丢弃迟到结果：Cube 过期 → 失败侧 + TimedOut 标志
        /// </summary>
        [Fact]
        public void Generate_Behavior_Parallel_Timeout()
        {
            string sample =
                "§ 'S_Job' = { 'Idle', 'Running', 'Failed' }\n" +
                "§ 'P_Start' ⇐\n" +
                "§ 'T_Run' [par, t=2] : 'P_Start' & 'S_Job' = 'Idle' → 'slow.brick'[] | 'S_Job' = 'Running' | 'S_Job' = 'Failed'";
            CompileResultV3 result = MauCompilerV3.Compile(sample, "ParTimeout");
            Assert.True(result.Success);
            string pocketRoot = Path.Combine(Path.GetTempPath(), "mau_v3_pto_" + Guid.NewGuid().ToString("N").Substring(0, 8));
            try
            {
                MauPocketCompiler compiler = new MauPocketCompiler(pocketRoot);
                MauPocketCompileResult pr = compiler.Compile(result.GeneratedCode, "FL_ParTimeout");
                Assert.True(pr.Success, "Emit 失败: " + string.Join("\n", pr.Diagnostics));
                Assembly asm = Assembly.LoadFrom(pr.AssemblyPath);
                DataBox.ResetSignals();
                using (FlowHandle handle = FlowHandle.Load(pr.AssemblyPath))
                {
                    IObservableFlow flow = handle.Flow;
                    // 注册进当前 ALC 的 BrickRuntimeV3
                    Type runtime = flow.GetType().Assembly.GetType("Mau.Generated.BrickRuntimeV3");
                    runtime.GetMethod("Register").Invoke(null, new object[] { "slow.brick", (Func<string[], bool>)(delegate (string[] args) { System.Threading.Thread.Sleep(500); return true; }) });
                    DataBox.Signal("P_Start");
                    flow.Tick(1);
                    // 帧 2/3——Cube 推进（t=2：触发帧 + 2 帧后 Expired）
                    flow.Tick(2);
                    flow.Tick(3);
                    // 后台仍在跑（500ms）——结果迟到后 Drain 时应超时丢弃
                    System.Threading.Thread.Sleep(700);
                    flow.Tick(4);
                    FlowStatusV3 status = flow.GetStatus();
                    Assert.Contains("S_Job=Failed", status.StateLines);
                    Assert.True(status.WireStatuses[0].TimedOut);
                    Assert.False(status.WireStatuses[0].Busy);
                }
            }
            finally
            {
                DataBox.ResetSignals();
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
        /// 多路分叉拒绝——结果 >2 → E205
        /// </summary>
        [Fact]
        public void Generate_Reject_MultiBranch()
        {
            string sample =
                "§ 'S_A' = { 'X', 'Y', 'Z' }\n" +
                "§ 'P_Go' ⇐\n" +
                "§ 'T_M' : 'P_Go' & 'S_A' = 'X' → 'cmd.match'[] | 'S_A' = 'X' | 'S_A' = 'Y' | 'S_A' = 'Z'";
            CompileResultV3 result = MauCompilerV3.Compile(sample, "Multi");
            Assert.False(result.Success);
            Assert.Equal("E205", result.Diagnostics[0].Code);
        }
    }
}
