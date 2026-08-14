using System;
using System.Collections.Generic;
using Mau.Contracts;

namespace Mau.Translator
{
    /// <summary>
    /// 词法器 v3——外观字符流 → Token 流（design-mau-v3 §三）。
    /// 白名单词法：任何不属于白名单的字符在词法层直接拒绝——99% 风险前置。
    /// 词法元素四种：注释行（丢弃）/ '...' 专有名词 / [...] 参数（子词法）/ 符号集（外观映射）。
    /// 错误码：E001 非法字符 / E002 专有名词白名单违规 / E003 字符串未闭合 / E004 参数未闭合。
    /// </summary>
    public static class MauLexerV3
    {
        /// <summary>
        /// 词法扫描——全量扫描收集所有词法错误（不因首错中断）
        /// </summary>
        /// <param name="source">源文本</param>
        /// <param name="appearance">符号外观表（我的方言拼写）</param>
        /// <returns>Token 流 + 诊断</returns>
        public static LexResultV3 Lex(string source, ISymbolAppearance appearance)
        {
            LexResultV3 result = new LexResultV3();
            if (source == null)
            {
                result.Diagnostics.Add(new MauDiagnostic("E000", 1, "源文本为空"));
                return result;
            }
            if (appearance == null)
            {
                result.Diagnostics.Add(new MauDiagnostic("E000", 1, "符号外观未提供"));
                return result;
            }

            List<TokenV3> tokens = result.Tokens;
            int line = 1;
            int col = 1;
            int i = 0;
            int len = source.Length;
            while (i < len)
            {
                char c = source[i];
                // [段1] 换行与空白
                if (c == '\n')
                {
                    line = line + 1;
                    col = 1;
                    i = i + 1;
                    continue;
                }
                if (c == ' ' || c == '\t' || c == '\r')
                {
                    col = col + 1;
                    i = i + 1;
                    continue;
                }
                // [段2] 行注释——词法丢弃
                if (c == '/' && i + 1 < len && source[i + 1] == '/')
                {
                    while (i < len && source[i] != '\n')
                    {
                        i = i + 1;
                        col = col + 1;
                    }
                    continue;
                }
                // [段3] 专有名词 '...'——命名白名单 [A-Za-z0-9_.]+
                if (c == '\'')
                {
                    int startLine = line;
                    int startCol = col;
                    int j = i + 1;
                    int contentStart = j;
                    while (j < len && source[j] != '\'' && source[j] != '\n')
                    {
                        j = j + 1;
                    }
                    if (j >= len || source[j] != '\'')
                    {
                        result.Diagnostics.Add(new MauDiagnostic("E002", startLine, "专有名词未闭合（缺 '）——列 " + startCol));
                        i = j;
                        col = col + (j - i) + 1;
                        continue;
                    }
                    string name = source.Substring(contentStart, j - contentStart);
                    if (!IsNameValid(name))
                    {
                        result.Diagnostics.Add(new MauDiagnostic("E002", startLine, "专有名词白名单违规: '" + name + "'——只允许 [A-Za-z0-9_.]+"));
                    }
                    else
                    {
                        tokens.Add(new TokenV3(TokenIds.Name, startLine, startCol, name));
                    }
                    col = col + (j - i) + 1;
                    i = j + 1;
                    continue;
                }
                // [段4] 字符串 "..."——不跨行
                if (c == '"')
                {
                    int startLine = line;
                    int j = i + 1;
                    while (j < len && source[j] != '"' && source[j] != '\n')
                    {
                        j = j + 1;
                    }
                    if (j >= len || source[j] != '"')
                    {
                        result.Diagnostics.Add(new MauDiagnostic("E003", startLine, "字符串未闭合（缺 \"）"));
                        i = j;
                        continue;
                    }
                    string str = source.Substring(i + 1, j - i - 1);
                    tokens.Add(new TokenV3(TokenIds.Str, startLine, col, str));
                    col = col + (j - i) + 1;
                    i = j + 1;
                    continue;
                }
                // [段5] 参数 [...]——子词法
                if (c == '[')
                {
                    int startLine = line;
                    int startCol = col;
                    LexParam(source, ref i, startLine, startCol, tokens, result.Diagnostics);
                    continue;
                }
                // [段5b] 裸数值——参数外数值（槽容量等）
                if (c >= '0' && c <= '9')
                {
                    int j = i;
                    while (j < len && ((source[j] >= '0' && source[j] <= '9') || source[j] == '.'))
                    {
                        j = j + 1;
                    }
                    tokens.Add(new TokenV3(TokenIds.Num, line, col, source.Substring(i, j - i)));
                    col = col + (j - i);
                    i = j;
                    continue;
                }
                // [段5c] 裸 @ 盒子 Key——@key（私有盒引用，参数外形态：捕获目标/条件判真）
                if (c == '@')
                {
                    int j = i + 1;
                    while (j < len && IsWordChar(source[j]))
                    {
                        j = j + 1;
                    }
                    if (j > i + 1)
                    {
                        tokens.Add(new TokenV3(TokenIds.Name, line, col, source.Substring(i, j - i)));
                        col = col + (j - i);
                        i = j;
                        continue;
                    }
                }
                // [段6] 符号集——外观映射（2 字符优先，1 字符兜底）
                uint tokenId;
                bool mapped = false;
                if (i + 1 < len)
                {
                    string two = new string(new char[] { c, source[i + 1] });
                    if (appearance.TryMap(two, out tokenId))
                    {
                        tokens.Add(new TokenV3(tokenId, line, col, ""));
                        i = i + 2;
                        col = col + 2;
                        mapped = true;
                    }
                }
                if (!mapped)
                {
                    if (appearance.TryMap(c.ToString(), out tokenId))
                    {
                        tokens.Add(new TokenV3(tokenId, line, col, ""));
                        i = i + 1;
                        col = col + 1;
                    }
                    else
                    {
                        result.Diagnostics.Add(new MauDiagnostic("E001", line, "非法字符 '" + SafeChar(c) + "'——不属于外观白名单（列 " + col + "）"));
                        i = i + 1;
                        col = col + 1;
                    }
                }
            }
            // [段7] 文件尾
            tokens.Add(new TokenV3(TokenIds.Eof, line, col, ""));
            result.Success = result.Diagnostics.Count == 0;
            return result;
        }

        /// <summary>
        /// 参数子词法——[ 内扫描 Word/Num/Str/=/, 直到 ]。
        /// 内容白名单：引用 [A-Za-z_0-9.]+ / 数值 [0-9.]+ / 字符串 "..."。
        /// </summary>
        /// <param name="source">源文本</param>
        /// <param name="i">扫描游标（引用传递，结束时指向 ] 之后）</param>
        /// <param name="startLine">[ 所在行</param>
        /// <param name="startCol">[ 所在列</param>
        /// <param name="tokens">输出 token 流</param>
        /// <param name="diags">诊断收集</param>
        private static void LexParam(string source, ref int i, int startLine, int startCol,
            List<TokenV3> tokens, List<MauDiagnostic> diags)
        {
            tokens.Add(new TokenV3(TokenIds.ParamOpen, startLine, startCol, ""));
            i = i + 1;
            int len = source.Length;
            bool closed = false;
            while (i < len)
            {
                char c = source[i];
                if (c == ' ' || c == '\t' || c == '\r' || c == '\n')
                {
                    i = i + 1;
                    continue;
                }
                if (c == ']')
                {
                    tokens.Add(new TokenV3(TokenIds.ParamClose, startLine, startCol, ""));
                    i = i + 1;
                    closed = true;
                    break;
                }
                if (c == '!')
                {
                    tokens.Add(new TokenV3(TokenIds.Word, startLine, startCol, "!"));
                    i = i + 1;
                    continue;
                }
                if (c == '=')
                {
                    tokens.Add(new TokenV3(TokenIds.Eq, startLine, startCol, ""));
                    i = i + 1;
                    continue;
                }
                if (c == ',')
                {
                    tokens.Add(new TokenV3(TokenIds.Sep, startLine, startCol, ""));
                    i = i + 1;
                    continue;
                }
                if (c == '"')
                {
                    int j = i + 1;
                    while (j < len && source[j] != '"' && source[j] != '\n')
                    {
                        j = j + 1;
                    }
                    if (j >= len || source[j] != '"')
                    {
                        diags.Add(new MauDiagnostic("E003", startLine, "参数内字符串未闭合"));
                        i = j;
                        continue;
                    }
                    tokens.Add(new TokenV3(TokenIds.Str, startLine, startCol, source.Substring(i + 1, j - i - 1)));
                    i = j + 1;
                    continue;
                }
                if (IsWordChar(c))
                {
                    int j = i;
                    while (j < len && IsWordChar(source[j]))
                    {
                        j = j + 1;
                    }
                    string word = source.Substring(i, j - i);
                    bool allNumeric = true;
                    for (int k = 0; k < word.Length; k++)
                    {
                        char wc = word[k];
                        if (!(wc >= '0' && wc <= '9') && wc != '.')
                        {
                            allNumeric = false;
                            break;
                        }
                    }
                    uint kind = TokenIds.Word;
                    if (allNumeric)
                    {
                        kind = TokenIds.Num;
                    }
                    tokens.Add(new TokenV3(kind, startLine, startCol, word));
                    i = j;
                    continue;
                }
                diags.Add(new MauDiagnostic("E001", startLine, "参数内非法字符 '" + SafeChar(c) + "'"));
                i = i + 1;
            }
            if (!closed)
            {
                diags.Add(new MauDiagnostic("E004", startLine, "参数未闭合（缺 ]）——列 " + startCol));
            }
        }

        /// <summary>
        /// 专有名词白名单校验——[A-Za-z0-9_.]+（含数字——v2 拍板：命名白名单含数字，如 CH4 接口名）
        /// </summary>
        /// <param name="name">名词内容</param>
        /// <returns>合法为真</returns>
        private static bool IsNameValid(string name)
        {
            if (name.Length == 0)
            {
                return false;
            }
            for (int i = 0; i < name.Length; i++)
            {
                if (!IsNameChar(name[i]))
                {
                    return false;
                }
                if (name[i] == '@' && i > 0)
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// 名词字符——字母/数字/下划线/点/@（@ 私有盒前缀——只允许出现在首位）
        /// </summary>
        /// <param name="c">字符</param>
        /// <returns>合法为真</returns>
        private static bool IsNameChar(char c)
        {
            return (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z')
                || (c >= '0' && c <= '9') || c == '_' || c == '.' || c == '@';
        }

        /// <summary>
        /// 参数词字符——字母/数字/下划线/点
        /// </summary>
        /// <param name="c">字符</param>
        /// <returns>合法为真</returns>
        private static bool IsWordChar(char c)
        {
            return IsNameChar(c);
        }

        /// <summary>
        /// 非法字符安全文本化——控制字符显示码点
        /// </summary>
        /// <param name="c">字符</param>
        /// <returns>可显示文本</returns>
        private static string SafeChar(char c)
        {
            if (c < ' ' || c > '~')
            {
                return "U+" + ((int)c).ToString("X4");
            }
            return c.ToString();
        }
    }

    /// <summary>
    /// 词法结果——Token 流 + 诊断（Success = 诊断空）
    /// </summary>
    public sealed class LexResultV3
    {
        /// <summary>
        /// 词法是否通过
        /// </summary>
        public bool Success;

        /// <summary>
        /// Token 流（末尾带 Eof）
        /// </summary>
        public List<TokenV3> Tokens = new List<TokenV3>();

        /// <summary>
        /// 词法诊断（E0xx）
        /// </summary>
        public List<MauDiagnostic> Diagnostics = new List<MauDiagnostic>();
    }
}
