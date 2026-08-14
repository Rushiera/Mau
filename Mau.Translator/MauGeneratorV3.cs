using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Mau.Translator
{
    /// <summary>
    /// 生成器 v3——FSM 网络 IR → C# 生成物（design-mau-v3 §八.6）。生成形态：状态机枚举单值 + IsXxx/GetState；被动传感器消费字段 + FireXxx；主动传感器帧门控采样；导线条件原子检查（全过才消费）+ 动作 try-catch + 结果分叉；槽计数字段 + TryAcquire/Release。积木强类型直调（P5 协议 A——内嵌积木源 + 编译期验型）；par 后台 Task.Run + Inbox 回投；[t=] Cube 时限。限制：导线结果 >2 的多路分叉报 E205（名称返回积木体系随积木重生扩展）
/// </summary>
    public static class MauGeneratorV3
    {
        /// <summary>
        /// 生成 C# 产物——IR → 源码文本
        /// </summary>
        /// <param name="doc">FSM 网络 IR</param>
        /// <param name="flowName">流程名（PascalCase——类名 FL_ 前缀）</param>
        /// <returns>生成结果</returns>
        public static GenerateResultV3 Generate(MauDocV3 doc, string flowName)
        {
            GenerateResultV3 result = new GenerateResultV3();
            if (doc == null)
            {
                result.Diagnostics.Add(new MauDiagnostic("E205", 1, "空 IR——无生成对象"));
                result.Success = false;
                return result;
            }
            // [段1] 多路分叉检查——结果 >2 待名称返回积木体系（P5）
            for (int w = 0; w < doc.Wires.Count; w++)
            {
                if (doc.Wires[w].Results.Count > 2)
                {
                    result.Diagnostics.Add(new MauDiagnostic("E205", doc.Wires[w].Line, "导线 '" + doc.Wires[w].Name + "' 结果 " + doc.Wires[w].Results.Count + " 路——多路分叉待名称返回积木体系（P5 积木重生轮）"));
                }
            }
            if (result.Diagnostics.Count > 0)
            {
                result.Success = false;
                return result;
            }
            StringBuilder sb = new StringBuilder();
            AppendHeader(sb, doc, flowName);
            AppendStateMachines(sb, doc);
            AppendSensors(sb, doc);
            AppendSlots(sb, doc);
            AppendWires(sb, doc);
            AppendTick(sb, doc);
            AppendGetStatus(sb, doc);
            sb.AppendLine("    }");
            sb.AppendLine("}");
            AppendBrickSources(sb, doc);
            result.Code = sb.ToString();
            result.Success = true;
            return result;
        }

        /// <summary>
        /// 文件头——生成来源与四柱统计
        /// </summary>
        /// <param name="sb">输出缓冲</param>
        /// <param name="doc">IR</param>
        /// <param name="flowName">流程名</param>
        private static void AppendHeader(StringBuilder sb, MauDocV3 doc, string flowName)
        {
            sb.AppendLine("// 生成: Mau v3.0 | 源: " + flowName + " | 外观: v3-default");
            sb.AppendLine("// 四柱: 状态机 " + doc.StateMachines.Count + " / 传感器 " + doc.Sensors.Count
                + " / 导线 " + doc.Wires.Count + " / 槽 " + doc.Slots.Count);
            sb.AppendLine("using System;");
            sb.AppendLine("using System.Collections.Generic;");
            sb.AppendLine("using Mau.Runtime;");
            sb.AppendLine("using System.Text;");
            sb.AppendLine("using System.Threading;");
            sb.AppendLine("");
            sb.AppendLine("namespace Mau.Generated");
            sb.AppendLine("{");
            sb.AppendLine("    public sealed class FL_" + flowName + " : IObservableFlow, ISensorLoop");
            sb.AppendLine("    {");
            sb.AppendLine("        // ── 观测字段（P3——GetStatus 快照源）──");
            sb.AppendLine("        private long _frame;");
            // [段1b] 构造——被动传感器注册进 DataBox 事件层（内部交互总线）
            bool hasPassive = false;
            for (int s = 0; s < doc.Sensors.Count; s++)
            {
                if (doc.Sensors[s].Passive)
                {
                    hasPassive = true;
                    break;
                }
            }
            if (hasPassive)
            {
                sb.AppendLine("        public FL_" + flowName + "()");
                sb.AppendLine("        {");
                for (int s = 0; s < doc.Sensors.Count; s++)
                {
                    if (doc.Sensors[s].Passive)
                    {
                        sb.AppendLine("            DataBox.RegisterSignal(\"" + doc.Sensors[s].Name + "\");");
                    }
                }
                sb.AppendLine("        }");
                sb.AppendLine("");
            }
        }

        /// <summary>
        /// 状态机生成——枚举 + 单值字段 + IsXxx + GetState
        /// </summary>
        /// <param name="sb">输出缓冲</param>
        /// <param name="doc">IR</param>
        private static void AppendStateMachines(StringBuilder sb, MauDocV3 doc)
{
            for (int s = 0; s < doc.StateMachines.Count; s++)
            {
                StateMachineDefV3 sm = doc.StateMachines[s];
                string pascal = NamePascal(sm.Name);
                sb.AppendLine("        // ── 状态机 " + sm.Name + " ──");
                sb.AppendLine("        public enum " + pascal + "State");
                sb.AppendLine("        {");
                for (int i = 0; i < sm.States.Count; i++)
                {
                    string sep = "";
                    if (i + 1 < sm.States.Count)
                    {
                        sep = ",";
                    }
                    sb.AppendLine("            " + NamePascal(sm.States[i]) + sep);
                }
                sb.AppendLine("        }");
                sb.AppendLine("        private " + pascal + "State " + NameField(sm.Name) + " = " + pascal + "State." + NamePascal(sm.States[0]) + ";");
                for (int i = 0; i < sm.States.Count; i++)
                {
                    string statePascal = NamePascal(sm.States[i]);
                    sb.AppendLine("        public bool Is" + statePascal + "() { return " + NameField(sm.Name) + " == " + pascal + "State." + statePascal + "; }");
                }
                sb.AppendLine("        public string GetState_" + NamePascal(sm.Name) + "() { return " + NameField(sm.Name) + ".ToString(); }");
                sb.AppendLine("");
            }
        }
        /// <summary>
        /// 传感器生成——被动消费字段 + FireXxx；主动帧门控采样
        /// </summary>
        /// <param name="sb">输出缓冲</param>
        /// <param name="doc">IR</param>
        private static void AppendSensors(StringBuilder sb, MauDocV3 doc)
        {
            for (int i = 0; i < doc.Sensors.Count; i++)
            {
                SensorDefV3 sensor = doc.Sensors[i];
                string pascal = NameBodyPascal(sensor.Name);
                if (sensor.Passive)
                {
                    // 被动传感器——DataBox 事件层（内部交互总线）：无本地字段，沿 = 注册信号
                    sb.AppendLine("        // ── 传感器 " + sensor.Name + "（被动——DataBox 事件沿，消费即清）──");
                    sb.AppendLine("        public bool IsSet_" + pascal + "() { return DataBox.TryPeek(\"" + sensor.Name + "\"); }");
                }
                else
                {
                    // 主动传感器壳——独立协程执行方法（帧门控 + 探测 + 捕获落盒 + 分支动作）
                    AppendSensorShell(sb, sensor, doc);
                }
                sb.AppendLine("");
            }
        }
/// <summary>
/// 盒子 Key 去 @ 前缀——@key → key（scope 由 BoxScopeExpr 决定）
/// </summary>
        /// <param name="boxRef">盒子引用原文</param>
        /// <returns>Key 原文</returns>
        private static string BoxKey(string boxRef)
        {
            if (boxRef.Length > 0 && boxRef[0] == '@')
            {
                return boxRef.Substring(1);
            }
            return boxRef;
        }/// <summary>
/// 盒子 scope 表达式——@key → FlowId 私有；key → "global"
/// </summary>
        /// <param name="boxRef">盒子引用原文</param>
        /// <returns>C# scope 表达式文本</returns>
        private static string BoxScopeExpr(string boxRef)
        {
            if (boxRef.Length > 0 && boxRef[0] == '@')
            {
                return "FlowContext.CurrentFlowId.ToString()";
            }
            return "\"global\"";
        }/// <summary>
/// 捕获写 C# 类型——按积木 out 端口契约
/// </summary>
        /// <param name="brickName">写源积木</param>
        /// <returns>C# 类型文本</returns>
        private static string CaptureCSType(string brickName)
        {
            BrickIndexEntry entry;
            if (BrickIndex.TryFind(brickName, out entry) && entry.OutputTypes.Count > 0)
            {
                return entry.OutputTypes[0];
            }
            return "string";
        }/// <summary>
/// 壳分支动作生成——条件包裹的积木调用序列（汇总写）
/// </summary>
        /// <param name="sb">输出缓冲</param>
        /// <param name="actions">动作列表</param>
        /// <param name="guard">条件守卫（"if (ok)" / "if (!ok)"）</param>
        /// <param name="doc">IR（盒子引用查询）</param>
        private static void AppendShellActions(StringBuilder sb, List<SensorActionV3> actions, string guard, MauDocV3 doc)
        {
            if (actions.Count == 0)
            {
                return;
            }
            sb.AppendLine("            " + guard);
            sb.AppendLine("            {");
            for (int i = 0; i < actions.Count; i++)
            {
                StringBuilder prelude = new StringBuilder();
                string expr = BrickCallExpr(actions[i].BrickName, actions[i].BrickArgs, "", doc, prelude);
                sb.Append(prelude);
                sb.AppendLine("                " + expr + ";");
            }
            sb.AppendLine("            }");
        }/// <summary>
/// 主动传感器壳方法生成——帧门控 + 探测（可捕获落盒）+ 分支动作（只读世界零状态转移）
/// </summary>
        /// <param name="sb">输出缓冲</param>
        /// <param name="sensor">传感器声明</param>
        /// <param name="doc">IR</param>
        private static void AppendSensorShell(StringBuilder sb, SensorDefV3 sensor, MauDocV3 doc)
        {
            string field = NameField(sensor.Name);
            string pascal = NameBodyPascal(sensor.Name);
            sb.AppendLine("        // ── 传感器 " + sensor.Name + "（主动壳——每 " + sensor.EveryFrames + " 帧探测）──");
            sb.AppendLine("        private long " + field + "_frame;");
            sb.AppendLine("        public void " + pascal + "_SensorExec(int frame)");
            sb.AppendLine("        {");
            if (sensor.EveryFrames > 0)
            {
                sb.AppendLine("            if ((frame - " + field + "_frame) < " + sensor.EveryFrames + ") { return; }");
                sb.AppendLine("            " + field + "_frame = frame;");
            }
            sb.AppendLine("            bool ok = false;");
            sb.AppendLine("            try");
            sb.AppendLine("            {");
            StringBuilder prelude = new StringBuilder();
            string expr = BrickCallExpr(sensor.BrickName, sensor.BrickArgs, "", doc, prelude);
            sb.Append(prelude);
            sb.AppendLine("                ok = " + expr + ";");
            if (sensor.CaptureTarget.Length > 0)
            {
                // 探测落盒——bool 返回值（探测积木 = 判断语义）落 DataBox 位置
                sb.AppendLine("                DataBox.Set<bool>(" + BoxScopeExpr(sensor.CaptureTarget) + ", \"" + BoxKey(sensor.CaptureTarget) + "\", ok);");
            }
            sb.AppendLine("            }");
            sb.AppendLine("            catch (Exception ex)");
            sb.AppendLine("            {");
            sb.AppendLine("                ok = false;");
            sb.AppendLine("            }");
            sb.AppendLine("            AuditStore.Default?.Record(\"Flow\", \"trace.sample\", -1, new AuditProp[] { new AuditProp(\"sensor\", \"" + sensor.Name + "\"), new AuditProp(\"value\", ok ? \"1\" : \"0\"), new AuditProp(\"frame\", frame.ToString()) });");
            // 分支动作——成功侧/失败侧
            AppendShellActions(sb, sensor.TrueActions, "if (ok)", doc);
            AppendShellActions(sb, sensor.FalseActions, "if (!ok)", doc);
            sb.AppendLine("        }");
        }/// <summary>
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
        }        /// <summary>
        /// 导线生成——条件检查 + 消费 + 执行 + 结果分叉 + 状态字段（观测）
        /// </summary>
        /// <param name="sb">输出缓冲</param>
        /// <param name="doc">IR</param>
        private static void AppendWires(StringBuilder sb, MauDocV3 doc)
{
            // [段0] 捕获结构体——任意 par+捕获导线存在时生成（后台回投载荷）
            bool anyCapturePar = false;
            for (int w = 0; w < doc.Wires.Count; w++)
            {
                if (doc.Wires[w].Parallel && doc.Wires[w].CaptureTarget.Length > 0)
                {
                    anyCapturePar = true;
                    break;
                }
            }
            if (anyCapturePar)
            {
                sb.AppendLine("        private struct BrickCaptureV3<T>");
                sb.AppendLine("        {");
                sb.AppendLine("            public bool Ok;");
                sb.AppendLine("            public T Value;");
                sb.AppendLine("        }");
            }
            AppendBoxTruthMethods(sb, doc);
            for (int w = 0; w < doc.Wires.Count; w++)
            {
                WireDefV3 wire = doc.Wires[w];
                string pascal = NamePascal(wire.Name);
                string attrs = "";
                if (wire.Parallel)
                {
                    attrs = attrs + " [par]";
                }
                if (wire.Timeout > 0)
                {
                    attrs = attrs + " [t=" + wire.Timeout + "]";
                }
                sb.AppendLine("        // ── 导线 " + wire.Name + " ──" + attrs);
                if (wire.Timeout > 0)
                {
                    sb.AppendLine("        private Cube " + NameField(wire.Name) + "_cube = new Cube(" + wire.Timeout + ");");
                }
                // 观测字段——GetStatus 快照源（P3）
                sb.AppendLine("        private long " + NameField(wire.Name) + "_frame;");
                sb.AppendLine("        private bool " + NameField(wire.Name) + "_busy;");
                sb.AppendLine("        private bool " + NameField(wire.Name) + "_timedout;");
                if (wire.Parallel)
                {
                    if (wire.CaptureTarget.Length > 0)
                    {
                        string captureType = CaptureCSType(wire.BrickName);
                        sb.AppendLine("        private readonly Inbox<BrickCaptureV3<" + captureType + ">> " + NameField(wire.Name) + "_inbox = new Inbox<BrickCaptureV3<" + captureType + ">>();");
                    }
                    else
                    {
                        sb.AppendLine("        private readonly Inbox<bool> " + NameField(wire.Name) + "_inbox = new Inbox<bool>();");
                    }
                }
                AppendWireCondition(sb, wire, pascal, doc);
                AppendWireConsume(sb, wire, pascal, doc);
                AppendWireExecute(sb, wire, pascal, doc);
                sb.AppendLine("");
            }
        }
        /// <summary>
        /// 导线条件方法——原子检查（不消费——全过才消费）。
        /// 被动传感器 = DataBox 事件沿预检（TryPeek）；主动传感器 = 实测值字段读（反复查询不消费）。
        /// </summary>
        /// <param name="sb">输出缓冲</param>
        /// <param name="wire">导线</param>
        /// <param name="pascal">导线 PascalCase 名</param>
        /// <param name="doc">IR（查传感器形态）</param>
        private static void AppendWireCondition(StringBuilder sb, WireDefV3 wire, string pascal, MauDocV3 doc)
        {
            sb.AppendLine("        private bool " + pascal + "_Condition()");
            sb.AppendLine("        {");
            for (int c = 0; c < wire.Conditions.Count; c++)
            {
                ConditionV3 cond = wire.Conditions[c];
                if (cond.IsStateAssert)
                {
                    sb.AppendLine("            if (" + NameField(cond.StateName) + " != " + NamePascal(cond.StateName) + "State." + NamePascal(cond.StateValue) + ") { return false; }");
                }
                else if (cond.IsBoxAssert)
                {
                    // 盒子判真——类型化读 + 判真（bool 直取 / string 非空 / 数值非零）
                    string boxType = "bool";
                    Dictionary<string, string> boxTypes = GenBoxTypes(doc);
                    if (boxTypes.ContainsKey(cond.BoxName))
                    {
                        boxType = boxTypes[cond.BoxName];
                    }
                    sb.AppendLine("            if (!BoxTrue_" + NameBodyPascal(cond.BoxName) + "()) { return false; }");
                }
                else if (FindSensor(doc, cond.SensorName).Passive)
                {
                    sb.AppendLine("            if (!DataBox.TryPeek(\"" + cond.SensorName + "\")) { return false; }");
                }
                else
                {
                    sb.AppendLine("            if (!IsSet_" + NameBodyPascal(cond.SensorName) + "()) { return false; }");
                }
            }
            sb.AppendLine("            return true;");
            sb.AppendLine("        }");
        }        /// <summary>
        /// 导线消费方法——条件全过后消费被动传感器事件沿（主动传感器实测值不消费）
        /// </summary>
        /// <param name="sb">输出缓冲</param>
        /// <param name="wire">导线</param>
        /// <param name="pascal">导线 PascalCase 名</param>
        /// <param name="doc">IR（查传感器形态）</param>
        private static void AppendWireConsume(StringBuilder sb, WireDefV3 wire, string pascal, MauDocV3 doc)
        {
            sb.AppendLine("        private void " + pascal + "_Consume()");
            sb.AppendLine("        {");
            for (int c = 0; c < wire.Conditions.Count; c++)
            {
                if (!wire.Conditions[c].IsStateAssert && FindSensor(doc, wire.Conditions[c].SensorName).Passive)
                {
                    sb.AppendLine("            DataBox.TryPoll(\"" + wire.Conditions[c].SensorName + "\");");
                }
            }
            sb.AppendLine("        }");
        }

        /// <summary>
        /// 按名查传感器声明
        /// </summary>
        /// <param name="doc">IR</param>
        /// <param name="name">传感器名</param>
        /// <returns>传感器声明</returns>
        private static SensorDefV3 FindSensor(MauDocV3 doc, string name)
        {
            for (int i = 0; i < doc.Sensors.Count; i++)
            {
                if (doc.Sensors[i].Name == name)
                {
                    return doc.Sensors[i];
                }
            }
            return new SensorDefV3();
        }

        /// <summary>
        /// 导线执行方法——条件 → 消费 → 动作 → 结果分叉
        /// </summary>
        /// <param name="sb">输出缓冲</param>
        /// <param name="wire">导线</param>
        /// <param name="pascal">导线 PascalCase 名</param>
        /// <param name="doc">IR（积木捕获与值传感器查询）</param>
        private static void AppendWireExecute(StringBuilder sb, WireDefV3 wire, string pascal, MauDocV3 doc)
        {
            string field = NameField(wire.Name);
            if (wire.Parallel)
            {
                AppendWireExecuteParallel(sb, wire, pascal, field, doc);
                return;
            }
            sb.AppendLine("        private void " + pascal + "_Execute(int frame)");
            sb.AppendLine("        {");
            sb.AppendLine("            if (!" + pascal + "_Condition())");
            sb.AppendLine("            {");
            sb.AppendLine("                return;");
            sb.AppendLine("            }");
            sb.AppendLine("            " + pascal + "_Consume();");
            // trace 埋点——触发（观测零语义影响）
            sb.AppendLine("            " + field + "_frame = frame;");
            sb.AppendLine("            AuditStore.Default?.Record(\"Flow\", \"trace.fire\", -1, new AuditProp[] { new AuditProp(\"wire\", \"" + wire.Name + "\"), new AuditProp(\"frame\", frame.ToString()) });");
            sb.AppendLine("            bool ok = true;");
            if (wire.BrickName.Length > 0)
            {
                string captureField = "";
                if (wire.CaptureTarget.Length > 0)
                {
                    captureField = "v_capture";
                }
                StringBuilder prelude = new StringBuilder();
                string expr = BrickCallExpr(wire.BrickName, wire.BrickArgs, captureField, doc, prelude);
                sb.AppendLine("            try");
                sb.AppendLine("            {");
                if (captureField.Length > 0)
                {
                    string captureType = CaptureCSType(wire.BrickName);
                    if (captureType == "long")
                    {
                        sb.AppendLine("                long v_capture = 0;");
                    }
                    else if (captureType == "int")
                    {
                        sb.AppendLine("                int v_capture = 0;");
                    }
                    else if (captureType == "bool")
                    {
                        sb.AppendLine("                bool v_capture = false;");
                    }
                    else
                    {
                        sb.AppendLine("                string v_capture = \"\";");
                    }
                }
                sb.Append(prelude);
                sb.AppendLine("                ok = " + expr + ";");
                if (wire.CaptureTarget.Length > 0)
                {
                    // 捕获落盒——DataBox 类型化位置（scope=私有/全局；无条件取值——失败侧 ERR 文本也在值里）
                    sb.AppendLine("                DataBox.Set<" + CaptureCSType(wire.BrickName) + ">(" + BoxScopeExpr(wire.CaptureTarget) + ", \"" + BoxKey(wire.CaptureTarget) + "\", v_capture);");
                }
                sb.AppendLine("            }");
                sb.AppendLine("            catch (Exception ex)");
                sb.AppendLine("            {");
                sb.AppendLine("                ok = false;");
                sb.AppendLine("            }");
            }
            AppendResultStatements(sb, wire, "ok", "frame");
            sb.AppendLine("        }");
        }/// <summary>
        /// par 导线执行——Busy 门防重入 + 主线程冻结启程 + Task.Run 后台动作 + Inbox 回投（主线程 Drain 应用）。
        /// 线程隔离：后台线程只调积木 handler + Inbox.Enqueue——零共享可变状态（RT.3 形态）。
        /// </summary>
        /// <param name="sb">输出缓冲</param>
        /// <param name="wire">导线</param>
        /// <param name="pascal">导线 PascalCase 名</param>
        /// <param name="field">导线字段名</param>
        private static void AppendWireExecuteParallel(StringBuilder sb, WireDefV3 wire, string pascal, string field, MauDocV3 doc)
        {
            sb.AppendLine("        private void " + pascal + "_Execute(int frame)");
            sb.AppendLine("        {");
            // [段1] Busy 门——后台动作在途防重入
            sb.AppendLine("            if (" + field + "_busy)");
            sb.AppendLine("            {");
            sb.AppendLine("                return;");
            sb.AppendLine("            }");
            sb.AppendLine("            if (!" + pascal + "_Condition())");
            sb.AppendLine("            {");
            sb.AppendLine("                return;");
            sb.AppendLine("            }");
            sb.AppendLine("            " + pascal + "_Consume();");
            // [段2] 主线程冻结启程——状态写入只在主线程
            sb.AppendLine("            " + field + "_busy = true;");
            sb.AppendLine("            " + field + "_frame = frame;");
            sb.AppendLine("            " + field + "_timedout = false;");
            if (wire.Timeout > 0)
            {
                sb.AppendLine("            " + field + "_cube.Start();");
            }
            sb.AppendLine("            AuditStore.Default?.Record(\"Flow\", \"trace.fire\", -1, new AuditProp[] { new AuditProp(\"wire\", \"" + wire.Name + "\"), new AuditProp(\"frame\", frame.ToString()) });");
            // [段3] 参数内联冻结——调用表达式字面量直嵌 lambda（零捕获零共享）
            string captureField = "";
            if (wire.CaptureTarget.Length > 0)
            {
                captureField = "v_capture";
            }
            string captureType = CaptureCSType(wire.BrickName);
            StringBuilder prelude = new StringBuilder();
            string expr = BrickCallExpr(wire.BrickName, wire.BrickArgs, captureField, doc, prelude);
            string preludeText = prelude.ToString();
            string captureDecl = "";
            if (wire.CaptureTarget.Length > 0)
            {
                if (captureType == "long")
                {
                    captureDecl = "                long v_capture = 0;";
                }
                else if (captureType == "int")
                {
                    captureDecl = "                int v_capture = 0;";
                }
                else if (captureType == "bool")
                {
                    captureDecl = "                bool v_capture = false;";
                }
                else
                {
                    captureDecl = "                string v_capture = \"\";";
                }
            }
            // [段4] Task.Run——后台线程只调积木 + Inbox 回投（零共享可变状态）
            sb.AppendLine("            System.Threading.Tasks.Task.Run(delegate ()");
            sb.AppendLine("            {");
            sb.AppendLine("                bool ok = false;");
            if (captureDecl.Length > 0)
            {
                sb.AppendLine(captureDecl);
            }
            sb.AppendLine("                try");
            sb.AppendLine("                {");
            sb.Append(preludeText);
            sb.AppendLine("                    ok = " + expr + ";");
            sb.AppendLine("                }");
            sb.AppendLine("                catch (Exception ex)");
            sb.AppendLine("                {");
            sb.AppendLine("                    ok = false;");
            sb.AppendLine("                }");
            if (wire.CaptureTarget.Length > 0)
            {
                sb.AppendLine("                " + field + "_inbox.Enqueue(new BrickCaptureV3<" + captureType + "> { Ok = ok, Value = v_capture });");
            }
            else
            {
                sb.AppendLine("                " + field + "_inbox.Enqueue(ok);");
            }
            sb.AppendLine("            });");
            sb.AppendLine("        }");
            // [段5] 回投应用——主线程 Tick 开头 Drain（见 AppendTickInboxes）
        }/// <summary>
        /// 结果语句生成——同步路径（ok 变量 + 分支 + trace.state）
        /// </summary>
        /// <param name="sb">输出缓冲</param>
        /// <param name="wire">导线</param>
        /// <param name="okVar">ok 变量名</param>
        /// <param name="frameVar">帧变量名</param>
        private static void AppendResultStatements(StringBuilder sb, WireDefV3 wire, string okVar, string frameVar)
        {
            if (wire.Results.Count == 1)
            {
                AppendAssign(sb, wire.Results[0]);
                sb.AppendLine("            AuditStore.Default?.Record(\"Flow\", \"trace.state\", -1, new AuditProp[] { new AuditProp(\"wire\", \"" + wire.Name + "\"), new AuditProp(\"to\", \"" + wire.Results[0].StateName + "=" + wire.Results[0].StateValue + "\"), new AuditProp(\"frame\", " + frameVar + ".ToString()) });");
            }
            else
            {
                sb.AppendLine("            if (" + okVar + ")");
                sb.AppendLine("            {");
                AppendAssign(sb, wire.Results[0]);
                sb.AppendLine("                AuditStore.Default?.Record(\"Flow\", \"trace.state\", -1, new AuditProp[] { new AuditProp(\"wire\", \"" + wire.Name + "\"), new AuditProp(\"to\", \"" + wire.Results[0].StateName + "=" + wire.Results[0].StateValue + "\"), new AuditProp(\"frame\", " + frameVar + ".ToString()) });");
                sb.AppendLine("            }");
                sb.AppendLine("            else");
                sb.AppendLine("            {");
                AppendAssign(sb, wire.Results[1]);
                sb.AppendLine("                AuditStore.Default?.Record(\"Flow\", \"trace.state\", -1, new AuditProp[] { new AuditProp(\"wire\", \"" + wire.Name + "\"), new AuditProp(\"to\", \"" + wire.Results[1].StateName + "=" + wire.Results[1].StateValue + "\"), new AuditProp(\"frame\", " + frameVar + ".ToString()) });");
                sb.AppendLine("            }");
            }
        }

        /// <summary>
        /// 状态赋值语句
        /// </summary>
        /// <param name="sb">输出缓冲</param>
        /// <param name="result">结果</param>
        private static void AppendAssign(StringBuilder sb, ResultV3 result)
        {
            sb.AppendLine("                " + NameField(result.StateName) + " = " + NamePascal(result.StateName) + "State." + NamePascal(result.StateValue) + ";");
        }

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
            sb.AppendLine("            TickInboxes(frame);");
            sb.AppendLine("            TickWires(frame);");
            sb.AppendLine("        }");
            sb.AppendLine("");
            AppendTickInboxes(sb, doc);
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
                sb.AppendLine("                new WireStatusV3() { Name = \"" + wire.Name + "\", Busy = " + field + "_busy, LastTriggerFrame = " + field + "_frame, TimedOut = " + field + "_timedout },");
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
        /// 结果转移体生成（无缩进前缀——调用方提供）
        /// </summary>
        /// <param name="sb">输出缓冲</param>
        /// <param name="wire">导线</param>
        /// <param name="okVar">ok 变量名</param>
        /// <param name="frameVar">帧变量名</param>
        private static void AppendResultBody(StringBuilder sb, WireDefV3 wire, string okVar, string frameVar)
        {
            if (wire.Results.Count == 1)
            {
                AppendAssign(sb, wire.Results[0]);
                sb.AppendLine("                    AuditStore.Default?.Record(\"Flow\", \"trace.state\", -1, new AuditProp[] { new AuditProp(\"wire\", \"" + wire.Name + "\"), new AuditProp(\"to\", \"" + wire.Results[0].StateName + "=" + wire.Results[0].StateValue + "\"), new AuditProp(\"frame\", " + frameVar + ".ToString()) });");
            }
            else
            {
                sb.AppendLine("                    if (" + okVar + ")");
                sb.AppendLine("                    {");
                AppendAssign(sb, wire.Results[0]);
                sb.AppendLine("                        AuditStore.Default?.Record(\"Flow\", \"trace.state\", -1, new AuditProp[] { new AuditProp(\"wire\", \"" + wire.Name + "\"), new AuditProp(\"to\", \"" + wire.Results[0].StateName + "=" + wire.Results[0].StateValue + "\"), new AuditProp(\"frame\", " + frameVar + ".ToString()) });");
                sb.AppendLine("                    }");
                sb.AppendLine("                    else");
                sb.AppendLine("                    {");
                AppendAssign(sb, wire.Results[1]);
                sb.AppendLine("                        AuditStore.Default?.Record(\"Flow\", \"trace.state\", -1, new AuditProp[] { new AuditProp(\"wire\", \"" + wire.Name + "\"), new AuditProp(\"to\", \"" + wire.Results[1].StateName + "=" + wire.Results[1].StateValue + "\"), new AuditProp(\"frame\", " + frameVar + ".ToString()) });");
                sb.AppendLine("                    }");
            }
        }

        /// <summary>
        /// 内嵌积木源——收集生成物引用的积木，源文件原文贴入生成物（复制即单包，R1 形态）。
        /// 依赖闭包：P5 最小化阶段积木零依赖（index.json dependencies 全空）——闭包解析随积木重生扩展。
        /// </summary>
        /// <param name="sb">输出缓冲</param>
        /// <param name="doc">FSM 网络 IR</param>
        private static void AppendBrickSources(StringBuilder sb, MauDocV3 doc)
        {
            List<string> names = new List<string>();
            for (int w = 0; w < doc.Wires.Count; w++)
            {
                if (doc.Wires[w].BrickName.Length > 0 && !names.Contains(doc.Wires[w].BrickName))
                {
                    names.Add(doc.Wires[w].BrickName);
                }
            }
            for (int s = 0; s < doc.Sensors.Count; s++)
            {
                if (!doc.Sensors[s].Passive && doc.Sensors[s].BrickName.Length > 0 && !names.Contains(doc.Sensors[s].BrickName))
                {
                    names.Add(doc.Sensors[s].BrickName);
                }
            }
            if (names.Count == 0)
            {
                return;
            }
            string root = BrickIndex.FindRepoRoot();
            for (int i = 0; i < names.Count; i++)
            {
                BrickIndexEntry entry;
                if (!BrickIndex.TryFind(names[i], out entry) || entry.Path.Length == 0)
                {
                    continue;
                }
                string path = Path.Combine(root, "Bricks", entry.Path);
                if (!File.Exists(path))
                {
                    continue;
                }
                sb.AppendLine("// ═══ 内嵌积木: " + names[i] + "（来源 Bricks/" + entry.Path + "——复制即单包）═══");
                // 剥 using 行——生成物文件级 using 统一在头部（CS1529 判例）
                string source = File.ReadAllText(path);
                string[] lines = source.Split('\n');
                for (int l = 0; l < lines.Length; l++)
                {
                    string trimmed = lines[l].Trim();
                    if (trimmed.StartsWith("using ", StringComparison.Ordinal))
                    {
                        continue;
                    }
                    sb.AppendLine(lines[l].TrimEnd('\r'));
                }
                sb.AppendLine("");
            }
        }
/// <summary>
/// 生成器盒子表——写源收集（导线捕获 + 壳探测捕获）：Key → C# 类型
/// </summary>
        /// <param name="doc">IR</param>
        /// <returns>盒子表</returns>
        private static Dictionary<string, string> GenBoxTypes(MauDocV3 doc)
        {
            Dictionary<string, string> boxTypes = new Dictionary<string, string>(StringComparer.Ordinal);
            for (int w = 0; w < doc.Wires.Count; w++)
            {
                WireDefV3 wire = doc.Wires[w];
                if (wire.CaptureTarget.Length > 0 && !boxTypes.ContainsKey(wire.CaptureTarget))
                {
                    boxTypes[wire.CaptureTarget] = CaptureCSType(wire.BrickName);
                }
            }
            for (int s = 0; s < doc.Sensors.Count; s++)
            {
                SensorDefV3 sensor = doc.Sensors[s];
                if (!sensor.Passive && sensor.CaptureTarget.Length > 0 && !boxTypes.ContainsKey(sensor.CaptureTarget))
                {
                    boxTypes[sensor.CaptureTarget] = "bool";
                }
            }
            return boxTypes;
        }/// <summary>
/// 盒子 Key 引用判定——@ 前缀私有盒或已注册写源的全局盒
/// </summary>
        /// <param name="doc">IR</param>
        /// <param name="name">参数引用名</param>
        /// <returns>true=盒子引用</returns>
        private static bool IsValueSensor(MauDocV3 doc, string name)
        {
            // 盒子 Key 引用判定——@ 前缀 = 私有盒（全局 Key 由写源表判定——生成器保守处理 @ 前缀即可）
            if (name.Length > 0 && name[0] == '@')
            {
                return true;
            }
            return GenBoxTypes(doc).ContainsKey(name);
        }/// <summary>
        /// 积木强类型调用表达式——Mau.Bricks.Xxx.Yyy(args, out _)——编译期验型（P5 协议 A）。
        /// 索引未命中兑底 false（E4xx 已拦，理论不可达）。
        /// </summary>
        /// <param name="brickName">积木名</param>
        /// <param name="args">参数原文列表</param>
        /// <returns>调用表达式文本</returns>
        private static string BrickCallExpr(string brickName, List<string> args, string captureField, MauDocV3 doc, StringBuilder prelude)
        {
            BrickIndexEntry entry;
            if (!BrickIndex.TryFind(brickName, out entry) || entry.Implementation.Length == 0)
            {
                return "false";
            }
            string s = entry.Implementation + "(";
            bool first = true;
            for (int i = 0; i < args.Count; i++)
            {
                if (!first)
                {
                    s = s + ", ";
                }
                first = false;
                string arg = args[i];
                string type = "";
                if (i < entry.InputTypes.Count)
                {
                    type = entry.InputTypes[i];
                }
                if (IsValueSensor(doc, arg))
                {
                    // 盒子 Key 引用——类型化前置取数（DataBox 位置），表达式用局部变量
                    string boxType = "string";
                    Dictionary<string, string> boxTypes = GenBoxTypes(doc);
                    if (boxTypes.ContainsKey(arg))
                    {
                        boxType = boxTypes[arg];
                    }
                    string local = "v_" + i.ToString();
                    if (boxType == "long")
                    {
                        prelude.AppendLine("                long " + local + " = 0;");
                    }
                    else if (boxType == "int")
                    {
                        prelude.AppendLine("                int " + local + " = 0;");
                    }
                    else if (boxType == "bool")
                    {
                        prelude.AppendLine("                bool " + local + " = false;");
                    }
                    else
                    {
                        prelude.AppendLine("                string " + local + " = \"\";");
                    }
                    prelude.AppendLine("                DataBox.TryGet<" + boxType + ">(" + BoxScopeExpr(arg) + ", \"" + BoxKey(arg) + "\", out " + local + ");");
                    s = s + local;
                }
                else if (type == "int" || type == "long")
                {
                    s = s + arg;
                }
                else if (arg.Length >= 2 && arg[0] == '"')
                {
                    s = s + arg;
                }
                else
                {
                    s = s + "\"" + arg + "\"";
                }
            }
            for (int o = 0; o < entry.OutputCount; o++)
            {
                if (!first)
                {
                    s = s + ", ";
                }
                first = false;
                if (o == 0 && captureField.Length > 0)
                {
                    // 捕获子句——首个 out 端口绑定捕获局部变量
                    s = s + "out " + captureField;
                }
                else
                {
                    s = s + "out _";
                }
            }
            s = s + ")";
            return s;
        }/// <summary>
        /// 单元体名 PascalCase（去前缀）——P_Go → Go / R_Slot → Slot（方法名用）
        /// </summary>
        /// <param name="name">声明名</param>
        /// <returns>去前缀 PascalCase</returns>
        private static string NameBodyPascal(string name)
{
            int sep = name.IndexOf('_');
            string body = name;
            if (sep >= 0)
            {
                body = name.Substring(sep + 1);
            }
            return NamePascal(body);
        }        /// <summary>
        /// 名称字段化——S_Talk → _s_talk / P_Go → _p_go（前缀字母小写 + 下划线去尾）
        /// </summary>
        /// <param name="name">声明名</param>
        /// <returns>C# 字段名</returns>
        private static string NameField(string name)
{
            int sep = name.IndexOf('_');
            string rest = name;
            if (sep >= 0)
            {
                rest = name.Substring(sep + 1);
            }
            string result = "";
            for (int i = 0; i < rest.Length; i++)
            {
                char c = rest[i];
                if (c == '_')
                {
                    continue;
                }
                if (i == 0)
                {
                    result = result + char.ToLowerInvariant(c);
                }
                else
                {
                    result = result + c;
                }
            }
            return "_" + result;
        }
        /// <summary>
        /// 名称 PascalCase——S_Talk → STalk / file.read → FileRead / Idle → Idle
        /// </summary>
        /// <param name="name">声明名</param>
        /// <returns>PascalCase 标识符</returns>
        private static string NamePascal(string name)
        {
            string result = "";
            bool upperNext = true;
            for (int i = 0; i < name.Length; i++)
            {
                char c = name[i];
                if (c == '_' || c == '.' || c == '-' || c == '@')
                {
                    upperNext = true;
                    continue;
                }
                if (upperNext)
                {
                    result = result + char.ToUpperInvariant(c);
                    upperNext = false;
                }
                else
                {
                    result = result + c;
                }
            }
            return result;
        }
    }

    /// <summary>
    /// 生成结果——C# 源码 + 诊断
    /// </summary>
    public sealed class GenerateResultV3
    {
        /// <summary>
        /// 生成是否成功
        /// </summary>
        public bool Success;

        /// <summary>
        /// C# 源码全文
        /// </summary>
        public string Code = "";

        /// <summary>
        /// 生成诊断
        /// </summary>
        public List<MauDiagnostic> Diagnostics = new List<MauDiagnostic>();
    }
}
