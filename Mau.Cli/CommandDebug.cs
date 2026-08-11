// ═══════════════════════════════════════════════════
// 命令: mau debug —— 运行时可视化调试（D3）
// 定位: 控制台 TUI——实时渲染命题/变迁/资源状态表 + 数据流日志流
// 特性: 帧驱动天然单步（--step 每帧暂停）；断点 = 命题置位/变迁启动（--pause-on）
// 对标: 构筑期诊断已对齐 dotnet 格式（文件:行:码:消息 + D1 行号映射）；
//       运行时按 Mau 特性设计——帧/命题/变迁/资源/数据流，非 GDB 照搬
// ═══════════════════════════════════════════════════
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using Mau.Development;
using Mau.Runtime;
using Mau.Translator;

namespace Mau.Cli
{
    /// <summary>
    /// mau debug 命令——运行时可视化调试（控制台 TUI）
    /// </summary>
    public static class CommandDebug
    {
        /// <summary>
        /// 执行 debug 命令
        /// </summary>
        /// <param name="args">命令行参数：mau debug <file.mau> [--ticks N] [--step] [--pause-on X] [--trace]</param>
        /// <returns>退出码</returns>
        public static int Execute(string[] args)
        {
            if (args.Length < 1)
            {
                Console.WriteLine("用法: mau debug <file.mau> [--ticks N] [--step] [--pause-on T_X|P_Y] [--trace]");
                Console.WriteLine("  控制台实时渲染——命题/变迁/资源状态表 + 数据流日志（D3 调试基建）");
                Console.WriteLine("  --step:     每帧暂停，回车推进（帧驱动天然单步）");
                Console.WriteLine("  --pause-on: 命题置位/变迁启动时暂停（断点语义）");
                Console.WriteLine("  --trace:    开启数据流追踪（SetTraceDataFlow——输出赋值/信号投递消费）");
                return 1;
            }

            string mauFile = args[0];
            if (!File.Exists(mauFile))
            {
                Console.WriteLine("文件不存在: " + mauFile);
                return 1;
            }
            long ticks = 600;
            bool step = false;
            bool trace = false;
            string pauseOn = "";
            List<string[]> fireCommands = new List<string[]>();
            for (int i = 1; i < args.Length; i++)
            {
                if (args[i] == "--ticks" && i + 1 < args.Length)
                {
                    long.TryParse(args[i + 1], out ticks);
                    i = i + 1;
                }
                else if (args[i] == "--step")
                {
                    step = true;
                }
                else if (args[i] == "--trace")
                {
                    trace = true;
                }
                else if (args[i] == "--pause-on" && i + 1 < args.Length)
                {
                    pauseOn = args[i + 1];
                    i = i + 1;
                }
                else if (args[i] == "--fire" && i + 1 < args.Length)
                {
                    // --fire MethodName key=val key2=val2 —— 收集到下一个 -- 参数为止
                    List<string> parts = new List<string>();
                    i = i + 1;
                    while (i < args.Length && !args[i].StartsWith("--", StringComparison.Ordinal))
                    {
                        parts.Add(args[i]);
                        i = i + 1;
                    }
                    i = i - 1;
                    fireCommands.Add(parts.ToArray());
                }
            }

            // [1] 编译——MauCompiler 唯一入口（BrickIndex + BRIKGROUP 内嵌）
            string sourceText = File.ReadAllText(mauFile, Encoding.UTF8);
            string flowName = Program.FlowNameFromPath(mauFile);
            CompileResult compileResult = MauCompiler.Compile(sourceText, flowName);
            if (!compileResult.Success)
            {
                Console.WriteLine("Mau 验证失败:");
                for (int i = 0; i < compileResult.Diagnostics.Count; i = i + 1)
                {
                    MauDiagnostic d = compileResult.Diagnostics[i];
                    Console.WriteLine("  " + mauFile + ":" + d.Line.ToString() + ": " + d.Code + ": " + d.Message);
                }
                return 1;
            }

            // [2] Roslyn 编译（口袋编译——诊断带 D1 行号映射）
            string pocketRoot = Path.Combine(Path.GetTempPath(), "mau_debug_pocket_" + Guid.NewGuid().ToString("N").Substring(0, 8));
            MauPocketCompiler compiler = new MauPocketCompiler(pocketRoot);
            MauPocketCompileResult pocketResult = compiler.Compile(compileResult.GeneratedCode, "FL_" + flowName, compileResult.GeneratedMap);
            if (!pocketResult.Success)
            {
                Console.WriteLine("C# 编译失败:");
                for (int i = 0; i < pocketResult.Diagnostics.Length; i = i + 1)
                {
                    Console.WriteLine("  " + pocketResult.Diagnostics[i]);
                }
                try
                {
                    if (Directory.Exists(pocketRoot))
                    {
                        Directory.Delete(pocketRoot, true);
                    }
                }
                catch
                {
                }
                return 2;
            }

            // [3] 加载 + 可选数据流追踪
            FlowHandle? handle = null;
            try
            {
                handle = FlowHandle.Load(pocketResult.AssemblyPath);
            }
            catch (Exception ex)
            {
                Console.WriteLine("DLL 加载失败: " + ex.Message);
                try
                {
                    if (Directory.Exists(pocketRoot))
                    {
                        Directory.Delete(pocketRoot, true);
                    }
                }
                catch
                {
                }
                return 3;
            }
            IObservableFlow flow = handle.Flow;
            if (trace)
            {
                Type flowType = flow.GetType();
                MethodInfo? setTrace = flowType.GetMethod("SetTraceDataFlow");
                if (setTrace != null)
                {
                    setTrace.Invoke(flow, new object[] { true });
                }
            }

            // [3b] 外部投递——Fire 信号（--fire MethodName key=val ...）
            for (int f = 0; f < fireCommands.Count; f = f + 1)
            {
                string[] fc = fireCommands[f];
                if (fc.Length == 0)
                {
                    continue;
                }
                try
                {
                    InvokeFire(flow, fc);
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Fire " + fc[0] + " 失败: " + ex.Message);
                }
            }

            // [4] 主循环——Tick + 渲染 + 单步/断点
            bool paused = false;
            for (long t = 0; t < ticks; t = t + 1)
            {
                flow.Tick();
                Render(flow, mauFile, t + 1);

                if (step)
                {
                    Console.WriteLine("  --step 帧 " + (t + 1).ToString() + "（回车推进，q 退出）");
                    if (WaitForContinue())
                    {
                        break;
                    }
                }
                else if (pauseOn.Length > 0)
                {
                    if (IsPauseHit(flow, pauseOn) && !paused)
                    {
                        paused = true;
                        Console.WriteLine("  ★ 断点命中: " + pauseOn + "（回车继续，q 退出）");
                        if (WaitForContinue())
                        {
                            break;
                        }
                    }
                }
            }

            // [5] 结束——最终状态 + 退出码
            Render(flow, mauFile, ticks);
            RuntimeStatus status = flow.GetStatus();
            bool done = false;
            bool failed = false;
            for (int i = 0; i < status.Propositions.Length; i = i + 1)
            {
                if (status.Propositions[i].Name == "P_Done")
                {
                    done = status.Propositions[i].Value;
                }
                if (status.Propositions[i].Name == "P_Failed")
                {
                    failed = status.Propositions[i].Value;
                }
            }
            Console.WriteLine("── 结果 ──");
            Console.WriteLine("  " + flowName + (done ? " ✅ 完成" : (failed ? " ❌ 失败" : " ⏳ 未完成")));

            // [6] 清理
            handle.TryUnload(3);
            try
            {
                if (Directory.Exists(pocketRoot))
                {
                    Directory.Delete(pocketRoot, true);
                }
            }
            catch
            {
            }
            return failed ? 2 : 0;
        }

        /// <summary>
        /// 反射调用 FireXxx 方法——--fire MethodName key=val key2=val2
        /// </summary>
        /// <param name="flow">生成物实例</param>
        /// <param name="fc">fire 指令——[0]=方法名，其余 key=val</param>
        private static void InvokeFire(IObservableFlow flow, string[] fc)
        {
            Type type = flow.GetType();
            MethodInfo? method = type.GetMethod(fc[0]);
            if (method == null)
            {
                throw new InvalidOperationException("未找到方法: " + fc[0] + "（类: " + type.Name + "）");
            }
            ParameterInfo[] paramInfos = method.GetParameters();
            object?[] paramValues = new object?[paramInfos.Length];
            for (int i = 0; i < paramInfos.Length; i = i + 1)
            {
                string pname = paramInfos[i].Name!;
                string? val = null;
                for (int a = 1; a < fc.Length; a = a + 1)
                {
                    string pair = fc[a];
                    int eq = pair.IndexOf('=');
                    if (eq > 0 && pair.Substring(0, eq) == pname)
                    {
                        val = pair.Substring(eq + 1);
                        break;
                    }
                }
                if (val != null)
                {
                    paramValues[i] = ConvertArg(val, paramInfos[i].ParameterType);
                }
                else
                {
                    if (paramInfos[i].ParameterType.IsValueType)
                    {
                        paramValues[i] = Activator.CreateInstance(paramInfos[i].ParameterType);
                    }
                    else
                    {
                        paramValues[i] = null;
                    }
                }
            }
            method.Invoke(flow, paramValues);
        }

        /// <summary>
        /// 字符串 → 目标类型转换
        /// </summary>
        /// <param name="val">字符串值</param>
        /// <param name="targetType">目标类型</param>
        /// <returns>转换后的值</returns>
        private static object? ConvertArg(string val, Type targetType)
        {
            if (targetType == typeof(string))
            {
                return val;
            }
            if (targetType == typeof(int))
            {
                return int.Parse(val);
            }
            if (targetType == typeof(long))
            {
                return long.Parse(val);
            }
            if (targetType == typeof(bool))
            {
                return bool.Parse(val);
            }
            if (targetType == typeof(double))
            {
                return double.Parse(val);
            }
            return val;
        }

        /// <summary>
        /// 渲染状态表——清屏重绘：帧号 + 命题/变迁/资源表 + 数据流日志尾部
        /// </summary>
        /// <param name="flow">生成物</param>
        /// <param name="mauFile">源文件路径（表头显示）</param>
        /// <param name="frame">当前帧</param>
        private static void Render(IObservableFlow flow, string mauFile, long frame)
        {
            try
            {
                Console.Clear();
            }
            catch
            {
            }
            RuntimeStatus status = flow.GetStatus();
            Console.WriteLine("── Mau Debug ── 帧 " + frame.ToString() + " │ " + Path.GetFileName(mauFile));

            // 命题表
            Console.WriteLine("命题:");
            for (int i = 0; i < status.Propositions.Length; i = i + 1)
            {
                PropSnapshot p = status.Propositions[i];
                Console.WriteLine("  " + p.Name + " " + (p.Value ? "✓" : "·") + " (" + p.Kind + ")");
            }

            // 变迁表
            Console.WriteLine("变迁:");
            for (int i = 0; i < status.Transitions.Length; i = i + 1)
            {
                TransSnapshot t = status.Transitions[i];
                string state = t.CubeState;
                if (t.LimitFrames > 0)
                {
                    Console.WriteLine("  " + t.Name + " [" + state + " " + t.ElapsedFrames.ToString() + "/" + t.LimitFrames.ToString() + "]");
                }
                else
                {
                    Console.WriteLine("  " + t.Name + " [" + state + "]");
                }
            }

            // 资源表
            if (status.Resources.Length > 0)
            {
                Console.WriteLine("资源:");
                for (int i = 0; i < status.Resources.Length; i = i + 1)
                {
                    ResSnapshot r = status.Resources[i];
                    Console.WriteLine("  " + r.Name + " " + r.QuotaCurrent.ToString() + "/" + r.QuotaMax.ToString());
                }
            }

            // 数据流日志——尾部 12 条
            MauDebug[] logs = flow.GetLogs();
            if (logs.Length > 0)
            {
                Console.WriteLine("── 日志 ──");
                int start = logs.Length > 12 ? logs.Length - 12 : 0;
                for (int i = start; i < logs.Length; i = i + 1)
                {
                    MauDebug d = logs[i];
                    Console.WriteLine("  #" + d.Frame.ToString() + " " + d.TransitionName + " " + d.Phase + " " + d.Message);
                }
            }
        }

        /// <summary>
        /// 等待回车——返回 true=用户输入 q 退出
        /// </summary>
        /// <returns>true=退出</returns>
        private static bool WaitForContinue()
        {
            // 限时轮询——非交互（管道/CI）场景最多等 10 秒自动继续，防挂死
            for (int i = 0; i < 200; i++)
            {
                if (Console.KeyAvailable)
                {
                    try
                    {
                        ConsoleKeyInfo key = Console.ReadKey(true);
                        return key.KeyChar == 'q' || key.KeyChar == 'Q';
                    }
                    catch
                    {
                        return false;
                    }
                }
                System.Threading.Thread.Sleep(50);
            }
            return false;
        }

        /// <summary>
        /// 断点命中检测——命题置位（P_ 前缀）或变迁启动（T_ 前缀，Cube 非 Idle）
        /// </summary>
        /// <param name="flow">生成物</param>
        /// <param name="target">断点目标——T_X 或 P_Y</param>
        /// <returns>命中为真</returns>
        private static bool IsPauseHit(IObservableFlow flow, string target)
        {
            RuntimeStatus status = flow.GetStatus();
            if (target.StartsWith("T_", StringComparison.Ordinal))
            {
                for (int i = 0; i < status.Transitions.Length; i = i + 1)
                {
                    if (status.Transitions[i].Name == target && status.Transitions[i].CubeState != "Idle")
                    {
                        return true;
                    }
                }
            }
            else if (target.StartsWith("P_", StringComparison.Ordinal))
            {
                for (int i = 0; i < status.Propositions.Length; i = i + 1)
                {
                    if (status.Propositions[i].Name == target && status.Propositions[i].Value)
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        /// <summary>
        /// <summary>
        /// 从文件路径推导流程名——file_convert.mau → FileConvert（统一 Program.FlowNameFromPath）
        /// </summary>
    }
}
