using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Mau.Runtime;
using CatHome4.Http;
using CatHome4.QQ;
using Mau.Providers;
using CH4;

namespace CatHome4.Admin
{
    /// <summary>
    /// Program 多猫管理面分部——CatEntry 注册表 + PortAllocator + cat.* 指令族 + cat.cfg 持久化（P9.3b）。
    /// 两态：静默（持久化未运行）/ 启动（运行 + 端口监听）；删除 = 本地销毁 + 目录删除。
    /// 线程模型：处理函数仅主线程调用（HTTP 线程经 P9.3c PumpCatQueues 入队泵消费）。
    /// </summary>
    internal static partial class AdminService
    {
        /// <summary>数据根——Bootstrap 赋值（P9.3c 静态化条目提前落地一行；cat.cfg/前文路径构造依赖）</summary>
        internal static string _dataRoot;

        /// <summary>多猫注册表——cat.* 指令族管理面（默认 majordomo 会话不在此列——主端口对话区专属）</summary>
        private static readonly List<CatEntry> _cats = new List<CatEntry>();

        /// <summary>cat.* 指令队列——HTTP 线程投递 / 主线程泵消费（ThreadGuard：注册表仅主线程触碰）</summary>
        internal static readonly ConcurrentQueue<string> _catQueue = new ConcurrentQueue<string>();

        /// <summary>majordomo 独立对话端口 HttpHost——serveChatPage=true（F2.2 方案 A：与多猫同构；主端口 8080 保留管理面板）</summary>
        private static HttpHost _majorHost;

        /// <summary>
        /// 全部 HttpHost 主线程泵——主循环帧驱动（majordomo + 每猫；快照构建 ThreadGuard 须宿主主线程）。
        /// Q6 修复（2026-09-08）：每猫/独立端口 HttpHost 的快照缓存从未被泵构建（恒 {}）→ 快照 title 注入不生效 → 前端标题/页头占位符。
        /// </summary>
        internal static void PumpAllHosts()
        {
            if (_majorHost != null)
            {
                _majorHost.PumpMainThread();
            }
            for (int i = 0; i < _cats.Count; i++)
            {
                HttpHost host = _cats[i].Host;
                if (host != null)
                {
                    host.PumpMainThread();
                }
            }
        }

        /// <summary>majordomo 独立对话端口——AllocatePort 分配（F2.2）</summary>
        private static int _majorPort = -1;

        /// <summary>majordomo 独立对话端口（F2.2 对外只读——Program 启动日志用）</summary>
        internal static int MajorPort
        {
            get { return _majorPort; }
        }

        /// <summary>majordomo Chat 指令入队面——HTTP 线程投递 / 主线程泵消费（F2.2 DispatchCommandForMajor）</summary>
        private static readonly ConcurrentQueue<string> _majorPendingChat = new ConcurrentQueue<string>();

        /// <summary>majordomo Note 指令入队面——HTTP 线程投递 / 主线程泵消费（F2.2）</summary>
        private static readonly ConcurrentQueue<string> _majorPendingNote = new ConcurrentQueue<string>();

        /// <summary>majordomo 会话指令入队面——HTTP 线程投递 / 主线程泵消费（P6b session.rollback/fork）</summary>
        private static readonly ConcurrentQueue<string> _majorPendingSessionCmd = new ConcurrentQueue<string>();

        /// <summary>majordomo session.new 请求标志——HTTP 线程置位/主线程泵消费（F2.2 同多猫 M2d）</summary>
        private static bool _majorSessionNewRequested;

        /// <summary>动态端口起始——8081 起（主端口 8080 保留）</summary>
        private const int CatPortStart = 8081;

        /// <summary>
        /// 解析猫启用根条目——cat.cfg enabledRoots → 全局池子集（空=全量；workspace 强制常驻）。
        /// M4e 猫级白名单：猫文件工具面只触及启用根。
        /// </summary>
        /// <param name="catKey">猫 key（majordomo=默认猫）</param>
        /// <returns>启用根条目数组</returns>
        internal static WorkspaceConfig.RootEntry[] ResolveCatRootEntries(string catKey)
        {
            WorkspaceConfig ws = null;
            DataBox.TryResolve<WorkspaceConfig>(out ws);
            if (ws == null || ws.Roots == null || ws.Roots.Length == 0)
            {
                return new WorkspaceConfig.RootEntry[0];
            }
            CatCfgData cfg = LoadCatCfg(Path.Combine(_dataRoot, "Data", "sessions", catKey, "cat.cfg"));
            string[] enabled = null;
            if (cfg != null && cfg.EnabledRoots != null && cfg.EnabledRoots.Length > 0)
            {
                enabled = cfg.EnabledRoots;
            }
            List<WorkspaceConfig.RootEntry> entries = new List<WorkspaceConfig.RootEntry>();
            for (int i = 0; i < ws.Roots.Length; i++)
            {
                WorkspaceConfig.RootEntry entry = ws.Roots[i];
                // workspace/runtime 强制常驻——不因启用列表为空或未勾选而移除
                if (entry.Id == "workspace" || entry.Id == "runtime")
                {
                    entries.Add(entry);
                    continue;
                }
                if (enabled == null)
                {
                    // 未配置 = 全量（行为不倒退）
                    entries.Add(entry);
                    continue;
                }
                bool found = false;
                for (int j = 0; j < enabled.Length; j++)
                {
                    if (string.Equals(enabled[j], entry.Id, StringComparison.OrdinalIgnoreCase))
                    {
                        found = true;
                        break;
                    }
                }
                if (found)
                {
                    entries.Add(entry);
                }
            }
            return entries.ToArray();
        }

        /// <summary>
        /// 猫级文件系统应用——按猫启用根重建 ToolCatContext 缓存（catcfg.apply / 会话构造时调用；主线程）。
        /// </summary>
        /// <param name="catKey">猫 key（majordomo=默认猫）</param>
        internal static void ApplyCatRoots(string catKey)
        {
            WorkspaceConfig.RootEntry[] entries = ResolveCatRootEntries(catKey);
            // §4.3 空值语义可观测——enabledRoots 空=全量（反直觉默认）：解析结果落审计行
            CatCfgData catCfg = LoadCatCfg(Path.Combine(_dataRoot, "Data", "sessions", catKey, "cat.cfg"));
            string rootSource = "全量";
            int rootDeclared = 0;
            if (catCfg != null && catCfg.EnabledRoots != null && catCfg.EnabledRoots.Length > 0)
            {
                rootSource = "子集";
                rootDeclared = catCfg.EnabledRoots.Length;
            }
            List<string> rootIds = new List<string>();
            for (int i = 0; i < entries.Length; i = i + 1)
            {
                rootIds.Add(entries[i].Id);
            }
            LogStore.Add("CatHome4", 1, "cat.roots | cat=" + catKey + " | 来源=" + rootSource + " | 声明=" + rootDeclared.ToString() + " | 实际=" + entries.Length.ToString() + " | [" + string.Join(",", rootIds) + "]", "CHAT");
            if (entries.Length == 0)
            {
                ToolCatContext.UpdateCatFileSystem(catKey, null, "");
                return;
            }
            string recycleRoot = Path.Combine(WorkspaceRecycleRootFor(entries, _dataRoot), "CatTemp", "fs_recycle");
            ToolCatContext.UpdateCatFileSystem(catKey, entries, recycleRoot);
        }

        /// <summary>
        /// 猫级回收站根——启用根内第一个可写根（对齐全局 WorkspaceRecycleRoot 语义；无可写根回退数据根）。
        /// </summary>
        /// <param name="entries">启用根条目</param>
        /// <param name="dataRoot">数据根</param>
        /// <returns>回收站基底目录</returns>
        private static string WorkspaceRecycleRootFor(WorkspaceConfig.RootEntry[] entries, string dataRoot)
        {
            for (int i = 0; i < entries.Length; i++)
            {
                if (entries[i].Writable)
                {
                    return entries[i].Path;
                }
            }
            return dataRoot;
        }
        /// <summary>
        /// cat.* 指令族处理——主线程调用（DispatchCommand 分支 P9.3c 接线）。
        /// </summary>
        /// <param name="line">指令行</param>
        /// <returns>结果文本（CLI 输出 / HTTP 回执）</returns>
        internal static string HandleCatCommand(string line)
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
            if (line.StartsWith("cat.pause", StringComparison.Ordinal))
            {
                // P6 中止——管理面指定猫（每猫端口无 key 版本走 DispatchCommandForCat/Major）
                string rest = line.Substring(9).Trim();
                if (rest.Length == 0)
                {
                    return "cat.pause | 用法: cat.pause <key>";
                }
                CatEntry cat = FindCat(rest);
                if (cat == null)
                {
                    return "cat.pause | 未找到猫: " + rest;
                }
                cat.Session.Pause();
                return "cat.pause | 已投递中止: " + cat.DisplayName;
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
            if (line.StartsWith("session.rollback ", StringComparison.Ordinal) || line.StartsWith("session.fork ", StringComparison.Ordinal))
            {
                // P6b 回滚/分支——管理端口通道（CLI/HTTP 主线程直执；HTTP 线程经 _catQueue 泵）
                return ExecuteSessionCmd(_chatBridge.DefaultSession, line);
            }
            if (line.StartsWith("catcfg.apply ", StringComparison.Ordinal))
            {
                // M3 每猫配置运行时生效——主线程泵消费（HTTP 端点落盘后入队）
                return HandleCatCfgApply(line.Substring(13).Trim());
            }
            return "cat.* 指令未识别: " + line;
        }

        /// <summary>
        /// 泵多猫队列——主线程消费（P9.3c 主循环 + DriveUntilIdle 接入）。
        /// 顺序保证：cat.* 指令泵在前（delete 可能移除注册表条目）→ 再遍历剩余猫的 PendingChat。
        /// </summary>
        internal static void PumpCatQueues()
        {
            // [段1] cat.* 指令泵消费（HTTP 线程投递 / 主线程执行）
            string catCmd;
            while (_catQueue.TryDequeue(out catCmd))
            {
                LogStore.Add("CatHome4", 1, "cat 指令: " + HandleCatCommand(catCmd), "CMD", "", "", 200);
            }
            // [段2] 每猫 Chat 指令泵消费（每猫 HttpHost HTTP 线程投递 / 主线程投递会话）
            for (int i = 0; i < _cats.Count; i++)
            {
                CatEntry cat = _cats[i];
                // M2d session.new 按猫重注入——忙时保留标志下帧重判（同默认猫排队语义）
                if (cat.SessionNewRequested)
                {
                    if (cat.Session.IsIdle)
                    {
                        cat.SessionNewRequested = false;
                        _chatBridge.HandleSessionNew(cat.Session, cat.Persona, cat.InjectList, cat.ToolSpecs, delegate (int n)
                        {
                            if (cat.Host != null)
                            {
                                cat.Host.PushChatDone(n);
                            }
                        });
                    }
                }
                string job;
                while (cat.PendingChat.TryDequeue(out job))
                {
                    cat.Session.PostUserMessage(job);
                }
                // M4c Note 指令泵消费（HTTP 线程投递 / 主线程执行）
                string noteText;
                while (cat.PendingNote.TryDequeue(out noteText))
                {
                    if (noteText == "\u0001start")
                    {
                        cat.Session.NoteStart();
                    }
                    else
                    {
                        cat.Session.NoteAdd(noteText);
                    }
                }
                // P6b 会话指令泵消费（session.rollback/fork——HTTP 线程投递 / 主线程执行）
                string scmd;
                while (cat.PendingSessionCmd.TryDequeue(out scmd))
                {
                    LogStore.Add("CatHome4", 1, "会话指令: " + ExecuteSessionCmd(cat.Session, scmd), "CMD", "", "", 200);
                }
            }
            // [段3] majordomo 独立对话端口指令泵（F2.2——独立端口 serve chat.html；HTTP 线程投递 / 主线程直投 DefaultSession）
            if (_majorSessionNewRequested)
            {
                if (_chatBridge.DefaultSession.IsIdle)
                {
                    _majorSessionNewRequested = false;
                    _chatBridge.HandleSessionNew(_chatBridge.DefaultSession, _chatBridge.DefaultPersona, _chatBridge.DefaultInjectList, _chatBridge.DefaultToolSpecs, delegate (int n)
                    {
                        if (_majorHost != null)
                        {
                            _majorHost.PushChatDone(n);
                        }
                    });
                }
            }
            string majorJob;
            while (_majorPendingChat.TryDequeue(out majorJob))
            {
                _chatBridge.DefaultSession.PostUserMessage(majorJob);
            }
            string majorNote;
            while (_majorPendingNote.TryDequeue(out majorNote))
            {
                if (majorNote == "\u0001start")
                {
                    _chatBridge.DefaultSession.NoteStart();
                }
                else
                {
                    _chatBridge.DefaultSession.NoteAdd(majorNote);
                }
            }
            // P6b majordomo 会话指令泵消费（session.rollback/fork——HTTP 线程投递 / 主线程执行）
            string mcmd;
            while (_majorPendingSessionCmd.TryDequeue(out mcmd))
            {
                LogStore.Add("CatHome4", 1, "会话指令: " + ExecuteSessionCmd(_chatBridge.DefaultSession, mcmd), "CMD", "", "", 200);
            }
        }

        /// <summary>
        /// cat.new——创建会话实体 + cat.cfg 持久化（静默态；cat.start 启动）。
        /// </summary>
        /// <param name="name">显示名</param>
        /// <returns>结果文本</returns>
        private static string HandleCatNew(string name)
        {
            string id = SessionStore.NewSessionId();
            CatEntry cat = CreateCatEntry(id, name, null);
            if (cat == null)
            {
                return "cat.new | 会话构造失败";
            }
            _cats.Add(cat);
            SaveCatCfg(cat);
            LogStore.Add("CatHome4", 1, "已创建猫「" + name + "」（id " + id + "），静默待启动", "CHAT");
            return "cat.new | id=" + id + " | name=" + name + " | 静默态（cat.start 启动）";
        }

        /// <summary>
        /// 会话指令执行——session.rollback/session.fork 统一入口（P6b；仅主线程调用）。
        /// </summary>
        /// <param name="session">目标会话（fork 时 = 源会话）</param>
        /// <param name="line">指令行（session.rollback &lt;msgIndex&gt; / session.fork &lt;name&gt; &lt;msgIndex&gt;）</param>
        /// <returns>结果文本</returns>
        internal static string ExecuteSessionCmd(ChatSession session, string line)
        {
            if (line.StartsWith("session.rollback ", StringComparison.Ordinal))
            {
                string arg = line.Substring(17).Trim();
                int idx;
                if (!int.TryParse(arg, out idx))
                {
                    return "session.rollback | 参数非法: " + arg + "（需要消息索引）";
                }
                return session.Rollback(idx);
            }
            if (line.StartsWith("session.fork ", StringComparison.Ordinal))
            {
                string arg = line.Substring(13).Trim();
                int space = arg.IndexOf(' ');
                if (space <= 0)
                {
                    return "session.fork | 用法: session.fork <显示名> <消息索引>";
                }
                string name = arg.Substring(0, space).Trim();
                int idx;
                if (!int.TryParse(arg.Substring(space + 1).Trim(), out idx))
                {
                    return "session.fork | 消息索引非法: " + arg.Substring(space + 1).Trim();
                }
                return HandleCatFork(session, name, idx);
            }
            return "session.* 指令未识别: " + line;
        }

        /// <summary>
        /// session.fork——从源猫节点分支新建独立 Cat（P6b：继承配置 + 播种节点前前文；静默态待启动）。
        /// 继承：persona/toolNames/injectList/apiConfigId/enabledRoots/qqbotId + 节点前前文；qqbot 转发默认关闭（enable=false——防双猫抢 Bot）；不继承：运行态。
        /// 配置源：多猫 = CatEntry 运行时实体（catcfg.apply 后真相）；majordomo = sessions/majordomo/cat.cfg（source.Id 是 Ticks——判例 fork 配置全空）。
        /// </summary>
        /// <param name="source">源会话（发起 fork 的猫）</param>
        /// <param name="name">新猫显示名</param>
        /// <param name="msgIndex">切点消息索引（assistant 正式回复）</param>
        /// <returns>结果文本</returns>
        private static string HandleCatFork(ChatSession source, string name, int msgIndex)
        {
            if (source == null)
            {
                return "session.fork | 源会话不存在";
            }
            if (!source.IsIdle)
            {
                return "session.fork | 源会话忙——分支仅空闲时执行";
            }
            LlmMessage[] all = source.Context.GetMessages();
            if (msgIndex < 0 || msgIndex >= all.Length)
            {
                return "session.fork | 节点索引越界: " + msgIndex.ToString();
            }
            LlmMessage target = all[msgIndex];
            if (target.Role != LlmRole.Assistant || target.Content == null || target.Content.Length == 0)
            {
                return "session.fork | 节点不是正式回复（仅 assistant 回复可作切点）";
            }
            // [段1] 播种前文——保留 [0..msgIndex]（含源 system——注入内容已在其中）
            LlmMessage[] seed = new LlmMessage[msgIndex + 1];
            for (int i = 0; i <= msgIndex; i = i + 1)
            {
                seed[i] = all[i];
            }
            // [段2] 继承源配置——运行时实体优先（多猫 CatEntry = catcfg.apply 后运行时真相）；majordomo 特判路径（source.Id 是 Ticks 非 "majordomo"——判例：fork 配置全空）
            string id = SessionStore.NewSessionId();
            CatCfgData cfgData = null;
            CatEntry srcCat = FindCat(source.Id);
            if (srcCat != null)
            {
                // 多猫——运行时实体字段（ApiConfigId/Persona/ToolNames/InjectList/QqBotId 继承）
                cfgData = new CatCfgData();
                cfgData.ApiConfigId = srcCat.ApiConfigId == Guid.Empty ? "" : srcCat.ApiConfigId.ToString("D");
                cfgData.Persona = srcCat.Persona;
                cfgData.ToolNames = srcCat.ToolNames;
                cfgData.InjectList = srcCat.InjectList;
                cfgData.QqBotId = srcCat.QqBotId == Guid.Empty ? "" : srcCat.QqBotId.ToString("D");
                cfgData.QqBotEnable = srcCat.QqBotEnable;
                // EnabledRoots CatEntry 不持有——从源磁盘 cfg 补读
                CatCfgData srcDisk = LoadCatCfg(Path.Combine(_dataRoot, "Data", "sessions", srcCat.Id, "cat.cfg"));
                if (srcDisk != null)
                {
                    cfgData.EnabledRoots = srcDisk.EnabledRoots;
                }
            }
            else
            {
                // majordomo 默认猫——cfg 路径固定 sessions/majordomo/cat.cfg（source.Id = Ticks 不可用于寻址）
                cfgData = LoadCatCfg(Path.Combine(_dataRoot, "Data", "sessions", "majordomo", "cat.cfg"));
            }
            if (cfgData == null)
            {
                cfgData = new CatCfgData();
            }
            cfgData.Id = id;
            cfgData.DisplayName = name;
            cfgData.Running = false;
            cfgData.Port = 0;
            // qqbotId 继承保留（防双猫抢 Bot 靠 enable=false——莎拍板：复制配置、默认不开启转发）
            cfgData.QqBotEnable = false;
            SaveCatCfgData(id, cfgData);
            // [段3] 创建 + 播种 + 注册（CreateCatEntry 读新 cfg——克隆配置生效）
            CatEntry cat = CreateCatEntry(id, name, seed);
            if (cat == null)
            {
                return "session.fork | 会话构造失败";
            }
            // [段3b] 播种前文立即落盘——会话文件不存在时重启扫描会走注入分支（播种丢失）；Save 保证重启恢复播种
            cat.Session.Store.Save(cat.Session.Context.GetMessages(), cat.Session.LastStats);
            _cats.Add(cat);
            LogStore.Add("CatHome4", 1, "已从「" + source.DisplayName + "」节点 " + msgIndex.ToString() + " 分支新猫「" + name + "」（id " + id + "），静默待启动", "CHAT");
            return "session.fork | id=" + id + " | name=" + name + " | 已从节点 " + msgIndex.ToString() + " 分支（静默态，cat.start 启动）";
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
            LogStore.Add("CatHome4", 1, "已启动猫「" + cat.DisplayName + "」，端口 " + port.ToString(), "CHAT");
            return "cat.start | " + cat.DisplayName + " | http://127.0.0.1:" + port.ToString();
        }

        /// <summary>
        /// cat.stop——停 HttpHost + 回静默态 + cfg 更新。
        /// </summary>
        /// <param name="key">猫寻址键</param>
        /// <returns>结果文本</returns>
        private static string HandleCatStop(string key)
        {
            // F2.2 majordomo 特殊会话——强制自启无关闭（拒绝）
            if (string.Equals(key, "majordomo", StringComparison.Ordinal))
            {
                return "cat.stop | majordomo 为特殊会话——强制自启，不可停止";
            }
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
                LogStore.Add("CatHome4", 2, "停止猫「" + cat.DisplayName + "」异常：" + ex.Message, "CHAT");
            }
            cat.Host = null;
            cat.Running = false;
            cat.Port = 0;
            SaveCatCfg(cat);
            LogStore.Add("CatHome4", 1, "已停止猫「" + cat.DisplayName + "」", "CHAT");
            return "cat.stop | " + cat.DisplayName + " | 已停止";
        }

        /// <summary>
        /// cat.delete——本地销毁（停 + 移除注册表/轮转表 + 删目录与前文文件）。
        /// </summary>
        /// <param name="key">猫寻址键</param>
        /// <returns>结果文本</returns>
        private static string HandleCatDelete(string key)
        {
            // F2.2 majordomo 特殊会话——不可删除（拒绝）
            if (string.Equals(key, "majordomo", StringComparison.Ordinal))
            {
                return "cat.delete | majordomo 为特殊会话——不可删除";
            }
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
            _chatBridge.RemoveSession(cat.Session);
            // M4e 猫级白名单——缓存随猫销毁
            ToolCatContext.RemoveCatFileSystem(cat.Id);
            DeleteCatFiles(cat.Id);
            LogStore.Add("CatHome4", 1, "已销毁猫「" + cat.DisplayName + "」（id " + cat.Id + "）", "CHAT");
            return "cat.delete | " + cat.DisplayName + " | 已销毁";
        }

        /// <summary>
        /// 建立猫实体——上下文 + 前文恢复/注入 + ChatSession 构造 + 轮转注册（cat.new 与启动扫描共用）。
        /// </summary>
        /// <param name="id">会话 ID</param>
        /// <param name="displayName">显示名</param>
        /// <param name="seedMessages">fork 播种前文（null/空=不播种——正常注入/恢复语义）</param>
        /// <returns>猫实体；构造失败 null</returns>
        private static CatEntry CreateCatEntry(string id, string displayName, LlmMessage[] seedMessages)
        {
            try
            {
                // [段1] 每猫 API 引用解析——cat.cfg apiConfigId → Guid.Empty=默认端点语义（Cat 可选配置；未配置走默认端点）
                CatCfgData cfgData = LoadCatCfg(Path.Combine(_dataRoot, "Data", "sessions", id, "cat.cfg"));
                Guid apiConfigId = Guid.Empty;
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
                if (apiConfigId == Guid.Empty)
                {
                    CH_LlmApiConfig defaultConfig = apiStore.ResolveDefault();
                    if (defaultConfig != null)
                    {
                        apiConfig = defaultConfig;
                    }
                }
                else
                {
                    apiStore.TryGet(apiConfigId, out apiConfig);
                }
                ConfigStore globalConfig = null;
                DataBox.TryResolve<ConfigStore>(out globalConfig);
                // R2.3 每猫 qqbot 配置——qqbotId/qqbotEnable（cfg 缺失=未绑定/禁用）
                Guid qqBotId = Guid.Empty;
                bool qqBotEnable = false;
                if (cfgData != null)
                {
                    if (cfgData.QqBotId != null && cfgData.QqBotId.Length > 0)
                    {
                        Guid parsed;
                        if (Guid.TryParse(cfgData.QqBotId, out parsed) && parsed != Guid.Empty)
                        {
                            qqBotId = parsed;
                        }
                    }
                    qqBotEnable = cfgData.QqBotEnable;
                }
                // [段2] 每猫配置三字段——persona/toolNames/injectList（M2；cfg 缺失=新猫走全局默认模板继承）
                string persona = "";
                string toolNames = "";
                string[] injectList = new string[0];
                if (cfgData != null)
                {
                    if (cfgData.Persona != null)
                    {
                        persona = cfgData.Persona;
                    }
                    if (cfgData.ToolNames != null)
                    {
                        toolNames = cfgData.ToolNames;
                    }
                    if (cfgData.InjectList != null)
                    {
                        injectList = cfgData.InjectList;
                    }
                }
                else
                {
                    // 新猫——cat-default.cfg 模板继承（模板缺失=现状空三字段）
                    CatDefaultCfgData tpl = LoadCatDefaultCfg();
                    if (tpl != null)
                    {
                        persona = tpl.DefaultPersona != null ? tpl.DefaultPersona : "";
                        toolNames = tpl.DefaultToolNames != null ? tpl.DefaultToolNames : "";
                        injectList = tpl.DefaultInjectList != null ? tpl.DefaultInjectList : new string[0];
                    }
                }
                // M2c 声明面裁剪——读时比对（非法名过滤/全空全量保底）
                ToolSpec[] catSpecs = FilterToolSpecs(ResolveToolNames(toolNames));
                // [段3] 上下文 + 前文恢复/注入
                ChatContext context = new ChatContext();
                SessionStore store = new SessionStore(Path.Combine(_dataRoot, "Data", "sessions", id, id + ".json"));
                store.SessionId = id;
                LlmMessage[] restored;
                SessionStats? restoredStats;
                if (store.TryLoad(out restored, out restoredStats))
                {
                    context.ReplaceMessages(restored);
                }
                else if (seedMessages != null && seedMessages.Length > 0)
                {
                    // fork 播种——以源猫节点前前文为起点（含源 system——注入内容已在其中；覆盖注入分支）
                    context.ReplaceMessages(seedMessages);
                }
                else
                {
                    // 无前文 = 新猫——按 cat.cfg injectList 清单注入（M2d：仅读 List 内文件，List 外一律不加载）
                    WorkspaceConfig workspace = null;
                    DataBox.TryResolve<WorkspaceConfig>(out workspace);
                    string injectPrompt = _chatBridge.BuildPrompt(workspace, catSpecs, persona, injectList);
                    context.SetSystemPrompt(injectPrompt);
                }
                // [段4] 会话构造——M1c 每猫独立 Runtime（API 配置池按该猫 apiConfigId 构造）；M2c 声明面按猫裁剪
                ILlmRuntime catRuntime = new DeepSeekLlmRuntime(apiStore, apiConfigId, globalConfig);
                SessionViewStore viewStore = new SessionViewStore(Path.Combine(_dataRoot, "Data", "sessions", id, id + ".view.json"));
                ChatSession session = new ChatSession(id, displayName, context, store, catRuntime, _oa, catSpecs, ExecuteTool, viewStore);
                // M4e 猫级白名单——多猫启用根（cat.cfg enabledRoots；缺省全量）+ 工具执行猫上下文
                session.SetCatKey(id);
                AdminService.ApplyCatRoots(id);
                session.AttachEnvInfo(() => BuildEnvInfoProvider());
                session.AttachRoundNotify(NotifyBalloon);
                session.RebuildView();
                // E3 前文统计——启动恢复持久化真实 usage（旧文件 null=零值）
                session.SetLoadedStats(restoredStats);
                _chatBridge.RegisterSession(session);
                CatEntry cat = new CatEntry();
                cat.Id = id;
                cat.DisplayName = displayName;
                cat.Running = false;
                cat.Port = 0;
                cat.Session = session;
                cat.Host = null;
                cat.PendingChat = new ConcurrentQueue<string>();
                cat.PendingNote = new ConcurrentQueue<string>();
                cat.PendingSessionCmd = new ConcurrentQueue<string>();
                cat.ApiConfigId = apiConfigId;
                cat.ApiConfig = apiConfig;
                cat.Persona = persona;
                cat.ToolNames = toolNames;
                cat.InjectList = injectList;
                cat.ToolSpecs = catSpecs;
                // §4.3 空值语义可观测——toolNames 空=全量保底（反直觉默认）：解析结果落审计行
                string toolSource = "子集";
                int toolDeclared = 0;
                if (toolNames == null || toolNames.Length == 0)
                {
                    toolSource = "全量";
                }
                else
                {
                    toolDeclared = toolNames.Split(',').Length;
                }
                int toolActual = 0;
                if (catSpecs != null)
                {
                    toolActual = catSpecs.Length;
                }
                LogStore.Add("CatHome4", 1, "cat.tools | cat=" + id + " | 来源=" + toolSource + " | 声明=" + toolDeclared.ToString() + " | 实际=" + toolActual.ToString(), "CHAT");
                cat.QqBotId = qqBotId;
                cat.QqBotEnable = qqBotEnable;
                LogStore.Add("CatHome4", 1, "猫「" + displayName + "」绑定 LLM 配置：" + (apiConfigId == Guid.Empty ? "默认端点" : apiConfigId.ToString("D")) + "，模型 " + apiConfig.DefaultModel, "CHAT");
                return cat;
            }
            catch (Exception ex)
            {
                LogStore.Add("CatHome4", 3, "创建猫实体失败：" + ex.Message, "CHAT");
                return null;
            }
        }

        /// <summary>
        /// 收集所有绑定 qqbot 的猫——默认猫（majordomo）+ 多猫（R2.3.5 输出转发轮询面）。
        /// </summary>
        /// <returns>全部绑定目标（QqBotId 非空）</returns>
        internal static List<QqTarget> CollectAllQqTargets()
        {
            List<QqTarget> result = new List<QqTarget>();
            if (_chatBridge.DefaultQqBotId != Guid.Empty)
            {
                ChatSession captured = _chatBridge.DefaultSession;
                result.Add(new QqTarget
                {
                    Key = "majordomo",
                    DisplayName = "majordomo",
                    QqBotId = _chatBridge.DefaultQqBotId,
                    Enable = _chatBridge.DefaultQqBotEnable,
                    Inject = delegate (string s) { captured.PostUserMessage(s); },
                    GetViewItems = delegate () { return ConvertQqViewItems(captured.ViewStore.GetBlocks()); },
                    IsIdle = delegate () { return captured.IsIdle; },
                    NewSession = delegate ()
                    {
                        _majorSessionNewRequested = true;
                        return "新会话已请求（注入执行中）\n" + _chatBridge.BuildInjectSummary(_chatBridge.DefaultPersona, _chatBridge.DefaultInjectList, _chatBridge.DefaultToolSpecs);
                    },
                    GetInfo = delegate () { return captured.GetInfoText(); }
                });
            }
            for (int i = 0; i < _cats.Count; i++)
            {
                CatEntry cat = _cats[i];
                if (cat.QqBotId != Guid.Empty)
                {
                    CatEntry captured = cat;
                    result.Add(new QqTarget
                    {
                        Key = cat.Id,
                        DisplayName = cat.DisplayName,
                        QqBotId = cat.QqBotId,
                        Enable = cat.QqBotEnable,
                        Inject = delegate (string s) { captured.Session.PostUserMessage(s); },
                        GetViewItems = delegate () { return ConvertQqViewItems(captured.Session.ViewStore.GetBlocks()); },
                        IsIdle = delegate () { return captured.Session.IsIdle; },
                        NewSession = delegate ()
                        {
                            captured.SessionNewRequested = true;
                            return "新会话已请求（注入执行中）\n" + _chatBridge.BuildInjectSummary(captured.Persona, captured.InjectList, captured.ToolSpecs);
                        },
                        GetInfo = delegate () { return captured.Session.GetInfoText(); }
                    });
                }
            }
            return result;
        }

        /// <summary>
        /// 收集绑定指定 qqbot 的猫——默认猫（majordomo）+ 多猫（R2.3.4 输入路由广播注入面）。
        /// </summary>
        /// <param name="qqBotId">qqbot 配置身份</param>
        /// <returns>绑定目标列表（空=未绑定任何猫）</returns>
        internal static List<QqTarget> CollectQqTargets(Guid qqBotId)
        {
            List<QqTarget> result = new List<QqTarget>();
            List<QqTarget> all = CollectAllQqTargets();
            for (int i = 0; i < all.Count; i++)
            {
                if (all[i].QqBotId == qqBotId)
                {
                    result.Add(all[i]);
                }
            }
            return result;
        }

        /// <summary>
        /// 视图块 → QQ 转发项转换——窄 DTO（QQ 域只消费 RenderType + Content；text 块提取 payload.content）。
        /// </summary>
        /// <param name="blocks">视图块数组</param>
        /// <returns>QQ 转发项数组</returns>
        private static QqViewItem[] ConvertQqViewItems(ViewBlock[] blocks)
        {
            if (blocks == null)
            {
                return new QqViewItem[0];
            }
            QqViewItem[] items = new QqViewItem[blocks.Length];
            for (int i = 0; i < blocks.Length; i = i + 1)
            {
                ViewBlock b = blocks[i];
                QqViewItem item = new QqViewItem();
                item.RenderType = b.RenderType ?? "";
                item.Content = "";
                if (item.RenderType == "text")
                {
                    item.Content = ExtractTextContent(b.Payload);
                }
                items[i] = item;
            }
            return items;
        }

        /// <summary>
        /// 提取 text 块内容——payload JSON 的 content 字段（防御式解析失败返回空串）。
        /// </summary>
        /// <param name="payloadJson">块载荷 JSON</param>
        /// <returns>内容文本</returns>
        private static string ExtractTextContent(string payloadJson)
        {
            if (payloadJson == null || payloadJson.Length == 0)
            {
                return "";
            }
            try
            {
                using (JsonDocument d = JsonDocument.Parse(payloadJson))
                {
                    if (d.RootElement.TryGetProperty("content", out JsonElement c) && c.ValueKind == JsonValueKind.String)
                    {
                        return c.GetString() ?? "";
                    }
                }
            }
            catch (Exception)
            {
            }
            return "";
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
        /// 猫会话统计——info 自查数据源（M4e 猫级白名单同源寻址；majordomo 走默认会话）。
        /// </summary>
        /// <param name="key">猫键（id/显示名；null=默认会话）</param>
        /// <returns>会话统计；未找到 null</returns>
        internal static SessionStats? GetCatStats(string key)
        {
            if (key == null || key.Length == 0 || key == "majordomo")
            {
                if (_chatBridge != null && _chatBridge.DefaultSession != null)
                {
                    return _chatBridge.DefaultSession.LastStats;
                }
                return null;
            }
            CatEntry cat = FindCat(key);
            if (cat != null && cat.Session != null)
            {
                return cat.Session.LastStats;
            }
            return null;
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
            return HttpHost.Start(new HttpHostOptions
            {
                Port = port,
                SessionId = cat.Session.Id,
                SnapshotBuilder = BuildSnapshotJson,
                Dispatcher = (string line) => DispatchCommandForCat(cat, line),
                FrameBuilder = null,
                HistoryBuilder = (int max) => _chatBridge.BuildHistoryView(cat.Session, max),
                CatsBuilder = null,
                NoteBuilder = () => cat.Session.BuildNoteJson(),
                PatchBuilder = null,
                ServeChatPage = true,
                RouteRegistrar = null,
                HtmlRootProvider = HtmlRoot,
                DisplayName = cat.DisplayName
            });
        }

        /// <summary>
        /// majordomo 独立对话端口启动——F2.2 方案 A（与多猫同构）：独立端口 serve chat.html，绑定 DefaultSession。
        /// 主端口 8080 保留管理面板（index.html）；DefaultSession.AttachHost 改绑本端口（chat 事件推送走 chat.html）。
        /// 强制自启：Bootstrap 调用（段6b 前）；无关闭/删除（cat.stop/cat.delete 拒绝——HandleCatStop/HandleCatDelete）。
        /// </summary>
        /// <returns>是否成功启动（端口分配失败 false）</returns>
        internal static bool StartMajorHost()
        {
            if (_majorHost != null)
            {
                return true;
            }
            int port = AllocatePort(CatPortStart);
            if (port < 0)
            {
                LogStore.Add("CatHome4", 2, "majordomo 独立端口启动失败：8081-8180 全占用", "CHAT");
                return false;
            }
            HttpHost host = HttpHost.Start(new HttpHostOptions
            {
                Port = port,
                SessionId = _chatBridge.DefaultSession.Id,
                SnapshotBuilder = BuildSnapshotJson,
                Dispatcher = (string line) => DispatchCommandForMajor(line),
                FrameBuilder = null,
                HistoryBuilder = (int max) => _chatBridge.BuildHistoryView(_chatBridge.DefaultSession, max),
                CatsBuilder = null,
                NoteBuilder = () => _chatBridge.DefaultSession.BuildNoteJson(),
                PatchBuilder = null,
                ServeChatPage = true,
                RouteRegistrar = null,
                HtmlRootProvider = HtmlRoot,
                DisplayName = _chatBridge.DefaultSession.DisplayName
            });
            _majorHost = host;
            _majorPort = port;
            // 会话事件推送改绑 majordomo 独立对话端口（主端口 index.html 管理面板不再消费 chat 事件——F2.1）
            _chatBridge.DefaultSession.AttachHost(host);
            LogStore.Add("CatHome4", 1, "majordomo 独立对话端口已启动：" + port.ToString(), "CHAT");
            return true;
        }

        /// <summary>
        /// majordomo 独立端口指令投递——Chat/session.new/note.* 路由 DefaultSession（主线程直投 / HTTP 线程入队泵）。
        /// F2.2 与 DispatchCommandForCat 同构——目标固定 DefaultSession；队列走 _majorPendingChat/_majorPendingNote。
        /// </summary>
        /// <param name="line">指令行</param>
        /// <returns>true=识别并投递</returns>
        private static bool DispatchCommandForMajor(string line)
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
                    _chatBridge.DefaultSession.PostUserMessage(content);
                }
                else
                {
                    _majorPendingChat.Enqueue(content);
                }
                return true;
            }
            if (line == "cat.pause")
            {
                // P6 中止——HTTP 线程直接调用（内部仅置位 + 取消令牌——线程安全；相位推进主线程 Pump 消费）
                _chatBridge.DefaultSession.Pause();
                return true;
            }
            if (line == "session.new")
            {
                // F2.2 按 DefaultSession 重注入——HTTP 线程置位/主线程泵消费（PumpCatQueues 段3）
                _majorSessionNewRequested = true;
                return true;
            }
            if (line.StartsWith("session.rollback ", StringComparison.Ordinal) || line.StartsWith("session.fork ", StringComparison.Ordinal))
            {
                // P6b 回滚/分支——主线程直执 / HTTP 线程入队泵消费（ThreadGuard：上下文仅主线程触碰）
                if (Environment.CurrentManagedThreadId == _mainThreadId)
                {
                    LogStore.Add("CatHome4", 1, "会话指令: " + ExecuteSessionCmd(_chatBridge.DefaultSession, line), "CMD", "", "", 200);
                }
                else
                {
                    _majorPendingSessionCmd.Enqueue(line);
                }
                return true;
            }
            if (line == "note.start")
            {
                // M4c Note 启动——主线程直执 / HTTP 线程入队泵
                if (Environment.CurrentManagedThreadId == _mainThreadId)
                {
                    _chatBridge.DefaultSession.NoteStart();
                }
                else
                {
                    _majorPendingNote.Enqueue("\u0001start");
                }
                return true;
            }
            if (line.StartsWith("note.add ", StringComparison.Ordinal))
            {
                // M4c Note 手动新增——主线程直执 / HTTP 线程入队泵
                if (Environment.CurrentManagedThreadId == _mainThreadId)
                {
                    _chatBridge.DefaultSession.NoteAdd(line.Substring(9).Trim());
                }
                else
                {
                    _majorPendingNote.Enqueue(line.Substring(9).Trim());
                }
                return true;
            }
            return false;
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
            if (line == "cat.pause")
            {
                // P6 中止——HTTP 线程直接调用（内部仅置位 + 取消令牌——线程安全；相位推进主线程 Pump 消费）
                cat.Session.Pause();
                return true;
            }
            if (line == "session.new")
            {
                // M2d 按猫重注入——HTTP 线程置位/主线程泵消费（会话忙时排队语义同默认猫）
                cat.SessionNewRequested = true;
                return true;
            }
            if (line.StartsWith("session.rollback ", StringComparison.Ordinal) || line.StartsWith("session.fork ", StringComparison.Ordinal))
            {
                // P6b 回滚/分支——主线程直执 / HTTP 线程入队泵消费（ThreadGuard：上下文仅主线程触碰）
                if (Environment.CurrentManagedThreadId == _mainThreadId)
                {
                    LogStore.Add("CatHome4", 1, "会话指令: " + ExecuteSessionCmd(cat.Session, line), "CMD", "", "", 200);
                }
                else
                {
                    cat.PendingSessionCmd.Enqueue(line);
                }
                return true;
            }
            if (line == "note.start")
            {
                // M4c Note 启动——拼接计划+进度推给 LLM（主线程直执 / HTTP 线程入队泵）
                if (Environment.CurrentManagedThreadId == _mainThreadId)
                {
                    cat.Session.NoteStart();
                }
                else
                {
                    cat.PendingNote.Enqueue("\u0001start");
                }
                return true;
            }
            if (line.StartsWith("note.add ", StringComparison.Ordinal))
            {
                // M4c Note 手动新增——主线程直执 / HTTP 线程入队泵（Note 状态仅主线程触碰）
                if (Environment.CurrentManagedThreadId == _mainThreadId)
                {
                    cat.Session.NoteAdd(line.Substring(9).Trim());
                }
                else
                {
                    cat.PendingNote.Enqueue(line.Substring(9).Trim());
                }
                return true;
            }
            return false;
        }

        /// <summary>
        /// 多猫列表 JSON——GET /api/v1/cats 回调（主端口管理页签数据源；P9.3c 接线）。
        /// F2.2——前置 majordomo 特殊会话条目（special:true + 强制自启 running + 独立对话端口；前端零差异渲染靠 special 标记）。
        /// </summary>
        /// <returns>列表 JSON</returns>
        internal static string BuildCatsJson()
        {
            List<object> list = new List<object>();
            // F2.2 majordomo 特殊会话——置顶 + special 标记（前端不视为多猫；无停止/删除）
            list.Add(new
            {
                id = "majordomo",
                name = "majordomo",
                running = _majorHost != null,
                port = _majorPort,
                special = true
            });
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
            return JsonUtil.Serialize(resp);
        }

        /// <summary>
        /// 启动扫描——sessions/*/cat.cfg 全部加载进注册表（静默猫跨进程可见）；running 猫拉起 HttpHost（P9.3c Bootstrap 接线）。
        /// </summary>
        internal static void LoadCatsOnBoot()
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
                CatEntry cat = CreateCatEntry(cfg.Id, cfg.DisplayName, null);
                if (cat == null)
                {
                    LogStore.Add("CatHome4", 2, "启动扫描：猫 " + cfg.Id + " 会话构造失败", "CHAT");
                    continue;
                }
                _cats.Add(cat);
                if (!cfg.Running)
                {
                    // 静默态——注册表可见（cat.list/start 可寻址），不拉起
                    LogStore.Add("CatHome4", 1, "启动扫描：猫「" + cat.DisplayName + "」为静默态（id " + cat.Id + "）", "CHAT");
                    continue;
                }
                int port = cfg.Port;
                if (port < 1024 || IsPortTaken(port))
                {
                    port = AllocatePort(CatPortStart);
                }
                if (port < 0)
                {
                    LogStore.Add("CatHome4", 2, "启动扫描：猫 " + cfg.Id + " 端口分配失败", "CHAT");
                    continue;
                }
                try
                {
                    cat.Host = StartCatHost(cat, port);
                }
                catch (Exception ex)
                {
                    LogStore.Add("CatHome4", 2, "启动扫描：猫 " + cfg.Id + " 拉起失败：" + ex.Message, "CHAT");
                    continue;
                }
                cat.Running = true;
                cat.Port = port;
                cat.Session.AttachHost(cat.Host);
                SaveCatCfg(cat);
                LogStore.Add("CatHome4", 1, "启动扫描：已拉起猫「" + cat.DisplayName + "」，端口 " + port.ToString(), "CHAT");
            }
        }

    }
}
