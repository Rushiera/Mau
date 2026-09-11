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
            // 动作序列（R0.1 多动作）——顺序执行，任一失败 ok=false（失败侧转移）；捕获各自落盒
            for (int a = 0; a < wire.Actions.Count; a++)
            {
                WireActionV3 action = wire.Actions[a];
                string captureField = "";
                if (action.CaptureTarget.Length > 0)
                {
                    captureField = "v_capture";
                }
                StringBuilder prelude = new StringBuilder();
                string expr = BrickCallExpr(action.BrickName, action.BrickArgs, captureField, doc, prelude);
                sb.AppendLine("            try");
                sb.AppendLine("            {");
                if (captureField.Length > 0)
                {
                    string captureType = CaptureCSType(action.BrickName);
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
                sb.AppendLine("                bool a_ok = " + expr + ";");
                if (action.CaptureTarget.Length > 0)
                {
                    // 捕获落盒——DataBox 类型化位置（scope=私有/全局；无条件取值——失败侧 ERR 文本也在值里）
                    sb.AppendLine("                DataBox.Set<" + CaptureCSType(action.BrickName) + ">(" + BoxScopeExpr(action.CaptureTarget) + ", \"" + BoxKey(action.CaptureTarget) + "\", v_capture);");
                }
                sb.AppendLine("                if (!a_ok)");
                sb.AppendLine("                {");
                sb.AppendLine("                    ok = false;");
                sb.AppendLine("                }");
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
/// <summary>
/// Flow 元数据生成——自曝加载必需信息（组名 + 认领工具清单）的规范化 JSON。
/// 数据源：IR 主动传感器中 oa.is_open["TOOL", "工具名"] 的参数（语料声明唯一真相）。
/// 宿主装配（扫描 dll 建路由表）/ 外观层（工具归属展示）统一经 IFlow.GetMetaJson 读取。
/// </summary>
/// <param name = "sb">输出缓冲</param>
/// <param name = "doc">IR</param>
/// <param name = "flowName">流程名（组名——dll 名 FL_<组名>.dll）</param>
private static void AppendGetMetaJson(StringBuilder sb, MauDocV3 doc, string flowName)
        {
        // 认领工具清单——主动传感器 BrickName=oa.is_open 且参数 [0]=="TOOL" → 参数 [1]=工具名（去重保序）
        // CollectArgs 字符串参数带引号序列化（"TOOL"）——剥引号后比对
        List<string> claims = new List<string>();
        for (int s = 0; s < doc.Sensors.Count; s = s + 1)
        {
            SensorDefV3 sensor = doc.Sensors[s];
            if (sensor.Passive)
            {
                continue;
            }

            if (sensor.BrickName != "oa.is_open")
            {
                continue;
            }

            if (sensor.BrickArgs.Count < 2)
            {
                continue;
            }

            string arg0 = sensor.BrickArgs[0].Trim('"');
            if (arg0 != "TOOL")
            {
                continue;
            }

            string tool = sensor.BrickArgs[1].Trim('"');
            if (tool.Length > 0 && !claims.Contains(tool))
            {
                claims.Add(tool);
            }
        }

    // JSON 序列化——工具名白名单安全字符（字母/数字/连字符/下划线），直接拼串
    string json = "{\"group\":\"" + flowName + "\",\"claims\":[";
    for (int i = 0; i < claims.Count; i = i + 1)
    {
        if (i > 0)
        {
            json = json + ",";
        }

        json = json + "\"" + claims[i] + "\"";
    }

    json = json + "]}";
    sb.AppendLine("        // ── Flow 元数据（R——自曝：组名 + 认领工具清单；宿主装配/外观层读取共用）──");
    // C# 字符串字面量转义——json 内引号需转义为 \"（生成物才可编译）
    string escaped = json.Replace("\"", "\\\"");
    sb.AppendLine("        private static readonly string _flowMetaJson = \"" + escaped + "\";");
    sb.AppendLine("        public string GetMetaJson()");
    sb.AppendLine("        {");
    sb.AppendLine("            return _flowMetaJson;");
    sb.AppendLine("        }");
    sb.AppendLine("");
}    /// <summary>
/// 工具定义生成——实现 IFlow.GetToolsJson：调用约定积木 tools.<flowName> 返回本组工具定义 JSON。
/// 约定积木缺失（非工具组 Flow——如 QuickCat）→ 回退空工具组 JSON（{"group":"<名>","tools":[]}）。
/// </summary>
/// <param name = "sb">输出缓冲</param>
/// <param name = "doc">IR（未用——工具定义在积木侧）</param>
/// <param name = "flowName">流程名（组名）</param>
private static void AppendGetToolsJson(StringBuilder sb, MauDocV3 doc, string flowName)
        {
    string brickName = "tools." + flowName.ToLowerInvariant();
    BrickIndexEntry entry;
    string body;
    if (BrickIndex.TryFind(brickName, out entry) && entry.Implementation.Length > 0)
    {
        body = "return " + entry.Implementation + "();";
    }
    else
    {
        body = "return \"{\\\"group\\\":\\\"" + flowName + "\\\",\\\"tools\\\":[]}\";";
    }

    sb.AppendLine("        // ── 工具定义（R——本组全部工具的 OpenAI 兼容定义；宿主工具池原料）──");
    sb.AppendLine("        public string GetToolsJson()");
    sb.AppendLine("        {");
    sb.AppendLine("            " + body);
    sb.AppendLine("        }");
    sb.AppendLine("");
}}
}