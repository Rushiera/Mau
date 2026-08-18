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
    /// CH4.Entry —— Mau 自举循环宿主（第一轮）
    /// 职责：Runtime 服务组装 + 语料加载 + Command 投递器 + LLM 桥 + 观测出口 + 指令路由。
    /// 业务代码 0 行——全部功能在 .mau 语料（热重载面）；无 UI（无头优先）。
    /// ChatBridge（工具协调）分部在 Program.Chat.cs——agent 循环基建（2026-08-16 拆分定性）。
    /// </summary>
    public static partial class Program
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

        /// <summary>IOTestCat Flow 句柄——热重载面</summary>
        private static FlowHandle _ioHandle;

        /// <summary>QuickCat Flow 句柄——热重载面</summary>
        private static FlowHandle _quickHandle;

        /// <summary>MajorDomoCat Flow 句柄——会话中枢工具执行器（P5 新猫）</summary>
        private static FlowHandle _majorHandle;

        /// <summary>注册 ID——观测与回收用</summary>
        private static long _toolId;

        /// <summary>IOTestCat 注册 ID——观测与回收用</summary>
        private static long _ioId;

        /// <summary>QuickCat 注册 ID——观测与回收用</summary>
        private static long _quickId;

        /// <summary>MajorDomoCat 注册 ID——观测与回收用</summary>
        private static long _majorId;

        /// <summary>命令总线——Command 投递器直接引用（SetText 面）</summary>
        private static CommandBus _bus;

        /// <summary>OA 工单平台——观测快照（诊断期）</summary>
        private static OA _oa;
/// <summary>HTTP 外观层——Kestrel + Minimal API（P6：快照/SSE/指令/静态页）</summary>
private static HttpHost _httpHost;
        /// <summary>
        /// 主程序入口——参数路由：无参=交互模式 / --selfcheck=启动自检 / --run "指令"=单指令脚本模式 / --script &lt;file&gt;=指令文件批量模式（P3a 热重载实测通道）
        /// </summary>
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
                // 脚本模式无人值守不暂停；交互模式暂停——错误可见（双击 exe 不闪退）
                bool pauseOnFail = true;
                for (int i = 0; i < args.Length; i = i + 1)
                {
                    if (args[i] == "--run" || args[i] == "--script" || args[i] == "--selfcheck")
                    {
                        pauseOnFail = false;
                    }
                }
                if (pauseOnFail)
                {
                    Console.WriteLine("按任意键退出……");
                    Console.ReadKey();
                }
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
        }/// <summary>
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
            // 文件系统服务——file.* 积木依赖（受控根 = 数据根；回收站 CatTemp/fs_recycle）
            string dataRoot = ResolveDataRoot();
            DataBox.Bind<FileSystemService>(new FileSystemService(new string[] { dataRoot }, Path.Combine(dataRoot, "CatTemp", "fs_recycle")));
            string configDir = Path.Combine(dataRoot, "Data", "config");
            ConfigStore llmConfig = ConfigStore.Load(Path.Combine(configDir, "llm.cfg"));
            DataBox.Bind<ConfigStore>(llmConfig);
            _llmRuntime = new DeepSeekLlmRuntime(llmConfig);
            DataBox.Bind<ILlmRuntime>(_llmRuntime);
            AuditStore audit = new AuditStore(10000);
            audit.ConfigureAudit(Path.Combine(dataRoot, "Data", "audit"), "run", 3);
            AuditStore.Default = audit;
            _runner.Audit = audit;
            // [段3b] LogStore 落盘——C/O 类专属 Log 持久化（P3c 观测全链——帧号可回溯；按会话命名）
            string logDir = Path.Combine(dataRoot, "Data", "logs");
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
            _sessionStore = new SessionStore(Path.Combine(dataRoot, "Data", "sessions", "majordomo.json"));
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
                string envKeyProbe = Environment.GetEnvironmentVariable("MAU_LLM_API_KEY");
                if (envKeyProbe != null)
                {
                    llmKeyProbe = envKeyProbe;
                }
            }
            if (llmKeyProbe.Length == 0)
            {
                string envKeyProbe = Environment.GetEnvironmentVariable("DEEPSEEK_API_KEY");
                if (envKeyProbe != null)
                {
                    llmKeyProbe = envKeyProbe;
                }
            }
            string llmState;
            if (llmKeyProbe.Length == 0)
            {
                llmState = "未注入(ERR路径)";
            }
            else
            {
                llmState = "已注入";
            }
            Console.WriteLine("[CH4.Entry] 就绪 | 四 Cat: ToolTestCat#" + _toolId + " IOTestCat#" + _ioId + " QuickCat#" + _quickId + " MajorDomoCat#" + _majorId + " | LLM: " + llmState + " | 帧节流 " + FrameSleepMs + "ms");
            // [段6] HTTP 外观层启动——P6 最小闭环（协议 design-ch4-protocol.md；快照回调 + 指令投递回调注入）
            _httpHost = HttpHost.Start(ResolveHttpPort(llmConfig), BuildSnapshotJson, DispatchCommand);
            Console.WriteLine("[CH4.Entry] HTTP 外观层就绪: http://127.0.0.1:" + _httpHost.Port);
        }
        /// <summary>
        /// 定位语料 dll 目录——参数 -dll 指定，否则默认仓库根 public/app/Flows（统一构筑链部署区）；无仓库根回退 CatTemp/ch4_build
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
            // 统一构筑链默认——public/app/Flows/（design-ch4-deploy §2.1）；仓库根探测（向上找 Mau.sln）
string root = FindRepoRoot(AppContext.BaseDirectory);
            if (root.Length > 0)
            {
                return Path.Combine(root, "public", "app", "Flows");
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
            Console.WriteLine("指令: ReadText <path> | QuickCat <system>|<content> | Chat <内容> | status | reload <tool|io|quick|major> [dll] | run <n> | pid | quit");
            while (true)
            {
                // [段1] 帧驱动——HTTP 快照推送主线程泵（ThreadGuard：快照构建须宿主主线程；空闲时也持续 Tick）
                _runner.Tick();
                if (_httpHost != null)
                {
                    _httpHost.PumpMainThread();
                }
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
                if (_httpHost != null)
                {
                    _httpHost.PumpMainThread();
                }
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
                DataBoxDataEntry d = boxSnap.Data[i];
                if (IsInternalBox(d.Scope, d.Key))
                {
                    continue;
                }
                Console.WriteLine("  " + d.Scope + "." + d.Key + " = " + d.Value);
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
                string cat;
                if (entry.Category.Length > 0)
                {
                    cat = "[" + entry.Category + "] ";
                }
                else
                {
                    cat = "";
                }
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
            string busySuffix;
            if (busyText.Length == 0)
            {
                busySuffix = "";
            }
            else
            {
                busySuffix = " | " + busyText;
            }
            Console.WriteLine("  " + name + " | " + stateText + busySuffix);
        }

        /// <summary>
        /// reload 热重载——tool|io|quick|major + 可选 dll 路径（缺省 = 当前 handle 同路径重读）
        /// 流程：新 Load + Tick 试跑验证（失败保留旧）→ UnregisterFlow 旧（D1：CommandBus key 同步清理）→ RegisterFlow 新 → 旧 TryUnload → 预热 → 报告
        /// </summary>
        /// <param name="args">cat + 空格 + dll 路径（dll 可选）</param>
        private static void ExecuteReload(string args)
        {
            string[] parts = args.Split(' ');
            string cat = parts[0].Trim();
            string dllPath;
            if (parts.Length > 1)
            {
                dllPath = parts[1].Trim();
            }
            else
            {
                dllPath = "";
            }
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
                string cat;
                if (entry.Category.Length > 0)
                {
                    cat = "[" + entry.Category + "] ";
                }
                else
                {
                    cat = "";
                }
                Console.WriteLine("  " + entry.Time + " | F" + entry.Frame + " | " + LogStore.LevelText(entry.Level) + " | " + cat + entry.Message);
            }
        }

        /// <summary>
        /// 更新指定 Cat 的句柄 + 注册 ID 字段
        /// </summary>
        /// <param name="cat">tool|io|quick|major</param>
        /// <param name="handle">新句柄</param>
        /// <param name="id">新注册 ID</param>
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
        }

        /// <summary>
        /// 审计事件杂音过滤——隐藏 trace.sample 类（主动传感器每帧采样刷屏；trace.fire/state 关键时序保留）
        /// </summary>
        /// <param name="events">原始事件序列</param>
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
        }
        /// <summary>
        /// 内部实现盒判定——观测面屏蔽（D5：log.entries/log.sync 是 List/Object 内部数据结构，不入 CLI/快照）
        /// </summary>
        /// <param name="scope">盒子域</param>
        /// <param name="key">盒子键</param>
        /// <returns>true=内部实现盒</returns>
        private static bool IsInternalBox(string scope, string key)
        {
            if (scope == "log" && (key == "entries" || key == "sync"))
            {
                return true;
            }
            return false;
        }
    /// <summary>
    /// 单 Cat 快照 JSON 追加——协议 §3.2 cats[].status 四柱映射（FlowStatusV3 → 匿名对象）
    /// </summary>
    /// <param name = "cats">目标列表</param>
    /// <param name = "name">Cat 名</param>
    /// <param name = "id">注册 ID</param>
    /// <param name = "handle">Flow 句柄</param>
    private static void AppendCatJson(List<object> cats, string name, long id, FlowHandle handle)
{
        if (handle.IsFaulted)
        {
            cats.Add(new { name = name, id = id, faulted = true, faultReason = handle.FaultReason, status = (object)null });
            return;
        }

        FlowStatusV3 status = handle.Flow.GetStatus();
        List<object> sensors = new List<object>();
        for (int i = 0; i < status.SensorValues.Length; i++)
        {
            SignalValueV3 s = status.SensorValues[i];
            sensors.Add(new { name = s.Name, value = s.Value });
        }

        List<object> slots = new List<object>();
        for (int i = 0; i < status.SlotLevels.Length; i++)
        {
            SlotValueV3 s = status.SlotLevels[i];
            slots.Add(new { name = s.Name, available = s.Available, capacity = s.Capacity });
        }

        List<object> wires = new List<object>();
        for (int i = 0; i < status.WireStatuses.Length; i++)
        {
            WireStatusV3 w = status.WireStatuses[i];
            wires.Add(new { name = w.Name, busy = w.Busy, lastTriggerFrame = w.LastTriggerFrame, timedOut = w.TimedOut });
        }

        cats.Add(new { name = name, id = id, faulted = false, faultReason = "", status = new { frame = status.Frame, stateLines = status.StateLines, sensors = sensors, slots = slots, wires = wires } });
    }
    /// <summary>
    /// 构建全量快照 JSON——协议 design-ch4-protocol.md §三（version/pid/frame/cats/oa/logs；logs 按 includeLogs 裁剪）
    /// </summary>
    /// <param name = "includeLogs">是否携带日志（GET 轮询 true / SSE 事件 false——协议 §4.2 snapshot 事件裁剪）</param>
    /// <returns>快照 JSON 文本</returns>
    private static string BuildSnapshotJson(bool includeLogs)
{
        List<object> cats = new List<object>();
        AppendCatJson(cats, "ToolTestCat", _toolId, _toolHandle);
        AppendCatJson(cats, "IOTestCat", _ioId, _ioHandle);
        AppendCatJson(cats, "QuickCat", _quickId, _quickHandle);
        AppendCatJson(cats, "MajorDomoCat", _majorId, _majorHandle);
        OAView oa = _oa.GetSnapshot();
        // [段1] boxes 字段——DataBox 全量截面（协议 v1.1：新增字段旧端忽略；复杂对象摘要化——内部实现盒子不刷爆快照）
        DataBoxSnapshot boxSnap = DataBox.Capture();
        List<object> boxes = new List<object>();
for (int i = 0; i < boxSnap.Data.Length; i++)
            {
                DataBoxDataEntry d = boxSnap.Data[i];
                if (IsInternalBox(d.Scope, d.Key))
                {
                    continue;
                }
                string t = "o";
            object val;
            if (d.Value is bool)
            {
                t = "b";
                val = d.Value;
            }
            else if (d.Value is long || d.Value is int || d.Value is double)
            {
                t = "n";
                val = d.Value;
            }
            else if (d.Value is string)
            {
                t = "s";
                val = d.Value;
            }
            else
            {
                t = "o";
                val = SummarizeBoxValue(d.Value);
            }
            boxes.Add(new { scope = d.Scope, key = d.Key, t = t, value = val });
        }
        // [段2] logs 段——includeLogs 裁剪（SSE 事件空数组；GET ?logs=N 由 HttpHost 动态合成替换）
        object logs;
        if (includeLogs)
        {
            List<object> logList = new List<object>();
            List<LogStore.LogEntry> all = LogStore.AllLog;
            lock (LogStore.Sync)
            {
                int start = all.Count - 50;
                if (start < 0)
                {
                    start = 0;
                }
                for (int i = start; i < all.Count; i++)
                {
                    LogStore.LogEntry entry = all[i];
                    logList.Add(new { time = entry.Time, frame = entry.Frame, level = LogStore.LevelText(entry.Level), category = entry.Category, module = entry.Module, message = entry.Message });
                }
            }
            logs = logList;
        }
        else
        {
            logs = new object[0];
        }
        // [段3] 快照组装——version/pid/frame/cats/oa/boxes/logs（协议 v1.1）
        var snapshot = new
        {
            version = 1,
            pid = Environment.ProcessId,
            frame = FlowRunner.GlobalFrame,
            cats = cats,
            oa = new
            {
                open = oa.OpenCount,
                work = oa.WorkCount,
                closed = oa.ClosedCount,
                timeout = oa.TimeoutCount
            },
            boxes = boxes,
            logs = logs
        };
        return JsonSerializer.Serialize(snapshot);
    }/// <summary>
    /// 盒子复杂值摘要——ToString 截断（快照 JSON 不展开复杂对象——防 log.entries 这类内部实现盒子刷爆快照）
    /// </summary>
    /// <param name = "value">原始值</param>
    /// <returns>摘要文本（≤160 字符）</returns>
    private static string SummarizeBoxValue(object value)
        {
            if (value == null)
            {
                return "null";
            }
            // D5：BCL 内部实现型——短摘要 [类型简单名]（TypeName 不出协议面）
            Type valueType = value.GetType();
string ns = valueType.Namespace;
            if (ns == "System" || ns == "System.Collections.Generic" || ns == "System.Collections")
            {
                string simple = valueType.Name;
                if (simple.Length > 40)
                {
                    simple = simple.Substring(0, 40) + "...";
                }
                return "[" + simple + "]";
            }
            string text = value.ToString() ?? "";
            if (text.Length > 160)
            {
                text = text.Substring(0, 160) + "...";
            }
            return text;
        }/// <summary>
        /// 仓库根探测——从当前目录向上找含 Mau.sln 的目录（部署区定位用；CH4.Entry 可从任意工作目录启动）
        /// </summary>
        /// <param name="startDir">起始目录</param>
        /// <returns>仓库根或空字符串</returns>
        private static string FindRepoRoot(string startDir)
        {
            string dir = new DirectoryInfo(startDir).FullName;
            while (true)
            {
                if (File.Exists(Path.Combine(dir, "Mau.sln")))
                {
                    return dir;
                }
                string parent = Directory.GetParent(dir)?.FullName;
                if (parent == null)
                {
                    return "";
                }
                dir = parent;
            }
        }
        /// <summary>
        /// 数据根解析——部署跟随运行环境（稳定分支即运行基座）：exe 所在目录向上找 Mau.sln 仓库根，Data 挂仓库根；找不到回退当前工作目录
        /// </summary>
        /// <returns>数据根目录</returns>
        private static string ResolveDataRoot()
        {
            string root = FindRepoRoot(AppContext.BaseDirectory);
            if (root.Length > 0)
            {
                return root;
            }
            return Directory.GetCurrentDirectory();
        }
        /// <summary>
        /// HTTP 端口解析——http.port 配置项（协议 §5b 预留）；缺省/非法/越界回退 8080
        /// </summary>
        /// <param name="config">配置存储</param>
        /// <returns>监听端口</returns>
        private static int ResolveHttpPort(ConfigStore config)
        {
            string raw = config.Get("http.port", "8080");
            int port;
            if (!int.TryParse(raw, out port))
            {
                port = 0;
            }
            if (port < 1024 || port > 65535)
            {
                Console.WriteLine("[CH4.Entry] http.port 配置非法(" + raw + ")——回退 8080");
                port = 8080;
            }
            return port;
        }
}
}