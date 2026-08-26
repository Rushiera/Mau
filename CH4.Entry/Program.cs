using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using Mau.Runtime;
using Mau.Providers;
using Mau.Development;

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

        /// <summary>QuickCat Flow 句柄——热重载面</summary>
        private static FlowHandle _quickHandle;

        /// <summary>工具组 Flow 句柄表——按 Flow 名索引（R0.2：TextCat/MauCat/CsCat/ConfigCat 独立 Flow——独立热重载/独立退役）</summary>
        private static readonly Dictionary<string, FlowHandle> _toolFlowHandles = new Dictionary<string, FlowHandle>();

        /// <summary>注册 ID——观测与回收用</summary>
        private static long _quickId;

        /// <summary>工具组 Flow 注册 ID 表——按 Flow 名索引（观测与回收用）</summary>
        private static readonly Dictionary<string, long> _toolFlowIds = new Dictionary<string, long>();

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
            _mainThreadId = Environment.CurrentManagedThreadId;
            try
            {
                Console.OutputEncoding = System.Text.Encoding.UTF8;
            }
            catch (Exception)
            {
                // 输出编码设置失败不影响功能
            }
            try
            {
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
            }
            finally
            {
                // O4 优雅收尾——统一观测四文件 flush 落盘（0 字节判例根因修复：--run 模式直接 return 丢缓冲）
                LogStore.CloseWriters();
                FrameStore.Close();
            }
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
            // 文件系统服务——file.* 积木依赖（受控根 = workspace.json 配置 roots；回收站=可写根 CatTemp/fs_recycle——P8.5 配置群）
            string dataRoot = ResolveDataRoot();
            _dataRoot = dataRoot;
            WorkspaceConfig workspace = WorkspaceConfig.Load(Path.Combine(dataRoot, "Data", "config", "workspace.json"), dataRoot);
            DataBox.Bind<WorkspaceConfig>(workspace);
            DataBox.Bind<FileSystemService>(new FileSystemService(workspace.Roots, Path.Combine(WorkspaceRecycleRoot(workspace, dataRoot), "CatTemp", "fs_recycle")));
            string configDir = Path.Combine(dataRoot, "Data", "config");
            ConfigStore llmConfig = ConfigStore.Load(Path.Combine(configDir, "llm.cfg"));
            DataBox.Bind<ConfigStore>(llmConfig);
            // P9.4 配置注册表——默认全局实例（每猫实例在 cat.new/启动扫描时注册）
            ConfigStoreRegistry.SetDefault(llmConfig);
            // M1a LLM API 配置池——llm-api.json 明文（零 key）+ Data/secrets 独立秘密文件（CH3 同构迁移；环境变量首次导入兜底）
            CH_LlmApiConfigStore apiConfigStore = new CH_LlmApiConfigStore(
                Path.Combine(dataRoot, "Data", "config"),
                Path.Combine(dataRoot, "Data", "secrets"));
            DataBox.Bind<CH_LlmApiConfigStore>(apiConfigStore);
            // M3 管理面静态引用——catcfg.apply 重建 Runtime 消费
            _apiStore = apiConfigStore;
            _globalConfig = llmConfig;
            // P8.5d 配置群多文件化——ui.* 用户偏好追加到同一 store（键前缀段路由；ui.json 缺失时首次写入自动创建）
            llmConfig.AddFile("ui", Path.Combine(configDir, "ui.json"));
            // R2.1 搜索配置群——search.* 独立 search.cfg（未配置=搜索工具不可用——先配置后才可用）
            llmConfig.AddFile("search", Path.Combine(configDir, "search.cfg"));
            // M1 语料面 Runtime——默认端点语义（QuickCat 工单 llm.stream 消费面；Guid.Empty=每次调用实时解析默认配置）
            // 空配置池无默认端点 → 启动失败（不做静默回退——必须先配置端点后才能运行）
            _llmRuntime = new DeepSeekLlmRuntime(apiConfigStore, Guid.Empty, llmConfig);
            DataBox.Bind<ILlmRuntime>(_llmRuntime);
            // P8 三期——Roslyn cs.* 编码工具域（MauRoslynBridge——受控根=可写根子集，只读知识根不参与项目扫描；磁盘权威快照 + 三态缓存）
            DataBox.Bind<ICSharpBridge>(new MauRoslynBridge(WritableRootPaths(workspace)));
            // R2.1 联网搜索服务——DeepSeek /responses + web_search（search.api_config_id 引用 LLM 池配置；未配置=不可用）
            DataBox.Bind<IWebSearchService>(new Mau.Providers.DeepSeekWebSearchService(apiConfigStore, llmConfig));
            // P8.5 配置群 schema——schema.json 元声明（默认值/敏感/可写——/api/v1/config 输出面）
            DataBox.Bind<ConfigSchema>(ConfigSchema.Load(Path.Combine(configDir, "schema.json")));
            AuditStore audit = new AuditStore();
            AuditStore.Default = audit;
            _runner.Audit = audit;
            // [段3] 统一观测运行目录——O 系列：Data/runs/<ts>/ 四文件（log.all/oa.all/frame.jsonl/err.all——design-ch4-observe §三）
            string sessionRunId = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string runDir = Path.Combine(dataRoot, "Data", "runs", sessionRunId);
            LogStore.ConfigureRuns(runDir);
            FrameStore.Configure(Path.Combine(runDir, "frame.jsonl"));
            // [段3a] Console 订阅——Log 同构行输出窗口（O4：过程行不再直打——统一观测）
            LogStore.ConsoleSink = delegate (string line)
            {
                Console.WriteLine(line);
            };
            // [段4] 语料加载——QuickCat（CLI 问答消费者）+ 4 工具组 Flow（R0.2：TextCat/MauCat/CsCat/ConfigCat——独立注册/独立热重载/独立退役）
            // 工具组容错降级：加载失败 → 警告 + 跳过注册（该组工具工单无人认领 → 宿主 FALLBACK 直执保底——负例路径合法化）
            _quickHandle = FlowHandle.Load(Path.Combine(dllDir, "FL_QuickCat.dll"));
            _quickId = _runner.RegisterFlow(_quickHandle.Flow, "QuickCat");
            LoadToolGroup("TextCat", dllDir);
            LoadToolGroup("MauCat", dllDir);
            LoadToolGroup("CsCat", dllDir);
            LoadToolGroup("ConfigCat", dllDir);
            LoadToolGroup("SearchCat", dllDir);
            // [段5] 会话面——上下文 + 前文恢复 + 工具定义 + 默认会话注册（P9.1 会话对象化：ChatSession 承载状态机——design-llm-streaming §六）
            ChatContext chatCtx = new ChatContext();
            SessionStore chatStore = new SessionStore(Path.Combine(dataRoot, "Data", "sessions", "majordomo.json"));
            LlmMessage[] restored;
            // M1 默认猫——API 配置身份从 sessions/majordomo/cat.cfg 读取；缺省 Guid.Empty=默认端点语义（每次调用实时解析）
            // M2 默认猫同构——persona/toolNames/injectList 三字段同迁（每猫配置完全独立；注入源从 workspace.inject 切到 cat.cfg）
            Guid defaultApiConfigId = Guid.Empty;
            CatCfgData defaultCfg = LoadCatCfg(Path.Combine(dataRoot, "Data", "sessions", "majordomo", "cat.cfg"));
            _defaultPersona = "";
            _defaultInjectList = new string[0];
            string defaultToolNames = "";
            if (defaultCfg != null)
            {
                if (defaultCfg.ApiConfigId != null && defaultCfg.ApiConfigId.Length > 0)
                {
                    Guid parsed;
                    if (Guid.TryParse(defaultCfg.ApiConfigId, out parsed) && parsed != Guid.Empty)
                    {
                        defaultApiConfigId = parsed;
                    }
                }
                if (defaultCfg.Persona != null)
                {
                    _defaultPersona = defaultCfg.Persona;
                }
                if (defaultCfg.InjectList != null)
                {
                    _defaultInjectList = defaultCfg.InjectList;
                }
                if (defaultCfg.ToolNames != null)
                {
                    defaultToolNames = defaultCfg.ToolNames;
                }
            }
            // R0.2 工具注册表——静态表灌入（声明单一真相源 = 注册表；须在声明面裁剪前 Init——FilterToolSpecs 消费注册表）
            ToolRegistry.Init(BuildToolSpecs());
            // M2c 声明面裁剪——读时比对（非法名过滤/全空全量保底）；session.new 重注入复用
            _defaultToolSpecs = FilterToolSpecs(ResolveToolNames(defaultToolNames));
            _defaultApiConfigId = defaultApiConfigId;
            if (chatStore.TryLoad(out restored))
            {
                chatCtx.ReplaceMessages(restored);
                Console.WriteLine("[CH4.Entry] 会话前文恢复: " + restored.Length + " 条消息（有前文——不注入）");
            }
            else
            {
                // 无前文 = 隐式新会话——按 cat.cfg injectList 清单注入（M2d：不再走全局 workspace.inject）
                string injectPrompt = BuildInjectPrompt(workspace, _defaultToolSpecs, _defaultPersona, _defaultInjectList);
                chatCtx.SetSystemPrompt(injectPrompt);
                Console.WriteLine("[CH4.Entry] 新会话注入: " + _defaultInjectList.Length.ToString() + " 个文件");
            }
            _tools = ToolRegistry.BuildSpecs();
            // P9.1 会话对象化——默认会话注册（工具表就位后构造——ChatSession 状态机承载面；M2c 声明面按猫裁剪）
            _defaultSession = new ChatSession(DateTime.Now.Ticks.ToString(), "majordomo", chatCtx, chatStore,
                new DeepSeekLlmRuntime(apiConfigStore, defaultApiConfigId, llmConfig), _oa, _defaultToolSpecs, ExecuteTool);
            RegisterSession(_defaultSession);
            // LLM 注入探测——默认端点解析（无默认端点 = 未注入；启动失败语义由语料面消费时暴露）
            CH_LlmApiConfig llmProbeConfig = apiConfigStore.ResolveDefault();
            string llmKeyProbe = "";
            if (llmProbeConfig != null)
            {
                llmKeyProbe = apiConfigStore.GetSecret(llmProbeConfig.ApiConfigId);
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
            // P8.5 观测落点——workspace.load 审计 + 全局盒 roots 摘要（design §七：当前受控根永远可从观测层看到）
            System.Text.StringBuilder rootSummary = new System.Text.StringBuilder();
            for (int i = 0; i < workspace.Roots.Length; i++)
            {
                if (i > 0)
                {
                    rootSummary.Append("|");
                }
                rootSummary.Append(workspace.Roots[i].Id);
                if (workspace.Roots[i].Writable)
                {
                    rootSummary.Append("(rw)");
                }
                else
                {
                    rootSummary.Append("(ro)");
                }
            }
            DataBox.Set<string>("global", "workspace.roots", rootSummary.ToString());
            LogStore.Add("CH4.Entry", 1, "workspace.load | roots=" + workspace.Roots.Length.ToString() + " | inject=" + workspace.Inject.Length.ToString() + " | " + rootSummary.ToString(), "CONFIG");
            Console.WriteLine("[CH4.Entry] 就绪 | Flows: QuickCat#" + _quickId + " " + ToolGroupSummary() + " | LLM: " + llmState + " | 帧节流 " + FrameSleepMs + "ms");
            // [段6] HTTP 外观层启动——P6 最小闭环（协议 design-ch4-protocol.md；快照回调 + 指令投递回调注入）
            // P9.3 多实例化签名——sessionId 归属默认会话；catsBuilder 多猫列表（管理页签数据源）；主端口服务 index.html
            _httpHost = HttpHost.Start(ResolveHttpPort(llmConfig), _defaultSession.Id, BuildSnapshotJson, DispatchCommand, BuildCompactFrameJson, (int max) => BuildHistoryView(_defaultSession, max), BuildCatsJson, () => _defaultSession.BuildNoteJson(), BuildPatchJson, false);
            _defaultSession.AttachHost(_httpHost);
            // [段6b] 启动扫描——sessions/*/cat.cfg 中 running 猫拉起（主 HTTP 就位后——每猫 HttpHost 独立实例）
            LoadCatsOnBoot();
            Console.WriteLine("[CH4.Entry] HTTP 外观层就绪: http://127.0.0.1:" + _httpHost.Port);
        }
        /// <summary>
        /// 环境信息——运行版本 + LLM 端点类型 + 当前时间（info 内置工具数据源；R0.2 拍板：不显示工具组清单——工具注册是前文初始化一次性）
        /// </summary>
        /// <returns>环境信息文本</returns>
        internal static string BuildEnvInfo()
        {
            string version = "?";
            try
            {
                System.Reflection.Assembly asm = System.Reflection.Assembly.GetEntryAssembly();
                if (asm != null)
                {
                    System.Reflection.AssemblyName an = asm.GetName();
                    if (an.Version != null)
                    {
                        version = an.Version.ToString(3);
                    }
                }
            }
            catch (Exception)
            {
                version = "?";
            }
            string llmInfo = "未配置";
            try
            {
                CH_LlmApiConfig cfg = _apiStore.ResolveDefault();
                if (cfg != null)
                {
                    string endpoint = cfg.Endpoint;
                    if (endpoint.Length > 0)
                    {
                        // 端点摘要——协议 + 主机（去路径尾斜杠；端点非私密——key 在 secrets 面）
                        endpoint = endpoint.Replace("https://", "").Replace("http://", "");
                        int slash = endpoint.IndexOf('/');
                        if (slash > 0)
                        {
                            endpoint = endpoint.Substring(0, slash);
                        }
                    }
                    llmInfo = cfg.ApiType + " | " + endpoint + " | model=" + cfg.DefaultModel;
                }
            }
            catch (Exception)
            {
                llmInfo = "读取失败";
            }
            string now = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            return "CH4 v" + version + " | LLM: " + llmInfo + " | " + now;
        }
        /// <summary>
        /// 工具组 Flow 摘要——就绪打印用（"TextCat#2 MauCat#3 ..."；未加载组省略）
        /// </summary>
        /// <returns>摘要文本</returns>
        private static string ToolGroupSummary()
        {
            string text = "";
            foreach (KeyValuePair<string, long> kv in _toolFlowIds)
            {
                if (text.Length > 0)
                {
                    text = text + " ";
                }
                text = text + kv.Key + "#" + kv.Value;
            }
            return text;
        }
        /// <summary>
        /// 加载工具组 Flow——Load dll + RegisterFlow + 存句柄表（R0.2 工具组独立注册/独立热重载/独立退役；失败警告 + 跳过——FALLBACK 保底）
        /// </summary>
        /// <param name="flowName">工具组 Flow 名（TextCat/MauCat/CsCat/ConfigCat——dll = FL_&lt;名&gt;.dll）</param>
        /// <param name="dllDir">语料 dll 目录</param>
        private static void LoadToolGroup(string flowName, string dllDir)
        {
            try
            {
                FlowHandle handle = FlowHandle.Load(Path.Combine(dllDir, "FL_" + flowName + ".dll"));
                long id = _runner.RegisterFlow(handle.Flow, flowName);
                _toolFlowHandles[flowName] = handle;
                _toolFlowIds[flowName] = id;
            }
            catch (Exception ex)
            {
                Console.WriteLine("[CH4.Entry] 警告: FL_" + flowName + ".dll 加载失败——该组工具降级 FALLBACK 直执: " + ex.Message);
            }
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