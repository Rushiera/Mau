using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Mau.Translator
{
    /// <summary>
    /// 生成器 v3——FSM 网络 IR → C# 生成物（design-mau-v3 §八.6）。生成形态：状态机枚举单值 + IsXxx/GetState；被动传感器消费字段 + FireXxx；主动传感器帧门控采样；导线条件原子检查（全过才消费）+ 动作 try-catch + 结果分叉；槽计数字段 + TryAcquire/Release。积木强类型直调（P5 协议 A——内嵌积木源 + 编译期验型）；par 后台 Task.Run + Inbox 回投；[t=] Cube 时限。限制：导线结果 >2 的多路分叉报 E205（名称返回积木体系随积木重生扩展）
    /// 生成主流程（Generate/Header/状态机/传感器）在本文件；导线面分部在 MauGeneratorV3.Wires.cs；
    /// Tick/槽/盒子判真面在 MauGeneratorV3.Tick.cs；积木面在 MauGeneratorV3.Bricks.cs；名称辅助在 MauGeneratorV3.Names.cs（P7b partial 拆分）。
    /// </summary>
    public static partial class MauGeneratorV3
    {
        /// <summary>
        /// 生成 C# 产物——IR → 源码文本（兼容入口——默认内嵌积木，debug/test 单文件路径）
        /// </summary>
        /// <param name="doc">FSM 网络 IR</param>
        /// <param name="flowName">流程名（PascalCase——类名 FL_ 前缀）</param>
        /// <returns>生成结果</returns>
        public static GenerateResultV3 Generate(MauDocV3 doc, string flowName)
        {
            return Generate(doc, flowName, true);
        }

        /// <summary>
        /// 生成 C# 产物——IR → 源码文本（组模式入口——embedBricks=false 时积木提取到 BRIKGROUP.cs，FL 只引用）
        /// </summary>
        /// <param name="doc">FSM 网络 IR</param>
        /// <param name="flowName">流程名（PascalCase——类名 FL_ 前缀）</param>
        /// <param name="embedBricks">true=内嵌积木源（单文件路径）；false=不内嵌（组模式——BRIKGROUP.cs 提供）</param>
        /// <returns>生成结果</returns>
        public static GenerateResultV3 Generate(MauDocV3 doc, string flowName, bool embedBricks)
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
            AppendGetSelfDesc(sb, doc);
            AppendGetMetaJson(sb, doc, flowName);
            AppendGetToolsJson(sb, doc, flowName);
            sb.AppendLine("    }");
            sb.AppendLine("}");
            if (embedBricks)
            {
                AppendBrickSources(sb, doc, flowName);
            }
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
            // 实体自述（R0.1）——Tick 开头捕获当前 Flow ID（FlowContext 驱动上下文），GetSelfDesc 读盒
            sb.AppendLine("        private long _flowId;");
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
                    sb.AppendLine("        public bool Is" + statePascal + "_" + pascal + "() { return " + NameField(sm.Name) + " == " + pascal + "State." + statePascal + "; }");
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
                // 探测落盒——bool 返回值（探测积木 = 判断语义——落盒即判断结果）
                sb.AppendLine("                DataBox.Set<bool>(" + BoxScopeExpr(sensor.CaptureTarget) + ", \"" + BoxKey(sensor.CaptureTarget) + "\", ok);");
            }
            sb.AppendLine("            }");
sb.AppendLine("            catch (Exception)");
            sb.AppendLine("            {");
            sb.AppendLine("                ok = false;");
            sb.AppendLine("            }");
            sb.AppendLine("            AuditStore.Default?.Record(\"Flow\", \"trace.sample\", -1, new AuditProp[] { new AuditProp(\"sensor\", \"" + sensor.Name + "\"), new AuditProp(\"value\", ok ? \"1\" : \"0\"), new AuditProp(\"frame\", frame.ToString()) }, false);");
            // 分支动作——成功侧/失败侧
            AppendShellActions(sb, sensor.TrueActions, "if (ok)", doc);
            AppendShellActions(sb, sensor.FalseActions, "if (!ok)", doc);
            sb.AppendLine("        }");
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