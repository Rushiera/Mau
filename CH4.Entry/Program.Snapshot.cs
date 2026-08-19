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
            AppendCatCompact(cats, "ToolTestCat", _toolHandle);
            AppendCatCompact(cats, "IOTestCat", _ioHandle);
            AppendCatCompact(cats, "QuickCat", _quickHandle);
            AppendCatCompact(cats, "MajorDomoCat", _majorHandle);
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
        /// 单 Cat 紧凑状态——状态行拼接（帧流一行体积控制）
        /// </summary>
        /// <param name="cats">目标列表</param>
        /// <param name="name">Cat 名</param>
        /// <param name="handle">Flow 句柄</param>
        private static void AppendCatCompact(List<object> cats, string name, FlowHandle handle)
        {
            if (handle.IsFaulted)
            {
                cats.Add(new { n = name, fd = true });
                return;
            }
            FlowStatusV3 status = handle.Flow.GetStatus();
            string states = "";
            for (int i = 0; i < status.StateLines.Length; i++)
            {
                if (i > 0)
                {
                    states = states + " ";
                }
                states = states + status.StateLines[i];
            }
            cats.Add(new { n = name, s = states });
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
    }
}
}