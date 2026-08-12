using System;
using System.Collections.Generic;
using System.Text;

namespace Mau.Translator
{
    /// <summary>
    /// 生成层——IR → C# 生成物（确定性输出，黄金对比锁定）
    /// </summary>
    public static class MauGeneratorV2
    {
        /// <summary>
        /// 生成入口
        /// </summary>
        /// <param name="doc">解析文档（已验证）</param>
        /// <param name="flowName">流程名——PascalCase，生成类名</param>
        /// <returns>生成物源码</returns>
        public static string Generate(MauDocV2 doc, string flowName)
{
            StringBuilder sb = new StringBuilder();
            string className = SanitizeClassName(flowName);

            // [段1] 文件头
            sb.Append("// ═══ " + className + " 生成物 — Mau v2.0 翻译器 ═══\n");
            sb.Append("// 生成物由翻译器确定性输出——手工修改无效，改 .mau 后重新生成\n");
            sb.Append("using System;\n");
            sb.Append("using System.Collections.Generic;\n");
            sb.Append("using Mau.Runtime;\n");
            sb.Append("\n");
            sb.Append("namespace Mau.Generated\n");
            sb.Append("{\n");
            sb.Append("    /// <summary>\n");
            sb.Append("    /// 生成物——" + className + " 受控系统\n");
            sb.Append("    /// </summary>\n");
            sb.Append("    public sealed class " + className + "\n");
            sb.Append("    {\n");
            // [段2] 注入字段（类型从引用端口推导——多引用不一致兜底 string）+ 设置器 SetXxx（M51 纯赋值语义）
            if (doc.Injections.Count > 0)
            {
                sb.Append("        // [注入字段]\n");
                for (int i = 0; i < doc.Injections.Count; i++)
                {
                    string injType = InferInjectionType(doc, doc.Injections[i]);
                    sb.Append("        private " + injType + "? _" + doc.Injections[i] + ";\n");
                }
                sb.Append("\n");
                for (int i = 0; i < doc.Injections.Count; i++)
                {
                    string injType = InferInjectionType(doc, doc.Injections[i]);
                    string setterName = "Set" + ToPascal(SanitizeName(doc.Injections[i]));
                    sb.Append("        /// <summary>\n");
                    sb.Append("        /// 注入字段设置——" + doc.Injections[i] + "（纯赋值，不置位信号）\n");
                    sb.Append("        /// </summary>\n");
                    sb.Append("        /// <param name=\"value\">注入值</param>\n");
                    sb.Append("        public void " + setterName + "(" + injType + "? value) { _" + doc.Injections[i] + " = value; }\n");
                    sb.Append("\n");
                }
            }

            // [段3] 状态机——枚举 + 字段（字段 _ 前缀——类型与字段同名 C# 不允许）；子机跳过（None 哨兵在子机段生成）
            for (int i = 0; i < doc.Machines.Count; i++)
            {
                MachineV2 m = doc.Machines[i];
                if (IsChildMachine(doc, m.Name))
                {
                    continue;
                }
                bool hasChild = HasChildBind(doc, m.Name);
                sb.Append("        private enum " + m.Name + "_State { ");
                for (int s = 0; s < m.States.Count; s++)
                {
                    if (s > 0)
                    {
                        sb.Append(", ");
                    }
                    sb.Append(SanitizeName(m.States[s]));
                }
                if (hasChild)
                {
                    // 父机不需要 None（自身枚举）；子机 None 在子机声明处
                }
                sb.Append(" }\n");
                sb.Append("        private " + m.Name + "_State _" + m.Name + "_State;\n");
                sb.Append("\n");
            }
            // 子机枚举——None 哨兵
            for (int i = 0; i < doc.Machines.Count; i++)
            {
                MachineV2 m = doc.Machines[i];
                if (IsChildMachine(doc, m.Name))
                {
                    sb.Append("        // [嵌套子机 " + m.Name + "——None = 未激活]\n");
                    sb.Append("        private enum " + m.Name + "_State { None");
                    for (int s = 0; s < m.States.Count; s++)
                    {
                        sb.Append(", ");
                        sb.Append(SanitizeName(m.States[s]));
                    }
                    sb.Append(" }\n");
                    sb.Append("        private " + m.Name + "_State _" + m.Name + "_State;\n");
                    sb.Append("\n");
                }
            }

            // [段4] 命题字段
            if (doc.Propositions.Count > 0)
            {
                sb.Append("        // [命题]\n");
                for (int i = 0; i < doc.Propositions.Count; i++)
                {
                    sb.Append("        private bool " + SanitizeName(doc.Propositions[i].Name) + ";\n");
                }
                sb.Append("\n");
            }

            // [段5] 资源计数
            if (doc.Resources.Count > 0)
            {
                sb.Append("        // [资源]\n");
                for (int i = 0; i < doc.Resources.Count; i++)
                {
                    sb.Append("        private int " + SanitizeName(doc.Resources[i].Name) + "_Count = " + doc.Resources[i].Quota + ";\n");
                }
                sb.Append("\n");
            }

            // [段5b] 积木输出端口字段——契约输出端口 → _字段（生成物可编译前提）
            GenerateOutputFields(sb, doc);

            // [段6] 测量帧计数 + 控制律 Cube
            if (doc.Measures.Count > 0)
            {
                sb.Append("        // [测量帧计数]\n");
                for (int i = 0; i < doc.Measures.Count; i++)
                {
                    sb.Append("        private int " + SanitizeName(doc.Measures[i].Name) + "_FrameCounter;\n");
                }
                sb.Append("\n");
            }
            if (doc.Laws.Count > 0)
            {
                sb.Append("        // [控制律 Cube]\n");
                for (int i = 0; i < doc.Laws.Count; i++)
                {
                    if (doc.Laws[i].Attrs.Timeout != null)
                    {
                        sb.Append("        private readonly Cube " + SanitizeName(doc.Laws[i].Name) + "_Cube = new Cube(" + doc.Laws[i].Attrs.Timeout + ");\n");
                    }
                }
                sb.Append("\n");
            // [段6b] worker 律汇合字段——∥ 后台执行 + ⋈ Inbox 回投（RT.3 生成器 ∥ 落地，2026-08-13）
            for (int w = 0; w < doc.Laws.Count; w++)
            {
                if (doc.Laws[w].Attrs.IsWorker)
                {
                    GenerateWorkerFields(sb, doc, doc.Laws[w]);
                }
            }
            }

            // [段7] 构造函数——初始状态置位
            sb.Append("        /// <summary>\n");
            sb.Append("        /// 构造——初始状态置位\n");
            sb.Append("        /// </summary>\n");
            sb.Append("        public " + className + "()\n");
            sb.Append("        {\n");
            for (int i = 0; i < doc.Machines.Count; i++)
            {
                MachineV2 m = doc.Machines[i];
                sb.Append("            " + m.Name + "_Enter_" + SanitizeName(m.States[0]) + "();\n");
            }
            sb.Append("        }\n");
            sb.Append("\n");

            // [段8] 状态机 Enter 族 + 查询
            GenerateMachines(sb, doc);

            // [段9] 测量采样
            GenerateMeasures(sb, doc);

            // [段10] Tick 帧驱动
            GenerateTick(sb, doc, className);

            // [段11] 控制律执行
            GenerateLaws(sb, doc);

            // [段12] 边界 Fire
            GenerateBoundaries(sb, doc);

            // [段13] 积木审计辅助——brick.invoke/ok/error 埋点（T13：观测不改变系统）
            sb.Append("        /// <summary>\n");
            sb.Append("        /// 积木调用审计——brick.invoke/ok/error（自动审计埋点，观测不改变系统）\n");
            sb.Append("        /// </summary>\n");
            sb.Append("        /// <param name=\"stage\">阶段——invoke/ok/error</param>\n");
            sb.Append("        /// <param name=\"law\">控制律名</param>\n");
            sb.Append("        /// <param name=\"brick\">积木名</param>\n");
            sb.Append("        /// <param name=\"frame\">全局帧号</param>\n");
            sb.Append("        private void AuditBrick(string stage, string law, string brick, int frame)\n");
            sb.Append("        {\n");
            sb.Append("            if (AuditStore.Default != null)\n");
            sb.Append("            {\n");
            sb.Append("                AuditStore.Default.Record(\"Flow\", \"brick.\" + stage, frame, new AuditProp[] {\n");
            sb.Append("                    new AuditProp(\"flow\", this.GetType().Name),\n");
            sb.Append("                    new AuditProp(\"law\", law),\n");
            sb.Append("                    new AuditProp(\"brick\", brick)\n");
            sb.Append("                }, false);\n");
            sb.Append("            }\n");
            sb.Append("        }\n");
            sb.Append("\n");

            sb.Append("    }\n");
            sb.Append("}\n");
            return sb.ToString();
        }        /// <summary>
        /// 状态机生成——Enter 族 + IsX + GetState/GetStatePath
        /// </summary>
        /// <param name="sb">输出</param>
        /// <param name="doc">文档</param>
        private static void GenerateMachines(StringBuilder sb, MauDocV2 doc)
{
            // Enter 族
            for (int i = 0; i < doc.Machines.Count; i++)
            {
                MachineV2 m = doc.Machines[i];
                bool isChild = IsChildMachine(doc, m.Name);
                for (int s = 0; s < m.States.Count; s++)
                {
                    string stateName = SanitizeName(m.States[s]);
                    sb.Append("        /// <summary>\n");
                    sb.Append("        /// 进入 " + m.Name + "." + m.States[s] + "——单值赋值（互斥由类型系统保证）\n");
                    sb.Append("        /// </summary>\n");
                    sb.Append("        private void " + m.Name + "_Enter_" + stateName + "()\n");
                    sb.Append("        {\n");
                    if (isChild)
                    {
                        // 子机——父隐式进入（父不在绑定状态时）；避免递归：仅改父枚举，不调父 Enter
                        MachineBindV2? myBind = FindBindByChild(doc, m.Name);
                        if (myBind != null)
                        {
                            sb.Append("            if (_" + SanitizeName(myBind.ParentName) + "_State != " + SanitizeName(myBind.ParentName) + "_State." + SanitizeName(myBind.ParentState) + ")\n");
                            sb.Append("            {\n");
                            sb.Append("                _" + SanitizeName(myBind.ParentName) + "_State = " + SanitizeName(myBind.ParentName) + "_State." + SanitizeName(myBind.ParentState) + ";\n");
                            sb.Append("            }\n");
                        }
                    }
                    sb.Append("            _" + m.Name + "_State = " + m.Name + "_State." + stateName + ";\n");
                    if (!isChild)
                    {
                        // 父机——嵌套钩子：进入绑定的父状态 → 子机初始；进入其他状态 → 子机 None
                        MachineBindV2? bind = FindBind(doc, m.Name, m.States[s]);
                        if (bind != null)
                        {
                            sb.Append("            " + SanitizeName(bind.ChildName) + "_Enter_" + SanitizeName(GetInitialState(doc, bind.ChildName)) + "();\n");
                        }
                        else if (HasChildBind(doc, m.Name))
                        {
                            string child = GetChildName(doc, m.Name);
                            sb.Append("            _" + child + "_State = " + child + "_State.None;\n");
                        }
                    }
                    sb.Append("        }\n");
                    sb.Append("\n");
                }
            }

            // 查询方法
            for (int i = 0; i < doc.Machines.Count; i++)
            {
                MachineV2 m = doc.Machines[i];
                for (int s = 0; s < m.States.Count; s++)
                {
                    string stateName = SanitizeName(m.States[s]);
                    sb.Append("        /// <summary>\n");
                    sb.Append("        /// " + m.Name + " 是否处于 " + m.States[s] + "\n");
                    sb.Append("        /// </summary>\n");
                    sb.Append("        public bool Is" + m.Name.Replace("S_", "") + stateName + "() { return _" + m.Name + "_State == " + m.Name + "_State." + stateName + "; }\n");
                    sb.Append("\n");
                }
            }

            // GetState/GetStatePath——平面机
            for (int i = 0; i < doc.Machines.Count; i++)
            {
                MachineV2 m = doc.Machines[i];
                if (IsChildMachine(doc, m.Name))
                {
                    continue;
                }
                string stateEnum = m.Name + "_State";
                sb.Append("        /// <summary>\n");
                sb.Append("        /// " + m.Name + " 当前状态名\n");
                sb.Append("        /// </summary>\n");
                sb.Append("        public string Get" + m.Name.Replace("S_", "") + "State()\n");
                sb.Append("        {\n");
                sb.Append("            switch (_" + m.Name + "_State)\n");
                sb.Append("            {\n");
                for (int s = 0; s < m.States.Count; s++)
                {
                    string stateName = SanitizeName(m.States[s]);
                    sb.Append("                case " + stateEnum + "." + stateName + ": return \"" + m.States[s] + "\";\n");
                }
                sb.Append("                default: return \"Unknown\";\n");
                sb.Append("            }\n");
                sb.Append("        }\n");
                sb.Append("\n");
            }

            // GetStatePath——嵌套路径
            for (int i = 0; i < doc.Machines.Count; i++)
            {
                MachineV2 m = doc.Machines[i];
                if (m.Binds.Count == 0)
                {
                    continue;
                }
                string stateEnum = m.Name + "_State";
                sb.Append("        /// <summary>\n");
                sb.Append("        /// " + m.Name + " 嵌套状态路径——'Parent.Child'\n");
                sb.Append("        /// </summary>\n");
                sb.Append("        public string Get" + m.Name.Replace("S_", "") + "StatePath()\n");
                sb.Append("        {\n");
                sb.Append("            switch (_" + m.Name + "_State)\n");
                sb.Append("            {\n");
                for (int b = 0; b < m.Binds.Count; b++)
                {
                    MachineBindV2 bind = m.Binds[b];
                    string child = SanitizeName(bind.ChildName);
                    sb.Append("                case " + stateEnum + "." + SanitizeName(bind.ParentState) + ": return \"" + bind.ParentState + ".\" + _" + child + "_State.ToString();\n");
                }
                sb.Append("                default: return _" + m.Name + "_State.ToString();\n");
                sb.Append("            }\n");
                sb.Append("        }\n");
                sb.Append("\n");
            }
        }
        /// <summary>
        /// 测量生成——采样方法 + 帧门控
        /// </summary>
        /// <param name="sb">输出</param>
        /// <param name="doc">文档</param>
        private static void GenerateMeasures(StringBuilder sb, MauDocV2 doc)
        {
            for (int i = 0; i < doc.Measures.Count; i++)
            {
                MeasureV2 m = doc.Measures[i];
                string name = SanitizeName(m.Name);
                sb.Append("        /// <summary>\n");
                sb.Append("        /// " + m.Name + " 采样——" + (m.Frame == 1 ? "每帧" : "第 " + m.Frame.ToString() + " 帧") + "写入 " + m.Target + "\n");
                sb.Append("        /// </summary>\n");
                sb.Append("        private void " + name + "_Sample()\n");
                sb.Append("        {\n");
                if (m.Frame == 1)
                {
                    sb.Append("            bool result = " + BuildBrickCall(m.Sample, doc) + ";\n");
                    sb.Append("            " + SanitizeName(m.Target) + " = result;\n");
                }
                else
                {
                    sb.Append("            if (" + name + "_FrameCounter % " + m.Frame.ToString() + " == 0)\n");
                    sb.Append("            {\n");
                    sb.Append("                bool result = " + BuildBrickCall(m.Sample, doc) + ";\n");
                    sb.Append("                " + SanitizeName(m.Target) + " = result;\n");
                    sb.Append("            }\n");
                }
                sb.Append("            " + name + "_FrameCounter = " + name + "_FrameCounter + 1;\n");
                sb.Append("        }\n");
                sb.Append("\n");
            }
        }

        /// <summary>
        /// Tick 生成——测量采样 + 控制律守卫（声明顺序）
        /// </summary>
        /// <param name="sb">输出</param>
        /// <param name="doc">文档</param>
        /// <param name="className">类名</param>
        private static void GenerateTick(StringBuilder sb, MauDocV2 doc, string className)
        {
            sb.Append("        /// <summary>\n");
            sb.Append("        /// 帧驱动——测量采样 + 控制律守卫（声明顺序）\n");
            sb.Append("        /// </summary>\n");
            sb.Append("        /// <param name=\"frame\">全局帧号</param>\n");
            sb.Append("        public void Tick(int frame)\n");
            sb.Append("        {\n");
            for (int i = 0; i < doc.Measures.Count; i++)
            {
                sb.Append("            " + SanitizeName(doc.Measures[i].Name) + "_Sample();\n");
            }
            for (int i = 0; i < doc.Laws.Count; i++)
            {
                sb.Append("            " + SanitizeName(doc.Laws[i].Name) + "_Execute(frame);\n");
            }
            sb.Append("        }\n");
            sb.Append("\n");
        }

        /// <summary>
        /// 控制律生成——守卫/消费/资源/操作/结果/时限七段
        /// </summary>
        /// <param name="sb">输出</param>
        /// <param name="doc">文档</param>
        private static void GenerateLaws(StringBuilder sb, MauDocV2 doc)
{
            for (int i = 0; i < doc.Laws.Count; i++)
            {
                LawV2 law = doc.Laws[i];
                if (law.Attrs.IsWorker)
                {
                    // RT.3——∥ worker 律分叉（后台执行 + Inbox 汇合形态）
                    GenerateWorkerLaw(sb, doc, law);
                    continue;
                }
                string name = SanitizeName(law.Name);
                sb.Append("        /// <summary>\n");
                sb.Append("        /// " + law.Name + " 控制律——条件 + 操作 → 结果\n");
                sb.Append("        /// </summary>\n");
                sb.Append("        /// <param name=\"frame\">全局帧号</param>\n");
                sb.Append("        private void " + name + "_Execute(int frame)\n");
                sb.Append("        {\n");

                string guard = BuildCondGuard(law.Conditions);
                string cubeIdle = law.Attrs.Timeout != null ? " && " + name + "_Cube.IsIdle()" : "";
                string cubeRunning = law.Attrs.Timeout != null ? " && " + name + "_Cube.IsRunning()" : "";
                // 名称返回积木判定——String 契约返回（matched 承载匹配名；两路 ok 驱动 / 多路 switch 分发）
                bool isNameReturn = false;
                if (law.Ops.Count == 1)
                {
                    BrickIndexEntry? routeEntry = null;
                    isNameReturn = BrickIndex.TryGet(law.Ops[0].BrickName, out routeEntry)
                        && routeEntry != null && routeEntry.Contract != null
                        && routeEntry.Contract.Return == Mau.Contracts.BrickReturnKind.String;
                }

                // [段1] 触发分支
                sb.Append("            // [段1] 条件守卫——全部成立 → 触发\n");
                sb.Append("            if (" + guard + cubeIdle + ")\n");
                sb.Append("            {\n");
                // [段2] 信号消费
                List<string> signals = CollectSignalConsumes(law.Conditions, doc);
                if (signals.Count > 0)
                {
                    sb.Append("                // [段2] 信号消费——触发即清除\n");
                    for (int s = 0; s < signals.Count; s++)
                    {
                        sb.Append("                " + signals[s] + " = false;\n");
                    }
                }
                // [段3] 资源获取
                List<string> resources = CollectResources(law.Conditions);
                if (resources.Count > 0)
                {
                    sb.Append("                // [段3] 资源获取——动作完成后释放\n");
                    for (int r = 0; r < resources.Count; r++)
                    {
                        sb.Append("                " + resources[r] + "_Count = " + resources[r] + "_Count - 1;\n");
                    }
                }
                if (law.Attrs.Timeout != null)
                {
                    sb.Append("                " + name + "_Cube.Start();\n");
                }
                // [段4] 操作——自动审计埋点（brick.invoke/ok/error，T13）
                if (law.Ops.Count > 0)
                {
                    if (isNameReturn)
                    {
                        // 多路名称返回——matched 承载匹配名（switch 分发源）
                        sb.Append("                // [段4] 操作——名称返回积木（多路匹配，switch 分发源）\n");
                        sb.Append("                string matched = \"\";\n");
                        sb.Append("                bool ok = false;\n");
                        sb.Append("                try\n");
                        sb.Append("                {\n");
                        for (int o = 0; o < law.Ops.Count; o++)
                        {
                            BrickCallV2 call = law.Ops[o];
                            sb.Append("                    AuditBrick(\"invoke\", \"" + law.Name + "\", \"" + call.BrickName + "\", frame);\n");
                            sb.Append("                    matched = " + BuildBrickCall(call, doc) + ";\n");
                            sb.Append("                    ok = matched.Length > 0;\n");
                            sb.Append("                    AuditBrick(\"ok\", \"" + law.Name + "\", \"" + call.BrickName + "\", frame);\n");
                        }
                        sb.Append("                }\n");
                        sb.Append("                catch (Exception ex)\n");
                        sb.Append("                {\n");
                        sb.Append("                    ok = false;\n");
                        sb.Append("                    AuditBrick(\"error\", \"" + law.Name + "\", \"" + law.Ops[0].BrickName + "\", frame);\n");
                        sb.Append("                }\n");
                    }
                    else
                    {
                        sb.Append("                // [段4] 操作——顺序执行 + try-catch 隔离 + 自动审计\n");
                        sb.Append("                bool ok = false;\n");
                        sb.Append("                try\n");
                        sb.Append("                {\n");
                        for (int o = 0; o < law.Ops.Count; o++)
                        {
                            BrickCallV2 call = law.Ops[o];
                            sb.Append("                    AuditBrick(\"invoke\", \"" + law.Name + "\", \"" + call.BrickName + "\", frame);\n");
                            sb.Append("                    ok = " + BuildBrickCall(call, doc) + ";\n");
                            sb.Append("                    AuditBrick(\"ok\", \"" + law.Name + "\", \"" + call.BrickName + "\", frame);\n");
                        }
                        sb.Append("                }\n");
                        sb.Append("                catch (Exception ex)\n");
                        sb.Append("                {\n");
                        sb.Append("                    ok = false;\n");
                        sb.Append("                    AuditBrick(\"error\", \"" + law.Name + "\", \"" + (law.Ops.Count > 0 ? law.Ops[0].BrickName : "") + "\", frame);\n");
                        sb.Append("                }\n");
                    }
                }
                else
                {
                    sb.Append("                // [段4] 操作——无（纯转移控制律）\n");
                    sb.Append("                bool ok = true;\n");
                }
                // [段5] 日志标记（[!] 属性——T14，零语义影响）
                if (law.Attrs.IsLogging)
                {
                    sb.Append("                if (LogStore.AllLog != null) { LogStore.Add(\"LAW\", 0, \"" + law.Name + " | 触发 | frame=\" + frame, \"\"); }\n");
                }
                // [段6] 结果转移
                sb.Append("                // [段5] 结果转移——bool 驱动首项成功 / 次项失败 / 多路 switch 分发\n");
                if (law.Results.Count == 1)
                {
                    sb.Append("                " + BuildResultStatement(law.Results[0]) + "\n");
                }
                else if (law.Results.Count == 2)
                {
                    sb.Append("                if (ok)\n");
                    sb.Append("                {\n");
                    sb.Append("                    " + BuildResultStatement(law.Results[0]) + "\n");
                    if (law.Attrs.IsLogging)
                    {
                        sb.Append("                    if (LogStore.AllLog != null) { LogStore.Add(\"LAW\", 0, \"" + law.Name + " | 成功 | frame=\" + frame, \"\"); }\n");
                    }
                    sb.Append("                }\n");
                    sb.Append("                else\n");
                    sb.Append("                {\n");
                    sb.Append("                    " + BuildResultStatement(law.Results[1]) + "\n");
                    if (law.Attrs.IsLogging)
                    {
                        sb.Append("                    if (LogStore.AllLog != null) { LogStore.Add(\"LAW\", 0, \"" + law.Name + " | 失败 | frame=\" + frame, \"\"); }\n");
                    }
                    sb.Append("                }\n");
                }
                else if (isNameReturn)
                {
                    // 名称返回多路——switch(matched) 按名分发，末项 = default 兜底
                    sb.Append("                switch (matched)\n");
                    sb.Append("                {\n");
                    List<string> candidates = CollectCandidateNames(law.Ops[0]);
                    for (int c = 0; c < candidates.Count && c < law.Results.Count - 1; c++)
                    {
                        sb.Append("                    case " + QuoteString(candidates[c]) + ": " + BuildResultStatement(law.Results[c]) + " break;\n");
                    }
                    sb.Append("                    default: " + BuildResultStatement(law.Results[law.Results.Count - 1]) + " break;\n");
                    sb.Append("                }\n");
                }
                else
                {
                    // 多结果非名称返回——[0]=成功 [1]=失败 [2+]=成功追加（语法糖展开：SEQ 级联/FBK 回环）
                    sb.Append("                if (ok)\n");
                    sb.Append("                {\n");
                    sb.Append("                    " + BuildResultStatement(law.Results[0]) + "\n");
                    for (int a = 2; a < law.Results.Count; a++)
                    {
                        sb.Append("                    " + BuildResultStatement(law.Results[a]) + "\n");
                    }
                    sb.Append("                }\n");
                    sb.Append("                else\n");
                    sb.Append("                {\n");
                    sb.Append("                    " + BuildResultStatement(law.Results[1]) + "\n");
                    sb.Append("                }\n");
                }
                // 资源释放
                if (resources.Count > 0)
                {
                    sb.Append("                // [段6] 资源释放\n");
                    for (int r = 0; r < resources.Count; r++)
                    {
                        sb.Append("                " + resources[r] + "_Count = " + resources[r] + "_Count + 1;\n");
                    }
                }
                // Cube 复位——触发完成（成功/失败均复位：允许 cond 再次满足时重新触发；否则 Cube 挂 Running→Expired 永不复位）
                if (law.Attrs.Timeout != null)
                {
                    sb.Append("                // [段6b] Cube 复位——触发完成（成功/失败均复位，允许再次触发）\n");
                    sb.Append("                " + name + "_Cube.Complete();\n");
                }
                sb.Append("            }\n");

                // [段7] τ 时限分支
                if (law.Attrs.Timeout != null)
                {
                    sb.Append("            // [段7] τ 时限——Cube 步进 + 耗尽 → 次项（超时走失败分支）\n");
                    sb.Append("            else if (" + guard + cubeRunning + ")\n");
                    sb.Append("            {\n");
                    sb.Append("                " + name + "_Cube.TickFrame();\n");
                    sb.Append("                if (" + name + "_Cube.IsExpired())\n");
                    sb.Append("                {\n");
                    if (law.Attrs.IsLogging)
                    {
                        sb.Append("                    if (LogStore.AllLog != null) { LogStore.Add(\"LAW\", 0, \"" + law.Name + " | 超时 | frame=\" + frame, \"\"); }\n");
                    }
                    if (law.Results.Count >= 2)
                    {
                        sb.Append("                    " + BuildResultStatement(law.Results[1]) + "\n");
                    }
                    // Cube 复位——超时走失败分支后 Expired → Idle（允许后续 cond 满足时重新触发）
                    sb.Append("                    " + name + "_Cube.Reset();\n");
                    sb.Append("                }\n");
                    sb.Append("            }\n");
                }

                sb.Append("        }\n");
                sb.Append("\n");
            }
        }
        /// <summary>
        /// 边界生成——Fire 方法 + OA 端口占位
        /// </summary>
        /// <param name="sb">输出</param>
        /// <param name="doc">文档</param>
        private static void GenerateBoundaries(StringBuilder sb, MauDocV2 doc)
        {
            for (int i = 0; i < doc.Boundaries.Count; i++)
            {
                BoundaryV2 b = doc.Boundaries[i];
                string signalName = b.MappedName != null ? b.MappedName : b.SignalName;
                string methodName = "Fire" + ToPascal(StripPrefix(b.SignalName));
                if (b.Dir == BoundaryDirV2.In)
                {
                    if (b.MappedName != null)
                    {
                        // OA 接收——端口解包
                        sb.Append("        /// <summary>\n");
                        sb.Append("        /// OA 接收——端口 '" + b.SignalName + "' 解包 → " + b.MappedName + "\n");
                        sb.Append("        /// </summary>\n");
                        sb.Append("        /// <param name=\"payload\">OA 载荷</param>\n");
                        sb.Append("        public void Receive" + ToPascal(b.SignalName) + "(string payload)\n");
                        sb.Append("        {\n");
                        sb.Append("            // [OA] 接收解包——宿主桥绑定后实现（oa.get 语义）\n");
                        sb.Append("            " + SanitizeName(b.MappedName) + " = true;\n");
                        sb.Append("        }\n");
                        sb.Append("\n");
                    }
                    else
                    {
                        // Fire 信号
                        if (b.Params.Count > 0)
                        {
                            sb.Append("        /// <summary>\n");
                            sb.Append("        /// 外部投递——" + b.SignalName + "（带载荷）\n");
                            sb.Append("        /// </summary>\n");
                            string paramList = "";
                            string assignList = "";
                            for (int p = 0; p < b.Params.Count; p++)
                            {
                                MauParamV2 param = b.Params[p];
                                string pname = SanitizeName(param.Text);
                                if (p > 0)
                                {
                                    paramList = paramList + ", ";
                                    assignList = assignList + "\n";
                                }
                                paramList = paramList + "string " + pname;
                                assignList = assignList + "            _" + pname + " = " + pname + ";";
                            }
                            sb.Append("        public void " + methodName + "(" + paramList + ")\n");
                            sb.Append("        {\n");
                            sb.Append(assignList + "\n");
                            sb.Append("            " + SanitizeName(signalName) + " = true;\n");
                            sb.Append("        }\n");
                            sb.Append("\n");
                        }
                        else
                        {
                            sb.Append("        /// <summary>\n");
                            sb.Append("        /// 外部投递——" + b.SignalName + "\n");
                            sb.Append("        /// </summary>\n");
                            sb.Append("        public void " + methodName + "()\n");
                            sb.Append("        {\n");
                            sb.Append("            " + SanitizeName(signalName) + " = true;\n");
                            sb.Append("        }\n");
                            sb.Append("\n");
                        }
                    }
                }
                else
                {
                    // 输出边界——OA 发送
                    sb.Append("        /// <summary>\n");
                    sb.Append("        /// OA 发送——端口 '" + b.SignalName + "' 打包 → '" + (b.MappedName ?? "?") + "'\n");
                    sb.Append("        /// </summary>\n");
                    sb.Append("        /// <returns>输出载荷</returns>\n");
                    sb.Append("        public string Send" + ToPascal(b.SignalName) + "()\n");
                    sb.Append("        {\n");
                    sb.Append("            // [OA] 发送打包——宿主桥绑定后实现（oa.set 语义）\n");
                    sb.Append("            return \"\";\n");
                    sb.Append("        }\n");
                    sb.Append("\n");
                }
            }
        }

        // ==================== 表达式构建 ====================

        /// <summary>
        /// 条件树 → C# 守卫表达式
        /// </summary>
        /// <param name="cond">条件节点</param>
        /// <returns>守卫表达式</returns>
        private static string BuildCondGuard(CondV2 cond)
{
            if (cond.Kind == CondKindV2.And)
            {
                string expr = "";
                for (int i = 0; i < cond.Items!.Count; i++)
                {
                    if (i > 0)
                    {
                        expr = expr + " && ";
                    }
                    expr = expr + "(" + BuildCondGuard(cond.Items[i]) + ")";
                }
                return expr;
            }
            if (cond.Kind == CondKindV2.Or)
            {
                string expr = "";
                for (int i = 0; i < cond.Items!.Count; i++)
                {
                    if (i > 0)
                    {
                        expr = expr + " || ";
                    }
                    expr = expr + "(" + BuildCondGuard(cond.Items[i]) + ")";
                }
                return expr;
            }
            if (cond.Kind == CondKindV2.StateEquals)
            {
                return "_" + SanitizeName(cond.MachineName!) + "_State == " + SanitizeName(cond.MachineName!) + "_State." + SanitizeName(cond.StateName!);
            }
            if (cond.Kind == CondKindV2.PropRef)
            {
                return SanitizeName(cond.RefName!);
            }
            if (cond.Kind == CondKindV2.ResourceRef)
            {
                return SanitizeName(cond.RefName!) + "_Count > 0";
            }
            return "true";
        }
        /// <summary>
        /// 条件中的信号命题收集——触发即消费
        /// </summary>
        /// <param name="cond">条件节点</param>
        /// <param name="doc">文档</param>
        /// <returns>信号字段名列表</returns>
        private static List<string> CollectSignalConsumes(CondV2 cond, MauDocV2 doc)
        {
            List<string> signals = new List<string>();
            CollectSignalsRecursive(cond, doc, signals);
            return signals;
        }

        /// <summary>
        /// 信号收集递归
        /// </summary>
        /// <param name="cond">条件节点</param>
        /// <param name="doc">文档</param>
        /// <param name="signals">收集结果</param>
        private static void CollectSignalsRecursive(CondV2 cond, MauDocV2 doc, List<string> signals)
        {
            if (cond == null)
            {
                return;
            }
            if (cond.Kind == CondKindV2.And || cond.Kind == CondKindV2.Or)
            {
                for (int i = 0; i < cond.Items!.Count; i++)
                {
                    CollectSignalsRecursive(cond.Items[i], doc, signals);
                }
                return;
            }
            if (cond.Kind == CondKindV2.PropRef)
            {
                PropositionV2? prop = doc.FindProposition(cond.RefName!);
                if (prop != null && prop.Kind == PropKindV2.Signal)
                {
                    signals.Add(SanitizeName(cond.RefName!));
                }
            }
        }

        /// <summary>
        /// 条件中的资源收集
        /// </summary>
        /// <param name="cond">条件节点</param>
        /// <returns>资源名列表</returns>
        private static List<string> CollectResources(CondV2 cond)
        {
            List<string> resources = new List<string>();
            CollectResourcesRecursive(cond, resources);
            return resources;
        }

        /// <summary>
        /// 资源收集递归
        /// </summary>
        /// <param name="cond">条件节点</param>
        /// <param name="resources">收集结果</param>
        private static void CollectResourcesRecursive(CondV2 cond, List<string> resources)
        {
            if (cond == null)
            {
                return;
            }
            if (cond.Kind == CondKindV2.And || cond.Kind == CondKindV2.Or)
            {
                for (int i = 0; i < cond.Items!.Count; i++)
                {
                    CollectResourcesRecursive(cond.Items[i], resources);
                }
                return;
            }
            if (cond.Kind == CondKindV2.ResourceRef)
            {
                resources.Add(SanitizeName(cond.RefName!));
            }
        }

        /// <summary>
        /// 结果项 → 转移语句
        /// </summary>
        /// <param name="res">结果项</param>
        /// <returns>C# 语句</returns>
        private static string BuildResultStatement(ResultV2 res)
        {
            if (res.Kind == ResultKindV2.StateTransfer)
            {
                return SanitizeName(res.MachineName!) + "_Enter_" + SanitizeName(res.StateName!) + "();";
            }
            return SanitizeName(res.PropName!) + " = true;";
        }
/// <summary>
/// 内嵌实现重写——完整限定名 Mau.Bricks.XxxBrick.Yyy → Mau.Bricks.BRIK_ID.Yyy（BRIK-ID 类名）
/// </summary>
/// <param name = "implementation">积木契约实现签名</param>
/// <param name = "brickId">BRIK-ID</param>
/// <returns>重写后的调用签名</returns>
private static string RewriteImplementation(string implementation, string brickId)
{
    int lastDot = implementation.LastIndexOf('.');
    if (lastDot < 0)
    {
        return implementation;
    }

    string method = implementation.Substring(lastDot + 1);
    return "Mau.Bricks." + BrickIndex.IdClassName(brickId) + "." + method;
}
/// <summary>
/// 端口类型 → C# 类型名（string/long/int/bool/数组递归）
/// </summary>
/// <param name = "t">端口类型</param>
/// <returns>C# 类型名</returns>
private static string CSharpTypeName(Type t)
{
    if (t == typeof(string))
    {
        return "string";
    }

    if (t == typeof(long))
    {
        return "long";
    }

    if (t == typeof(int))
    {
        return "int";
    }

    if (t == typeof(bool))
    {
        return "bool";
    }

    if (t == typeof(double))
    {
        return "double";
    }

    if (t == typeof(float))
    {
        return "float";
    }

    if (t.IsArray)
    {
        return CSharpTypeName(t.GetElementType()!) + "[]";
    }

    return t.Name;
}        /// <summary>
/// 单积木调用收集——Ref 参数匹配注入名时记录端口类型
/// </summary>
/// <param name = "call">积木调用</param>
/// <param name = "injectionName">注入字段名</param>
/// <param name = "found">已收集类型（引用传递）</param>
/// <param name = "conflict">类型冲突标记（引用传递）</param>
private static void CollectInjectionPortType(BrickCallV2 call, string injectionName, ref string? found, ref bool conflict)
{
    BrickIndexEntry? entry = null;
    if (!BrickIndex.TryGet(call.BrickName, out entry) || entry == null || entry.Contract == null)
    {
        return;
    }

    for (int p = 0; p < call.Params.Count && p < entry.Contract.Inputs.Count; p++)
    {
        if (call.Params[p].Kind == MauParamKindV2.Ref && call.Params[p].Text == injectionName)
        {
            string typeName = CSharpTypeName(entry.Contract.Inputs[p].Type);
            if (found == null)
            {
                found = typeName;
            }
            else if (found != typeName)
            {
                conflict = true;
            }
        }
    }
}/// <summary>
/// 注入字段类型推导——遍历控制律操作/测量采样，Ref 参数匹配注入名 → 积木输入端口类型（V2.0.5）
/// </summary>
/// <param name = "doc">文档</param>
/// <param name = "injectionName">注入字段名</param>
/// <returns>C# 类型名（多引用不一致/未引用兜底 string）</returns>
private static string InferInjectionType(MauDocV2 doc, string injectionName)
{
    string? found = null;
    bool conflict = false;
    for (int l = 0; l < doc.Laws.Count; l++)
    {
        for (int o = 0; o < doc.Laws[l].Ops.Count; o++)
        {
            CollectInjectionPortType(doc.Laws[l].Ops[o], injectionName, ref found, ref conflict);
        }
    }

    for (int m = 0; m < doc.Measures.Count; m++)
    {
        if (doc.Measures[m].Sample != null)
        {
            CollectInjectionPortType(doc.Measures[m].Sample, injectionName, ref found, ref conflict);
        }
    }

    if (conflict || found == null)
    {
        return "string";
    }

    return found;
}/// <summary>
        /// 积木调用 → C# 调用表达式（Bricks 静态类占位——M7 按积木契约替换）
        /// </summary>
        /// <param name="call">积木调用</param>
        /// <returns>C# 表达式</returns>
        private static string BuildBrickCall(BrickCallV2 call, MauDocV2 doc, string refPrefix = "_", string outPrefix = "_")
{
            // 积木契约查询——索引已加载时生成真实调用（完整限定名 + 输出端口 out）；未加载回退占位（纯结构生成）
            BrickIndexEntry? entry = null;
            bool hasContract = BrickIndex.TryGet(call.BrickName, out entry) && entry != null && entry.Contract != null;
            bool isNameReturn = hasContract && entry!.Contract!.Return == Mau.Contracts.BrickReturnKind.String;
            StringBuilder sb = new StringBuilder();
            if (hasContract)
            {
                // 内嵌重命名——完整限定名 Mau.Bricks.XxxBrick.Yyy → Mau.Bricks.BRIK_ID.Yyy（BRIK-ID 类名，BRIKGROUP 内嵌段提供）
                sb.Append(RewriteImplementation(entry!.Contract!.Implementation, entry.Id));
            }
            else
            {
                sb.Append("Bricks.");
                sb.Append(ToPascal(call.BrickName));
            }
            sb.Append("(");
            for (int p = 0; p < call.Params.Count; p++)
            {
                if (p > 0)
                {
                    sb.Append(", ");
                }
                // 按积木契约输入端口类型生成参数表达式（类型感知——bool/数组按端口类型转换；注入字段可空非空化）
                System.Type? portType = null;
                if (hasContract && p < entry!.Contract!.Inputs.Count)
                {
                    portType = entry.Contract.Inputs[p].Type;
                }
                bool isInjection = call.Params[p].Kind == MauParamKindV2.Ref && doc.Injections.Contains(call.Params[p].Text);
                sb.Append(BuildParamExpr(call.Params[p], portType, isInjection, refPrefix));
            }
            // 输出端口——out 传递（与输入参数拼接）；名称返回积木无 out（返回即名称）
            if (hasContract && !isNameReturn)
            {
                for (int o = 0; o < entry!.Contract!.Outputs.Count; o++)
                {
                    if (call.Params.Count > 0 || o > 0)
                    {
                        sb.Append(", ");
                    }
                    sb.Append("out " + outPrefix);
                    sb.Append(SanitizeName(entry.Contract.Outputs[o].Name));
                }
            }
            sb.Append(")");
            return sb.ToString();
        }/// <summary>
        /// 参数项 → C# 表达式（类型感知——按积木契约输入端口类型转换 bool/数组）
        /// </summary>
        /// <param name="param">参数项</param>
        /// <param name="portType">输入端口类型（契约查询；null = 未知回退默认）</param>
        /// <returns>C# 表达式</returns>
        private static string BuildParamExpr(MauParamV2 param, System.Type? portType, bool nullableField = false, string prefix = "_")
{
            if (param.Kind == MauParamKindV2.Ref)
            {
                // 注入/输出端口字段引用——prefix 前缀（worker 律 f_ 快照；同步律 _ 字段）
                string field = prefix + SanitizeName(param.Text);
                if (nullableField && portType != null && (portType == typeof(long) || portType == typeof(int)
                    || portType == typeof(bool) || portType == typeof(double) || portType == typeof(float)))
                {
                    return "(" + field + " ?? 0)";
                }
                return field;
            }
            if (param.Kind == MauParamKindV2.Number)
            {
                // 数值按端口类型解释——bool：0=false 1=true（语法面零新增，类型由端口决定）
                if (portType == typeof(bool))
                {
                    return param.Text == "1" ? "true" : "false";
                }
                return param.Text;
            }
            if (param.Kind == MauParamKindV2.String)
            {
                return QuoteString(param.Text);
            }
            if (param.Kind == MauParamKindV2.Array)
            {
                // 数组按端口元素类型生成——string[]/long[]/int[]/double[]/float[]/bool[]（默认 string[]）
                string elemType = "string";
                if (portType != null && portType.IsArray)
                {
                    System.Type elem = portType.GetElementType()!;
                    if (elem == typeof(long))
                    {
                        elemType = "long";
                    }
                    else if (elem == typeof(int))
                    {
                        elemType = "int";
                    }
                    else if (elem == typeof(double))
                    {
                        elemType = "double";
                    }
                    else if (elem == typeof(float))
                    {
                        elemType = "float";
                    }
                    else if (elem == typeof(bool))
                    {
                        elemType = "bool";
                    }
                }
                StringBuilder sb = new StringBuilder();
                sb.Append("new ");
                sb.Append(elemType);
                sb.Append("[] { ");
                for (int i = 0; i < param.Items!.Count; i++)
                {
                    if (i > 0)
                    {
                        sb.Append(", ");
                    }
                    if (elemType == "bool")
                    {
                        // bool 数组元素——数值 0/1 转 false/true
                        sb.Append(param.Items[i].Kind == MauParamKindV2.Number && param.Items[i].Text == "1" ? "true" : "false");
                    }
                    else
                    {
                        sb.Append(BuildParamExpr(param.Items[i], null, false, prefix));
                    }
                }
                sb.Append(" }");
                return sb.ToString();
            }
            return "\"\"";
        }
        /// <summary>
        /// 字符串转义——C# 字面量
        /// </summary>
        /// <param name="text">原始文本</param>
        /// <returns>C# 字符串字面量</returns>
        private static string QuoteString(string text)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("\"");
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c == '"')
                {
                    sb.Append("\\\"");
                }
                else if (c == '\\')
                {
                    sb.Append("\\\\");
                }
                else if (c == '\n')
                {
                    sb.Append("\\n");
                }
                else if (c == '\r')
                {
                    sb.Append("\\r");
                }
                else if (c == '\t')
                {
                    sb.Append("\\t");
                }
                else
                {
                    sb.Append(c);
                }
            }
            sb.Append("\"");
            return sb.ToString();
        }

        // ==================== 工具 ====================

        /// <summary>
        /// 状态机是否绑定子机
        /// </summary>
        /// <param name="doc">文档</param>
        /// <param name="machineName">状态机名</param>
        /// <returns>为真</returns>
        private static bool HasChildBind(MauDocV2 doc, string machineName)
        {
            MachineV2? m = doc.FindMachine(machineName);
            if (m == null)
            {
                return false;
            }
            return m.Binds.Count > 0;
        }

        /// <summary>
        /// 状态机是否为子机（被 ∈ 绑定）
        /// </summary>
        /// <param name="doc">文档</param>
        /// <param name="machineName">状态机名</param>
        /// <returns>为真</returns>
        private static bool IsChildMachine(MauDocV2 doc, string machineName)
        {
            for (int i = 0; i < doc.Machines.Count; i++)
            {
                for (int b = 0; b < doc.Machines[i].Binds.Count; b++)
                {
                    if (doc.Machines[i].Binds[b].ChildName == machineName)
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        /// <summary>
        /// 查找绑定——父状态对应子机
        /// </summary>
        /// <param name="doc">文档</param>
        /// <param name="machineName">父状态机名</param>
        /// <param name="stateName">父状态名</param>
        /// <returns>绑定（无则 null）</returns>
        private static MachineBindV2? FindBind(MauDocV2 doc, string machineName, string stateName)
        {
            MachineV2? m = doc.FindMachine(machineName);
            if (m == null)
            {
                return null;
            }
            for (int b = 0; b < m.Binds.Count; b++)
            {
                if (m.Binds[b].ParentState == stateName)
                {
                    return m.Binds[b];
                }
            }
            return null;
        }

        /// <summary>
        /// 查找绑定——子机对应的父绑定（隐式父进入用）
        /// </summary>
        /// <param name="doc">文档</param>
        /// <param name="childName">子机名</param>
        /// <returns>绑定（无则 null）</returns>
        private static MachineBindV2? FindBindByChild(MauDocV2 doc, string childName)
        {
            for (int i = 0; i < doc.Machines.Count; i++)
            {
                for (int b = 0; b < doc.Machines[i].Binds.Count; b++)
                {
                    if (doc.Machines[i].Binds[b].ChildName == childName)
                    {
                        return doc.Machines[i].Binds[b];
                    }
                }
            }
            return null;
        }

        /// <summary>
        /// 子机初始状态
        /// </summary>
        /// <param name="doc">文档</param>
        /// <param name="childName">子机名</param>
        /// <returns>初始状态名</returns>
        private static string GetInitialState(MauDocV2 doc, string childName)
        {
            MachineV2? m = doc.FindMachine(childName);
            if (m == null || m.States.Count == 0)
            {
                return "";
            }
            return m.States[0];
        }

        /// <summary>
        /// 子机名（父机唯一子机）
        /// </summary>
        /// <param name="doc">文档</param>
        /// <param name="machineName">父机名</param>
        /// <returns>子机名</returns>
        private static string GetChildName(MauDocV2 doc, string machineName)
        {
            MachineV2? m = doc.FindMachine(machineName);
            if (m == null || m.Binds.Count == 0)
            {
                return "";
            }
            return SanitizeName(m.Binds[0].ChildName);
        }

        /// <summary>
        /// 多路候选名收集——积木参数（跳过首参键）作为 case 标签
        /// </summary>
        /// <param name="call">积木调用</param>
        /// <returns>候选名列表</returns>
        private static List<string> CollectCandidateNames(BrickCallV2 call)
{
            List<string> names = new List<string>();
            for (int i = 1; i < call.Params.Count; i++)
            {
                MauParamV2 p = call.Params[i];
                if (p.Kind == MauParamKindV2.String || p.Kind == MauParamKindV2.Ref)
                {
                    names.Add(p.Text);
                }
                else if (p.Kind == MauParamKindV2.Array && p.Items != null)
                {
                    // 数组字面量——候选列表（'cmd.match'['key', ["A","B","C"]]）
                    for (int e = 0; e < p.Items.Count; e++)
                    {
                        MauParamV2 item = p.Items[e];
                        if (item.Kind == MauParamKindV2.String || item.Kind == MauParamKindV2.Ref)
                        {
                            names.Add(item.Text);
                        }
                    }
                }
            }
            return names;
        }
        /// <summary>
        /// 前缀剥离——P_ 前缀（Fire 方法名用）
        /// </summary>
        /// <param name="name">原始名</param>
        /// <returns>去前缀名</returns>
        private static string StripPrefix(string name)
        {
            if (name.StartsWith("P_", StringComparison.Ordinal))
            {
                return name.Substring(2);
            }
            return name;
        }

        /// <summary>
        /// 标识符清洗——非法字符替换
        /// </summary>
        /// <param name="name">原始名</param>
        /// <returns>C# 合法标识符</returns>
        private static string SanitizeName(string name)
        {
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < name.Length; i++)
            {
                char c = name[i];
                if ((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '_')
                {
                    sb.Append(c);
                }
                else
                {
                    sb.Append('_');
                }
            }
            return sb.ToString();
        }

        /// <summary>
        /// 类名清洗——非法字符替换 + 首字母大写
        /// </summary>
        /// <param name="name">流程名</param>
        /// <returns>C# 类名</returns>
        private static string SanitizeClassName(string name)
        {
            string cleaned = SanitizeName(name);
            if (cleaned.Length == 0)
            {
                return "FL_Flow";
            }
            char first = cleaned[0];
            if (first >= 'a' && first <= 'z')
            {
                cleaned = ((char)(first - 32)).ToString() + cleaned.Substring(1);
            }
            return cleaned;
        }

        /// <summary>
        /// 点分名 → PascalCase（llm.completions → LlmCompletions）
        /// </summary>
        /// <param name="name">点分名</param>
        /// <returns>PascalCase</returns>
        private static string ToPascal(string name)
        {
            StringBuilder sb = new StringBuilder();
            bool upperNext = true;
            for (int i = 0; i < name.Length; i++)
            {
                char c = name[i];
                if (c == '.' || c == '_')
                {
                    upperNext = true;
                    continue;
                }
                if (upperNext && c >= 'a' && c <= 'z')
                {
                    sb.Append((char)(c - 32));
                }
                else
                {
                    sb.Append(c);
                }
                upperNext = false;
            }
            return sb.ToString();
        }
/// <summary>
/// Type 到 C# 类型名
/// </summary>
/// <param name = "type">类型</param>
/// <returns>类型名</returns>
private static string TypeName(Type type)
{
    if (type.IsArray)
    {
        Type element = type.GetElementType()!;
        return TypeName(element) + "[]";
    }

    if (type == typeof(string))
    {
        return "string";
    }

    if (type == typeof(bool))
    {
        return "bool";
    }

    if (type == typeof(int))
    {
        return "int";
    }

    if (type == typeof(long))
    {
        return "long";
    }

    if (type == typeof(double))
    {
        return "double";
    }

    if (type.IsGenericType)
    {
        string baseName = type.Name;
        int tick = baseName.IndexOf('`');
        if (tick >= 0)
        {
            baseName = baseName.Substring(0, tick);
        }

        string fullBase = (type.Namespace ?? "").Length > 0 ? ((type.Namespace ?? "") + "." + baseName) : baseName;
        StringBuilder sb = new StringBuilder();
        sb.Append(fullBase);
        sb.Append("<");
        Type[] args = type.GetGenericArguments();
        for (int i = 0; i < args.Length; i = i + 1)
        {
            if (i > 0)
            {
                sb.Append(", ");
            }

            sb.Append(TypeName(args[i]));
        }

        sb.Append(">");
        return sb.ToString();
    }

    string full = (type.Namespace ?? "").Length > 0 ? ((type.Namespace ?? "") + "." + type.Name) : type.Name;
    return full;
}    /// <summary>
/// 输出端口字段生成——积木契约输出端口 → _字段声明（生成物可编译前提）
/// </summary>
/// <param name = "sb">输出</param>
/// <param name = "doc">文档</param>
private static void GenerateOutputFields(StringBuilder sb, MauDocV2 doc)
{
            // [段1] 收集全部积木调用——控制律操作 + 测量采样
            List<BrickCallV2> calls = new List<BrickCallV2>();
            for (int i = 0; i < doc.Laws.Count; i++)
            {
                for (int o = 0; o < doc.Laws[i].Ops.Count; o++)
                {
                    calls.Add(doc.Laws[i].Ops[o]);
                }
            }
            for (int m = 0; m < doc.Measures.Count; m++)
            {
                if (doc.Measures[m].Sample != null)
                {
                    calls.Add(doc.Measures[m].Sample);
                }
            }
            // [段2] 去重声明——输出端口名唯一
            Dictionary<string, string> fields = new Dictionary<string, string>(StringComparer.Ordinal);
            for (int c = 0; c < calls.Count; c++)
            {
                BrickIndexEntry entry;
                if (!BrickIndex.TryGet(calls[c].BrickName, out entry) || entry.Contract == null)
                {
                    continue;
                }
                for (int o = 0; o < entry.Contract.Outputs.Count; o++)
                {
                    string fieldName = "_" + SanitizeName(entry.Contract.Outputs[o].Name);
                    if (!fields.ContainsKey(fieldName))
                    {
                        fields[fieldName] = TypeName(entry.Contract.Outputs[o].Type);
                    }
                }
            }
            // [段3] 边界 Fire 载荷参数——_字段声明（⇐ 'P_X'['param'] → Fire 方法写入 _param）
            for (int b = 0; b < doc.Boundaries.Count; b++)
            {
                BoundaryV2 boundary = doc.Boundaries[b];
                if (boundary.Dir != BoundaryDirV2.In || boundary.MappedName != null || boundary.Params.Count == 0)
                {
                    continue;
                }
                for (int p = 0; p < boundary.Params.Count; p++)
                {
                    // 注入字段段已声明同名字段——跳过（注入与 Fire 载荷同源——RT.3 E2E 重复声明修复）
                    if (doc.Injections.Contains(boundary.Params[p].Text))
                    {
                        continue;
                    }
                    string fieldName = "_" + SanitizeName(boundary.Params[p].Text);
                    if (!fields.ContainsKey(fieldName))
                    {
                        fields[fieldName] = "string";
                    }
                }
            }
            if (fields.Count == 0)
            {
                return;
            }
            sb.Append("        // [积木输出端口]\n");
            foreach (KeyValuePair<string, string> kv in fields)
            {
                sb.Append("        private " + kv.Value + " " + kv.Key + " = default;\n");
            }
            sb.Append("\n");
        }/// <summary>
/// 输出端口字段类型查找——全文档积木调用契约 Outputs 名匹配
/// </summary>
/// <param name = "doc">文档</param>
/// <param name = "rawName">引用名（未加前缀）</param>
/// <param name = "typeName">匹配到的 C# 类型名</param>
/// <returns>找到</returns>
private static bool TryFindOutType(MauDocV2 doc, string rawName, out string typeName)
{
    List<BrickCallV2> calls = new List<BrickCallV2>();
    for (int i = 0; i < doc.Laws.Count; i++)
    {
        for (int o = 0; o < doc.Laws[i].Ops.Count; o++)
        {
            calls.Add(doc.Laws[i].Ops[o]);
        }
    }

    for (int m = 0; m < doc.Measures.Count; m++)
    {
        if (doc.Measures[m].Sample != null)
        {
            calls.Add(doc.Measures[m].Sample);
        }
    }

    for (int c = 0; c < calls.Count; c++)
    {
        BrickIndexEntry entry;
        if (!BrickIndex.TryGet(calls[c].BrickName, out entry) || entry.Contract == null)
        {
            continue;
        }

        for (int o = 0; o < entry.Contract.Outputs.Count; o++)
        {
            if (entry.Contract.Outputs[o].Name == rawName)
            {
                typeName = TypeName(entry.Contract.Outputs[o].Type);
                return true;
            }
        }
    }

    typeName = "string";
    return false;
}/// <summary>
/// worker 引用收集递归——Ref 参数登记冻结字段；数组递归展开
/// </summary>
/// <param name = "param">参数项</param>
/// <param name = "doc">文档</param>
/// <param name = "names">字段名列表</param>
/// <param name = "types">类型文本列表</param>
/// <param name = "nullables">可空标记列表</param>
private static void CollectWorkerRefsParam(MauParamV2 param, MauDocV2 doc, List<string> names, List<string> types, List<bool> nullables)
{
    if (param.Kind == MauParamKindV2.Ref)
    {
        string field = "_" + SanitizeName(param.Text);
        if (names.Contains(field))
        {
            return;
        }

        if (doc.Injections.Contains(param.Text))
        {
            names.Add(field);
            types.Add(InferInjectionType(doc, param.Text));
            nullables.Add(true);
            return;
        }

        string outType;
        if (TryFindOutType(doc, param.Text, out outType))
        {
            names.Add(field);
            types.Add(outType);
            nullables.Add(false);
            return;
        }

        names.Add(field);
        types.Add("string");
        nullables.Add(false);
        return;
    }

    if (param.Kind == MauParamKindV2.Array && param.Items != null)
    {
        for (int i = 0; i < param.Items.Count; i++)
        {
            CollectWorkerRefsParam(param.Items[i], doc, names, types, nullables);
        }
    }
}/// <summary>
/// worker 律操作段字段引用收集——输入冻结源（_字段名 + 类型文本 + 可空标记三平行列表）
/// </summary>
/// <param name = "call">积木调用</param>
/// <param name = "doc">文档</param>
/// <param name = "names">字段名列表（_前缀，去重）</param>
/// <param name = "types">类型文本列表</param>
/// <param name = "nullables">可空标记列表</param>
private static void CollectWorkerRefs(BrickCallV2 call, MauDocV2 doc, List<string> names, List<string> types, List<bool> nullables)
{
    for (int p = 0; p < call.Params.Count; p++)
    {
        CollectWorkerRefsParam(call.Params[p], doc, names, types, nullables);
    }
}/// <summary>
/// worker 律字段段——Inbox 汇合队列 + Busy 门 + 超时放弃标记 + 结果载荷类
/// </summary>
/// <param name = "sb">输出</param>
/// <param name = "doc">文档</param>
/// <param name = "law">控制律</param>
private static void GenerateWorkerFields(StringBuilder sb, MauDocV2 doc, LawV2 law)
{
    string name = SanitizeName(law.Name);
    sb.Append("        // [worker 汇合 " + law.Name + "——∥ 后台执行 + ⋈ Inbox 回投]\n");
    sb.Append("        private readonly Inbox<" + name + "_WorkerResult> _" + name + "_Inbox = new Inbox<" + name + "_WorkerResult>();\n");
    sb.Append("        private bool _" + name + "_Busy;\n");
    sb.Append("        private bool _" + name + "_TimedOut;\n");
    sb.Append("\n");
    // 结果载荷类——Ok + out 端口回投字段（全 ops 契约 Outputs 去重）
    List<string> outNames = new List<string>();
    List<string> outTypes = new List<string>();
    for (int o = 0; o < law.Ops.Count; o++)
    {
        BrickIndexEntry entry;
        if (!BrickIndex.TryGet(law.Ops[o].BrickName, out entry) || entry.Contract == null)
        {
            continue;
        }

        for (int p = 0; p < entry.Contract.Outputs.Count; p++)
        {
            string outName = SanitizeName(entry.Contract.Outputs[p].Name);
            if (!outNames.Contains(outName))
            {
                outNames.Add(outName);
                outTypes.Add(TypeName(entry.Contract.Outputs[p].Type));
            }
        }
    }

    sb.Append("        /// <summary>\n");
    sb.Append("        /// worker 结果载荷——" + law.Name + " 后台执行结果（Ok + out 端口回投）\n");
    sb.Append("        /// </summary>\n");
    sb.Append("        private sealed class " + name + "_WorkerResult\n");
    sb.Append("        {\n");
    sb.Append("            /// <summary>执行成功</summary>\n");
    sb.Append("            public bool Ok;\n");
    for (int p = 0; p < outNames.Count; p++)
    {
        sb.Append("            /// <summary>out 端口 " + outNames[p] + " 回投</summary>\n");
        sb.Append("            public " + outTypes[p] + " " + outNames[p] + ";\n");
    }

    sb.Append("        }\n");
    sb.Append("\n");
}/// <summary>
/// worker 律生成——∥ 后台执行：守卫/信号消费/资源获取主线程触发帧完成，
/// 操作 Task.Run 后台执行（输入冻结 f_ 快照），结果 ⋈ Inbox 回投主线程应用
/// </summary>
/// <param name = "sb">输出</param>
/// <param name = "doc">文档</param>
/// <param name = "law">控制律</param>
private static void GenerateWorkerLaw(StringBuilder sb, MauDocV2 doc, LawV2 law)
{
            string name = SanitizeName(law.Name);
            string guard = BuildCondGuard(law.Conditions);
            bool hasTimeout = law.Attrs.Timeout != null;
            List<string> signals = CollectSignalConsumes(law.Conditions, doc);
            List<string> resources = CollectResources(law.Conditions);
            // 输入冻结集合——操作段全部字段引用
            List<string> refNames = new List<string>();
            List<string> refTypes = new List<string>();
            List<bool> refNullables = new List<bool>();
            for (int o = 0; o < law.Ops.Count; o++)
            {
                CollectWorkerRefs(law.Ops[o], doc, refNames, refTypes, refNullables);
            }
            // out 端口集合——后台局部 r_ 变量 + 结果对象字段
            List<string> outNames = new List<string>();
            List<string> outTypes = new List<string>();
            for (int o = 0; o < law.Ops.Count; o++)
            {
                BrickIndexEntry entry;
                if (!BrickIndex.TryGet(law.Ops[o].BrickName, out entry) || entry.Contract == null)
                {
                    continue;
                }
                for (int p = 0; p < entry.Contract.Outputs.Count; p++)
                {
                    string outName = SanitizeName(entry.Contract.Outputs[p].Name);
                    if (!outNames.Contains(outName))
                    {
                        outNames.Add(outName);
                        outTypes.Add(TypeName(entry.Contract.Outputs[p].Type));
                    }
                }
            }
            string brickLabel = law.Ops.Count > 0 ? law.Ops[0].BrickName : "";

            sb.Append("        /// <summary>\n");
            sb.Append("        /// " + law.Name + " 控制律（∥ worker）——主线程守卫 + 后台操作 + ⋈ 汇合应用\n");
            sb.Append("        /// </summary>\n");
            sb.Append("        /// <param name=\"frame\">全局帧号</param>\n");
            sb.Append("        private void " + name + "_Execute(int frame)\n");
            sb.Append("        {\n");
            // [段1] 触发分支——守卫/信号/资源/冻结全在主线程
            sb.Append("            // [段1] 条件守卫——Busy 门防重入（worker 执行中不重复触发）\n");
            sb.Append("            if ((" + guard + ") && !_" + name + "_Busy)\n");
            sb.Append("            {\n");
            if (signals.Count > 0)
            {
                sb.Append("                // [段2] 信号消费——触发即清除\n");
                for (int s = 0; s < signals.Count; s++)
                {
                    sb.Append("                " + signals[s] + " = false;\n");
                }
            }
            if (resources.Count > 0)
            {
                sb.Append("                // [段3] 资源获取——动作完成后释放\n");
                for (int r = 0; r < resources.Count; r++)
                {
                    sb.Append("                " + resources[r] + "_Count = " + resources[r] + "_Count - 1;\n");
                }
            }
            if (refNames.Count > 0)
            {
                sb.Append("                // [段4] 输入冻结——f_ 局部快照（后台线程不读主线程字段）\n");
                for (int f = 0; f < refNames.Count; f++)
                {
                    string declType = refTypes[f] + (refNullables[f] ? "?" : "");
                    sb.Append("                " + declType + " f_" + refNames[f].Substring(1) + " = " + refNames[f] + ";\n");
                }
            }
            // 审计 invoke——主线程触发帧（后台不打审计——线程安全 + 帧语义清晰）
            sb.Append("                AuditBrick(\"invoke\", \"" + law.Name + "\", \"" + brickLabel + "\", frame);\n");
            // [段5] worker 后台执行——Task.Run + Inbox 回投
            sb.Append("                // [段5] worker 后台执行（∥——Task.Run + Inbox 回投）\n");
            sb.Append("                _" + name + "_Busy = true;\n");
            sb.Append("                _" + name + "_TimedOut = false;\n");
            if (hasTimeout)
            {
                sb.Append("                " + name + "_Cube.Start();\n");
            }
            sb.Append("                System.Threading.Tasks.Task.Run(delegate ()\n");
            sb.Append("                {\n");
            for (int p = 0; p < outNames.Count; p++)
            {
                sb.Append("                    " + outTypes[p] + " r_" + outNames[p] + " = default;\n");
            }
            sb.Append("                    bool ok = false;\n");
            sb.Append("                    try\n");
            sb.Append("                    {\n");
            for (int o = 0; o < law.Ops.Count; o++)
            {
                sb.Append("                        ok = " + BuildBrickCall(law.Ops[o], doc, "f_", "r_") + ";\n");
            }
            sb.Append("                    }\n");
            sb.Append("                    catch (Exception ex)\n");
            sb.Append("                    {\n");
            sb.Append("                        ok = false;\n");
            sb.Append("                    }\n");
            sb.Append("                    " + name + "_WorkerResult r0 = new " + name + "_WorkerResult();\n");
            sb.Append("                    r0.Ok = ok;\n");
            for (int p = 0; p < outNames.Count; p++)
            {
                sb.Append("                    r0." + outNames[p] + " = r_" + outNames[p] + ";\n");
            }
            sb.Append("                    _" + name + "_Inbox.Enqueue(r0);\n");
            sb.Append("                });\n");
            sb.Append("            }\n");
            // [段6] τ 超时分支——worker 运行期间 Cube 步进 + 耗尽 → 失败后置
            if (hasTimeout)
            {
                sb.Append("            // [段6] τ 时限——worker 运行期间 Cube 步进 + 耗尽 → 失败后置\n");
                sb.Append("            else if (_" + name + "_Busy && " + name + "_Cube.IsRunning())\n");
                sb.Append("            {\n");
                sb.Append("                " + name + "_Cube.TickFrame();\n");
                sb.Append("                if (" + name + "_Cube.IsExpired())\n");
                sb.Append("                {\n");
                sb.Append("                    _" + name + "_TimedOut = true;\n");
                if (law.Attrs.IsLogging)
                {
                    sb.Append("                    if (LogStore.AllLog != null) { LogStore.Add(\"LAW\", 0, \"" + law.Name + " | 超时 | frame=\" + frame, \"\"); }\n");
                }
                for (int r = 0; r < resources.Count; r++)
                {
                    sb.Append("                    " + resources[r] + "_Count = " + resources[r] + "_Count + 1;\n");
                }
                if (law.Results.Count >= 2)
                {
                    sb.Append("                    " + BuildResultStatement(law.Results[1]) + "\n");
                }
                sb.Append("                    " + name + "_Cube.Reset();\n");
                sb.Append("                }\n");
                sb.Append("            }\n");
            }
            // [段7] inbox 汇合（⋈）——后台结果主线程应用
            sb.Append("            // [段7] inbox 汇合（⋈）——后台结果主线程应用\n");
            sb.Append("            _" + name + "_Inbox.Drain(delegate (" + name + "_WorkerResult r)\n");
            sb.Append("            {\n");
            sb.Append("                _" + name + "_Busy = false;\n");
            for (int p = 0; p < outNames.Count; p++)
            {
                sb.Append("                _" + outNames[p] + " = r." + outNames[p] + ";\n");
            }
            sb.Append("                string stage = \"ok\";\n");
            sb.Append("                if (!r.Ok)\n");
            sb.Append("                {\n");
            sb.Append("                    stage = \"error\";\n");
            sb.Append("                }\n");
            sb.Append("                AuditBrick(stage, \"" + law.Name + "\", \"" + brickLabel + "\", frame);\n");
            if (law.Attrs.IsLogging)
            {
                sb.Append("                if (LogStore.AllLog != null) { LogStore.Add(\"LAW\", 0, \"" + law.Name + " | \" + stage + \" | frame=\" + frame, \"\"); }\n");
            }
            sb.Append("                if (_" + name + "_TimedOut)\n");
            sb.Append("                {\n");
            sb.Append("                    // 超时后到达——结果丢弃（超时分支已应用失败后置与资源释放）\n");
            sb.Append("                    return;\n");
            sb.Append("                }\n");
            for (int r = 0; r < resources.Count; r++)
            {
                sb.Append("                " + resources[r] + "_Count = " + resources[r] + "_Count + 1;\n");
            }
            if (hasTimeout)
            {
                sb.Append("                " + name + "_Cube.Complete();\n");
            }
            if (law.Results.Count == 1)
            {
                sb.Append("                " + BuildResultStatement(law.Results[0]) + "\n");
            }
            else if (law.Results.Count >= 2)
            {
                sb.Append("                if (r.Ok)\n");
                sb.Append("                {\n");
                sb.Append("                    " + BuildResultStatement(law.Results[0]) + "\n");
                sb.Append("                }\n");
                sb.Append("                else\n");
                sb.Append("                {\n");
                sb.Append("                    " + BuildResultStatement(law.Results[1]) + "\n");
                sb.Append("                }\n");
            }
            sb.Append("            });\n");
            sb.Append("        }\n");
            sb.Append("\n");
        }}
}
