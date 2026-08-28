using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using Mau.Runtime;
using CatHome4.Admin;
using CatHome4.QQ;
using CatHome4.Observe;
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
            ObserveService.PrintStatus();
            Console.WriteLine("[CatHome4] 自检通过");
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
            ObserveService.PrintStatus();
            return 0;
        }

        /// <summary>
        /// 交互模式——读行 → 指令路由 → 驱动 → 观测
        /// </summary>
        /// <returns>退出码</returns>
        private static int RunInteractive()
        {
            Console.WriteLine("指令: QuickCat <system>|<content> | Chat <内容> | status | reload <quick|dev> [dll] | run <n> | pid | quit");
            while (true)
            {
                // [段1] 帧驱动——HTTP 快照推送主线程泵（ThreadGuard：快照构建须宿主主线程；空闲时也持续 Tick）
                _runner.Tick();
                if (_httpHost != null)
                {
                    _httpHost.PumpMainThread();
                }
                _chatBridge.PumpSessions();
                PumpChatQueue();
                AdminService.PumpCatQueues();
                QQBotService.Tick();
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
                if (Environment.CurrentManagedThreadId == _mainThreadId)
                {
                    // 主线程（CLI）——直接执行（ThreadGuard 合规）
                    HandleQuickCat(system, content);
                }
                else
                {
                    // HTTP 线程——入队主线程泵（OA 等待循环必须主线程驱动）
                    _quickQueue.Enqueue(system + "\u0001" + content);
                }
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
                    // 主线程（控制台）——直接投递会话（相位推进在 Pump——ThreadGuard 合规）
                    _chatBridge.DefaultSession.PostUserMessage(chatContent);
                }
                else
                {
                    // HTTP 线程——入队主线程泵（FlowRunner.Tick 仅宿主主线程——跨线程违规判例 2026-08-18）
                    _chatQueue.Enqueue(chatContent);
                }
                return true;
            }
            // P8.5 显式新会话——清前文 + 按清单重新注入（唯一重注入通道；主线程直执 / HTTP 线程置位泵——ThreadGuard 契约；会话忙时标志保留泵重判）
            if (line == "session.new")
            {
                if (Environment.CurrentManagedThreadId == _mainThreadId)
                {
                    if (_chatBridge.DefaultSession.IsIdle)
                    {
                        _chatBridge.HandleSessionNew(_chatBridge.DefaultSession, _chatBridge.DefaultPersona, _chatBridge.DefaultInjectList, _chatBridge.DefaultToolSpecs, delegate(int n)
                        {
                            if (_httpHost != null)
                            {
                                _httpHost.PushChatDone(n);
                            }
                        });
                    }
                    else
                    {
                        _chatBridge.SessionNewRequested = true;
                        Console.WriteLine("[CatHome4] 会话忙——session.new 排队执行");
                    }
                }
                else
                {
                    _chatBridge.SessionNewRequested = true;
                }
                return true;
            }
            if (line == "note.start")
            {
                // M4c Note 启动——拼接计划+进度推给 LLM（主线程直执 / HTTP 线程入队泵）
                if (Environment.CurrentManagedThreadId == _mainThreadId)
                {
                    _chatBridge.DefaultSession.NoteStart();
                }
                else
                {
                    _sessionCmdQueue.Enqueue(line);
                }
                return true;
            }
            if (line.StartsWith("note.add ", StringComparison.Ordinal))
            {
                // M4c Note 手动新增——主线程直执 / HTTP 线程入队泵（Note 状态仅主线程触碰）
                if (Environment.CurrentManagedThreadId == _mainThreadId)
                {
                    _chatBridge.DefaultSession.NoteAdd(line.Substring(9).Trim());
                }
                else
                {
                    _sessionCmdQueue.Enqueue(line);
                }
                return true;
            }
            if (line == "session clear" || line == "session count")
            {
                // 会话调试指令——主线程直执 / HTTP 线程入队泵（ThreadGuard：_chatContext 仅主线程触碰——2026-08-20 遗留改造；会话忙时入队重判）
                if (Environment.CurrentManagedThreadId == _mainThreadId)
                {
                    if (_chatBridge.DefaultSession.IsIdle)
                    {
                        _chatBridge.HandleSessionCmd(_chatBridge.DefaultSession, line);
                    }
                    else
                    {
                        _sessionCmdQueue.Enqueue(line);
                        Console.WriteLine("[CatHome4] 会话忙——" + line + " 排队执行");
                    }
                }
                else
                {
                    _sessionCmdQueue.Enqueue(line);
                }
                return true;
            }
            // R0.2 热重载指令——CLI 通道（--run/--script）支持：reload quick|text|mau|cs|config [dll]（主线程直执——ExecuteReload 事务三段式）
            if (line.StartsWith("reload ", StringComparison.Ordinal))
            {
                if (Environment.CurrentManagedThreadId == _mainThreadId)
                {
                    Console.WriteLine(ExecuteReload(line.Substring(7).Trim()));
                }
                else
                {
                    Console.WriteLine("[CatHome4] reload 仅主线程执行——CLI 通道使用");
                }
                return true;
            }
            // P9.3 多猫管理指令族——主线程直执 / HTTP 线程入队泵（ThreadGuard：注册表仅主线程触碰）
            // M3 catcfg.apply 同族路由（每猫配置运行时生效——HTTP 端点落盘后入队）
            if (line.StartsWith("cat.", StringComparison.Ordinal) || line.StartsWith("catcfg.", StringComparison.Ordinal))
            {
                if (Environment.CurrentManagedThreadId == _mainThreadId)
                {
                    Console.WriteLine("[CatHome4] " + AdminService.HandleCatCommand(line));
                }
                else
                {
                    AdminService._catQueue.Enqueue(line);
                }
                return true;
            }
            return false;
        }

        /// <summary>
        /// HTTP Chat 入队队列——Kestrel 线程投递 / 主线程泵消费（ThreadGuard：FlowRunner.Tick 仅主线程）
        /// </summary>
        private static System.Collections.Concurrent.ConcurrentQueue<string> _chatQueue = new System.Collections.Concurrent.ConcurrentQueue<string>();

        /// <summary>
        /// QuickCat 指令队列——HTTP 线程投递 / 主线程泵消费（D8 修复：OA 发单等待回执需主线程驱动——ThreadGuard 契约）
        /// </summary>
        private static System.Collections.Concurrent.ConcurrentQueue<string> _quickQueue = new System.Collections.Concurrent.ConcurrentQueue<string>();

        /// <summary>
        /// 会话调试指令队列——session clear/count（HTTP 线程投递 / 主线程泵消费——_chatContext 仅主线程触碰）
        /// </summary>
        private static System.Collections.Concurrent.ConcurrentQueue<string> _sessionCmdQueue = new System.Collections.Concurrent.ConcurrentQueue<string>();

        /// <summary>
        /// 泵 HTTP Chat 队列——主线程消费入队任务（RunInteractive 主循环 + DriveUntilIdle 每轮调用）
        /// </summary>
        private static void PumpChatQueue()
        {
            // P8.5：会话新开请求优先处理（HTTP 线程置位——主线程泵执行，ThreadGuard 契约）；会话忙时标志保留——下帧重判（排队语义保持）
            if (_chatBridge.SessionNewRequested)
            {
                if (_chatBridge.DefaultSession.IsIdle)
                {
                    _chatBridge.SessionNewRequested = false;
                    _chatBridge.HandleSessionNew(_chatBridge.DefaultSession, _chatBridge.DefaultPersona, _chatBridge.DefaultInjectList, _chatBridge.DefaultToolSpecs, delegate(int n)
                    {
                        if (_httpHost != null)
                        {
                            _httpHost.PushChatDone(n);
                        }
                    });
                }
            }
            // D8：QuickCat 指令泵消费（HTTP 线程投递——主线程 OA 驱动）
            string quickJob;
            while (_quickQueue.TryDequeue(out quickJob))
            {
                int quickSep = quickJob.IndexOf('\u0001');
                if (quickSep >= 0)
                {
                    HandleQuickCat(quickJob.Substring(0, quickSep), quickJob.Substring(quickSep + 1));
                }
            }
            // 会话调试指令泵消费（session clear/count——HTTP 线程投递 / 主线程执行；会话忙时保留队列——下帧重判）
            string sessionCmd;
            while (_sessionCmdQueue.TryDequeue(out sessionCmd))
            {
                if (!_chatBridge.DefaultSession.IsIdle)
                {
                    _sessionCmdQueue.Enqueue(sessionCmd);
                    break;
                }
                _chatBridge.HandleSessionCmd(_chatBridge.DefaultSession, sessionCmd);
            }
            string job;
            while (_chatQueue.TryDequeue(out job))
            {
                _chatBridge.DefaultSession.PostUserMessage(job);
            }
        }



        /// <summary>
        /// QuickCat 指令执行——D8 修复（2026-08-20）：OA 发单（officeName=QuickCat）→ 双参载荷 → 驱动帧等待回执 → 输出。
        /// 旧路径 CommandBus SetText 已死（ToolTestCat 退役后未适配——REJECT 未注册）；语料 oa.is_open["TOOL","QuickCat"] 接单。
        /// </summary>
        /// <param name="system">系统提示词</param>
        /// <param name="content">用户内容</param>
        private static void HandleQuickCat(string system, string content)
        {
            const long QuickOwnerId = 100;
            const long QuickTimeoutFrames = 4800;
            const long QuickWaitFrames = 4800 + 600;
            long officeId = _oa.Post(QuickOwnerId, "TOOL", "QuickCat", QuickTimeoutFrames);
            if (officeId <= 0)
            {
                Console.WriteLine("[CatHome4] QuickCat 发单失败");
                return;
            }
            _oa.SetStr(officeId, QuickOwnerId, "system", system);
            _oa.SetStr(officeId, QuickOwnerId, "content", content);
            // 等待回执——同步问答语义：驱动帧直至 Closed/TimeOut（LLM 流式 60s+ 宽裕）
            for (long f = 0; f < QuickWaitFrames; f++)
            {
                OfficeState st = _oa.GetStatus(officeId);
                if (st == OfficeState.Closed || st == OfficeState.TimeOut)
                {
                    break;
                }
                _runner.Tick();
                if (_httpHost != null)
                {
                    _httpHost.PumpMainThread();
                }
                System.Threading.Thread.Sleep(FrameSleepMs);
            }
            Office quickOffice = _oa.GetOffice(officeId);
            string quickResult = "";
            if (quickOffice.Result.Strs != null && quickOffice.Result.Strs.ContainsKey("result"))
            {
                quickResult = quickOffice.Result.Strs["result"];
            }
            if (quickResult.Length == 0)
            {
                quickResult = "ERR|EMPTY_RESULT|QuickCat 无回执（状态 " + quickOffice.Status.ToString() + "）";
            }
            Console.WriteLine("[QuickCat] " + quickResult);
            _oa.RemoveByOwner(QuickOwnerId);
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
                _chatBridge.PumpSessions();
                PumpChatQueue();
                AdminService.PumpCatQueues();
                QQBotService.Tick();
                if (AllIdle())
                {
                    return;
                }
                Thread.Sleep(FrameSleepMs);
            }
            Console.WriteLine("[CatHome4] 驱动帧上限 " + MaxFramesPerRun + " 到达——仍有未闭环活动");
        }

        /// <summary>
        /// 三 Cat 全部 Idle 判定——状态行全部 =Idle（编排者双状态机都 Idle 才算空闲）
        /// </summary>
        /// <returns>true=全部空闲</returns>
        private static bool AllIdle()
        {
            // P9.1 会话判定——全部会话 Idle（原三 Cat 语料判定保留）
            for (int i = 0; i < _chatBridge.Sessions.Count; i++)
            {
                if (!_chatBridge.Sessions[i].IsIdle)
                {
                    return false;
                }
            }
            if (!IsFlowIdle(_quickHandle))
            {
                return false;
            }
            // R0.2 工具组 Flow 全部 Idle 判定——遍历句柄表（TextCat/MauCat/CsCat/ConfigCat）
            foreach (KeyValuePair<string, FlowHandle> kv in _toolFlowHandles)
            {
                if (!IsFlowIdle(kv.Value))
                {
                    return false;
                }
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
            if (handle == null)
            {
                return true;
            }
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
                ObserveService.PrintStatus();
                return true;
            }

            if (line == "statusq")
            {
                ObserveService.PrintStatusShort();
                return true;
            }

            if (line == "pid")
            {
                Console.WriteLine("[CatHome4] pid=" + Environment.ProcessId);
                return true;
            }

            if (line.StartsWith("run ", StringComparison.Ordinal))
            {
                long frames;
                if (!long.TryParse(line.Substring(4).Trim(), out frames) || frames < 0)
                {
                    Console.WriteLine("[CatHome4] run 参数无效——需非负整数帧数");
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
                    Console.WriteLine("格式: send QuickCat <system>|<content> 或 send Chat <内容>");
                }
                return true;
            }

            if (!DispatchCommand(line))
            {
                Console.WriteLine("格式: QuickCat <system>|<content> 或 Chat <内容>");
                return true;
            }
            DriveUntilIdle();
            ObserveService.PrintStatusShort();
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
                Console.WriteLine("[CatHome4] 脚本文件不存在: " + path);
                return 2;
            }
            string[] lines = File.ReadAllLines(path);
            Console.WriteLine("[CatHome4] 脚本模式 | " + lines.Length + " 行 | pid=" + Environment.ProcessId);
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
            Console.WriteLine("[CatHome4] 脚本结束 | pid=" + Environment.ProcessId);
            return 0;
        }
    }
}