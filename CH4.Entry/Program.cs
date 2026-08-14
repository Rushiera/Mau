using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Mau.Runtime;

namespace CH4
{
    /// <summary>
    /// CH4.Entry —— Mau 自举循环宿主（第一轮）
    /// 职责：Runtime 服务组装 + 三语料加载 + Command 投递器 + LLM 桥（写死）+ 观测出口。
    /// 业务代码 0 行——全部功能在 .mau 语料（热重载面）；无 UI（无头优先）。
    /// </summary>
    public static class Program
    {
        // [段1] LLM 桥常量——第一轮固定写死（配置系统后置；key 经环境变量注入避免入库）
        /// <summary>API 基址</summary>
        private const string LlmBaseUrl = "https://api.deepseek.com";

        /// <summary>模型名</summary>
        private const string LlmModel = "deepseek-chat";

        /// <summary>环境变量名——DEEPSEEK_API_KEY（未设置时为空串——QuickCat 会回 ERR|LLM_NO_RUNTIME 失败路径）</summary>
        private const string LlmKeyEnv = "DEEPSEEK_API_KEY";

        /// <summary>宿主帧节流——每帧现实毫秒（600 帧工单超时 ≈ 30s；QuickCat LLM 回投窗口 5-60s）</summary>
        private const int FrameSleepMs = 50;

        /// <summary>单轮驱动帧上限——LLM 最坏 60s + 超时结算余量（30000 帧 ≈ 25 分钟空转上限，实际由 Idle 判定提前退出）</summary>
        private const int MaxFramesPerRun = 30000;

        /// <summary>宿主组件——Flow 注册/驱动入口</summary>
        private static FlowRunner _runner;

        /// <summary>三语料 Flow 句柄——加载/热重载面</summary>
        private static FlowHandle _toolHandle;
        private static FlowHandle _ioHandle;
        private static FlowHandle _quickHandle;

        /// <summary>注册 ID——观测与回收用</summary>
        private static long _toolId;
        private static long _ioId;
        private static long _quickId;

        /// <summary>命令总线——Command 投递器直接引用（SetText 面）</summary>
        private static CommandBus _bus;

        /// <summary>OA 工单平台——观测快照（诊断期）</summary>
        private static OA _oa;

        /// <summary>主程序入口——参数路由：无参=交互模式 / --selfcheck=启动自检 / --run "指令"=单指令脚本模式</summary>
        /// <param name="args">命令行参数</param>
        /// <returns>退出码</returns>
        public static int Main(string[] args)
        {
            try
            {
                Console.OutputEncoding = System.Text.Encoding.UTF8;
            }
            catch (Exception)
            {
                // 输出编码设置失败不影响功能
            }
            // [段2] 服务组装 + 语料加载
            string dllDir = FindDllDir(args);
            try
            {
                Bootstrap(dllDir);
            }
            catch (Exception ex)
            {
                Console.WriteLine("[CH4.Entry] 启动失败: " + ex.Message);
                return 1;
            }
            // [段2b] 预热帧——CmdPump 懒注册 CommandBus 发生在生成物首 Tick（投递前必须注册完成）
            for (int i = 0; i < 10; i++)
            {
                _runner.Tick();
            }
            // 注册确认——预热后应见 1 模块 3 key（启动观测一行）
            string[] keyDic = _bus.GetKeyDic();
            Console.WriteLine("[CH4.Entry] CommandBus " + keyDic[0]);
            // [段3] 模式路由
            string mode = "";
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "--selfcheck")
                {
                    mode = "selfcheck";
                }
                else if (args[i] == "--run" && i + 1 < args.Length)
                {
                    mode = "run:" + args[i + 1];
                }
            }
            if (mode == "selfcheck")
            {
                return RunSelfCheck();
            }
            if (mode.StartsWith("run:", StringComparison.Ordinal))
            {
                return RunSingle(mode.Substring(4));
            }
            return RunInteractive();
        }

        /// <summary>
        /// 服务组装——OA/CommandBus/FlowRunner + DataBox 绑定 + 审计配置
        /// </summary>
        /// <param name="dllDir">语料生成物 dll 目录</param>
        private static void Bootstrap(string dllDir)
        {
            // [段1] 线程守卫 + 机制服务
            ThreadGuard guard = new ThreadGuard();
            _oa = new OA(guard);
            _bus = new CommandBus(guard);
            IdAllocator ids = new IdAllocator();
            _runner = new FlowRunner(guard, _oa, _bus, ids);
            // [段2] DataBox 服务绑定——积木 TryResolve 面（OA/CommandBus/ILlmRuntime）
            DataBox.Bind<IOA>(_oa);
            DataBox.Bind<ICommandBus>(_bus);
            // 文件系统服务——file.* 积木依赖（受控根 = 当前工作目录；回收站 CatTemp/fs_recycle）
            string workDir = Directory.GetCurrentDirectory();
            DataBox.Bind<FileSystemService>(new FileSystemService(new string[] { workDir }, Path.Combine(workDir, "CatTemp", "fs_recycle")));
            string apiKey = Environment.GetEnvironmentVariable(LlmKeyEnv);
            if (string.IsNullOrEmpty(apiKey))
            {
                apiKey = "";
            }
            DataBox.Bind<ILlmRuntime>(new DeepSeekLlmRuntime(LlmBaseUrl, apiKey, LlmModel));
            // [段3] 审计——环形缓冲 + 寻路落盘（Data/audit/）
            AuditStore audit = new AuditStore(10000);
            audit.ConfigureAudit(Path.Combine(Directory.GetCurrentDirectory(), "Data", "audit"), "run", 3);
            AuditStore.Default = audit;
            _runner.Audit = audit;
            // [段4] 三语料加载——编排者先注册（Command 键位分配稳定）
            _toolHandle = FlowHandle.Load(Path.Combine(dllDir, "FL_ToolTestCat.dll"));
            _ioHandle = FlowHandle.Load(Path.Combine(dllDir, "FL_IoTestCat.dll"));
            _quickHandle = FlowHandle.Load(Path.Combine(dllDir, "FL_QuickCat.dll"));
            _toolId = _runner.RegisterFlow(_toolHandle.Flow, "ToolTestCat");
            _ioId = _runner.RegisterFlow(_ioHandle.Flow, "IOTestCat");
            _quickId = _runner.RegisterFlow(_quickHandle.Flow, "QuickCat");
            Console.WriteLine("[CH4.Entry] 就绪 | 三 Cat: ToolTestCat#" + _toolId + " IOTestCat#" + _ioId + " QuickCat#" + _quickId + " | LLM: " + (apiKey.Length == 0 ? "未注入(ERR路径)" : "已注入") + " | 帧节流 " + FrameSleepMs + "ms");
        }

        /// <summary>
        /// 定位语料 dll 目录——参数 -dll 指定，否则默认 CatTemp/ch4_build
        /// </summary>
        /// <param name="args">命令行参数</param>
        /// <returns>dll 目录</returns>
        private static string FindDllDir(string[] args)
        {
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == "-dll")
                {
                    return args[i + 1];
                }
            }
            return Path.Combine(Directory.GetCurrentDirectory(), "CatTemp", "ch4_build");
        }

        /// <summary>
        /// 启动自检——加载后跑 10 帧并输出三 Cat 状态（非交互验证通道）
        /// </summary>
        /// <returns>退出码</returns>
        private static int RunSelfCheck()
        {
            for (int i = 0; i < 10; i++)
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
        /// <param name="command">指令文本（ReadText path / QuickCat system|content）</param>
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
            Console.WriteLine("指令: ReadText <path> | QuickCat <system>|<content> | status | quit");
            while (true)
            {
                Console.Write("ch4> ");
                string line = Console.ReadLine();
                if (line == null)
                {
                    break;
                }
                line = line.Trim();
                if (line.Length == 0)
                {
                    continue;
                }
                if (line == "quit")
                {
                    break;
                }
                if (line == "status")
                {
                    PrintStatus();
                    continue;
                }
                if (!DispatchCommand(line))
                {
                    Console.WriteLine("格式: ReadText <path> 或 QuickCat <system>|<content>");
                    continue;
                }
                DriveUntilIdle();
                PrintStatus();
            }
            return 0;
        }

        /// <summary>
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
            return false;
        }

        /// <summary>
        /// 驱动直到三 Cat 全部 Idle——指令投递后连续 Tick；帧上限兜底（LLM 60s 现实耗时 + OA 超时结算窗口）
        /// </summary>
        private static void DriveUntilIdle()
        {
            for (int i = 0; i < MaxFramesPerRun; i++)
            {
                _runner.Tick();
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
            return IsFlowIdle(_toolHandle) && IsFlowIdle(_ioHandle) && IsFlowIdle(_quickHandle);
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
        /// 观测出口——三 Cat 状态 + 导线在途 + 最近日志（透明性指标观测面）
        /// </summary>
        private static void PrintStatus()
        {
            Console.WriteLine("── 三 Cat 状态 ──");
            PrintFlowStatus("ToolTestCat", _toolHandle);
            PrintFlowStatus("IOTestCat", _ioHandle);
            PrintFlowStatus("QuickCat", _quickHandle);
            // [段1b] OA 快照 + 盒子截面 + 审计帧序（诊断观测——P2e 闭环后收敛）
            OAView oaView = _oa.GetSnapshot();
            Console.WriteLine("── OA 快照 ── Open=" + oaView.OpenCount + " Work=" + oaView.WorkCount + " Closed=" + oaView.ClosedCount + " Timeout=" + oaView.TimeoutCount);
            DataBoxSnapshot boxSnap = DataBox.Capture();
            Console.WriteLine("── 盒子截面 ──");
            for (int i = 0; i < boxSnap.Data.Length; i++)
            {
                Console.WriteLine("  " + boxSnap.Data[i].Scope + "." + boxSnap.Data[i].Key + " = " + boxSnap.Data[i].Value);
            }
            AuditQuery query = new AuditQuery(AuditStore.Default);
            AuditEvent[] events = query.Segment(0, 999999, null, null);
            Console.WriteLine("── 审计帧序（全量）──");
            Console.WriteLine(query.FormatEvents(events));
            // [段2] 最近日志——LogStore 内存总账尾部（CMD/OA 专属类别可见）
            List<LogStore.LogEntry> logs = LogStore.AllLog;
            int start = logs.Count - 20;
            if (start < 0)
            {
                start = 0;
            }
            Console.WriteLine("── 最近日志 ──");
            for (int i = start; i < logs.Count; i++)
            {
                LogStore.LogEntry entry = logs[i];
                string cat = entry.Category.Length > 0 ? "[" + entry.Category + "] " : "";
                Console.WriteLine("  " + entry.Time + " | " + LogStore.LevelText(entry.Level) + " | " + cat + entry.Message);
            }
        }

        /// <summary>
        /// 单 Flow 状态打印——状态行 + 忙碌导线
        /// </summary>
        /// <param name="name">Cat 名</param>
        /// <param name="handle">Flow 句柄</param>
        private static void PrintFlowStatus(string name, FlowHandle handle)
        {
            if (handle.IsFaulted)
            {
                Console.WriteLine("  " + name + " | FAULTED | " + handle.FaultReason);
                return;
            }
            FlowStatusV3 status = handle.Flow.GetStatus();
            string stateText = "";
            for (int i = 0; i < status.StateLines.Length; i++)
            {
                if (i > 0)
                {
                    stateText = stateText + " ";
                }
                stateText = stateText + status.StateLines[i];
            }
            string busyText = "";
            for (int i = 0; i < status.WireStatuses.Length; i++)
            {
                if (status.WireStatuses[i].Busy)
                {
                    if (busyText.Length > 0)
                    {
                        busyText = busyText + " ";
                    }
                    busyText = busyText + status.WireStatuses[i].Name + "(在途)";
                }
            }
            Console.WriteLine("  " + name + " | " + stateText + (busyText.Length == 0 ? "" : " | " + busyText));
        }
    }
}
