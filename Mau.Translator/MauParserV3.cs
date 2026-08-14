using System;
using System.Collections.Generic;
using Mau.Contracts;

namespace Mau.Translator
{
    /// <summary>
    /// 解析器 v3——Token 流 → FSM 网络 IR（design-mau-v3 §二）。
    /// 单元类型由名前缀 + 结构符号推导，零单元关键字（词法宪法：语义词退化为符号或推导）：
    /// S_ → 状态机（= { ... }）/ P_ → 传感器（⇐ 被动 / ↻ 主动）/ T_ → 导线（: 条件 → 动作 | 结果）/ R_ → 槽（: 容量）。
    /// 错误码：E100 未知单元前缀 / E101 段结构缺失 / E102 状态集合格式 / E103 导线结构 / E104 槽格式 / E105 传感器格式。
    /// </summary>
    public static class MauParserV3
    {
        /// <summary>
        /// 解析 Token 流——按 § 分段，逐段按前缀分派
        /// </summary>
        /// <param name="tokens">词法 Token 流</param>
        /// <returns>FSM 网络 IR + 诊断</returns>
        public static MauDocV3 Parse(List<TokenV3> tokens)
        {
            MauDocV3 doc = new MauDocV3();
            if (tokens == null || tokens.Count == 0)
            {
                doc.Diagnostics.Add(new MauDiagnostic("E101", 1, "空 Token 流"));
                doc.Success = false;
                return doc;
            }
            // [段1] 按 § 切段——段外 token（§ 前的非注释）报 E101
            List<List<TokenV3>> sections = SplitSections(tokens, doc);
            for (int s = 0; s < sections.Count; s++)
            {
                List<TokenV3> section = sections[s];
                ParseSection(section, doc);
            }
            doc.Success = doc.Diagnostics.Count == 0;
            return doc;
        }

        /// <summary>
        /// 按 § 切段——每个段从 § 后第一个 token 到下一 § 前（含 Eof 前）。
        /// 段外 token（第一个 § 之前的非注释内容）报 E101——声明必须以 § 开头。
        /// </summary>
        /// <param name="tokens">Token 流</param>
        /// <param name="doc">诊断收集</param>
        /// <returns>段列表（每段不含 § 标记自身）</returns>
        private static List<List<TokenV3>> SplitSections(List<TokenV3> tokens, MauDocV3 doc)
        {
            List<List<TokenV3>> sections = new List<List<TokenV3>>();
            List<TokenV3>? current = null;
            for (int i = 0; i < tokens.Count; i++)
            {
                TokenV3 t = tokens[i];
                if (t.Id == TokenIds.Section)
                {
                    if (current != null)
                    {
                        sections.Add(current);
                    }
                    current = new List<TokenV3>();
                    continue;
                }
                if (t.Id == TokenIds.Eof)
                {
                    if (current != null)
                    {
                        sections.Add(current);
                    }
                    break;
                }
                if (current != null)
                {
                    current.Add(t);
                }
                else
                {
                    doc.Diagnostics.Add(new MauDiagnostic("E101", t.Line, "段外 token——声明必须以 § 段首标记开头"));
                }
            }
            return sections;
        }

        /// <summary>
        /// 解析单个段——按段首 Name 前缀分派单元
        /// </summary>
        /// <param name="section">段 token（不含 §）</param>
        /// <param name="doc">输出 IR</param>
        private static void ParseSection(List<TokenV3> section, MauDocV3 doc)
        {
            // [段1] 空段（§ 后无内容）无语义——排版自由
            if (section.Count == 0)
            {
                return;
            }
            TokenV3 first = section[0];
            if (first.Id != TokenIds.Name)
            {
                doc.Diagnostics.Add(new MauDiagnostic("E100", first.Line, "段首必须是专有名词（'S_...'/'P_...'/'T_...'/'R_...'）"));
                return;
            }
            string name = first.Value;
            if (name.StartsWith("S_", StringComparison.Ordinal))
            {
                ParseStateMachine(name, first, section, doc);
            }
            else if (name.StartsWith("P_", StringComparison.Ordinal))
            {
                ParseSensor(name, first, section, doc);
            }
            else if (name.StartsWith("T_", StringComparison.Ordinal))
            {
                ParseWire(name, first, section, doc);
            }
            else if (name.StartsWith("R_", StringComparison.Ordinal))
            {
                ParseSlot(name, first, section, doc);
            }
            else
            {
                doc.Diagnostics.Add(new MauDiagnostic("E100", first.Line, "未知单元前缀: '" + name + "'——只支持 S_（状态机）/ P_（传感器）/ T_（导线）/ R_（槽）"));
            }
        }

        /// <summary>
        /// 解析状态机段——§ 'S_X' = { 'A', 'B' }
        /// </summary>
        /// <param name="name">状态机名</param>
        /// <param name="first">段首 token</param>
        /// <param name="section">段 token</param>
        /// <param name="doc">输出 IR</param>
        private static void ParseStateMachine(string name, TokenV3 first, List<TokenV3> section, MauDocV3 doc)
        {
            if (section.Count < 5 || section[1].Id != TokenIds.Eq || section[2].Id != TokenIds.SetOpen)
            {
                doc.Diagnostics.Add(new MauDiagnostic("E102", first.Line, "状态机格式: § 'S_X' = { 'A', 'B' }——缺 = 或 {"));
                return;
            }
            StateMachineDefV3 def = new StateMachineDefV3();
            def.Name = name;
            def.Line = first.Line;
            int i = 3;
            while (i < section.Count && section[i].Id != TokenIds.SetClose)
            {
                if (section[i].Id != TokenIds.Name)
                {
                    doc.Diagnostics.Add(new MauDiagnostic("E102", section[i].Line, "状态集合内必须是专有名词——第 " + i + " 个元素非法"));
                    return;
                }
                if (def.States.Contains(section[i].Value))
                {
                    doc.Diagnostics.Add(new MauDiagnostic("E102", section[i].Line, "状态重名: '" + section[i].Value + "'"));
                    return;
                }
                def.States.Add(section[i].Value);
                i = i + 1;
                if (i < section.Count && section[i].Id == TokenIds.Sep)
                {
                    i = i + 1;
                }
            }
            if (i >= section.Count || section[i].Id != TokenIds.SetClose)
            {
                doc.Diagnostics.Add(new MauDiagnostic("E102", first.Line, "状态集合未闭合（缺 }）"));
                return;
            }
            if (def.States.Count == 0)
            {
                doc.Diagnostics.Add(new MauDiagnostic("E102", first.Line, "状态集合为空——至少一个状态（首元素 = 初始）"));
                return;
            }
            doc.StateMachines.Add(def);
        }
/// <summary>
/// 解析壳分支动作——→ 动作 | 动作（成功侧/失败侧——各侧可多积木调用）
/// </summary>
/// <param name = "section">段 token</param>
/// <param name = "start">动作区起点</param>
/// <param name = "def">输出传感器</param>
/// <param name = "doc">诊断收集</param>
/// <param name = "isTrue">true=成功侧（| 之前）</param>
private static void ParseSensorBranchActions(List<TokenV3> section, int start, SensorDefV3 def, MauDocV3 doc, bool isTrue)
{
    List<SensorActionV3> actions = isTrue ? def.TrueActions : def.FalseActions;
    int i = start;
    while (i < section.Count)
    {
        if (section[i].Id == TokenIds.Branch)
        {
            if (isTrue)
            {
                // | 切换失败侧——失败侧后续全部归 FalseActions
                ParseSensorBranchActions(section, i + 1, def, doc, false);
                return;
            }

            // 失败侧不再有 |（双分支终止）
            doc.Diagnostics.Add(new MauDiagnostic("E105", section[i].Line, "主动传感器分支最多两路: → 动作 | 动作"));
            return;
        }

        if (section[i].Id != TokenIds.Name)
        {
            doc.Diagnostics.Add(new MauDiagnostic("E105", section[i].Line, "主动传感器动作区非法 token——期望积木名"));
            return;
        }

        SensorActionV3 action = new SensorActionV3();
        action.BrickName = section[i].Value;
        i = i + 1;
        if (i < section.Count && section[i].Id == TokenIds.ParamOpen)
        {
            action.BrickArgs = CollectArgs(section, i);
            i = FindParamClose(section, i) + 1;
            if (i == 0)
            {
                i = section.Count;
            }
        }

        actions.Add(action);
    }
}
        /// <summary>
        /// 解析传感器段——§ 'P_X' ⇐（被动）/ § 'P_Q' ↻ [N] 'brick'[...]（主动）
        /// </summary>
        /// <param name="name">传感器名</param>
        /// <param name="first">段首 token</param>
        /// <param name="section">段 token</param>
        /// <param name="doc">输出 IR</param>
        private static void ParseSensor(string name, TokenV3 first, List<TokenV3> section, MauDocV3 doc)
{
            SensorDefV3 def = new SensorDefV3();
            def.Name = name;
            def.Line = first.Line;
            if (section.Count < 2)
            {
                doc.Diagnostics.Add(new MauDiagnostic("E105", first.Line, "传感器格式: § 'P_X' ⇐（被动）或 § 'P_Q' ↻ [N]: 'brick'[...] → 动作 | 动作（主动壳）"));
                return;
            }
            if (section[1].Id == TokenIds.In)
            {
                // 被动触发器——事件沿，帧边界消费（导线/状态机之间的延迟接口）
                def.Passive = true;
                doc.Sensors.Add(def);
                return;
            }
            if (section[1].Id == TokenIds.Sample)
            {
                // 主动壳——帧门控 + 探测（可捕获）→ 分支动作（只读世界，零状态转移）
                def.Passive = false;
                int i = 2;
                if (i < section.Count && section[i].Id == TokenIds.ParamOpen)
                {
                    int close = FindParamClose(section, i);
                    if (close < 0)
                    {
                        doc.Diagnostics.Add(new MauDiagnostic("E105", first.Line, "采样周期参数未闭合"));
                        return;
                    }
                    long every = 0;
                    if (!ParseFrames(section, i + 1, close, out every))
                    {
                        doc.Diagnostics.Add(new MauDiagnostic("E105", first.Line, "采样周期格式: [N 帧]——N 为数值"));
                        return;
                    }
                    def.EveryFrames = every;
                    i = close + 1;
                }
                if (i >= section.Count || section[i].Id != TokenIds.Colon)
                {
                    doc.Diagnostics.Add(new MauDiagnostic("E105", first.Line, "主动传感器缺冒号: § 'P_X' ↻ [N]: 'brick'[...] → 动作 | 动作"));
                    return;
                }
                i = i + 1;
                if (i >= section.Count || section[i].Id != TokenIds.Name)
                {
                    doc.Diagnostics.Add(new MauDiagnostic("E105", first.Line, "主动传感器缺探测积木名: ↻ [N]: 'brick'[...]"));
                    return;
                }
                def.BrickName = section[i].Value;
                i = i + 1;
                if (i < section.Count && section[i].Id == TokenIds.ParamOpen)
                {
                    def.BrickArgs = CollectArgs(section, i);
                    i = FindParamClose(section, i) + 1;
                    if (i == 0)
                    {
                        i = section.Count;
                    }
                }
                // 探测捕获——> @key（探测 out 落盒）
                if (i < section.Count && section[i].Id == TokenIds.Capture)
                {
                    i = i + 1;
                    if (i < section.Count && section[i].Id == TokenIds.Name)
                    {
                        def.CaptureTarget = section[i].Value;
                        i = i + 1;
                    }
                    else
                    {
                        doc.Diagnostics.Add(new MauDiagnostic("E105", first.Line, "探测捕获格式: > @key——缺盒子 Key"));
                        return;
                    }
                }
                // 分支动作——→ 动作 | 动作（成功侧/失败侧——各侧可多积木）
                int arrow = FindArrow(section, i);
                if (arrow >= 0)
                {
                    ParseSensorBranchActions(section, arrow + 1, def, doc, true);
                    if (doc.Diagnostics.Count > 0)
                    {
                        return;
                    }
                }
                doc.Sensors.Add(def);
                return;
            }
            doc.Diagnostics.Add(new MauDiagnostic("E105", first.Line, "传感器缺端口符号——⇐（被动）或 ↻（主动壳）"));
        }
        /// <summary>
        /// 解析导线段——§ 'T_X' [属性] : 条件 & 条件 → 动作 | 结果 | 结果
        /// </summary>
        /// <param name="name">导线名</param>
        /// <param name="first">段首 token</param>
        /// <param name="section">段 token</param>
        /// <param name="doc">输出 IR</param>
        private static void ParseWire(string name, TokenV3 first, List<TokenV3> section, MauDocV3 doc)
        {
            WireDefV3 def = new WireDefV3();
            def.Name = name;
            def.Line = first.Line;
            int i = 1;
            // [段1] 可选属性 [t=10] [par] [join] [!]
            if (i < section.Count && section[i].Id == TokenIds.ParamOpen)
            {
                int close = FindParamClose(section, i);
                if (close < 0)
                {
                    doc.Diagnostics.Add(new MauDiagnostic("E103", first.Line, "导线属性未闭合（缺 ]）"));
                    return;
                }
                ParseWireAttrs(section, i + 1, close, def, doc);
                if (doc.Diagnostics.Count > 0)
                {
                    return;
                }
                i = close + 1;
            }
            // [段2] 冒号分隔
            if (i >= section.Count || section[i].Id != TokenIds.Colon)
            {
                doc.Diagnostics.Add(new MauDiagnostic("E103", first.Line, "导线格式: § 'T_X' [属性] : 条件 → 动作 | 结果——缺冒号"));
                return;
            }
            i = i + 1;
            // [段3] 条件区——直到 →（传感器沿或状态断言，& 合取）
            int arrow = FindArrow(section, i);
            if (arrow < 0)
            {
                doc.Diagnostics.Add(new MauDiagnostic("E103", first.Line, "导线缺结果箭头 →"));
                return;
            }
            ParseConditions(section, i, arrow, def, doc);
            if (doc.Diagnostics.Count > 0)
            {
                return;
            }
            i = arrow + 1;
            // [段4] 动作区——积木名 + 参数（直到 | 或段尾）
            if (i < section.Count && section[i].Id == TokenIds.Name)
            {
                def.BrickName = section[i].Value;
                i = i + 1;
                if (i < section.Count && section[i].Id == TokenIds.ParamOpen)
                {
                    def.BrickArgs = CollectArgs(section, i);
                    i = FindParamClose(section, i) + 1;
                    if (i == 0)
                    {
                        i = section.Count;
                    }
                }
                else if (i < section.Count && section[i].Id == TokenIds.Branch)
                {
                    i = i + 1;
                }
                else
                {
                    i = section.Count;
                }
            }
            else if (i < section.Count && section[i].Id == TokenIds.Branch)
            {
                i = i + 1;
            }
            // [段4b] 捕获子句——> 'P_X'（动作积木首个 out 端口捕获到值传感器）
            if (def.BrickName.Length > 0 && i < section.Count && section[i].Id == TokenIds.Capture)
            {
                i = i + 1;
                if (i < section.Count && section[i].Id == TokenIds.Name)
                {
                    def.CaptureTarget = section[i].Value;
                    i = i + 1;
                }
                else
                {
                    doc.Diagnostics.Add(new MauDiagnostic("E103", first.Line, "捕获格式: 动作 > 'P_X'——缺值传感器名"));
                    return;
                }
            }
            // [段5] 结果区——'S_X' = 'W' 每个 | 分支一条
            while (i < section.Count)
            {
                if (section[i].Id == TokenIds.Name)
                {
                    ResultV3 result = new ResultV3();
                    result.StateName = section[i].Value;
                    int j = i + 1;
                    if (j < section.Count && section[j].Id == TokenIds.Eq)
                    {
                        j = j + 1;
                    }
                    if (j < section.Count && section[j].Id == TokenIds.Name)
                    {
                        result.StateValue = section[j].Value;
                        def.Results.Add(result);
                        i = j + 1;
                    }
                    else
                    {
                        doc.Diagnostics.Add(new MauDiagnostic("E103", section[i].Line, "结果格式: 'S_X' = 'W'——缺状态值"));
                        return;
                    }
                }
                else if (section[i].Id == TokenIds.Branch)
                {
                    i = i + 1;
                }
                else
                {
                    doc.Diagnostics.Add(new MauDiagnostic("E103", section[i].Line, "结果区非法 token"));
                    return;
                }
            }
            if (def.Results.Count == 0)
            {
                doc.Diagnostics.Add(new MauDiagnostic("E103", first.Line, "导线至少一个结果（状态转移）"));
                return;
            }
            doc.Wires.Add(def);
        }

        /// <summary>
        /// 解析槽段——§ 'R_X' : 容量
        /// </summary>
        /// <param name="name">槽名</param>
        /// <param name="first">段首 token</param>
        /// <param name="section">段 token</param>
        /// <param name="doc">输出 IR</param>
        private static void ParseSlot(string name, TokenV3 first, List<TokenV3> section, MauDocV3 doc)
        {
            if (section.Count < 3 || section[1].Id != TokenIds.Colon || section[2].Id != TokenIds.Num)
            {
                doc.Diagnostics.Add(new MauDiagnostic("E104", first.Line, "槽格式: § 'R_X' : 容量（数值，1 = 独占）"));
                return;
            }
            long capacity;
            if (!long.TryParse(section[2].Value, out capacity) || capacity < 1)
            {
                doc.Diagnostics.Add(new MauDiagnostic("E104", first.Line, "槽容量必须 ≥ 1: '" + section[2].Value + "'"));
                return;
            }
            SlotDefV3 def = new SlotDefV3();
            def.Name = name;
            def.Line = first.Line;
            def.Capacity = capacity;
            doc.Slots.Add(def);
        }

        /// <summary>
        /// 解析导线属性——[t=10] [par] [join] [!]
        /// </summary>
        /// <param name="section">段 token</param>
        /// <param name="start">属性内容起点</param>
        /// <param name="close">] 位置</param>
        /// <param name="def">输出导线</param>
        /// <param name="doc">诊断收集</param>
        private static void ParseWireAttrs(List<TokenV3> section, int start, int close,
            WireDefV3 def, MauDocV3 doc)
        {
            int i = start;
            while (i < close)
            {
                TokenV3 t = section[i];
                if (t.Id == TokenIds.Word && t.Value == "t")
                {
                    if (i + 2 < close && section[i + 1].Id == TokenIds.Eq && section[i + 2].Id == TokenIds.Num)
                    {
                        long timeout;
                        if (long.TryParse(section[i + 2].Value, out timeout))
                        {
                            def.Timeout = timeout;
                        }
                        i = i + 3;
                    }
                    else
                    {
                        doc.Diagnostics.Add(new MauDiagnostic("E103", t.Line, "时限格式: [t=N]——N 为数值"));
                        return;
                    }
                }
                else if (t.Id == TokenIds.Word && t.Value == "par")
                {
                    def.Parallel = true;
                    i = i + 1;
                }
                else if (t.Id == TokenIds.Word && t.Value == "join")
                {
                    def.Join = true;
                    i = i + 1;
                }
                else if (t.Id == TokenIds.Word && t.Value == "!")
                {
                    def.Logging = true;
                    i = i + 1;
                }
                else if (t.Id == TokenIds.Sep)
                {
                    i = i + 1;
                }
                else
                {
                    doc.Diagnostics.Add(new MauDiagnostic("E103", t.Line, "未知导线属性: '" + t.Value + "'——只支持 t=N / par / join / !"));
                    return;
                }
            }
        }

        /// <summary>
        /// 解析条件区——传感器沿（'P_X'）或状态断言（'S_X' = 'Y'），& 合取
        /// </summary>
        /// <param name="section">段 token</param>
        /// <param name="start">条件区起点</param>
        /// <param name="arrow">→ 位置</param>
        /// <param name="def">输出导线</param>
        /// <param name="doc">诊断收集</param>
        private static void ParseConditions(List<TokenV3> section, int start, int arrow,
            WireDefV3 def, MauDocV3 doc)
        {
            int i = start;
            while (i < arrow)
            {
                TokenV3 t = section[i];
                if (t.Id == TokenIds.And)
                {
                    i = i + 1;
                    continue;
                }
                if (t.Id == TokenIds.Name)
                {
                    ConditionV3 cond = new ConditionV3();
                    if (i + 2 < arrow && section[i + 1].Id == TokenIds.Eq)
                    {
                        cond.IsStateAssert = true;
                        cond.StateName = t.Value;
                        cond.StateValue = section[i + 2].Value;
                        i = i + 3;
                    }
                    else if (t.Value.Length > 0 && t.Value[0] == '@')
                    {
                        // 盒子判真——@key 私有盒（全局 Key 判真引用无 @ 前缀——与传感器沿同构，由验证器写源区分）
                        cond.IsStateAssert = false;
                        cond.IsBoxAssert = true;
                        cond.BoxName = t.Value;
                        i = i + 1;
                    }
                    else
                    {
                        cond.IsStateAssert = false;
                        cond.SensorName = t.Value;
                        i = i + 1;
                    }
                    def.Conditions.Add(cond);
                }
                else
                {
                    doc.Diagnostics.Add(new MauDiagnostic("E103", t.Line, "条件区非法 token——只允许 'P_X'（传感器沿）/ 'S_X' = 'Y'（状态断言）/ &（合取）"));
                    return;
                }
            }
        }

        /// <summary>
        /// 定位 →（结果箭头）——从 start 起扫描
        /// </summary>
        /// <param name="section">段 token</param>
        /// <param name="start">起点</param>
        /// <returns>箭头下标；无则 -1</returns>
        private static int FindArrow(List<TokenV3> section, int start)
        {
            for (int i = start; i < section.Count; i++)
            {
                if (section[i].Id == TokenIds.Arrow)
                {
                    return i;
                }
            }
            return -1;
        }

        /// <summary>
        /// 定位 ] 配对的参数闭合
        /// </summary>
        /// <param name="section">段 token</param>
        /// <param name="open">[ 下标</param>
        /// <returns>] 下标；无则 -1</returns>
        private static int FindParamClose(List<TokenV3> section, int open)
        {
            for (int i = open + 1; i < section.Count; i++)
            {
                if (section[i].Id == TokenIds.ParamClose)
                {
                    return i;
                }
            }
            return -1;
        }

        /// <summary>
        /// 采样周期解析——[N] 内容提取 N（Num token）
        /// </summary>
        /// <param name="section">段 token</param>
        /// <param name="start">内容起点</param>
        /// <param name="close">] 位置</param>
        /// <param name="frames">输出帧数</param>
        /// <returns>合法为真</returns>
        private static bool ParseFrames(List<TokenV3> section, int start, int close, out long frames)
        {
            frames = 0;
            for (int i = start; i < close; i++)
            {
                if (section[i].Id == TokenIds.Num)
                {
                    if (!long.TryParse(section[i].Value, out frames))
                    {
                        return false;
                    }
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// 动作/采样参数收集——[...] 内容原文（Str/Num/Word 序列化为文本）
        /// </summary>
        /// <param name="section">段 token</param>
        /// <param name="open">[ 下标</param>
        /// <returns>参数文本列表（逗号分隔切分）</returns>
        private static List<string> CollectArgs(List<TokenV3> section, int open)
        {
            List<string> args = new List<string>();
            int close = FindParamClose(section, open);
            if (close < 0)
            {
                return args;
            }
            string current = "";
            for (int i = open + 1; i < close; i++)
            {
                TokenV3 t = section[i];
                if (t.Id == TokenIds.Sep)
                {
                    if (current.Length > 0)
                    {
                        args.Add(current);
                        current = "";
                    }
                }
                else if (t.Id == TokenIds.Str)
                {
                    current = current + "\"" + t.Value + "\"";
                }
                else
                {
                    current = current + t.Value;
                }
            }
            if (current.Length > 0)
            {
                args.Add(current);
            }
            return args;
        }
    }
}
