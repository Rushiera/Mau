using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Mau.Translator
{
    /// <summary>
    /// MauGeneratorV3 导线面分部——导线生成（条件/消费/执行/par 回投/结果分叉）。
    /// P7b partial 拆分——自 MauGeneratorV3.cs 原样搬移，逻辑零改动。
    /// </summary>
    public static partial class MauGeneratorV3
    {
        /// <summary>
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
                // 观测字段——GetStatus 快照源（P3；busy/timedout 仅 par 导线——同步导线恒 false 不产字段，D6）
                sb.AppendLine("        private long " + NameField(wire.Name) + "_frame;");
                if (wire.Parallel)
                {
                    sb.AppendLine("        private bool " + NameField(wire.Name) + "_busy;");
                    sb.AppendLine("        private bool " + NameField(wire.Name) + "_timedout;");
                }
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
                sb.AppendLine("            catch (Exception)");
                sb.AppendLine("            {");
                sb.AppendLine("                ok = false;");
                sb.AppendLine("            }");
            }
            AppendResultStatements(sb, wire, "ok", "frame");
            sb.AppendLine("        }");
        }/// <summary>
/// par 导线执行——Busy 门防重入 + 主线程冻结启程（@key 参数启程帧取数） + Task.Run 后台动作 + Inbox 回投（主线程 Drain 应用）。线程隔离：后台线程只调积木 handler + Inbox.Enqueue——lambda 捕获启程帧值快照，零共享可变状态（RT.3 形态）。
/// </summary>
///

        ///
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
            // [段3] 参数冻结取数——@key 引用在启程帧主线程前置取数（FlowContext 是 ThreadStatic——后台线程读不到 FlowId，取数必须在启程侧完成）
            string captureField = "";
            if (wire.CaptureTarget.Length > 0)
            {
                captureField = "v_capture";
            }
            string captureType = CaptureCSType(wire.BrickName);
            StringBuilder prelude = new StringBuilder();
            string expr = BrickCallExpr(wire.BrickName, wire.BrickArgs, captureField, doc, prelude);
            string preludeText = prelude.ToString();
            if (preludeText.Length > 0)
            {
                preludeText = preludeText.Replace("\n                ", "\n            ");
                if (preludeText.StartsWith("                "))
                {
                    preludeText = preludeText.Substring(4);
                }
            }
            sb.Append(preludeText);
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
            // [段4] Task.Run——后台线程只调积木 + Inbox 回投（lambda 捕获启程帧冻结的值快照——零共享可变状态）
            sb.AppendLine("            System.Threading.Tasks.Task.Run(delegate ()");
            sb.AppendLine("            {");
            sb.AppendLine("                bool ok = false;");
            if (captureDecl.Length > 0)
            {
                sb.AppendLine(captureDecl);
            }
            sb.AppendLine("                try");
            sb.AppendLine("                {");
            sb.AppendLine("                    ok = " + expr + ";");
            sb.AppendLine("                }");
            sb.AppendLine("                catch (Exception)");
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
    }
}