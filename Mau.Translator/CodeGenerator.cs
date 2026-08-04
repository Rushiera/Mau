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
            sb.AppendLine();
            sb.AppendLine("namespace Mau.Generated.Flows");
            sb.AppendLine("{");

            // [段2] 类声明
            sb.AppendLine("    /// <summary>");
            sb.AppendLine("    /// " + flowName + " 流程——由 Mau 声明生成");
            sb.AppendLine("    /// </summary>");
            sb.AppendLine("    public sealed class " + className);
            sb.AppendLine("    {");

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

            // [段4] 动作参数字段——参数名 = 端口名
            for (int i = 0; i < doc.Transitions.Count; i++)
            {
                IrTransition t = doc.Transitions[i];
                for (int p = 0; p < t.Params.Count; p++)
                {
                    string fieldName = "_" + t.Params[p].PortName;
                    string typeName = PortTypeName(doc, t, t.Params[p].PortName);
                    string initializer = IsReferenceType(doc, t, t.Params[p].PortName) ? " = null!;" : ";";
                    sb.AppendLine("        /// <summary>");
                    sb.AppendLine("        /// 变迁 " + t.Name + " 的动作参数——" + t.Params[p].PortName);
                    sb.AppendLine("        /// </summary>");
                    sb.AppendLine("        private " + typeName + " " + fieldName + initializer);
                    sb.AppendLine();
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

            // [段6] 构造——Cube 初始化
            bool hasCube = HasCube(doc);
            if (hasCube)
            {
                sb.AppendLine("        /// <summary>");
                sb.AppendLine("        /// 构造：初始化时限 Cube");
                sb.AppendLine("        /// </summary>");
                sb.AppendLine("        public " + className + "()");
                sb.AppendLine("        {");
                for (int i = 0; i < doc.Transitions.Count; i++)
                {
                    IrTransition t = doc.Transitions[i];
                    if (t.HasTimeout && t.TimeoutMode != "None")
                    {
                        sb.AppendLine("            " + t.Name + "_Cube = new Cube(" + CubeInit(t) + ");");
                    }
                }
                sb.AppendLine("        }");
                sb.AppendLine();
            }

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
            if (BrickRegistry.TryGet(t.BrickName, out contract))
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
            if (BrickRegistry.TryGet(t.BrickName, out contract))
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
            if (BrickRegistry.TryGet(t.BrickName, out contract))
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
            return type.Name;
        }

        /// <summary>
        /// 生成 Fire 方法——信号命题的外部投递入口
        /// </summary>
        /// <param name="sb">输出缓冲</param>
        /// <param name="doc">文档</param>
        /// <param name="p">信号命题</param>
        private static void AppendFireMethod(StringBuilder sb, MauDocument doc, IrProposition p)
        {
            // 收集引用该信号的所有变迁的动作参数（去重）
            List<IrParamBinding> params_ = new List<IrParamBinding>();
            for (int i = 0; i < doc.Transitions.Count; i++)
            {
                IrTransition t = doc.Transitions[i];
                if (!t.Preconditions.Contains(p.Name))
                {
                    continue;
                }
                for (int a = 0; a < t.Params.Count; a++)
                {
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
            sb.AppendLine("            " + p.Name + " = true;");
            sb.AppendLine("        }");
            sb.AppendLine();
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
                if (!t.Preconditions.Contains(signalName))
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

            for (int i = 0; i < doc.Transitions.Count; i++)
            {
                IrTransition t = doc.Transitions[i];
                AppendTransitionBlock(sb, doc, t);
                if (i < doc.Transitions.Count - 1)
                {
                    sb.AppendLine();
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

            // [段A] 前置检查块
            sb.AppendLine("            // [" + t.Name + "] 前置检查");
            sb.AppendLine("            if (" + PreconditionText(t, hasCube, cubeName) + ")");
            sb.AppendLine("            {");

            // 信号前置消费
            for (int p = 0; p < t.Preconditions.Count; p++)
            {
                IrProposition? prop = doc.FindProposition(t.Preconditions[p]);
                if (prop != null && prop.Kind == PropositionKind.Signal)
                {
                    sb.AppendLine("                // 信号消费");
                    sb.AppendLine("                " + prop.Name + " = false;");
                }
            }

            if (hasCube)
            {
                sb.AppendLine("                " + cubeName + ".Start();");
            }

            // 动作调用——参数用字段名
            sb.AppendLine("                // 执行动作（积木调用）");
            sb.AppendLine("                bool ok = " + BrickCallText(doc, t) + ";");

            // 后置注册
            sb.AppendLine("                if (ok)");
            sb.AppendLine("                {");
            for (int p = 0; p < t.PostOk.Count; p++)
            {
                sb.AppendLine("                    // 正常后置注册");
                sb.AppendLine("                    " + t.PostOk[p] + " = true;");
            }
            sb.AppendLine("                }");
            sb.AppendLine("                else");
            sb.AppendLine("                {");
            for (int p = 0; p < t.PostError.Count; p++)
            {
                sb.AppendLine("                    // 错误后置注册（互斥）");
                sb.AppendLine("                    " + t.PostError[p] + " = true;");
            }
            sb.AppendLine("                }");

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
                sb.AppendLine("                    " + cubeName + ".Complete();");
                sb.AppendLine("                }");
                sb.AppendLine("            }");
            }
        }

        /// <summary>
        /// 前置检查条件文本
        /// </summary>
        /// <param name="t">变迁</param>
        /// <param name="hasCube">是否有时限 Cube</param>
        /// <param name="cubeName">Cube 字段名</param>
        /// <returns>条件文本</returns>
        private static string PreconditionText(IrTransition t, bool hasCube, string cubeName)
        {
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < t.Preconditions.Count; i++)
            {
                if (i > 0)
                {
                    sb.Append(" && ");
                }
                sb.Append(t.Preconditions[i]);
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
        /// 积木调用文本——完整限定名
        /// </summary>
        /// <param name="doc">文档</param>
        /// <param name="t">变迁</param>
        /// <returns>调用文本</returns>
        private static string BrickCallText(MauDocument doc, IrTransition t)
        {
            BrickContract? contract;
            string implementation = "Mau.Bricks.FileBrick.Convert";
            if (BrickRegistry.TryGet(t.BrickName, out contract))
            {
                implementation = contract.Implementation;
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
                sb.Append("_" + t.Params[i].PortName);
            }
            sb.Append(")");
            return sb.ToString();
        }
    }
}
