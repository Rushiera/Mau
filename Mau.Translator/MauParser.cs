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
            IrTransition? currentTransition = null;
            IrResource? currentResource = null;
            IrChannel? currentChannel = null;
            IrComposition? currentComposition = null;

            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Replace("\r", "");
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

                // [段4] 命题块入口
                if (trimmed == "命题:")
                {
                    inPropositionBlock = true;
                    currentTransition = null;
                    currentResource = null;
                    currentChannel = null;
                    currentComposition = null;
                    continue;
                }

                // [段5] 变迁块入口
                if (trimmed.StartsWith("变迁 "))
                {
                    inPropositionBlock = false;
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

                // [段6] 命题块内的命题行
                if (inPropositionBlock)
                {
                    ParsePropositionLine(trimmed, lineNo, doc, diags);
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
                        string[] items = val.Split(',');
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

                // [段8] 无法识别
                diags.Add(new MauDiagnostic("E103", lineNo, "无法识别的行: " + trimmed));
            }

            return result;
        }
        /// <summary>
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
            string[] items = value.Split(',');
            for (int i = 0; i < items.Length; i++)
            {
                string item = items[i].Trim();
                if (item.Length == 0)
                {
                    continue;
                }
                int arrow = item.IndexOf("→");
                if (arrow < 0)
                {
                    // 简写格式：端口名即变量名
                    t.Params.Add(new IrParamBinding(item, item));
                    continue;
                }
                string variable = item.Substring(0, arrow).Trim();
                string port = item.Substring(arrow + 1).Trim();
                t.Params.Add(new IrParamBinding(variable, port));
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
    }
}
