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
    /// Program 快照面分部——协议 §三 cats/boxes/logs 组装（HttpHost 回调查询）。
    /// P7b partial 拆分——自 Program.cs 原样搬移，逻辑零改动。
    /// </summary>
    public static partial class Program
    {
        /// <summary>
        /// 构建紧凑帧 JSON——frame.jsonl 一行（回放够用：帧/pid/chat_state/各 Cat 状态行/OA 计数；主线程泵调用）
        /// </summary>
        /// <returns>紧凑 JSON 文本</returns>
        private static string BuildCompactFrameJson()
        {
            List<object> cats = new List<object>();
            AppendRegistryCatsCompact(cats);
            OAView oa = _oa.GetSnapshot();
            string chatState = "";
            string cs;
            if (DataBox.TryGet<string>("global", "chat_state", out cs) && cs != null)
            {
                chatState = cs;
            }
            var frame = new
            {
                f = FlowRunner.GlobalFrame,
                pid = Environment.ProcessId,
                chat = chatState,
                oa = new
                {
                    o = oa.OpenCount,
                    w = oa.WorkCount,
                    c = oa.ClosedCount,
                    t = oa.TimeoutCount
                },
                cats = cats
            };
            return JsonSerializer.Serialize(frame);
        }

        /// <summary>
        /// 单 Flow 紧凑状态——状态行拼接（帧流一行体积控制；名字来自注册表）
        /// </summary>
        /// <param name="cats">目标列表</param>
        /// <param name="entry">注册表条目</param>
        /// <param name="flow">Flow 实例（可 null 防御）</param>
        private static void AppendFlowCompact(List<object> cats, FlowEntry entry, IFlow flow)
        {
            if (flow == null)
            {
                cats.Add(new { n = entry.Name, s = "-" });
                return;
            }
            IObservableFlow obs = flow as IObservableFlow;
            if (obs == null)
            {
                cats.Add(new { n = entry.Name, s = "-" });
                return;
            }
            FlowStatusV3 status = obs.GetStatus();
            string states = "";
            for (int i = 0; i < status.StateLines.Length; i++)
            {
                if (i > 0)
                {
                    states = states + " ";
                }
                states = states + status.StateLines[i];
            }
            cats.Add(new { n = entry.Name, s = states });
        }

        /// <summary>
        /// 追加全部 Runtime 实体紧凑状态——从 FlowRegistry 拉取真实实例（替代硬编码）。按注册 ID 排序确定性输出。
        /// </summary>
        /// <param name="cats">目标列表</param>
        private static void AppendRegistryCatsCompact(List<object> cats)
        {
            FlowEntry[] entries = _runner.Registry.Entries;
            Array.Sort(entries, delegate(FlowEntry a, FlowEntry b)
            {
                return a.Id.CompareTo(b.Id);
            });
            for (int i = 0; i < entries.Length; i++)
            {
                IFlow flow = _runner.Registry.Get(entries[i].Id);
                AppendFlowCompact(cats, entries[i], flow);
            }
        }

        /// <summary>
        /// 单 Flow 快照 JSON 追加——协议 §3.2 cats[].status 四柱映射（FlowStatusV3 → 匿名对象）。
        /// 名字/id/type/kind 来自注册表条目——Runtime 真实实例，非业务硬编码。
        /// </summary>
        /// <param name="cats">目标列表</param>
        /// <param name="entry">注册表条目</param>
        /// <param name="flow">Flow 实例（可 null 防御）</param>
        private static void AppendFlowJson(List<object> cats, FlowEntry entry, IFlow flow)
        {
            if (flow == null)
            {
                cats.Add(new { name = entry.Name, id = entry.Id, type = entry.TypeName, kind = entry.Kind, faulted = true, faultReason = "注册表实例缺失", status = (object)null });
                return;
            }
            IObservableFlow obs = flow as IObservableFlow;
            if (obs == null)
            {
                cats.Add(new { name = entry.Name, id = entry.Id, type = entry.TypeName, kind = entry.Kind, faulted = false, faultReason = "无观测面（非 IObservableFlow）", status = (object)null });
                return;
            }

            FlowStatusV3 status = obs.GetStatus();
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

            cats.Add(new { name = entry.Name, id = entry.Id, type = entry.TypeName, kind = entry.Kind, faulted = false, faultReason = "", status = new { stateLines = status.StateLines, sensors = sensors, slots = slots, wires = wires }, desc = obs.GetSelfDesc() });
        }

        /// <summary>
        /// 追加全部 Runtime 实体快照——从 FlowRegistry 拉取真实实例（替代硬编码 QuickCat/DevCat；R0.2 工具组 Flow 化后自动覆盖 TextCat/MauCat/CsCat/ConfigCat）。
        /// 任何 RegisterFlow 注册的实例自动出现——状态面板 = Runtime 真实截面。按注册 ID 排序确定性输出。
        /// </summary>
        /// <param name="cats">目标列表</param>
        private static void AppendRegistryCats(List<object> cats)
        {
            FlowEntry[] entries = _runner.Registry.Entries;
            Array.Sort(entries, delegate(FlowEntry a, FlowEntry b)
            {
                return a.Id.CompareTo(b.Id);
            });
            for (int i = 0; i < entries.Length; i++)
            {
                IFlow flow = _runner.Registry.Get(entries[i].Id);
                AppendFlowJson(cats, entries[i], flow);
            }
        }
    /// <summary>
    /// 构建全量快照 JSON——协议 design-ch4-protocol.md §三（version/pid/frame/cats/oa/logs；logs 按 includeLogs 裁剪）
    /// </summary>
    /// <param name = "includeLogs">是否携带日志（GET 轮询 true / SSE 事件 false——协议 §4.2 snapshot 事件裁剪）</param>
    /// <returns>快照 JSON 文本</returns>
    private static string BuildSnapshotJson(bool includeLogs)
{
        List<object> cats = new List<object>();
        AppendRegistryCats(cats);
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
            boxes.Add(BuildBoxEntry(d));
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
        // [段3] 快照组装——version/pid/frame/cats/sessions/oa/tools/boxes/logs（协议 v1.2：前端状态区会话卡 + 工具注册表）
        var snapshot = new
        {
            version = 2,
            pid = Environment.ProcessId,
            frame = FlowRunner.GlobalFrame,
            cats = cats,
            sessions = BuildSessionsJson(),
            oa = new
            {
                open = oa.OpenCount,
                work = oa.WorkCount,
                closed = oa.ClosedCount,
                timeout = oa.TimeoutCount
            },
            tools = BuildToolsJson(),
            boxes = boxes,
            logs = logs
        };
        return JsonSerializer.Serialize(snapshot);
    }

    /// <summary>
    /// 会话状态段——会话注册表（Majordomo + 多猫）四相环预览（外观层状态区）。
    /// 数据源：Program._sessions（P9.1 会话对象化注册表）；ChatSession 公开观测属性。
    /// </summary>
    /// <returns>会话条目数组（id/name/phase/round/msgCount/pending/noteActive）</returns>
    private static List<object> BuildSessionsJson()
    {
        List<object> sessions = new List<object>();
        List<ChatSession> all = _sessions;
        for (int i = 0; i < all.Count; i++)
        {
            ChatSession s = all[i];
            sessions.Add(new
            {
                id = s.Id,
                name = s.DisplayName,
                phase = PhaseText(s.Phase),
                round = s.Round,
                msgCount = s.MsgCount,
                pending = s.PendingCount,
                noteActive = s.NoteActive
            });
        }
        return sessions;
    }

    /// <summary>
    /// 相位文本化——ChatPhase 枚举 → 状态徽标串（快照对外文本；未知回退 Idle）
    /// </summary>
    /// <param name="phase">会话相位</param>
    /// <returns>文本（Idle/LlmRunning/ToolBatchRunning/Done）</returns>
    private static string PhaseText(ChatPhase phase)
    {
        if (phase == ChatPhase.LlmRunning)
        {
            return "LlmRunning";
        }
        if (phase == ChatPhase.ToolBatchRunning)
        {
            return "ToolBatchRunning";
        }
        if (phase == ChatPhase.Done)
        {
            return "Done";
        }
        return "Idle";
    }

    /// <summary>
    /// 工具注册表段——按组分类摘要（外观层状态区；R0.2 ToolRegistry 单一真相源）。
    /// 条目：name + group（归属 Flow）/builtin（内置标记）。
    /// </summary>
    /// <returns>工具条目数组</returns>
    private static List<object> BuildToolsJson()
    {
        List<object> tools = new List<object>();
        ToolSpec[] specs = ToolRegistry.BuildSpecs();
        for (int i = 0; i < specs.Length; i++)
        {
            ToolRegistryEntry entry = ToolRegistry.Find(specs[i].Name);
            string group = "";
            bool builtin = false;
            if (entry != null)
            {
                group = entry.OwnerFlow;
                builtin = entry.IsBuiltin;
            }
            tools.Add(new { name = specs[i].Name, group = group, builtin = builtin });
        }
        return tools;
    }

    /// <summary>
    /// 构建单盒子条目对象——类型归一（bool/number/string/object 摘要）。
    /// </summary>
    /// <param name="d">DataBox 条目</param>
    /// <returns>条目匿名对象</returns>
    private static object BuildBoxEntry(DataBoxDataEntry d)
    {
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
        return new { scope = d.Scope, key = d.Key, t = t, value = val };
    }

    // 增量 diff 缓存——主线程独占（PumpMainThread 调用；ThreadGuard 契约）
    /// <summary>上次 cats 段 JSON——变化检测</summary>
    private static string _lastCatsJson = "";

    /// <summary>上次 oa 段 JSON——变化检测</summary>
    private static string _lastOaJson = "";

    /// <summary>上次 sessions 段 JSON——变化检测（会话四相环预览）</summary>
    private static string _lastSessionsJson = "";

    /// <summary>上次 boxes 字典——scope+key → 条目 JSON（变化检测）</summary>
    private static Dictionary<string, string> _lastBoxes = new Dictionary<string, string>();

    /// <summary>
    /// 构建增量 patch——与上次快照对比只含变化段（cats 整段/oa 计数/boxes 字典 diff）；无变化返回 null（零推送）。
    /// 主线程泵调用（PumpMainThread——ThreadGuard 契约）；缓存字段主线程独占。
    /// </summary>
    /// <returns>patch JSON 文本；无变化 null</returns>
    public static string BuildPatchJson()
    {
        // [段1] cats 段——整段序列化对比（状态转移才变，频率低）
        List<object> cats = new List<object>();
        AppendRegistryCats(cats);
        string catsJson = JsonSerializer.Serialize(cats);
        bool catsChanged = !string.Equals(catsJson, _lastCatsJson, StringComparison.Ordinal);
        // [段1b] sessions 段——整段序列化对比（会话四相环/轮次/消息数变化推送）
        List<object> sessions = BuildSessionsJson();
        string sessionsJson = JsonSerializer.Serialize(sessions);
        bool sessionsChanged = !string.Equals(sessionsJson, _lastSessionsJson, StringComparison.Ordinal);
        // [段2] oa 段——四计数对比
        OAView oa = _oa.GetSnapshot();
        object oaObj = new
        {
            open = oa.OpenCount,
            work = oa.WorkCount,
            closed = oa.ClosedCount,
            timeout = oa.TimeoutCount
        };
        string oaJson = JsonSerializer.Serialize(oaObj);
        bool oaChanged = !string.Equals(oaJson, _lastOaJson, StringComparison.Ordinal);
        // [段3] boxes 段——字典 diff（scope+key → 条目 JSON；新增/变化进 set，消失进 del）
        DataBoxSnapshot boxSnap = DataBox.Capture();
        Dictionary<string, string> boxesNow = new Dictionary<string, string>();
        for (int i = 0; i < boxSnap.Data.Length; i++)
        {
            DataBoxDataEntry d = boxSnap.Data[i];
            if (IsInternalBox(d.Scope, d.Key))
            {
                continue;
            }
            string entryKey = d.Scope + "\u0001" + d.Key;
            boxesNow[entryKey] = JsonSerializer.Serialize(BuildBoxEntry(d));
        }
        List<object> boxSet = new List<object>();
        List<object> boxDel = new List<object>();
        foreach (KeyValuePair<string, string> kv in boxesNow)
        {
            string old;
            if (!_lastBoxes.TryGetValue(kv.Key, out old) || !string.Equals(old, kv.Value, StringComparison.Ordinal))
            {
                boxSet.Add(JsonSerializer.Deserialize<object>(kv.Value));
            }
        }
        foreach (KeyValuePair<string, string> kv in _lastBoxes)
        {
            if (!boxesNow.ContainsKey(kv.Key))
            {
                int sep = kv.Key.IndexOf('\u0001');
                boxDel.Add(new { scope = kv.Key.Substring(0, sep), key = kv.Key.Substring(sep + 1) });
            }
        }
        bool boxesChanged = boxSet.Count > 0 || boxDel.Count > 0;
        // [段4] 无变化零推送——Idle 稳态静默
        if (!catsChanged && !sessionsChanged && !oaChanged && !boxesChanged)
        {
            return null;
        }
        // [段5] patch 组装 + 缓存更新
        Dictionary<string, object> patch = new Dictionary<string, object>();
        patch["frame"] = FlowRunner.GlobalFrame;
        if (catsChanged)
        {
            patch["cats"] = cats;
            _lastCatsJson = catsJson;
        }
        if (sessionsChanged)
        {
            patch["sessions"] = sessions;
            _lastSessionsJson = sessionsJson;
        }
        if (oaChanged)
        {
            patch["oa"] = oaObj;
            _lastOaJson = oaJson;
        }
        if (boxesChanged)
        {
            patch["boxes"] = new
            {
                set = boxSet,
                del = boxDel
            };
            _lastBoxes = boxesNow;
        }
        return JsonSerializer.Serialize(patch);
    }
}
}