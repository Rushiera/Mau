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
            "§ 'T_Start' : 'P_Go' & 'S_Talk' = 'Idle' → 'probe.sink'[\"hi\", 0] | 'S_Talk' = 'Thinking' | 'S_Talk' = 'Done'";

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
            Assert.Contains("public bool IsIdle_STalk()", result.GeneratedCode);
            Assert.Contains("public FlowStatusV3 GetStatus()", result.GeneratedCode);
            Assert.Contains("trace.fire", result.GeneratedCode);
            Assert.Contains("trace.state", result.GeneratedCode);
            Assert.Contains("ProbeSinkBrick.Sink(", result.GeneratedCode);
        }
/// <summary>
/// 行为断言——强类型直调真积木恒 true → 成功侧 Thinking；GetStatus 四柱快照（失败侧覆盖归 par 时限测试）
/// </summary>
///
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
        // 强类型直调——真积木 probe.sink 恒 true → 成功侧 Thinking（失败侧覆盖归 par 时限测试）
        using (FlowHandle handle = FlowHandle.Load(pr.AssemblyPath))
        {
            IObservableFlow flow = handle.Flow;
            DataBox.Signal("P_Go");
            flow.Tick(1);
            FlowStatusV3 status = flow.GetStatus();
            Assert.Contains("S_Talk=Thinking", status.StateLines);
            Assert.Equal(1L, status.Frame);
            Assert.Single(status.WireStatuses);
            Assert.Equal(1L, status.WireStatuses[0].LastTriggerFrame);
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
        /// 主动传感器行为——协程壳帧门控 + 捕获落盒 + 盒子判真条件 + 快照状态转移
        /// </summary>
        [Fact]
        public void Generate_Behavior_ActiveSensor()
{
    string sample =
        "§ 'S_Poll' = { 'Waiting', 'Got' }\n" +
        "§ 'P_Q' ↻ [2]: 'probe.sink'[\"x\", 0] > @q\n" +
        "§ 'T_Hit' : @q & 'S_Poll' = 'Waiting' → | 'S_Poll' = 'Got' | 'S_Poll' = 'Got'";
    CompileResultV3 result = MauCompilerV3.Compile(sample, "Poll");
    Assert.True(result.Success);
    string pocketRoot = Path.Combine(Path.GetTempPath(), "mau_v3_poll_" + Guid.NewGuid().ToString("N").Substring(0, 8));
    try
    {
        MauPocketCompiler compiler = new MauPocketCompiler(pocketRoot);
        MauPocketCompileResult pr = compiler.Compile(result.GeneratedCode, "FL_Poll");
        Assert.True(pr.Success, "Emit 失败: " + string.Join("\n", pr.Diagnostics));
        Assembly asm = Assembly.LoadFrom(pr.AssemblyPath);
        using (FlowHandle handle = FlowHandle.Load(pr.AssemblyPath))
        {
            IObservableFlow flow = handle.Flow;
            ISensorLoop shell = (ISensorLoop)flow;
            // 协程壳驱动 + Flow 驱动分离——probe.sink 恒 true → 门控 2 帧采样命中（f=2 起 Got）
            for (int f = 0; f < 10; f++)
            {
                shell.TickSensors(f);
                flow.Tick(f);
                FlowStatusV3 status = flow.GetStatus();
                if (Array.IndexOf(status.StateLines, "S_Poll=Got") >= 0)
                {
                    Assert.True(f >= 2, "帧 " + f + " 时采样命中——门控 2 帧，最早第 2 帧");
                    // 探测落盒验证——bool 返回值落 DataBox（测试直驱 FlowContext 未注入——scope 为 "0"）
                    bool q;
                    Assert.True(DataBox.TryGet<bool>("0", "q", out q), "探测捕获未落盒");
                    Assert.True(q, "盒子 q 应为探测返回值 true（probe.sink 恒 true）");
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
                    // 2 事件：trace.fire → trace.state（P8 观测语义——signal.* 高频完全出局；trace.* 仅内存可见不落盘）
                    Assert.Equal(2, snap.Length);
                    Assert.Equal("trace.fire", snap[0].Category);
                    Assert.Equal("trace.state", snap[1].Category);
                    Assert.Equal("8", snap[0].Props[1].Value);
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
                "§ 'T_Run' [par] : 'P_Start' & 'S_Job' = 'Idle' → 'probe.sink_slow'[\"job\", 200] | 'S_Job' = 'Running' | 'S_Job' = 'Done'";
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
                "§ 'T_Run' [par, t=2] : 'P_Start' & 'S_Job' = 'Idle' → 'probe.sink_slow'[\"job\", 500] | 'S_Job' = 'Running' | 'S_Job' = 'Failed'";
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
                "§ 'T_M' : 'P_Go' & 'S_A' = 'X' → 'probe.sink'[\"x\", 0] | 'S_A' = 'X' | 'S_A' = 'Y' | 'S_A' = 'Z'";
            CompileResultV3 result = MauCompilerV3.Compile(sample, "Multi");
            Assert.False(result.Success);
            Assert.Equal("E205", result.Diagnostics[0].Code);
        }

        /// <summary>
        /// Command 传感器生成——CmdPump 拉邮件 + Signal 置沿 + payload 落全局盒 + 全局盒取数
        /// </summary>
        [Fact]
        public void Generate_CmdSensor_PumpAndGlobalBox()
        {
            string sample =
                "§ 'S_A' = { 'X', 'Y' }\n" +
                "§ 'P_Cmd' ⇚ \"CmdKey\"\n" +
                "§ 'T_Go' : 'P_Cmd' & 'S_A' = 'X' → 'probe.sink'[CmdKey, 0] | 'S_A' = 'Y' | 'S_A' = 'X'";
            CompileResultV3 result = MauCompilerV3.Compile(sample, "CmdFlow");
            Assert.True(result.Success);
            Assert.Contains("CmdPump()", result.GeneratedCode);
            Assert.Contains("bus.Register(FlowContext.CurrentFlowId, new string[] { \"CmdKey\" })", result.GeneratedCode);
            Assert.Contains("GetCommandEmail(FlowContext.CurrentFlowId)", result.GeneratedCode);
            Assert.Contains("DataBox.Signal(\"P_Cmd\")", result.GeneratedCode);
            Assert.Contains("DataBox.Set<string>(\"global\", key, email.CmdTexts[i])", result.GeneratedCode);
            Assert.Contains("DataBox.TryGet<string>(\"global\", \"CmdKey\"", result.GeneratedCode);
        }
    }
}
