// ═══════════════════════════════════════════════════
// 积木: tool.display
// ID:   BRIK-TOOL-014
// 类别: TOOL
// 作用: 工具结果单行摘要——toolName/argsJson/resultText → 人类可读摘要行（[icon n/m] 格式）
// 依赖: 无
// 包: 无
// 引用: System · System.Text · System.Text.Json
// 原理: 解析 args → 按工具名分派 Fmt 方法 → 包装 emoji 图标 + 并发编号 → 换行过滤（CH2 CH_Tool_LLMToolDisplay 移植 2026-08-10）
// 常用: UiPet 快照合成——Tool 条目摘要化显示（UI 薄壳只读摘要，LLM 上下文保持完整结果）
// ═══════════════════════════════════════════════════
using System;
using System.Collections.Generic;
using System.Text;

namespace Mau.Bricks
{
    /// <summary>
    /// 工具积木——tool.display 工具结果单行摘要（CH2 LLMToolDisplay 移植——六域工具全覆盖）
    /// </summary>
    public static class ToolDisplayBrick
    {
        /// <summary>
        /// 工具结果摘要——解析 args → 分派 Fmt → 包装图标/编号 → 过滤换行
        /// </summary>
        /// <param name="toolName">工具名（点号化路由名）</param>
        /// <param name="argsJson">工具参数 JSON 原文</param>
        /// <param name="resultText">工具执行结果文本</param>
        /// <param name="toolIndex">并发编号（1-based，0=不显示）</param>
        /// <param name="toolTotal">并发总数（≤1=不显示编号）</param>
        /// <param name="display">摘要行</param>
        /// <returns>true=成功</returns>
        public static bool Display(string toolName, string argsJson, string resultText,
            long toolIndex, long toolTotal, out string display)
        {
            display = "";
            if (toolName == null || toolName.Length == 0)
            {
                return false;
            }
            Dictionary<string, string> p = ParseArgs(argsJson);
            string body = Dispatch(toolName, p, resultText);
            string num = "";
            if (toolTotal > 1)
            {
                num = " " + toolIndex.ToString() + "/" + toolTotal.ToString();
            }
            string icon = GetIcon(toolName);
            string line = "[" + icon + num + "] " + body;
            display = line.Replace("\r", "").Replace("\n", "  ");
            return true;
        }

        /// <summary>
        /// 参数 JSON → 扁平字典（字符串值原样；数组/对象保留原始 JSON；解析失败返回空表）
        /// </summary>
        private static Dictionary<string, string> ParseArgs(string argsJson)
        {
            Dictionary<string, string> dic = new Dictionary<string, string>();
            if (string.IsNullOrEmpty(argsJson) || !argsJson.StartsWith("{"))
            {
                return dic;
            }
            try
            {
                using (System.Text.Json.JsonDocument doc = System.Text.Json.JsonDocument.Parse(argsJson))
                {
                    foreach (System.Text.Json.JsonProperty prop in doc.RootElement.EnumerateObject())
                    {
                        if (prop.Value.ValueKind == System.Text.Json.JsonValueKind.String)
                        {
                            dic[prop.Name] = prop.Value.GetString() ?? "";
                        }
                        else
                        {
                            dic[prop.Name] = prop.Value.GetRawText();
                        }
                    }
                }
            }
            catch
            {
            }
            return dic;
        }

        /// <summary>字典取值——不存在返回空串</summary>
        private static string Arg(Dictionary<string, string> p, string key)
        {
            string v;
            if (p.TryGetValue(key, out v))
            {
                return v;
            }
            return "";
        }

        /// <summary>
        /// 工具名 → emoji 图标（CH2 GetIcon 移植）
        /// </summary>
        private static string GetIcon(string name)
        {
            if (name == "file.read" || name == "file.read_between" || name == "file.read_lines") { return "📖"; }
            if (name == "file.write") { return "✏️"; }
            if (name == "file.append") { return "📎"; }
            if (name == "file.replace") { return "🔄"; }
            if (name == "file.tree") { return "🌲"; }
            if (name == "file.find") { return "🔍"; }
            if (name == "file.move") { return "📦"; }
            if (name == "file.delete") { return "🗑️"; }
            if (name == "file.batch") { return "📦"; }
            if (name == "shell.exec") { return "💻"; }
            if (name.StartsWith("excel.")) { return "📊"; }
            if (name.StartsWith("docx.")) { return "📄"; }
            if (name.StartsWith("system.")) { return "ℹ️"; }
            if (name.StartsWith("csharp.")) { return "🐎"; }
            if (name.StartsWith("mau.")) { return "🔧"; }
            if (name.StartsWith("math.")) { return "🎲"; }
            if (name.StartsWith("text.")) { return "📝"; }
            if (name == "approval.request" || name == "approval.resolve"
                || name == "approval.reject" || name == "approval.pending") { return "❓"; }
            return "🔹";
        }

        /// <summary>
        /// 工具名分派到 Fmt 方法——六域工具全覆盖；未知工具尽力展示
        /// </summary>
        private static string Dispatch(string name, Dictionary<string, string> p, string result)
        {
            // 文件读取
            if (name == "file.read") { return Fmt_Read(p, result); }
            if (name == "file.read_between") { return Fmt_ReadBetween(p, result); }
            if (name == "file.read_lines") { return Fmt_ReadLines(p, result); }
            // 文件写入
            if (name == "file.write") { return Fmt_Write(p, result); }
            if (name == "file.append") { return Fmt_Append(p, result); }
            if (name == "file.replace") { return Fmt_Replace(p, result); }
            // 文件操作
            if (name == "file.tree") { return Fmt_Tree(p, result); }
            if (name == "file.find") { return Fmt_Find(p, result); }
            if (name == "file.move") { return Fmt_Move(p, result); }
            if (name == "file.delete") { return Fmt_Delete(p, result); }
            if (name == "file.batch") { return Fmt_Batch(p, result); }
            // 办公
            if (name == "excel.read") { return Fmt_ExcelRead(p, result); }
            if (name == "excel.write") { return Fmt_ExcelWrite(p, result); }
            if (name == "docx.read") { return Fmt_DocxRead(p, result); }
            if (name == "docx.write") { return Fmt_DocxWrite(p, result); }
            // 系统
            if (name == "system.info") { return "系统信息 → " + Q(FirstLine(result)); }
            if (name == "system.env") { return "环境变量 " + Q(Arg(p, "name")) + " → " + Q(FirstLine(result)); }
            // Shell
            if (name == "shell.exec") { return Fmt_Shell(p, result); }
            // C# / Mau / 数学 / 文本
            if (name.StartsWith("csharp.")) { return "C# 操作 → " + Q(FirstLine(result)); }
            if (name.StartsWith("mau.")) { return "Mau 指令 → " + Q(FirstLine(result)); }
            if (name.StartsWith("math.")) { return "计算 → " + Q(FirstLine(result)); }
            if (name.StartsWith("text.")) { return "文本处理 → " + Q(FirstLine(result)); }
            // 审批
            if (name.StartsWith("approval.")) { return "审批请求 → " + Q(FirstLine(result)); }
            // 未知
            return Fmt_Unknown(name, p, result);
        }

        private static string Fmt_Read(Dictionary<string, string> p, string result)
        {
            return "读取文件 " + Q(TruncPath(Arg(p, "path"))) + " → " + Q(SummarizeText(result));
        }

        private static string Fmt_ReadBetween(Dictionary<string, string> p, string result)
        {
            string str1 = Arg(p, "str1");
            string str2 = Arg(p, "str2");
            string anchors = "";
            if (str1.Length > 0) { anchors = anchors + " 「" + Trunc(str1, 15) + "」"; }
            if (str2.Length > 0) { anchors = anchors + " ~ 「" + Trunc(str2, 15) + "」"; }
            return "区间读取 " + Q(TruncPath(Arg(p, "path"))) + anchors + " → " + Q(SummarizeText(result));
        }

        private static string Fmt_ReadLines(Dictionary<string, string> p, string result)
        {
            string startStr = Arg(p, "start_line");
            string endStr = Arg(p, "end_line");
            string rangeDesc;
            if (endStr.Length > 0) { rangeDesc = "第 " + startStr + " 行到第 " + endStr + " 行"; }
            else { rangeDesc = "第 " + startStr + " 行起"; }
            return "按行读取 " + Q(FileName(Arg(p, "path"))) + " " + Q(rangeDesc) + " → " + Q(SummarizeText(result));
        }

        private static string Fmt_Write(Dictionary<string, string> p, string result)
        {
            return "写入文件 " + Q(TruncPath(Arg(p, "path"))) + " → OK";
        }

        private static string Fmt_Append(Dictionary<string, string> p, string result)
        {
            return "追加文本 " + Q(TruncPath(Arg(p, "path"))) + " → OK";
        }

        private static string Fmt_Replace(Dictionary<string, string> p, string result)
        {
            string fileName = FileName(Arg(p, "path"));
            string oldBrief = Trunc(Arg(p, "str"), 15);
            string newBrief = Trunc(Arg(p, "new_str"), 15);
            return "替换文本 " + Q(fileName) + " → " + Q(newBrief) + " 覆盖 " + Q(oldBrief);
        }

        private static string Fmt_Tree(Dictionary<string, string> p, string result)
        {
            string first = FirstLine(result);
            return "目录树 " + Q(TruncPath(Arg(p, "path"))) + " → " + Q(first.Length > 0 ? first : "（空）");
        }

        private static string Fmt_Find(Dictionary<string, string> p, string result)
        {
            string first = FirstLine(result);
            return "搜索 " + Q(TruncPath(Arg(p, "dir"))) + " → " + Q(first.Length > 0 ? first : "（无命中）");
        }

        private static string Fmt_Move(Dictionary<string, string> p, string result)
        {
            return "移动 " + Q(TruncPath(Arg(p, "src"))) + " → " + Q(TruncPath(Arg(p, "dest")));
        }

        private static string Fmt_Delete(Dictionary<string, string> p, string result)
        {
            return "软删除 " + Q(TruncPath(Arg(p, "path")));
        }

        private static string Fmt_Batch(Dictionary<string, string> p, string result)
        {
            return "批量操作 → " + Q(SummarizeText(result));
        }

        private static string Fmt_ExcelRead(Dictionary<string, string> p, string result)
        {
            return "读取表格 " + Q(TruncPath(Arg(p, "path"))) + " → " + Q(SummarizeText(result));
        }

        private static string Fmt_ExcelWrite(Dictionary<string, string> p, string result)
        {
            return "写入表格 " + Q(TruncPath(Arg(p, "path"))) + " → OK";
        }

        private static string Fmt_DocxRead(Dictionary<string, string> p, string result)
        {
            return "读取文档 " + Q(TruncPath(Arg(p, "path"))) + " → " + Q(SummarizeText(result));
        }

        private static string Fmt_DocxWrite(Dictionary<string, string> p, string result)
        {
            return "写入文档 " + Q(TruncPath(Arg(p, "path"))) + " → OK";
        }

        private static string Fmt_Shell(Dictionary<string, string> p, string result)
        {
            string cmd = Arg(p, "command");
            string cmdBrief = cmd.Length > 40 ? cmd.Substring(0, 40) + "…" : cmd;
            string first = FirstLine(result);
            return "命令执行 " + Q(cmdBrief) + " → " + Q(first.Length > 0 ? first : "（无输出）");
        }

        private static string Fmt_Unknown(string name, Dictionary<string, string> p, string result)
        {
            string first = FirstLine(result);
            return name + " → " + Q(first.Length > 0 ? first : "（无输出）");
        }

        /// <summary>结果文本智能摘要——首行优先，超长截断，空结果返回（无内容）</summary>
        private static string SummarizeText(string result)
        {
            if (string.IsNullOrEmpty(result))
            {
                return "（无内容）";
            }
            string first = FirstLine(result);
            if (first.Length > 60)
            {
                return Trunc(first, 60);
            }
            if (first.Length > 0)
            {
                return first;
            }
            return Trunc(result, 60);
        }

        /// <summary>取首行</summary>
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

        /// <summary>截断到 N 字符（尾加 …）</summary>
        private static string Trunc(string text, int max)
        {
            if (text == null || text.Length <= max)
            {
                return text ?? "";
            }
            return text.Substring(0, max) + "…";
        }

        /// <summary>取文件名</summary>
        private static string FileName(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return "";
            }
            int slash = path.LastIndexOf('/');
            int backslash = path.LastIndexOf('\\');
            int idx = slash > backslash ? slash : backslash;
            if (idx < 0)
            {
                return path;
            }
            return path.Substring(idx + 1);
        }

        /// <summary>路径截断——取文件名 + 上级目录名</summary>
        private static string TruncPath(string path)
        {
            string name = FileName(path);
            if (name.Length == 0)
            {
                return "";
            }
            if (name.Length > 40)
            {
                return Trunc(name, 40);
            }
            return name;
        }

        /// <summary>引号包裹——内部双引号替换为单引号（防干扰 RenderQuoted 分割）</summary>
        private static string Q(string s)
        {
            return "\"" + (s ?? "").Replace("\"", "'") + "\"";
        }
    }
}
// #MAU_CHECKSUM:SHA256:D4BCBB130A31395E90024F5C78B29331CBDA027FC38E4809BA7D7BFF7297C7DB
