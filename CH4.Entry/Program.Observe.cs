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
    /// Program 观测面分部——三 Cat 状态打印/杂音过滤/内部盒屏蔽/摘要化。
    /// P7b partial 拆分——自 Program.cs 原样搬移，逻辑零改动。
    /// </summary>
    public static partial class Program
    {
        /// <summary>
        /// 观测出口——三 Cat 状态 + 导线在途 + 最近日志（透明性指标观测面）
        /// </summary>
        private static void PrintStatus()
        {
            Console.WriteLine("── 两 Cat 状态 ──");
            PrintFlowStatus("QuickCat", _quickHandle);
            PrintFlowStatus("DevCat", _devHandle);
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
            if (handle == null)
            {
                Console.WriteLine("  " + name + " | 未加载（降级）");
                return;
            }
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
        /// 精简观测出口——三 Cat 状态 + OA 快照 + 最近日志（跳过审计帧序/盒子截面——热重载实测断言面）
        /// </summary>
        private static void PrintStatusShort()
        {
            Console.WriteLine("── 两 Cat 状态 ──");
            PrintFlowStatus("QuickCat", _quickHandle);
            PrintFlowStatus("DevCat", _devHandle);
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
        }
    }
}