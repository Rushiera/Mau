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
    /// Program 热重载面分部——reload 事务（忙时拒绝/加载/换注册/试跑回滚/成功换柄）。
    /// P7b partial 拆分——自 Program.cs 原样搬移，逻辑零改动。
    /// </summary>
    public static partial class Program
    {
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
                    for (int i = 0; i < WarmupFrames; i++)
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
            for (int i = 0; i < WarmupFrames; i++)
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
    }
}