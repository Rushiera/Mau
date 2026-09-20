using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using Mau.Runtime;

namespace CH4
{
    /// <summary>
    /// 工具摘要格式化——将工具参数 JSON + 结果文本翻译为一行最简标准化自然语言介绍。
    /// 对齐 CH2 CH_Tool_LLMToolDisplay（P7 折叠气泡 summary）；每个工具一个独立 Fmt 方法，便于单独调校。
    /// 消费方：ChatSession 实时推送 toolcard payload + SessionViewStore 历史落盘（summary 字段——前端兜底 name）。
    /// </summary>
    internal static class ToolSummaryFormatter
    {
        // ═══════════════════════════════════════════
        // 主入口
        // ═══════════════════════════════════════════

        /// <summary>
        /// 解析参数 JSON → 分发到各工具 Fmt 方法 → 单行化。
        /// </summary>
        /// <param name="toolName">工具名（LLM 传回的原名，如 text-read）</param>
        /// <param name="argsJson">工具参数 JSON 字符串</param>
        /// <param name="resultText">工具执行结果文本</param>
        /// <returns>一行标准化自然语言摘要（无 icon——前端 summary 自带 🔧 前缀）</returns>
        public static string Build(string toolName, string argsJson, string resultText)
        {
            string name = toolName ?? "";
            Dictionary<string, string> p = ParseArgs(argsJson);
            // 结构化返回头剥离（design-ch4-tools 附录）——非 cs-* 工具吃正文；cs-* 自解首行元数据（Fmt_CSharpCode）
            string rawResult = resultText ?? "";
            string dispatchText = name.StartsWith("cs-", StringComparison.Ordinal) ? rawResult : StripMetaHead(rawResult);
            string body = Dispatch(name, p, dispatchText);
            if (body.Length == 0)
            {
                body = "调用工具 \"" + name + "\"";
            }
            // 单行化——HTML summary 元素不折行（防 ChatUI 错位）
            return body.Replace("\r", "").Replace("\n", "  ");
        }

        // ═══════════════════════════════════════════
        // 参数解析
        // ═══════════════════════════════════════════

        /// <summary>
        /// 结构化返回头剥离——首行 JSON 含 tool 字段 → 返回正文；无头 / 非结构化结果原样返回。
        /// 约定：工具返回体 = 首行 JSON 元数据头 + 正文定界行（design-ch4-tools 附录）。
        /// </summary>
        /// <param name="text">工具结果原文</param>
        /// <returns>正文（无头时 = 原文）</returns>
        private static string StripMetaHead(string text)
        {
            if (string.IsNullOrEmpty(text) || text[0] != '{')
            {
                return text;
            }
            int nl = text.IndexOf('\n');
            string first = (nl < 0) ? text : text.Substring(0, nl);
            try
            {
                using (JsonDocument doc = JsonDocument.Parse(first))
                {
                    JsonElement toolEl;
                    if (doc.RootElement.ValueKind != JsonValueKind.Object ||
                        !doc.RootElement.TryGetProperty("tool", out toolEl) ||
                        toolEl.ValueKind != JsonValueKind.String)
                    {
                        return text;
                    }
                }
            }
            catch (Exception ex)
            {
                LogStore.Add("CatHome4", 2, "工具头解析失败（按原文）: " + ex.Message, "TOOL");
                return text;
            }
            return (nl < 0) ? "" : text.Substring(nl + 1);
        }

        /// <summary>
        /// 将工具 args JSON 解析为扁平字典——字符串取值，数组/对象保留原始 JSON（供统计类 Fmt 再解析）。
        /// </summary>
        /// <param name="argsJson">参数 JSON</param>
        /// <returns>扁平字典（解析失败返回空字典——容错）</returns>
        private static Dictionary<string, string> ParseArgs(string argsJson)
        {
            Dictionary<string, string> dic = new Dictionary<string, string>();
            if (string.IsNullOrEmpty(argsJson) || !argsJson.StartsWith("{"))
            {
                return dic;
            }
            try
            {
                using (JsonDocument doc = JsonDocument.Parse(argsJson))
                {
                    foreach (JsonProperty prop in doc.RootElement.EnumerateObject())
                    {
                        string v;
                        if (prop.Value.ValueKind == JsonValueKind.String)
                        {
                            v = prop.Value.GetString() ?? "";
                        }
                        else
                        {
                            v = prop.Value.GetRawText();
                        }
                        dic[prop.Name] = v;
                    }
                }
            }
            catch (Exception ex)
            {
                LogStore.Add("CatHome4", 2, "结构化头解析失败（按空表）: " + ex.Message, "TOOL");
            }
            return dic;
        }

        /// <summary>从字典取值——不存在返回空串</summary>
        private static string Arg(Dictionary<string, string> p, string key)
        {
            if (p.TryGetValue(key, out string v))
            {
                return v;
            }
            return "";
        }

        // ═══════════════════════════════════════════
        // 分派——按 CH4 工具组（TextCat/CsCat/ConfigCat/MauCat/PsCat/SearchCat/VisionCat/TempToolCat/内置）
        // ═══════════════════════════════════════════

        /// <summary>
        /// 根据工具名路由到对应的 Fmt_xxx 格式化方法——全工具覆盖 + 未知兜底。
        /// </summary>
        /// <param name="name">工具名</param>
        /// <param name="p">参数字典</param>
        /// <param name="result">结果文本</param>
        /// <returns>格式化摘要行</returns>
        private static string Dispatch(string name, Dictionary<string, string> p, string result)
        {
            // [段1] TextCat——读取
            if (name == "text-read") { return Fmt_Read(p, result); }
            if (name == "text-read_between") { return Fmt_ReadBetween(p, result); }
            if (name == "text-read_lines") { return Fmt_ReadLines(p, result); }

            // [段2] TextCat——写入
            if (name == "text-write") { return Fmt_Write(p, result); }
            if (name == "text-append") { return Fmt_Append(p, result); }

            // [段3] TextCat——修改/操作
            if (name == "text-replace") { return Fmt_Replace(p, result); }
            if (name == "text-grep") { return Fmt_Find(p, result, "检索内容"); }

            // [段3b] FileCat——file-*（A67 拆分：结构面独立组——同一套格式化）
            if (name == "file-tree") { return Fmt_Tree(p, result); }
            if (name == "file-find") { return Fmt_Find(p, result, "搜索文件"); }
            if (name == "file-move") { return Fmt_Move(p, result); }
            if (name == "file-delete") { return Fmt_Delete(p, result); }
            if (name == "file-copy") { return Fmt_Copy(p, result); }

            // [段4] CsCat——cs-* 统一（动词映射 + 类.成员 + 结果统计）
            if (name.StartsWith("cs-")) { return Fmt_CSharpCode(name, p, result); }

            // [段5] ConfigCat
            if (name == "config-list") { return Fmt_ConfigList(p, result); }
            if (name == "config-get") { return Fmt_ConfigGet(p, result); }
            if (name == "config-set") { return Fmt_ConfigSet(p, result); }
            if (name == "config-reset") { return Fmt_ConfigReset(p, result); }

            // [段6] MauCat
            if (name == "mau-verify") { return Fmt_Mau(name, p, result, "Mau验证"); }
            if (name == "mau-gen") { return Fmt_Mau(name, p, result, "Mau生成"); }
            if (name == "mau-proj") { return Fmt_Mau(name, p, result, "Mau组翻译"); }

            // [段7] PsCat / SearchCat / VisionCat / TempToolCat
            if (name == "powershell") { return Fmt_Shell(p, result); }
            if (name == "web-search") { return Fmt_WebSearch(p, result); }
            if (name == "image-analyze") { return Fmt_Vision(p, result); }
            if (name == "temp-info") { return Fmt_TempInfo(p, result); }
            if (name == "temp-exec") { return Fmt_TempExec(p, result); }

            // [段8] 内置工具
            if (name == "Note") { return Fmt_Note(p, result); }
            if (name == "time") { return "获取时间 → " + Q(Trunc(FirstLine(result), 40)); }
            if (name == "random") { return Fmt_Random(p, result); }
            if (name == "info") { return Fmt_Info(result); }

            // [段9] 未知工具——尽力展示参数
            return Fmt_Unknown(name, p, result);
        }

        // ═══════════════════════════════════════════
        // TextCat——读取
        // ═══════════════════════════════════════════

        /// <summary>text-read——文件内容截断预览</summary>
        private static string Fmt_Read(Dictionary<string, string> p, string result)
        {
            string path = Arg(p, "path");
            return "读取文件 " + Q(TruncPath(path)) + " → " + Q(SummarizeText(result));
        }

        /// <summary>text-read_between——区间内容截断预览（锚点截 15 字）</summary>
        private static string Fmt_ReadBetween(Dictionary<string, string> p, string result)
        {
            string path = Arg(p, "path");
            string str1 = Arg(p, "str1");
            string str2 = Arg(p, "str2");
            string anchors = "";
            if (str1.Length > 0)
            {
                anchors = anchors + " 「" + (str1.Length > 15 ? str1.Substring(0, 15) + "…" : str1) + "」";
            }
            if (str2.Length > 0)
            {
                anchors = anchors + " ~ 「" + (str2.Length > 15 ? str2.Substring(0, 15) + "…" : str2) + "」";
            }
            return "区间读取 " + Q(TruncPath(path)) + anchors + " → " + Q(SummarizeText(result));
        }

        /// <summary>text-read_lines——文件名+行范围+内容预览</summary>
        private static string Fmt_ReadLines(Dictionary<string, string> p, string result)
        {
            string path = Arg(p, "path");
            string startStr = Arg(p, "start_line");
            string endStr = Arg(p, "end_line");
            string fileName = System.IO.Path.GetFileName(path);
            if (fileName.Length == 0)
            {
                fileName = TruncPath(path);
            }
            int startLine = 1;
            int endLine = 0;
            int.TryParse(startStr, out startLine);
            int.TryParse(endStr, out endLine);
            if (startLine < 1)
            {
                startLine = 1;
            }
            string rangeDesc;
            if (endLine > 0)
            {
                rangeDesc = "第 " + startLine + " 行到第 " + endLine + " 行";
            }
            else
            {
                rangeDesc = "第 " + startLine + " 行起";
            }
            return "按行读取 " + Q(fileName) + " " + Q(rangeDesc) + " → " + Q(SummarizeText(result));
        }

        // ═══════════════════════════════════════════
        // TextCat——写入
        // ═══════════════════════════════════════════

        /// <summary>text-write——路径+内容预览</summary>
        private static string Fmt_Write(Dictionary<string, string> p, string result)
        {
            string path = Arg(p, "path");
            string content = Arg(p, "content");
            string preview = content.Length > 0 ? " ← " + Q(ResultPreview(content)) : "";
            return "写入文件 " + Q(TruncPath(path)) + preview + " → OK";
        }

        /// <summary>text-append——路径+追加内容预览</summary>
        private static string Fmt_Append(Dictionary<string, string> p, string result)
        {
            string path = Arg(p, "path");
            string content = Arg(p, "content");
            string preview = content.Length > 0 ? " ← " + Q(ResultPreview(content)) : "";
            return "追加文本 " + Q(TruncPath(path)) + preview + " → OK";
        }

        // ═══════════════════════════════════════════
        // TextCat——修改/操作
        // ═══════════════════════════════════════════

        /// <summary>text-replace——路径+新旧片段（UTF-8 字节数）</summary>
        private static string Fmt_Replace(Dictionary<string, string> p, string result)
        {
            string path = Arg(p, "path");
            // 参数键名对齐 text.replace 积木契约（BRIK-TEXT-004：path/old/new/mode）——str/new_str 取空致 0B 回归
            string oldStr = Arg(p, "old");
            string newStr = Arg(p, "new");
            string fileName = System.IO.Path.GetFileName(path);
            if (fileName.Length == 0)
            {
                fileName = TruncPath(path);
            }
            string oldBrief = oldStr.Length > 15 ? oldStr.Substring(0, 15) + "…" : oldStr;
            string newBrief = newStr.Length > 15 ? newStr.Substring(0, 15) + "…" : newStr;
            int oldB = Encoding.UTF8.GetByteCount(oldStr);
            int newB = Encoding.UTF8.GetByteCount(newStr);
            return "替换文本 " + Q(fileName) + " → " + Q(newBrief) + "(" + newB + "B) 覆盖 " + Q(oldBrief) + "(" + oldB + "B)";
        }
        /// <summary>text-find / text-grep——命中统计+文件预览（verb 区分搜索文件/检索内容；CH4 输出适配：find=相对路径行，grep=相对路径:行号:上下文）</summary>
        private static string Fmt_Find(Dictionary<string, string> p, string result, string verb)
        {
            string dir = Arg(p, "dir");
            string pat = Arg(p, "pattern");
            string kw = Arg(p, "keyword");
            string cond = "";
            if (pat.Length > 0)
            {
                cond = cond + " glob=" + Q(pat);
            }
            if (kw.Length > 0)
            {
                cond = cond + " 含 " + Q(kw);
            }
            string loc = dir.Length > 0 ? TruncPath(dir) : "当前目录";

            // 行解析——find：相对路径行；grep：相对路径:行号:上下文（首个冒号前=路径）
            HashSet<string> files = new HashSet<string>(StringComparer.Ordinal);
            int hitCount = 0;
            string[] lines = result.Split('\n');
            for (int i = 0; i < lines.Length; i = i + 1)
            {
                string line = lines[i].Trim();
                if (line.Length == 0 || line.StartsWith("ERR|") || line.StartsWith("（"))
                {
                    continue;
                }
                string fp = line;
                int colon = line.IndexOf(':');
                if (colon > 0)
                {
                    fp = line.Substring(0, colon);
                    hitCount = hitCount + 1;
                }
                if (fp.Length > 0)
                {
                    files.Add(fp);
                }
            }

            StringBuilder sb = new StringBuilder();
            sb.Append(verb);
            sb.Append(" "); sb.Append(Q(loc)); sb.Append(cond);
            if (files.Count > 0)
            {
                sb.Append(" → "); sb.Append(files.Count); sb.Append("文件");
                if (hitCount > 0)
                {
                    sb.Append("·"); sb.Append(hitCount); sb.Append("命中");
                }
                int shown = 0;
                StringBuilder preview = new StringBuilder();
                foreach (string f in files)
                {
                    if (shown >= 3)
                    {
                        break;
                    }
                    if (shown > 0)
                    {
                        preview.Append(", ");
                    }
                    preview.Append(Q(TruncPath(f)));
                    shown = shown + 1;
                }
                sb.Append("（"); sb.Append(preview.ToString());
                if (files.Count > 3)
                {
                    sb.Append("…");
                }
                sb.Append("）");
            }
            else
            {
                sb.Append(" → 无结果");
            }
            return sb.ToString();
        }

        /// <summary>text-tree——目录树截断预览</summary>
        private static string Fmt_Tree(Dictionary<string, string> p, string result)
        {
            string path = Arg(p, "path");
            string depth = Arg(p, "depth");
            string extra = depth.Length > 0 ? " depth=" + Q(depth) : "";
            string loc = path.Length > 0 ? TruncPath(path) : "当前目录";
            return "展开目录 " + Q(loc) + extra + " → " + Q(SummarizeFileTree(result));
        }

        /// <summary>text-move——源 → 目标</summary>
        private static string Fmt_Move(Dictionary<string, string> p, string result)
        {
            string src = Arg(p, "src");
            string dest = Arg(p, "dest");
            return "移动文件 " + Q(TruncPath(src)) + " → " + Q(TruncPath(dest)) + " → " + Q(SummarizeBatch(result));
        }

        /// <summary>text-delete——删除确认</summary>
        private static string Fmt_Delete(Dictionary<string, string> p, string result)
        {
            string path = Arg(p, "path");
            return "删除文件 " + Q(TruncPath(path)) + " → " + Q(SummarizeBatch(result));
        }

        /// <summary>file-copy——源 → 目标（A67 拆分新增）</summary>
        private static string Fmt_Copy(Dictionary<string, string> p, string result)
        {
            string src = Arg(p, "src");
            string dest = Arg(p, "dest");
            return "复制文件 " + Q(TruncPath(src)) + " → " + Q(TruncPath(dest)) + " → " + Q(SummarizeBatch(result));
        }

        // ═══════════════════════════════════════════
        // CsCat——cs-* 统一处理
        // ═══════════════════════════════════════════

        /// <summary>cs-* 动词映射——工具名 → 中文动作</summary>
        private static readonly Dictionary<string, string> CSharpVerbMap = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { "cs-check", "C#检查" },
            { "cs-build", "C#构建" },
            { "cs-list", "C#列表" },
            { "cs-read", "C#读取" },
            { "cs-find_ref", "C#查找引用" },
            { "cs-patch", "C#修改" },
            { "cs-member", "C#成员操作" },
            { "cs-comment", "C#注释" },
            { "cs-comment_check", "C#注释检查" },
            { "cs-dead", "C#死代码扫描" },
        };

        /// <summary>
        /// 格式化 cs-* 工具——动词 + 类.成员 + 行范围/位置 + 结果统计（JSON 或中文统计提取）。
        /// </summary>
        /// <param name="name">cs-* 工具名</param>
        /// <param name="p">参数字典</param>
        /// <param name="result">结果文本</param>
        /// <returns>格式化摘要行</returns>
        private static string Fmt_CSharpCode(string name, Dictionary<string, string> p, string result)
        {
            string className = Arg(p, "class");
            string member = Arg(p, "member");
            string method = Arg(p, "method");
            if (member.Length == 0)
            {
                member = method;
            }

            string verb;
            if (!CSharpVerbMap.TryGetValue(name, out verb))
            {
                verb = name;
            }

            StringBuilder sb = new StringBuilder();
            sb.Append(verb);

            if (name == "cs-check" || name == "cs-build")
            {
                string csproj = Arg(p, "csproj");
                if (csproj.Length > 0)
                {
                    sb.Append(" "); sb.Append(Q(TruncPath(csproj)));
                }
            }
            else if (name == "cs-list")
            {
                if (className.Length > 0)
                {
                    sb.Append(" "); sb.Append(Q(className));
                }
                else
                {
                    sb.Append(" "); sb.Append(Q("全项目"));
                }
            }
            else if (name == "cs-read" || name == "cs-find_ref")
            {
                sb.Append(" ");
                if (className.Length > 0)
                {
                    sb.Append(Q(className));
                    if (member.Length > 0)
                    {
                        sb.Append("."); sb.Append(Q(member));
                    }
                }
            }
            else if (name == "cs-patch")
            {
                sb.Append(" ");
                if (className.Length > 0)
                {
                    sb.Append(Q(className));
                    if (member.Length > 0)
                    {
                        sb.Append("."); sb.Append(Q(member));
                    }
                }
                string sl = Arg(p, "start_line");
                string el = Arg(p, "end_line");
                if (sl.Length > 0)
                {
                    sb.Append(" L"); sb.Append(Q(sl));
                    if (el.Length > 0 && el != sl)
                    {
                        sb.Append("~"); sb.Append(Q(el));
                    }
                }
                string body = Arg(p, "body");
                if (body.Length > 0)
                {
                    sb.Append(" ← "); sb.Append(ResultPreview(body));
                }
            }
            else if (name == "cs-member")
            {
                sb.Append(" ");
                if (className.Length > 0)
                {
                    sb.Append(Q(className));
                }
                string pos = Arg(p, "position");
                string anchor = Arg(p, "anchor");
                if (pos.Length > 0)
                {
                    sb.Append(" 于 "); sb.Append(Q(pos));
                }
                if (anchor.Length > 0)
                {
                    sb.Append(" "); sb.Append(Q(anchor));
                }
                string newName = Arg(p, "new_name");
                if (newName.Length > 0)
                {
                    sb.Append(" → "); sb.Append(Q(newName));
                }
            }
            else if (name == "cs-comment")
            {
                sb.Append(" ");
                if (className.Length > 0)
                {
                    sb.Append(Q(className));
                    if (member.Length > 0)
                    {
                        sb.Append("."); sb.Append(Q(member));
                    }
                }
                string type = Arg(p, "type");
                if (type.Length > 0)
                {
                    sb.Append(" "); sb.Append(Q(type));
                }
            }

            // 结果——优先解析首行 JSON 元数据头（结构化返回体：首行头 + 正文定界）
            if (result.Length > 0)
            {
                string headLine = FirstLine(result);
                if (headLine.StartsWith("{"))
                {
                    string parsed = ParseCSharpResult(headLine);
                    if (parsed.Length > 0)
                    {
                        sb.Append(" → "); sb.Append(parsed);
                    }
                }
                else if (IsOkResult(result))
                {
                    string okStats = ExtractCSharpStats(name, result);
                    if (okStats.Length > 0)
                    {
                        sb.Append(" → OK（"); sb.Append(Q(okStats)); sb.Append("）");
                    }
                    else
                    {
                        sb.Append(" → OK");
                    }
                }
                else
                {
                    string elseStats = ExtractCSharpStats(name, result);
                    if (elseStats.Length > 0)
                    {
                        sb.Append(" → OK（"); sb.Append(Q(elseStats)); sb.Append("）");
                    }
                    else
                    {
                        sb.Append(" → "); sb.Append(Q(Trunc(FirstLine(result), 60)));
                    }
                }
            }
            return sb.ToString();
        }

        /// <summary>从 cs-* 文本结果中提取关键统计数字——中文关键词锚定</summary>
        private static string ExtractCSharpStats(string name, string result)
        {
            if (name == "cs-check" || name == "cs-build")
            {
                int errs = ExtractInt(result, "错误", -1);
                int warns = ExtractInt(result, "警告", -1);
                if (errs >= 0 || warns >= 0)
                {
                    string s = "共";
                    if (errs >= 0)
                    {
                        s = s + errs + "错";
                    }
                    if (warns >= 0)
                    {
                        s = s + (errs >= 0 ? " " : "") + warns + "警";
                    }
                    return s;
                }
            }
            if (name == "cs-dead")
            {
                int dead = ExtractInt(result, "零引用", -1);
                if (dead < 0)
                {
                    dead = ExtractInt(result, "个", -1);
                }
                if (dead >= 0)
                {
                    return "共" + dead + "个死代码";
                }
            }
            if (name == "cs-comment_check")
            {
                int missing = ExtractInt(result, "缺少", -1);
                if (missing < 0)
                {
                    missing = ExtractInt(result, "缺", -1);
                }
                if (missing >= 0)
                {
                    return "共" + missing + "处缺注释";
                }
            }
            if (name == "cs-list")
            {
                int members = ExtractInt(result, "成员", -1);
                if (members >= 0)
                {
                    return "共" + members + "成员";
                }
            }
            if (name == "cs-find_ref")
            {
                int refs = ExtractInt(result, "引用", -1);
                if (refs >= 0)
                {
                    return "共" + refs + "处引用";
                }
            }
            return "";
        }

        /// <summary>解析 cs-* JSON 结果为人类可读摘要——提取 ok/errors/warnings/统计字段</summary>
        private static string ParseCSharpResult(string result)
        {
            try
            {
                using (JsonDocument doc = JsonDocument.Parse(result))
                {
                    JsonElement root = doc.RootElement;
                    if (root.ValueKind != JsonValueKind.Object)
                    {
                        return "";
                    }
                    // ok 布尔
                    bool ok = true;
                    JsonElement okEl;
                    if (root.TryGetProperty("ok", out okEl) && okEl.ValueKind == JsonValueKind.False)
                    {
                        ok = false;
                    }
                    // 统计字段——errors/warnings/diagnostics
                    int errors = -1;
                    int warnings = -1;
                    JsonElement errEl;
                    if (root.TryGetProperty("errors", out errEl) && errEl.ValueKind == JsonValueKind.Number)
                    {
                        errors = errEl.GetInt32();
                    }
                    JsonElement warnEl;
                    if (root.TryGetProperty("warnings", out warnEl) && warnEl.ValueKind == JsonValueKind.Number)
                    {
                        warnings = warnEl.GetInt32();
                    }
                    if (errors >= 0 || warnings >= 0)
                    {
                        string s = ok ? "OK（共" : "FAIL（共";
                        if (errors >= 0)
                        {
                            s = s + errors + "错";
                        }
                        if (warnings >= 0)
                        {
                            s = s + (errors >= 0 ? " " : "") + warnings + "警";
                        }
                        return s + "）";
                    }
                    // 消息字段兜底
                    JsonElement msgEl;
                    if (root.TryGetProperty("message", out msgEl) && msgEl.ValueKind == JsonValueKind.String)
                    {
                        string msg = msgEl.GetString() ?? "";
                        if (msg.Length > 0)
                        {
                            return Trunc(msg, 60);
                        }
                    }
                    if (!ok)
                    {
                        return "FAIL";
                    }
                    return "OK";
                }
            }
            catch (Exception ex)
            {
                LogStore.Add("CatHome4", 2, "结果状态解析失败（按空）: " + ex.Message, "TOOL");
                return "";
            }
        }

        /// <summary>从文本中提取指定关键词前的首个整数——跳过空格与常见中文单位（"共 2 处缺少"→2）；找不到返回 fallback</summary>
        private static int ExtractInt(string text, string keyword, int fallback)
        {
            int idx = text.IndexOf(keyword);
            if (idx < 0)
            {
                return fallback;
            }
            string numStr = "";
            int i = idx - 1;
            while (i >= 0)
            {
                char c = text[i];
                if (c >= '0' && c <= '9')
                {
                    numStr = c + numStr;
                    i = i - 1;
                    continue;
                }
                // 跳过空格与常见中文单位/标点——继续往前找数字
                if (c == ' ' || c == '\u3000' || c == '处' || c == '个' || c == '条' || c == '项' || c == '名' || c == '类' || c == '行' || c == '：' || c == ':' || c == '，' || c == ',' || c == '、')
                {
                    i = i - 1;
                    continue;
                }
                break;
            }
            if (numStr.Length == 0)
            {
                return fallback;
            }
            int v;
            if (int.TryParse(numStr, out v))
            {
                return v;
            }
            return fallback;
        }

        // ═══════════════════════════════════════════
        // ConfigCat
        // ═══════════════════════════════════════════

        /// <summary>config-list——配置项清单摘要</summary>
        private static string Fmt_ConfigList(Dictionary<string, string> p, string result)
        {
            return "配置列表 → " + Q(SummarizeText(result));
        }

        /// <summary>config-get——读取配置项</summary>
        private static string Fmt_ConfigGet(Dictionary<string, string> p, string result)
        {
            string key = Arg(p, "key");
            string extra = key.Length > 0 ? " " + Q(key) : "";
            return "读取配置" + extra + " → " + Q(Trunc(FirstLine(result), 60));
        }

        /// <summary>config-set——写入配置项（值预览）</summary>
        private static string Fmt_ConfigSet(Dictionary<string, string> p, string result)
        {
            string key = Arg(p, "key");
            string value = Arg(p, "value");
            string extra = key.Length > 0 ? " " + Q(key) : "";
            string valShow = value.Length > 0 ? "=" + Q(ResultPreview(value)) : "";
            return "设置配置" + extra + valShow + " → " + Q(SummarizeBatch(result));
        }

        /// <summary>config-reset——重置配置项</summary>
        private static string Fmt_ConfigReset(Dictionary<string, string> p, string result)
        {
            string key = Arg(p, "key");
            string extra = key.Length > 0 ? " " + Q(key) : "";
            return "重置配置" + extra + " → " + Q(SummarizeBatch(result));
        }

        // ═══════════════════════════════════════════
        // MauCat / PsCat / SearchCat / VisionCat / TempToolCat
        // ═══════════════════════════════════════════

        /// <summary>mau-verify/mau-gen/mau-proj——语料链操作</summary>
        private static string Fmt_Mau(string name, Dictionary<string, string> p, string result, string verb)
        {
            string file = Arg(p, "file");
            string proj = Arg(p, "proj");
            string target = file.Length > 0 ? file : proj;
            string extra = target.Length > 0 ? " " + Q(TruncPath(target)) : "";
            return verb + extra + " → " + Q(SummarizeText(result));
        }

        /// <summary>powershell——执行命令（命令截 40 字）</summary>
        private static string Fmt_Shell(Dictionary<string, string> p, string result)
        {
            string cmd = Arg(p, "command");
            string cmdShow = cmd.Length > 40 ? cmd.Substring(0, 40) + "…" : cmd;
            return "执行命令 " + Q(cmdShow) + " → " + Q(SummarizeText(result));
        }

        /// <summary>web-search——搜索词 + 结果摘要</summary>
        private static string Fmt_WebSearch(Dictionary<string, string> p, string result)
        {
            string query = Arg(p, "query");
            string extra = query.Length > 0 ? " " + Q(Trunc(query, 30)) : "";
            return "搜索网页" + extra + " → " + Q(SummarizeText(result));
        }

        /// <summary>image-analyze——图片路径 + 分析摘要</summary>
        private static string Fmt_Vision(Dictionary<string, string> p, string result)
        {
            string path = Arg(p, "path");
            string extra = path.Length > 0 ? " " + Q(TruncPath(path)) : "";
            return "分析图片" + extra + " → " + Q(Trunc(FirstLine(result), 60));
        }

        /// <summary>temp-info——临时工具区信息</summary>
        private static string Fmt_TempInfo(Dictionary<string, string> p, string result)
        {
            return "临时工具信息 → " + Q(SummarizeText(result));
        }

        /// <summary>temp-exec——临时执行（Key 分发）</summary>
        private static string Fmt_TempExec(Dictionary<string, string> p, string result)
        {
            string key = Arg(p, "key");
            string extra = key.Length > 0 ? " " + Q(key) : "";
            return "临时执行" + extra + " → " + Q(Trunc(FirstLine(result), 60));
        }

        // ═══════════════════════════════════════════
        // 内置工具
        // ═══════════════════════════════════════════

        /// <summary>Note——任务追踪进度（第N条 完成X 待做Y + 目标预览）</summary>
        private static string Fmt_Note(Dictionary<string, string> p, string result)
        {
            string firstLine = FirstLine(result);
            string progress = "";
            int idxDi = firstLine.IndexOf("第");
            int idxTiao = firstLine.IndexOf("条");
            if (idxDi >= 0 && idxTiao > idxDi)
            {
                progress = firstLine.Substring(idxDi + 1, idxTiao - idxDi - 1);
            }
            string done = "";
            int idxDone = firstLine.IndexOf("已完成");
            if (idxDone > 0)
            {
                string donePart = firstLine.Substring(idxDone + 3).Trim();
                string numStr = "";
                for (int ci = 0; ci < donePart.Length; ci = ci + 1)
                {
                    if (donePart[ci] >= '0' && donePart[ci] <= '9')
                    {
                        numStr = numStr + donePart[ci];
                    }
                    else if (numStr.Length > 0)
                    {
                        break;
                    }
                }
                done = numStr;
            }
            string todo = "";
            int idxTodo = firstLine.IndexOf("待完成");
            if (idxTodo > 0)
            {
                string todoPart = firstLine.Substring(idxTodo + 3).Trim();
                string numStr = "";
                for (int ci = 0; ci < todoPart.Length; ci = ci + 1)
                {
                    if (todoPart[ci] >= '0' && todoPart[ci] <= '9')
                    {
                        numStr = numStr + todoPart[ci];
                    }
                    else if (numStr.Length > 0)
                    {
                        break;
                    }
                }
                todo = numStr;
            }
            // 目标——提取"任务目标："后的片段（截 30 字）
            string taskGoal = "";
            int idxGoal = firstLine.IndexOf("任务目标");
            if (idxGoal > 0)
            {
                string goalPart = firstLine.Substring(idxGoal);
                int idxColon = goalPart.IndexOf("：");
                if (idxColon < 0)
                {
                    idxColon = goalPart.IndexOf(":");
                }
                if (idxColon >= 0)
                {
                    taskGoal = goalPart.Substring(idxColon + 1).Trim();
                }
            }
            StringBuilder sb = new StringBuilder();
            sb.Append("任务追踪 → 第"); sb.Append(Q(progress.Length > 0 ? progress : "?")); sb.Append("条");
            if (done.Length > 0)
            {
                sb.Append(" 完成"); sb.Append(Q(done));
            }
            if (todo.Length > 0)
            {
                sb.Append(" 待做"); sb.Append(Q(todo));
            }
            if (taskGoal.Length > 0)
            {
                sb.Append(" "); sb.Append(Q(Trunc(taskGoal, 30)));
            }
            return sb.ToString();
        }

        /// <summary>random——随机数范围 + 结果</summary>
        private static string Fmt_Random(Dictionary<string, string> p, string result)
        {
            string min = Arg(p, "min");
            string max = Arg(p, "max");
            string range = "[";
            if (min.Length > 0)
            {
                range = range + min;
            }
            range = range + ",";
            if (max.Length > 0)
            {
                range = range + max;
            }
            range = range + ")";
            return "随机数 " + Q(range) + " → " + Q(Trunc(FirstLine(result), 20));
        }

        /// <summary>未知工具——列出参数数与结果首行</summary>
        private static string Fmt_Unknown(string name, Dictionary<string, string> p, string result)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("调用工具 "); sb.Append(Q(name));
            if (p.Count > 0)
            {
                sb.Append(" 参数="); sb.Append(p.Count); sb.Append("个");
            }
            if (result.Length > 0)
            {
                sb.Append(" → "); sb.Append(Q(Trunc(FirstLine(result), 50)));
            }
            return sb.ToString();
        }

        // ═══════════════════════════════════════════
        // 辅助：结果摘要
        // ═══════════════════════════════════════════

        /// <summary>文本摘要——≤60 单行直接显示；否则首行 60 字 +（N行 · 大小）</summary>
        private static string SummarizeText(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return "空";
            }
            int lines = CountLines(text);
            string first = FirstLine(text);
            if (text.Length <= 60 && lines <= 1)
            {
                return text;
            }
            return Trunc(first, 60) + "（" + lines + "行 · " + FormatSize(text.Length) + "）";
        }

        /// <summary>FileTree 结果摘要——N 文件 M 目录</summary>
        private static string SummarizeFileTree(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return "空";
            }
            int files = 0;
            int dirs = 0;
            string[] lines = text.Split('\n');
            for (int i = 0; i < lines.Length; i = i + 1)
            {
                string line = lines[i].Trim();
                // Q4 忽略/git 提示行不计入统计（text-tree 结果 [git]/[skip] 前缀——2026-09-08）
                if (line.Length == 0 || line.StartsWith("ERR|") || line.StartsWith("[git]") || line.StartsWith("[skip]"))
                {
                    continue;
                }
                if (line.EndsWith("/"))
                {
                    dirs = dirs + 1;
                }
                else
                {
                    files = files + 1;
                }
            }
            StringBuilder sb = new StringBuilder();
            sb.Append(files); sb.Append("文件 ");
            sb.Append(dirs); sb.Append("目录");
            if (text.Length > 0)
            {
                sb.Append("（"); sb.Append(FormatSize(text.Length)); sb.Append("）");
            }
            return sb.ToString();
        }
        /// <summary>批量类结果摘要——成功/失败首行或大小</summary>
        private static string SummarizeBatch(string result)
        {
            if (string.IsNullOrEmpty(result))
            {
                return "OK";
            }
            string first = FirstLine(result);
            if (first.StartsWith("OK"))
            {
                return Trunc(first, 60);
            }
            if (first.Contains("✓") || first.Contains("✗") || first.Contains("成功") || first.Contains("失败"))
            {
                return Trunc(first, 60);
            }
            if (first.Contains("空操作") || first.Contains("解析失败"))
            {
                return Trunc(first, 40);
            }
            if (first.Contains("成功") || first.Contains("OK"))
            {
                return Trunc(first, 60);
            }
            return FormatSize(result.Length);
        }

        /// <summary>内容预览——参数内容取前 30 字（单行化）</summary>
        private static string ResultPreview(string content)
        {
            string oneLine = content.Replace("\r", "").Replace("\n", " ");
            if (oneLine.Length <= 30)
            {
                return oneLine;
            }
            return oneLine.Substring(0, 30) + "…";
        }

        // ═══════════════════════════════════════════
        // 辅助：字符串工具
        // ═══════════════════════════════════════════

        /// <summary>判断结果是否为 OK（无错误）</summary>
        private static bool IsOkResult(string result)
        {
            if (result.Length == 0)
            {
                return true;
            }
            if (result == "OK")
            {
                return true;
            }
            if (result.StartsWith("[OK]"))
            {
                return true;
            }
            return false;
        }

        /// <summary>统计行数——按 \n 分割</summary>
        private static int CountLines(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return 0;
            }
            int count = 1;
            for (int i = 0; i < text.Length; i = i + 1)
            {
                if (text[i] == '\n')
                {
                    count = count + 1;
                }
            }
            if (text.EndsWith("\n"))
            {
                count = count - 1;
            }
            return count;
        }

        /// <summary>取第一行（到第一个 \n 为止）</summary>
        private static string FirstLine(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return "";
            }
            int nl = text.IndexOf('\n');
            if (nl < 0)
            {
                return text;
            }
            return text.Substring(0, nl);
        }

        /// <summary>格式化字节数为可读大小——B/KB/MB</summary>
        private static string FormatSize(int bytes)
        {
            if (bytes < 1000)
            {
                return bytes.ToString() + " B";
            }
            if (bytes < 1000 * 1000)
            {
                return (bytes / 1000.0).ToString("F1") + " KB";
            }
            return (bytes / (1000.0 * 1000.0)).ToString("F1") + " MB";
        }

        /// <summary>字符串截断到 maxLen——超出加…</summary>
        private static string Trunc(string s, int maxLen)
        {
            if (s == null)
            {
                return "";
            }
            if (s.Length <= maxLen)
            {
                return s;
            }
            return s.Substring(0, maxLen) + "…";
        }

        /// <summary>路径截断——≤55 保留；过长保留末尾文件名（…\file）</summary>
        private static string TruncPath(string path)
        {
            if (path == null || path.Length <= 55)
            {
                return path ?? "";
            }
            int lastSlash = path.LastIndexOfAny(new char[] { '/', '\\' });
            if (lastSlash > 0 && path.Length - lastSlash <= 40)
            {
                return "…" + path.Substring(lastSlash);
            }
            return path.Substring(0, 25) + "…" + path.Substring(path.Length - 25);
        }

        /// <summary>引号包裹——内部双引号替换为单引号（防 HTML summary 引号干扰）</summary>
        private static string Q(string s)
        {
            return "\"" + s.Replace("\"", "'") + "\"";
        }
        /// <summary>info——本会话环境自省（分类 JSON 块：版本 / 猫 / 本地对话端点；解析失败退回原文首行，失败可见）。</summary>
        /// <param name="result">工具结果原文（分类 JSON 块）</param>
        /// <returns>摘要行</returns>
        private static string Fmt_Info(string result)
        {
            if (string.IsNullOrEmpty(result) || result[0] != '{')
            {
                return "环境信息 → " + Q(Trunc(FirstLine(result), 60));
            }
            string version = "";
            string cat = "";
            string chat = "";
            try
            {
                using (JsonDocument doc = JsonDocument.Parse(result))
                {
                    JsonElement root = doc.RootElement;
                    if (root.ValueKind != JsonValueKind.Object)
                    {
                        return "环境信息 → " + Q(Trunc(FirstLine(result), 60));
                    }
                    JsonElement el;
                    if (root.TryGetProperty("cat", out el) && el.ValueKind == JsonValueKind.String)
                    {
                        cat = el.GetString() ?? "";
                    }
                    if (root.TryGetProperty("version", out el) && el.ValueKind == JsonValueKind.Object)
                    {
                        JsonElement vv;
                        if (el.TryGetProperty("version", out vv) && vv.ValueKind == JsonValueKind.String)
                        {
                            version = vv.GetString() ?? "";
                        }
                    }
                    if (root.TryGetProperty("endpoint", out el) && el.ValueKind == JsonValueKind.Object)
                    {
                        JsonElement cc;
                        if (el.TryGetProperty("chat", out cc) && cc.ValueKind == JsonValueKind.String)
                        {
                            chat = cc.GetString() ?? "";
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogStore.Add("CatHome4", 2, "环境信息摘要解析失败（按兜底）: " + ex.Message, "TOOL");
                return "环境信息 → " + Q(Trunc(FirstLine(result), 60));
            }
            StringBuilder sb = new StringBuilder();
            sb.Append("环境信息 → v");
            sb.Append(version);
            if (cat.Length > 0)
            {
                sb.Append(" · 猫 ");
                sb.Append(cat);
            }
            if (chat.Length > 0)
            {
                sb.Append(" · ");
                sb.Append(chat);
            }
            return sb.ToString();
        }
    }
}
