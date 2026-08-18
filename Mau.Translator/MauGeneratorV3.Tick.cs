using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Mau.Translator
{
    /// <summary>
    /// MauGeneratorV3 Tick/槽/盒子判真面分部——Tick 驱动/CmdPump/Inbox 回投/GetStatus/槽/BoxTrue。
    /// P7b partial 拆分——自 MauGeneratorV3.cs 原样搬移，逻辑零改动。
    /// </summary>
    public static partial class MauGeneratorV3
    {
        /// <summary>
        /// Tick 驱动——存帧 + 主动传感器采样 → 导线执行
        /// </summary>
        /// <param name="sb">输出缓冲</param>
        /// <param name="doc">IR</param>
        private static void AppendTick(StringBuilder sb, MauDocV3 doc)
        {
            sb.AppendLine("        public void Tick(int frame)");
            sb.AppendLine("        {");
            sb.AppendLine("            _frame = frame;");
            bool hasCmd = false;
            for (int i = 0; i < doc.Sensors.Count; i++)
            {
                if (doc.Sensors[i].IsCmd)
                {
                    hasCmd = true;
                    break;
                }
            }
            if (hasCmd)
            {
                sb.AppendLine("            CmdPump();");
            }
            sb.AppendLine("            TickInboxes(frame);");
            sb.AppendLine("            TickWires(frame);");
            sb.AppendLine("        }");
            sb.AppendLine("");
            AppendTickInboxes(sb, doc);
            if (hasCmd)
            {
                AppendCommandPump(sb, doc);
            }
            // 主动壳接口实现——ISensorLoop.TickSensors（宿主协程列表驱动，与 Flow.Tick 分离）
            bool hasActive = false;
            for (int i = 0; i < doc.Sensors.Count; i++)
            {
                if (!doc.Sensors[i].Passive)
                {
                    hasActive = true;
                    break;
                }
            }
            sb.AppendLine("        public void TickSensors(int frame)");
            sb.AppendLine("        {");
            if (hasActive)
            {
                for (int i = 0; i < doc.Sensors.Count; i++)
                {
                    if (!doc.Sensors[i].Passive)
                    {
                        sb.AppendLine("            " + NameBodyPascal(doc.Sensors[i].Name) + "_SensorExec(frame);");
                    }
                }
            }
            sb.AppendLine("        }");
            sb.AppendLine("");
            sb.AppendLine("        private void TickWires(int frame)");
            sb.AppendLine("        {");
            for (int w = 0; w < doc.Wires.Count; w++)
            {
                sb.AppendLine("            " + NamePascal(doc.Wires[w].Name) + "_Execute(frame);");
            }
            sb.AppendLine("        }");
        }
/// <summary>
/// Command 泵生成——懒注册 CommandBus + 每帧拉邮件 → 传感器置沿 + CmdTexts 落全局盒。
/// Command = 唯一外部输入总线：宿主演化投递（SetText），生成物 Tick 内拉取（主线程契约）。
/// payload 落盒 key 名 = Command key（全局盒 "global"）——语料经 key 裸词引用。
/// </summary>
/// <param name = "sb">输出缓冲</param>
/// <param name = "doc">IR</param>
private static void AppendCommandPump(StringBuilder sb, MauDocV3 doc)
{
    // [段1] 字段——懒注册标记（构造期 FlowId 未设置——首次 Tick 注册）
    sb.AppendLine("        private bool _cmdRegistered;");
    sb.AppendLine("");
    sb.AppendLine("        private void CmdPump()");
    sb.AppendLine("        {");
    // [段2] 懒注册——TryResolve 失败静默（宿主未绑 CommandBus = 无外部输入）
    sb.AppendLine("            if (!_cmdRegistered)");
    sb.AppendLine("            {");
    sb.AppendLine("                ICommandBus bus;");
    sb.AppendLine("                DataBox.TryResolve<ICommandBus>(out bus);");
    sb.AppendLine("                if (bus == null)");
    sb.AppendLine("                {");
    sb.AppendLine("                    return;");
    sb.AppendLine("                }");
    StringBuilder keys = new StringBuilder();
    int cmdCount = 0;
    for (int s = 0; s < doc.Sensors.Count; s++)
    {
        if (doc.Sensors[s].IsCmd)
        {
            if (cmdCount > 0)
            {
                keys.Append(", ");
            }
            keys.Append("\"");
            keys.Append(doc.Sensors[s].CmdKey);
            keys.Append("\"");
            cmdCount = cmdCount + 1;
        }
    }
    sb.AppendLine("                bus.Register(FlowContext.CurrentFlowId, new string[] { " + keys.ToString() + " });");
    sb.AppendLine("                _cmdRegistered = true;");
    sb.AppendLine("            }");
    // [段3] 拉邮件——无新指令即空转
    sb.AppendLine("            ICommandBus cmd;");
    sb.AppendLine("            DataBox.TryResolve<ICommandBus>(out cmd);");
    sb.AppendLine("            if (cmd == null)");
    sb.AppendLine("            {");
    sb.AppendLine("                return;");
    sb.AppendLine("            }");
    sb.AppendLine("            CommandPack email = cmd.GetCommandEmail(FlowContext.CurrentFlowId);");
    sb.AppendLine("            if (!email.HasCommands)");
    sb.AppendLine("            {");
    sb.AppendLine("                return;");
    sb.AppendLine("            }");
    // [段4] 逐 key 匹配传感器——有 payload 才置沿 + 落全局盒（模板邮件含全部注册 key——无 payload 的 key 不触发）
    sb.AppendLine("            for (int i = 0; i < email.CmdKeys.Length; i = i + 1)");
    sb.AppendLine("            {");
    sb.AppendLine("                string key = email.CmdKeys[i];");
    sb.AppendLine("                if (email.CmdTexts != null && i < email.CmdTexts.Length && email.CmdTexts[i] != null)");
    sb.AppendLine("                {");
    for (int s = 0; s < doc.Sensors.Count; s++)
    {
        if (!doc.Sensors[s].IsCmd)
        {
            continue;
        }
        if (s == 0)
        {
            sb.AppendLine("                    if (key == \"" + doc.Sensors[s].CmdKey + "\")");
        }
        else
        {
            sb.AppendLine("                    else if (key == \"" + doc.Sensors[s].CmdKey + "\")");
        }
        sb.AppendLine("                    {");
        sb.AppendLine("                        DataBox.Signal(\"" + doc.Sensors[s].Name + "\");");
        sb.AppendLine("                    }");
    }
    sb.AppendLine("                    DataBox.Set<string>(\"global\", key, email.CmdTexts[i]);");
    sb.AppendLine("                }");
    sb.AppendLine("            }");
    sb.AppendLine("        }");
    sb.AppendLine("");
}
        /// <summary>
        /// GetStatus 生成——四柱快照组装（P3 观测支柱：状态机枚举值/主动传感器实测/槽余量/导线状态）
        /// </summary>
        /// <param name="sb">输出缓冲</param>
        /// <param name="doc">IR</param>
        private static void AppendGetStatus(StringBuilder sb, MauDocV3 doc)
        {
            sb.AppendLine("        public FlowStatusV3 GetStatus()");
            sb.AppendLine("        {");
            sb.AppendLine("            FlowStatusV3 s = new FlowStatusV3();");
            sb.AppendLine("            s.Frame = _frame;");
            // [段1] 状态机枚举值——"S_X=Y" 每机一行
            sb.AppendLine("            s.StateLines = new string[]");
            sb.AppendLine("            {");
            for (int i = 0; i < doc.StateMachines.Count; i++)
            {
                StateMachineDefV3 sm = doc.StateMachines[i];
                string line = "\"" + sm.Name + "=\" + " + NameField(sm.Name) + ".ToString()";
                string sep = "";
                if (i + 1 < doc.StateMachines.Count)
                {
                    sep = ",";
                }
                sb.AppendLine("                " + line + sep);
            }
            sb.AppendLine("            };");
            // [段2] 传感器实测——主动壳产物在 DataBox（sys.box 直读），快照不重复承载
            sb.AppendLine("            s.SensorValues = new SignalValueV3[]");
            sb.AppendLine("            {");
            sb.AppendLine("            };");
            // [段3] 槽余量
            sb.AppendLine("            s.SlotLevels = new SlotValueV3[]");
            sb.AppendLine("            {");
            for (int i = 0; i < doc.Slots.Count; i++)
            {
                SlotDefV3 slot = doc.Slots[i];
                sb.AppendLine("                new SlotValueV3() { Name = \"" + slot.Name + "\", Available = " + NameField(slot.Name) + ", Capacity = " + slot.Capacity + " },");
            }
            sb.AppendLine("            };");
            // [段4] 导线状态
            sb.AppendLine("            s.WireStatuses = new WireStatusV3[]");
            sb.AppendLine("            {");
            for (int w = 0; w < doc.Wires.Count; w++)
            {
                WireDefV3 wire = doc.Wires[w];
                string field = NameField(wire.Name);
                if (wire.Parallel)
                {
                    sb.AppendLine("                new WireStatusV3() { Name = \"" + wire.Name + "\", Busy = " + field + "_busy, LastTriggerFrame = " + field + "_frame, TimedOut = " + field + "_timedout },");
                }
                else
                {
                    sb.AppendLine("                new WireStatusV3() { Name = \"" + wire.Name + "\", Busy = false, LastTriggerFrame = " + field + "_frame, TimedOut = false },");
                }
            }
            sb.AppendLine("            };");
            sb.AppendLine("            return s;");
            sb.AppendLine("        }");
        }

        /// <summary>
        /// TickInboxes 生成——par 导线后台结果应用（主线程 Drain）：时限推进 + 超时丢弃 + 结果转移
        /// </summary>
        /// <param name="sb">输出缓冲</param>
        /// <param name="doc">IR</param>
        private static void AppendTickInboxes(StringBuilder sb, MauDocV3 doc)
{
            bool anyParallel = false;
            for (int w = 0; w < doc.Wires.Count; w++)
            {
                if (doc.Wires[w].Parallel)
                {
                    anyParallel = true;
                    break;
                }
            }
            if (!anyParallel)
            {
                sb.AppendLine("        private void TickInboxes(int frame)");
                sb.AppendLine("        {");
                sb.AppendLine("        }");
                sb.AppendLine("");
                return;
            }
            sb.AppendLine("        private void TickInboxes(int frame)");
            sb.AppendLine("        {");
            for (int w = 0; w < doc.Wires.Count; w++)
            {
                WireDefV3 wire = doc.Wires[w];
                if (!wire.Parallel)
                {
                    continue;
                }
                string field = NameField(wire.Name);
                string pascal = NamePascal(wire.Name);
                // [段1] 时限推进——在途的 Cube 每帧步进
                if (wire.Timeout > 0)
                {
                    sb.AppendLine("            if (" + field + "_busy)");
                    sb.AppendLine("            {");
                    sb.AppendLine("                " + field + "_cube.TickFrame();");
                    sb.AppendLine("            }");
                }
                // [段2] 回投应用——主线程 Drain（捕获导线走 BrickCaptureV3 载荷）
                if (wire.CaptureTarget.Length > 0)
                {
                    string captureType = CaptureCSType(wire.BrickName);
                    sb.AppendLine("            " + field + "_inbox.Drain(delegate (BrickCaptureV3<" + captureType + "> cap)");
                    sb.AppendLine("            {");
                    sb.AppendLine("                bool ok = cap.Ok;");
                    sb.AppendLine("                DataBox.Set<" + captureType + ">(" + BoxScopeExpr(wire.CaptureTarget) + ", \"" + BoxKey(wire.CaptureTarget) + "\", cap.Value);");
                }
                else
                {
                    sb.AppendLine("            " + field + "_inbox.Drain(delegate (bool ok)");
                    sb.AppendLine("            {");
                }
                sb.AppendLine("                " + field + "_busy = false;");
                sb.AppendLine("                bool timedOut = false;");
                if (wire.Timeout > 0)
                {
                    sb.AppendLine("                timedOut = " + field + "_cube.IsExpired();");
                    sb.AppendLine("                " + field + "_cube.Complete();");
                }
                sb.AppendLine("                if (timedOut)");
                sb.AppendLine("                {");
                sb.AppendLine("                    " + field + "_timedout = true;");
                sb.AppendLine("                    AuditStore.Default?.Record(\"Flow\", \"trace.timeout\", -1, new AuditProp[] { new AuditProp(\"wire\", \"" + wire.Name + "\"), new AuditProp(\"frame\", frame.ToString()) });");
                // 超时归宿——多结果导线走第二支（失败侧），单结果保持原归宿
                ResultV3 timeoutResult = wire.Results[0];
                if (wire.Results.Count > 1)
                {
                    timeoutResult = wire.Results[1];
                }
                AppendAssign(sb, timeoutResult);
                sb.AppendLine("                }");
                sb.AppendLine("                else");
                sb.AppendLine("                {");
                AppendResultBody(sb, wire, "ok", "frame");
                sb.AppendLine("                }");
                sb.AppendLine("            });");
            }
            sb.AppendLine("        }");
            sb.AppendLine("");
        }

        /// <summary>
        /// 槽生成——计数字段（初始=容量）+ TryAcquire/Release
        /// </summary>
        /// <param name="sb">输出缓冲</param>
        /// <param name="doc">IR</param>
        private static void AppendSlots(StringBuilder sb, MauDocV3 doc)
        {
            for (int i = 0; i < doc.Slots.Count; i++)
            {
                SlotDefV3 slot = doc.Slots[i];
                string pascal = NameBodyPascal(slot.Name);
                sb.AppendLine("        // ── 槽 " + slot.Name + "（容量 " + slot.Capacity + "——初始值 = 容量）──");
                sb.AppendLine("        private long " + NameField(slot.Name) + " = " + slot.Capacity + ";");
                sb.AppendLine("        public bool TryAcquire" + pascal + "()");
                sb.AppendLine("        {");
                sb.AppendLine("            if (" + NameField(slot.Name) + " > 0)");
                sb.AppendLine("            {");
                sb.AppendLine("                " + NameField(slot.Name) + " = " + NameField(slot.Name) + " - 1;");
                sb.AppendLine("                return true;");
                sb.AppendLine("            }");
                sb.AppendLine("            return false;");
                sb.AppendLine("        }");
                sb.AppendLine("        public void Release" + pascal + "()");
                sb.AppendLine("        {");
                sb.AppendLine("            " + NameField(slot.Name) + " = " + NameField(slot.Name) + " + 1;");
                sb.AppendLine("        }");
                sb.AppendLine("");
            }
        }
        /// <summary>
        /// 盒子判真方法生成——导线条件引用的盒子（@key 私有 / key 全局）：类型化读 + 判真
        /// </summary>
        /// <param name="sb">输出缓冲</param>
        /// <param name="doc">IR</param>
        private static void AppendBoxTruthMethods(StringBuilder sb, MauDocV3 doc)
        {
            Dictionary<string, string> boxTypes = GenBoxTypes(doc);
            HashSet<string> used = new HashSet<string>(StringComparer.Ordinal);
            for (int w = 0; w < doc.Wires.Count; w++)
            {
                WireDefV3 wire = doc.Wires[w];
                for (int c = 0; c < wire.Conditions.Count; c++)
                {
                    if (wire.Conditions[c].IsBoxAssert && !used.Contains(wire.Conditions[c].BoxName))
                    {
                        used.Add(wire.Conditions[c].BoxName);
                    }
                }
            }
            foreach (string box in used)
            {
                string boxType = "bool";
                if (boxTypes.ContainsKey(box))
                {
                    boxType = boxTypes[box];
                }
                string pascal = NameBodyPascal(box);
                sb.AppendLine("        private bool BoxTrue_" + pascal + "()");
                sb.AppendLine("        {");
                if (boxType == "string")
                {
                    sb.AppendLine("            string v = \"\";");
                    sb.AppendLine("            if (DataBox.TryGet<string>(" + BoxScopeExpr(box) + ", \"" + BoxKey(box) + "\", out v)) { return v.Length > 0; }");
                }
                else if (boxType == "bool")
                {
                    sb.AppendLine("            bool v = false;");
                    sb.AppendLine("            if (DataBox.TryGet<bool>(" + BoxScopeExpr(box) + ", \"" + BoxKey(box) + "\", out v)) { return v; }");
                }
                else
                {
                    sb.AppendLine("            " + boxType + " v = 0;");
                    sb.AppendLine("            if (DataBox.TryGet<" + boxType + ">(" + BoxScopeExpr(box) + ", \"" + BoxKey(box) + "\", out v)) { return v != 0; }");
                }
                sb.AppendLine("            return false;");
                sb.AppendLine("        }");
            }
        }
    }
}