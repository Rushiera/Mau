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
    /// 入口/组装面（Main/Bootstrap/FindDllDir）在本文件；
    /// 运行模式分部在 Program.Run.cs；观测面在 Program.Observe.cs；快照面在 Program.Snapshot.cs；
    /// 热重载面在 Program.Reload.cs；路径解析面在 Program.Resolve.cs；工具协调在 Program.Chat.cs（P7b partial 拆分续）。
    /// </summary>
    public static partial class Program
    {
        // [段1] LLM 桥常量——P4 配置项机制接管：llm.* 从 Data/config/llm.cfg 拉起（改配置不重编译）；key 优先级：配置 → 环境变量 DEEPSEEK_API_KEY
        /// <summary>宿主帧节流——每帧现实毫秒（600 帧工单超时 ≈ 30s；QuickCat LLM 回投窗口 5-60s）</summary>
        private const int FrameSleepMs = 50;

        /// <summary>单轮驱动帧上限——LLM 最坏 60s + 超时结算余量（30000 帧 ≈ 25 分钟空转上限，实际由 Idle 判定提前退出）</summary>
        private const int MaxFramesPerRun = 30000;

        /// <summary>预热帧——CmdPump 懒注册 CommandBus 发生在生成物首 Tick（注册完成前宿主投递会被 SetText REJECT——未注册）；10 帧含安全余量（启动/重载/回滚/自检共用——P7b 缺口2 纪律文档化）</summary>
        private const int WarmupFrames = 10;

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
            for (int i = 0; i < WarmupFrames; i++)
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
    }
}