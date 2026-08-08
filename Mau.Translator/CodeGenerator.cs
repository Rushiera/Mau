using System;
using System.Collections.Generic;
using System.Text;
using Mau.Contracts;

namespace Mau.Translator
{
    /// <summary>
    /// 代码生成器——IR 到 C#，确定性输出（黄金文件可比）
    /// </summary>
    public static class CodeGenerator
    {
        /// <summary>
        /// 生成 C# 源码
        /// </summary>
        /// <param name="doc">验证通过的文档</param>
        /// <param name="flowName">流程名——PascalCase，类名为 FL_ + flowName</param>
        /// <returns>C# 源码文本</returns>
        public static string Generate(MauDocument doc, string flowName)
        {
            StringBuilder sb = new StringBuilder();
            string className = "FL_" + flowName;

            // [段1] 头部注释——不含时间戳，保证字节级稳定
            sb.AppendLine("// 本文件由 Mau Translator v0.1 自动生成 —— 请勿手改");
            sb.AppendLine("// 流程: " + flowName);
            sb.AppendLine("// 基座: " + (doc.BaseName.Length > 0 ? doc.BaseName : "Mau.Runtime/v0.1"));
            sb.AppendLine();
            sb.AppendLine("using Mau.Runtime;");
            sb.AppendLine("using System.Threading.Tasks;");
            sb.AppendLine();
            sb.AppendLine("namespace Mau.Generated.Flows");
            sb.AppendLine("{");

            // [段2] 类声明
            sb.AppendLine("    /// <summary>");
            sb.AppendLine("    /// " + flowName + " 流程——由 Mau 声明生成");
            sb.AppendLine("    /// </summary>");
            string classLine = "    public sealed class " + className;
            bool hasFlowInterface = false;
            for (int k = 0; k < doc.Interfaces.Count; k++)
            {
                if (doc.Interfaces[k].EndsWith("IObservableFlow", StringComparison.Ordinal))
                {
                    hasFlowInterface = true;
                }
            }
            if (!hasFlowInterface)
            {
                classLine = classLine + " : IObservableFlow";
            }
            for (int k = 0; k < doc.Interfaces.Count; k++)
            {
                if (k == 0 && !hasFlowInterface)
                {
                    classLine = classLine + ", " + doc.Interfaces[k];
                }
                else if (hasFlowInterface)
                {
                    classLine = classLine + (k == 0 ? " : " : ", ") + doc.Interfaces[k];
                }
                else
                {
                    classLine = classLine + ", " + doc.Interfaces[k];
                }
            }
            sb.AppendLine(classLine);
            sb.AppendLine("    {");

            // [段2b] 观察协议字段——帧号 + 调试日志
            sb.AppendLine("        /// <summary>");
            sb.AppendLine("        /// 内部帧号——每 Tick 自增");
            sb.AppendLine("        /// </summary>");
            sb.AppendLine("        private long _frame;");
            sb.AppendLine();
            sb.AppendLine("        /// <summary>");
            sb.AppendLine("        /// 环形调试日志——200 条上限");
            sb.AppendLine("        /// </summary>");
            sb.AppendLine("        private FlowLog _logs;");
            sb.AppendLine();
            sb.AppendLine("        /// <summary>");
            sb.AppendLine("        /// 数据流追踪开关——SetTraceDataFlow 控制（D2 调试基建：输出赋值/信号投递消费记录）");
            sb.AppendLine("        /// </summary>");
            sb.AppendLine("        private bool _traceDataFlow;");
            sb.AppendLine();

            // [段3] 命题字段
            for (int i = 0; i < doc.Propositions.Count; i++)
            {
                IrProposition p = doc.Propositions[i];
                sb.AppendLine("        /// <summary>");
                sb.AppendLine("        /// 命题 " + p.Name + "：" + PropositionComment(p.Kind));
                sb.AppendLine("        /// </summary>");
                sb.AppendLine("        private bool " + p.Name + ";");
                sb.AppendLine();
            }
            // [段3b] 资源字段——独占=1 配额=N；前置检查槽位
            for (int i = 0; i < doc.Resources.Count; i++)
            {
                IrResource r = doc.Resources[i];
                string resInit = r.Kind == "独占" ? "1" : r.Quota.ToString();
                sb.AppendLine("        /// <summary>");
                sb.AppendLine("        /// 资源 " + r.Name + "：槽位 " + resInit + "（" + r.Kind + "）");
                sb.AppendLine("        /// </summary>");
                sb.AppendLine("        private int " + r.Name + "_avail;");
                sb.AppendLine();
            }

            // [段4] 动作参数字段——输入参数名 = 端口名；输出端口字段从契约声明
            HashSet<string> _paramFields = new HashSet<string>();
            for (int i = 0; i < doc.Transitions.Count; i++)
            {
                IrTransition t = doc.Transitions[i];
                for (int p = 0; p < t.Params.Count; p++)
                {
                    // 箭头绑定（变量→端口）不生成端口字段——参数值来自源变量字段（其他变迁输出）；常量绑定不生成字段（字面量直传）
                    if (t.Params[p].IsArrow || t.Params[p].IsConstant)
                    {
                        continue;
                    }
                    string fieldName = "_" + t.Params[p].PortName;
                    if (_paramFields.Contains(fieldName))
                    {
                        continue;
                    }
                    _paramFields.Add(fieldName);
                    string typeName = PortTypeName(doc, t, t.Params[p].PortName);
                    string initializer = IsReferenceType(doc, t, t.Params[p].PortName) ? " = null!;" : ";";
                    sb.AppendLine("        /// <summary>");
                    sb.AppendLine("        /// 变迁 " + t.Name + " 的动作参数——" + t.Params[p].PortName);
                    sb.AppendLine("        /// </summary>");
                    sb.AppendLine("        private " + typeName + " " + fieldName + initializer);
                    sb.AppendLine();
                }
                // 输出端口字段——从积木契约 Outputs 声明
                BrickContract? outContract = null;
                if (t.BrickName.Length > 0 && BrickIndex.TryGet(t.BrickName, out BrickIndexEntry outEntry) && (outContract = outEntry.Contract) != null)
                {
                    for (int o = 0; o < outContract.Outputs.Count; o++)
                    {
                        string outField = "_" + outContract.Outputs[o].Name;
                        if (_paramFields.Contains(outField))
                        {
                            continue;
                        }
                        _paramFields.Add(outField);
                        string outType = TypeName(outContract.Outputs[o].Type);
                        string outInit = outContract.Outputs[o].Type.IsValueType ? ";" : " = null!;";
                        sb.AppendLine("        /// <summary>");
                        sb.AppendLine("        /// 变迁 " + t.Name + " 的输出端口——" + outContract.Outputs[o].Name);
                        sb.AppendLine("        /// </summary>");
                        sb.AppendLine("        private " + outType + " " + outField + outInit);
                        sb.AppendLine("        /// <summary>");
                        sb.AppendLine("        /// 输出端口 " + outContract.Outputs[o].Name + "——宿主只读");
                        sb.AppendLine("        /// </summary>");
                        sb.AppendLine("        public " + outType + " " + outContract.Outputs[o].Name);
                        sb.AppendLine("        {");
                        sb.AppendLine("            get { return " + outField + "; }");
                        sb.AppendLine("        }");
                        sb.AppendLine();
                    }
                }
            }

            // [段5] Cube 字段——有时限的变迁
            for (int i = 0; i < doc.Transitions.Count; i++)
            {
                IrTransition t = doc.Transitions[i];
                if (t.HasTimeout && t.TimeoutMode != "None")
                {
                    sb.AppendLine("        /// <summary>");
                    sb.AppendLine("        /// 变迁 " + t.Name + " 的时限 Cube（" + CubeComment(t) + "）");
                    sb.AppendLine("        /// </summary>");
                    sb.AppendLine("        private Cube " + t.Name + "_Cube;");
                    sb.AppendLine();
                }
            }

            // [段5b] Inbox 字段——worker 线程 + inbox 汇合的变迁；无时限 worker 另加 Busy 门
            for (int i = 0; i < doc.Transitions.Count; i++)
            {
                IrTransition t = doc.Transitions[i];
                if (t.Thread == "worker" && t.Join == "inbox")
                {
                    sb.AppendLine("        /// <summary>");
                    sb.AppendLine("        /// 变迁 " + t.Name + " 的 Inbox 双缓冲——后台结果回投");
                    sb.AppendLine("        /// </summary>");
                    sb.AppendLine("        private Inbox<bool> " + t.Name + "_Inbox;");
                    sb.AppendLine();
                }
                if (t.Thread == "worker" && t.Join == "inbox"
                    && !(t.HasTimeout && t.TimeoutMode != "None"))
                {
                    sb.AppendLine("        /// <summary>");
                    sb.AppendLine("        /// 变迁 " + t.Name + " 的 Busy 门——无时限 worker 防重复启动");
                    sb.AppendLine("        /// </summary>");
                    sb.AppendLine("        private bool " + t.Name + "_Busy;");
                    sb.AppendLine();
                }
            }

            // [段6] 构造——_logs 始终初始化，Cube/Inbox 按需
            sb.AppendLine("        /// <summary>");
            string ctorComment = "构造：初始化日志缓冲";
            bool hasC = HasCube(doc);
            bool hasI = false;
            for (int i = 0; i < doc.Transitions.Count; i++)
            {
                if (doc.Transitions[i].Thread == "worker" && doc.Transitions[i].Join == "inbox")
                {
                    hasI = true;
                    break;
                }
            }
            if (hasC || hasI) { ctorComment = ctorComment + "与 Cube/Inbox"; }
            sb.AppendLine("        /// " + ctorComment);
            sb.AppendLine("        /// </summary>");
            sb.AppendLine("        public " + className + "()");
            sb.AppendLine("        {");
            sb.AppendLine("            _logs = new FlowLog();");
            for (int i = 0; i < doc.Transitions.Count; i++)
            {
                IrTransition t = doc.Transitions[i];
                if (t.HasTimeout && t.TimeoutMode != "None")
                {
                    sb.AppendLine("            " + t.Name + "_Cube = new Cube(" + CubeInit(t) + ");");
                }
            }
            for (int i = 0; i < doc.Transitions.Count; i++)
            {
                IrTransition t = doc.Transitions[i];
                if (t.Thread == "worker" && t.Join == "inbox")
                {
                    sb.AppendLine("            " + t.Name + "_Inbox = new Inbox<bool>();");
                }
            }
            for (int i = 0; i < doc.Resources.Count; i++)
            {
                IrResource r = doc.Resources[i];
                string resInit = r.Kind == "独占" ? "1" : r.Quota.ToString();
                sb.AppendLine("            " + r.Name + "_avail = " + resInit + ";");
            }
            sb.AppendLine("        }");
            sb.AppendLine();

            // [段7] Fire 方法——每个信号命题一个外部投递入口
            for (int i = 0; i < doc.Propositions.Count; i++)
            {
                IrProposition p = doc.Propositions[i];
                if (p.Kind != PropositionKind.Signal)
                {
                    continue;
                }
                AppendFireMethod(sb, doc, p);
            }

            // [段8] Tick 方法
            AppendTickMethod(sb, doc, className);

            // [段8c] GetStatus 方法——运行时状态快照
            sb.AppendLine("        /// <summary>");
            sb.AppendLine("        /// 获取运行时状态快照——全量截面");
            sb.AppendLine("        /// </summary>");
            sb.AppendLine("        /// <returns>当前帧状态</returns>");
            sb.AppendLine("        public RuntimeStatus GetStatus()");
            sb.AppendLine("        {");
            sb.Append("            PropSnapshot[] props = new PropSnapshot[");
            sb.Append(doc.Propositions.Count.ToString());
            sb.AppendLine("];");
            for (int i = 0; i < doc.Propositions.Count; i++)
            {
                IrProposition p = doc.Propositions[i];
                string kindStr = "Fact";
                if (p.Kind == PropositionKind.Condition) { kindStr = "Condition"; }
                else if (p.Kind == PropositionKind.Signal) { kindStr = "Signal"; }
                sb.AppendLine("            props[" + i.ToString() + "] = new PropSnapshot(\"" + p.Name + "\", \"" + kindStr + "\", " + p.Name + ");");
            }
            sb.Append("            TransSnapshot[] trans = new TransSnapshot[");
            sb.Append(doc.Transitions.Count.ToString());
            sb.AppendLine("];");
            for (int i = 0; i < doc.Transitions.Count; i++)
            {
                IrTransition t = doc.Transitions[i];
                string cubeVar = t.HasTimeout && t.TimeoutMode != "None" ? t.Name + "_Cube" : "null";
                if (t.HasTimeout && t.TimeoutMode != "None")
                {
                    sb.AppendLine("            trans[" + i.ToString() + "] = new TransSnapshot(\"" + t.Name + "\", " + cubeVar + ".State.ToString(), " + cubeVar + ".ElapsedFrames, " + cubeVar + ".LimitFrames);");
                }
                else
                {
                    sb.AppendLine("            trans[" + i.ToString() + "] = new TransSnapshot(\"" + t.Name + "\", \"Idle\", 0, 0);");
                }
            }
            sb.Append("            ResSnapshot[] res = new ResSnapshot[");
            sb.Append(doc.Resources.Count.ToString());
            sb.AppendLine("];");
            for (int i = 0; i < doc.Resources.Count; i++)
            {
                IrResource r = doc.Resources[i];
                if (r.Kind == "独占")
                {
                    sb.AppendLine("            res[" + i.ToString() + "] = new ResSnapshot(\"" + r.Name + "\", true, 1 - " + r.Name + "_avail, 1);");
                }
                else
                {
                    sb.AppendLine("            res[" + i.ToString() + "] = new ResSnapshot(\"" + r.Name + "\", false, " + r.Quota.ToString() + " - " + r.Name + "_avail, " + r.Quota.ToString() + ");");
                }
            }
            sb.AppendLine("            return new RuntimeStatus(_frame, props, trans, res);");
            sb.AppendLine("        }");
            sb.AppendLine();

            // [段8d] GetLogs 方法——全量调试日志
            sb.AppendLine("        /// <summary>");
            sb.AppendLine("        /// 获取全量调试日志");
            sb.AppendLine("        /// </summary>");
            sb.AppendLine("        /// <returns>日志数组，时间顺序</returns>");
            sb.AppendLine("        public MauDebug[] GetLogs()");
            sb.AppendLine("        {");
            sb.AppendLine("            return _logs.GetAll();");
            sb.AppendLine("        }");
            sb.AppendLine();
            sb.AppendLine("        /// <summary>");
            sb.AppendLine("        /// 开启数据流追踪——输出端口赋值/信号投递消费记录 MauDebug（D2 调试基建）");
            sb.AppendLine("        /// </summary>");
            sb.AppendLine("        /// <param name=\"enabled\">true=记录数据流日志</param>");
            sb.AppendLine("        public void SetTraceDataFlow(bool enabled)");
            sb.AppendLine("        {");
            sb.AppendLine("            _traceDataFlow = enabled;");
            sb.AppendLine("        }");
            sb.AppendLine();

            // [段8b] 组合声明——文档化序列/并行/选择/重试
            // ⚠️ Composition 为 experimental（documentation-only）——执行语义未实现，勿作为生产构筑依赖
            for (int i = 0; i < doc.Compositions.Count; i++)
            {
                IrComposition c = doc.Compositions[i];
                sb.AppendLine("        // 组合 " + c.Name + ":  [experimental — documentation-only，执行语义未实现]");
                if (c.Sequence.Count > 0)
                {
                    sb.Append("        //   序列: ");
                    for (int s = 0; s < c.Sequence.Count; s++)
                    {
                        if (s > 0)
                        {
                            sb.Append(", ");
                        }
                        sb.Append(c.Sequence[s]);
                    }
                    sb.AppendLine();
                }
                if (c.Parallel.Count > 0)
                {
                    sb.Append("        //   并行: ");
                    for (int p = 0; p < c.Parallel.Count; p++)
                    {
                        if (p > 0)
                        {
                            sb.Append(", ");
                        }
                        sb.Append(c.Parallel[p]);
                    }
                    sb.AppendLine();
                }
                if (c.Choice.Length > 0)
                {
                    sb.AppendLine("        //   选择: " + c.Choice);
                }
                if (c.Retry > 0)
                {
                    sb.AppendLine("        //   重试: " + c.Retry);
                }
                if (c.Merge.Length > 0)
                {
                    sb.AppendLine("        //   汇合: " + c.Merge);
                }
                sb.AppendLine();
            }

            // [段9] 查询方法——每个事实命题一个 IsXxx
            for (int i = 0; i < doc.Propositions.Count; i++)
            {
                IrProposition p = doc.Propositions[i];
                if (p.Kind != PropositionKind.Fact)
                {
                    continue;
                }
                sb.AppendLine("        /// <summary>");
                sb.AppendLine("        /// 查询结果：" + p.Name.Substring(2));
                sb.AppendLine("        /// </summary>");
                sb.AppendLine("        /// <returns>" + p.Name.Substring(2) + "成立</returns>");
                sb.AppendLine("        public bool Is" + p.Name.Substring(2) + "()");
                sb.AppendLine("        {");
                sb.AppendLine("            return " + p.Name + ";");
                sb.AppendLine("        }");
                sb.AppendLine();
                // 显式重置——重置: 显式（默认）生成 Reset 方法，宿主按需调用
                if (p.Reset != "轮末")
                {
                    sb.AppendLine("        /// <summary>");
                    sb.AppendLine("        /// 重置结果：" + p.Name.Substring(2));
                    sb.AppendLine("        /// </summary>");
                    sb.AppendLine("        public void Reset" + p.Name.Substring(2) + "()");
                    sb.AppendLine("        {");
                    sb.AppendLine("            " + p.Name + " = false;");
                    sb.AppendLine("        }");
                    sb.AppendLine();
                }
            }

            // [段10] 类收尾
            sb.AppendLine("    }");
            sb.AppendLine("}");

            return sb.ToString();
        }

        /// <summary>
        /// 命题注释描述
        /// </summary>
        /// <param name="kind">命题类型</param>
        /// <returns>注释文本</returns>
        private static string PropositionComment(PropositionKind kind)
        {
            if (kind == PropositionKind.Condition)
            {
                return "状态查询，真值自由变化";
            }
            if (kind == PropositionKind.Signal)
            {
                return "信号，消费即清除";
            }
            return "终态事实，置位后保持";
        }

        /// <summary>
        /// Cube 注释描述
        /// </summary>
        /// <param name="t">变迁</param>
        /// <returns>注释文本</returns>
        private static string CubeComment(IrTransition t)
        {
            if (t.TimeoutMode == "Idle")
            {
                return "空闲 " + t.TimeoutFrames + " 帧超时";
            }
            return t.TimeoutFrames + " 帧有限模式";
        }

        /// <summary>
        /// Cube 构造参数
        /// </summary>
        /// <param name="t">变迁</param>
        /// <returns>构造参数文本</returns>
        private static string CubeInit(IrTransition t)
        {
            if (t.TimeoutMode == "Idle")
            {
                return "CubeMode.Idle, " + t.TimeoutFrames;
            }
            return t.TimeoutFrames.ToString();
        }

        /// <summary>
        /// 是否生成了 Cube 字段
        /// </summary>
        /// <param name="doc">文档</param>
        /// <returns>存在为真</returns>
        private static bool HasCube(MauDocument doc)
        {
            for (int i = 0; i < doc.Transitions.Count; i++)
            {
                IrTransition t = doc.Transitions[i];
                if (t.HasTimeout && t.TimeoutMode != "None")
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// 端口类型名——从积木契约查询
        /// </summary>
        /// <param name="doc">文档</param>
        /// <param name="t">变迁</param>
        /// <param name="portName">端口名</param>
        /// <returns>C# 类型名</returns>
        private static string PortTypeName(MauDocument doc, IrTransition t, string portName)
        {
            BrickContract? contract;
            if (BrickIndex.TryGet(t.BrickName, out BrickIndexEntry entry) && (contract = entry.Contract) != null)
            {
                for (int i = 0; i < contract.Inputs.Count; i++)
                {
                    if (contract.Inputs[i].Name == portName)
                    {
                        return TypeName(contract.Inputs[i].Type);
                    }
                }
            }
            return "string";
        }

        /// <summary>
        /// 端口是否为引用类型——决定字段 null 初始化
        /// </summary>
        /// <param name="doc">文档</param>
        /// <param name="t">变迁</param>
        /// <param name="portName">端口名</param>
        /// <returns>引用类型为真</returns>
        private static bool IsReferenceType(MauDocument doc, IrTransition t, string portName)
        {
            BrickContract? contract;
            if (BrickIndex.TryGet(t.BrickName, out BrickIndexEntry entry) && (contract = entry.Contract) != null)
            {
                for (int i = 0; i < contract.Inputs.Count; i++)
                {
                    if (contract.Inputs[i].Name == portName)
                    {
                        return !contract.Inputs[i].Type.IsValueType;
                    }
                }
            }
            return true;
        }

        /// <summary>
        /// 端口描述——生成参数注释
        /// </summary>
        /// <param name="doc">文档</param>
        /// <param name="t">变迁</param>
        /// <param name="portName">端口名</param>
        /// <returns>描述文本</returns>
        private static string PortDescription(MauDocument doc, IrTransition t, string portName)
        {
            BrickContract? contract;
            if (BrickIndex.TryGet(t.BrickName, out BrickIndexEntry entry) && (contract = entry.Contract) != null)
            {
                for (int i = 0; i < contract.Inputs.Count; i++)
                {
                    if (contract.Inputs[i].Name == portName)
                    {
                        if (contract.Inputs[i].Description.Length > 0)
                        {
                            return contract.Inputs[i].Description;
                        }
                        return "参数 " + portName;
                    }
                }
            }
            return "参数 " + portName;
        }

        /// <summary>
        /// Type 到 C# 类型名
        /// </summary>
        /// <param name="type">类型</param>
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
                string fullBase = (type.Namespace ?? "").Length > 0
                    ? ((type.Namespace ?? "") + "." + baseName) : baseName;
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
            // 引用类型——全限定名（生成物 using 仅基座，端口类型须可解析）
            string full = (type.Namespace ?? "").Length > 0
                ? ((type.Namespace ?? "") + "." + type.Name) : type.Name;
            return full;
        }

        /// <summary>
        /// 生成 Fire 方法——信号命题的外部投递入口
        /// </summary>
        /// <param name="sb">输出缓冲</param>
        /// <param name="doc">文档</param>
        /// <param name="p">信号命题</param>
        private static void AppendFireMethod(StringBuilder sb, MauDocument doc, IrProposition p)
        {
            // 收集引用该信号的所有变迁的动作参数（去重）——仅简写绑定（外部注入），箭头绑定来自积木输出
            List<IrParamBinding> params_ = new List<IrParamBinding>();
            for (int i = 0; i < doc.Transitions.Count; i++)
            {
                IrTransition t = doc.Transitions[i];
                // 前置含信号——支持析取组（∨）内匹配
                if (!TransitionReferencesSignal(doc, t, p.Name))
                {
                    continue;
                }
                for (int a = 0; a < t.Params.Count; a++)
                {
                    // 仅收简写注入（非箭头非常量）——箭头绑定来自字段/常量，不来自 Fire
                    if (t.Params[a].IsArrow || t.Params[a].IsConstant)
                    {
                        continue;
                    }
                    bool exists = false;
                    for (int b = 0; b < params_.Count; b++)
                    {
                        if (params_[b].PortName == t.Params[a].PortName)
                        {
                            exists = true;
                        }
                    }
                    if (!exists)
                    {
                        params_.Add(t.Params[a]);
                    }
                }
            }

            string methodName = "Fire" + p.Name.Substring(2);
            sb.AppendLine("        /// <summary>");
            sb.AppendLine("        /// 外部投递信号：" + p.Name.Substring(2));
            sb.AppendLine("        /// </summary>");
            for (int i = 0; i < params_.Count; i++)
            {
                IrTransition? t = FindTransitionByPort(doc, p.Name, params_[i].PortName);
                string desc = PortDescription(doc, t!, params_[i].PortName);
                sb.AppendLine("        /// <param name=\"" + params_[i].PortName + "\">" + desc + "</param>");
            }

            StringBuilder signature = new StringBuilder();
            signature.Append("public void " + methodName + "(");
            for (int i = 0; i < params_.Count; i++)
            {
                if (i > 0)
                {
                    signature.Append(", ");
                }
                IrTransition? t = FindTransitionByPort(doc, p.Name, params_[i].PortName);
                string typeName = PortTypeName(doc, t!, params_[i].PortName);
                signature.Append(typeName + " " + params_[i].PortName);
            }
            signature.Append(")");
            sb.AppendLine("        " + signature.ToString());
            sb.AppendLine("        {");
            for (int i = 0; i < params_.Count; i++)
            {
                sb.AppendLine("            _" + params_[i].PortName + " = " + params_[i].PortName + ";");
            }
            sb.AppendLine("            if (_traceDataFlow) { _logs.Add(new MauDebug(_frame, \"Fire\", \"Signal\", \"" + p.Name + "\")); }");
            sb.AppendLine("            " + p.Name + " = true;");
            sb.AppendLine("        }");
            sb.AppendLine();
        }

        /// <summary>
        /// 变迁前置是否引用指定信号——支持析取组（∨）内匹配
        /// </summary>
        /// <param name="doc">文档</param>
        /// <param name="t">变迁</param>
        /// <param name="signalName">信号名</param>
        /// <returns>引用为真</returns>
        private static bool TransitionReferencesSignal(MauDocument doc,
            IrTransition t, string signalName)
        {
            for (int p = 0; p < t.Preconditions.Count; p = p + 1)
            {
                if (t.Preconditions[p] == signalName)
                {
                    return true;
                }
                string[] orParts = t.Preconditions[p].Split('∨');
                for (int o = 0; o < orParts.Length; o = o + 1)
                {
                    if (orParts[o].Trim() == signalName)
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        /// <summary>
        /// 找引用指定信号且含指定端口的变迁
        /// </summary>
        /// <param name="doc">文档</param>
        /// <param name="signalName">信号名</param>
        /// <param name="portName">端口名</param>
        /// <returns>变迁</returns>
        private static IrTransition? FindTransitionByPort(MauDocument doc, string signalName, string portName)
        {
            for (int i = 0; i < doc.Transitions.Count; i++)
            {
                IrTransition t = doc.Transitions[i];
                if (!TransitionReferencesSignal(doc, t, signalName))
                {
                    continue;
                }
                for (int a = 0; a < t.Params.Count; a++)
                {
                    if (t.Params[a].PortName == portName)
                    {
                        return t;
                    }
                }
            }
            return doc.Transitions.Count > 0 ? doc.Transitions[0] : null;
        }

        /// <summary>
        /// 生成 Tick 方法——前置检查/触发/执行/后置注册/时限检查
        /// </summary>
        /// <param name="sb">输出缓冲</param>
        /// <param name="doc">文档</param>
        /// <param name="className">类名</param>
        private static void AppendTickMethod(StringBuilder sb, MauDocument doc, string className)
{
            sb.AppendLine("        /// <summary>");
            sb.AppendLine("        /// 每帧驱动——由主 Tick 调用");
            sb.AppendLine("        /// </summary>");
            sb.AppendLine("        public void Tick()");
            sb.AppendLine("        {");
            sb.AppendLine("            _frame = _frame + 1;");

            for (int i = 0; i < doc.Transitions.Count; i++)
            {
                IrTransition t = doc.Transitions[i];
                AppendTransitionBlock(sb, doc, t);
                if (i < doc.Transitions.Count - 1)
                {
                    sb.AppendLine();
                }
            }

            // 轮末重置——重置: 轮末 的事实命题每帧自动清
            for (int i = 0; i < doc.Propositions.Count; i++)
            {
                IrProposition p = doc.Propositions[i];
                if (p.Kind == PropositionKind.Fact && p.Reset == "轮末")
                {
                    sb.AppendLine("            // 轮末重置——" + p.Name);
                    sb.AppendLine("            " + p.Name + " = false;");
                }
            }

            sb.AppendLine("        }");
            sb.AppendLine();
        }
        /// <summary>
        /// 生成单个变迁的 Tick 块
        /// </summary>
        /// <param name="sb">输出缓冲</param>
        /// <param name="doc">文档</param>
        /// <param name="t">变迁</param>
        private static void AppendTransitionBlock(StringBuilder sb, MauDocument doc, IrTransition t)
{
            bool hasCube = t.HasTimeout && t.TimeoutMode != "None";
            string cubeName = t.Name + "_Cube";
            bool isWorkerInbox = t.Thread == "worker" && t.Join == "inbox";

            if (isWorkerInbox)
            {
                string inboxName = t.Name + "_Inbox";

                // [段A] worker+inbox 前置检查 + 启动后台任务
                string workerGate = PreconditionText(t, hasCube, cubeName, doc);
                if (!hasCube)
                {
                    // 无时限 worker——Busy 门防止重复启动（有 Cube 时 IsIdle 已防重入）
                    workerGate = (workerGate.Length > 0 && workerGate != "true")
                        ? (workerGate + " && !" + t.Name + "_Busy")
                        : ("!" + t.Name + "_Busy");
                }
                sb.AppendLine("            // [" + t.Name + "] worker+inbox 前置检查");
                sb.AppendLine("            if (" + workerGate + ")");
                sb.AppendLine("            {");
                for (int p = 0; p < t.Preconditions.Count; p++)
                {
                    // 析取前置（∨）内的信号同样消费——拆分解析
                    string[] orParts = t.Preconditions[p].Split('∨');
                    for (int o = 0; o < orParts.Length; o = o + 1)
                    {
                        IrProposition? prop = doc.FindProposition(orParts[o].Trim());
                        if (prop != null && prop.Kind == PropositionKind.Signal)
                        {
                            sb.AppendLine("                // 信号消费");
                            sb.AppendLine("                if (_traceDataFlow) { _logs.Add(new MauDebug(_frame, \"" + t.Name + "\", \"Consume\", \"" + prop.Name + "\")); }");
                            sb.AppendLine("                " + prop.Name + " = false;");
                        }
                    }
                }
                if (hasCube)
                {
                    sb.AppendLine("                " + cubeName + ".Start();");
                }
            string debugFiredW = DebugLogLine(t, "Fired", "                ");
            if (debugFiredW.Length > 0)
            {
                sb.AppendLine(debugFiredW);
            }
                sb.AppendLine("                // 启动后台任务——积木在 worker 线程执行，结果回投 inbox");
                sb.AppendLine("                var inbox = " + inboxName + ";");
                if (!hasCube)
                {
                    sb.AppendLine("                " + t.Name + "_Busy = true;");
                }
                // 输入冻结——参数快照为局部变量（后台线程读主线程字段存在竞态）
                AppendInputFreeze(sb, doc, t, "                ");
                AppendResourceAcquire(sb, doc, t, "                ");
                sb.AppendLine("                Task.Run(() =>");
                sb.AppendLine("                {");
                sb.AppendLine("                    try");
                sb.AppendLine("                    {");
                sb.AppendLine("                        bool ok = " + BrickCallText(doc, t, true) + ";");
                sb.AppendLine("                        inbox.Enqueue(ok);");
                sb.AppendLine("                    }");
                sb.AppendLine("                    catch");
                sb.AppendLine("                    {");
                sb.AppendLine("                        // 后台异常——回投失败，走错误后置");
                sb.AppendLine("                        inbox.Enqueue(false);");
                sb.AppendLine("                    }");
                sb.AppendLine("                });");
                sb.AppendLine("            }");
                sb.AppendLine();

                // [段B] inbox 排空——主线程处理后台结果
                sb.AppendLine("            // [" + t.Name + "] inbox 排空");
                sb.AppendLine("            " + inboxName + ".Drain(ok =>");
                sb.AppendLine("            {");
                if (hasCube)
                {
                    sb.AppendLine("                if (!" + cubeName + ".IsRunning())");
                    sb.AppendLine("                {");
                    sb.AppendLine("                    return;");
                    sb.AppendLine("                }");
                }
                sb.AppendLine("                if (ok)");
                sb.AppendLine("                {");
                for (int p = 0; p < t.PostOk.Count; p++)
                {
                    sb.AppendLine("                    " + t.PostOk[p] + " = true;");
                }
                if (hasCube && t.TimeoutMode == "Idle")
                {
                    sb.AppendLine("                    // 空闲时限——事件到达重置空闲计数");
                    sb.AppendLine("                    " + cubeName + ".Touch();");
                }
            string debugOkW = DebugLogLine(t, "Ok", "                    ");
            if (debugOkW.Length > 0)
            {
                sb.AppendLine(debugOkW);
            }
                    AppendResourceRelease(sb, doc, t, "                    ");
                sb.AppendLine("                }");
                sb.AppendLine("                else");

                sb.AppendLine("                {");
                for (int p = 0; p < t.PostError.Count; p++)
                {
                    sb.AppendLine("                    " + t.PostError[p] + " = true;");
                }
            string debugErrorW = DebugLogLine(t, "Error", "                    ");
            if (debugErrorW.Length > 0)
            {
                sb.AppendLine(debugErrorW);
            }
                    AppendResourceRelease(sb, doc, t, "                    ");
                sb.AppendLine("                }");
                if (hasCube)
                {
                    sb.AppendLine("                " + cubeName + ".Complete();");
                }
                else
                {
                    // 无时限 worker——任务结束复位 Busy 门
                    sb.AppendLine("                " + t.Name + "_Busy = false;");
                }
                sb.AppendLine("            });");

                // [段C] 时限检查——与主线程变迁相同
                if (hasCube)
                {
                    sb.AppendLine();
                    sb.AppendLine("            // [" + t.Name + "] 时限检查");
                    sb.AppendLine("            if (" + cubeName + ".IsRunning())");
                    sb.AppendLine("            {");
                    sb.AppendLine("                " + cubeName + ".TickFrame();");
                    sb.AppendLine("                if (" + cubeName + ".IsExpired())");
                    sb.AppendLine("                {");
                    for (int p = 0; p < t.PostError.Count; p++)
                    {
                        sb.AppendLine("                    // 超时 → 错误后置");
                        sb.AppendLine("                    " + t.PostError[p] + " = true;");
                    }
                    AppendResourceRelease(sb, doc, t, "                    ");
            string debugTimeoutW = DebugLogLine(t, "Timeout", "                    ");
            if (debugTimeoutW.Length > 0)
            {
                sb.AppendLine(debugTimeoutW);
            }
                    sb.AppendLine("                    " + cubeName + ".Complete();");
                    sb.AppendLine("                }");

                    sb.AppendLine("            }");
                }
                return;
            }

            // [段A] 前置检查块（主线程同步变迁——原有逻辑）
            sb.AppendLine("            // [" + t.Name + "] 前置检查");
            sb.AppendLine("            if (" + PreconditionText(t, hasCube, cubeName, doc) + ")");
            sb.AppendLine("            {");

            // 信号前置消费——含析取组拆分
            for (int p = 0; p < t.Preconditions.Count; p++)
            {
                string[] orParts = t.Preconditions[p].Split('∨');
                for (int o = 0; o < orParts.Length; o = o + 1)
                {
                    IrProposition? prop = doc.FindProposition(orParts[o].Trim());
                    if (prop != null && prop.Kind == PropositionKind.Signal)
                    {
                        sb.AppendLine("                // 信号消费");
                        sb.AppendLine("                if (_traceDataFlow) { _logs.Add(new MauDebug(_frame, \"" + t.Name + "\", \"Consume\", \"" + prop.Name + "\")); }");
                        sb.AppendLine("                " + prop.Name + " = false;");
                    }
                }
            }

            if (hasCube)
            {
                sb.AppendLine("                " + cubeName + ".Start();");
            }
            string debugFired = DebugLogLine(t, "Fired", "            ");
            if (debugFired.Length > 0)
            {
                sb.AppendLine(debugFired);
            }

            AppendResourceAcquire(sb, doc, t, "                ");
            // 动作调用——参数用字段名
            sb.AppendLine("                // 执行动作（积木调用）");
            sb.AppendLine("                bool ok = " + BrickCallText(doc, t, false) + ";");
            // 输出端口赋值追踪（D2）——从契约遍历输出端口，记录赋值
            BrickContract? traceContract = null;
            if (t.BrickName.Length > 0 && BrickIndex.TryGet(t.BrickName, out BrickIndexEntry traceEntry) && (traceContract = traceEntry.Contract) != null)
            {
                for (int o = 0; o < traceContract.Outputs.Count; o++)
                {
                    sb.AppendLine("                if (_traceDataFlow) { _logs.Add(new MauDebug(_frame, \"" + t.Name + "\", \"Set\", \"_" + traceContract.Outputs[o].Name + "=\" + System.Convert.ToString(_" + traceContract.Outputs[o].Name + "))); }");
                }
            }

            // 后置注册
            sb.AppendLine("                if (ok)");
            sb.AppendLine("                {");
            for (int p = 0; p < t.PostOk.Count; p++)
            {
                sb.AppendLine("                    // 正常后置注册");
                sb.AppendLine("                    " + t.PostOk[p] + " = true;");
            }
            if (hasCube && t.TimeoutMode == "Idle")
            {
                sb.AppendLine("                    // 空闲时限——事件到达重置空闲计数");
                sb.AppendLine("                    " + cubeName + ".Touch();");
            }
            string debugOk = DebugLogLine(t, "Ok", "                    ");
            if (debugOk.Length > 0)
            {
                sb.AppendLine(debugOk);
            }
            sb.AppendLine("                }");
            sb.AppendLine("                else");

            sb.AppendLine("                {");
            for (int p = 0; p < t.PostError.Count; p++)
            {
                sb.AppendLine("                    // 错误后置注册（互斥）");
                sb.AppendLine("                    " + t.PostError[p] + " = true;");
            }
            string debugError = DebugLogLine(t, "Error", "                    ");
            if (debugError.Length > 0)
            {
                sb.AppendLine(debugError);
            }
            sb.AppendLine("                }");
                    AppendResourceRelease(sb, doc, t, "                    ");

            if (hasCube)
            {
                sb.AppendLine();
                sb.AppendLine("                // 同步积木当帧完成");
                sb.AppendLine("                " + cubeName + ".Complete();");
            }

            sb.AppendLine("            }");

            // [段B] 时限检查块
            if (hasCube)
            {
                sb.AppendLine();
                sb.AppendLine("            // [" + t.Name + "] 时限检查");
                sb.AppendLine("            if (" + cubeName + ".IsRunning())");
                sb.AppendLine("            {");
                sb.AppendLine("                " + cubeName + ".TickFrame();");
                sb.AppendLine("                if (" + cubeName + ".IsExpired())");
                sb.AppendLine("                {");
                for (int p = 0; p < t.PostError.Count; p++)
                {
                    sb.AppendLine("                    // 超时 → 错误后置");
                    sb.AppendLine("                    " + t.PostError[p] + " = true;");
                }
            string debugTimeout = DebugLogLine(t, "Timeout", "                    ");
            if (debugTimeout.Length > 0)
            {
                sb.AppendLine(debugTimeout);
            }
                    AppendResourceRelease(sb, doc, t, "                    ");
                sb.AppendLine("                    " + cubeName + ".Complete();");
                sb.AppendLine("                }");
                sb.AppendLine("            }");
            }
        }
/// <summary>
/// 生成调试日志行——sb.AppendLine(_logs.Add(...))
/// </summary>
/// <param name = "t">变迁</param>
/// <param name = "phase">阶段——Fired/Ok/Error/Timeout</param>
/// <param name = "indent">缩进字符串</param>
/// <returns>生成的日志行文本，t.DebugMessage 为空时返回空</returns>
private static string DebugLogLine(IrTransition t, string phase, string indent)
{
    if (t.DebugMessage.Length == 0)
    {
        return "";
    }

    string interpolated = t.DebugMessage;
    int pos = 0;
    while (pos < interpolated.Length - 3)
    {
        int open = interpolated.IndexOf("{{", pos);
        if (open < 0)
        {
            break;
        }

        int close = interpolated.IndexOf("}}", open + 2);
        if (close < 0)
        {
            break;
        }

        string portRef = interpolated.Substring(open + 2, close - open - 2);
        interpolated = interpolated.Substring(0, open) + "{_" + portRef + "}" + interpolated.Substring(close + 2);
        pos = open + portRef.Length + 4;
    }

    string logCode = "_logs.Add(new MauDebug(_frame, \"" + t.Name + "\", \"" + phase + "\", $\"" + interpolated + "\"));";
    return indent + logCode;
}/// <summary>
        /// 前置检查条件文本
        /// </summary>
        /// <param name="t">变迁</param>
        /// <param name="hasCube">是否有时限 Cube</param>
        /// <param name="cubeName">Cube 字段名</param>
        /// <param name="doc">文档</param>
        /// <returns>条件文本</returns>
        private static string PreconditionText(IrTransition t, bool hasCube, string cubeName, MauDocument doc)
{
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < t.Preconditions.Count; i++)
            {
                if (i > 0)
                {
                    sb.Append(" && ");
                }
                string item = t.Preconditions[i];
                if (item.IndexOf('∨') >= 0)
                {
                    string[] orParts = item.Split('∨');
                    sb.Append("(");
                    for (int o = 0; o < orParts.Length; o++)
                    {
                        if (o > 0)
                        {
                            sb.Append(" || ");
                        }
                        string atom = orParts[o].Trim();
                        if (atom.Length > 0)
                        {
                            sb.Append(PreconditionAtom(doc, atom));
                        }
                    }
                    sb.Append(")");
                }
                else
                {
                    sb.Append(PreconditionAtom(doc, item));
                }
            }
            if (hasCube)
            {
                if (t.Preconditions.Count > 0)
                {
                    sb.Append(" && ");
                }
                sb.Append(cubeName + ".IsIdle()");
            }
            if (sb.Length == 0)
            {
                return "true";
            }
            return sb.ToString();
        }
/// <summary>
/// 内嵌实现重写——完整限定名 Mau.Bricks.XxxBrick.Yyy → BRIK_ID.Yyy（BRIK-ID 类名）
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
}        /// <summary>
        /// 积木调用文本——完整限定名，输出端口以 out 前缀传递
        /// </summary>
        /// <param name="doc">文档</param>
        /// <param name="t">变迁</param>
        /// <param name="frozen">冻结模式——输入引用 f_ 局部变量（worker 后台线程安全；主线程传 false）</param>
        /// <returns>调用文本</returns>
        private static string BrickCallText(MauDocument doc, IrTransition t, bool frozen)
{
            BrickIndexEntry? entry;
            string implementation = "";
            if (BrickIndex.TryGet(t.BrickName, out entry))
            {
                // 内嵌重命名——完整限定名 Mau.Bricks.XxxBrick.Yyy → BRIK_ID.Yyy（BRIK-ID 类名）
                implementation = RewriteImplementation(entry.Contract.Implementation, entry.Id);
            }
            StringBuilder sb = new StringBuilder();
            sb.Append(implementation);
            sb.Append("(");
            for (int i = 0; i < t.Params.Count; i++)
            {
                if (i > 0)
                {
                    sb.Append(", ");
                }
                if (t.Params[i].IsConstant)
                {
                    // 常量字面量——数字原样，字符串加引号
                    long num;
                    if (long.TryParse(t.Params[i].ConstantValue, out num))
                    {
                        sb.Append(t.Params[i].ConstantValue);
                    }
                    else
                    {
                        sb.Append("\"" + t.Params[i].ConstantValue + "\"");
                    }
                }
                else if (t.Params[i].IsArray)
                {
                    // 数组字面量——[a,b,c] → new T[] { ... }（B2：语料可构造数组端口）
                    System.Type? elemType = null;
                    if (entry != null)
                    {
                        for (int ip = 0; ip < entry.Contract.Inputs.Count; ip++)
                        {
                            if (entry.Contract.Inputs[ip].Name == t.Params[i].PortName
                                && entry.Contract.Inputs[ip].Type.IsArray)
                            {
                                elemType = entry.Contract.Inputs[ip].Type.GetElementType();
                                break;
                            }
                        }
                    }
                    if (elemType == null)
                    {
                        elemType = typeof(string);
                    }
                    sb.Append("new " + TypeName(elemType) + "[] { ");
                    for (int e = 0; e < t.Params[i].ArrayItems.Count; e++)
                    {
                        if (e > 0)
                        {
                            sb.Append(", ");
                        }
                        string elem = t.Params[i].ArrayItems[e];
                        long num;
                        if (long.TryParse(elem, out num))
                        {
                            sb.Append(num.ToString());
                        }
                        else
                        {
                            string text = elem;
                            if (text.Length >= 2 && text.StartsWith("\"") && text.EndsWith("\""))
                            {
                                text = text.Substring(1, text.Length - 2);
                            }
                            sb.Append("\"" + text + "\"");
                        }
                    }
                    sb.Append(" }");
                }
                else if (frozen)
                {
                    // 冻结模式——输入引用 f_ 局部变量（AppendInputFreeze 生成的快照）
                    sb.Append("f_" + t.Params[i].Variable);
                }
                else
                {
                    // 变量引用——简写（变量=端口名）→ _端口；箭头绑定（变量→端口）→ _变量（源输出字段）
                    sb.Append("_" + t.Params[i].Variable);
                }
            }
            // 输出端口——out 传递，与输入参数拼接
            if (entry != null)
            {
                BrickContract contract = entry.Contract;
                for (int o = 0; o < contract.Outputs.Count; o++)
                {
                    if (t.Params.Count > 0 || o > 0)
                    {
                        sb.Append(", ");
                    }
                    sb.Append("out _" + contract.Outputs[o].Name);
                }
            }
            sb.Append(")");
            return sb.ToString();
        }

        /// <summary>
        /// 输入冻结行——worker 启动前把非常量参数快照为局部变量（f_ 前缀），后台线程不直接读主线程字段
        /// </summary>
        /// <param name="sb">输出缓冲</param>
        /// <param name="doc">文档</param>
        /// <param name="t">变迁</param>
        /// <param name="indent">缩进前缀</param>
        private static void AppendInputFreeze(StringBuilder sb, MauDocument doc, IrTransition t, string indent)
        {
            for (int p = 0; p < t.Params.Count; p++)
            {
                if (t.Params[p].IsConstant)
                {
                    continue;
                }
                string typeName = PortTypeName(doc, t, t.Params[p].PortName);
                sb.AppendLine(indent + typeName + " f_" + t.Params[p].Variable + " = _" + t.Params[p].Variable + ";");
            }
        }/// <summary>
/// 前置项文本——命题直接输出；资源输出槽位检查；析取组由调用方展开
/// </summary>
/// <param name = "doc">文档</param>
/// <param name = "name">前置项名</param>
/// <returns>条件文本</returns>
private static string PreconditionAtom(MauDocument doc, string name)
{
    for (int i = 0; i < doc.Resources.Count; i++)
    {
        if (doc.Resources[i].Name == name)
        {
            return name + "_avail > 0";
        }
    }

    return name;
} 
        /// <summary>
        /// 判断名字是否为已声明资源
        /// </summary>
        /// <param name="doc">文档</param>
        /// <param name="name">名字</param>
        /// <returns>是资源为真</returns>
        private static bool IsResource(MauDocument doc, string name)
        {
            for (int i = 0; i < doc.Resources.Count; i++)
            {
                if (doc.Resources[i].Name == name)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// 变迁占用资源——触发时槽位扣减；析取组不占资源
        /// </summary>
        /// <param name="sb">输出缓冲</param>
        /// <param name="doc">文档</param>
        /// <param name="t">变迁</param>
        /// <param name="indent">缩进前缀</param>
        private static void AppendResourceAcquire(StringBuilder sb, MauDocument doc, IrTransition t, string indent)
        {
            for (int i = 0; i < t.Preconditions.Count; i++)
            {
                string item = t.Preconditions[i];
                if (item.IndexOf('∨') >= 0)
                {
                    continue;
                }
                if (IsResource(doc, item))
                {
                    sb.AppendLine(indent + item + "_avail = " + item + "_avail - 1;");
                }
            }
        }

        /// <summary>
        /// 变迁归还资源——完成/失败/超时后槽位归还
        /// </summary>
        /// <param name="sb">输出缓冲</param>
        /// <param name="doc">文档</param>
        /// <param name="t">变迁</param>
        /// <param name="indent">缩进前缀</param>
        private static void AppendResourceRelease(StringBuilder sb, MauDocument doc, IrTransition t, string indent)
        {
            for (int i = 0; i < t.Preconditions.Count; i++)
            {
                string item = t.Preconditions[i];
                if (item.IndexOf('∨') >= 0)
                {
                    continue;
                }
                if (IsResource(doc, item))
                {
                    sb.AppendLine(indent + item + "_avail = " + item + "_avail + 1;");
                }
            }
        }
    }
}
