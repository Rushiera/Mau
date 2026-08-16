using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
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
        // [段1] LLM 桥常量——P4 配置项机制接管：llm.* 从 Data/config/llm.cfg 拉起（改配置不重编译）；key 优先级：配置 → 环境变量 DEEPSEEK_API_KEY
        /// <summary>宿主帧节流——每帧现实毫秒（600 帧工单超时 ≈ 30s；QuickCat LLM 回投窗口 5-60s）</summary>
        private const int FrameSleepMs = 50;

        /// <summary>单轮驱动帧上限——LLM 最坏 60s + 超时结算余量（30000 帧 ≈ 25 分钟空转上限，实际由 Idle 判定提前退出）</summary>
        private const int MaxFramesPerRun = 30000;

        /// <summary>宿主组件——Flow 注册/驱动入口</summary>
        private static FlowRunner _runner;

        /// <summary>三语料 Flow 句柄——加载/热重载面</summary>
        private static FlowHandle _toolHandle;
/// <summary>
/// IOTestCat Flow 句柄——热重载面
/// </summary>
        private static FlowHandle _ioHandle;
/// <summary>
/// QuickCat Flow 句柄——热重载面
/// </summary>
        private static FlowHandle _quickHandle;

        /// <summary>注册 ID——观测与回收用</summary>
        private static long _toolId;
/// <summary>
/// IOTestCat 注册 ID——观测与回收用
/// </summary>
        private static long _ioId;
/// <summary>
/// QuickCat 注册 ID——观测与回收用
/// </summary>
        private static long _quickId;

        /// <summary>命令总线——Command 投递器直接引用（SetText 面）</summary>
        private static CommandBus _bus;

        /// <summary>OA 工单平台——观测快照（诊断期）</summary>
        private static OA _oa;
/// <summary>
/// 主程序入口——参数路由：无参=交互模式 / --selfcheck=启动自检 / --run "指令"=单指令脚本模式 / --script <file>=指令文件批量模式（P3a 热重载实测通道）
/// </summary>
///

        ///
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
                else if (args[i] == "--script" && i + 1 < args.Length)
                {
                    mode = "script:" + args[i + 1];
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
            if (mode.StartsWith("script:", StringComparison.Ordinal))
            {
                return RunScript(mode.Substring(7));
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
            string configDir = Path.Combine(Directory.GetCurrentDirectory(), "Data", "config");
            ConfigStore llmConfig = ConfigStore.Load(Path.Combine(configDir, "llm.cfg"));
            DataBox.Bind<ConfigStore>(llmConfig);
            _llmRuntime = new DeepSeekLlmRuntime(llmConfig);
            DataBox.Bind<ILlmRuntime>(_llmRuntime);
            AuditStore audit = new AuditStore(10000);
            audit.ConfigureAudit(Path.Combine(Directory.GetCurrentDirectory(), "Data", "audit"), "run", 3);
            AuditStore.Default = audit;
            _runner.Audit = audit;
            // [段3b] LogStore 落盘——C/O 类专属 Log 持久化（P3c 观测全链——帧号可回溯；按会话命名）
            string logDir = Path.Combine(Directory.GetCurrentDirectory(), "Data", "logs");
            if (!Directory.Exists(logDir))
            {
                Directory.CreateDirectory(logDir);
            }
            LogStore.ConfigureLogFile(Path.Combine(logDir, "run_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".log"));
            // [段4] 四语料加载——编排者先注册（Command 键位分配稳定）
            _toolHandle = FlowHandle.Load(Path.Combine(dllDir, "FL_ToolTestCat.dll"));
            _ioHandle = FlowHandle.Load(Path.Combine(dllDir, "FL_IoTestCat.dll"));
            _quickHandle = FlowHandle.Load(Path.Combine(dllDir, "FL_QuickCat.dll"));
            _majorHandle = FlowHandle.Load(Path.Combine(dllDir, "FL_MajorDomoCat.dll"));
            _toolId = _runner.RegisterFlow(_toolHandle.Flow, "ToolTestCat");
            _ioId = _runner.RegisterFlow(_ioHandle.Flow, "IOTestCat");
            _quickId = _runner.RegisterFlow(_quickHandle.Flow, "QuickCat");
            _majorId = _runner.RegisterFlow(_majorHandle.Flow, "MajorDomoCat");
            // [段5] 会话面——上下文 + 前文恢复 + 工具定义（P5：MajorDomoCat 会话中枢）
            _chatContext = new ChatContext();
            _chatContext.SetSystemPrompt(BuildSystemPrompt());
            _sessionStore = new SessionStore(Path.Combine(Directory.GetCurrentDirectory(), "Data", "sessions", "majordomo.json"));
            LlmMessage[] restored;
            if (_sessionStore.TryLoad(out restored))
            {
                _chatContext.ReplaceMessages(restored);
                Console.WriteLine("[CH4.Entry] 会话前文恢复: " + restored.Length + " 条消息");
            }
            _tools = BuildTools();
            _pendingToolCalls = new List<ToolCallInfo>();
            string llmKeyProbe = llmConfig.Get("llm.api_key", "");
            if (llmKeyProbe.Length == 0)
            {
                string envKeyProbe = Environment.GetEnvironmentVariable("DEEPSEEK_API_KEY");
                if (envKeyProbe != null)
                {
                    llmKeyProbe = envKeyProbe;
                }
            }
            Console.WriteLine("[CH4.Entry] 就绪 | 四 Cat: ToolTestCat#" + _toolId + " IOTestCat#" + _ioId + " QuickCat#" + _quickId + " MajorDomoCat#" + _majorId + " | LLM: " + (llmKeyProbe.Length == 0 ? "未注入(ERR路径)" : "已注入") + " | 帧节流 " + FrameSleepMs + "ms");
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
            Console.WriteLine("指令: ReadText <path> | QuickCat <system>|<content> | status | reload <tool|io|quick> [dll] | run <n> | pid | quit");
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
                if (!ExecuteLine(line))
                {
                    break;
                }
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
            if (line.StartsWith("Chat ", StringComparison.Ordinal))
            {
                string chatContent = line.Substring(5).Trim();
                if (chatContent.Length == 0)
                {
                    return false;
                }
                HandleChat(chatContent);
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
        /// 观测出口——三 Cat 状态 + 导线在途 + 最近日志（透明性指标观测面）
        /// </summary>
        private static void PrintStatus()
{
            Console.WriteLine("── 三 Cat 状态 ──");
            PrintFlowStatus("ToolTestCat", _toolHandle);
            PrintFlowStatus("IOTestCat", _ioHandle);
            PrintFlowStatus("QuickCat", _quickHandle);
            // [段1] OA 快照 + 盒子截面 + 审计帧序（杂音过滤——trace.sample 每帧采样隐藏，关键事件帧序可回溯）
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
            Console.WriteLine("── 审计帧序（trace.sample 已过滤）──");
            Console.WriteLine(query.FormatEvents(FilterNoise(events)));
            // [段2] 最近日志——LogStore 内存总账尾部（CMD/OA 专属类别 + 帧号可见）
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
                Console.WriteLine("  " + entry.Time + " | F" + entry.Frame + " | " + LogStore.LevelText(entry.Level) + " | " + cat + entry.Message);
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
/// <summary>
/// reload 热重载——tool|io|quick + 可选 dll 路径（缺省 = 当前 handle 同路径重读）
/// 流程：新 Load + Tick 试跑验证（失败保留旧）→ UnregisterFlow 旧（D1：CommandBus key 同步清理）→ RegisterFlow 新 → 旧 TryUnload → 预热 → 报告
/// </summary>
/// <param name = "args">cat + 空格 + dll 路径（dll 可选）</param>
private static void ExecuteReload(string args)
{
            string[] parts = args.Split(' ');
            string cat = parts[0].Trim();
            string dllPath = parts.Length > 1 ? parts[1].Trim() : "";
            // [段0] 忙时拒绝——工具批次执行中（Chat 处理中）reload 会导致旧批次完成信号永不置位（WaitForTools 空转帧上限）
            if (_toolBatchActive)
            {
                Console.WriteLine("[CH4.Entry] reload 拒绝: 工具批次执行中（Chat 处理中）——等待完成后再试");
                return;
            }
            FlowHandle oldHandle;
            long oldId;
            string name;
            if (cat == "tool")
            {
                oldHandle = _toolHandle;
                oldId = _toolId;
                name = "ToolTestCat";
            }
            else if (cat == "io")
            {
                oldHandle = _ioHandle;
                oldId = _ioId;
                name = "IOTestCat";
            }
            else if (cat == "quick")
            {
                oldHandle = _quickHandle;
                oldId = _quickId;
                name = "QuickCat";
            }
            else if (cat == "major")
            {
                oldHandle = _majorHandle;
                oldId = _majorId;
                name = "MajorDomoCat";
            }
            else
            {
                Console.WriteLine("[CH4.Entry] reload 目标无效——tool|io|quick|major");
                return;
            }
            if (dllPath.Length == 0)
            {
                dllPath = oldHandle.SourceDll;
            }
            // [段1] 加载新版本（Load 异常 = dll 损坏——失败保留旧；试跑验证移到注册后 runner.Tick——FlowContext 正确注入）
            FlowHandle newHandle;
            try
            {
                newHandle = FlowHandle.Load(dllPath);
            }
            catch (Exception ex)
            {
                Console.WriteLine("[CH4.Entry] reload " + name + " 失败: 新版本加载未通过——" + ex.Message);
                return;
            }
            // [段2] 换注册——卸旧（D1 修复：CommandBus key 同步清理 + DataBox FlowId scope 清理）→ 注册新 → 试跑帧（runner 帧序注入 FlowContext=newId——CmdPump 真实注册，无幽灵 owner）
            _runner.UnregisterFlow(oldId);
            long newId = _runner.RegisterFlow(newHandle.Flow, name);
            try
            {
                _runner.Tick();
            }
            catch (Exception ex)
            {
                // [段2b] 试跑异常回滚——重载旧 dll 全新实例（CmdPump 全新注册；旧 handle 弃用）
                _runner.UnregisterFlow(newId);
                newHandle.TryUnload(3);
                FlowHandle rollback;
                long rollbackId;
                try
                {
                    rollback = FlowHandle.Load(oldHandle.SourceDll);
                    rollbackId = _runner.RegisterFlow(rollback.Flow, name);
                    for (int i = 0; i < 10; i++)
                    {
                        _runner.Tick();
                    }
                }
                catch (Exception ex2)
                {
                    Console.WriteLine("[CH4.Entry] reload " + name + " 失败: 试跑异常且回滚失败——" + ex.Message + " / " + ex2.Message);
                    return;
                }
                oldHandle.TryUnload(3);
                SetCatHandle(cat, rollback, rollbackId);
                Console.WriteLine("[CH4.Entry] reload " + name + " 失败: 试跑帧异常已回滚（旧版本全新实例 #" + rollbackId + "）——" + ex.Message);
                return;
            }
            // [段3] 成功路径——换句柄 + 卸载旧 ALC + 预热帧
            oldHandle.TryUnload(3);
            SetCatHandle(cat, newHandle, newId);
            for (int i = 0; i < 10; i++)
            {
                _runner.Tick();
            }
            string[] keyDic = _bus.GetKeyDic();
            Console.WriteLine("[CH4.Entry] reload " + name + ": #" + oldId + " → #" + newId + " | pid=" + Environment.ProcessId + " | " + keyDic[0]);
            for (int k = 0; k < keyDic.Length; k++)
            {
                Console.WriteLine("    " + keyDic[k]);
            }
        }/// <summary>
/// 单行指令执行——交互/脚本共用（true=继续，false=结束）
/// </summary>
/// <param name = "line">已 Trim 的指令行</param>
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
/// <param name = "path">指令文件路径（# 开头行为注释）</param>
/// <returns>退出码</returns>
 private  static  int  RunScript ( string  path ) { if  ( ! File . Exists ( path ) ) { Console . WriteLine ( "[CH4.Entry] 脚本文件不存在: " + path ) ;  return  2 ;  } string [ ]  lines  =  File . ReadAllLines ( path ) ;  Console . WriteLine ( "[CH4.Entry] 脚本模式 | " + lines . Length + " 行 | pid=" + Environment . ProcessId ) ;  for  ( int  i  =  0 ;  i < lines . Length ;  i ++ ) { string  line  =  lines [ i ] . Trim ( ) ;  if  ( line . Length == 0 || line . StartsWith ( "#" ,  StringComparison . Ordinal ) ) { continue ;  } Console . WriteLine ( "── 脚本指令 " + ( i + 1 ) + ": " + line + " ──" ) ;  if  ( ! ExecuteLine ( line ) ) { break ;  } } Console . WriteLine ( "[CH4.Entry] 脚本结束 | pid=" + Environment . ProcessId ) ;  return  0 ;  }
/// <summary>
/// 精简观测出口——三 Cat 状态 + OA 快照 + 最近日志（跳过审计帧序/盒子截面——热重载实测断言面）
/// </summary>
private static void PrintStatusShort()
{
    Console.WriteLine("── 三 Cat 状态 ──");
    PrintFlowStatus("ToolTestCat", _toolHandle);
    PrintFlowStatus("IOTestCat", _ioHandle);
    PrintFlowStatus("QuickCat", _quickHandle);
    OAView oaView = _oa.GetSnapshot();
    Console.WriteLine("── OA 快照 ── Open=" + oaView.OpenCount + " Work=" + oaView.WorkCount + " Closed=" + oaView.ClosedCount + " Timeout=" + oaView.TimeoutCount);
    List<LogStore.LogEntry> logs = LogStore.AllLog;
    int start = logs.Count - 10;
    if (start < 0)
    {
        start = 0;
    }

    Console.WriteLine("── 最近日志 ──");
    for (int i = start; i < logs.Count; i++)
    {
        LogStore.LogEntry entry = logs[i];
        string cat = entry.Category.Length > 0 ? "[" + entry.Category + "] " : "";
        Console.WriteLine("  " + entry.Time + " | F" + entry.Frame + " | " + LogStore.LevelText(entry.Level) + " | " + cat + entry.Message);
    }
}
/// <summary>
/// 更新指定 Cat 的句柄 + 注册 ID 字段
/// </summary>
/// <param name = "cat">tool|io|quick</param>
/// <param name = "handle">新句柄</param>
/// <param name = "id">新注册 ID</param>
private static void SetCatHandle(string cat, FlowHandle handle, long id)
{
            if (cat == "tool")
            {
                _toolHandle = handle;
                _toolId = id;
            }
            else if (cat == "io")
            {
                _ioHandle = handle;
                _ioId = id;
            }
            else if (cat == "quick")
            {
                _quickHandle = handle;
                _quickId = id;
            }
            else
            {
                _majorHandle = handle;
                _majorId = id;
            }
        }/// <summary>
/// 审计事件杂音过滤——隐藏 trace.sample 类（主动传感器每帧采样刷屏；trace.fire/state 关键时序保留）
/// </summary>
/// <param name = "events">原始事件序列</param>
/// <returns>过滤后事件序列</returns>
private static AuditEvent[] FilterNoise(AuditEvent[] events)
{
    List<AuditEvent> kept = new List<AuditEvent>();
    for (int i = 0; i < events.Length; i++)
    {
        if (events[i].Category == "trace.sample")
        {
            continue;
        }

        kept.Add(events[i]);
    }

    return kept.ToArray();
}/// <summary>
/// LLM 运行时——ChatStream 调度（Bootstrap 注入）
/// </summary>
private static ILlmRuntime _llmRuntime; 
/// <summary>
/// MajorDomoCat Flow 句柄——会话中枢工具执行器（P5 新猫）
/// </summary>
 private  static  FlowHandle  _majorHandle ;  
/// <summary>
/// MajorDomoCat 注册 ID——观测与回收用
/// </summary>
 private  static  long  _majorId ;  
/// <summary>
/// 会话上下文——MajorDomoCat 消息历史（前文持久化面）
/// </summary>
 private  static  ChatContext  _chatContext ;  
/// <summary>
/// 会话前文管理器——Data/sessions/majordomo.json 落盘（重启恢复）
/// </summary>
 private  static  SessionStore  _sessionStore ;  
/// <summary>
/// 工具定义——P5 测试期写死两件（read_file 读 + ask 问）
/// </summary>
 private  static  ToolSpec [ ]  _tools ;  
/// <summary>
/// LLM 后台运行中标志（Task.Run 消费 ChatStream）
/// </summary>
 private  static  volatile  bool  _llmBusy ;  
/// <summary>
/// LLM 后台结果——完整回复文本
/// </summary>
 private  static  string  _llmResultText ;  
/// <summary>
/// LLM 后台结果——完整思考文本（工具轮次回传铁律）
/// </summary>
 private  static  string  _llmReasoning ;  
/// <summary>
/// LLM 后台结果——tool_calls JSON 数组（聚合后整体）
/// </summary>
 private  static  string  _llmToolCallsJson ;  
/// <summary>
/// LLM 后台错误标志
/// </summary>
 private  static  bool  _llmError ;  
/// <summary>
/// LLM 后台错误文本（ERR| 前缀——失败可见性）
/// </summary>
 private  static  string  _llmErrorText ;  
/// <summary>
/// 待回传工具调用清单——与工具执行结果配对（按顺序回传）
/// </summary>
 private  static  List < ToolCallInfo > _pendingToolCalls ;  
/// <summary>
/// 工具循环收敛上限——3 轮（莎拍板：单读/单问/并发读问覆盖测试场景）
/// </summary>
 private  const  int  MaxToolRounds  =  3 ;
/// <summary>
/// 后台消费 LLM 流——消息序列 + 工具定义 → 文本/思考/tool_calls 累积（Task.Run——主线程零阻塞）。
/// </summary>
/// <param name = "messages">消息序列</param>
/// <param name = "tools">工具定义</param>
private static void RunLlmInBackground(LlmMessage[] messages, ToolSpec[] tools)
{
    _llmBusy = true;
    _llmResultText = "";
    _llmReasoning = "";
    _llmToolCallsJson = "";
    _llmError = false;
    _llmErrorText = "";
    System.Threading.Tasks.Task.Run(async delegate
    {
        try
        {
            System.Text.StringBuilder text = new System.Text.StringBuilder();
            System.Text.StringBuilder reasoning = new System.Text.StringBuilder();
            string toolCalls = "";
            await foreach (LlmStreamEvent ev in _llmRuntime.ChatStream(messages, tools))
            {
                if (ev.Kind == LlmStreamKind.Text)
                {
                    text.Append(ev.Text);
                }
                else if (ev.Kind == LlmStreamKind.Reasoning)
                {
                    reasoning.Append(ev.Text);
                }
                else if (ev.Kind == LlmStreamKind.ToolCalls)
                {
                    toolCalls = ev.Text;
                }
                else if (ev.Kind == LlmStreamKind.Error)
                {
                    _llmError = true;
                    _llmErrorText = ev.Text;
                }
            }

            _llmResultText = text.ToString();
            _llmReasoning = reasoning.ToString();
            _llmToolCallsJson = toolCalls;
        }
        catch (Exception ex)
        {
            _llmError = true;
            _llmErrorText = "ERR|" + ex.GetType().Name + "|" + ex.Message;
        }
        finally
        {
            _llmBusy = false;
        }
    });
}/// <summary>
/// 等待 LLM 后台完成——帧驱动 + 帧上限兜底（超时按错误回传）
/// </summary>
private static void WaitForLlm()
{
    for (int i = 0; i < MaxFramesPerRun; i++)
    {
        if (!_llmBusy)
        {
            return;
        }

        _runner.Tick();
        Thread.Sleep(FrameSleepMs);
    }

    _llmBusy = false;
    _llmError = true;
    _llmErrorText = "ERR|LLM_TIMEOUT|LLM 调用超时（帧上限 " + MaxFramesPerRun + "）";
}/// <summary>
/// 等待语料工具执行完成——轮询 llm_tool_done 标志（语料合流置位）+ 帧上限兜底
/// </summary>
private static void WaitForTools()
{
            for (int i = 0; i < MaxFramesPerRun; i++)
            {
                _runner.Tick();
                string done;
                if (DataBox.TryGet<string>("global", "llm_tool_done", out done) && done != null && done == "1")
                {
                    return;
                }
                Thread.Sleep(FrameSleepMs);
            }
            Console.WriteLine("[MajorDomoCat] 工具执行超时（帧上限 " + MaxFramesPerRun + "）——按空结果回传");
            DataBox.Set<string>("global", "llm_tool_done", "");
        }/// <summary>
/// 构建系统提示词——MajorDomoCat 会话中枢身份 + 工具语义声明
/// </summary>
/// <returns>系统提示词</returns>
private static string BuildSystemPrompt()
{
    return "你是 MajorDomoCat——CH4 自举宿主的管理员对话中枢（P5 工具协调测试）。" + System.Environment.NewLine + "你有两个工具：" + System.Environment.NewLine + "- read_file(path)：读取指定路径的文本文件内容" + System.Environment.NewLine + "- ask(question)：向 QuickCat 问答子工具提问，获取简洁回答" + System.Environment.NewLine + "需要文件内容时调用 read_file；需要独立问答时调用 ask；可以同时调用多个工具（并发执行）。" + System.Environment.NewLine + "工具结果返回后，基于结果继续回答用户。";
}/// <summary>
/// 构建工具定义——P5 测试期写死两件（read_file + ask——OpenAI function schema）
/// </summary>
/// <returns>工具数组</returns>
private static ToolSpec[] BuildTools()
{
    ToolSpec read = new ToolSpec("read_file", "读取指定路径的文本文件内容", "{\"type\":\"object\",\"properties\":{\"path\":{\"type\":\"string\",\"description\":\"要读取的文件路径\"}},\"required\":[\"path\"]}");
    ToolSpec ask = new ToolSpec("ask", "向 QuickCat 问答子工具提问，获取简洁回答", "{\"type\":\"object\",\"properties\":{\"question\":{\"type\":\"string\",\"description\":\"要提问的问题\"}},\"required\":[\"question\"]}");
    return new ToolSpec[]
    {
        read,
        ask
    };
}/// <summary>
/// 读取 JSON 对象字符串属性——防御式（缺字段返回空串）
/// </summary>
/// <param name = "obj">JSON 对象</param>
/// <param name = "prop">属性名</param>
/// <returns>属性值</returns>
private static string GetStringProp(JsonElement obj, string prop)
{
    JsonElement value;
    if (obj.TryGetProperty(prop, out value) && value.ValueKind == JsonValueKind.String)
    {
        string got = value.GetString();
        if (got != null)
        {
            return got;
        }
    }

    return "";
}/// <summary>
/// 从 arguments JSON 提取参数——防御式（解析失败返回空串）
/// </summary>
/// <param name = "argumentsJson">参数 JSON</param>
/// <param name = "key">参数名</param>
/// <returns>参数值</returns>
private static string ExtractArg(string argumentsJson, string key)
{
    try
    {
        using (JsonDocument doc = JsonDocument.Parse(argumentsJson))
        {
            return GetStringProp(doc.RootElement, key);
        }
    }
    catch (Exception)
    {
        return "";
    }
}/// <summary>
/// 待回传工具清单中是否含指定工具名
/// </summary>
/// <param name = "name">工具名</param>
/// <returns>true=含</returns>
private static bool HasTool(string name)
{
    for (int i = 0; i < _pendingToolCalls.Count; i++)
    {
        if (_pendingToolCalls[i].Name == name)
        {
            return true;
        }
    }

    return false;
}/// <summary>
/// 截断显示文本——控制台防刷屏
/// </summary>
/// <param name = "text">原文</param>
/// <param name = "max">上限</param>
/// <returns>截断文本</returns>
private static string TrimDisplay(string text, int max)
{
    if (text == null)
    {
        return "";
    }

    if (text.Length <= max)
    {
        return text;
    }

    return text.Substring(0, max) + "...";
}/// <summary>
/// 投递工具调用——解析 tool_calls JSON → 参数落全局盒 + 分支 key 投递（信号名即解析结果）。
/// 已知工具（read_file/ask）投递语料；未知工具记录待回传 ERR。
/// </summary>
/// <param name = "toolCallsJson">tool_calls JSON 数组</param>
/// <returns>true=有已知工具已投递（语料将执行）</returns>
private static bool DispatchToolCalls(string toolCallsJson)
{
            _pendingToolCalls.Clear();
            try
            {
                using (JsonDocument doc = JsonDocument.Parse(toolCallsJson))
                {
                    JsonElement root = doc.RootElement;
                    for (int i = 0; i < root.GetArrayLength(); i++)
                    {
                        JsonElement call = root[i];
                        string id = GetStringProp(call, "id");
                        // OpenAI wire：name/arguments 在 function 嵌套对象内
                        string name = "";
                        string arguments = "";
                        JsonElement funcEl;
                        if (call.TryGetProperty("function", out funcEl))
                        {
                            name = GetStringProp(funcEl, "name");
                            arguments = GetStringProp(funcEl, "arguments");
                        }
                        ToolCallInfo info = new ToolCallInfo(id, name, arguments);
                        _pendingToolCalls.Add(info);
                        if (name == "read_file")
                        {
                            _bus.SetText("TOOL_Read_Path", ExtractArg(arguments, "path"), "llm");
                        }
                        else if (name == "ask")
                        {
                            _bus.SetText("TOOL_Ask_Content", ExtractArg(arguments, "question"), "llm");
                        }
                    }
                }
                // [段2] 分支投递——批次开始沿 + 每线必投其一（要执行 / 跳过）
                bool wantRead = HasTool("read_file");
                bool wantAsk = HasTool("ask");
                if (wantRead || wantAsk)
                {
                    // 批次开始——独立沿（与 Exec/Skip 分离，避免同帧沿竞争被先声明导线消费）
                    _bus.SetText("TOOL_Batch_Start", "", "llm");
                    if (wantRead)
                    {
                        _bus.SetText("TOOL_Exec_Read", "", "llm");
                    }
                    else
                    {
                        _bus.SetText("TOOL_Skip_Read", "", "llm");
                    }
                    if (wantAsk)
                    {
                        _bus.SetText("TOOL_Exec_Ask", "", "llm");
                    }
                    else
                    {
                        _bus.SetText("TOOL_Skip_Ask", "", "llm");
                    }
                    _toolBatchActive = true;
                    return true;
                }
                return false;
            }
            catch (Exception ex)
            {
                Console.WriteLine("[MajorDomoCat] tool_calls 解析失败: " + ex.Message);
                return false;
            }
        }/// <summary>
/// 收集工具结果——读全局结果盒 → 按 tool_calls 顺序回传上下文 → 清盒。
/// </summary>
private static void CollectToolResults()
{
            string readResult = "";
            string askResult = "";
            DataBox.TryGet<string>("global", "llm_result_read", out readResult);
            DataBox.TryGet<string>("global", "llm_result_ask", out askResult);
            // TryGet 失败时 out 为 default(null)——防御归一（DataBox 契约：失败赋 default）
            if (readResult == null)
            {
                readResult = "";
            }
            if (askResult == null)
            {
                askResult = "";
            }
            // [段1] 清盒——语料下批写入前干净（残留防护）
            DataBox.Set<string>("global", "llm_tool_done", "");
            DataBox.Set<string>("global", "llm_result_read", "");
            DataBox.Set<string>("global", "llm_result_ask", "");
            // [段2] 按 tool_calls 顺序回传——每调用一条 tool result；未知工具 ERR 文本
            for (int i = 0; i < _pendingToolCalls.Count; i++)
            {
                ToolCallInfo info = _pendingToolCalls[i];
                string result;
                if (info.Name == "read_file")
                {
                    result = readResult;
                }
                else if (info.Name == "ask")
                {
                    result = askResult;
                }
                else
                {
                    result = "ERR|UNKNOWN_TOOL|未知工具: " + info.Name;
                }
                if (result.Length == 0)
                {
                    result = "ERR|EMPTY_RESULT|工具执行无结果（超时或失败）";
                }
                _chatContext.AddToolResult(info.Id, info.Name, result);
                Console.WriteLine("  [工具结果] " + info.Name + " → " + TrimDisplay(result, 120));
            _pendingToolCalls.Clear();
            _toolBatchActive = false;
        }
        }/// <summary>
/// 会话中枢处理——Chat 指令入口（P5 工具协调核心）。
/// 流程：追加用户消息 → 工具循环（≤3 轮）：LLM 后台流式 → 纯文本则完成 / tool_calls 则投递语料执行 → 结果回传续轮。
/// 消息维护与前文落盘在宿主（CH4 侧实现前文管理器）；工具执行在语料（MajorDomoCat）。
/// </summary>
/// <param name = "content">用户消息内容</param>
private static void HandleChat(string content)
{
    _chatContext.AddUserMessage(content);
    Console.WriteLine("── MajorDomoCat 处理中 ──");
    for (int round = 0; round < MaxToolRounds; round++)
    {
        // [段1] LLM 调用——后台流式（消息序列 + 工具定义）
        LlmMessage[] messages = _chatContext.GetMessages();
        RunLlmInBackground(messages, _tools);
        WaitForLlm();
        if (_llmError)
        {
            _chatContext.AddAssistantMessage(_llmErrorText);
            Console.WriteLine("[MajorDomoCat] LLM 错误: " + _llmErrorText);
            break;
        }

        if (_llmToolCallsJson.Length == 0)
        {
            // [段2] 纯文本回复——本轮完成
            _chatContext.AddAssistantMessage(_llmResultText);
            Console.WriteLine("[MajorDomoCat] " + _llmResultText);
            break;
        }

        // [段3] 工具调用——追加 assistant tool_calls + 投递语料执行
        _chatContext.AddAssistantToolCalls(_llmToolCallsJson, _llmReasoning);
        Console.WriteLine("[MajorDomoCat] 工具调用(" + (round + 1) + "/" + MaxToolRounds + "): " + TrimDisplay(_llmToolCallsJson, 200));
        bool dispatched = DispatchToolCalls(_llmToolCallsJson);
        if (dispatched)
        {
            WaitForTools();
            CollectToolResults();
            Console.WriteLine("[MajorDomoCat] 工具结果已回传，续轮");
        }
        else
        {
            // 无已知工具——直接按 ERR 回传（不经过语料）
            CollectToolResults();
            Console.WriteLine("[MajorDomoCat] 无已知工具可执行——按错误回传");
        }
    }

    // [段4] 前文落盘——会话结束保存（重启恢复面）
    _sessionStore.Save(_chatContext.GetMessages());
    Console.WriteLine("[CH4.Entry] 会话前文已落盘: " + _chatContext.GetMessageCount() + " 条消息");
}/// <summary>
/// 工具批次执行中标志——DispatchToolCalls 置位 / CollectToolResults 复位；reload 忙时拒绝（防旧批次完成信号永不置位 → 空转 25 分钟）
/// </summary>
private static bool _toolBatchActive;}
}
