using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Mau.Translator
{
/// <summary>
/// 生成器 v3——FSM 网络 IR → C# 生成物（design-mau-v3 §八.6）。生成形态：状态机枚举单值 + IsXxx/GetState；被动传感器消费字段 + FireXxx；主动传感器帧门控采样；导线条件原子检查（全过才消费）+ 动作 try-catch + 结果分叉；槽计数字段 + TryAcquire/Release。积木强类型直调（P5 协议 A——内嵌积木源 + 编译期验型）；par 后台 Task.Run + Inbox 回投；[t=] Cube 时限。限制：导线结果 >2 的多路分叉报 E205（名称返回积木体系随积木重生扩展）
/// </summary>
///
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
            sb.AppendLine("using System.Threading;");
            sb.AppendLine("");
            sb.AppendLine("namespace Mau.Generated");
            sb.AppendLine("{");
            sb.AppendLine("    public sealed class FL_" + flowName + " : IObservableFlow");
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
                    sb.AppendLine("            " + NamePascal(sm.States[i]) + (i + 1 < sm.States.Count ? "," : ""));
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
                string field = NameField(sensor.Name);
                string pascal = NameBodyPascal(sensor.Name);
                if (sensor.Passive)
                {
                    // 被动传感器——DataBox 事件层（内部交互总线）：无本地字段，沿 = 注册信号
                    sb.AppendLine("        // ── 传感器 " + sensor.Name + "（被动——DataBox 事件沿，消费即清）──");
                    sb.AppendLine("        public bool IsSet_" + pascal + "() { return DataBox.TryPeek(\"" + sensor.Name + "\"); }");
                }
                else
                {
                    sb.AppendLine("        // ── 传感器 " + sensor.Name + "（主动——每 " + sensor.EveryFrames + " 帧采样）──");
                    sb.AppendLine("        private bool " + field + ";");
                    sb.AppendLine("        private long " + field + "_frame;");
                    sb.AppendLine("        public bool IsSet_" + pascal + "() { return " + field + "; }");
                }
                sb.AppendLine("");
            }
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
        /// 导线生成——条件检查 + 消费 + 执行 + 结果分叉 + 状态字段（观测）
        /// </summary>
        /// <param name="sb">输出缓冲</param>
        /// <param name="doc">IR</param>
        private static void AppendWires(StringBuilder sb, MauDocV3 doc)
        {
            for (int w = 0; w < doc.Wires.Count; w++)
            {
                WireDefV3 wire = doc.Wires[w];
                string pascal = NamePascal(wire.Name);
                sb.AppendLine("        // ── 导线 " + wire.Name + " ──" + (wire.Parallel ? " [par]" : "") + (wire.Timeout > 0 ? " [t=" + wire.Timeout + "]" : ""));
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
                    sb.AppendLine("        private readonly Inbox<bool> " + NameField(wire.Name) + "_inbox = new Inbox<bool>();");
                }
                AppendWireCondition(sb, wire, pascal, doc);
                AppendWireConsume(sb, wire, pascal, doc);
                AppendWireExecute(sb, wire, pascal);
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
                else if (FindSensor(doc, cond.SensorName).Passive)
                {
                    sb.AppendLine("            if (!DataBox.TryPeek(\"" + cond.SensorName + "\")) { return false; }");
                }
                else
                {
                    sb.AppendLine("            if (!" + NameField(cond.SensorName) + ") { return false; }");
                }
            }
            sb.AppendLine("            return true;");
            sb.AppendLine("        }");
        }

        /// <summary>
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
        private static void AppendWireExecute(StringBuilder sb, WireDefV3 wire, string pascal)
        {
            string field = NameField(wire.Name);
            if (wire.Parallel)
            {
                AppendWireExecuteParallel(sb, wire, pascal, field);
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
                sb.AppendLine("            try");
                sb.AppendLine("            {");
                sb.AppendLine("                ok = " + BrickCallExpr(wire.BrickName, wire.BrickArgs) + ";");
                sb.AppendLine("            }");
                sb.AppendLine("            catch (Exception ex)");
                sb.AppendLine("            {");
                sb.AppendLine("                ok = false;");
                sb.AppendLine("            }");
            }
            AppendResultStatements(sb, wire, "ok", "frame");
            sb.AppendLine("        }");
        }

        /// <summary>
        /// par 导线执行——Busy 门防重入 + 主线程冻结启程 + Task.Run 后台动作 + Inbox 回投（主线程 Drain 应用）。
        /// 线程隔离：后台线程只调积木 handler + Inbox.Enqueue——零共享可变状态（RT.3 形态）。
        /// </summary>
        /// <param name="sb">输出缓冲</param>
        /// <param name="wire">导线</param>
        /// <param name="pascal">导线 PascalCase 名</param>
        /// <param name="field">导线字段名</param>
        private static void AppendWireExecuteParallel(StringBuilder sb, WireDefV3 wire, string pascal, string field)
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
            // [段4] Task.Run——后台线程只调积木 + Inbox 回投（零共享可变状态）
            sb.AppendLine("            System.Threading.Tasks.Task.Run(delegate ()");
            sb.AppendLine("            {");
            sb.AppendLine("                bool ok = false;");
            sb.AppendLine("                try");
            sb.AppendLine("                {");
            sb.AppendLine("                    ok = " + BrickCallExpr(wire.BrickName, wire.BrickArgs) + ";");
            sb.AppendLine("                }");
            sb.AppendLine("                catch (Exception ex)");
            sb.AppendLine("                {");
            sb.AppendLine("                    ok = false;");
            sb.AppendLine("                }");
            sb.AppendLine("                " + field + "_inbox.Enqueue(ok);");
            sb.AppendLine("            });");
            sb.AppendLine("        }");
            // [段5] 回投应用——主线程 Tick 开头 Drain（见 AppendTickInboxes）
        }

        /// <summary>
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
            sb.AppendLine("            TickSensors(frame);");
            sb.AppendLine("            TickWires(frame);");
            sb.AppendLine("        }");
            sb.AppendLine("");
            AppendTickInboxes(sb, doc);
            sb.AppendLine("        private void TickSensors(int frame)");
            sb.AppendLine("        {");
            for (int i = 0; i < doc.Sensors.Count; i++)
            {
                SensorDefV3 sensor = doc.Sensors[i];
                if (sensor.Passive)
                {
                    continue;
                }
                string field = NameField(sensor.Name);
                if (sensor.EveryFrames > 0)
                {
                    sb.AppendLine("            if ((frame - " + field + "_frame) < " + sensor.EveryFrames + ") { return; }");
                    sb.AppendLine("            " + field + "_frame = frame;");
                }
                sb.AppendLine("            try");
                sb.AppendLine("            {");
                sb.AppendLine("                " + field + " = " + BrickCallExpr(sensor.BrickName, sensor.BrickArgs) + ";");
                sb.AppendLine("            }");
                sb.AppendLine("            catch (Exception ex)");
                sb.AppendLine("            {");
                sb.AppendLine("                " + field + " = false;");
                sb.AppendLine("            }");
                // trace 埋点——采样（观测零语义影响）
                sb.AppendLine("            AuditStore.Default?.Record(\"Flow\", \"trace.sample\", -1, new AuditProp[] { new AuditProp(\"sensor\", \"" + sensor.Name + "\"), new AuditProp(\"value\", " + field + " ? \"1\" : \"0\"), new AuditProp(\"frame\", frame.ToString()) });");
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
                sb.AppendLine("                " + line + (i + 1 < doc.StateMachines.Count ? "," : ""));
            }
            sb.AppendLine("            };");
            // [段2] 主动传感器实测值
            sb.AppendLine("            s.SensorValues = new SignalValueV3[]");
            sb.AppendLine("            {");
            int sensorCount = 0;
            for (int i = 0; i < doc.Sensors.Count; i++)
            {
                if (doc.Sensors[i].Passive)
                {
                    continue;
                }
                SensorDefV3 sensor = doc.Sensors[i];
                sb.AppendLine("                new SignalValueV3() { Name = \"" + sensor.Name + "\", Value = " + NameField(sensor.Name) + " },");
                sensorCount = sensorCount + 1;
            }
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
                // [段2] 回投应用——主线程 Drain
                sb.AppendLine("            " + field + "_inbox.Drain(delegate (bool ok)");
                sb.AppendLine("            {");
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
                AppendAssign(sb, wire.Results[wire.Results.Count > 1 ? 1 : 0]);
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
        /// 积木强类型调用表达式——Mau.Bricks.Xxx.Yyy(args, out _)——编译期验型（P5 协议 A）。
        /// 索引未命中兑底 false（E4xx 已拦，理论不可达）。
        /// </summary>
        /// <param name="brickName">积木名</param>
        /// <param name="args">参数原文列表</param>
        /// <returns>调用表达式文本</returns>
        private static string BrickCallExpr(string brickName, List<string> args)
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
                string type = i < entry.InputTypes.Count ? entry.InputTypes[i] : "";
                if (type == "int" || type == "long")
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
                s = s + "out _";
            }
            s = s + ")";
            return s;
        }
        /// <summary>
        /// 单元体名 PascalCase（去前缀）——P_Go → Go / R_Slot → Slot（方法名用）
        /// </summary>
        /// <param name="name">声明名</param>
        /// <returns>去前缀 PascalCase</returns>
        private static string NameBodyPascal(string name)
        {
            int sep = name.IndexOf('_');
            string body = sep >= 0 ? name.Substring(sep + 1) : name;
            return NamePascal(body);
        }
        /// <summary>
        /// 名称字段化——S_Talk → _s_talk / P_Go → _p_go（前缀字母小写 + 下划线去尾）
        /// </summary>
        /// <param name="name">声明名</param>
        /// <returns>C# 字段名</returns>
        private static string NameField(string name)
        {
            int sep = name.IndexOf('_');
            string rest = sep >= 0 ? name.Substring(sep + 1) : name;
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
                if (c == '_' || c == '.' || c == '-')
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
