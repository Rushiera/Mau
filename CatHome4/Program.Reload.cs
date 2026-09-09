using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using Mau.Runtime;
using Mau.Providers;

namespace CH4
{
    /// <summary>
    /// Program 热重载面分部——reload 事务（忙时拒绝/加载/换注册/试跑回滚/成功换柄）。
    /// P7b partial 拆分——自 Program.cs 原样搬移，逻辑零改动。
    /// </summary>
    public static partial class Program
    {
        /// <summary>
        /// reload 热重载——Flow 注册名（QuickCat/TextCat/...；host-flows 查当前清单）+ 可选 dll 路径（缺省 = 当前 handle 同路径重读）
        /// 流程：新 Load + Tick 试跑验证（失败保留旧）→ UnregisterFlow 旧（D1：CommandBus key 同步清理）→ RegisterFlow 新 → 旧 TryUnload → 预热 → 报告
        /// P8.5b：返回结果文本（host-reload 工具 LLM 可见；Console 同步输出行为不变）
        /// </summary>
        /// <param name="args">cat + 空格 + dll 路径（dll 可选）</param>
        /// <returns>reload 结果文本</returns>
        private static string ExecuteReload(string args)
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
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
            // [段0] 忙时拒绝——任一会话工具批次执行中 reload 会导致旧批次完成信号永不置位（WaitForTools 空转帧上限）——host-* 直执路径豁免（ChatSession 工具批临时解除）
            if (_chatBridge.IsAnyToolBatchActive())
            {
                string busyMsg = "reload 拒绝: 工具批次执行中（Chat 处理中）——等待完成后再试";
                Console.WriteLine("[CMD] " + busyMsg);
                sb.AppendLine(busyMsg);
                return sb.ToString();
            }
            FlowHandle oldHandle;
            long oldId;
            string name;
            if (cat == "QuickCat")
            {
                oldHandle = _quickHandle;
                oldId = _quickId;
                name = "QuickCat";
            }
            else
            {
                // R0.2 工具组按 Flow 名寻址——Registry 动态许可已由 ExecHostReload 校验；此处查宿主句柄表
                name = cat;
                if (!_toolFlowHandles.TryGetValue(name, out oldHandle))
                {
                    string badMsg = "reload 目标无效——宿主句柄表中无该 Flow: " + name + "（host-flows 可查现状）";
                    Console.WriteLine("[CMD] " + badMsg);
                    sb.AppendLine(badMsg);
                    return sb.ToString();
                }
                if (!_toolFlowIds.TryGetValue(name, out oldId))
                {
                    oldId = -1;
                }
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
                string failMsg = "reload " + name + " 失败: 新版本加载未通过——" + ex.Message;
                Console.WriteLine("[CMD] " + failMsg);
                sb.AppendLine(failMsg);
                return sb.ToString();
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
                    for (int i = 0; i < WarmupFrames; i++)
                    {
                        _runner.Tick();
                    }
                }
                catch (Exception ex2)
                {
                    string rollbackFailMsg = "reload " + name + " 失败: 试跑异常且回滚失败——" + ex.Message + " / " + ex2.Message;
                    Console.WriteLine("[CMD] " + rollbackFailMsg);
                    sb.AppendLine(rollbackFailMsg);
                    return sb.ToString();
                }
                oldHandle.TryUnload(3);
                SetCatHandle(cat, rollback, rollbackId);
                string rollbackMsg = "reload " + name + " 失败: 试跑帧异常已回滚（旧版本全新实例 #" + rollbackId + "）——" + ex.Message;
                Console.WriteLine("[CMD] " + rollbackMsg);
                sb.AppendLine(rollbackMsg);
                return sb.ToString();
            }
            // [段3] 成功路径——换句柄 + 卸载旧 ALC + 预热帧
            oldHandle.TryUnload(3);
            SetCatHandle(cat, newHandle, newId);
            // 工具定义热同步——新版本 GetToolsJson 可能已变（改 tools.<组> 积木描述 → reload 生效；design-ch4-tools-pool §七）
            if (name != "QuickCat")
            {
                ToolPool.Clear();
                foreach (KeyValuePair<string, FlowHandle> kv in _toolFlowHandles)
                {
                    ToolPool.AddFromFlow(kv.Value.Flow);
                }
                if (_quickHandle != null)
                {
                    ToolPool.AddFromFlow(_quickHandle.Flow);
                }
                ToolPool.AddFromBuiltin(BuildBuiltinToolsJson());
                ToolRegistry.Init(ToolPool.BuildSpecs(), ToolPool.BuildOwnerFlowMap());
            }
            for (int i = 0; i < WarmupFrames; i++)
            {
                _runner.Tick();
            }
            string[] keyDic = _bus.GetKeyDic();
            string okMsg = "reload " + name + ": #" + oldId + " → #" + newId + " | pid=" + Environment.ProcessId + " | " + keyDic[0];
            Console.WriteLine("[CMD] " + okMsg);
            sb.AppendLine(okMsg);
            for (int k = 0; k < keyDic.Length; k++)
            {
                Console.WriteLine("[CMD]     " + keyDic[k]);
                sb.AppendLine("    " + keyDic[k]);
            }
            return sb.ToString();
        }

        /// <summary>
        /// host-reload 工具执行——解析 cat 参数（Flow 注册名）→ 动态校验（Registry.Entries）→ ExecuteReload（复用事务三段式）；宿主级工具——ExecuteToolBatch 白名单直执不走 OA
        /// </summary>
        /// <param name="argsJson">参数 JSON</param>
        /// <returns>reload 结果文本</returns>
        private static string ExecHostReload(string argsJson)
        {
            string cat = "";
            try
            {
                using (JsonDocument doc = JsonDocument.Parse(argsJson))
                {
                    JsonElement root = doc.RootElement;
                    JsonElement c;
                    if (root.TryGetProperty("cat", out c) && c.ValueKind == JsonValueKind.String)
                    {
                        string got = c.GetString();
                        if (got != null)
                        {
                            cat = got;
                        }
                    }
                }
            }
            catch (Exception)
            {
                cat = "";
            }
            if (cat.Length == 0)
            {
                return "ERR|BAD_ARG|host-reload cat 参数缺失——须为已加载 Flow 注册名（host-flows 可查当前清单）";
            }
            // 动态许可——Registry.Entries 为真相源（新增工具组 Flow 零宿主改动；dev 等死目标天然拒绝）
            bool found = false;
            FlowEntry[] entries = _runner.Registry.Entries;
            for (int i = 0; i < entries.Length; i = i + 1)
            {
                if (string.Equals(entries[i].Name, cat, StringComparison.Ordinal))
                {
                    found = true;
                    break;
                }
            }
            if (!found)
            {
                StringBuilder avail = new StringBuilder();
                for (int i = 0; i < entries.Length; i = i + 1)
                {
                    if (i > 0)
                    {
                        avail.Append(" / ");
                    }
                    avail.Append(entries[i].Name);
                }
                return "ERR|BAD_ARG|host-reload cat 须为已加载 Flow 注册名（当前: " + avail.ToString() + "）——host-flows 可查";
            }
            return ExecuteReload(cat);
        }

        /// <summary>
        /// host-flows 工具执行——查看当前运行 Flow 现状（Registry 动态面：Id/Name/Kind + 宿主句柄状态 + dll）；宿主级直执
        /// </summary>
        /// <param name="argsJson">参数 JSON（无参）</param>
        /// <returns>Flow 现状文本</returns>
        private static string ExecHostFlows(string argsJson)
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            FlowEntry[] entries = _runner.Registry.Entries;
            sb.Append("Flow 运行现状（" + entries.Length.ToString() + " 个）:");
            for (int i = 0; i < entries.Length; i = i + 1)
            {
                FlowEntry entry = entries[i];
                string state;
                string dll = "";
                if (string.Equals(entry.Name, "QuickCat", StringComparison.Ordinal))
                {
                    state = _quickHandle != null ? "alive" : "missing";
                    if (_quickHandle != null)
                    {
                        dll = _quickHandle.SourceDll;
                    }
                }
                else
                {
                    FlowHandle handle;
                    if (_toolFlowHandles.TryGetValue(entry.Name, out handle))
                    {
                        state = "alive";
                        dll = handle.SourceDll;
                    }
                    else
                    {
                        state = "no-handle";
                    }
                }
                sb.Append(System.Environment.NewLine);
                sb.Append("  #" + entry.Id.ToString() + " " + entry.Name + " kind=" + (entry.Kind != null ? entry.Kind : "") + " " + state + (dll.Length > 0 ? " | " + dll : ""));
            }
            return sb.ToString();
        }

        /// <summary>
        /// 更新指定 Flow 的句柄 + 注册 ID 字段——QuickCat 独立字段；工具组按 Flow 名写字典（R0.2）
        /// </summary>
        /// <param name="cat">Flow 注册名（QuickCat/TextCat/MauCat/CsCat/ConfigCat/...）</param>
        /// <param name="handle">新句柄</param>
        /// <param name="id">新注册 ID</param>
        private static void SetCatHandle(string cat, FlowHandle handle, long id)
        {
            if (cat == "QuickCat")
            {
                _quickHandle = handle;
                _quickId = id;
                // 观测面句柄同步——reload 后旧快照访问已 dispose 句柄崩溃（ObjectDisposedException 判例 2026-09-08）
                CatHome4.Observe.ObserveService.UpdateQuickHandle(handle, id);
                return;
            }
            _toolFlowHandles[cat] = handle;
            _toolFlowIds[cat] = id;
        }
    }
}