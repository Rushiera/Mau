using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Mau.Runtime;
using Mau.Providers;

namespace CH4
{
    /// <summary>
    /// Program 多猫管理面分部——CatEntry 注册表 + PortAllocator + cat.* 指令族 + cat.cfg 持久化（P9.3b）。
    /// 两态：静默（持久化未运行）/ 启动（运行 + 端口监听）；删除 = 本地销毁 + 目录删除。
    /// 线程模型：处理函数仅主线程调用（HTTP 线程经 P9.3c PumpCatQueues 入队泵消费）。
    /// </summary>
    public static partial class Program
    {
        /// <summary>数据根——Bootstrap 赋值（P9.3c 静态化条目提前落地一行；cat.cfg/前文路径构造依赖）</summary>
        private static string _dataRoot;

        /// <summary>多猫注册表——cat.* 指令族管理面（默认 majordomo 会话不在此列——主端口对话区专属）</summary>
        private static readonly List<CatEntry> _cats = new List<CatEntry>();

        /// <summary>cat.* 指令队列——HTTP 线程投递 / 主线程泵消费（ThreadGuard：注册表仅主线程触碰）</summary>
        private static readonly ConcurrentQueue<string> _catQueue = new ConcurrentQueue<string>();

        /// <summary>动态端口起始——8081 起（主端口 8080 保留）</summary>
        private const int CatPortStart = 8081;

        /// <summary>
        /// 猫实体——注册表条目（会话 + 外观层 + 持久化态）。
        /// </summary>
        private sealed class CatEntry
        {
            /// <summary>会话唯一 ID——创建时间戳注入（cat.cfg 持久化）</summary>
            public string Id;

            /// <summary>显示名——用户输入（cat.new 参数）</summary>
            public string DisplayName;

            /// <summary>运行态——true=HttpHost 监听中</summary>
            public bool Running;

            /// <summary>监听端口——静默态 0</summary>
            public int Port;

            /// <summary>会话实体——PumpSessions 轮转推进</summary>
            public ChatSession Session;

            /// <summary>HTTP 外观层——启动态非空</summary>
            public HttpHost Host;

            /// <summary>HTTP 线程 Chat 指令入队面——主线程泵消费（P9.3c PumpCatQueues）</summary>
            public ConcurrentQueue<string> PendingChat;

            /// <summary>每猫配置存储——sessions/&lt;id&gt;/config.cfg（P9.4 per-cat 路由；ConfigStoreRegistry 注册）</summary>
            public ConfigStore Config;

            /// <summary>LLM API 配置身份——cat.cfg 持久化；缺省回退内置 DeepSeek（M1b）</summary>
            public Guid ApiConfigId;

            /// <summary>API 配置解析副本——M1c 每猫 Runtime 构造消费</summary>
            public CH_LlmApiConfig ApiConfig;
        }

        /// <summary>
        /// cat.cfg 数据形态——sessions/&lt;id&gt;/cat.cfg（防御式读写；原子写落盘）。
        /// </summary>
        private sealed class CatCfgData
        {
            /// <summary>会话 ID</summary>
            public string Id { get; set; }

            /// <summary>显示名</summary>
            public string DisplayName { get; set; }

            /// <summary>运行态——启动扫描拉起依据</summary>
            public bool Running { get; set; }

            /// <summary>监听端口——静默态 0</summary>
            public int Port { get; set; }

            /// <summary>LLM API 配置身份——缺省空串（回退内置 DeepSeek）</summary>
            public string ApiConfigId { get; set; }
        }

        /// <summary>
        /// cat.* 指令族处理——主线程调用（DispatchCommand 分支 P9.3c 接线）。
        /// </summary>
        /// <param name="line">指令行</param>
        /// <returns>结果文本（CLI 输出 / HTTP 回执）</returns>
        private static string HandleCatCommand(string line)
        {
            if (line.StartsWith("cat.new ", StringComparison.Ordinal))
            {
                string name = line.Substring(8).Trim();
                if (name.Length == 0)
                {
                    return "cat.new | 显示名不能为空";
                }
                return HandleCatNew(name);
            }
            if (line == "cat.list")
            {
                return HandleCatList();
            }
            if (line.StartsWith("cat.start ", StringComparison.Ordinal))
            {
                return HandleCatStart(line.Substring(10).Trim());
            }
            if (line.StartsWith("cat.stop ", StringComparison.Ordinal))
            {
                return HandleCatStop(line.Substring(9).Trim());
            }
            if (line.StartsWith("cat.delete ", StringComparison.Ordinal))
            {
                return HandleCatDelete(line.Substring(11).Trim());
            }
            if (line.StartsWith("cat.chat ", StringComparison.Ordinal))
            {
                // 格式：cat.chat <key> <内容>——内核每猫对话通道（M1d 验收 + 回归复用；DriveUntilIdle 等回复完成）
                string rest = line.Substring(9).Trim();
                int space = rest.IndexOf(' ');
                if (space <= 0)
                {
                    return "cat.chat | 用法: cat.chat <key> <内容>";
                }
                string key = rest.Substring(0, space).Trim();
                string content = rest.Substring(space + 1).Trim();
                if (content.Length == 0)
                {
                    return "cat.chat | 内容不能为空";
                }
                CatEntry cat = FindCat(key);
                if (cat == null)
                {
                    return "cat.chat | 未找到猫: " + key;
                }
                cat.Session.PostUserMessage(content);
                return "cat.chat | 已投递: " + cat.DisplayName;
            }
            return "cat.* 指令未识别: " + line;
        }

        /// <summary>
        /// 泵多猫队列——主线程消费（P9.3c 主循环 + DriveUntilIdle 接入）。
        /// 顺序保证：cat.* 指令泵在前（delete 可能移除注册表条目）→ 再遍历剩余猫的 PendingChat。
        /// </summary>
        private static void PumpCatQueues()
        {
            // [段1] cat.* 指令泵消费（HTTP 线程投递 / 主线程执行）
            string catCmd;
            while (_catQueue.TryDequeue(out catCmd))
            {
                Console.WriteLine("[CH4.Entry] " + HandleCatCommand(catCmd));
            }
            // [段2] 每猫 Chat 指令泵消费（每猫 HttpHost HTTP 线程投递 / 主线程投递会话）
            for (int i = 0; i < _cats.Count; i++)
            {
                CatEntry cat = _cats[i];
                string job;
                while (cat.PendingChat.TryDequeue(out job))
                {
                    cat.Session.PostUserMessage(job);
                }
            }
        }

        /// <summary>
        /// cat.new——创建会话实体 + cat.cfg 持久化（静默态；cat.start 启动）。
        /// </summary>
        /// <param name="name">显示名</param>
        /// <returns>结果文本</returns>
        private static string HandleCatNew(string name)
        {
            string id = DateTime.Now.Ticks.ToString();
            CatEntry cat = CreateCatEntry(id, name);
            if (cat == null)
            {
                return "cat.new | 会话构造失败";
            }
            _cats.Add(cat);
            SaveCatCfg(cat);
            LogStore.Add("CH4.Entry", 1, "cat.new | id=" + id + " | name=" + name + " | 静默态", "CHAT");
            return "cat.new | id=" + id + " | name=" + name + " | 静默态（cat.start 启动）";
        }

        /// <summary>
        /// cat.list——注册表全量列出（id/显示名/状态/端口）。
        /// </summary>
        /// <returns>结果文本</returns>
        private static string HandleCatList()
        {
            if (_cats.Count == 0)
            {
                return "cat.list | 0 只猫";
            }
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            sb.Append("cat.list | " + _cats.Count.ToString() + " 只猫");
            for (int i = 0; i < _cats.Count; i++)
            {
                CatEntry cat = _cats[i];
                string state;
                if (cat.Running)
                {
                    state = "运行中 :" + cat.Port.ToString();
                }
                else
                {
                    state = "静默";
                }
                sb.Append(Environment.NewLine);
                sb.Append("  " + cat.Id + " | " + cat.DisplayName + " | " + state);
            }
            return sb.ToString();
        }

        /// <summary>
        /// cat.start——分配端口 + 拉起 HttpHost + 会话附加（竞态换端口重试一次）。
        /// </summary>
        /// <param name="key">猫寻址键（id 精确/前缀唯一/显示名）</param>
        /// <returns>结果文本</returns>
        private static string HandleCatStart(string key)
        {
            CatEntry cat = FindCat(key);
            if (cat == null)
            {
                return "cat.start | 未找到猫: " + key;
            }
            if (cat.Running)
            {
                return "cat.start | 已在运行: " + cat.DisplayName + " :" + cat.Port.ToString();
            }
            int port = AllocatePort(CatPortStart);
            if (port < 0)
            {
                return "cat.start | 端口分配失败（8081-8180 全占用）";
            }
            HttpHost host;
            try
            {
                host = StartCatHost(cat, port);
            }
            catch (Exception ex)
            {
                // 试绑后释放的竞态窗口——换端口重试一次
                int retryPort = AllocatePort(port + 1);
                if (retryPort < 0)
                {
                    return "cat.start | 端口绑定失败: " + ex.Message;
                }
                host = StartCatHost(cat, retryPort);
                port = retryPort;
            }
            cat.Running = true;
            cat.Port = port;
            cat.Host = host;
            cat.Session.AttachHost(host);
            SaveCatCfg(cat);
            LogStore.Add("CH4.Entry", 1, "cat.start | id=" + cat.Id + " | port=" + port.ToString(), "CHAT");
            return "cat.start | " + cat.DisplayName + " | http://127.0.0.1:" + port.ToString();
        }

        /// <summary>
        /// cat.stop——停 HttpHost + 回静默态 + cfg 更新。
        /// </summary>
        /// <param name="key">猫寻址键</param>
        /// <returns>结果文本</returns>
        private static string HandleCatStop(string key)
        {
            CatEntry cat = FindCat(key);
            if (cat == null)
            {
                return "cat.stop | 未找到猫: " + key;
            }
            if (!cat.Running)
            {
                return "cat.stop | 已是静默态: " + cat.DisplayName;
            }
            try
            {
                cat.Host.Stop();
            }
            catch (Exception ex)
            {
                LogStore.Add("CH4.Entry", 2, "cat.stop | 停止异常 | " + ex.Message, "CHAT");
            }
            cat.Host = null;
            cat.Running = false;
            cat.Port = 0;
            SaveCatCfg(cat);
            LogStore.Add("CH4.Entry", 1, "cat.stop | id=" + cat.Id, "CHAT");
            return "cat.stop | " + cat.DisplayName + " | 已停止";
        }

        /// <summary>
        /// cat.delete——本地销毁（停 + 移除注册表/轮转表 + 删目录与前文文件）。
        /// </summary>
        /// <param name="key">猫寻址键</param>
        /// <returns>结果文本</returns>
        private static string HandleCatDelete(string key)
        {
            CatEntry cat = FindCat(key);
            if (cat == null)
            {
                return "cat.delete | 未找到猫: " + key;
            }
            if (cat.Running)
            {
                try
                {
                    cat.Host.Stop();
                }
                catch (Exception)
                {
                    // 停止异常不阻断删除
                }
            }
            _cats.Remove(cat);
            RemoveSession(cat.Session);
            ConfigStoreRegistry.Unregister(cat.Id);
            DeleteCatFiles(cat.Id);
            LogStore.Add("CH4.Entry", 1, "cat.delete | id=" + cat.Id + " | name=" + cat.DisplayName, "CHAT");
            return "cat.delete | " + cat.DisplayName + " | 已销毁";
        }

        /// <summary>
        /// 建立猫实体——上下文 + 前文恢复/注入 + ChatSession 构造 + 轮转注册（cat.new 与启动扫描共用）。
        /// </summary>
        /// <param name="id">会话 ID</param>
        /// <param name="displayName">显示名</param>
        /// <returns>猫实体；构造失败 null</returns>
        private static CatEntry CreateCatEntry(string id, string displayName)
        {
            try
            {
                // [段1] 每猫 API 引用解析——cat.cfg apiConfigId → API 配置池（缺省回退内置 DeepSeek；M1b/M1c）
                CatCfgData cfgData = LoadCatCfg(Path.Combine(_dataRoot, "Data", "sessions", id, "cat.cfg"));
                Guid apiConfigId = CH_LlmApiConfigStore.DeepSeekApiConfigId;
                if (cfgData != null && cfgData.ApiConfigId != null && cfgData.ApiConfigId.Length > 0)
                {
                    Guid parsed;
                    if (Guid.TryParse(cfgData.ApiConfigId, out parsed) && parsed != Guid.Empty)
                    {
                        apiConfigId = parsed;
                    }
                }
                CH_LlmApiConfigStore apiStore = null;
                DataBox.TryResolve<CH_LlmApiConfigStore>(out apiStore);
                if (apiStore == null)
                {
                    // 防御——Bootstrap 已绑定，实际不触发；空 Store 保证非空
                    apiStore = new CH_LlmApiConfigStore(Path.Combine(_dataRoot, "Data", "config"), Path.Combine(_dataRoot, "Data", "secrets"));
                }
                CH_LlmApiConfig apiConfig = new CH_LlmApiConfig();
                if (!apiStore.TryGet(apiConfigId, out apiConfig))
                {
                    apiConfigId = CH_LlmApiConfigStore.DeepSeekApiConfigId;
                    if (!apiStore.TryGet(apiConfigId, out apiConfig))
                    {
                        apiConfig = new CH_LlmApiConfig();
                    }
                }
                ConfigStore globalConfig = null;
                DataBox.TryResolve<ConfigStore>(out globalConfig);
                // [段2] 上下文 + 前文恢复/注入
                ChatContext context = new ChatContext();
                SessionStore store = new SessionStore(Path.Combine(_dataRoot, "Data", "sessions", id + ".json"));
                LlmMessage[] restored;
                if (store.TryLoad(out restored))
                {
                    context.ReplaceMessages(restored);
                }
                else
                {
                    // 无前文 = 新猫——按 workspace.json inject 清单注入（与默认会话同源）
                    WorkspaceConfig workspace = null;
                    DataBox.TryResolve<WorkspaceConfig>(out workspace);
                    string injectPrompt = BuildInjectPrompt(workspace, BuildToolSpecs());
                    context.SetSystemPrompt(injectPrompt);
                }
                // [段3] 会话构造——M1c 每猫独立 Runtime（API 配置池按该猫 apiConfigId 构造）
                ILlmRuntime catRuntime = new DeepSeekLlmRuntime(apiStore, apiConfigId, globalConfig);
                ChatSession session = new ChatSession(id, displayName, context, store, catRuntime, _oa, _tools, ExecuteTool);
                RegisterSession(session);
                CatEntry cat = new CatEntry();
                cat.Id = id;
                cat.DisplayName = displayName;
                cat.Running = false;
                cat.Port = 0;
                cat.Session = session;
                cat.Host = null;
                cat.PendingChat = new ConcurrentQueue<string>();
                // P9.4 per-cat 配置——独立 config.cfg（不存在空实例；首次写落盘）+ 注册表登记
                cat.Config = ConfigStore.Load(Path.Combine(_dataRoot, "Data", "sessions", id, "config.cfg"));
                ConfigStoreRegistry.Register(id, cat.Config);
                cat.ApiConfigId = apiConfigId;
                cat.ApiConfig = apiConfig;
                LogStore.Add("CH4.Entry", 1, "cat.api | id=" + id + " | api=" + apiConfigId.ToString("D") + " | model=" + apiConfig.DefaultModel, "CHAT");
                return cat;
            }
            catch (Exception ex)
            {
                LogStore.Add("CH4.Entry", 3, "cat.create | 失败 | " + ex.Message, "CHAT");
                return null;
            }
        }

        /// <summary>
        /// 猫寻址——id 精确 / 显示名精确 / id 前缀唯一（歧义返回 null 要求更精确）。
        /// </summary>
        /// <param name="key">寻址键</param>
        /// <returns>猫实体；未找到/歧义 null</returns>
        private static CatEntry FindCat(string key)
        {
            if (key == null || key.Length == 0)
            {
                return null;
            }
            CatEntry exact = null;
            CatEntry prefix = null;
            for (int i = 0; i < _cats.Count; i++)
            {
                CatEntry cat = _cats[i];
                if (cat.Id == key || cat.DisplayName == key)
                {
                    exact = cat;
                    break;
                }
                if (cat.Id.StartsWith(key, StringComparison.Ordinal))
                {
                    if (prefix != null)
                    {
                        return null;
                    }
                    prefix = cat;
                }
            }
            if (exact != null)
            {
                return exact;
            }
            return prefix;
        }

        /// <summary>
        /// 动态端口分配——起始端口起 TCP 试绑（试绑后释放；竞态由 cat.start 重试兜底）。
        /// </summary>
        /// <param name="startPort">起始端口（含）</param>
        /// <returns>可用端口；全占用 -1</returns>
        private static int AllocatePort(int startPort)
        {
            for (int port = startPort; port < startPort + 100; port++)
            {
                if (IsPortTaken(port))
                {
                    continue;
                }
                try
                {
                    TcpListener listener = new TcpListener(IPAddress.Loopback, port);
                    listener.Start();
                    listener.Stop();
                    return port;
                }
                catch (Exception)
                {
                    // 试绑失败——继续下一端口
                }
            }
            return -1;
        }

        /// <summary>
        /// 端口占用检查——注册表运行中猫的端口集合（系统占用由试绑探测）。
        /// </summary>
        /// <param name="port">端口</param>
        /// <returns>true=注册表内已占用</returns>
        private static bool IsPortTaken(int port)
        {
            for (int i = 0; i < _cats.Count; i++)
            {
                if (_cats[i].Running && _cats[i].Port == port)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// 每猫 HttpHost 启动——回调闭包按猫（快照全局 / dispatcher 按猫 / history 按猫 / chat.html 页）。
        /// </summary>
        /// <param name="cat">猫实体</param>
        /// <param name="port">监听端口</param>
        /// <returns>HttpHost 实例</returns>
        private static HttpHost StartCatHost(CatEntry cat, int port)
        {
            return HttpHost.Start(
                port,
                cat.Session.Id,
                BuildSnapshotJson,
                (string line) => DispatchCommandForCat(cat, line),
                null,
                (int max) => BuildHistoryView(cat.Session, max),
                null,
                true);
        }

        /// <summary>
        /// 每猫指令投递——Chat 指令路由本猫会话（主线程直投 / HTTP 线程入队泵）。
        /// </summary>
        /// <param name="cat">猫实体</param>
        /// <param name="line">指令行</param>
        /// <returns>true=识别并投递</returns>
        private static bool DispatchCommandForCat(CatEntry cat, string line)
        {
            if (line.StartsWith("Chat ", StringComparison.Ordinal))
            {
                string content = line.Substring(5).Trim();
                if (content.Length == 0)
                {
                    return false;
                }
                if (Environment.CurrentManagedThreadId == _mainThreadId)
                {
                    cat.Session.PostUserMessage(content);
                }
                else
                {
                    cat.PendingChat.Enqueue(content);
                }
                return true;
            }
            return false;
        }

        /// <summary>
        /// 多猫列表 JSON——GET /api/v1/cats 回调（主端口管理页签数据源；P9.3c 接线）。
        /// </summary>
        /// <returns>列表 JSON</returns>
        private static string BuildCatsJson()
        {
            List<object> list = new List<object>();
            for (int i = 0; i < _cats.Count; i++)
            {
                CatEntry cat = _cats[i];
                list.Add(new
                {
                    id = cat.Id,
                    name = cat.DisplayName,
                    running = cat.Running,
                    port = cat.Port
                });
            }
            var resp = new
            {
                version = 1,
                cats = list
            };
            return JsonSerializer.Serialize(resp);
        }

        /// <summary>
        /// 启动扫描——sessions/*/cat.cfg 全部加载进注册表（静默猫跨进程可见）；running 猫拉起 HttpHost（P9.3c Bootstrap 接线）。
        /// </summary>
        private static void LoadCatsOnBoot()
        {
            string sessionsDir = Path.Combine(_dataRoot, "Data", "sessions");
            if (!Directory.Exists(sessionsDir))
            {
                return;
            }
            string[] dirs;
            try
            {
                dirs = Directory.GetDirectories(sessionsDir);
            }
            catch (Exception)
            {
                return;
            }
            for (int i = 0; i < dirs.Length; i++)
            {
                // 默认猫 majordomo 目录跳过——Bootstrap 专属处理（M1c 默认猫 cat.cfg 读取面），不进多猫注册表
                if (string.Equals(Path.GetFileName(dirs[i]), "majordomo", StringComparison.Ordinal))
                {
                    continue;
                }
                string cfgPath = Path.Combine(dirs[i], "cat.cfg");
                if (!File.Exists(cfgPath))
                {
                    continue;
                }
                CatCfgData cfg = LoadCatCfg(cfgPath);
                if (cfg == null || cfg.Id == null || cfg.Id.Length == 0)
                {
                    continue;
                }
                CatEntry cat = CreateCatEntry(cfg.Id, cfg.DisplayName);
                if (cat == null)
                {
                    LogStore.Add("CH4.Entry", 2, "cat.boot | 会话构造失败 | " + cfg.Id, "CHAT");
                    continue;
                }
                _cats.Add(cat);
                if (!cfg.Running)
                {
                    // 静默态——注册表可见（cat.list/start 可寻址），不拉起
                    LogStore.Add("CH4.Entry", 1, "cat.boot | 静默 | id=" + cat.Id + " | name=" + cat.DisplayName, "CHAT");
                    continue;
                }
                int port = cfg.Port;
                if (port < 1024 || IsPortTaken(port))
                {
                    port = AllocatePort(CatPortStart);
                }
                if (port < 0)
                {
                    LogStore.Add("CH4.Entry", 2, "cat.boot | 端口分配失败 | " + cfg.Id, "CHAT");
                    continue;
                }
                try
                {
                    cat.Host = StartCatHost(cat, port);
                }
                catch (Exception ex)
                {
                    LogStore.Add("CH4.Entry", 2, "cat.boot | 拉起失败 | " + cfg.Id + " | " + ex.Message, "CHAT");
                    continue;
                }
                cat.Running = true;
                cat.Port = port;
                cat.Session.AttachHost(cat.Host);
                SaveCatCfg(cat);
                LogStore.Add("CH4.Entry", 1, "cat.boot | 拉起 | id=" + cat.Id + " | port=" + port.ToString(), "CHAT");
            }
        }

        /// <summary>
        /// cat.cfg 读取——防御式解析（损坏/缺字段回退默认；不存在返回 null）。
        /// </summary>
        /// <param name="path">cfg 路径</param>
        /// <returns>配置数据；损坏 null</returns>
        private static CatCfgData LoadCatCfg(string path)
        {
            try
            {
                string json = File.ReadAllText(path);
                using (JsonDocument doc = JsonDocument.Parse(json))
                {
                    JsonElement root = doc.RootElement;
                    CatCfgData data = new CatCfgData();
                    data.Id = GetStringProp(root, "id");
                    data.DisplayName = GetStringProp(root, "displayName");
                    data.Running = GetBoolProp(root, "running");
                    data.Port = GetIntProp(root, "port");
                    data.ApiConfigId = GetStringProp(root, "apiConfigId");
                    return data;
                }
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// cat.cfg 原子写——ConfigStore.AtomicWrite（临时文件 + 改名；CH2 模式移植）。
        /// </summary>
        /// <param name="cat">猫实体</param>
        private static void SaveCatCfg(CatEntry cat)
        {
            string dir = Path.Combine(_dataRoot, "Data", "sessions", cat.Id);
            string path = Path.Combine(dir, "cat.cfg");
            var data = new
            {
                id = cat.Id,
                displayName = cat.DisplayName,
                running = cat.Running,
                port = cat.Port,
                apiConfigId = cat.ApiConfigId.ToString("D")
            };
            try
            {
                ConfigStore.AtomicWrite(path, JsonSerializer.Serialize(data));
            }
            catch (Exception ex)
            {
                LogStore.Add("CH4.Entry", 2, "cat.cfg | 写入失败 | " + ex.Message, "CHAT");
            }
        }

        /// <summary>
        /// 删除猫文件——sessions/&lt;id&gt;/ 目录 + 前文 json（异常不阻断删除流程）。
        /// </summary>
        /// <param name="id">会话 ID</param>
        private static void DeleteCatFiles(string id)
        {
            try
            {
                string dir = Path.Combine(_dataRoot, "Data", "sessions", id);
                if (Directory.Exists(dir))
                {
                    Directory.Delete(dir, true);
                }
                string storePath = Path.Combine(_dataRoot, "Data", "sessions", id + ".json");
                if (File.Exists(storePath))
                {
                    File.Delete(storePath);
                }
            }
            catch (Exception ex)
            {
                LogStore.Add("CH4.Entry", 2, "cat.delete | 文件清理异常 | " + ex.Message, "CHAT");
            }
        }

        /// <summary>
        /// 读取 JSON 对象布尔属性——防御式（缺字段返回 false）。
        /// </summary>
        /// <param name="obj">JSON 对象</param>
        /// <param name="prop">属性名</param>
        /// <returns>属性值</returns>
        private static bool GetBoolProp(JsonElement obj, string prop)
        {
            JsonElement value;
            if (obj.TryGetProperty(prop, out value) && value.ValueKind == JsonValueKind.True)
            {
                return true;
            }
            return false;
        }

        /// <summary>
        /// 读取 JSON 对象整数属性——防御式（缺字段/非整数返回 0）。
        /// </summary>
        /// <param name="obj">JSON 对象</param>
        /// <param name="prop">属性名</param>
        /// <returns>属性值</returns>
        private static int GetIntProp(JsonElement obj, string prop)
        {
            JsonElement value;
            if (obj.TryGetProperty(prop, out value) && value.ValueKind == JsonValueKind.Number)
            {
                int n;
                if (value.TryGetInt32(out n))
                {
                    return n;
                }
            }
            return 0;
        }
    }
}
