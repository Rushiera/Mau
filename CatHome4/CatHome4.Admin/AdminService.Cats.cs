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
using CatHome4.Contracts;
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
        /// <summary>
        /// info 系返回体序列化选项——缩进 + 中文直显（LLM 可读优先）。
        /// 消费面：info 内置工具（Program.BuildEnvInfo）与 cat.info（本类 HandleCatInfo）——单一定义两处共用。
        /// </summary>
        internal static readonly JsonSerializerOptions InfoJsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        /// <summary>多猫注册表——cat.* 指令族管理面（默认 majordomo 会话不在此列——主端口对话区专属）</summary>
        private static readonly List<CatEntry> _cats = new List<CatEntry>();

        /// <summary>cat.* 指令队列——HTTP 线程投递 / 主线程泵消费（ThreadGuard：注册表仅主线程触碰）</summary>
        internal static readonly ConcurrentQueue<string> _catQueue = new ConcurrentQueue<string>();

        /// <summary>majordomo 独立对话端口 HttpHost——serveChatPage=true（F2.2 方案 A：与多猫同构；主端口保留管理面板）</summary>
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

        /// <summary>
        /// 解析猫启用根条目——cat.cfg enabledRoots → 全局池子集（空/缺失=空白名单——仅常驻 workspace 可用；失败默认收紧，不回落全量）。
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
                // workspace 强制常驻——不因启用列表为空或未勾选而移除（id 大小写不敏感：workspace.json 记作 WorkSpace）
                if (string.Equals(entry.Id, "workspace", StringComparison.OrdinalIgnoreCase) || string.Equals(entry.Id, "runtime", StringComparison.OrdinalIgnoreCase))
                {
                    entries.Add(entry);
                    continue;
                }
                // 未配置 / 解析失败 = 空白名单——失败默认收紧（只剩常驻根），不回落全量
                if (enabled == null)
                {
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
            // §4.3 空值语义可观测——enabledRoots 空=空白名单（仅常驻根；失败默认收紧）：解析结果落审计行
            CatCfgData catCfg = LoadCatCfg(Path.Combine(_dataRoot, "Data", "sessions", catKey, "cat.cfg"));
            string rootSource = "空白名单";
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
            if (line == "cat.new" || line.StartsWith("cat.new ", StringComparison.Ordinal))
            {
                // 空名 = 自动命名（默认小猫 / 默认小猫(1)…）——新建不再强制输入显示名
                string name = "";
                if (line.Length > 8)
                {
                    name = line.Substring(8).Trim();
                }
                return HandleCatNew(name);
            }
            if (line == "cat.list")
            {
                return HandleCatList();
            }
            if (line == "cat.info")
            {
                return HandleCatInfo();
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
                if (string.Equals(rest, "majordomo", StringComparison.Ordinal))
                {
                    return "cat.pause | majordomo 为特殊会话——不可自查（主干会话不能对自己下中止指令）";
                }
                CatEntry cat = FindCat(rest);
                if (cat == null)
                {
                    return "cat.pause | 未找到猫: " + rest;
                }
                cat.Session.Pause();
                return "cat.pause | 已投递中止: " + cat.DisplayName;
            }
            if (line.StartsWith("cat.continue", StringComparison.Ordinal))
            {
                // 继续——管理面指定猫（每猫端口无 key 版本走 DispatchCommandForCat）
                string rest = line.Substring(12).Trim();
                if (rest.Length == 0)
                {
                    return "cat.continue | 用法: cat.continue <key>";
                }
                if (string.Equals(rest, "majordomo", StringComparison.Ordinal))
                {
                    return "cat.continue | majordomo 为特殊会话——不可自查（主干会话不能对自己下继续指令）";
                }
                CatEntry cat = FindCat(rest);
                if (cat == null)
                {
                    return "cat.continue | 未找到猫: " + rest;
                }
                cat.Session.Continue();
                return "cat.continue | 已投递继续: " + cat.DisplayName;
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
                if (string.Equals(key, "majordomo", StringComparison.Ordinal))
                {
                    return "cat.chat | majordomo 为特殊会话——不可自查（主干会话不能对自己投递对话）";
                }
                CatEntry cat = FindCat(key);
                if (cat == null)
                {
                    return "cat.chat | 未找到猫: " + key;
                }
                cat.Session.PostUserMessage(content);
                return "cat.chat | 已投递: " + cat.DisplayName;
            }
            if (line.StartsWith("cat.new-session", StringComparison.Ordinal))
            {
                // 猫详情弹层预设 /new——按猫请求新会话（等价该猫前端 session.new；HTTP 线程置位/主线程泵消费）
                string rest = line.Substring(15).Trim();
                if (rest.Length == 0)
                {
                    return "cat.new-session | 用法: cat.new-session <key>";
                }
                if (string.Equals(rest, "majordomo", StringComparison.Ordinal))
                {
                    _majorSessionNewRequested = true;
                    return "cat.new-session | 已请求新会话: majordomo";
                }
                CatEntry cat = FindCat(rest);
                if (cat == null)
                {
                    return "cat.new-session | 未找到猫: " + rest;
                }
                cat.SessionNewRequested = true;
                return "cat.new-session | 已请求新会话: " + cat.DisplayName;
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
            // A62 每猫配置读写指令——工具面 config-cat-get / config-cat-set 的调度底座（合并写语义）
            if (line.StartsWith("cat.cfg.get ", StringComparison.Ordinal))
            {
                return HandleCatCfgGet(line.Substring(12).Trim());
            }
            if (line.StartsWith("cat.cfg.set ", StringComparison.Ordinal))
            {
                return HandleCatCfgSet(line.Substring(12).Trim());
            }
            return "cat.* 指令未识别: " + line + "\n" + CatCommandUsage();
        }
        /// <summary>
        /// cat.* 指令族用法清单——错 key 自解释（入口面 = 用法声明面；design-ch4-cat-admin §五）。
        /// </summary>
        /// <returns>指令清单文本</returns>
        internal static string CatCommandUsage()
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            sb.AppendLine("cat.* / catcfg.* 可用指令:");
            sb.AppendLine("  cat.list | 全部猫（id/显示名/运行态:端口）");
            sb.AppendLine("  cat.info | 全猫状态统计（运行态/端口/流式态/前文长度/最近活跃——整块 JSON）");
            sb.AppendLine("  cat.new <显示名> | 新建猫（静默态，待 cat.start）");
            sb.AppendLine("  cat.start <key> | 分配端口 + 拉起 HttpHost");
            sb.AppendLine("  cat.stop <key> | 停止（majordomo 禁停）");
            sb.AppendLine("  cat.delete <key> | 销毁（majordomo 禁删）");
            sb.AppendLine("  cat.pause <key> | 中止当前回合");
            sb.AppendLine("  cat.continue <key> | 继续（当前前文再发一次请求）");
            sb.AppendLine("  cat.chat <key> <内容> | 内核对话投递");
            sb.AppendLine("  cat.new-session <key> | 按猫请求新会话（清前文 + 重新注入）");
            sb.AppendLine("  cat.cfg.get <key> | 该猫 cat.cfg 全量字段");
            sb.AppendLine("  cat.cfg.set <key> <字段> <值> | 字段级合并写（未提交字段保留）");
            sb.Append("  catcfg.apply <key> | 每猫配置运行时生效");
            return sb.ToString();
        }
        /// <summary>
        /// 猫寻址 → cat.cfg 路径解析（majordomo=默认猫固定路径；多猫经 FindCat 寻址——id 精确 / 显示名 / id 前缀唯一）。
        /// </summary>
        /// <param name="key">猫寻址键</param>
        /// <param name="id">输出：猫 id（cat.cfg 目录名）</param>
        /// <param name="path">输出：cat.cfg 绝对路径</param>
        /// <returns>错误文本（空=通过）</returns>
        private static string ResolveCatCfgTarget(string key, out string id, out string path)
        {
            id = "";
            path = "";
            if (key == null || key.Length == 0)
            {
                return "缺少猫寻址键";
            }
            if (string.Equals(key, "majordomo", StringComparison.Ordinal))
            {
                id = "majordomo";
            }
            else
            {
                CatEntry cat = FindCat(key);
                if (cat == null)
                {
                    return "未找到猫: " + key + "（cat.list 查看全部）";
                }
                id = cat.Id;
            }
            path = Path.Combine(_dataRoot, "Data", "sessions", id, "cat.cfg");
            return "";
        }
        /// <summary>
        /// 字符串数组 → 逗号清单（空数组空串）。
        /// </summary>
        /// <param name="values">数组</param>
        /// <returns>逗号清单</returns>
        private static string JoinArray(string[] values)
        {
            if (values == null || values.Length == 0)
            {
                return "";
            }
            return string.Join(",", values);
        }
        /// <summary>
        /// cat.cfg 字段清单格式化——get / set 共用（写后读回对照用同一口径）。
        /// </summary>
        /// <param name="id">猫 id</param>
        /// <param name="cfg">配置数据</param>
        /// <returns>清单文本</returns>
        private static string FormatCatCfg(string id, CatCfgData cfg)
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            sb.AppendLine("cat.cfg | " + id);
            sb.AppendLine("  id=" + cfg.Id);
            sb.AppendLine("  displayName=" + cfg.DisplayName);
            sb.AppendLine("  apiConfigId=" + cfg.ApiConfigId);
            sb.AppendLine("  persona=" + cfg.Persona);
            sb.AppendLine("  toolNames=" + cfg.ToolNames);
            sb.AppendLine("  injectList=" + JoinArray(cfg.InjectList));
            sb.AppendLine("  packs=" + JoinArray(cfg.Packs));
            sb.AppendLine("  qqbotId=" + cfg.QqBotId);
            sb.AppendLine("  qqbotEnable=" + (cfg.QqBotEnable ? "true" : "false"));
            sb.Append("  enabledRoots=" + JoinArray(cfg.EnabledRoots));
            return sb.ToString();
        }
        /// <summary>
        /// cat.cfg.get——该猫 cat.cfg 全量字段（不设掩码：cat.cfg 不含密钥字段——密钥在 Data/secrets；design-ch4-cat-admin §三）。
        /// </summary>
        /// <param name="key">猫寻址键（majordomo=默认猫；多猫=会话 id / 显示名 / id 前缀唯一）</param>
        /// <returns>字段清单文本</returns>
        internal static string HandleCatCfgGet(string key)
        {
            string id;
            string path;
            string error = ResolveCatCfgTarget(key, out id, out path);
            if (error.Length > 0)
            {
                return "cat.cfg.get | " + error;
            }
            CatCfgData cfg = LoadCatCfg(path);
            if (cfg == null)
            {
                return "cat.cfg.get | cat.cfg 不存在: " + id;
            }
            return FormatCatCfg(id, cfg);
        }
        /// <summary>
        /// 逗号 / 空白分隔清单解析——去空项（packs / enabledRoots 字段值用）。
        /// </summary>
        /// <param name="value">原始串（如 "overwork,ccbp-core"）</param>
        /// <returns>去空项数组</returns>
        private static string[] SplitList(string value)
        {
            if (value == null || value.Trim().Length == 0)
            {
                return new string[0];
            }
            string[] parts = value.Split(new char[] { ',', ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            List<string> list = new List<string>();
            for (int i = 0; i < parts.Length; i = i + 1)
            {
                string item = parts[i].Trim();
                if (item.Length > 0)
                {
                    list.Add(item);
                }
            }
            return list.ToArray();
        }
        /// <summary>
        /// API 配置身份归一——空 / 全零 = 默认端点语义（Guid.Empty 串，读侧实时解析全局默认）；否则须合法 Guid。
        /// </summary>
        /// <param name="value">原始值</param>
        /// <param name="error">输出：错误文本（空=通过）</param>
        /// <returns>归一后的身份串</returns>
        private static string NormalizeApiConfigId(string value, out string error)
        {
            error = "";
            string trimmed = (value == null) ? "" : value.Trim();
            if (trimmed.Length == 0 || trimmed == Guid.Empty.ToString("D"))
            {
                return Guid.Empty.ToString("D");
            }
            Guid parsed;
            if (!Guid.TryParse(trimmed, out parsed) || parsed == Guid.Empty)
            {
                error = "apiConfigId 非法: " + trimmed + "（空=默认端点）";
                return "";
            }
            return trimmed;
        }
        /// <summary>
        /// 注入清单解析——逗号分隔 + 路径合法性过滤（只接受完整路径：绝对路径 / id: 命名空间；非法项剔除并记 L2）。
        /// </summary>
        /// <param name="value">原始串（如 "CCBP:L1,CCBP:SOUL.md"）</param>
        /// <returns>合法路径数组</returns>
        private static string[] ParseInjectList(string value)
        {
            string[] parts = SplitList(value);
            List<string> list = new List<string>();
            for (int i = 0; i < parts.Length; i = i + 1)
            {
                if (IsValidInjectPath(parts[i]))
                {
                    list.Add(parts[i]);
                }
                else
                {
                    LogStore.Add("CatHome4", 2, "猫配置：注入路径被拒绝（非法格式）" + parts[i], "CONFIG");
                }
            }
            return list.ToArray();
        }
        /// <summary>
        /// cat.cfg.set——字段级合并写（读现值 → 改单字段 → 全量写回；未提交字段逐字保留）。
        /// 🔴 不复用 POST /api/v1/cat-config 的整对象替换语义（判例 2026-09-16：部分字段提交致 qqbotEnable 被关、enabledRoots 收窄）。
        /// 返回落盘实况（写后读回全量对照）；生效走 catcfg.apply 链（apiConfigId 立即，其余前文项新会话生效）。
        /// </summary>
        /// <param name="rest">参数串：&lt;key&gt; &lt;字段&gt; &lt;值…&gt;（值取行内剩余全部原文——persona 多行原样直达）</param>
        /// <returns>落盘实况文本</returns>
        internal static string HandleCatCfgSet(string rest)
        {
            if (rest == null || rest.Length == 0)
            {
                return "cat.cfg.set | 用法: cat.cfg.set <key> <字段> <值>";
            }
            int sp1 = rest.IndexOf(' ');
            if (sp1 <= 0)
            {
                return "cat.cfg.set | 用法: cat.cfg.set <key> <字段> <值>";
            }
            string key = rest.Substring(0, sp1).Trim();
            string tail = rest.Substring(sp1 + 1);
            int sp2 = tail.IndexOf(' ');
            if (sp2 <= 0)
            {
                return "cat.cfg.set | 用法: cat.cfg.set <key> <字段> <值>";
            }
            string field = tail.Substring(0, sp2).Trim();
            string value = tail.Substring(sp2 + 1).Trim();
            string id;
            string path;
            string error = ResolveCatCfgTarget(key, out id, out path);
            if (error.Length > 0)
            {
                return "cat.cfg.set | " + error;
            }
            CatCfgData cfg = LoadCatCfg(path);
            if (cfg == null)
            {
                return "cat.cfg.set | cat.cfg 不存在: " + id;
            }
            // [段1] 字段级写入——白名单 + 类型/值域校验（复用端点写时校验函数；非法一律报错退出，不静默回落）
            string applied;
            bool immediate = false;
            if (field == "displayName")
            {
                if (value.Length == 0)
                {
                    return "cat.cfg.set | displayName 不能为空";
                }
                cfg.DisplayName = value;
                applied = "displayName=" + value;
            }
            else if (field == "apiConfigId")
            {
                string normalized = NormalizeApiConfigId(value, out error);
                if (error.Length > 0)
                {
                    return "cat.cfg.set | " + error;
                }
                cfg.ApiConfigId = normalized;
                applied = "apiConfigId=" + normalized;
                immediate = true;
            }
            else if (field == "persona")
            {
                cfg.Persona = value;
                applied = "persona（" + value.Length.ToString() + " 字符）";
            }
            else if (field == "toolNames")
            {
                string valid = ValidateToolNames(value);
                cfg.ToolNames = valid;
                applied = "toolNames=" + valid;
            }
            else if (field == "injectList")
            {
                string[] paths = ParseInjectList(value);
                cfg.InjectList = paths;
                applied = "injectList=" + JoinArray(paths);
            }
            else if (field == "packs")
            {
                string[] packKeys = SplitList(value);
                cfg.Packs = packKeys;
                applied = "packs=" + JoinArray(packKeys);
            }
            else if (field == "qqbotId")
            {
                if (value.Length == 0)
                {
                    cfg.QqBotId = "";
                    applied = "qqbotId=（已清空）";
                }
                else
                {
                    Guid parsed;
                    if (!Guid.TryParse(value, out parsed) || parsed == Guid.Empty)
                    {
                        return "cat.cfg.set | qqbotId 非法: " + value;
                    }
                    string boundBy = FindQqBotBindingOwner(id, value);
                    if (boundBy.Length > 0)
                    {
                        return "cat.cfg.set | 该 QQ Bot 已绑定猫「" + boundBy + "」——一只 Bot 只能绑一只猫";
                    }
                    cfg.QqBotId = value;
                    applied = "qqbotId=" + value;
                }
            }
            else if (field == "qqbotEnable")
            {
                if (string.Equals(value, "true", StringComparison.OrdinalIgnoreCase))
                {
                    cfg.QqBotEnable = true;
                    applied = "qqbotEnable=true";
                }
                else if (string.Equals(value, "false", StringComparison.OrdinalIgnoreCase))
                {
                    cfg.QqBotEnable = false;
                    applied = "qqbotEnable=false";
                }
                else
                {
                    return "cat.cfg.set | qqbotEnable 非法值: " + value + "（true/false）";
                }
            }
            else if (field == "enabledRoots")
            {
                string[] roots = ValidateEnabledRoots(SplitList(value));
                cfg.EnabledRoots = roots;
                applied = "enabledRoots=" + JoinArray(roots);
            }
            else
            {
                return "cat.cfg.set | 非法字段: " + field + "（可用: displayName/apiConfigId/persona/toolNames/injectList/packs/qqbotId/qqbotEnable/enabledRoots）";
            }
            // [段2] 全量写回 + 生效入队（与端点同源：写后 catcfg.apply 主线程泵消费）
            SaveCatCfgData(id, cfg);
            _catQueue.Enqueue("catcfg.apply " + id);
            // [段3] 落盘实况——写后读回对照（非"已提交"）
            CatCfgData after = LoadCatCfg(path);
            if (after == null)
            {
                return "cat.cfg.set | " + id + " | " + applied + " 已写入，但读回失败（落盘异常——请核查 cat.cfg）";
            }
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            sb.AppendLine("cat.cfg.set | " + id + " | " + applied + " 已更新");
            sb.AppendLine("  [落盘] cat.cfg 已写回（未提交字段逐字保留）");
            if (immediate)
            {
                sb.AppendLine("  [生效] 立即（LLM 端点已切换）");
            }
            else
            {
                sb.AppendLine("  [生效] 前文项新会话生效（catcfg.apply 已入队）");
            }
            sb.Append(FormatCatCfg(id, after));
            return sb.ToString();
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
            if (name == null || name.Length == 0)
            {
                // 空名 = 自动命名（默认小猫 / 默认小猫(1)…）——新建不再强制输入显示名
                name = NextDefaultCatName();
            }
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
        /// 默认猫名生成——「默认小猫」占用时依次「默认小猫(1)」「默认小猫(2)」…（注册表内已用名比对，大小写不敏感）。
        /// </summary>
        /// <returns>未占用的默认显示名</returns>
        private static string NextDefaultCatName()
        {
            string baseName = "默认小猫";
            HashSet<string> used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < _cats.Count; i++)
            {
                if (_cats[i].DisplayName != null)
                {
                    used.Add(_cats[i].DisplayName);
                }
            }
            if (!used.Contains(baseName))
            {
                return baseName;
            }
            int n = 1;
            while (true)
            {
                string candidate = baseName + "(" + n.ToString() + ")";
                if (!used.Contains(candidate))
                {
                    return candidate;
                }
                n = n + 1;
            }
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
            cat.Session.Store.Rewrite(cat.Session.Context.GetMessages(), cat.Session.LastStats);
            _cats.Add(cat);
            LogStore.Add("CatHome4", 1, "已从「" + source.DisplayName + "」节点 " + msgIndex.ToString() + " 分支新猫「" + name + "」（id " + id + "），静默待启动", "CHAT");
            return "session.fork | id=" + id + " | name=" + name + " | 已从节点 " + msgIndex.ToString() + " 分支（静默态，cat.start 启动）";
        }

        /// <summary>
        /// cat.list——全猫列出（主干会话前置 + 多猫注册表；id/显示名/状态/端口）。
        /// 主干会话口径与前端列表（BuildCatsJson）一致——_cats 注册表不含 majordomo。
        /// </summary>
        /// <returns>结果文本</returns>
        private static string HandleCatList()
        {
            // 主干会话前置——与前端列表（BuildCatsJson）同口径：多猫注册表 _cats 不含 majordomo
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            sb.Append("cat.list | " + (_cats.Count + 1).ToString() + " 只猫");
            sb.Append(Environment.NewLine);
            string majorState;
            if (_majorHost != null)
            {
                majorState = "运行中 :" + _majorPort.ToString();
            }
            else
            {
                majorState = "静默";
            }
            sb.Append("  majordomo | majordomo | " + majorState + " | 主干会话（禁停禁删）");
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
        /// cat.info——全猫状态统计（主干 + 多猫；字段与快照 sessions 段 / info tokens 段同源——不另起数据面）。
        /// 返回体 = 整块分类 JSON（缩进 + 中文直显——同 info 规格，无「头 + 正文」两段）。
        /// </summary>
        /// <returns>JSON 文本</returns>
        private static string HandleCatInfo()
        {
            List<object> catList = new List<object>();
            ChatSession majorSession = null;
            if (_chatBridge != null)
            {
                majorSession = _chatBridge.DefaultSession;
            }
            catList.Add(BuildCatInfoEntry("majordomo", "majordomo", _majorHost != null, _majorPort, true, majorSession));
            for (int i = 0; i < _cats.Count; i = i + 1)
            {
                CatEntry cat = _cats[i];
                catList.Add(BuildCatInfoEntry(cat.Id, cat.DisplayName, cat.Running, cat.Port, false, cat.Session));
            }
            Dictionary<string, object> info = new Dictionary<string, object>();
            info["ok"] = true;
            info["tool"] = "cat.info";
            info["time"] = new { now = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") };
            info["count"] = catList.Count;
            info["cats"] = catList;
            return JsonSerializer.Serialize(info, InfoJsonOptions);
        }
        /// <summary>
        /// 单猫状态条目——运行面（运行态 / 端口 / 主干标记）+ 会话观测字段（与快照 sessions 段 / info tokens 段同源）。
        /// context 取 ContextTokensKnown（请求级实时优先，未请求回落轮末落盘值——看别猫场景 idle 猫亦有值）。
        /// 会话缺失（未附加 / 已销毁）时只输出运行面字段——不补空值、不写 NaN（同 info「无则省略键」口径）。
        /// </summary>
        /// <param name="id">猫 id</param>
        /// <param name="name">显示名</param>
        /// <param name="running">是否运行</param>
        /// <param name="port">监听端口（0=未监听）</param>
        /// <param name="special">是否主干特殊会话（禁停禁删）</param>
        /// <param name="session">会话实体（可空）</param>
        /// <returns>状态条目字典</returns>
        private static Dictionary<string, object> BuildCatInfoEntry(string id, string name, bool running, int port, bool special, ChatSession session)
        {
            Dictionary<string, object> entry = new Dictionary<string, object>();
            entry["id"] = id;
            entry["name"] = name;
            entry["special"] = special;
            entry["running"] = running;
            entry["port"] = port;
            if (session == null)
            {
                return entry;
            }
            string runStateName;
            int requests;
            Dictionary<string, long> runMs = session.GetRunState(out runStateName, out requests);
            entry["phase"] = session.Phase.ToString();
            entry["runState"] = runStateName;
            entry["runMs"] = runMs;
            entry["requests"] = requests;
            entry["round"] = session.Round;
            entry["msgCount"] = session.MsgCount;
            entry["pending"] = session.PendingCount;
            entry["noteActive"] = session.NoteActive;
            entry["contextCount"] = session.ContextCount;
            entry["context"] = session.ContextTokensKnown;
            entry["lastActiveAt"] = session.LastContextChangeAt;
            return entry;
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
            int port = AllocatePort(PortBand.FamilyFrom);
            if (port < 0)
            {
                return "cat.start | 端口分配失败（端口段 " + PortBand.FamilyFrom.ToString() + "-" + PortBand.FamilyTo.ToString() + " 全占用）";
            }
            HttpHost host;
            try
            {
                host = StartCatHost(cat, port);
            }
            catch (Exception ex)
            {
                // 试绑后释放的竞态窗口——换端口重试一次
                int retryPort = AllocatePort(port + PortBand.FamilyStep());
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
            SetCatRuntime(cat.Id, true, port);
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
            SetCatRuntime(cat.Id, false, 0);
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
                catch (Exception ex)
                {
                    // 停止异常不阻断删除
                    LogStore.Add("AdminService", 2, "停止猫实例异常，不阻断删除: " + ex.Message, "SYS");
                }
            }
            _cats.Remove(cat);
            _chatBridge.RemoveSession(cat.Session);
            // M4e 猫级白名单——缓存随猫销毁
            ToolCatContext.RemoveCatFileSystem(cat.Id);
            // 运行态条目随猫销毁（Data/runtime/cats.json）
            RemoveCatRuntime(cat.Id);
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
                // [段2] 每猫配置四字段——persona/toolNames/injectList/packs（M2/R4；cfg 缺失=新猫走全局默认模板继承）
                string persona = "";
                string toolNames = "";
                string[] injectList = new string[0];
                string[] packs = new string[0];
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
                    if (cfgData.Packs != null)
                    {
                        packs = cfgData.Packs;
                    }
                }
                else
                {
                    // 新猫——cat-default.cfg 模板继承（模板缺失=现状空四字段）
                    CatDefaultCfgData tpl = LoadCatDefaultCfg();
                    if (tpl != null)
                    {
                        persona = tpl.DefaultPersona != null ? tpl.DefaultPersona : "";
                        toolNames = tpl.DefaultToolNames != null ? tpl.DefaultToolNames : "";
                        injectList = tpl.DefaultInjectList != null ? tpl.DefaultInjectList : new string[0];
                        packs = tpl.DefaultPacks != null ? tpl.DefaultPacks : new string[0];
                    }
                }
                // M2c 声明面裁剪——读时比对（非法名过滤/全空全量保底）
                ToolSpec[] catSpecs = FilterToolSpecs(ResolveToolNames(toolNames, false), false);
                // [段3] 上下文 + 前文恢复/注入
                ChatContext context = new ChatContext();
                SessionStore store = new SessionStore(Path.Combine(_dataRoot, "Data", "sessions", id, id + ".jsonl"));
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
                // 会话标识 ≡ 猫 key（唯一标识——不再有独立"会话身份"层）；哨兵：标识被外部改写时校正并回写（常态恒等不触发）
                if (!string.Equals(store.SessionId, id, StringComparison.Ordinal))
                {
                    store.SessionId = id;
                    store.Rewrite(context.GetMessages(), restoredStats);
                    LogStore.Add("CatHome4", 1, "会话标识对齐猫 key: " + id, "CHAT");
                }
                // [段4] 会话构造——M1c 每猫独立 Runtime（API 配置池按该猫 apiConfigId 构造）；M2c 声明面按猫裁剪
                // 端点角色——会话级主要 / 备用（同一实例注入 Runtime 与会话：自动故障转移与手动对调共用一份状态）
                LlmEndpointRole apiRole = new LlmEndpointRole();
                ILlmRuntime catRuntime = new DeepSeekLlmRuntime(apiStore, apiConfigId, globalConfig, apiRole);
                SessionViewStore viewStore = new SessionViewStore(Path.Combine(_dataRoot, "Data", "sessions", id, id + ".view.json"));
                ChatSession session = new ChatSession(id, displayName, context, store, catRuntime, _oa, catSpecs, ExecuteTool, viewStore);
                session.AttachApiRole(apiRole);
                // A111——块序变更通知接线（视图层变更 → 转发面游标校正；猫 key 在组合根注入）
                AttachViewOrderNotify(id, viewStore);
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
                cat.ApiRole = apiRole;
                cat.Persona = persona;
                cat.ToolNames = toolNames;
                cat.InjectList = injectList;
                cat.Packs = packs;
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
        /// 解析猫的有效 LLM API 配置——cat.cfg apiConfigId 零值/缺失=跟随全局默认端点，非零=按身份取（A50 端点解析同源化）。
        /// 消费面：info 环境信息（本猫实际生效端点）；默认猫与多猫同源。
        /// </summary>
        /// <param name="catKey">猫 key（majordomo=默认猫；多猫=会话 ID）</param>
        /// <param name="followDefault">输出：true=跟随全局默认端点；false=猫显式绑定（引用失效时同样为 false）</param>
        /// <returns>生效配置；无默认端点/引用失效 null</returns>
        internal static CH_LlmApiConfig ResolveEffectiveApiConfig(string catKey, out bool followDefault)
        {
            followDefault = true;
            Guid apiConfigId = Guid.Empty;
            if (catKey != null && catKey.Length > 0)
            {
                CatCfgData cfg = LoadCatCfg(Path.Combine(_dataRoot, "Data", "sessions", catKey, "cat.cfg"));
                apiConfigId = ResolveApiConfigId(cfg);
            }
            if (_apiStore == null)
            {
                return null;
            }
            if (apiConfigId == Guid.Empty)
            {
                return _apiStore.ResolveDefault();
            }
            followDefault = false;
            CH_LlmApiConfig config;
            if (_apiStore.TryGet(apiConfigId, out config))
            {
                return config;
            }
            return null;
        }

        /// <summary>
        /// 收集所有绑定 qqbot 的猫——默认猫（majordomo）+ 多猫（R2.3.5 输出转发轮询面；A58 的 1:1 约束在 Bot 侧——每猫各自的 Bot）。
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
                    IsTimebackActive = delegate () { return captured.TimebackActive; },
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
                        IsTimebackActive = delegate () { return captured.Session.TimebackActive; },
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
        /// 取绑定指定 qqbot 的猫（A58 1:1——一个 Bot 唯一对应一只猫：重复配置取首个 + L2 留痕）。
        /// </summary>
        /// <param name="qqBotId">qqbot 配置身份</param>
        /// <returns>绑定目标列表（≤1 项；空=未绑定任何猫）</returns>
        internal static List<QqTarget> CollectQqTargets(Guid qqBotId)
        {
            List<QqTarget> result = new List<QqTarget>();
            List<QqTarget> all = CollectAllQqTargets();
            for (int i = 0; i < all.Count; i = i + 1)
            {
                if (all[i].QqBotId != qqBotId)
                {
                    continue;
                }
                if (result.Count == 0)
                {
                    result.Add(all[i]);
                    continue;
                }
                // 1:1 约束外（旧数据 / 手工改 cat.cfg）——取首个 + L2 留痕（可诊断）
                LogStore.Add("QQBot", 2, "同一 Bot 被多猫绑定（取首个） | " + qqBotId.ToString("D") + " | 首个 " + result[0].Key + " · 忽略 " + all[i].Key, "QQBOT");
                break;
            }
            return result;
        }

        /// <summary>
        /// qqbot 绑定占用者查询（A58 1:1）——返回除 catKey 自身外绑定了该 Bot 的猫显示名（空=无人占用）。
        /// 两处消费：保存配置时查重（冲突显式拒绝）· 配置读面标注（前端禁用已绑他猫的 Bot）。
        /// </summary>
        /// <param name="catKey">当前猫寻址键（majordomo=默认猫；自身不计入占用）</param>
        /// <param name="qqbotId">Bot 身份（Guid 文本）</param>
        /// <returns>占用者显示名（空串=无占用）</returns>
        internal static string FindQqBotBindingOwner(string catKey, string qqbotId)
        {
            Guid target;
            if (!Guid.TryParse(qqbotId, out target) || target == Guid.Empty)
            {
                return "";
            }
            if (catKey != "majordomo" && _chatBridge.DefaultQqBotId == target)
            {
                return "majordomo";
            }
            for (int i = 0; i < _cats.Count; i = i + 1)
            {
                CatEntry cat = _cats[i];
                if (cat.Id == catKey)
                {
                    continue;
                }
                if (cat.QqBotId == target)
                {
                    return cat.DisplayName;
                }
            }
            return "";
        }

        /// <summary>
        /// QQBot 渠道说明串——info 环境信息消费（A58）：该猫已绑定且启用 qqbot 转发时给出文件发送用法；
        /// 未绑定 / 未启用 → 空串（info 不显示任何 qqbot 相关内容）。
        /// </summary>
        /// <param name="catKey">猫 key（majordomo=默认猫）</param>
        /// <returns>说明串（空=未接入 qqbot）</returns>
        internal static string BuildCatQqBotInfo(string catKey)
        {
            if (catKey == null || catKey.Length == 0)
            {
                return "";
            }
            Guid botId;
            bool enabled;
            if (catKey == "majordomo")
            {
                botId = _chatBridge.DefaultQqBotId;
                enabled = _chatBridge.DefaultQqBotEnable;
            }
            else
            {
                CatEntry cat = FindCat(catKey);
                if (cat == null)
                {
                    return "";
                }
                botId = cat.QqBotId;
                enabled = cat.QqBotEnable;
            }
            if (botId == Guid.Empty || !enabled)
            {
                return "";
            }
            string botName = ResolveQqBotDisplayName(botId);
            if (botName.Length == 0)
            {
                botName = botId.ToString("D");
            }
            return botName + "：发本地文件=单独一行写 [QQBot发送文件:\"<真实绝对路径>\"]（≤10MB · 仅私聊）";
        }
        /// <summary>
        /// 本地对话端点端口解析——info 环境信息消费（本猫实际监听端口）。
        /// majordomo = 独立对话端口；多猫 = 注册表运行态端口；未监听 / 未知 key = 0。
        /// </summary>
        /// <param name="catKey">猫 key（majordomo=默认猫）</param>
        /// <returns>监听端口；0=未监听</returns>
        internal static int ResolveCatPort(string catKey)
        {
            if (catKey == null || catKey.Length == 0)
            {
                return 0;
            }
            if (catKey == "majordomo")
            {
                if (_majorPort < 0)
                {
                    return 0;
                }
                return _majorPort;
            }
            CatEntry cat = FindCat(catKey);
            if (cat == null || !cat.Running)
            {
                return 0;
            }
            return cat.Port;
        }

        /// <summary>
        /// Bot 显示名解析——Bot 池按身份查显示名（池内缺失 → 空串，调用方回退身份串）。
        /// </summary>
        /// <param name="botId">Bot 配置身份</param>
        /// <returns>显示名（空=池内缺失）</returns>
        internal static string ResolveQqBotDisplayName(Guid botId)
        {
            CH_QqBotConfigStore store = null;
            DataBox.TryResolve<CH_QqBotConfigStore>(out store);
            if (store == null)
            {
                return "";
            }
            CH_QqBotConfig cfg;
            if (store.TryGet(botId, out cfg) && cfg != null && cfg.DisplayName != null)
            {
                return cfg.DisplayName;
            }
            return "";
        }

        /// <summary>
        /// 装配块序变更通知（A111）——视图层变更（清除 / 重建 / 轮统计清理 / 区间转废弃）→ QQ 转发面游标校正。
        /// 猫 key 由组合根注入（Core 不知猫 key、QQ 不知视图层——两侧零耦合）；未挂 qqbot 的猫同样接线（无转发态即无动作）。
        /// </summary>
        /// <param name="catKey">猫 key（转发态游标键——与 QqTarget.Key 同源）</param>
        /// <param name="viewStore">会话视图存储</param>
        internal static void AttachViewOrderNotify(string catKey, SessionViewStore viewStore)
        {
            if (viewStore == null)
            {
                return;
            }
            viewStore.OnBlocksReordered = delegate (ViewOrderChange change)
            {
                QQBotService.NotifyBlocksReordered(catKey, change);
            };
        }

        /// <summary>
        /// 视图块 → QQ 转发项转换——窄 DTO（QQ 域只消费 RenderType / Content / Done / Hash；text 块提取 payload.content，
        /// Hash 供转发面锚定游标——A111）。
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
                item.Done = "";
                if (b.Hash == null)
                {
                    item.Hash = "";
                }
                else
                {
                    item.Hash = b.Hash;
                }
                if (item.RenderType == "text")
                {
                    item.Content = ExtractTextContent(b.Payload);
                }
                else if (item.RenderType == "roundsum")
                {
                    item.Done = ExtractRoundSumDone(b.Payload);
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
            catch (Exception ex)
            {
                LogStore.Add("CatHome4", 2, "载荷 content 提取失败（回落空）: " + ex.Message, "CHAT");
            }
            return "";
        }

        /// <summary>
        /// 提取 roundsum 块的 done 字段——本轮结束语义（tool=工具主动 done / stream=流式自然收尾）。
        /// 缺失或解析失败回退 stream（保守按自然收尾：来源照常出队消费，不误留）。
        /// </summary>
        /// <param name="payloadJson">roundsum 载荷 JSON（{"type":"roundsum","data":{...,"done":"..."}}）</param>
        /// <returns>done 值（tool / stream）</returns>
        private static string ExtractRoundSumDone(string payloadJson)
        {
            if (payloadJson == null || payloadJson.Length == 0)
            {
                return "stream";
            }
            try
            {
                using (JsonDocument d = JsonDocument.Parse(payloadJson))
                {
                    JsonElement data;
                    if (d.RootElement.TryGetProperty("data", out data) && data.ValueKind == JsonValueKind.Object)
                    {
                        JsonElement done;
                        if (data.TryGetProperty("done", out done) && done.ValueKind == JsonValueKind.String)
                        {
                            string got = done.GetString();
                            if (got != null && got.Length > 0)
                            {
                                return got;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogStore.Add("CatHome4", 2, "roundsum done 提取失败（按 stream 处理）: " + ex.Message, "CHAT");
            }
            return "stream";
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
        /// 默认会话判定——特权工具永久激活基准（猫 key = majordomo / 空；design-ch4-tools §三·十）。
        /// </summary>
        /// <param name="key">猫寻址键</param>
        /// <returns>true=默认会话</returns>
        internal static bool IsDefaultCatKey(string key)
        {
            if (key == null || key.Length == 0)
            {
                return true;
            }
            return string.Equals(key, "majordomo", StringComparison.Ordinal);
        }

        /// <summary>
        /// 当前授权工具名——工具判定授权面的实时查询入口（design-ch4-tools §三·十一）。
        /// 实时解析当前 cat.cfg 名单（空 / 全非法 → 全量保底；随注册表变化即时反映）；默认猫读静态面原始串，多猫读 CatEntry。
        /// </summary>
        /// <param name="catKey">猫键（null / 空 / majordomo = 默认猫）</param>
        /// <returns>授权工具名数组（非 null）</returns>
        internal static string[] ResolveCatAuthorizedToolNames(string catKey)
        {
            string raw = "";
            if (catKey == null || catKey.Length == 0 || catKey == "majordomo")
            {
                if (_chatBridge != null)
                {
                    raw = _chatBridge.DefaultToolNames;
                }
            }
            else
            {
                CatEntry cat = FindCat(catKey);
                if (cat != null && cat.ToolNames != null)
                {
                    raw = cat.ToolNames;
                }
            }
            if (raw == null)
            {
                raw = "";
            }
            return ResolveToolNames(raw, IsDefaultCatKey(catKey));
        }

        /// <summary>
        /// 猫会话——按猫键取会话实体（M4e 猫级白名单同源寻址；majordomo/空键走默认会话）。
        /// 消费面：info 自查、快照 sessions 段的实时字段（前文长度 / 最近前文变动时刻）。
        /// </summary>
        /// <param name="key">猫键（id/显示名；null/空=majordomo 默认会话）</param>
        /// <returns>会话；未找到 null</returns>
        internal static ChatSession FindCatSession(string key)
        {
            if (key == null || key.Length == 0 || key == "majordomo")
            {
                if (_chatBridge != null && _chatBridge.DefaultSession != null)
                {
                    return _chatBridge.DefaultSession;
                }
                return null;
            }
            CatEntry cat = FindCat(key);
            if (cat != null && cat.Session != null)
            {
                return cat.Session;
            }
            return null;
        }

        /// <summary>
        /// 动态端口分配——从起始端口起沿端口段方向 TCP 试绑（试绑后释放；竞态由 cat.start 重试兜底）。
        /// 扫描边界 = 端口段终点（开发区 8070 / 部署区 8180）——段内全占即失败，不越界到对方区段。
        /// </summary>
        /// <param name="startPort">起始端口（含；应在端口段内）</param>
        /// <returns>可用端口；段内全占用 -1</returns>
        private static int AllocatePort(int startPort)
        {
            if (PortBand == null)
            {
                LogStore.Add("CatHome4", 2, "端口分配失败：端口段未注入（AdminService.Configure 未接线）", "CHAT");
                return -1;
            }
            int step = PortBand.FamilyStep();
            int limit = PortBand.FamilyTo;
            int port = startPort;
            while (true)
            {
                if (step > 0 && port > limit)
                {
                    break;
                }
                if (step < 0 && port < limit)
                {
                    break;
                }
                if (!IsPortTaken(port))
                {
                    try
                    {
                        TcpListener listener = new TcpListener(IPAddress.Loopback, port);
                        listener.Start();
                        listener.Stop();
                        return port;
                    }
                    catch (Exception ex)
                    {
                        // 试绑失败——继续下一端口
                        LogStore.Add("AdminService", 1, "端口试绑未成，继续下一端口: " + ex.Message, "SYS");
                    }
                }
                port = port + step;
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
                FrontendTestPort = PortBand.FrontendTestPort,
                SessionId = cat.Session.Id,
                SnapshotBuilder = BuildSnapshotJson,
                Dispatcher = (string line) => DispatchCommandForCat(cat, line),
                EnvelopeBuilder = MakeChatEnvelopeBuilder(cat.Session),
                FrameBuilder = null,
                HistoryBuilder = (int max) => _chatBridge.BuildHistoryView(cat.Session, max),
                CatsBuilder = null,
                NoteBuilder = () => cat.Session.BuildNoteJson(),
                DelayBuilder = () => DelayQueue.BuildListJson(cat.Session.Id),
                PatchBuilder = null,
                SessionStateBuilder = () => cat.Session.BuildRunStateJson(),
                ApiRoleBuilder = () => BuildApiRoleJson(cat.Session),
                ApiRoleToggler = () => ToggleApiRoleJson(cat.Session),
                ServeChatPage = true,
                RouteRegistrar = RegisterChatPageRoutes,
                HtmlRootProvider = HtmlRoot,
                DisplayName = cat.DisplayName
            });
        }

        /// <summary>
        /// majordomo 独立对话端口启动——F2.2 方案 A（与多猫同构）：独立端口 serve chat.html，绑定 DefaultSession。
        /// 主端口保留管理面板（index.html）；DefaultSession.AttachHost 改绑本端口（chat 事件推送走 chat.html）。
        /// 强制自启：Bootstrap 调用（段6b 前）；无关闭/删除（cat.stop/cat.delete 拒绝——HandleCatStop/HandleCatDelete）。
        /// </summary>
        /// <returns>是否成功启动（端口分配失败 false）</returns>
        internal static bool StartMajorHost()
        {
            if (_majorHost != null)
            {
                return true;
            }
            int port = AllocatePort(PortBand.FamilyFrom);
            if (port < 0)
            {
                LogStore.Add("CatHome4", 2, "majordomo 独立端口启动失败：端口段 " + PortBand.FamilyFrom.ToString() + "-" + PortBand.FamilyTo.ToString() + " 全占用", "CHAT");
                return false;
            }
            HttpHost host = HttpHost.Start(new HttpHostOptions
            {
                Port = port,
                FrontendTestPort = PortBand.FrontendTestPort,
                SessionId = _chatBridge.DefaultSession.Id,
                SnapshotBuilder = BuildSnapshotJson,
                Dispatcher = (string line) => DispatchCommandForMajor(line),
                EnvelopeBuilder = MakeChatEnvelopeBuilder(_chatBridge.DefaultSession),
                FrameBuilder = null,
                HistoryBuilder = (int max) => _chatBridge.BuildHistoryView(_chatBridge.DefaultSession, max),
                CatsBuilder = null,
                NoteBuilder = () => _chatBridge.DefaultSession.BuildNoteJson(),
                DelayBuilder = () => DelayQueue.BuildListJson(_chatBridge.DefaultSession.Id),
                PatchBuilder = null,
                SessionStateBuilder = () => _chatBridge.DefaultSession.BuildRunStateJson(),
                ApiRoleBuilder = () => BuildApiRoleJson(_chatBridge.DefaultSession),
                ApiRoleToggler = () => ToggleApiRoleJson(_chatBridge.DefaultSession),
                ServeChatPage = true,
                RouteRegistrar = RegisterChatPageRoutes,
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
            if (line == "cat.continue")
            {
                // 继续——HTTP 线程直接调用（内部仅前文非空校验 + 入队——线程安全；相位推进主线程 Pump 消费）
                _chatBridge.DefaultSession.Continue();
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
            // 延迟指令族——delay.*（design-ch4-delay §5.1；调度器内部锁保护——HTTP 线程可直执）
            if (DelayCommand.Handle(_chatBridge.DefaultSession.Id, line))
            {
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
            if (line == "cat.continue")
            {
                // 继续——HTTP 线程直接调用（内部仅前文非空校验 + 入队——线程安全；相位推进主线程 Pump 消费）
                cat.Session.Continue();
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
            // 延迟指令族——delay.*（design-ch4-delay §5.1；调度器内部锁保护——HTTP 线程可直执）
            if (DelayCommand.Handle(cat.Session.Id, line))
            {
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
            // 主面板数据面扩展——补 apiConfigId / qqbotId / enabledRoots（行内编辑与目录白名单展示用）
            // 会话状态扩展（2026-10-01）——复用 BuildCatInfoEntry（与 cat.info / 快照 sessions 段同源，不另起数据面）
            CatCfgData majorCfg = LoadCatCfg(Path.Combine(_dataRoot, "Data", "sessions", "majordomo", "cat.cfg"));
            string majorName = "majordomo";
            string majorApi = "";
            string majorQq = "";
            string[] majorRoots = new string[0];
            if (majorCfg != null)
            {
                if (majorCfg.DisplayName != null && majorCfg.DisplayName.Length > 0)
                {
                    majorName = majorCfg.DisplayName;
                }
                if (majorCfg.ApiConfigId != null)
                {
                    majorApi = majorCfg.ApiConfigId;
                }
                if (majorCfg.QqBotId != null)
                {
                    majorQq = majorCfg.QqBotId;
                }
                if (majorCfg.EnabledRoots != null)
                {
                    majorRoots = majorCfg.EnabledRoots;
                }
            }
            ChatSession majorSession = null;
            if (_chatBridge != null)
            {
                majorSession = _chatBridge.DefaultSession;
            }
            Dictionary<string, object> majorEntry = BuildCatInfoEntry("majordomo", majorName, _majorHost != null, _majorPort, true, majorSession);
            majorEntry["apiConfigId"] = majorApi;
            majorEntry["qqbotId"] = majorQq;
            majorEntry["enabledRoots"] = majorRoots;
            list.Add(majorEntry);
            for (int i = 0; i < _cats.Count; i++)
            {
                CatEntry cat = _cats[i];
                CatCfgData cfg = LoadCatCfg(Path.Combine(_dataRoot, "Data", "sessions", cat.Id, "cat.cfg"));
                string apiConfigId = "";
                string qqbotId = "";
                string[] enabledRoots = new string[0];
                if (cfg != null)
                {
                    if (cfg.ApiConfigId != null)
                    {
                        apiConfigId = cfg.ApiConfigId;
                    }
                    if (cfg.QqBotId != null)
                    {
                        qqbotId = cfg.QqBotId;
                    }
                    if (cfg.EnabledRoots != null)
                    {
                        enabledRoots = cfg.EnabledRoots;
                    }
                }
                Dictionary<string, object> entry = BuildCatInfoEntry(cat.Id, cat.DisplayName, cat.Running, cat.Port, false, cat.Session);
                entry["apiConfigId"] = apiConfigId;
                entry["qqbotId"] = qqbotId;
                entry["enabledRoots"] = enabledRoots;
                list.Add(entry);
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
            catch (Exception ex)
            {
                LogStore.Add("CatHome4", 2, "启动扫描目录枚举失败（跳过加载）: " + ex.Message, "CHAT");
                return;
            }
            // 运行态分离——running/port 取自 Data/runtime/cats.json（cat.cfg 只承载用户配置；启动链零写入 cat.cfg）
            Dictionary<string, CatRuntimeEntry> runtimeMap = LoadCatRuntime();
            bool runtimeDirty = false;
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
                // 运行态取值——文件优先；无记录则从旧 cat.cfg 迁移一次（旧版本把 running/port 混在 cat.cfg）
                CatRuntimeEntry rt;
                if (!runtimeMap.TryGetValue(cat.Id, out rt))
                {
                    if (MigrateCatRuntime(cat.Id, cfg.Running, cfg.Port, runtimeMap))
                    {
                        runtimeDirty = true;
                        rt = runtimeMap[cat.Id];
                        LogStore.Add("CatHome4", 1, "启动扫描：猫「" + cat.DisplayName + "」运行态迁移（running=" + (rt.Running ? "true" : "false") + " port=" + rt.Port.ToString() + "）", "CHAT");
                    }
                }
                if (rt == null || !rt.Running)
                {
                    // 静默态——注册表可见（cat.list/start 可寻址），不拉起
                    LogStore.Add("CatHome4", 1, "启动扫描：猫「" + cat.DisplayName + "」为静默态（id " + cat.Id + "）", "CHAT");
                    continue;
                }
                int port = rt.Port;
                if (port < 1024 || IsPortTaken(port))
                {
                    port = AllocatePort(PortBand.FamilyFrom);
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
                // 端口与登记值不一致（被占回落）→ 只更新运行态文件（启动链不写 cat.cfg）
                if (port != rt.Port)
                {
                    rt.Port = port;
                    runtimeDirty = true;
                }
                LogStore.Add("CatHome4", 1, "启动扫描：已拉起猫「" + cat.DisplayName + "」，端口 " + port.ToString(), "CHAT");
            }
            if (runtimeDirty)
            {
                SaveCatRuntime(runtimeMap);
            }
        }

    }
}
