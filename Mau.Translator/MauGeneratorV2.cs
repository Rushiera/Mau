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

            // [段2] 注入字段
            if (doc.Injections.Count > 0)
            {
                sb.Append("        // [注入字段]\n");
                for (int i = 0; i < doc.Injections.Count; i++)
                {
                    sb.Append("        private string? _" + doc.Injections[i] + ";\n");
                }
                sb.Append("\n");
            }

            // [段3] 状态机——枚举 + 字段
            for (int i = 0; i < doc.Machines.Count; i++)
            {
                MachineV2 m = doc.Machines[i];
                bool hasChild = HasChildBind(doc, m.Name);
                sb.Append("        // [状态机 " + m.Name + (hasChild ? "（含嵌套子机）" : "") + "]\n");
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
                sb.Append("        private " + m.Name + "_State " + m.Name + "_State;\n");
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
                    sb.Append("        private " + m.Name + "_State " + m.Name + "_State;\n");
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
                    sb.Append("        private int " + SanitizeName(doc.Resources[i].Name) + "_Count;\n");
                }
                sb.Append("\n");
            }

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
                        sb.Append("        private readonly Cube " + SanitizeName(doc.Laws[i].Name) + "_Cube = new Cube();\n");
                    }
                }
                sb.Append("\n");
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

            sb.Append("    }\n");
            sb.Append("}\n");
            return sb.ToString();
        }

        /// <summary>
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
                            sb.Append("            if (" + SanitizeName(myBind.ParentName) + "_State != " + SanitizeName(myBind.ParentName) + "_State." + SanitizeName(myBind.ParentState) + ")\n");
                            sb.Append("            {\n");
                            sb.Append("                " + SanitizeName(myBind.ParentName) + "_State = " + SanitizeName(myBind.ParentName) + "_State." + SanitizeName(myBind.ParentState) + ";\n");
                            sb.Append("            }\n");
                        }
                    }
                    sb.Append("            " + m.Name + "_State = " + m.Name + "_State." + stateName + ";\n");
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
                            sb.Append("            " + child + "_State = " + child + "_State.None;\n");
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
                    sb.Append("        public bool Is" + m.Name.Replace("S_", "") + stateName + "() { return " + m.Name + "_State == " + m.Name + "_State." + stateName + "; }\n");
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
                sb.Append("            switch (" + m.Name + "_State)\n");
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
                sb.Append("            switch (" + m.Name + "_State)\n");
                sb.Append("            {\n");
                for (int b = 0; b < m.Binds.Count; b++)
                {
                    MachineBindV2 bind = m.Binds[b];
                    string child = SanitizeName(bind.ChildName);
                    sb.Append("                case " + stateEnum + "." + SanitizeName(bind.ParentState) + ": return \"" + bind.ParentState + ".\" + " + child + "_State.ToString();\n");
                }
                sb.Append("                default: return " + m.Name + "_State.ToString();\n");
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
                    sb.Append("            bool result = " + BuildBrickCall(m.Sample) + ";\n");
                    sb.Append("            " + SanitizeName(m.Target) + " = result;\n");
                }
                else
                {
                    sb.Append("            if (" + name + "_FrameCounter % " + m.Frame.ToString() + " == 0)\n");
                    sb.Append("            {\n");
                    sb.Append("                bool result = " + BuildBrickCall(m.Sample) + ";\n");
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
                // [段4] 操作
                if (law.Ops.Count > 0)
                {
                    sb.Append("                // [段4] 操作——顺序执行 + try-catch 隔离\n");
                    sb.Append("                bool ok = false;\n");
                    sb.Append("                try\n");
                    sb.Append("                {\n");
                    for (int o = 0; o < law.Ops.Count; o++)
                    {
                        BrickCallV2 call = law.Ops[o];
                        sb.Append("                    // [AUDIT] brick.invoke | " + law.Name + " | " + call.BrickName + "\n");
                        sb.Append("                    ok = " + BuildBrickCall(call) + ";\n");
                        sb.Append("                    // [AUDIT] brick.ok | " + law.Name + " | " + call.BrickName + "\n");
                    }
                    sb.Append("                }\n");
                    sb.Append("                catch (Exception ex)\n");
                    sb.Append("                {\n");
                    sb.Append("                    ok = false;\n");
                    sb.Append("                    // [AUDIT] brick.error | " + law.Name + " | \" + ex.Message + \"\n");
                    sb.Append("                }\n");
                }
                else
                {
                    sb.Append("                // [段4] 操作——无（纯转移控制律）\n");
                    sb.Append("                bool ok = true;\n");
                }
                // [段5] 日志标记
                if (law.Attrs.IsLogging)
                {
                    sb.Append("                // [LOG] LAW | " + law.Name + " | frame=\" + frame + \" | 触发\n");
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
                        sb.Append("                    // [LOG] LAW | " + law.Name + " | frame=\" + frame + \" | 成功\n");
                    }
                    sb.Append("                }\n");
                    sb.Append("                else\n");
                    sb.Append("                {\n");
                    sb.Append("                    " + BuildResultStatement(law.Results[1]) + "\n");
                    if (law.Attrs.IsLogging)
                    {
                        sb.Append("                    // [LOG] LAW | " + law.Name + " | frame=\" + frame + \" | 失败\n");
                    }
                    sb.Append("                }\n");
                }
                else
                {
                    // 多路——名称返回积木：switch(matched) 按名分发，末项 = default 兜底
                    sb.Append("                string matched = " + BuildBrickCall(law.Ops[0]) + ";\n");
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
                // 资源释放
                if (resources.Count > 0)
                {
                    sb.Append("                // [段6] 资源释放\n");
                    for (int r = 0; r < resources.Count; r++)
                    {
                        sb.Append("                " + resources[r] + "_Count = " + resources[r] + "_Count + 1;\n");
                    }
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
                        sb.Append("                    // [LOG] LAW | " + law.Name + " | frame=\" + frame + \" | 超时\n");
                    }
                    if (law.Results.Count >= 2)
                    {
                        sb.Append("                    " + BuildResultStatement(law.Results[1]) + "\n");
                    }
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
                return SanitizeName(cond.MachineName!) + "_State == " + SanitizeName(cond.MachineName!) + "_State." + SanitizeName(cond.StateName!);
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
        /// 积木调用 → C# 调用表达式（Bricks 静态类占位——M7 按积木契约替换）
        /// </summary>
        /// <param name="call">积木调用</param>
        /// <returns>C# 表达式</returns>
        private static string BuildBrickCall(BrickCallV2 call)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("Bricks.");
            sb.Append(ToPascal(call.BrickName));
            sb.Append("(");
            for (int p = 0; p < call.Params.Count; p++)
            {
                if (p > 0)
                {
                    sb.Append(", ");
                }
                sb.Append(BuildParamExpr(call.Params[p]));
            }
            sb.Append(")");
            return sb.ToString();
        }

        /// <summary>
        /// 参数项 → C# 表达式
        /// </summary>
        /// <param name="param">参数项</param>
        /// <returns>C# 表达式</returns>
        private static string BuildParamExpr(MauParamV2 param)
        {
            if (param.Kind == MauParamKindV2.Ref)
            {
                return SanitizeName(param.Text);
            }
            if (param.Kind == MauParamKindV2.Number)
            {
                return param.Text;
            }
            if (param.Kind == MauParamKindV2.String)
            {
                return QuoteString(param.Text);
            }
            if (param.Kind == MauParamKindV2.Array)
            {
                StringBuilder sb = new StringBuilder();
                sb.Append("new string[] { ");
                for (int i = 0; i < param.Items!.Count; i++)
                {
                    if (i > 0)
                    {
                        sb.Append(", ");
                    }
                    sb.Append(BuildParamExpr(param.Items[i]));
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
    }
}
