using System;
using System.IO;
using Mau.Runtime;
using Mau.Translator;

namespace Mau.Cli
{
    /// <summary>
    /// mau debug（v3 重活）——运行时可视化调试：IObservableFlow 链路的控制台观测工具。
    /// 四柱状态表渲染（状态机枚举值/传感器实测/槽余量/导线状态）+ 帧驱动天然单步 + 状态断点。
    /// </summary>
    public static class CommandDebugV3
    {
        /// <summary>
        /// 执行 debug 命令
        /// </summary>
        /// <param name="args">命令行参数：mau debug &lt;file.mau&gt; [--ticks N] [--step] [--pause-on S_X=Y]</param>
        /// <returns>退出码</returns>
        public static int Run(string[] args)
        {
            if (args.Length < 1)
            {
                Console.WriteLine("用法: mau debug <file.mau> [--ticks N] [--step] [--pause-on S_X=Y]");
                Console.WriteLine("  四柱状态表渲染——状态机枚举值/传感器实测/槽余量/导线状态");
                Console.WriteLine("  --step:     每帧暂停，回车推进（帧驱动天然单步）");
                Console.WriteLine("  --pause-on: 状态行命中时暂停（断点语义——如 S_Talk=Thinking）");
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
            string pauseOn = "";
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
                else if (args[i] == "--pause-on" && i + 1 < args.Length)
                {
                    pauseOn = args[i + 1];
                    i = i + 1;
                }
            }
            // [段1] 编译——词法/解析/验证/分析全链
            string source = File.ReadAllText(mauFile);
            string flowName = Program.FlowNameFromPath(mauFile);
            CompileResultV3 result = MauCompilerV3.Compile(source, flowName);
            if (!result.Success)
            {
                Console.WriteLine("Mau 验证失败:");
                for (int i = 0; i < result.Diagnostics.Count; i++)
                {
                    MauDiagnostic d = result.Diagnostics[i];
                    Console.WriteLine("  " + mauFile + ":" + d.Line + ": " + d.Code + ": " + d.Message);
                }
                return 1;
            }
            // [段2] Roslyn Emit——PocketCompiler（无 SDK）
            string pocketRoot = Path.Combine(Path.GetTempPath(), "mau_debug_v3_" + Guid.NewGuid().ToString("N").Substring(0, 8));
            try
            {
                Mau.Development.MauPocketCompiler compiler = new Mau.Development.MauPocketCompiler(pocketRoot);
                Mau.Development.MauPocketCompileResult pr = compiler.Compile(result.GeneratedCode, "FL_" + flowName);
                if (!pr.Success)
                {
                    Console.WriteLine("C# 编译失败:");
                    for (int i = 0; i < pr.Diagnostics.Length; i++)
                    {
                        Console.WriteLine("  " + pr.Diagnostics[i]);
                    }
                    return 2;
                }
                // [段3] 接口加载——FlowHandle（IObservableFlow 契约）
                using (FlowHandle handle = FlowHandle.Load(pr.AssemblyPath))
                {
                    IObservableFlow flow = handle.Flow;
                    Console.WriteLine("══ mau debug " + flowName + " ══（外观: v3-default）");
                    Console.WriteLine("  --ticks " + ticks + (step ? " --step" : "") + (pauseOn.Length > 0 ? " --pause-on " + pauseOn : ""));
                    Console.WriteLine();
                    // [段4] 主循环——Tick + 渲染 + 单步/断点
                    bool paused = false;
                    for (int t = 0; t < ticks; t++)
                    {
                        flow.Tick(t + 1);
                        Render(flow, t + 1);
                        if (step)
                        {
                            Console.WriteLine("  --step 帧 " + (t + 1) + "（回车推进，q 退出）");
                            if (WaitForContinue())
                            {
                                break;
                            }
                        }
                        else if (pauseOn.Length > 0 && !paused && HasStateLine(flow, pauseOn))
                        {
                            paused = true;
                            Console.WriteLine("  ★ 断点命中: " + pauseOn + "（回车继续，q 退出）");
                            if (WaitForContinue())
                            {
                                break;
                            }
                        }
                    }
                    // [段5] 最终状态
                    Console.WriteLine("── 结果 ──");
                    Render(flow, (int)ticks);
                }
                return 0;
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
        /// 四柱状态表渲染——每帧截面
        /// </summary>
        /// <param name="flow">生成物</param>
        /// <param name="frame">当前帧号</param>
        private static void Render(IObservableFlow flow, int frame)
        {
            FlowStatusV3 s = flow.GetStatus();
            Console.WriteLine("── 帧 " + frame + " ──");
            // [段1] 状态机枚举值
            for (int i = 0; i < s.StateLines.Length; i++)
            {
                Console.WriteLine("  " + s.StateLines[i]);
            }
            // [段2] 主动传感器实测值
            for (int i = 0; i < s.SensorValues.Length; i++)
            {
                Console.WriteLine("  " + s.SensorValues[i].Name + " = " + (s.SensorValues[i].Value ? "1" : "0"));
            }
            // [段3] 槽余量
            for (int i = 0; i < s.SlotLevels.Length; i++)
            {
                Console.WriteLine("  " + s.SlotLevels[i].Name + " = " + s.SlotLevels[i].Available + "/" + s.SlotLevels[i].Capacity);
            }
            // [段4] 导线状态（仅显示有活动的）
            for (int i = 0; i < s.WireStatuses.Length; i++)
            {
                WireStatusV3 w = s.WireStatuses[i];
                if (w.Busy || w.TimedOut || w.LastTriggerFrame > 0)
                {
                    string state = w.Busy ? "BUSY" : (w.TimedOut ? "TIMEDOUT" : "ok");
                    Console.WriteLine("  " + w.Name + " [" + state + "] 最近触发帧=" + w.LastTriggerFrame);
                }
            }
            Console.WriteLine();
        }

        /// <summary>
        /// 断点检查——状态行命中（如 "S_Talk=Thinking"）
        /// </summary>
        /// <param name="flow">生成物</param>
        /// <param name="stateLine">期望状态行</param>
        /// <returns>命中为真</returns>
        private static bool HasStateLine(IObservableFlow flow, string stateLine)
        {
            FlowStatusV3 s = flow.GetStatus();
            for (int i = 0; i < s.StateLines.Length; i++)
            {
                if (s.StateLines[i] == stateLine)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// 等待回车继续——q 退出
        /// </summary>
        /// <returns>true=用户退出</returns>
        private static bool WaitForContinue()
        {
            while (true)
            {
                string? line = Console.ReadLine();
                if (line == null)
                {
                    return false;
                }
                if (line.Trim() == "q")
                {
                    return true;
                }
                if (line.Length == 0)
                {
                    return false;
                }
            }
        }
    }
}
