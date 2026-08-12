using System;
using System.Collections.Generic;
using System.Text;

namespace Mau.Translator
{
    /// <summary>
    /// 段类型——词法第 7 步段级结构判定结果
    /// </summary>
    public enum MauSectionKindV2
    {
        /// <summary>未识别</summary>
        Unknown,
        /// <summary>版本声明——'Mau' 2.0</summary>
        Version,
        /// <summary>实现接口——@'...'</summary>
        Interface,
        /// <summary>注入字段——$'...'</summary>
        Inject,
        /// <summary>状态机集合定义——'S_X' = { ... }</summary>
        MachineDef,
        /// <summary>状态机嵌套绑定——'S_X' ∈ 'S_Y.Z'</summary>
        MachineBind,
        /// <summary>命题声明——'P_A', 'P_B'</summary>
        Propositions,
        /// <summary>资源声明——'R_X': 1</summary>
        Resource,
        /// <summary>系统边界输入——⇐ ...</summary>
        BoundaryIn,
        /// <summary>系统边界输出——⇒ ...</summary>
        BoundaryOut,
        /// <summary>测量——'M_X'[ω=1]: 'P_Y' := 'brick'[...]</summary>
        Measure,
        /// <summary>控制律——'T_X'[τ=10]: ... → ...</summary>
        Law,
        /// <summary>语法糖——'SEQ'[...]</summary>
        Sugar,
    }

    /// <summary>
    /// 参数项种类——[...] 容器内元素
    /// </summary>
    public enum MauParamKindV2
    {
        /// <summary>引用——变量/积木参数名</summary>
        Ref,
        /// <summary>数值</summary>
        Number,
        /// <summary>字符串（数据——双引号保护还原）</summary>
        String,
        /// <summary>数组字面量——嵌套列表</summary>
        Array,
        /// <summary>属性：时限 τ=</summary>
        AttrTau,
        /// <summary>属性：帧/采样周期 ω=</summary>
        AttrOmega,
        /// <summary>属性：worker 线程 ∥</summary>
        AttrParallel,
        /// <summary>属性：inbox 汇合 ⋈</summary>
        AttrJoin,
        /// <summary>属性：日志标记 !</summary>
        AttrLog,
    }

    /// <summary>
    /// 参数项——[...] 容器内元素（数组递归）
    /// </summary>
    public sealed class MauParamV2
    {
        /// <summary>种类</summary>
        public MauParamKindV2 Kind;

        /// <summary>文本——Ref 名/Number 原文/String 内容；Array 为空</summary>
        public string Text = "";

        /// <summary>数组元素——Kind=Array 时非空</summary>
        public List<MauParamV2>? Items;
    }

    /// <summary>
    /// 词法段——§ 分段产物（占位符形态）
    /// </summary>
    public sealed class MauSectionV2
    {
        /// <summary>段号（1-based——错误定位）</summary>
        public int Index;

        /// <summary>段类型——第 7 步判定</summary>
        public MauSectionKindV2 Kind = MauSectionKindV2.Unknown;

        /// <summary>段文本——占位符形态（无空白/注释/字符串原文）</summary>
        public string Text = "";
    }

    /// <summary>
    /// 词法结果——段列表 + 全局命名表/参数表
    /// </summary>
    public sealed class LexResultV2
    {
        /// <summary>
        /// 是否成功——无错误诊断（构造默认成功，末尾按诊断修正）
        /// </summary>
        public bool Success;

        /// <summary>
        /// 诊断列表（E0xx 词法）
        /// </summary>
        public List<MauDiagnostic> Diagnostics = new List<MauDiagnostic>();

        /// <summary>
        /// 段列表——空段/注释段跳过
        /// </summary>
        public List<MauSectionV2> Sections = new List<MauSectionV2>();

        /// <summary>
        /// 命名表——'...' 提取（N{n} 索引，首次出现顺序）
        /// </summary>
        public List<string> Names = new List<string>();

        /// <summary>
        /// 参数表——[...] 容器（A{n} 索引，首次出现顺序）
        /// </summary>
        public List<MauParamV2> Params = new List<MauParamV2>();

        /// <summary>
        /// 构造词法结果
        /// </summary>
        public LexResultV2()
        {
            Success = true;
        }
    }

    /// <summary>
    /// 词法层——七步流水线：字符串保护 → 空白剔除 → § 分段+注释 → 命名提取 → 参数提取 → 符号校验 → 段分类
    /// </summary>
    public static class MauLexerV2
    {
        /// <summary>
        /// 符号集——词法定界 + 数学关系 + 属性键（不含 ' " § 与数字——单独处理）
        /// </summary>
        private const string SymbolChars = ",=:{}@$→|∧∨+∈⇐⇒τ∥⋈!";

        /// <summary>
        /// 词法分析入口——七步流水线
        /// </summary>
        /// <param name="sourceText">.mau 源文本</param>
        /// <returns>词法结果</returns>
        public static LexResultV2 Lex(string sourceText)
        {
            LexResultV2 result = new LexResultV2();
            if (sourceText == null)
            {
                sourceText = "";
            }

            // [段1] 字符串保护——"..." 整体替换为 "S{n}"，内容入字符串表（原样保留含 §/换行）
            List<string> strings = new List<string>();
            string step1 = ProtectStrings(sourceText, strings, result);
            if (result.Diagnostics.Count > 0)
            {
                return result;
            }

            // [段2] 空白剔除——物理换行（\n \r \u2028 \u2029）与空格/tab
            string step2 = StripWhitespace(step1);

            // [段3] § 分段 + 段内注释（// 到段尾）丢弃 + 空段跳过
            List<string> rawSections = SplitSections(step2);

            // [段4-7] 逐段：命名提取 → 参数提取 → 符号校验 → 段分类
            for (int i = 0; i < rawSections.Count; i++)
            {
                string raw = rawSections[i];
                if (raw.Length == 0)
                {
                    continue;
                }

                // [段4] 参数容器提取——[...] 整体 → A{n}（嵌套数组深度计数；内部 ' " 原样保留待解析）
                List<string> paramRaws = new List<string>();
                string noParams = ExtractParamContainers(raw, paramRaws, result, i + 1);
                if (result.Diagnostics.Count > 0)
                {
                    return result;
                }

                // [段5] 命名提取——裸 '...'（容器外）→ N{n}，白名单 [A-Za-z_.]+
                string noNames = ExtractNames(noParams, result, i + 1);
                if (result.Diagnostics.Count > 0)
                {
                    return result;
                }

                // [段6] 参数容器内部解析——逐容器：逗号分隔元素 → MauParamV2（引用/数值/字符串/数组/属性）
                for (int p = 0; p < paramRaws.Count; p++)
                {
                    MauParamV2 container = ParseParamContainer(paramRaws[p], strings, result, i + 1);
                    if (result.Diagnostics.Count > 0)
                    {
                        return result;
                    }
                    result.Params.Add(container);
                }

                // [段7a] 符号校验——剩余符号流逐字符 ∈ 符号集 + 数值 token + 占位符
                string symbolText = ValidateSymbols(noNames, result, i + 1);
                if (result.Diagnostics.Count > 0)
                {
                    return result;
                }

                // [段7b] 段分类——纯符号串结构判定（语法糖判定需要容器内容——→/∥ 在参数容器内）
                MauSectionV2 section = new MauSectionV2();
                section.Index = i + 1;
                section.Text = symbolText;
                section.Kind = ClassifySection(symbolText, paramRaws);
                result.Sections.Add(section);
            }

            result.Success = result.Diagnostics.Count == 0;
            return result;
        }

        /// <summary>
        /// 字符串保护——"..." 提取为 "S{n}"（内容入字符串表，原样保留）
        /// </summary>
        /// <param name="text">源文本</param>
        /// <param name="strings">字符串表——输出</param>
        /// <param name="result">词法结果（诊断收集）</param>
        /// <returns>保护后文本</returns>
        private static string ProtectStrings(string text, List<string> strings, LexResultV2 result)
        {
            StringBuilder sb = new StringBuilder();
            int i = 0;
            while (i < text.Length)
            {
                char c = text[i];
                if (c == '"')
                {
                    int close = text.IndexOf('"', i + 1);
                    if (close < 0)
                    {
                        result.Diagnostics.Add(new MauDiagnostic("E004", 0, "字符串未闭合——缺少收尾双引号"));
                        result.Success = false;
                        return "";
                    }
                    string content = text.Substring(i + 1, close - i - 1);
                    strings.Add(content);
                    sb.Append("\"S{");
                    sb.Append((strings.Count - 1).ToString());
                    sb.Append("}\"");
                    i = close + 1;
                    continue;
                }
                sb.Append(c);
                i = i + 1;
            }
            return sb.ToString();
        }

        /// <summary>
        /// 空白剔除——物理换行（\n \r \u2028 \u2029）与空格/tab/缩进
        /// </summary>
        /// <param name="text">保护后文本</param>
        /// <returns>无空白文本</returns>
        private static string StripWhitespace(string text)
        {
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c == '\n' || c == '\r' || c == '\u2028' || c == '\u2029' || c == ' ' || c == '\t')
                {
                    continue;
                }
                sb.Append(c);
            }
            return sb.ToString();
        }

        /// <summary>
        /// § 分段 + 段内注释丢弃——段 = § 开头到下一 § 前；// 到段尾 = 注释；空段跳过
        /// </summary>
        /// <param name="text">无空白文本</param>
        /// <returns>段列表（注释/空段已剔除）</returns>
        private static List<string> SplitSections(string text)
        {
            List<string> sections = new List<string>();
            int start = 0;
            while (start < text.Length)
            {
                int next = text.IndexOf('§', start);
                string section;
                if (next < 0)
                {
                    section = text.Substring(start);
                    start = text.Length;
                }
                else
                {
                    section = text.Substring(start, next - start);
                    start = next + 1;
                }

                // 段内注释——// 到段尾丢弃
                int comment = section.IndexOf("//", StringComparison.Ordinal);
                if (comment >= 0)
                {
                    section = section.Substring(0, comment);
                }

                if (section.Length > 0)
                {
                    sections.Add(section);
                }
            }
            return sections;
        }

        /// <summary>
        /// 参数容器提取——[...] 整体 → A{n}（嵌套数组深度计数；容器原始内容入 paramRaws）
        /// </summary>
        /// <param name="text">段文本（含字符串占位）</param>
        /// <param name="paramRaws">容器原始内容——输出（按编号顺序）</param>
        /// <param name="result">词法结果</param>
        /// <param name="sectionNo">段号</param>
        /// <returns>提取后文本（A 占位符形态）</returns>
        private static string ExtractParamContainers(string text, List<string> paramRaws, LexResultV2 result, int sectionNo)
        {
            StringBuilder sb = new StringBuilder();
            int i = 0;
            while (i < text.Length)
            {
                char c = text[i];
                if (c == '[')
                {
                    // 深度计数找匹配 ]（嵌套数组）
                    int depth = 1;
                    int j = i + 1;
                    while (j < text.Length && depth > 0)
                    {
                        if (text[j] == '[')
                        {
                            depth = depth + 1;
                        }
                        else if (text[j] == ']')
                        {
                            depth = depth - 1;
                        }
                        j = j + 1;
                    }
                    if (depth > 0)
                    {
                        result.Diagnostics.Add(new MauDiagnostic("E002", sectionNo, "参数容器未闭合——缺少 ]"));
                        result.Success = false;
                        return "";
                    }
                    string raw = text.Substring(i + 1, j - i - 2);
                    paramRaws.Add(raw);
                    // 全局编号——result.Params 已有数量 + 段内已收集数量
                    sb.Append("A{");
                    sb.Append((result.Params.Count + paramRaws.Count - 1).ToString());
                    sb.Append("}");
                    i = j;
                    continue;
                }
                sb.Append(c);
                i = i + 1;
            }
            return sb.ToString();
        }

        /// <summary>
        /// 命名提取——裸 '...'（容器外）→ N{n}，白名单 [A-Za-z_.]+
        /// </summary>
        /// <param name="text">容器提取后文本</param>
        /// <param name="result">词法结果</param>
        /// <param name="sectionNo">段号</param>
        /// <returns>提取后文本（N 占位符形态）</returns>
        private static string ExtractNames(string text, LexResultV2 result, int sectionNo)
        {
            StringBuilder sb = new StringBuilder();
            int i = 0;
            while (i < text.Length)
            {
                char c = text[i];
                if (c == '\'')
                {
                    int close = text.IndexOf('\'', i + 1);
                    if (close < 0)
                    {
                        result.Diagnostics.Add(new MauDiagnostic("E001", sectionNo, "命名未闭合——缺少收尾单引号"));
                        result.Success = false;
                        return "";
                    }
                    string name = text.Substring(i + 1, close - i - 1);
                    if (!IsValidName(name))
                    {
                        result.Diagnostics.Add(new MauDiagnostic("E001", sectionNo, "命名白名单违规——'" + name + "' 只允许 [A-Za-z0-9_.]+"));
                        result.Success = false;
                        return "";
                    }
                    // 同名复用索引——首次出现顺序编号（设计稿 2.2）
                    int nameIndex = result.Names.IndexOf(name);
                    if (nameIndex < 0)
                    {
                        result.Names.Add(name);
                        nameIndex = result.Names.Count - 1;
                    }
                    sb.Append("N{");
                    sb.Append(nameIndex.ToString());
                    sb.Append("}");
                    i = close + 1;
                    continue;
                }
                sb.Append(c);
                i = i + 1;
            }
            return sb.ToString();
        }

        /// <summary>
        /// 命名白名单校验——[A-Za-z0-9_.]+（数字允许——工程接口名如 CH4.Contracts.ICat；中文/符号拒绝）
        /// </summary>
        /// <param name="name">命名</param>
        /// <returns>合法为真</returns>
        private static bool IsValidName(string name)
        {
            if (name.Length == 0)
            {
                return false;
            }
            for (int i = 0; i < name.Length; i++)
            {
                char c = name[i];
                bool ok = (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || c == '_' || c == '.' || (c >= '0' && c <= '9');
                if (!ok)
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// 参数容器内部解析——逗号分隔元素（深度 0）→ MauParamV2
        /// </summary>
        /// <param name="raw">容器原始内容</param>
        /// <param name="strings">字符串表</param>
        /// <param name="result">词法结果</param>
        /// <param name="sectionNo">段号</param>
        /// <returns>容器参数项</returns>
        private static MauParamV2 ParseParamContainer(string raw, List<string> strings, LexResultV2 result, int sectionNo)
        {
            // 语法糖容器——含 '（命名引用）且含 →（SEQ/FBK）或 ∥（PAR）：按糖分隔符拆分为引用序列
            // 纯属性容器（[τ=5, ∥] 等）不含 '——不走糖分支
            if (raw.Contains("'", StringComparison.Ordinal)
                && (raw.Contains("→", StringComparison.Ordinal) || raw.Contains("∥", StringComparison.Ordinal)))
            {
                char sep = raw.Contains("→", StringComparison.Ordinal) ? '→' : '∥';
                string[] parts = raw.Split(sep);
                MauParamV2 sugar = new MauParamV2();
                sugar.Kind = MauParamKindV2.Array;
                sugar.Items = new List<MauParamV2>();
                for (int i = 0; i < parts.Length; i++)
                {
                    string part = parts[i].Trim();
                    if (part.Length == 0)
                    {
                        continue;
                    }
                    MauParamV2 item = ParseParamElement(part, strings, result, sectionNo);
                    if (!result.Success)
                    {
                        return new MauParamV2();
                    }
                    sugar.Items.Add(item);
                }
                return sugar;
            }

            // 普通容器——逗号分隔元素（深度 0）
            List<string> elements = SplitElements(raw, result, sectionNo);
            if (!result.Success)
            {
                return new MauParamV2();
            }

            MauParamV2 container = new MauParamV2();
            container.Kind = MauParamKindV2.Array;
            container.Items = new List<MauParamV2>();
            for (int i = 0; i < elements.Count; i++)
            {
                container.Items.Add(ParseParamElement(elements[i], strings, result, sectionNo));
                if (!result.Success)
                {
                    return new MauParamV2();
                }
            }
            return container;
        }

        /// <summary>
        /// 逗号分隔元素——深度 0 拆分（嵌套 [ ] 内的逗号不拆）
        /// </summary>
        /// <param name="raw">容器内容</param>
        /// <param name="result">词法结果</param>
        /// <param name="sectionNo">段号</param>
        /// <returns>元素列表</returns>
        private static List<string> SplitElements(string raw, LexResultV2 result, int sectionNo)
        {
            List<string> elements = new List<string>();
            if (raw.Length == 0)
            {
                return elements;
            }
            int depth = 0;
            int start = 0;
            for (int i = 0; i < raw.Length; i++)
            {
                char c = raw[i];
                if (c == '[')
                {
                    depth = depth + 1;
                }
                else if (c == ']')
                {
                    depth = depth - 1;
                }
                else if (c == ',' && depth == 0)
                {
                    elements.Add(raw.Substring(start, i - start));
                    start = i + 1;
                }
            }
            elements.Add(raw.Substring(start));
            return elements;
        }

        /// <summary>
        /// 参数元素解析——引用/数值/字符串/数组/属性（按形态机械区分）
        /// </summary>
        /// <param name="element">元素原文</param>
        /// <param name="strings">字符串表</param>
        /// <param name="result">词法结果</param>
        /// <param name="sectionNo">段号</param>
        /// <returns>参数项</returns>
        private static MauParamV2 ParseParamElement(string element, List<string> strings, LexResultV2 result, int sectionNo)
        {
            MauParamV2 item = new MauParamV2();
            if (element.Length == 0)
            {
                result.Diagnostics.Add(new MauDiagnostic("E002", sectionNo, "参数元素为空——逗号后缺少内容"));
                result.Success = false;
                return item;
            }

            char first = element[0];
            if (first == '\'')
            {
                // 引用——白名单 [A-Za-z_0-9.]+
                if (element.Length < 2 || element[element.Length - 1] != '\'')
                {
                    result.Diagnostics.Add(new MauDiagnostic("E002", sectionNo, "参数引用未闭合——'" + element));
                    result.Success = false;
                    return item;
                }
                string refName = element.Substring(1, element.Length - 2);
                if (!IsValidRef(refName))
                {
                    result.Diagnostics.Add(new MauDiagnostic("E002", sectionNo, "参数引用白名单违规——'" + refName + "' 只允许 [A-Za-z_0-9.]+"));
                    result.Success = false;
                    return item;
                }
                item.Kind = MauParamKindV2.Ref;
                item.Text = refName;
                return item;
            }

            if (first == '"')
            {
                // 字符串——"S{n}" 占位还原
                if (element.Length < 5 || element[1] != 'S' || element[2] != '{' || element[element.Length - 2] != '}')
                {
                    result.Diagnostics.Add(new MauDiagnostic("E002", sectionNo, "字符串参数格式错误——" + element));
                    result.Success = false;
                    return item;
                }
                string idxText = element.Substring(3, element.Length - 5);
                int idx = ParseIndex(idxText);
                if (idx < 0 || idx >= strings.Count)
                {
                    result.Diagnostics.Add(new MauDiagnostic("E002", sectionNo, "字符串表索引越界——" + element));
                    result.Success = false;
                    return item;
                }
                item.Kind = MauParamKindV2.String;
                item.Text = strings[idx];
                return item;
            }

            if (first == '[')
            {
                // 数组——递归解析（元素再拆分）
                if (element[element.Length - 1] != ']')
                {
                    result.Diagnostics.Add(new MauDiagnostic("E002", sectionNo, "数组参数未闭合——缺少 ]"));
                    result.Success = false;
                    return item;
                }
                string inner = element.Substring(1, element.Length - 2);
                List<string> subElements = SplitElements(inner, result, sectionNo);
                if (!result.Success)
                {
                    return item;
                }
                item.Kind = MauParamKindV2.Array;
                item.Items = new List<MauParamV2>();
                for (int i = 0; i < subElements.Count; i++)
                {
                    item.Items.Add(ParseParamElement(subElements[i], strings, result, sectionNo));
                    if (!result.Success)
                    {
                        return item;
                    }
                }
                return item;
            }

            if (element.StartsWith("τ=", StringComparison.Ordinal))
            {
                item.Kind = MauParamKindV2.AttrTau;
                item.Text = element.Substring(2);
                return item;
            }

            if (element.StartsWith("ω=", StringComparison.Ordinal))
            {
                item.Kind = MauParamKindV2.AttrOmega;
                item.Text = element.Substring(2);
                return item;
            }

            if (element == "∥")
            {
                item.Kind = MauParamKindV2.AttrParallel;
                return item;
            }

            if (element == "⋈")
            {
                item.Kind = MauParamKindV2.AttrJoin;
                return item;
            }

            if (element == "!")
            {
                item.Kind = MauParamKindV2.AttrLog;
                return item;
            }

            // 数值
            if (IsNumber(element))
            {
                item.Kind = MauParamKindV2.Number;
                item.Text = element;
                return item;
            }

            result.Diagnostics.Add(new MauDiagnostic("E002", sectionNo, "参数元素无法识别——" + element));
            result.Success = false;
            return item;
        }

        /// <summary>
        /// 参数引用白名单校验——[A-Za-z_0-9.]+
        /// </summary>
        /// <param name="refName">引用名</param>
        /// <returns>合法为真</returns>
        private static bool IsValidRef(string refName)
        {
            if (refName.Length == 0)
            {
                return false;
            }
            for (int i = 0; i < refName.Length; i++)
            {
                char c = refName[i];
                bool ok = (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || c == '_' || c == '.' || (c >= '0' && c <= '9');
                if (!ok)
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// 数值 token 校验——[0-9]+(\.[0-9]+)?
        /// </summary>
        /// <param name="text">文本</param>
        /// <returns>合法为真</returns>
        private static bool IsNumber(string text)
        {
            if (text.Length == 0)
            {
                return false;
            }
            int dotCount = 0;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c >= '0' && c <= '9')
                {
                    continue;
                }
                if (c == '.')
                {
                    dotCount = dotCount + 1;
                    if (dotCount > 1)
                    {
                        return false;
                    }
                    // 点不在首位/末位
                    if (i == 0 || i == text.Length - 1)
                    {
                        return false;
                    }
                    continue;
                }
                return false;
            }
            return true;
        }

        /// <summary>
        /// 索引解析——纯数字字符串
        /// </summary>
        /// <param name="text">数字文本</param>
        /// <returns>索引值（非法为 -1）</returns>
        private static int ParseIndex(string text)
        {
            if (text.Length == 0)
            {
                return -1;
            }
            int value = 0;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c < '0' || c > '9')
                {
                    return -1;
                }
                value = value * 10 + (c - '0');
            }
            return value;
        }

        /// <summary>
        /// 符号校验——剩余符号流逐字符 ∈ 符号集 + 数值 token + 占位符（N/A/S）
        /// </summary>
        /// <param name="text">命名提取后文本</param>
        /// <param name="result">词法结果</param>
        /// <param name="sectionNo">段号</param>
        /// <returns>校验后文本（原样）</returns>
        private static string ValidateSymbols(string text, LexResultV2 result, int sectionNo)
        {
            int i = 0;
            while (i < text.Length)
            {
                char c = text[i];

                // 占位符——N{n}/A{n}/"S{n}"
                if (c == 'N' || c == 'A' || c == '"')
                {
                    int consume = TryConsumePlaceholder(text, i);
                    if (consume > 0)
                    {
                        i = i + consume;
                        continue;
                    }
                }

                // 数值 token——连续 [0-9.] 序列整体校验
                if (IsDigitOrDot(c))
                {
                    int j = i;
                    while (j < text.Length && IsDigitOrDot(text[j]))
                    {
                        j = j + 1;
                    }
                    string number = text.Substring(i, j - i);
                    if (!IsNumber(number))
                    {
                        result.Diagnostics.Add(new MauDiagnostic("E003", sectionNo, "数值 token 非法——'" + number + "'（应为 [0-9]+(.[0-9]+)?）"));
                        result.Success = false;
                        return "";
                    }
                    i = j;
                    continue;
                }

                // 符号集成员
                if (SymbolChars.IndexOf(c) >= 0)
                {
                    i = i + 1;
                    continue;
                }

                result.Diagnostics.Add(new MauDiagnostic("E003", sectionNo, "符号集外字符——U+" + ((int)c).ToString("X4") + " '" + c + "'（不在词法白名单）"));
                result.Success = false;
                return "";
            }
            return text;
        }

        /// <summary>
        /// 数字或小数点判断
        /// </summary>
        /// <param name="c">字符</param>
        /// <returns>为真</returns>
        private static bool IsDigitOrDot(char c)
        {
            return (c >= '0' && c <= '9') || c == '.';
        }

        /// <summary>
        /// 占位符消费——N{n}/A{n}/"S{n}" 形态匹配则返回长度（{ } 定界避免索引与数字黏连歧义）
        /// </summary>
        /// <param name="text">文本</param>
        /// <param name="i">当前位置</param>
        /// <returns>占位符长度（非占位符为 0）</returns>
        private static int TryConsumePlaceholder(string text, int i)
        {
            char c = text[i];
            int j = i + 1;
            if (c == '"')
            {
                // "S{n}"
                if (j >= text.Length || text[j] != 'S')
                {
                    return 0;
                }
                j = j + 1;
            }
            // { 数字 } 定界
            if (j >= text.Length || text[j] != '{')
            {
                return 0;
            }
            j = j + 1;
            int digits = 0;
            while (j < text.Length && text[j] >= '0' && text[j] <= '9')
            {
                j = j + 1;
                digits = digits + 1;
            }
            if (digits == 0 || j >= text.Length || text[j] != '}')
            {
                return 0;
            }
            j = j + 1;
            if (c == '"')
            {
                // 必须闭合引号
                if (j < text.Length && text[j] == '"')
                {
                    return j - i + 1;
                }
                return 0;
            }
            return j - i;
        }

        /// <summary>
        /// 段分类——纯符号串 token 序列结构判定（第 7 步）
        /// </summary>
        /// <param name="text">段文本（占位符形态）</param>
        /// <param name="paramRaws">参数容器原始内容——语法糖判定用（→/∥ 在容器内）</param>
        /// <returns>段类型</returns>
        private static MauSectionKindV2 ClassifySection(string text, List<string> paramRaws)
        {
            if (text.Length == 0)
            {
                return MauSectionKindV2.Unknown;
            }

            char first = text[0];
            if (first == '⇐')
            {
                return MauSectionKindV2.BoundaryIn;
            }
            if (first == '⇒')
            {
                return MauSectionKindV2.BoundaryOut;
            }
            if (first == '@')
            {
                return MauSectionKindV2.Interface;
            }
            if (first == '$')
            {
                return MauSectionKindV2.Inject;
            }
            if (first != 'N')
            {
                return MauSectionKindV2.Unknown;
            }

            // N 占位符开头——读取 N{n} 占位符后继续
            int pos = 1;
            if (pos < text.Length && text[pos] == '{')
            {
                pos = pos + 1;
                while (pos < text.Length && text[pos] >= '0' && text[pos] <= '9')
                {
                    pos = pos + 1;
                }
                if (pos < text.Length && text[pos] == '}')
                {
                    pos = pos + 1;
                }
            }

            // 段尾——单裸命名 = 命题声明（'P_Init'）
            if (pos >= text.Length)
            {
                return MauSectionKindV2.Propositions;
            }

            char second = text[pos];
            if (second == '=')
            {
                // N = { ... } → MachineDef；N = 数值？——版本声明是 N 数值（无 =）
                int after = pos + 1;
                if (after < text.Length && text[after] == '{')
                {
                    return MauSectionKindV2.MachineDef;
                }
                return MauSectionKindV2.Unknown;
            }
            if (second == '∈')
            {
                return MauSectionKindV2.MachineBind;
            }
            if (second == ',')
            {
                return MauSectionKindV2.Propositions;
            }
            if (second == 'A')
            {
                // N A{n}——容器后判定
                int aEnd = pos + 1;
                if (aEnd < text.Length && text[aEnd] == '{')
                {
                    aEnd = aEnd + 1;
                    while (aEnd < text.Length && text[aEnd] >= '0' && text[aEnd] <= '9')
                    {
                        aEnd = aEnd + 1;
                    }
                    if (aEnd < text.Length && text[aEnd] == '}')
                    {
                        aEnd = aEnd + 1;
                    }
                }
                if (aEnd < text.Length && text[aEnd] == ':')
                {
                    // 测量或控制律——含 → 优先 Law（捕获赋值控制律也含 :=）；无 → 含 := → Measure
                    if (text.Contains("→", StringComparison.Ordinal))
                    {
                        return MauSectionKindV2.Law;
                    }
                    if (text.Contains(":=", StringComparison.Ordinal))
                    {
                        return MauSectionKindV2.Measure;
                    }
                    return MauSectionKindV2.Unknown;
                }
                // 语法糖——N[A] 无冒号 = 糖引用段（容器内引用序列——SEQ/PAR/FBK/未知糖名由验证层判）
                return MauSectionKindV2.Sugar;
            }
            if (second == ':')
            {
                // N : 数值 → Resource；否则无属性容器的控制律/测量（属性可选）
                int after = pos + 1;
                if (after < text.Length && (text[after] >= '0' && text[after] <= '9'))
                {
                    return MauSectionKindV2.Resource;
                }
                if (text.Contains("→", StringComparison.Ordinal))
                {
                    return MauSectionKindV2.Law;
                }
                if (text.Contains(":=", StringComparison.Ordinal))
                {
                    return MauSectionKindV2.Measure;
                }
                return MauSectionKindV2.Unknown;
            }

            // N 数值 → Version（'Mau' 2.0）
            if (second >= '0' && second <= '9')
            {
                return MauSectionKindV2.Version;
            }

            return MauSectionKindV2.Unknown;
        }
    }
}
