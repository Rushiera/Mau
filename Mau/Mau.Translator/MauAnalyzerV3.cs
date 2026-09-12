using System;
using System.Collections.Generic;

namespace Mau.Translator
{
    /// <summary>
    /// 分析器 v3——自检仪：状态机图论验证（design-mau-v3 §六）。
    /// LLM 负责表达，确定性代码负责验真。分析失败 = 拒绝生成。
    /// 四分析：稳定性（无界环）/ 可达性（BFS）/ 扰动覆盖（失败侧恢复路径）/ 关键路径（SCC 收缩 + 拓扑 DP 报告）。
    /// 错误码：E300 无界环（环上无事件驱动刹车）/ E301 不可达状态 / E302 失败侧无恢复路径。
    /// 报告：Reports 文本列表（关键路径——排错定位用，不拦截）。
    /// </summary>
    public static class MauAnalyzerV3
    {
        /// <summary>
        /// 分析结果——错误拦截 + 报告
        /// </summary>
        /// <param name="doc">FSM 网络 IR</param>
        /// <returns>分析结果</returns>
        public static AnalysisResultV3 Analyze(MauDocV3 doc)
        {
            AnalysisResultV3 result = new AnalysisResultV3();
            if (doc == null)
            {
                result.Errors.Add(new MauDiagnostic("E300", 1, "空 IR——无可分析对象"));
                result.Success = false;
                return result;
            }
            // [段1] 建图——节点（状态机.状态）+ 边（导线结果转移）+ 外生目标（无状态断言条件的导线目标）
            GraphV3 graph = BuildGraph(doc);
            // [段2] 稳定性——无事件子图找环（环上无传感器条件 = 无界环）
            FindUnboundedLoops(doc, graph, result);
            // [段3] 可达性——BFS 从初始态 + 外生目标出发
            FindUnreachable(doc, graph, result);
            // [段4] 扰动覆盖——失败侧目标必须有出路（出度 ≥1 或终态）
            FindNoRecovery(doc, graph, result);
            // [段5] 关键路径——SCC 收缩 → DAG → 拓扑 DP（报告级）
            BuildCriticalPathReport(graph, result);
            result.Success = result.Errors.Count == 0;
            return result;
        }

        /// <summary>
        /// 构建状态转移图
        /// </summary>
        /// <param name="doc">FSM 网络 IR</param>
        /// <returns>转移图</returns>
        private static GraphV3 BuildGraph(MauDocV3 doc)
        {
            GraphV3 graph = new GraphV3();
            // [段1] 节点注册——每个状态的唯一 key = "状态机.状态"
            for (int s = 0; s < doc.StateMachines.Count; s++)
            {
                StateMachineDefV3 sm = doc.StateMachines[s];
                for (int i = 0; i < sm.States.Count; i++)
                {
                    graph.NodeIndex[sm.Name + "." + sm.States[i]] = graph.NodeNames.Count;
                    graph.NodeNames.Add(sm.Name + "." + sm.States[i]);
                    graph.NodeLine.Add(sm.Line);
                }
                graph.InitialNodes.Add(sm.Name + "." + sm.States[0]);
            }
            // [段2] 边——导线条件（状态断言）→ 结果目标
            for (int w = 0; w < doc.Wires.Count; w++)
            {
                WireDefV3 wire = doc.Wires[w];
                bool hasEventSource = HasSensorCondition(wire);
                // 源集合——状态断言条件的（状态机, 状态）
                List<string> sources = new List<string>();
                for (int c = 0; c < wire.Conditions.Count; c++)
                {
                    if (wire.Conditions[c].IsStateAssert)
                    {
                        sources.Add(wire.Conditions[c].StateName + "." + wire.Conditions[c].StateValue);
                    }
                }
                if (sources.Count == 0)
                {
                    // 外生边——无状态断言源（纯传感器触发）——目标状态外生可达
                    for (int r = 0; r < wire.Results.Count; r++)
                    {
                        string target = wire.Results[r].StateName + "." + wire.Results[r].StateValue;
                        if (graph.NodeIndex.ContainsKey(target))
                        {
                            graph.ExternNodes.Add(target);
                        }
                    }
                    continue;
                }
                for (int sIdx = 0; sIdx < sources.Count; sIdx++)
                {
                    if (!graph.NodeIndex.ContainsKey(sources[sIdx]))
                    {
                        continue;
                    }
                    for (int r = 0; r < wire.Results.Count; r++)
                    {
                        string target = wire.Results[r].StateName + "." + wire.Results[r].StateValue;
                        if (!graph.NodeIndex.ContainsKey(target))
                        {
                            continue;
                        }
                        EdgeV3 edge = new EdgeV3();
                        edge.From = sources[sIdx];
                        edge.To = target;
                        edge.Wire = wire;
                        edge.EventDriven = hasEventSource;
                        graph.Edges.Add(edge);
                    }
                }
            }
            // [段3] 出度统计——终态判定（出度 0）
            graph.OutDegree = new int[graph.NodeNames.Count];
            for (int e = 0; e < graph.Edges.Count; e++)
            {
                int fromIdx;
                if (graph.NodeIndex.TryGetValue(graph.Edges[e].From, out fromIdx))
                {
                    graph.OutDegree[fromIdx] = graph.OutDegree[fromIdx] + 1;
                }
            }
            return graph;
        }

        /// <summary>
        /// 导线是否含传感器条件——事件驱动刹车（环检测判据）
        /// </summary>
        /// <param name="wire">导线</param>
        /// <returns>含传感器沿条件为真</returns>
        private static bool HasSensorCondition(WireDefV3 wire)
        {
            for (int i = 0; i < wire.Conditions.Count; i++)
            {
                // 事件刹车 = 传感器沿条件（被动/Command/主动壳采样）；盒子判真与状态断言是事实条件（电平/非沿），不构成刹车
                if (!wire.Conditions[i].IsStateAssert && !wire.Conditions[i].IsBoxAssert)
                {
                    return true;
                }
            }
            return false;
        }/// <summary>
         /// 稳定性——无事件子图找环：环上所有边都无传感器条件 = 无界环（每帧可触发，无事件刹车）
         /// </summary>
         /// <param name="doc">IR（诊断行号用）</param>
         /// <param name="graph">转移图</param>
         /// <param name="result">分析结果</param>
        private static void FindUnboundedLoops(MauDocV3 doc, GraphV3 graph, AnalysisResultV3 result)
        {
            // 无事件子图——只保留 EventDriven=false 的边
            List<int>[] adj = BuildAdjacency(graph, false);
            // DFS 三色找环
            int[] color = new int[graph.NodeNames.Count];
            List<int> stack = new List<int>();
            for (int i = 0; i < graph.NodeNames.Count; i++)
            {
                if (color[i] == 0)
                {
                    DfsCycle(graph, adj, i, color, stack, result);
                }
            }
        }

        /// <summary>
        /// DFS 找环——发现环即报 E300（附环路径摘要）
        /// </summary>
        /// <param name="graph">转移图</param>
        /// <param name="adj">邻接表</param>
        /// <param name="node">当前节点</param>
        /// <param name="color">染色 0 白 1 灰 2 黑</param>
        /// <param name="stack">当前路径</param>
        /// <param name="result">分析结果</param>
        private static void DfsCycle(GraphV3 graph, List<int>[] adj, int node, int[] color,
            List<int> stack, AnalysisResultV3 result)
        {
            color[node] = 1;
            stack.Add(node);
            for (int e = 0; e < adj[node].Count; e++)
            {
                int next = adj[node][e];
                if (color[next] == 0)
                {
                    DfsCycle(graph, adj, next, color, stack, result);
                }
                else if (color[next] == 1)
                {
                    // 回边——环
                    if (CycleHasBrickAction(graph, stack, next, node))
                    {
                        // 环上含积木动作——积木执行有副作用（推进工作流/可能翻转盒子电平），非纯空转——A2 放行
                        continue;
                    }
                    string cycle = BuildCycleText(graph, stack, next);
                    result.Errors.Add(new MauDiagnostic("E300", graph.NodeLine[node], "无界环——环上无传感器事件刹车: " + cycle));
                }
            }
            stack.RemoveAt(stack.Count - 1);
            color[node] = 2;
        }
        /// <summary>
        /// 环路径文本化
        /// </summary>
        /// <param name="graph">转移图</param>
        /// <param name="stack">当前路径</param>
        /// <param name="backTo">回边目标</param>
        /// <returns>环摘要</returns>
        private static string BuildCycleText(GraphV3 graph, List<int> stack, int backTo)
        {
            string s = "";
            bool started = false;
            for (int i = 0; i < stack.Count; i++)
            {
                if (stack[i] == backTo)
                {
                    started = true;
                }
                if (started)
                {
                    if (s.Length > 0)
                    {
                        s = s + " → ";
                    }
                    s = s + graph.NodeNames[stack[i]];
                }
            }
            return s + " → " + graph.NodeNames[backTo];
        }

        /// <summary>
        /// 环是否含积木动作——环上任意边导线有积木调用（Actions.Count>0）
        /// 积木执行有副作用（推进工作流/可能翻转盒子电平），非纯空转——A2 放行
        /// </summary>
        /// <param name="graph">转移图</param>
        /// <param name="stack">当前路径</param>
        /// <param name="backTo">回边目标</param>
        /// <param name="node">当前节点</param>
        /// <returns>环上含积木动作为真</returns>
        private static bool CycleHasBrickAction(GraphV3 graph, List<int> stack, int backTo, int node)
        {
            // 回边 node → backTo
            if (EdgeHasAction(graph, graph.NodeNames[node], graph.NodeNames[backTo]))
            {
                return true;
            }
            // 栈路径 backTo → ... → node 的相邻对
            for (int i = 0; i < stack.Count; i++)
            {
                if (stack[i] == backTo)
                {
                    for (int j = i; j < stack.Count - 1; j++)
                    {
                        if (EdgeHasAction(graph, graph.NodeNames[stack[j]], graph.NodeNames[stack[j + 1]]))
                        {
                            return true;
                        }
                    }
                    break;
                }
            }
            return false;
        }

        /// <summary>
        /// 单边是否含积木动作——查转移图边表（From → To 的导线带动作）
        /// </summary>
        /// <param name="graph">转移图</param>
        /// <param name="from">源状态 key</param>
        /// <param name="to">目标状态 key</param>
        /// <returns>该方向任一边含动作为真</returns>
        private static bool EdgeHasAction(GraphV3 graph, string from, string to)
        {
            for (int e = 0; e < graph.Edges.Count; e++)
            {
                EdgeV3 edge = graph.Edges[e];
                if (edge.From == from && edge.To == to)
                {
                    if (edge.Wire.Actions.Count > 0)
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        /// <summary>
        /// 可达性——BFS 从初始态 + 外生目标出发，不可达状态报 E301
        /// </summary>
        /// <param name="doc">IR</param>
        /// <param name="graph">转移图</param>
        /// <param name="result">分析结果</param>
        private static void FindUnreachable(MauDocV3 doc, GraphV3 graph, AnalysisResultV3 result)
        {
            List<int>[] adj = BuildAdjacency(graph, true);
            bool[] visited = new bool[graph.NodeNames.Count];
            List<int> queue = new List<int>();
            for (int i = 0; i < graph.InitialNodes.Count; i++)
            {
                int idx;
                if (graph.NodeIndex.TryGetValue(graph.InitialNodes[i], out idx))
                {
                    queue.Add(idx);
                    visited[idx] = true;
                }
            }
            for (int i = 0; i < graph.ExternNodes.Count; i++)
            {
                int idx;
                if (graph.NodeIndex.TryGetValue(graph.ExternNodes[i], out idx) && !visited[idx])
                {
                    queue.Add(idx);
                    visited[idx] = true;
                }
            }
            int head = 0;
            while (head < queue.Count)
            {
                int cur = queue[head];
                head = head + 1;
                for (int e = 0; e < adj[cur].Count; e++)
                {
                    int next = adj[cur][e];
                    if (!visited[next])
                    {
                        visited[next] = true;
                        queue.Add(next);
                    }
                }
            }
            for (int i = 0; i < graph.NodeNames.Count; i++)
            {
                if (!visited[i])
                {
                    result.Errors.Add(new MauDiagnostic("E301", graph.NodeLine[i], "不可达状态: '" + graph.NodeNames[i] + "'——无任何转移链到达（孤立或死状态）"));
                }
            }
        }

        /// <summary>
        /// 扰动覆盖——失败侧（多结果导线的第 2 支起）目标必须有恢复路径。
        /// 判据：失败侧目标自身是终态（出度 0）→ 通过（设计归宿）；
        /// 出度 ≥1 → 全边图 BFS 必须能到达某终态，否则在非终态环里空转 → E302。
        /// </summary>
        /// <param name="doc">IR</param>
        /// <param name="graph">转移图</param>
        /// <param name="result">分析结果</param>
        private static void FindNoRecovery(MauDocV3 doc, GraphV3 graph, AnalysisResultV3 result)
        {
            List<int>[] adj = BuildAdjacency(graph, true);
            int n = graph.NodeNames.Count;
            // 终态集——出度 0
            bool[] terminal = new bool[n];
            int terminalCount = 0;
            for (int i = 0; i < n; i++)
            {
                terminal[i] = graph.OutDegree[i] == 0;
                if (terminal[i])
                {
                    terminalCount = terminalCount + 1;
                }
            }
            // 无终态系统（纯事件循环）——E302 不适用（无"归宿"概念）
            if (terminalCount == 0)
            {
                return;
            }
            for (int w = 0; w < doc.Wires.Count; w++)
            {
                WireDefV3 wire = doc.Wires[w];
                if (wire.Results.Count < 2)
                {
                    continue;
                }
                for (int r = 1; r < wire.Results.Count; r++)
                {
                    string target = wire.Results[r].StateName + "." + wire.Results[r].StateValue;
                    int idx;
                    if (!graph.NodeIndex.TryGetValue(target, out idx))
                    {
                        continue;
                    }
                    // 终态归宿——通过
                    if (terminal[idx])
                    {
                        continue;
                    }
                    // BFS 到终态——有恢复路径
                    bool[] visited = new bool[n];
                    List<int> queue = new List<int>();
                    queue.Add(idx);
                    visited[idx] = true;
                    int head = 0;
                    bool recovered = false;
                    while (head < queue.Count)
                    {
                        int cur = queue[head];
                        head = head + 1;
                        if (terminal[cur])
                        {
                            recovered = true;
                            break;
                        }
                        for (int e = 0; e < adj[cur].Count; e++)
                        {
                            int next = adj[cur][e];
                            if (!visited[next])
                            {
                                visited[next] = true;
                                queue.Add(next);
                            }
                        }
                    }
                    if (!recovered)
                    {
                        result.Errors.Add(new MauDiagnostic("E302", wire.Line, "导线 '" + wire.Name + "' 失败侧目标 '" + target + "' 无恢复路径——无法到达任何终态（非终态环内空转）"));
                    }
                }
            }
        }

        /// <summary>
        /// 关键路径——SCC 收缩 → DAG → 拓扑 DP 最长链（报告级，不拦截）
        /// </summary>
        /// <param name="graph">转移图</param>
        /// <param name="result">分析结果</param>
        private static void BuildCriticalPathReport(GraphV3 graph, AnalysisResultV3 result)
        {
            List<int>[] adj = BuildAdjacency(graph, true);
            int n = graph.NodeNames.Count;
            // Kosaraju SCC
            bool[] visited = new bool[n];
            List<int> order = new List<int>();
            for (int i = 0; i < n; i++)
            {
                if (!visited[i])
                {
                    DfsOrder(adj, i, visited, order);
                }
            }
            // 反向图
            List<int>[] rev = new List<int>[n];
            for (int i = 0; i < n; i++)
            {
                rev[i] = new List<int>();
            }
            for (int i = 0; i < n; i++)
            {
                for (int e = 0; e < adj[i].Count; e++)
                {
                    rev[adj[i][e]].Add(i);
                }
            }
            int[] comp = new int[n];
            for (int i = 0; i < n; i++)
            {
                comp[i] = -1;
            }
            int compCount = 0;
            for (int i = order.Count - 1; i >= 0; i--)
            {
                int node = order[i];
                if (comp[node] == -1)
                {
                    DfsAssign(rev, node, comp, compCount);
                    compCount = compCount + 1;
                }
            }
            // DAG DP——最长路径（节点数）
            int[] dp = new int[compCount];
            for (int i = 0; i < compCount; i++)
            {
                dp[i] = 1;
            }
            for (int i = order.Count - 1; i >= 0; i--)
            {
                int node = order[i];
                for (int e = 0; e < adj[node].Count; e++)
                {
                    int next = adj[node][e];
                    if (comp[node] != comp[next])
                    {
                        if (dp[comp[node]] + 1 > dp[comp[next]])
                        {
                            dp[comp[next]] = dp[comp[node]] + 1;
                        }
                    }
                }
            }
            int longest = 1;
            for (int i = 0; i < compCount; i++)
            {
                if (dp[i] > longest)
                {
                    longest = dp[i];
                }
            }
            result.Reports.Add("关键路径: 最长状态转移链 " + longest + " 个 SCC 节点（" + n + " 状态 / " + graph.Edges.Count + " 边 / " + compCount + " 强连通分量）");
        }

        /// <summary>
        /// 邻接表构建——allEdges=true 全边；false 仅无事件边（环检测用）
        /// </summary>
        /// <param name="graph">转移图</param>
        /// <param name="allEdges">边过滤开关</param>
        /// <returns>邻接表</returns>
        private static List<int>[] BuildAdjacency(GraphV3 graph, bool allEdges)
        {
            List<int>[] adj = new List<int>[graph.NodeNames.Count];
            for (int i = 0; i < adj.Length; i++)
            {
                adj[i] = new List<int>();
            }
            for (int e = 0; e < graph.Edges.Count; e++)
            {
                EdgeV3 edge = graph.Edges[e];
                if (!allEdges && edge.EventDriven)
                {
                    continue;
                }
                int fromIdx;
                int toIdx;
                if (graph.NodeIndex.TryGetValue(edge.From, out fromIdx)
                    && graph.NodeIndex.TryGetValue(edge.To, out toIdx))
                {
                    adj[fromIdx].Add(toIdx);
                }
            }
            return adj;
        }

        /// <summary>
        /// DFS 后序（Kosaraju 第一步）
        /// </summary>
        /// <param name="adj">邻接表</param>
        /// <param name="node">当前节点</param>
        /// <param name="visited">访问标记</param>
        /// <param name="order">后序输出</param>
        private static void DfsOrder(List<int>[] adj, int node, bool[] visited, List<int> order)
        {
            visited[node] = true;
            for (int e = 0; e < adj[node].Count; e++)
            {
                if (!visited[adj[node][e]])
                {
                    DfsOrder(adj, adj[node][e], visited, order);
                }
            }
            order.Add(node);
        }

        /// <summary>
        /// SCC 分量标注（Kosaraju 第二步）
        /// </summary>
        /// <param name="rev">反向图</param>
        /// <param name="node">当前节点</param>
        /// <param name="comp">分量标注</param>
        /// <param name="compId">分量 ID</param>
        private static void DfsAssign(List<int>[] rev, int node, int[] comp, int compId)
        {
            comp[node] = compId;
            for (int e = 0; e < rev[node].Count; e++)
            {
                if (comp[rev[node][e]] == -1)
                {
                    DfsAssign(rev, rev[node][e], comp, compId);
                }
            }
        }
    }

    /// <summary>
    /// 状态转移图——节点/边/初始集/外生集/出度
    /// </summary>
    public sealed class GraphV3
    {
        /// <summary>
        /// 节点 key（状态机.状态）→ 下标
        /// </summary>
        public Dictionary<string, int> NodeIndex = new Dictionary<string, int>(StringComparer.Ordinal);

        /// <summary>
        /// 节点名（与 NodeIndex 反向）
        /// </summary>
        public List<string> NodeNames = new List<string>();

        /// <summary>
        /// 节点声明行号（诊断定位）
        /// </summary>
        public List<int> NodeLine = new List<int>();

        /// <summary>
        /// 初始状态集（每个状态机首元素）
        /// </summary>
        public List<string> InitialNodes = new List<string>();

        /// <summary>
        /// 外生可达目标集（无状态断言条件的导线目标）
        /// </summary>
        public List<string> ExternNodes = new List<string>();

        /// <summary>
        /// 转移边
        /// </summary>
        public List<EdgeV3> Edges = new List<EdgeV3>();

        /// <summary>
        /// 出度（终态 = 出度 0）
        /// </summary>
        public int[] OutDegree = new int[0];
    }

    /// <summary>
    /// 转移边——源状态 → 目标状态（携带导线 + 事件驱动标记）
    /// </summary>
    public sealed class EdgeV3
    {
        /// <summary>
        /// 源状态 key
        /// </summary>
        public string From = "";

        /// <summary>
        /// 目标状态 key
        /// </summary>
        public string To = "";

        /// <summary>
        /// 产生该边的导线
        /// </summary>
        public WireDefV3 Wire = new WireDefV3();

        /// <summary>
        /// 导线是否含传感器条件（事件驱动刹车）
        /// </summary>
        public bool EventDriven;
    }

    /// <summary>
    /// 分析结果——错误拦截 + 报告
    /// </summary>
    public sealed class AnalysisResultV3
    {
        /// <summary>
        /// 分析错误（拦截——分析失败 = 拒绝生成）
        /// </summary>
        public List<MauDiagnostic> Errors = new List<MauDiagnostic>();

        /// <summary>
        /// 分析报告（关键路径等——排错定位用，不拦截）
        /// </summary>
        public List<string> Reports = new List<string>();

        /// <summary>
        /// 分析是否通过
        /// </summary>
        public bool Success;
    }
}
