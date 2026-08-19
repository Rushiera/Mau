using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using Mau.Runtime;
using Mau.Providers;

namespace CH4
{
    /// <summary>
    /// Program 运行模式分部——自检/单指令/交互/脚本/指令路由/驱动与空闲判定。
    /// P7b partial 拆分——自 Program.cs 原样搬移，逻辑零改动。
    /// </summary>
    public static partial class Program
    {
        /// <summary>
        /// 启动自检——加载后跑 10 帧并输出三 Cat 状态（非交互验证通道）
        /// </summary>
        /// <returns>退出码</returns>
        private static int RunSelfCheck()
        {
            for (int i = 0; i < WarmupFrames; i++)
            {
                _runner.Tick();
                Thread.Sleep(FrameSleepMs);
            }
            PrintStatus();
            Console.WriteLine("[CH4.Entry] 自检通过");
            return 0;
        }

        /// <summary>
        /// 单指令脚本模式——投递一条 Command → 驱动至闭环 → 输出 → 退出（P2e 验证通道）
        /// </summary>
        /// <param name="command">指令行</param>
        /// <returns>退出码</returns>
        private static int RunSingle(string command)
        {
            bool ok = DispatchCommand(command);
            if (!ok)
            {
                return 2;
            }
            DriveUntilIdle();
            PrintStatus();
            return 0;
        }

        /// <summary>
        /// 交互模式——读行 → 指令路由 → 驱动 → 观测
        /// </summary>
        /// <returns>退出码</returns>
        private static int RunInteractive()
        {
            Console.WriteLine("指令: ReadText <path> | QuickCat <system>|<content> | Chat <内容> | status | reload <tool|io|quick|major> [dll] | run <n> | pid | quit");
            while (true)
            {
                // [段1] 帧驱动——HTTP 快照推送主线程泵（ThreadGuard：快照构建须宿主主线程；空闲时也持续 Tick）
                _runner.Tick();
                if (_httpHost != null)
                {
                    _httpHost.PumpMainThread();
                }
                PumpChatQueue();
                // [段2] 按键轮询——有输入才 ReadLine（阻塞读会卡住帧驱动）
                if (Console.KeyAvailable)
                {
                    string line = Console.ReadLine();
                    if (line == null)
                    {
                        break;
                    }
                    line = line.Trim();
                    if (line.Length == 0)
                    {
                        Thread.Sleep(FrameSleepMs);
                        continue;
                    }
                    if (!ExecuteLine(line))
                    {
                        break;
                    }
                }
                Thread.Sleep(FrameSleepMs);
            }
            return 0;
        }/// <summary>
        /// Command 解析与投递——宿主做字符串值识别（语料面零值比较）；QuickCat 双参同帧投两个 key
        /// </summary>
        /// <param name="line">输入行</param>
        /// <returns>true=识别成功并已投递</returns>
        private static bool DispatchCommand(string line)
        {
            if (line.StartsWith("ReadText ", StringComparison.Ordinal))
            {
                string path = line.Substring(9).Trim();
                if (path.Length == 0)
                {
                    return false;
                }
                _bus.SetText("TOOL_Text_Read", path, "cli");
                return true;
            }
            if (line.StartsWith("QuickCat ", StringComparison.Ordinal))
            {
                string payload = line.Substring(9).Trim();
                int sep = payload.IndexOf('|');
                if (sep < 0)
                {
                    return false;
                }
                string system = payload.Substring(0, sep).Trim();
                string content = payload.Substring(sep + 1).Trim();
                if (content.Length == 0)
                {
                    return false;
                }
                _bus.SetText("TOOL_Quick_System", system, "cli");
                _bus.SetText("TOOL_Quick_Content", content, "cli");
                return true;
            }
            if (line.StartsWith("Chat ", StringComparison.Ordinal))
            {
                string chatContent = line.Substring(5).Trim();
                if (chatContent.Length == 0)
                {
                    return false;
                }
                if (Environment.CurrentManagedThreadId == _mainThreadId)
                {
                    // 主线程（控制台）——直接执行（ThreadGuard 合规）
                    HandleChat(chatContent);
                }
                else
                {
                    // HTTP 线程——入队主线程泵（FlowRunner.Tick 仅宿主主线程——跨线程违规判例 2026-08-18）
                    _chatQueue.Enqueue(chatContent);
                }
                return true;
            }
            if (line == "session clear")
            {
                _chatContext.Clear();
                _sessionStore.Save(_chatContext.GetMessages());
                Console.WriteLine("[CH4.Entry] 会话已清空（保留系统提示词）");
                return true;
            }
            if (line == "session count")
            {
                Console.WriteLine("[CH4.Entry] 会话消息数: " + _chatContext.GetMessageCount());
                return true;
            }
            return false;
        }

        /// <summary>
        /// HTTP Chat 入队队列——Kestrel 线程投递 / 主线程泵消费（ThreadGuard：FlowRunner.Tick 仅主线程）
        /// </summary>
        private static System.Collections.Concurrent.ConcurrentQueue<string> _chatQueue = new System.Collections.Concurrent.ConcurrentQueue<string>();

        /// <summary>
        /// 泵 HTTP Chat 队列——主线程消费入队任务（RunInteractive 主循环 + DriveUntilIdle 每轮调用）
        /// </summary>
        private static void PumpChatQueue()
        {
            string job;
            while (_chatQueue.TryDequeue(out job))
            {
                HandleChat(job);
            }
        }

        /// <summary>
        /// 驱动直到三 Cat 全部 Idle——指令投递后连续 Tick；帧上限兜底（LLM 60s 现实耗时 + OA 超时结算窗口）
        /// </summary>
        private static void DriveUntilIdle()
{
            for (int i = 0; i < MaxFramesPerRun; i++)
            {
                _runner.Tick();
                if (_httpHost != null)
                {
                    _httpHost.PumpMainThread();
                }
                PumpChatQueue();
                if (AllIdle())
                {
                    return;
                }
                Thread.Sleep(FrameSleepMs);
            }
            Console.WriteLine("[CH4.Entry] 驱动帧上限 " + MaxFramesPerRun + " 到达——仍有未闭环活动");
        }
        /// <summary>
        /// 三 Cat 全部 Idle 判定——状态行全部 =Idle（编排者双状态机都 Idle 才算空闲）
        /// </summary>
        /// <returns>true=全部空闲</returns>
        private static bool AllIdle()
        {
            if (!IsFlowIdle(_toolHandle) || !IsFlowIdle(_ioHandle) || !IsFlowIdle(_quickHandle))
            {
                return false;
            }
            // OA 无未完成工单——三 Cat Idle 但工单 Open = 接单窗口期（QuickCat 探测壳 1 帧延迟），不得判空闲提前退出
            OAView view = _oa.GetSnapshot();
            return view.OpenCount == 0 && view.WorkCount == 0;
        }

        /// <summary>
        /// 单 Flow 空闲判定——GetStatus 状态行无 "=Idle" 以外的值
        /// </summary>
        /// <param name="handle">Flow 句柄</param>
        /// <returns>true=该 Flow 全部状态机 Idle</returns>
        private static bool IsFlowIdle(FlowHandle handle)
        {
            if (handle.IsFaulted)
            {
                return true;
            }
            FlowStatusV3 status = handle.Flow.GetStatus();
            for (int i = 0; i < status.StateLines.Length; i++)
            {
                string line = status.StateLines[i];
                if (!line.EndsWith("=Idle", StringComparison.Ordinal))
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// 单行指令执行——交互/脚本共用（true=继续，false=结束）
        /// </summary>
        /// <param name="line">已 Trim 的指令行</param>
        /// <returns>false=结束会话</returns>
        private static bool ExecuteLine(string line)
        {
            if (line == "quit")
            {
                return false;
            }
            if (line == "status")
            {
                PrintStatus();
                return true;
            }

            if (line == "statusq")
            {
                PrintStatusShort();
                return true;
            }

            if (line == "pid")
            {
                Console.WriteLine("[CH4.Entry] pid=" + Environment.ProcessId);
                return true;
            }

            if (line.StartsWith("run ", StringComparison.Ordinal))
            {
                long frames;
                if (!long.TryParse(line.Substring(4).Trim(), out frames) || frames < 0)
                {
                    Console.WriteLine("[CH4.Entry] run 参数无效——需非负整数帧数");
                    return true;
                }

                for (long i = 0; i < frames; i++)
                {
                    _runner.Tick();
                    Thread.Sleep(FrameSleepMs);
                }

                return true;
            }
            if (line.StartsWith("reload ", StringComparison.Ordinal))
            {
                ExecuteReload(line.Substring(7).Trim());
                return true;
            }

            if (line.StartsWith("send ", StringComparison.Ordinal))
            {
                // 只投递不驱动——配合 run <n> 手动帧驱动（超时/挂单场景精确帧数控制）
                if (!DispatchCommand(line.Substring(5).Trim()))
                {
                    Console.WriteLine("格式: send ReadText <path> 或 send QuickCat <system>|<content>");
                }
                return true;
            }

            if (!DispatchCommand(line))
            {
                Console.WriteLine("格式: ReadText <path> 或 QuickCat <system>|<content>");
                return true;
            }
            DriveUntilIdle();
            PrintStatusShort();
            return true;
        }

        /// <summary>
        /// 脚本模式——指令文件逐行执行（P3a 热重载实测通道：全程同进程，pid 不变可证）
        /// </summary>
        /// <param name="path">指令文件路径（# 开头行为注释）</param>
        /// <returns>退出码</returns>
        private static int RunScript(string path)
        {
            if (!File.Exists(path))
            {
                Console.WriteLine("[CH4.Entry] 脚本文件不存在: " + path);
                return 2;
            }
            string[] lines = File.ReadAllLines(path);
            Console.WriteLine("[CH4.Entry] 脚本模式 | " + lines.Length + " 行 | pid=" + Environment.ProcessId);
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal))
                {
                    continue;
                }
                Console.WriteLine("── 脚本指令 " + (i + 1) + ": " + line + " ──");
                if (!ExecuteLine(line))
                {
                    break;
                }
            }
            Console.WriteLine("[CH4.Entry] 脚本结束 | pid=" + Environment.ProcessId);
            return 0;
        }
    }
}