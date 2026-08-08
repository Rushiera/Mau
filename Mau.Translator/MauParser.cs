using System;
using System.Collections.Generic;

namespace Mau.Translator
{
    /// <summary>
    /// 解析结果——文档 + 诊断
    /// </summary>
    public sealed class ParseResult
    {
        /// <summary>
        /// 解析出的文档——有错误时可能不完整
        /// </summary>
        public MauDocument Document;

        /// <summary>
        /// 语法诊断列表
        /// </summary>
        public List<MauDiagnostic> Diagnostics;

        /// <summary>
        /// 构造解析结果
        /// </summary>
        public ParseResult()
        {
            Document = new MauDocument();
            Diagnostics = new List<MauDiagnostic>();
        }
    }

    /// <summary>
    /// Mau 解析器——文本到 IR，逐行块扫描
    /// </summary>
    public static class MauParser
    {
        /// <summary>
        /// 解析 Mau 源文本
        /// </summary>
        /// <param name="sourceText">Mau 源文本</param>
        /// <returns>解析结果</returns>
        public static ParseResult Parse(string sourceText)
{
            ParseResult result = new ParseResult();
            MauDocument doc = result.Document;
            List<MauDiagnostic> diags = result.Diagnostics;

            string[] lines = sourceText.Split('\n');
            bool headerDone = false;
            bool inPropositionBlock = false;
            bool inExternalBlock = false;
            IrTransition? currentTransition = null;
            IrResource? currentResource = null;
            IrChannel? currentChannel = null;
            IrComposition? currentComposition = null;
            IrProposition? currentProposition = null;

            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Replace("\r", "");
                // 剥离行尾注释——引号外 // 起截断（调试消息内 // 保留）
                int commentPos = FindCommentStart(line);
                if (commentPos >= 0)
                {
                    line = line.Substring(0, commentPos);
                }
                string trimmed = line.Trim();
                int lineNo = i + 1;

                // [段1] 空行与注释跳过
                if (trimmed.Length == 0)
                {
                    continue;
                }
                if (trimmed.StartsWith("//"))
                {
                    continue;
                }

                // [段2] 文件头——首行 Mau <版本>
                if (!headerDone)
                {
                    if (trimmed.StartsWith("Mau "))
                    {
                        doc.Version = trimmed.Substring(4).Trim();
                        headerDone = true;
                        continue;
                    }
                    diags.Add(new MauDiagnostic("E101", lineNo, "缺少文件头版本声明（Mau <版本>）"));
                    headerDone = true;
                }

                // [段3] 基座声明
                if (trimmed.StartsWith("基座:"))
                {
                    doc.BaseName = trimmed.Substring(3).Trim();
                    continue;
                }

                // [段3b] 实现接口声明
                if (trimmed.StartsWith("实现:"))
                {
                    string val = trimmed.Substring(3).Trim();
                    string[] items = val.Split(',');
                    for (int k = 0; k < items.Length; k++)
                    {
                        string item = items[k].Trim();
                        if (item.Length > 0)
                        {
                            doc.Interfaces.Add(item);
                        }
                    }
                    continue;
                }

                // [段4] 命题块入口——无名字块（多命题单行格式）
                if (trimmed == "命题:")
                {
                    inPropositionBlock = true;
                    currentProposition = null;
                    currentTransition = null;
                    currentResource = null;
                    currentChannel = null;
                    currentComposition = null;
                    continue;
                }

                // [段4b] 命题块入口——带名字块（单命题字段格式）
                if (trimmed.StartsWith("命题 "))
                {
                    inPropositionBlock = false;
                    currentTransition = null;
                    currentResource = null;
                    currentChannel = null;
                    currentComposition = null;
                    string rest = trimmed.Substring(3).Trim();
                    string name = rest.EndsWith(":") ? rest.Substring(0, rest.Length - 1) : rest;
                    name = name.Trim();
                    if (name.Length == 0)
                    {
                        diags.Add(new MauDiagnostic("E105", lineNo, "命题名缺失"));
                        name = "P_Unknown";
                    }
                    if (!name.StartsWith("P_"))
                    {
                        diags.Add(new MauDiagnostic("E104", lineNo, "命题名必须以 P_ 前缀: " + name));
                    }
                    currentProposition = new IrProposition(name, PropositionKind.Condition, lineNo);
                    doc.Propositions.Add(currentProposition);
                    continue;
                }

                // [段5] 变迁块入口
                if (trimmed.StartsWith("变迁 "))
                {
                    inPropositionBlock = false;
                    currentProposition = null;
                    currentResource = null;
                    currentChannel = null;
                    currentComposition = null;
                    string rest = trimmed.Substring(3).Trim();
                    string name = rest.EndsWith(":") ? rest.Substring(0, rest.Length - 1) : rest;
                    name = name.Trim();
                    if (name.Length == 0)
                    {
                        diags.Add(new MauDiagnostic("E102", lineNo, "变迁名缺失"));
                        name = "T_Unknown";
                    }
                    currentTransition = new IrTransition(name, lineNo);
                    doc.Transitions.Add(currentTransition);
                    continue;
                }

                // [段5b] 资源块入口
                if (trimmed.StartsWith("资源 "))
                {
                    inPropositionBlock = false;
                    currentProposition = null;
                    currentTransition = null;
                    currentChannel = null;
                    currentComposition = null;
                    string rest = trimmed.Substring(3).Trim();
                    string name = rest.EndsWith(":") ? rest.Substring(0, rest.Length - 1) : rest;
                    name = name.Trim();
                    if (name.Length == 0)
                    {
                        diags.Add(new MauDiagnostic("E120", lineNo, "资源名缺失"));
                        name = "R_Unknown";
                    }
                    currentResource = new IrResource(name, lineNo);
                    doc.Resources.Add(currentResource);
                    continue;
                }

                // [段5c] 通道块入口
                if (trimmed.StartsWith("通道 "))
                {
                    inPropositionBlock = false;
                    currentProposition = null;
                    currentTransition = null;
                    currentResource = null;
                    currentComposition = null;
                    string rest = trimmed.Substring(3).Trim();
                    string name = rest.EndsWith(":") ? rest.Substring(0, rest.Length - 1) : rest;
                    name = name.Trim();
                    if (name.Length == 0)
                    {
                        diags.Add(new MauDiagnostic("E130", lineNo, "通道名缺失"));
                        name = "C_Unknown";
                    }
                    currentChannel = new IrChannel(name, lineNo);
                    doc.Channels.Add(currentChannel);
                    continue;
                }

                // [段5d] 组合块入口
                if (trimmed.StartsWith("组合 "))
                {
                    inPropositionBlock = false;
                    currentProposition = null;
                    currentTransition = null;
                    currentResource = null;
                    currentChannel = null;
                    string rest = trimmed.Substring(3).Trim();
                    string name = rest.EndsWith(":") ? rest.Substring(0, rest.Length - 1) : rest;
                    name = name.Trim();
                    if (name.Length == 0)
                    {
                        diags.Add(new MauDiagnostic("E140", lineNo, "组合名缺失"));
                        name = "FL_Unknown";
                    }
                    currentComposition = new IrComposition(name, lineNo);
                    doc.Compositions.Add(currentComposition);
                    continue;
                }

                // [段5e] 对外块入口——模块 OA 边界契约（接收/发送 Key 声明）
                if (trimmed == "对外:")
                {
                    inPropositionBlock = false;
                    currentProposition = null;
                    currentTransition = null;
                    currentResource = null;
                    currentChannel = null;
                    currentComposition = null;
                    inExternalBlock = true;
                    continue;
                }

                // [段6] 命题块内的命题行
                if (inPropositionBlock)
                {
                    ParsePropositionLine(trimmed, lineNo, doc, diags);
                    continue;
                }

                // [段6b] 单命题块内的字段行——类型/初始/重置
                if (currentProposition != null)
                {
                    if (trimmed.StartsWith("类型:"))
                    {
                        string typeWord = trimmed.Substring(3).Trim();
                        if (typeWord == "条件")
                        {
                            currentProposition.Kind = PropositionKind.Condition;
                        }
                        else if (typeWord == "信号")
                        {
                            currentProposition.Kind = PropositionKind.Signal;
                        }
                        else if (typeWord == "事实")
                        {
                            currentProposition.Kind = PropositionKind.Fact;
                        }
                        else
                        {
                            diags.Add(new MauDiagnostic("E150", lineNo, "命题类型非法——需要 条件/信号/事实: " + typeWord));
                        }
                    }
                    else if (trimmed.StartsWith("初始:"))
                    {
                        string initText = trimmed.Substring(3).Trim();
                        if (initText == "真")
                        {
                            currentProposition.Initial = true;
                        }
                        else if (initText == "假")
                        {
                            currentProposition.Initial = false;
                        }
                        else
                        {
                            diags.Add(new MauDiagnostic("E151", lineNo, "初始值非法——需要 真/假: " + initText));
                        }
                    }
                    else if (trimmed.StartsWith("重置:"))
                    {
                        currentProposition.Reset = trimmed.Substring(3).Trim();
                    }
                    else
                    {
                        diags.Add(new MauDiagnostic("E152", lineNo, "命题块内无法识别的字段: " + trimmed));
                    }
                    continue;
                }

                // [段7] 变迁块内的字段行
                if (currentTransition != null)
                {
                    ParseTransitionField(currentTransition, trimmed, lineNo, diags);
                    continue;
                }

                // [段7b] 资源块内的字段行
                if (currentResource != null)
                {
                    if (trimmed == "独占")
                    {
                        currentResource.Kind = "独占";
                    }
                    else if (trimmed.StartsWith("配额:"))
                    {
                        currentResource.Kind = "配额";
                        string quotaText = trimmed.Substring(3).Trim();
                        long quota;
                        if (long.TryParse(quotaText, out quota) && quota >= 0)
                        {
                            currentResource.Quota = quota;
                        }
                        else
                        {
                            diags.Add(new MauDiagnostic("E121", lineNo, "配额值非法——需要非负整数: " + quotaText));
                        }
                    }
                    else
                    {
                        diags.Add(new MauDiagnostic("E122", lineNo, "资源块内无法识别的字段: " + trimmed));
                    }
                    continue;
                }

                // [段7c] 通道块内的字段行
                if (currentChannel != null)
                {
                    if (trimmed.StartsWith("源:"))
                    {
                        currentChannel.Source = trimmed.Substring(2).Trim();
                    }
                    else if (trimmed.StartsWith("目标:"))
                    {
                        currentChannel.Target = trimmed.Substring(3).Trim();
                    }
                    else if (trimmed.StartsWith("类型:"))
                    {
                        currentChannel.ChannelType = trimmed.Substring(3).Trim();
                    }
                    else
                    {
                        diags.Add(new MauDiagnostic("E131", lineNo, "通道块内无法识别的字段: " + trimmed));
                    }
                    continue;
                }

                // [段7d] 组合块内的字段行
                if (currentComposition != null)
                {
                    if (trimmed.StartsWith("序列:"))
                    {
                        string val = trimmed.Substring(3).Trim();
                        string[] items = val.Split(',');
                        for (int s = 0; s < items.Length; s++)
                        {
                            string item = items[s].Trim();
                            if (item.Length > 0)
                            {
                                currentComposition.Sequence.Add(item);
                            }
                        }
                    }
                    else if (trimmed.StartsWith("并行:"))
                    {
                        string val = trimmed.Substring(3).Trim();
                        string[] items = val.Split(new char[] { ',', '‖' });
                        for (int p = 0; p < items.Length; p++)
                        {
                            string item = items[p].Trim();
                            if (item.Length > 0)
                            {
                                currentComposition.Parallel.Add(item);
                            }
                        }
                    }
                    else if (trimmed.StartsWith("选择:"))
                    {
                        currentComposition.Choice = trimmed.Substring(3).Trim();
                    }
                    else if (trimmed.StartsWith("重试:"))
                    {
                        string retryText = trimmed.Substring(3).Trim();
                        long retry;
                        if (long.TryParse(retryText, out retry) && retry >= 0)
                        {
                            currentComposition.Retry = retry;
                        }
                        else
                        {
                            diags.Add(new MauDiagnostic("E141", lineNo, "重试次数非法——需要非负整数: " + retryText));
                        }
                    }
                    else if (trimmed.StartsWith("汇合:"))
                    {
                        currentComposition.Merge = trimmed.Substring(3).Trim();
                    }
                    else
                    {
                        diags.Add(new MauDiagnostic("E142", lineNo, "组合块内无法识别的字段: " + trimmed));
                    }
                    continue;
                }

                // [段7e] 对外块内的字段行——接收: OAKey → 端口 / 发送: 端口 → OAKey
                if (inExternalBlock)
                {
                    if (trimmed.StartsWith("接收:"))
                    {
                        ParseExternalEntry("接收", trimmed.Substring(3).Trim(), lineNo, doc, diags);
                    }
                    else if (trimmed.StartsWith("发送:"))
                    {
                        ParseExternalEntry("发送", trimmed.Substring(3).Trim(), lineNo, doc, diags);
                    }
                    else
                    {
                        diags.Add(new MauDiagnostic("E150", lineNo, "对外块内无法识别的字段: " + trimmed));
                    }
                    continue;
                }

                // [段8] 无法识别
                diags.Add(new MauDiagnostic("E103", lineNo, "无法识别的行: " + trimmed));
            }

            return result;
        }

        /// <summary>
        /// 解析对外条目——接收: OAKey → 端口；发送: 端口 → OAKey
        /// </summary>
        /// <param name="direction">方向——接收/发送</param>
        /// <param name="value">条目文本</param>
        /// <param name="lineNo">行号</param>
        /// <param name="doc">文档</param>
        /// <param name="diags">诊断列表</param>
        private static void ParseExternalEntry(string direction, string value,
            int lineNo, MauDocument doc, List<MauDiagnostic> diags)
        {
            int arrow = value.IndexOf("→");
            if (arrow < 0)
            {
                diags.Add(new MauDiagnostic("E151", lineNo, "对外条目缺少 → 分隔——接收: OAKey → 端口 / 发送: 端口 → OAKey"));
                return;
            }
            string left = value.Substring(0, arrow).Trim();
            string right = value.Substring(arrow + 1).Trim();
            if (left.Length == 0 || right.Length == 0)
            {
                diags.Add(new MauDiagnostic("E151", lineNo, "对外条目两侧不能为空"));
                return;
            }
            if (direction == "接收")
            {
                // 接收: OAKey → 模块端口
                doc.Externals.Add(new IrExternal("接收", left, right, lineNo));
            }
            else
            {
                // 发送: 模块端口 → OAKey
                doc.Externals.Add(new IrExternal("发送", right, left, lineNo));
            }
        }

        /// <summary>
        /// 找行尾注释起点——引号外第一个 //；双引号内的 // 视为内容保留
        /// </summary>
/// <param name = "line">原始行文本</param>
/// <returns>注释起点索引，无注释返回 -1</returns>
private static int FindCommentStart(string line)
{
    bool inQuote = false;
    for (int i = 0; i < line.Length - 1; i = i + 1)
    {
        char c = line[i];
        if (c == '"')
        {
            inQuote = !inQuote;
            continue;
        }

        if (!inQuote && c == '/' && line[i + 1] == '/')
        {
            return i;
        }
    }

    return -1;
}        /// <summary>
        /// 解析命题行——P_Name 类型
        /// </summary>
        /// <param name="line">去缩进后的行</param>
        /// <param name="lineNo">行号</param>
        /// <param name="doc">文档</param>
        /// <param name="diags">诊断列表</param>
        private static void ParsePropositionLine(string line, int lineNo, MauDocument doc, List<MauDiagnostic> diags)
        {
            List<string> parts = SplitWords(line);
            if (parts.Count == 0)
            {
                return;
            }
            string name = parts[0];
            if (!name.StartsWith("P_"))
            {
                diags.Add(new MauDiagnostic("E104", lineNo, "命题名必须以 P_ 前缀: " + name));
                return;
            }

            PropositionKind kind = PropositionKind.Condition;
            if (parts.Count >= 2)
            {
                string typeWord = parts[1];
                if (typeWord == "条件")
                {
                    kind = PropositionKind.Condition;
                }
                else if (typeWord == "信号")
                {
                    kind = PropositionKind.Signal;
                }
                else if (typeWord == "事实")
                {
                    kind = PropositionKind.Fact;
                }
                else
                {
                    diags.Add(new MauDiagnostic("E105", lineNo, "未知命题类型: " + typeWord + "（条件/信号/事实）"));
                    return;
                }
            }

            IrProposition prop = new IrProposition(name, kind, lineNo);
            if (kind == PropositionKind.Fact && prop.Initial)
            {
                diags.Add(new MauDiagnostic("E106", lineNo, "事实不允许初始真（单调）: " + name));
            }
            doc.Propositions.Add(prop);
        }

        /// <summary>
        /// 解析变迁字段行——字段: 值
        /// </summary>
        /// <param name="t">目标变迁</param>
        /// <param name="line">去缩进后的行</param>
        /// <param name="lineNo">行号</param>
        /// <param name="diags">诊断列表</param>
        private static void ParseTransitionField(IrTransition t, string line, int lineNo, List<MauDiagnostic> diags)
        {
            int colon = line.IndexOf(':');
            if (colon < 0)
            {
                diags.Add(new MauDiagnostic("E107", lineNo, "字段格式错误——需要 字段: 值: " + line));
                return;
            }
            string key = line.Substring(0, colon).Trim();
            string value = line.Substring(colon + 1).Trim();

            if (key == "前置")
            {
                t.Preconditions = SplitConjunction(value);
                return;
            }
            if (key == "动作")
            {
                t.BrickName = value;
                return;
            }
            if (key == "参数")
            {
                ParseParams(value, t, lineNo, diags);
                return;
            }
            if (key == "时限")
            {
                ParseTimeout(value, t, lineNo, diags);
                return;
            }
            if (key == "后置")
            {
                ParsePost(value, t);
                return;
            }
            if (key == "线程")
            {
                t.Thread = value;
                return;
            }
            if (key == "帧")
            {
                t.FrameSpec = value;
                return;
            }
            if (key == "汇合")
            {
                t.Join = value;
                return;
            }
            if (key == "调试")
            {
                string msg = value;
                if (msg.StartsWith("\"") && msg.EndsWith("\""))
                {
                    msg = msg.Substring(1, msg.Length - 2);
                }
                t.DebugMessage = msg;
                return;
            }

            diags.Add(new MauDiagnostic("E108", lineNo, "未知变迁字段: " + key));
        }

        /// <summary>
        /// 按 ∧ 分割前置命题名列表
        /// </summary>
        /// <param name="value">前置声明文本</param>
        /// <returns>命题名列表</returns>
        private static List<string> SplitConjunction(string value)
        {
            List<string> names = new List<string>();
            string[] parts = value.Split('∧');
            for (int i = 0; i < parts.Length; i++)
            {
                string name = parts[i].Trim();
                if (name.Length > 0)
                {
                    names.Add(name);
                }
            }
            return names;
        }

        /// <summary>
        /// 解析参数绑定——两种格式：端口名列表（变量=端口名）或 变量 → 端口，逗号分隔
        /// </summary>
        /// <param name="value">参数声明文本</param>
        /// <param name="t">目标变迁</param>
        /// <param name="lineNo">行号</param>
        /// <param name="diags">诊断列表</param>
        private static void ParseParams(string value, IrTransition t, int lineNo, List<MauDiagnostic> diags)
{
            // 深度感知分割——方括号内逗号属于数组字面量（B2），不分割
            List<string> items = SplitParamBindings(value);
            for (int i = 0; i < items.Count; i++)
            {
                string item = items[i].Trim();
                if (item.Length == 0)
                {
                    continue;
                }
                int arrow = item.IndexOf("→");
                if (arrow < 0)
                {
                    // 简写格式：端口名即变量名（外部注入）
                    t.Params.Add(new IrParamBinding(item, item, false));
                    continue;
                }
                string variable = item.Substring(0, arrow).Trim();
                string port = item.Substring(arrow + 1).Trim();
                IrParamBinding binding = new IrParamBinding(variable, port, true);
                // 数组字面量绑定——[a,b,c]（B2：语料可构造数组端口）
                if (variable.Length >= 2 && variable.StartsWith("[") && variable.EndsWith("]"))
                {
                    binding.IsArray = true;
                    string inner = variable.Substring(1, variable.Length - 2);
                    string[] elems = inner.Split(',');
                    for (int e = 0; e < elems.Length; e++)
                    {
                        string elem = elems[e].Trim();
                        if (elem.Length > 0)
                        {
                            binding.ArrayItems.Add(elem);
                        }
                    }
                }
                // 常量字面量绑定——字符串（"..."）或数字字面量（0/-1/300 等）
                else if (variable.Length >= 2 && variable.StartsWith("\"") && variable.EndsWith("\""))
                {
                    binding.IsConstant = true;
                    binding.ConstantValue = variable.Substring(1, variable.Length - 2);
                }
                else
                {
                    long number;
                    if (long.TryParse(variable, out number))
                    {
                        binding.IsConstant = true;
                        binding.ConstantValue = variable;
                    }
                }
                t.Params.Add(binding);
            }
        }
        /// <summary>
        /// 解析时限——N帧/空闲N帧/无
        /// </summary>
        /// <param name="value">时限声明文本</param>
        /// <param name="t">目标变迁</param>
        /// <param name="lineNo">行号</param>
        /// <param name="diags">诊断列表</param>
        private static void ParseTimeout(string value, IrTransition t, int lineNo, List<MauDiagnostic> diags)
        {
            if (value == "无")
            {
                t.HasTimeout = true;
                t.TimeoutMode = "None";
                t.TimeoutFrames = 0;
                return;
            }
            if (value.EndsWith("帧"))
            {
                string body = value.Substring(0, value.Length - 1);
                if (body.StartsWith("空闲"))
                {
                    string framesText = body.Substring(2);
                    long frames;
                    if (long.TryParse(framesText, out frames) && frames > 0)
                    {
                        t.HasTimeout = true;
                        t.TimeoutMode = "Idle";
                        t.TimeoutFrames = frames;
                    }
                    else
                    {
                        diags.Add(new MauDiagnostic("E110", lineNo, "空闲时限帧数非法: " + value));
                    }
                    return;
                }
                long totalFrames;
                if (long.TryParse(body, out totalFrames) && totalFrames > 0)
                {
                    t.HasTimeout = true;
                    t.TimeoutMode = "Total";
                    t.TimeoutFrames = totalFrames;
                }
                else
                {
                    diags.Add(new MauDiagnostic("E110", lineNo, "时限帧数非法: " + value));
                }
                return;
            }
            diags.Add(new MauDiagnostic("E110", lineNo, "时限格式非法——需要 N帧/空闲N帧/无: " + value));
        }

        /// <summary>
        /// 解析后置——A / B 互斥，首个为正常后置，其余为错误后置
        /// </summary>
        /// <param name="value">后置声明文本</param>
        /// <param name="t">目标变迁</param>
        private static void ParsePost(string value, IrTransition t)
        {
            string[] parts = value.Split('/');
            for (int i = 0; i < parts.Length; i++)
            {
                string name = parts[i].Trim();
                if (name.Length == 0)
                {
                    continue;
                }
                if (t.PostOk.Count == 0)
                {
                    t.PostOk.Add(name);
                }
                else
                {
                    t.PostError.Add(name);
                }
            }
        }

        /// <summary>
        /// 按空白分割单词
        /// </summary>
        /// <param name="line">输入行</param>
        /// <returns>非空单词列表</returns>
        private static List<string> SplitWords(string line)
        {
            List<string> words = new List<string>();
            string[] raw = line.Split(' ', '\t');
            for (int i = 0; i < raw.Length; i++)
            {
                string word = raw[i].Trim();
                if (word.Length > 0)
                {
                    words.Add(word);
                }
            }
            return words;
        }
/// <summary>
/// 深度感知分割参数绑定——方括号内逗号不分割（数组字面量 [a,b,c]，B2）
/// </summary>
/// <param name = "value">参数声明文本</param>
/// <returns>绑定片段列表</returns>
private static List<string> SplitParamBindings(string value)
{
    List<string> result = new List<string>();
    System.Text.StringBuilder current = new System.Text.StringBuilder();
    int depth = 0;
    for (int i = 0; i < value.Length; i++)
    {
        char c = value[i];
        if (c == '[')
        {
            depth = depth + 1;
        }
        else if (c == ']')
        {
            depth = depth - 1;
        }

        if (c == ',' && depth == 0)
        {
            result.Add(current.ToString());
            current.Clear();
            continue;
        }

        current.Append(c);
    }

    result.Add(current.ToString());
    return result;
}    }
}
