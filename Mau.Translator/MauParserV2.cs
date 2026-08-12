using System;
using System.Collections.Generic;

namespace Mau.Translator
{
    /// <summary>
    /// 解析结果
    /// </summary>
    public sealed class ParseResultV2
    {
        /// <summary>是否成功——无错误诊断</summary>
        public bool Success = true;

        /// <summary>诊断列表（E1xx 语法）</summary>
        public List<MauDiagnostic> Diagnostics = new List<MauDiagnostic>();

        /// <summary>解析文档——成功时可用</summary>
        public MauDocV2 Doc = new MauDocV2();
    }

    /// <summary>
    /// 解析层——段 → IR（含类型推导三遍扫描）
    /// </summary>
    public static class MauParserV2
    {
        /// <summary>
        /// 解析入口——词法结果 → IR
        /// </summary>
        /// <param name="lex">词法结果</param>
        /// <returns>解析结果</returns>
        public static ParseResultV2 Parse(LexResultV2 lex)
        {
            ParseResultV2 result = new ParseResultV2();
            if (lex == null)
            {
                result.Diagnostics.Add(new MauDiagnostic("E100", 0, "词法结果为空"));
                result.Success = false;
                return result;
            }

            for (int i = 0; i < lex.Sections.Count; i++)
            {
                MauSectionV2 section = lex.Sections[i];
                List<TokV2> tokens = Tokenize(section.Text);
                switch (section.Kind)
                {
                    case MauSectionKindV2.Version:
                        ParseVersion(tokens, lex, result, section.Index);
                        break;
                    case MauSectionKindV2.Interface:
                        ParseInterface(tokens, lex, result, section.Index);
                        break;
                    case MauSectionKindV2.Inject:
                        ParseInject(tokens, lex, result, section.Index);
                        break;
                    case MauSectionKindV2.MachineDef:
                        ParseMachineDef(tokens, lex, result, section.Index);
                        break;
                    case MauSectionKindV2.MachineBind:
                        ParseMachineBind(tokens, lex, result, section.Index);
                        break;
                    case MauSectionKindV2.Propositions:
                        ParsePropositions(tokens, lex, result, section.Index);
                        break;
                    case MauSectionKindV2.Resource:
                        ParseResource(tokens, lex, result, section.Index);
                        break;
                    case MauSectionKindV2.BoundaryIn:
                        ParseBoundary(tokens, lex, result, section.Index, BoundaryDirV2.In);
                        break;
                    case MauSectionKindV2.BoundaryOut:
                        ParseBoundary(tokens, lex, result, section.Index, BoundaryDirV2.Out);
                        break;
                    case MauSectionKindV2.Measure:
                        ParseMeasure(tokens, lex, result, section.Index);
                        break;
                    case MauSectionKindV2.Law:
                        ParseLaw(tokens, lex, result, section.Index);
                        break;
                    case MauSectionKindV2.Sugar:
                        ParseSugar(tokens, lex, result, section.Index);
                        break;
                    default:
                        result.Diagnostics.Add(new MauDiagnostic("E101", section.Index, "无法识别的段结构"));
                        break;
                }
                if (!result.Success)
                {
                    return result;
                }
            }

            // 类型推导三遍扫描（边界 → 测量 → 结果侧）
            InferKinds(result);
            if (!result.Success)
            {
                return result;
            }

            result.Success = result.Diagnostics.Count == 0;
            return result;
        }

        // ==================== 版本/接口/注入 ====================

        /// <summary>
        /// 版本声明——'Mau' 2.0 / 'Mau.Runtime' 2.0
        /// </summary>
        /// <param name="tokens">token 序列</param>
        /// <param name="lex">词法结果</param>
        /// <param name="result">解析结果</param>
        /// <param name="sectionNo">段号</param>
        private static void ParseVersion(List<TokV2> tokens, LexResultV2 lex, ParseResultV2 result, int sectionNo)
        {
            if (tokens.Count < 2 || tokens[0].Kind != 'N' || tokens[1].Kind != 'D')
            {
                Error(result, sectionNo, "E101", "版本声明格式错误——应为 '名称' 版本号");
                return;
            }
            string name = lex.Names[tokens[0].Index];
            string value = tokens[1].Text;
            if (name == "Mau")
            {
                result.Doc.SyntaxVersion = value;
            }
            else if (name == "Mau.Runtime")
            {
                result.Doc.RuntimeVersion = value;
            }
            else
            {
                Error(result, sectionNo, "E101", "未知版本声明——'" + name + "'（应为 Mau / Mau.Runtime）");
            }
        }

        /// <summary>
        /// 实现接口——@'...'
        /// </summary>
        /// <param name="tokens">token 序列</param>
        /// <param name="lex">词法结果</param>
        /// <param name="result">解析结果</param>
        /// <param name="sectionNo">段号</param>
        private static void ParseInterface(List<TokV2> tokens, LexResultV2 lex, ParseResultV2 result, int sectionNo)
        {
            if (tokens.Count < 2 || tokens[0].Kind != 'S' || tokens[1].Kind != 'N')
            {
                Error(result, sectionNo, "E101", "接口声明格式错误——应为 @'接口名'");
                return;
            }
            result.Doc.Interfaces.Add(lex.Names[tokens[1].Index]);
        }

        /// <summary>
        /// 注入字段——$'...', '...'
        /// </summary>
        /// <param name="tokens">token 序列</param>
        /// <param name="lex">词法结果</param>
        /// <param name="result">解析结果</param>
        /// <param name="sectionNo">段号</param>
        private static void ParseInject(List<TokV2> tokens, LexResultV2 lex, ParseResultV2 result, int sectionNo)
        {
            if (tokens.Count < 2 || tokens[0].Kind != 'S')
            {
                Error(result, sectionNo, "E101", "注入声明格式错误——应为 $'字段', '字段'");
                return;
            }
            for (int i = 1; i < tokens.Count; i++)
            {
                if (tokens[i].Kind == 'S' && tokens[i].Text == ",")
                {
                    continue;
                }
                if (tokens[i].Kind != 'N')
                {
                    Error(result, sectionNo, "E101", "注入字段格式错误——应为命名");
                    return;
                }
                result.Doc.Injections.Add(lex.Names[tokens[i].Index]);
            }
        }

        // ==================== 状态机 ====================

        /// <summary>
        /// 状态机集合定义——'S_X' = { 'Idle', ... }
        /// </summary>
        /// <param name="tokens">token 序列</param>
        /// <param name="lex">词法结果</param>
        /// <param name="result">解析结果</param>
        /// <param name="sectionNo">段号</param>
        private static void ParseMachineDef(List<TokV2> tokens, LexResultV2 lex, ParseResultV2 result, int sectionNo)
        {
            // N = { N, N, ... }
            if (tokens.Count < 4 || tokens[0].Kind != 'N' || tokens[1].Kind != 'S'
                || tokens[1].Text != "=" || tokens[2].Kind != 'S' || tokens[2].Text != "{")
            {
                Error(result, sectionNo, "E101", "状态机声明格式错误——应为 'S_X' = { 'A', 'B', ... }");
                return;
            }
            MachineV2 machine = new MachineV2();
            machine.Name = lex.Names[tokens[0].Index];
            for (int i = 3; i < tokens.Count; i++)
            {
                if (tokens[i].Kind == 'S' && tokens[i].Text == "}")
                {
                    break;
                }
                if (tokens[i].Kind == 'S' && tokens[i].Text == ",")
                {
                    continue;
                }
                if (tokens[i].Kind != 'N')
                {
                    Error(result, sectionNo, "E101", "状态名格式错误——应为命名");
                    return;
                }
                machine.States.Add(lex.Names[tokens[i].Index]);
            }
            if (machine.States.Count == 0)
            {
                Error(result, sectionNo, "E101", "状态机 '" + machine.Name + "' 无状态");
                return;
            }
            result.Doc.Machines.Add(machine);
        }

        /// <summary>
        /// 状态机嵌套绑定——'S_Child' ∈ 'S_Parent.State'
        /// </summary>
        /// <param name="tokens">token 序列</param>
        /// <param name="lex">词法结果</param>
        /// <param name="result">解析结果</param>
        /// <param name="sectionNo">段号</param>
        private static void ParseMachineBind(List<TokV2> tokens, LexResultV2 lex, ParseResultV2 result, int sectionNo)
        {
            // N ∈ N
            if (tokens.Count != 3 || tokens[0].Kind != 'N' || tokens[1].Kind != 'S' || tokens[2].Kind != 'N')
            {
                Error(result, sectionNo, "E101", "嵌套绑定格式错误——应为 'S_Child' ∈ 'S_Parent.State'");
                return;
            }
            MachineBindV2 bind = new MachineBindV2();
            bind.ChildName = lex.Names[tokens[0].Index];
            string parentRef = lex.Names[tokens[2].Index];
            // 'S_Talk.Active' → Parent='S_Talk', State='Active'
            int dot = parentRef.LastIndexOf('.');
            if (dot <= 0 || dot == parentRef.Length - 1)
            {
                Error(result, sectionNo, "E101", "嵌套绑定父引用格式错误——应为 'S_Parent.State'，实际 '" + parentRef + "'");
                return;
            }
            bind.ParentName = parentRef.Substring(0, dot);
            bind.ParentState = parentRef.Substring(dot + 1);
            // 子状态机由 MachineDef 段声明——此处只登记绑定
            // 绑定登记到父状态机
            MachineV2? parent = result.Doc.FindMachine(bind.ParentName);
            if (parent == null)
            {
                Error(result, sectionNo, "E101", "嵌套绑定父状态机不存在——'" + bind.ParentName + "'（需先声明）");
                return;
            }
            // 父状态隐式加入——嵌套父状态无须在集合中显式列出（案例 14.2 形态）
            if (!parent.States.Contains(bind.ParentState))
            {
                parent.States.Add(bind.ParentState);
            }
            parent.Binds.Add(bind);
        }

        // ==================== 命题/资源/边界 ====================

        /// <summary>
        /// 命题声明——'P_A', 'P_B'
        /// </summary>
        /// <param name="tokens">token 序列</param>
        /// <param name="lex">词法结果</param>
        /// <param name="result">解析结果</param>
        /// <param name="sectionNo">段号</param>
        private static void ParsePropositions(List<TokV2> tokens, LexResultV2 lex, ParseResultV2 result, int sectionNo)
        {
            for (int i = 0; i < tokens.Count; i++)
            {
                if (tokens[i].Kind == 'S' && tokens[i].Text == ",")
                {
                    continue;
                }
                if (tokens[i].Kind != 'N')
                {
                    Error(result, sectionNo, "E101", "命题声明格式错误——应为命名列表");
                    return;
                }
                string name = lex.Names[tokens[i].Index];
                if (result.Doc.FindProposition(name) != null)
                {
                    Error(result, sectionNo, "E101", "命题重复声明——'" + name + "'");
                    return;
                }
                result.Doc.Propositions.Add(new PropositionV2 { Name = name });
            }
        }

        /// <summary>
        /// 资源声明——'R_X': 1
        /// </summary>
        /// <param name="tokens">token 序列</param>
        /// <param name="lex">词法结果</param>
        /// <param name="result">解析结果</param>
        /// <param name="sectionNo">段号</param>
        private static void ParseResource(List<TokV2> tokens, LexResultV2 lex, ParseResultV2 result, int sectionNo)
        {
            // N : 数值
            if (tokens.Count != 3 || tokens[0].Kind != 'N' || tokens[1].Kind != 'S'
                || tokens[1].Text != ":" || tokens[2].Kind != 'D')
            {
                Error(result, sectionNo, "E101", "资源声明格式错误——应为 'R_X': 配额");
                return;
            }
            ResourceV2 res = new ResourceV2();
            res.Name = lex.Names[tokens[0].Index];
            res.Quota = ParseNumber(tokens[2].Text, -1);
            if (res.Quota <= 0)
            {
                Error(result, sectionNo, "E101", "资源配额必须为正整数——'" + res.Name + "'");
                return;
            }
            result.Doc.Resources.Add(res);
        }

        /// <summary>
        /// 系统边界——⇐ / ⇒
        /// </summary>
        /// <param name="tokens">token 序列</param>
        /// <param name="lex">词法结果</param>
        /// <param name="result">解析结果</param>
        /// <param name="sectionNo">段号</param>
        /// <param name="dir">方向</param>
        private static void ParseBoundary(List<TokV2> tokens, LexResultV2 lex, ParseResultV2 result, int sectionNo, BoundaryDirV2 dir)
        {
            // 形态：⇐ N [A] [→ N] / ⇒ N [→ N] [A]
            if (tokens.Count < 2 || tokens[0].Kind != 'S' || tokens[1].Kind != 'N')
            {
                Error(result, sectionNo, "E101", "边界声明格式错误——应为 ⇐/⇒ 信号 [→ 目标]");
                return;
            }
            BoundaryV2 boundary = new BoundaryV2();
            boundary.Dir = dir;
            boundary.SignalName = lex.Names[tokens[1].Index];
            int i = 2;
            if (i < tokens.Count && tokens[i].Kind == 'A')
            {
                boundary.Params = GetParams(tokens[i].Index, lex);
                i = i + 1;
            }
            if (i < tokens.Count)
            {
                if (tokens[i].Kind == 'S' && tokens[i].Text == "→" && i + 1 < tokens.Count && tokens[i + 1].Kind == 'N')
                {
                    boundary.MappedName = lex.Names[tokens[i + 1].Index];
                    i = i + 2;
                }
                else
                {
                    Error(result, sectionNo, "E101", "边界声明格式错误——多余的 token");
                    return;
                }
            }
            if (i < tokens.Count)
            {
                Error(result, sectionNo, "E101", "边界声明格式错误——多余的 token");
                return;
            }
            result.Doc.Boundaries.Add(boundary);
        }

        // ==================== 测量 ====================

        /// <summary>
        /// 测量——'M_X'[ω=1]: 'P_Y' := 'brick'[...]
        /// </summary>
        /// <param name="tokens">token 序列</param>
        /// <param name="lex">词法结果</param>
        /// <param name="result">解析结果</param>
        /// <param name="sectionNo">段号</param>
        private static void ParseMeasure(List<TokV2> tokens, LexResultV2 lex, ParseResultV2 result, int sectionNo)
        {
            // N A : N := N [A]
            if (tokens.Count < 6 || tokens[0].Kind != 'N' || tokens[1].Kind != 'A'
                || tokens[2].Kind != 'S' || tokens[2].Text != ":"
                || tokens[3].Kind != 'N' || tokens[4].Kind != 'S' || tokens[4].Text != ":="
                || tokens[5].Kind != 'N')
            {
                Error(result, sectionNo, "E101", "测量声明格式错误——应为 'M_X'[ω=1]: 'P_Y' := 'brick'[...]");
                return;
            }
            MeasureV2 measure = new MeasureV2();
            measure.Name = lex.Names[tokens[0].Index];
            // 属性容器——ω 帧
            List<MauParamV2> attrs = GetParams(tokens[1].Index, lex);
            for (int a = 0; a < attrs.Count; a++)
            {
                if (attrs[a].Kind == MauParamKindV2.AttrOmega)
                {
                    measure.Frame = ParseNumber(attrs[a].Text, 1);
                }
            }
            measure.Target = lex.Names[tokens[3].Index];
            measure.Sample.BrickName = lex.Names[tokens[5].Index];
            if (tokens.Count > 6)
            {
                if (tokens[6].Kind == 'A')
                {
                    measure.Sample.Params = GetParams(tokens[6].Index, lex);
                }
                else
                {
                    Error(result, sectionNo, "E101", "测量声明格式错误——积木参数应为参数容器");
                    return;
                }
            }
            if (tokens.Count > 7)
            {
                Error(result, sectionNo, "E101", "测量声明格式错误——多余的 token");
                return;
            }
            result.Doc.Measures.Add(measure);
        }

        // ==================== 控制律 ====================

        /// <summary>
        /// 控制律——'T_X'[τ=10]: 条件 + 操作 → 结果 | 结果
        /// </summary>
        /// <param name="tokens">token 序列</param>
        /// <param name="lex">词法结果</param>
        /// <param name="result">解析结果</param>
        /// <param name="sectionNo">段号</param>
        private static void ParseLaw(List<TokV2> tokens, LexResultV2 lex, ParseResultV2 result, int sectionNo)
        {
            if (tokens.Count < 4 || tokens[0].Kind != 'N')
            {
                Error(result, sectionNo, "E101", "控制律格式错误——应为 'T_X'[τ=10]: ...");
                return;
            }
            LawV2 law = new LawV2();
            law.Name = lex.Names[tokens[0].Index];
            law.Conditions.Kind = CondKindV2.And;
            law.Conditions.Items = new List<CondV2>();
            int i = 1;
            if (i < tokens.Count && tokens[i].Kind == 'A')
            {
                ParseAttrs(tokens[i].Index, lex, law.Attrs);
                i = i + 1;
            }
            if (i >= tokens.Count || tokens[i].Kind != 'S' || tokens[i].Text != ":")
            {
                Error(result, sectionNo, "E101", "控制律格式错误——缺少冒号");
                return;
            }
            i = i + 1;

            // 前置区——条件 + 操作（+ 分隔；N 后跟 A = 操作）
            List<CondV2> conds = new List<CondV2>();
            int arrow = FindToken(tokens, i, "→");
            if (arrow < 0)
            {
                Error(result, sectionNo, "E101", "控制律格式错误——缺少 →");
                return;
            }
            ParsePreface(tokens, i, arrow, lex, result, sectionNo, law, conds);
            if (!result.Success)
            {
                return;
            }
            for (int c = 0; c < conds.Count; c++)
            {
                law.Conditions.Items.Add(conds[c]);
            }
            // 单条件段——顶层直接用该条件（保留 ∨/∧ 结构）；多段 → And 合取
            if (conds.Count == 1)
            {
                law.Conditions = conds[0];
            }

            // 结果区——→ 后 | 分隔分支
            i = arrow + 1;
            List<ResultV2> results = new List<ResultV2>();
            while (i < tokens.Count)
            {
                if (tokens[i].Kind == 'S' && tokens[i].Text == "|")
                {
                    i = i + 1;
                    continue;
                }
                if (i + 1 < tokens.Count && tokens[i].Kind == 'N' && tokens[i + 1].Kind == 'S' && tokens[i + 1].Text == "=")
                {
                    // 状态转移——N = N
                    if (i + 2 >= tokens.Count || tokens[i + 2].Kind != 'N')
                    {
                        Error(result, sectionNo, "E101", "控制律结果格式错误——状态转移应为 'S_X' = 'Y'");
                        return;
                    }
                    ResultV2 r = new ResultV2();
                    r.Kind = ResultKindV2.StateTransfer;
                    r.MachineName = lex.Names[tokens[i].Index];
                    r.StateName = lex.Names[tokens[i + 2].Index];
                    results.Add(r);
                    i = i + 3;
                }
                else if (tokens[i].Kind == 'N')
                {
                    ResultV2 r = new ResultV2();
                    r.Kind = ResultKindV2.PropRegister;
                    r.PropName = lex.Names[tokens[i].Index];
                    results.Add(r);
                    i = i + 1;
                }
                else
                {
                    Error(result, sectionNo, "E101", "控制律结果格式错误——无法识别的分支");
                    return;
                }
            }
            if (results.Count == 0)
            {
                Error(result, sectionNo, "E101", "控制律 '" + law.Name + "' 无结果分支");
                return;
            }
            law.Results = results;
            law.ResultKind = results[0].Kind;
            // 结果侧单一类型检查（混用留给验证层，此处先收拢）
            for (int r = 1; r < results.Count; r++)
            {
                if (results[r].Kind != law.ResultKind)
                {
                    Error(result, sectionNo, "E101", "控制律结果侧单一类型违规——状态转移与命题注册混用");
                    return;
                }
            }
            result.Doc.Laws.Add(law);
        }

        /// <summary>
        /// 前置区解析——条件段 + 操作段（+ 分隔）
        /// </summary>
        /// <param name="tokens">token 序列</param>
        /// <param name="start">起始位置（冒号后）</param>
        /// <param name="end">结束位置（→ 前）</param>
        /// <param name="lex">词法结果</param>
        /// <param name="result">解析结果</param>
        /// <param name="sectionNo">段号</param>
        /// <param name="law">控制律（操作写入）</param>
        /// <param name="conds">条件收集</param>
        private static void ParsePreface(List<TokV2> tokens, int start, int end, LexResultV2 lex, ParseResultV2 result, int sectionNo, LawV2 law, List<CondV2> conds)
        {
            // 按 + 切段（顶层）
            int segStart = start;
            for (int i = start; i <= end; i++)
            {
                bool isLast = i == end;
                bool isPlus = !isLast && tokens[i].Kind == 'S' && tokens[i].Text == "+";
                if (!isLast && !isPlus)
                {
                    continue;
                }
                int segEnd = isLast ? end : i;
                if (segEnd > segStart)
                {
                    ParsePrefaceSegment(tokens, segStart, segEnd, lex, result, sectionNo, law, conds);
                    if (!result.Success)
                    {
                        return;
                    }
                }
                segStart = i + 1;
            }
        }

        /// <summary>
        /// 前置段解析——单段（条件表达式或积木调用）
        /// </summary>
        /// <param name="tokens">token 序列</param>
        /// <param name="start">段起始</param>
        /// <param name="end">段结束（不含）</param>
        /// <param name="lex">词法结果</param>
        /// <param name="result">解析结果</param>
        /// <param name="sectionNo">段号</param>
        /// <param name="law">控制律（操作写入）</param>
        /// <param name="conds">条件收集</param>
        private static void ParsePrefaceSegment(List<TokV2> tokens, int start, int end, LexResultV2 lex, ParseResultV2 result, int sectionNo, LawV2 law, List<CondV2> conds)
        {
            // 捕获赋值——N := N A（别名 := 积木调用）
            int assign = FindToken(tokens, start, ":=");
            if (assign >= 0 && assign < end)
            {
                if (assign == start || assign + 1 >= end || tokens[assign + 1].Kind != 'N')
                {
                    Error(result, sectionNo, "E101", "捕获赋值格式错误——应为 '别名' := '积木'[...]");
                    return;
                }
                BrickCallV2 call = new BrickCallV2();
                call.CaptureName = lex.Names[tokens[start].Index];
                call.BrickName = lex.Names[tokens[assign + 1].Index];
                if (assign + 2 < end && tokens[assign + 2].Kind == 'A')
                {
                    call.Params = GetParams(tokens[assign + 2].Index, lex);
                }
                law.Ops.Add(call);
                return;
            }

            // 积木调用——N 后跟 A
            if (tokens[start].Kind == 'N' && start + 1 < end && tokens[start + 1].Kind == 'A')
            {
                BrickCallV2 call = new BrickCallV2();
                call.BrickName = lex.Names[tokens[start].Index];
                call.Params = GetParams(tokens[start + 1].Index, lex);
                if (start + 2 < end)
                {
                    Error(result, sectionNo, "E101", "控制律操作格式错误——积木调用后有多余内容");
                    return;
                }
                law.Ops.Add(call);
                return;
            }

            // 条件表达式
            CondV2 cond = ParseCondExpr(tokens, start, end, lex, result, sectionNo);
            if (!result.Success)
            {
                return;
            }
            conds.Add(cond);
        }

        /// <summary>
        /// 条件表达式解析——∧/∨ 树
        /// </summary>
        /// <param name="tokens">token 序列</param>
        /// <param name="start">起始（含）</param>
        /// <param name="end">结束（不含）</param>
        /// <param name="lex">词法结果</param>
        /// <param name="result">解析结果</param>
        /// <param name="sectionNo">段号</param>
        /// <returns>条件树</returns>
        private static CondV2 ParseCondExpr(List<TokV2> tokens, int start, int end, LexResultV2 lex, ParseResultV2 result, int sectionNo)
        {
            // 先按 ∨ 拆分（顶层），再按 ∧ 拆分（顶层）
            List<int> orSplits = new List<int>();
            for (int i = start; i < end; i++)
            {
                if (tokens[i].Kind == 'S' && tokens[i].Text == "∨")
                {
                    orSplits.Add(i);
                }
            }
            if (orSplits.Count > 0)
            {
                CondV2 or = new CondV2();
                or.Kind = CondKindV2.Or;
                or.Items = new List<CondV2>();
                int segStart = start;
                for (int s = 0; s <= orSplits.Count; s++)
                {
                    int segEnd = s < orSplits.Count ? orSplits[s] : end;
                    CondV2 child = ParseCondAnd(tokens, segStart, segEnd, lex, result, sectionNo);
                    if (!result.Success)
                    {
                        return new CondV2();
                    }
                    or.Items.Add(child);
                    segStart = segEnd + 1;
                }
                return or;
            }
            return ParseCondAnd(tokens, start, end, lex, result, sectionNo);
        }

        /// <summary>
        /// 合取解析——∧ 拆分子项
        /// </summary>
        /// <param name="tokens">token 序列</param>
        /// <param name="start">起始（含）</param>
        /// <param name="end">结束（不含）</param>
        /// <param name="lex">词法结果</param>
        /// <param name="result">解析结果</param>
        /// <param name="sectionNo">段号</param>
        /// <returns>条件树</returns>
        private static CondV2 ParseCondAnd(List<TokV2> tokens, int start, int end, LexResultV2 lex, ParseResultV2 result, int sectionNo)
        {
            List<int> andSplits = new List<int>();
            for (int i = start; i < end; i++)
            {
                if (tokens[i].Kind == 'S' && tokens[i].Text == "∧")
                {
                    andSplits.Add(i);
                }
            }
            if (andSplits.Count > 0)
            {
                CondV2 and = new CondV2();
                and.Kind = CondKindV2.And;
                and.Items = new List<CondV2>();
                int segStart = start;
                for (int s = 0; s <= andSplits.Count; s++)
                {
                    int segEnd = s < andSplits.Count ? andSplits[s] : end;
                    CondV2 child = ParseCondAtom(tokens, segStart, segEnd, lex, result, sectionNo);
                    if (!result.Success)
                    {
                        return new CondV2();
                    }
                    and.Items.Add(child);
                    segStart = segEnd + 1;
                }
                return and;
            }
            return ParseCondAtom(tokens, start, end, lex, result, sectionNo);
        }

        /// <summary>
        /// 条件原子解析——N = N（状态断言）/ N（命题/资源引用）
        /// </summary>
        /// <param name="tokens">token 序列</param>
        /// <param name="start">起始（含）</param>
        /// <param name="end">结束（不含）</param>
        /// <param name="lex">词法结果</param>
        /// <param name="result">解析结果</param>
        /// <param name="sectionNo">段号</param>
        /// <returns>条件原子</returns>
        private static CondV2 ParseCondAtom(List<TokV2> tokens, int start, int end, LexResultV2 lex, ParseResultV2 result, int sectionNo)
        {
            CondV2 cond = new CondV2();
            int len = end - start;
            if (len == 3 && tokens[start].Kind == 'N' && tokens[start + 1].Kind == 'S'
                && tokens[start + 1].Text == "=" && tokens[start + 2].Kind == 'N')
            {
                cond.Kind = CondKindV2.StateEquals;
                cond.MachineName = lex.Names[tokens[start].Index];
                cond.StateName = lex.Names[tokens[start + 2].Index];
                return cond;
            }
            if (len == 1 && tokens[start].Kind == 'N')
            {
                string refName = lex.Names[tokens[start].Index];
                if (refName.StartsWith("R_", StringComparison.Ordinal))
                {
                    cond.Kind = CondKindV2.ResourceRef;
                }
                else
                {
                    cond.Kind = CondKindV2.PropRef;
                }
                cond.RefName = refName;
                return cond;
            }
            Error(result, sectionNo, "E101", "条件格式错误——应为状态断言 'S_X' = 'Y' 或命题/资源引用");
            return cond;
        }

        // ==================== 语法糖 ====================

        /// <summary>
        /// 语法糖——'SEQ'['T_A' → 'T_B']
        /// </summary>
        /// <param name="tokens">token 序列</param>
        /// <param name="lex">词法结果</param>
        /// <param name="result">解析结果</param>
        /// <param name="sectionNo">段号</param>
        private static void ParseSugar(List<TokV2> tokens, LexResultV2 lex, ParseResultV2 result, int sectionNo)
        {
            if (tokens.Count < 2 || tokens[0].Kind != 'N' || tokens[1].Kind != 'A')
            {
                Error(result, sectionNo, "E101", "语法糖格式错误——应为 'SEQ'['T_A' → 'T_B']");
                return;
            }
            SugarV2 sugar = new SugarV2();
            sugar.Name = lex.Names[tokens[0].Index];
            List<MauParamV2> items = GetParams(tokens[1].Index, lex);
            for (int i = 0; i < items.Count; i++)
            {
                if (items[i].Kind != MauParamKindV2.Ref)
                {
                    Error(result, sectionNo, "E101", "语法糖引用格式错误——应为命名引用");
                    return;
                }
                sugar.RefNames.Add(items[i].Text);
            }
            result.Doc.Sugars.Add(sugar);
        }

        // ==================== 类型推导 ====================

        /// <summary>
        /// 类型推导三遍扫描——边界（信号）→ 测量（条件）→ 结果侧（事实）
        /// </summary>
        /// <param name="result">解析结果</param>
        private static void InferKinds(ParseResultV2 result)
        {
            MauDocV2 doc = result.Doc;

            // 第 1 遍：边界声明——⇐ 'P_X' → 信号
            for (int i = 0; i < doc.Boundaries.Count; i++)
            {
                BoundaryV2 b = doc.Boundaries[i];
                if (b.Dir != BoundaryDirV2.In)
                {
                    continue;
                }
                string signalName = b.MappedName != null ? b.MappedName : b.SignalName;
                SetPropKind(doc, signalName, PropKindV2.Signal, "boundary");
            }

            // 第 2 遍：测量——'P_Y' := ... → 条件
            for (int i = 0; i < doc.Measures.Count; i++)
            {
                SetPropKind(doc, doc.Measures[i].Target, PropKindV2.Condition, doc.Measures[i].Name);
            }

            // 第 3 遍：控制律结果侧——'P_Z' → 事实
            for (int i = 0; i < doc.Laws.Count; i++)
            {
                LawV2 law = doc.Laws[i];
                for (int r = 0; r < law.Results.Count; r++)
                {
                    if (law.Results[r].Kind == ResultKindV2.PropRegister)
                    {
                        SetPropKind(doc, law.Results[r].PropName!, PropKindV2.Fact, law.Name);
                    }
                }
            }
        }

        /// <summary>
        /// 设置命题类型——已推导冲突报错（写入源唯一）
        /// </summary>
        /// <param name="doc">文档</param>
        /// <param name="propName">命题名</param>
        /// <param name="kind">类型</param>
        /// <param name="source">写入源</param>
        private static void SetPropKind(MauDocV2 doc, string propName, PropKindV2 kind, string source)
        {
            PropositionV2? prop = doc.FindProposition(propName);
            if (prop == null)
            {
                // 隐式声明——引用即存在
                prop = new PropositionV2 { Name = propName };
                doc.Propositions.Add(prop);
            }
            if (prop.Kind != PropKindV2.Unknown && prop.Kind != kind)
            {
                // 写入冲突——多个写入源（E201 在验证层；此处保守记录为 Unknown 冲突标记）
                prop.WriteSource = prop.WriteSource + "+" + source;
                return;
            }
            prop.Kind = kind;
            prop.WriteSource = source;
        }

        // ==================== 工具 ====================

        /// <summary>
        /// 从参数表取容器
        /// </summary>
        /// <param name="index">A 索引</param>
        /// <param name="lex">词法结果</param>
        /// <returns>参数项列表</returns>
        private static List<MauParamV2> GetParams(int index, LexResultV2 lex)
        {
            if (index < 0 || index >= lex.Params.Count)
            {
                return new List<MauParamV2>();
            }
            MauParamV2 container = lex.Params[index];
            if (container.Items == null)
            {
                return new List<MauParamV2>();
            }
            return container.Items;
        }

        /// <summary>
        /// 属性容器解析——写入控制律属性
        /// </summary>
        /// <param name="index">A 索引</param>
        /// <param name="lex">词法结果</param>
        /// <param name="attrs">属性对象</param>
        private static void ParseAttrs(int index, LexResultV2 lex, LawAttrsV2 attrs)
        {
            List<MauParamV2> items = GetParams(index, lex);
            for (int i = 0; i < items.Count; i++)
            {
                MauParamV2 item = items[i];
                if (item.Kind == MauParamKindV2.AttrTau)
                {
                    attrs.Timeout = ParseNumber(item.Text, -1);
                }
                else if (item.Kind == MauParamKindV2.AttrOmega)
                {
                    attrs.PollFrame = ParseNumber(item.Text, 1);
                }
                else if (item.Kind == MauParamKindV2.AttrParallel)
                {
                    attrs.IsWorker = true;
                }
                else if (item.Kind == MauParamKindV2.AttrJoin)
                {
                    attrs.IsJoin = true;
                }
                else if (item.Kind == MauParamKindV2.AttrLog)
                {
                    attrs.IsLogging = true;
                }
            }
        }

        /// <summary>
        /// 数值解析——整数
        /// </summary>
        /// <param name="text">数值文本</param>
        /// <param name="fallback">非法回落值</param>
        /// <returns>数值</returns>
        private static int ParseNumber(string text, int fallback)
        {
            int dot = text.IndexOf('.');
            string intPart = dot >= 0 ? text.Substring(0, dot) : text;
            int value = 0;
            for (int i = 0; i < intPart.Length; i++)
            {
                char c = intPart[i];
                if (c < '0' || c > '9')
                {
                    return fallback;
                }
                value = value * 10 + (c - '0');
            }
            return value;
        }

        /// <summary>
        /// token 查找——符号
        /// </summary>
        /// <param name="tokens">token 序列</param>
        /// <param name="start">起始位置</param>
        /// <param name="symbol">符号文本</param>
        /// <returns>位置（未找到为 -1）</returns>
        private static int FindToken(List<TokV2> tokens, int start, string symbol)
        {
            for (int i = start; i < tokens.Count; i++)
            {
                if (tokens[i].Kind == 'S' && tokens[i].Text == symbol)
                {
                    return i;
                }
            }
            return -1;
        }

        /// <summary>
        /// 诊断添加
        /// </summary>
        /// <param name="result">解析结果</param>
        /// <param name="sectionNo">段号</param>
        /// <param name="code">错误码</param>
        /// <param name="message">消息</param>
        private static void Error(ParseResultV2 result, int sectionNo, string code, string message)
        {
            result.Diagnostics.Add(new MauDiagnostic(code, sectionNo, message));
            result.Success = false;
        }

        // ==================== token 化 ====================

        /// <summary>
        /// 内部 token——占位符/符号/数值
        /// </summary>
        private sealed class TokV2
        {
            /// <summary>类型——N 命名 / A 参数 / S 符号 / D 数值</summary>
            public char Kind;

            /// <summary>文本——符号/数值</summary>
            public string Text = "";

            /// <summary>索引——N/A 占位符索引</summary>
            public int Index;
        }

        /// <summary>
        /// 段文本 token 化——N{n}/A{n}/符号/数值
        /// </summary>
        /// <param name="text">段文本（占位符形态）</param>
        /// <returns>token 列表</returns>
        private static List<TokV2> Tokenize(string text)
        {
            List<TokV2> tokens = new List<TokV2>();
            int i = 0;
            while (i < text.Length)
            {
                char c = text[i];
                if (c == 'N' || c == 'A')
                {
                    // N{n}/A{n}
                    int j = i + 1;
                    if (j < text.Length && text[j] == '{')
                    {
                        j = j + 1;
                        int index = 0;
                        bool valid = false;
                        while (j < text.Length && text[j] >= '0' && text[j] <= '9')
                        {
                            index = index * 10 + (text[j] - '0');
                            j = j + 1;
                            valid = true;
                        }
                        if (valid && j < text.Length && text[j] == '}')
                        {
                            TokV2 tok = new TokV2();
                            tok.Kind = c;
                            tok.Index = index;
                            tokens.Add(tok);
                            i = j + 1;
                            continue;
                        }
                    }
                    // 非法——词法已拦截，此处跳过
                    i = i + 1;
                    continue;
                }
                if (c == ':' && i + 1 < text.Length && text[i + 1] == '=')
                {
                    TokV2 tok = new TokV2();
                    tok.Kind = 'S';
                    tok.Text = ":=";
                    tokens.Add(tok);
                    i = i + 2;
                    continue;
                }
                if ((c >= '0' && c <= '9') || c == '.')
                {
                    int j = i;
                    while (j < text.Length && ((text[j] >= '0' && text[j] <= '9') || text[j] == '.'))
                    {
                        j = j + 1;
                    }
                    TokV2 tok = new TokV2();
                    tok.Kind = 'D';
                    tok.Text = text.Substring(i, j - i);
                    tokens.Add(tok);
                    i = j;
                    continue;
                }
                TokV2 sym = new TokV2();
                sym.Kind = 'S';
                sym.Text = c.ToString();
                tokens.Add(sym);
                i = i + 1;
            }
            return tokens;
        }
    }
}
