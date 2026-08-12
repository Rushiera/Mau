using System;
using System.Collections.Generic;

namespace Mau.Translator
{
    /// <summary>
    /// 分析结果
    /// </summary>
    public sealed class AnalyzeResultV2
    {
        /// <summary>是否通过——无错误诊断</summary>
        public bool Success = true;

        /// <summary>诊断列表（E3xx 分析）</summary>
        public List<MauDiagnostic> Diagnostics = new List<MauDiagnostic>();

        /// <summary>关键路径报告——瓶颈链</summary>
        public string KeyPathReport = "";
    }

    /// <summary>
    /// 系统分析器——稳定性/可达性/关键路径/扰动覆盖（构筑期只读，不进入运行时）
    /// </summary>
    public static class MauAnalyzerV2
    {
        /// <summary>
        /// 分析入口
        /// </summary>
        /// <param name="doc">解析文档（已验证）</param>
        /// <returns>分析结果</returns>
        public static AnalyzeResultV2 Analyze(MauDocV2 doc)
        {
            AnalyzeResultV2 result = new AnalyzeResultV2();
            if (doc == null)
            {
                AddError(result, "E300", "文档为空");
                return result;
            }

            // [段1] 图构建——节点 = 状态；边 = 控制律转移
            List<StateNodeV2> nodes = BuildGraph(doc);
            if (nodes.Count == 0)
            {
                result.Success = true;
                return result;
            }

            // [段2] 稳定性——环检测（先于可达性——无逃逸死环报 E311 更具体）
            CheckStability(nodes, result);
            if (!result.Success)
            {
                return result;
            }

            // [段3] 可达性——每机 BFS
            CheckReachability(doc, nodes, result);
            if (!result.Success)
            {
                return result;
            }

            // [段4] 关键路径——拓扑 DP（无环时）
            result.KeyPathReport = BuildKeyPathReport(nodes);

            // [段5] 扰动覆盖——失败分支可达终态
            CheckDisturbance(doc, nodes, result);

            result.Success = result.Diagnostics.Count == 0;
            return result;
        }

        /// <summary>
        /// 状态节点——图节点
        /// </summary>
        private sealed class StateNodeV2
        {
            /// <summary>状态机名</summary>
            public string Machine = "";

            /// <summary>状态名</summary>
            public string State = "";

            /// <summary>出边列表</summary>
            public List<GraphEdgeV2> Edges = new List<GraphEdgeV2>();

            /// <summary>入边数（拓扑）</summary>
            public int InDegree;

            /// <summary>是否可达（BFS 标记）</summary>
            public bool Reachable;

            /// <summary>最长路径权重（关键路径 DP）</summary>
            public int Longest;

            /// <summary>最长路径来源</summary>
            public StateNodeV2? Prev;
        }

        /// <summary>
        /// 图边——控制律转移
        /// </summary>
        private sealed class GraphEdgeV2
        {
            /// <summary>目标节点</summary>
            public StateNodeV2 Target = null!;

            /// <summary>来源控制律名</summary>
            public string LawName = "";

            /// <summary>是否有 τ 约束</summary>
            public bool HasTimeout;

            /// <summary>是否有资源约束</summary>
            public bool HasResource;

            /// <summary>是否有测量条件驱动</summary>
            public bool HasMeasureCond;

            /// <summary>权重（关键路径——1 + τ/10）</summary>
            public int Weight;
        }

        /// <summary>
        /// 图构建——节点 + 控制律转移边
        /// </summary>
        /// <param name="doc">文档</param>
        /// <returns>节点列表</returns>
        private static List<StateNodeV2> BuildGraph(MauDocV2 doc)
        {
            List<StateNodeV2> nodes = new List<StateNodeV2>();
            Dictionary<string, StateNodeV2> byId = new Dictionary<string, StateNodeV2>(StringComparer.Ordinal);
            for (int i = 0; i < doc.Machines.Count; i++)
            {
                MachineV2 m = doc.Machines[i];
                for (int s = 0; s < m.States.Count; s++)
                {
                    StateNodeV2 node = new StateNodeV2();
                    node.Machine = m.Name;
                    node.State = m.States[s];
                    nodes.Add(node);
                    byId[m.Name + "." + m.States[s]] = node;
                }
            }

            for (int i = 0; i < doc.Laws.Count; i++)
            {
                LawV2 law = doc.Laws[i];
                // 前置状态（可能多个——析取中多个状态断言）
                List<StateNodeV2> fromNodes = CollectFromStates(law, byId);
                // 结果状态转移
                for (int r = 0; r < law.Results.Count; r++)
                {
                    ResultV2 res = law.Results[r];
                    if (res.Kind != ResultKindV2.StateTransfer)
                    {
                        continue;
                    }
                    string targetId = res.MachineName! + "." + res.StateName!;
                    StateNodeV2? target = byId.TryGetValue(targetId, out StateNodeV2? t) ? t : null;
                    if (target == null)
                    {
                        continue;
                    }
                    // 边信息
                    bool hasRes = HasResourceCond(law.Conditions);
                    bool hasMeasure = HasMeasureCond(law.Conditions, doc);
                    int weight = 1;
                    if (law.Attrs.Timeout != null)
                    {
                        weight = weight + law.Attrs.Timeout.Value / 10;
                    }
                    if (fromNodes.Count == 0)
                    {
                        // 无前置状态——入口/全局律：从机器初始状态出发（近似——分析可达性不建模全局触发）
                        StateNodeV2? initial = null;
                        for (int n = 0; n < nodes.Count; n++)
                        {
                            if (nodes[n].Machine == res.MachineName && nodes[n].State == GetInitialState(doc, res.MachineName!))
                            {
                                initial = nodes[n];
                                break;
                            }
                        }
                        if (initial != null && initial != target)
                        {
                            AddEdge(initial, target, law, hasRes, hasMeasure, weight);
                        }
                    }
                    else
                    {
                        bool sameMachine = false;
                        for (int f = 0; f < fromNodes.Count; f++)
                        {
                            if (fromNodes[f].Machine == res.MachineName)
                            {
                                AddEdge(fromNodes[f], target, law, hasRes, hasMeasure, weight);
                                sameMachine = true;
                            }
                        }
                        if (!sameMachine)
                        {
                            // 跨机结果——从目标机初始状态建边（SEQ 链语义：链启动后按序可达）
                            StateNodeV2? initial = null;
                            for (int n = 0; n < nodes.Count; n++)
                            {
                                if (nodes[n].Machine == res.MachineName && nodes[n].State == GetInitialState(doc, res.MachineName!))
                                {
                                    initial = nodes[n];
                                    break;
                                }
                            }
                            if (initial != null && initial != target)
                            {
                                AddEdge(initial, target, law, hasRes, hasMeasure, weight);
                            }
                        }
                    }
                }
            }
            return nodes;
        }

        /// <summary>
        /// 边添加——同机内转移（自环跳过——全局入口律的保持转移不建模）
        /// </summary>
        /// <param name="from">起点</param>
        /// <param name="to">终点</param>
        /// <param name="law">控制律</param>
        /// <param name="hasRes">有资源约束</param>
        /// <param name="hasMeasure">有测量条件</param>
        /// <param name="weight">权重</param>
        private static void AddEdge(StateNodeV2 from, StateNodeV2 to, LawV2 law, bool hasRes, bool hasMeasure, int weight)
        {
            if (from == to)
            {
                return;
            }
            // 去重——同源同目标同控制律只加一条
            for (int i = 0; i < from.Edges.Count; i++)
            {
                if (from.Edges[i].Target == to && from.Edges[i].LawName == law.Name)
                {
                    return;
                }
            }
            GraphEdgeV2 edge = new GraphEdgeV2();
            edge.Target = to;
            edge.LawName = law.Name;
            edge.HasTimeout = law.Attrs.Timeout != null;
            edge.HasResource = hasRes;
            edge.HasMeasureCond = hasMeasure;
            edge.Weight = weight;
            from.Edges.Add(edge);
        }

        /// <summary>
        /// 是否为绑定父状态——子机激活即父进入（隐式可达）
        /// </summary>
        /// <param name="doc">文档</param>
        /// <param name="machineName">状态机名</param>
        /// <param name="stateName">状态名</param>
        /// <returns>为真</returns>
        private static bool IsBindParentState(MauDocV2 doc, string machineName, string stateName)
        {
            MachineV2? m = doc.FindMachine(machineName);
            if (m == null)
            {
                return false;
            }
            for (int b = 0; b < m.Binds.Count; b++)
            {
                if (m.Binds[b].ParentState == stateName)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// 初始状态名
        /// </summary>
        /// <param name="doc">文档</param>
        /// <param name="machineName">状态机名</param>
        /// <returns>初始状态名</returns>
        private static string GetInitialState(MauDocV2 doc, string machineName)
        {
            MachineV2? m = doc.FindMachine(machineName);
            if (m == null || m.States.Count == 0)
            {
                return "";
            }
            return m.States[0];
        }

        /// <summary>
        /// 前置状态收集——条件树中的状态断言
        /// </summary>
        /// <param name="law">控制律</param>
        /// <param name="byId">节点表</param>
        /// <returns>前置状态节点</returns>
        private static List<StateNodeV2> CollectFromStates(LawV2 law, Dictionary<string, StateNodeV2> byId)
        {
            List<StateNodeV2> from = new List<StateNodeV2>();
            CollectStatesRecursive(law.Conditions, byId, from);
            return from;
        }

        /// <summary>
        /// 状态收集递归
        /// </summary>
        /// <param name="cond">条件节点</param>
        /// <param name="byId">节点表</param>
        /// <param name="from">收集结果</param>
        private static void CollectStatesRecursive(CondV2 cond, Dictionary<string, StateNodeV2> byId, List<StateNodeV2> from)
        {
            if (cond == null)
            {
                return;
            }
            if (cond.Kind == CondKindV2.And || cond.Kind == CondKindV2.Or)
            {
                for (int i = 0; i < cond.Items!.Count; i++)
                {
                    CollectStatesRecursive(cond.Items[i], byId, from);
                }
                return;
            }
            if (cond.Kind == CondKindV2.StateEquals)
            {
                string id = cond.MachineName! + "." + cond.StateName!;
                if (byId.TryGetValue(id, out StateNodeV2? node))
                {
                    from.Add(node);
                }
            }
        }

        /// <summary>
        /// 条件树是否含资源引用
        /// </summary>
        /// <param name="cond">条件节点</param>
        /// <returns>为真</returns>
        private static bool HasResourceCond(CondV2 cond)
        {
            if (cond == null)
            {
                return false;
            }
            if (cond.Kind == CondKindV2.ResourceRef)
            {
                return true;
            }
            if (cond.Kind == CondKindV2.And || cond.Kind == CondKindV2.Or)
            {
                for (int i = 0; i < cond.Items!.Count; i++)
                {
                    if (HasResourceCond(cond.Items[i]))
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        /// <summary>
        /// 条件树是否含测量条件（测量驱动）
        /// </summary>
        /// <param name="cond">条件节点</param>
        /// <param name="doc">文档</param>
        /// <returns>为真</returns>
        private static bool HasMeasureCond(CondV2 cond, MauDocV2 doc)
        {
            if (cond == null)
            {
                return false;
            }
            if (cond.Kind == CondKindV2.PropRef)
            {
                PropositionV2? prop = doc.FindProposition(cond.RefName!);
                if (prop != null && prop.Kind == PropKindV2.Condition)
                {
                    return true;
                }
                return false;
            }
            if (cond.Kind == CondKindV2.And || cond.Kind == CondKindV2.Or)
            {
                for (int i = 0; i < cond.Items!.Count; i++)
                {
                    if (HasMeasureCond(cond.Items[i], doc))
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        /// <summary>
        /// 可达性检查——BFS 从初始；不可达 E301；非终态无路到终态 E302
        /// </summary>
        /// <param name="doc">文档</param>
        /// <param name="nodes">节点列表</param>
        /// <param name="result">分析结果</param>
        private static void CheckReachability(MauDocV2 doc, List<StateNodeV2> nodes, AnalyzeResultV2 result)
        {
            // 按状态机分组 BFS
            for (int i = 0; i < doc.Machines.Count; i++)
            {
                MachineV2 m = doc.Machines[i];
                // 初始节点
                StateNodeV2? initial = null;
                for (int n = 0; n < nodes.Count; n++)
                {
                    if (nodes[n].Machine == m.Name && nodes[n].State == m.States[0])
                    {
                        initial = nodes[n];
                        break;
                    }
                }
                if (initial == null)
                {
                    continue;
                }
                // BFS
                List<StateNodeV2> queue = new List<StateNodeV2>();
                initial.Reachable = true;
                queue.Add(initial);
                int head = 0;
                while (head < queue.Count)
                {
                    StateNodeV2 cur = queue[head];
                    head = head + 1;
                    for (int e = 0; e < cur.Edges.Count; e++)
                    {
                        StateNodeV2 target = cur.Edges[e].Target;
                        if (!target.Reachable)
                        {
                            target.Reachable = true;
                            queue.Add(target);
                        }
                    }
                }
                // 不可达状态——绑定子机的父状态豁免（子机激活即父进入，隐式可达）
                for (int n = 0; n < nodes.Count; n++)
                {
                    if (nodes[n].Machine == m.Name && !nodes[n].Reachable)
                    {
                        if (IsBindParentState(doc, m.Name, nodes[n].State))
                        {
                            continue;
                        }
                        AddError(result, "E301", "状态不可达——'" + m.Name + "." + nodes[n].State + "'（无任何控制律能到达）");
                        return;
                    }
                }
            }

            // 非终态无路到终态——终态 = 出度 0；环存在时交由 E311（死环）/ 生命周期豁免
            for (int n = 0; n < nodes.Count; n++)
            {
                StateNodeV2 node = nodes[n];
                if (node.Edges.Count == 0)
                {
                    continue; // 自身是终态
                }
                bool hasCycle = false;
                bool can = CanReachTerminal(node, out hasCycle);
                if (!can && !hasCycle)
                {
                    AddError(result, "E302", "非终态无路到终态——'" + node.Machine + "." + node.State + "'（死循环风险，缺退出路径）");
                    return;
                }
            }
        }

        /// <summary>
        /// 是否可达终态（出度 0）——探索全可达空间；环记为 hasCycle（环由 E311/生命周期豁免处理）
        /// </summary>
        /// <param name="start">起点</param>
        /// <param name="hasCycle">可达空间是否含环</param>
        /// <returns>可达终态为真</returns>
        private static bool CanReachTerminal(StateNodeV2 start, out bool hasCycle)
        {
            hasCycle = false;
            List<StateNodeV2> visited = new List<StateNodeV2>();
            List<StateNodeV2> stack = new List<StateNodeV2>();
            stack.Add(start);
            while (stack.Count > 0)
            {
                StateNodeV2 cur = stack[stack.Count - 1];
                stack.RemoveAt(stack.Count - 1);
                if (visited.Contains(cur))
                {
                    continue;
                }
                visited.Add(cur);
                if (cur.Edges.Count == 0)
                {
                    return true;
                }
                for (int e = 0; e < cur.Edges.Count; e++)
                {
                    StateNodeV2 target = cur.Edges[e].Target;
                    if (visited.Contains(target))
                    {
                        hasCycle = true;
                    }
                    else
                    {
                        stack.Add(target);
                    }
                }
            }
            return false;
        }

        /// <summary>
        /// 稳定性检查——无界环 E311 / 震荡 E312
        /// </summary>
        /// <param name="nodes">节点列表</param>
        /// <param name="result">分析结果</param>
        private static void CheckStability(List<StateNodeV2> nodes, AnalyzeResultV2 result)
        {
            // 强连通分量检测（Tarjan 简化——DFS 找环）
            List<StateNodeV2> stack = new List<StateNodeV2>();
            List<StateNodeV2> visited = new List<StateNodeV2>();
            for (int i = 0; i < nodes.Count; i++)
            {
                if (!visited.Contains(nodes[i]))
                {
                    FindCycle(nodes[i], visited, stack, result);
                    if (!result.Success)
                    {
                        return;
                    }
                }
            }
        }

        /// <summary>
        /// DFS 环检测——环上无任何约束 → 无界环
        /// </summary>
        /// <param name="node">当前节点</param>
        /// <param name="visited">已访问</param>
        /// <param name="stack">路径栈</param>
        /// <param name="result">分析结果</param>
        private static void FindCycle(StateNodeV2 node, List<StateNodeV2> visited, List<StateNodeV2> stack, AnalyzeResultV2 result)
        {
            visited.Add(node);
            stack.Add(node);
            for (int e = 0; e < node.Edges.Count; e++)
            {
                GraphEdgeV2 edge = node.Edges[e];
                StateNodeV2 target = edge.Target;
                if (stack.Contains(target))
                {
                    // 找到环——检查是否有逃逸边（环上节点指向环外 = 有界工作循环/复位环，跳过）
                    int idx = stack.IndexOf(target);
                    bool hasEscape = false;
                    for (int i = idx; i < stack.Count; i++)
                    {
                        StateNodeV2 cur = stack[i];
                        for (int ee = 0; ee < cur.Edges.Count; ee++)
                        {
                            if (!stack.Contains(cur.Edges[ee].Target))
                            {
                                hasEscape = true;
                                break;
                            }
                        }
                        if (hasEscape)
                        {
                            break;
                        }
                    }
                    if (!hasEscape)
                    {
                        // 无逃逸死环——检查环上所有边是否有约束
                        bool constrained = false;
                        for (int i = idx; i < stack.Count; i++)
                        {
                            StateNodeV2 cur = stack[i];
                            for (int ee = 0; ee < cur.Edges.Count; ee++)
                            {
                                if (stack.Contains(cur.Edges[ee].Target))
                                {
                                    GraphEdgeV2 ce = cur.Edges[ee];
                                    if (ce.HasTimeout || ce.HasResource || ce.HasMeasureCond)
                                    {
                                        constrained = true;
                                        break;
                                    }
                                }
                            }
                            if (constrained)
                            {
                                break;
                            }
                        }
                        if (!constrained)
                        {
                            AddError(result, "E311", "无界环——状态 '" + node.Machine + "." + node.State + "' 环上无 τ/资源/测量约束且无逃逸（死循环风险）");
                            return;
                        }
                    }
                }
                else if (!visited.Contains(target))
                {
                    FindCycle(target, visited, stack, result);
                    if (!result.Success)
                    {
                        return;
                    }
                }
            }
            stack.RemoveAt(stack.Count - 1);
        }

        /// <summary>
        /// 关键路径报告——拓扑 DP 最长路径（有环则跳过）
        /// </summary>
        /// <param name="nodes">节点列表</param>
        /// <returns>报告</returns>
        private static string BuildKeyPathReport(List<StateNodeV2> nodes)
        {
            // 入度计算
            for (int i = 0; i < nodes.Count; i++)
            {
                nodes[i].InDegree = 0;
                nodes[i].Longest = 1;
                nodes[i].Prev = null;
            }
            for (int i = 0; i < nodes.Count; i++)
            {
                for (int e = 0; e < nodes[i].Edges.Count; e++)
                {
                    nodes[i].Edges[e].Target.InDegree = nodes[i].Edges[e].Target.InDegree + 1;
                }
            }
            // Kahn 拓扑——仅处理无环部分
            List<StateNodeV2> queue = new List<StateNodeV2>();
            int processed = 0;
            for (int i = 0; i < nodes.Count; i++)
            {
                if (nodes[i].InDegree == 0)
                {
                    queue.Add(nodes[i]);
                }
            }
            int head = 0;
            while (head < queue.Count)
            {
                StateNodeV2 cur = queue[head];
                head = head + 1;
                processed = processed + 1;
                for (int e = 0; e < cur.Edges.Count; e++)
                {
                    GraphEdgeV2 edge = cur.Edges[e];
                    edge.Target.InDegree = edge.Target.InDegree - 1;
                    if (edge.Target.Longest < cur.Longest + edge.Weight)
                    {
                        edge.Target.Longest = cur.Longest + edge.Weight;
                        edge.Target.Prev = cur;
                    }
                    if (edge.Target.InDegree == 0)
                    {
                        queue.Add(edge.Target);
                    }
                }
            }
            if (processed < nodes.Count)
            {
                return "（存在环——关键路径跳过）";
            }
            // 找最长
            StateNodeV2? best = null;
            for (int i = 0; i < nodes.Count; i++)
            {
                if (best == null || nodes[i].Longest > best.Longest)
                {
                    best = nodes[i];
                }
            }
            if (best == null)
            {
                return "";
            }
            // 回溯路径
            List<string> path = new List<string>();
            StateNodeV2? cur2 = best;
            while (cur2 != null)
            {
                path.Add(cur2.Machine + "." + cur2.State);
                cur2 = cur2.Prev;
            }
            path.Reverse();
            string report = "关键路径（权重 " + best.Longest.ToString() + "）：";
            for (int i = 0; i < path.Count; i++)
            {
                if (i > 0)
                {
                    report = report + " → ";
                }
                report = report + path[i];
            }
            return report;
        }

        /// <summary>
        /// 扰动覆盖——失败分支（| 次项及之后）可达终态
        /// </summary>
        /// <param name="doc">文档</param>
        /// <param name="nodes">节点列表</param>
        /// <param name="result">分析结果</param>
        private static void CheckDisturbance(MauDocV2 doc, List<StateNodeV2> nodes, AnalyzeResultV2 result)
        {
            Dictionary<string, StateNodeV2> byId = new Dictionary<string, StateNodeV2>(StringComparer.Ordinal);
            for (int i = 0; i < nodes.Count; i++)
            {
                byId[nodes[i].Machine + "." + nodes[i].State] = nodes[i];
            }
            for (int i = 0; i < doc.Laws.Count; i++)
            {
                LawV2 law = doc.Laws[i];
                if (law.Results.Count < 2)
                {
                    continue;
                }
                for (int r = 1; r < law.Results.Count; r++)
                {
                    ResultV2 res = law.Results[r];
                    if (res.Kind != ResultKindV2.StateTransfer)
                    {
                        continue;
                    }
                    string id = res.MachineName! + "." + res.StateName!;
                    if (!byId.TryGetValue(id, out StateNodeV2? target))
                    {
                        continue;
                    }
                    if (target.Edges.Count == 0)
                    {
                        continue; // 失败落入终态——OK
                    }
                    bool hasCycle = false;
                    if (!CanReachTerminal(target, out hasCycle) && !hasCycle)
                    {
                        AddError(result, "E321", "扰动无恢复——控制律 '" + law.Name + "' 失败分支 '" + id + "' 无法到达终态（恢复路径缺失）");
                        return;
                    }
                }
            }
        }

        /// <summary>
        /// 错误添加
        /// </summary>
        /// <param name="result">分析结果</param>
        /// <param name="code">错误码</param>
        /// <param name="message">消息</param>
        private static void AddError(AnalyzeResultV2 result, string code, string message)
        {
            result.Diagnostics.Add(new MauDiagnostic(code, 0, message));
            result.Success = false;
        }
    }
}
