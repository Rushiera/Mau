using System.Collections.Generic;
using Mau.Contracts;

namespace Mau.Translator
{
    /// <summary>
    /// Mau 静态验证器——构筑期 11 项检查，任何一项失败拒绝生成
    /// </summary>
    public static class MauValidator
    {
        /// <summary>
        /// 执行静态验证
        /// </summary>
        /// <param name="doc">解析后的文档</param>
        /// <returns>诊断列表——空表示全部通过</returns>
        public static List<MauDiagnostic> Validate(MauDocument doc)
{
            List<MauDiagnostic> diags = new List<MauDiagnostic>();

            // [段1] 第 1 项：积木存在性——每个变迁的动作必须在注册表
            for (int i = 0; i < doc.Transitions.Count; i++)
            {
                IrTransition t = doc.Transitions[i];
                if (t.BrickName.Length == 0)
                {
                    diags.Add(new MauDiagnostic("E001", t.Line, "变迁 " + t.Name + " 缺少动作积木"));
                    continue;
                }
                BrickContract? contract;
                if (!BrickRegistry.TryGet(t.BrickName, out contract))
                {
                    diags.Add(new MauDiagnostic("E001", t.Line, "积木未注册: " + t.BrickName));
                }
            }

            // [段2] 第 2 项：参数端口匹配——参数端口名必须在积木输入端口集
            for (int i = 0; i < doc.Transitions.Count; i++)
            {
                IrTransition t = doc.Transitions[i];
                if (t.BrickName.Length == 0)
                {
                    continue;
                }
                BrickContract? contract;
                if (!BrickRegistry.TryGet(t.BrickName, out contract))
                {
                    continue;
                }
                for (int p = 0; p < t.Params.Count; p++)
                {
                    string portName = t.Params[p].PortName;
                    if (!HasInputPort(contract, portName))
                    {
                        diags.Add(new MauDiagnostic("E002", t.Line, "参数端口 " + portName + " 不在积木 " + t.BrickName + " 的输入端口集"));
                    }
                }
            }

            // [段2b] 调试插值验证——{{端口名}} 中的端口必须在参数声明中有匹配
            for (int i = 0; i < doc.Transitions.Count; i++)
            {
                IrTransition t = doc.Transitions[i];
                if (t.DebugMessage.Length == 0)
                {
                    continue;
                }
                int pos = 0;
                string msg = t.DebugMessage;
                while (pos < msg.Length - 3)
                {
                    int open = msg.IndexOf("{{", pos);
                    if (open < 0)
                    {
                        break;
                    }
                    int close = msg.IndexOf("}}", open + 2);
                    if (close < 0)
                    {
                        diags.Add(new MauDiagnostic("E014", t.Line, "调试消息中 {{ 缺少匹配的 }}: " + msg));
                        break;
                    }
                    string portRef = msg.Substring(open + 2, close - open - 2).Trim();
                    bool found = false;
                    for (int p = 0; p < t.Params.Count; p++)
                    {
                        if (t.Params[p].PortName == portRef)
                        {
                            found = true;
                            break;
                        }
                    }
                    if (!found)
                    {
                        diags.Add(new MauDiagnostic("E014", t.Line, "调试消息中 {{" + portRef + "}} 引用了未在参数中声明的端口"));
                    }
                    pos = close + 2;
                }
            }

            // [段3] 第 3 项：命题引用完整性——前置/后置引用必须已声明
            for (int i = 0; i < doc.Transitions.Count; i++)
            {
                IrTransition t = doc.Transitions[i];
                for (int p = 0; p < t.Preconditions.Count; p++)
                {
                    string[] orParts = t.Preconditions[p].Split('∨');
                    for (int o = 0; o < orParts.Length; o++)
                    {
                        string name = orParts[o].Trim();
                        if (name.Length == 0)
                        {
                            continue;
                        }
                        if (FindResource(doc, name) != null)
                        {
                            continue;
                        }
                        if (doc.FindProposition(name) == null)
                        {
                            diags.Add(new MauDiagnostic("E003", t.Line, "前置引用未声明的命题: " + t.Preconditions[p]));
                            break;
                        }
                    }
                }
                if (t.PostOk.Count == 0 && t.PostError.Count == 0)
                {
                    diags.Add(new MauDiagnostic("E012", t.Line, "变迁 " + t.Name + " 缺少后置声明"));
                }
                for (int p = 0; p < t.PostOk.Count; p++)
                {
                    if (doc.FindProposition(t.PostOk[p]) == null)
                    {
                        diags.Add(new MauDiagnostic("E003", t.Line, "后置引用未声明的命题: " + t.PostOk[p]));
                    }
                }
                for (int p = 0; p < t.PostError.Count; p++)
                {
                    if (doc.FindProposition(t.PostError[p]) == null)
                    {
                        diags.Add(new MauDiagnostic("E003", t.Line, "错误后置引用未声明的命题: " + t.PostError[p]));
                    }
                }
            }

            // [段4] 第 4 项：有界环判定——变迁依赖图 DFS 找环，第一期无资源声明，有环即拒绝
            List<string>? cycle = FindCycle(doc);
            if (cycle != null)
            {
                string path = string.Join(" → ", cycle.ToArray());
                diags.Add(new MauDiagnostic("E004", 0, "无界环——环路上无消耗性资源: " + path));
            }

            // [段5] 第 5 项：事实互斥冲突——同一变迁的正常/错误后置不得重叠
            for (int i = 0; i < doc.Transitions.Count; i++)
            {
                IrTransition t = doc.Transitions[i];
                for (int a = 0; a < t.PostOk.Count; a++)
                {
                    for (int b = 0; b < t.PostError.Count; b++)
                    {
                        if (t.PostOk[a] == t.PostError[b])
                        {
                            diags.Add(new MauDiagnostic("E005", t.Line, "变迁 " + t.Name + " 的正常/错误后置重叠: " + t.PostOk[a]));
                        }
                    }
                }
                if (t.PostError.Count > 1)
                {
                    diags.Add(new MauDiagnostic("E013", t.Line, "变迁 " + t.Name + " 的错误后置超过 1 个——第一期仅支持双后置（正常/错误）"));
                }
            }

            // [段5b] 第 6 项：时限格式防线——解析层已做基本校验，此处二次防线
            for (int i = 0; i < doc.Transitions.Count; i++)
            {
                IrTransition t = doc.Transitions[i];
                if (t.HasTimeout)
                {
                    if (t.TimeoutMode == "Total" || t.TimeoutMode == "Idle")
                    {
                        if (t.TimeoutFrames <= 0)
                        {
                            diags.Add(new MauDiagnostic("E006", t.Line, "时限帧数必须为正整数: " + t.TimeoutFrames));
                        }
                    }
                    else if (t.TimeoutMode != "None")
                    {
                        diags.Add(new MauDiagnostic("E006", t.Line, "时限模式非法——需要 Total/Idle/None: " + t.TimeoutMode));
                    }
                }
            }

            // [段6] 第 7 项：配额合法性——配额资源必须有正整数配额值
            for (int i = 0; i < doc.Resources.Count; i++)
            {
                IrResource r = doc.Resources[i];
                if (r.Kind == "配额")
                {
                    if (r.Quota <= 0)
                    {
                        diags.Add(new MauDiagnostic("E007", r.Line, "配额资源 " + r.Name + " 的配额值必须为正整数: " + r.Quota));
                    }
                }
            }

            // [段7] 第 8 项：线程/汇合合法性——worker 线程变迁必须声明 inbox 汇合
            for (int i = 0; i < doc.Transitions.Count; i++)
            {
                IrTransition t = doc.Transitions[i];
                if (t.Thread == "worker" && t.Join != "inbox")
                {
                    diags.Add(new MauDiagnostic("E008", t.Line, "worker 线程变迁必须声明 汇合: inbox——结果无法回主线程: " + t.Name));
                }
            }

            // [段8] 第 9 项：组合引用存在——组合中引用的变迁必须已声明
            string[] validChannelTypes = new string[] { "直连", "inbox", "工单", "命令", "快照", "跨进程" };
            for (int i = 0; i < doc.Compositions.Count; i++)
            {
                IrComposition comp = doc.Compositions[i];
                for (int s = 0; s < comp.Sequence.Count; s++)
                {
                    if (doc.FindTransition(comp.Sequence[s]) == null)
                    {
                        diags.Add(new MauDiagnostic("E009", comp.Line, "组合 " + comp.Name + " 序列引用了未声明的变迁: " + comp.Sequence[s]));
                    }
                }
                for (int p = 0; p < comp.Parallel.Count; p++)
                {
                    if (doc.FindTransition(comp.Parallel[p]) == null)
                    {
                        diags.Add(new MauDiagnostic("E009", comp.Line, "组合 " + comp.Name + " 并行引用了未声明的变迁: " + comp.Parallel[p]));
                    }
                }
            }

            // [段9] 第 10 项：基座兼容——基座声明必须指向 Mau.Runtime
            if (doc.BaseName.Length > 0 && !doc.BaseName.StartsWith("Mau.Runtime"))
            {
                diags.Add(new MauDiagnostic("E010", 0, "基座声明与当前运行基座不匹配——需要 Mau.Runtime: " + doc.BaseName));
            }

            // [段10] 第 11 项：通道类型合法——必须在六类型集合
            for (int i = 0; i < doc.Channels.Count; i++)
            {
                IrChannel c = doc.Channels[i];
                bool valid = false;
                for (int t = 0; t < validChannelTypes.Length; t++)
                {
                    if (c.ChannelType == validChannelTypes[t])
                    {
                        valid = true;
                        break;
                    }
                }
                if (!valid)
                {
                    diags.Add(new MauDiagnostic("E011", c.Line, "通道类型非法——需要直连/inbox/工单/命令/快照/跨进程: " + c.ChannelType));
                }
            }

            return diags;
        }
        /// <summary>
        /// 检查契约是否有指定输入端口
        /// </summary>
        /// <param name="contract">积木契约</param>
        /// <param name="portName">端口名</param>
        /// <returns>存在为真</returns>
        private static bool HasInputPort(BrickContract contract, string portName)
        {
            for (int i = 0; i < contract.Inputs.Count; i++)
            {
                if (contract.Inputs[i].Name == portName)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// 变迁依赖图找环——T1 后置命题出现在 T2 前置中则 T1 → T2
        /// </summary>
        /// <param name="doc">文档</param>
        /// <returns>环路径（变迁名列表）或空</returns>
        private static List<string>? FindCycle(MauDocument doc)
        {
            int count = doc.Transitions.Count;
            int[] state = new int[count];
            List<int> path = new List<int>();

            for (int start = 0; start < count; start++)
            {
                if (state[start] == 0)
                {
                    List<string>? cycle = Dfs(start, doc, state, path);
                    if (cycle != null)
                    {
                        return cycle;
                    }
                }
            }
            return null;
        }

        /// <summary>
        /// 按名字查资源——资源可出现在前置（槽位检查）
        /// </summary>
        /// <param name="doc">文档</param>
        /// <param name="name">资源名</param>
        /// <returns>资源节点或空</returns>
        private static IrResource? FindResource(MauDocument doc, string name)
        {
            for (int i = 0; i < doc.Resources.Count; i++)
            {
                if (doc.Resources[i].Name == name)
                {
                    return doc.Resources[i];
                }
            }
            return null;
        }

        /// <summary>
        /// 环是否经过资源前置的变迁——资源提供有界性（配额拦无限循环）
        /// </summary>
        /// <param name="doc">文档</param>
        /// <param name="cycle">环路径（变迁名列表）</param>
        /// <returns>有资源为真</returns>
        private static bool CycleHasResource(MauDocument doc, List<string> cycle)
        {
            for (int i = 0; i < cycle.Count; i++)
            {
                IrTransition? t = doc.FindTransition(cycle[i]);
                if (t == null)
                {
                    continue;
                }
                for (int p = 0; p < t.Preconditions.Count; p++)
                {
                    string[] orParts = t.Preconditions[p].Split('∨');
                    for (int o = 0; o < orParts.Length; o++)
                    {
                        if (FindResource(doc, orParts[o].Trim()) != null)
                        {
                            return true;
                        }
                    }
                }
            }
            return false;
        }

        /// <summary>
        /// DFS 三色标记找环
        /// </summary>
        /// <param name="node">当前变迁索引</param>
        /// <param name="doc">文档</param>
        /// <param name="state">访问状态——0 未访问/1 访问中/2 已访问</param>
        /// <param name="path">当前路径</param>
        /// <returns>环路径或空</returns>
        private static List<string>? Dfs(int node, MauDocument doc, int[] state, List<int> path)
        {
            state[node] = 1;
            path.Add(node);

            IrTransition t = doc.Transitions[node];
            List<int> nexts = FindNexts(t, doc);
            for (int i = 0; i < nexts.Count; i++)
            {
                int next = nexts[i];
                if (state[next] == 1)
                {
                    // 找到环——从路径中截取
                    int startIndex = path.IndexOf(next);
                    List<string> cycle = new List<string>();
                    for (int k = startIndex; k < path.Count; k++)
                    {
                        cycle.Add(doc.Transitions[path[k]].Name);
                    }
                    cycle.Add(doc.Transitions[next].Name);
                    if (CycleHasResource(doc, cycle))
                    {
                        continue;  // 环上有资源前置——配额提供有界性，豁免
                    }
                    return cycle;
                }
                if (state[next] == 0)
                {
                    List<string>? cycle = Dfs(next, doc, state, path);
                    if (cycle != null)
                    {
                        return cycle;
                    }
                }
            }

            path.RemoveAt(path.Count - 1);
            state[node] = 2;
            return null;
        }

        /// <summary>
        /// 计算变迁的后继变迁索引——后置命题出现在其他变迁前置中
        /// </summary>
        /// <param name="t">当前变迁</param>
        /// <param name="doc">文档</param>
        /// <returns>后继索引列表</returns>
        private static List<int> FindNexts(IrTransition t, MauDocument doc)
        {
            List<int> nexts = new List<int>();
            for (int i = 0; i < doc.Transitions.Count; i++)
            {
                IrTransition other = doc.Transitions[i];
                if (other == t)
                {
                    continue;
                }
                bool linked = false;
                for (int a = 0; a < t.PostOk.Count && !linked; a++)
                {
                    if (other.Preconditions.Contains(t.PostOk[a]))
                    {
                        linked = true;
                    }
                }
                for (int a = 0; a < t.PostError.Count && !linked; a++)
                {
                    if (other.Preconditions.Contains(t.PostError[a]))
                    {
                        linked = true;
                    }
                }
                if (linked)
                {
                    nexts.Add(i);
                }
            }
            return nexts;
        }
    }
}
