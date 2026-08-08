using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Mau.Translator
{
    /// <summary>
    /// 积木内嵌器——BRIKGROUP 段构建：闭包计算 → 文本库读取 → 校验尾验证 → 类名重命名 → 拼接。
    /// 顶层设计落地：积木是文本语料，翻译器产物必须完备（内敛）——生成物自包含所用积木源码。
    /// </summary>
    public static class BrickEmbedder
    {
        /// <summary>
        /// 收集语料动作闭包——动作积木 + 依赖递归（去重按 BRIK-ID，环防护）
        /// </summary>
        /// <param name="doc">解析文档</param>
        /// <returns>闭包条目（按 ID 稳定排序）</returns>
        public static List<BrickIndexEntry> CollectClosure(MauDocument doc)
        {
            Dictionary<string, BrickIndexEntry> result =
                new Dictionary<string, BrickIndexEntry>(StringComparer.Ordinal);
            HashSet<string> visiting = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < doc.Transitions.Count; i++)
            {
                IrTransition t = doc.Transitions[i];
                if (t.BrickName.Length == 0)
                {
                    continue;
                }
                CollectEntry(t.BrickName, result, visiting);
            }
            List<BrickIndexEntry> sorted = new List<BrickIndexEntry>(result.Values);
            sorted.Sort(delegate (BrickIndexEntry a, BrickIndexEntry b)
            {
                return string.CompareOrdinal(a.Id, b.Id);
            });
            return sorted;
        }

        /// <summary>
        /// 递归收集单条积木及其依赖
        /// </summary>
        /// <param name="name">积木名</param>
        /// <param name="result">收集结果（按 ID）</param>
        /// <param name="visiting">环防护集合</param>
        private static void CollectEntry(string name,
            Dictionary<string, BrickIndexEntry> result, HashSet<string> visiting)
        {
            BrickIndexEntry entry;
            if (!BrickIndex.TryGet(name, out entry))
            {
                return;
            }
            if (result.ContainsKey(entry.Id))
            {
                return;
            }
            if (!visiting.Add(entry.Id))
            {
                return; // 环——依赖声明错误，静默截断（编译期由验证器报）
            }
            for (int i = 0; i < entry.Dependencies.Count; i++)
            {
                string dep = entry.Dependencies[i];
                if (dep.EndsWith(".cs", StringComparison.Ordinal))
                {
                    continue; // 支撑文件——基座提供（SUPPORT 过渡期），不内嵌
                }
                CollectEntry(dep, result, visiting);
            }
            visiting.Remove(entry.Id);
            result[entry.Id] = entry;
        }

        /// <summary>
        /// 构建内嵌段——闭包全部源码 → 校验 → 类名重命名 → 拼接
        /// </summary>
        /// <param name="closure">闭包条目</param>
        /// <param name="error">失败原因（空=成功）</param>
        /// <returns>内嵌段源码（空=失败）</returns>
        public static string BuildEmbeddedSection(List<BrickIndexEntry> closure, out string error)
        {
            error = "";
            if (closure.Count == 0)
            {
                return "";
            }
            // [段1] 读取 + 验证 + 建立类名映射（旧类名 → BRIK-ID 类名）
            Dictionary<string, string> classNameMap =
                new Dictionary<string, string>(StringComparer.Ordinal);
            List<string> rawSources = new List<string>();
            for (int i = 0; i < closure.Count; i++)
            {
                BrickIndexEntry entry = closure[i];
                string source;
                if (!ReadAndVerify(entry, out source, out error))
                {
                    return "";
                }
                string oldClass = ExtractClassName(source);
                if (oldClass.Length == 0)
                {
                    error = "内嵌源码缺少 public static class 声明: " + entry.Name;
                    return "";
                }
                classNameMap[oldClass] = BrickIndex.IdClassName(entry.Id);
                rawSources.Add(source);
            }
            // [段2] 逐文件剥离包装 + 全映射替换 + 段标记
            Dictionary<string, string> usings = new Dictionary<string, string>(StringComparer.Ordinal);
            StringBuilder body = new StringBuilder();
            for (int i = 0; i < closure.Count; i++)
            {
                string cleaned = StripWrapper(rawSources[i], usings);
                if (cleaned.Length == 0)
                {
                    error = "内嵌源码剥离后为空: " + closure[i].Name;
                    return "";
                }
                cleaned = ReplaceAllWords(cleaned, classNameMap);
                body.Append("    // #BRICK:");
                body.Append(closure[i].Id);
                body.Append(" BEGIN\n");
                body.Append(cleaned);
                body.Append("\n    // #BRICK:");
                body.Append(closure[i].Id);
                body.Append(" END\n");
            }
            // [段3] 拼接内嵌段——using 合并 + 各积木源码保留自身 namespace 块（不包外壳，防嵌套）
            StringBuilder section = new StringBuilder();
            section.Append("// ═══ 内嵌积木源码（BRIKGROUP——Bricks 文本库拼接，非人工维护）═══\n");
            foreach (string u in usings.Values)
            {
                section.Append(u);
                section.Append('\n');
            }
            section.Append(body.ToString());
            return section.ToString();
        }

        /// <summary>
        /// 读取并验证积木文件——文件头「积木」字段匹配 + 校验尾 SHA256 验证
        /// </summary>
        /// <param name="entry">索引条目</param>
        /// <param name="source">验证通过的源码（已去校验尾，换行归一化）</param>
        /// <param name="error">失败原因</param>
        /// <returns>验证通过</returns>
        private static bool ReadAndVerify(BrickIndexEntry entry, out string source, out string error)
        {
            source = "";
            error = "";
            string path = BrickIndex.ResolveSourcePath(entry);
            string full;
            try
            {
                full = File.ReadAllText(path);
            }
            catch (Exception ex)
            {
                error = "积木文件不可读 " + entry.Name + ": " + ex.Message;
                return false;
            }
            full = full.Replace("\r\n", "\n");
            // 校验尾提取
            string[] lines = full.Split('\n');
            int checksumIdx = -1;
            for (int i = lines.Length - 1; i >= 0; i--)
            {
                string trimmed = lines[i].Trim();
                if (trimmed.Length == 0)
                {
                    continue;
                }
                if (trimmed.StartsWith("// #MAU_CHECKSUM:SHA256:", StringComparison.Ordinal))
                {
                    checksumIdx = i;
                }
                break;
            }
            if (checksumIdx < 0)
            {
                error = "积木文件缺少校验尾: " + entry.Name + "（修复: mau bricks reseal）";
                return false;
            }
            string claimed = lines[checksumIdx].Trim().Substring(24).Trim();
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < checksumIdx; i++)
            {
                if (i > 0)
                {
                    sb.Append('\n');
                }
                sb.Append(lines[i]);
            }
            string body = sb.ToString().TrimEnd('\n');
            string computed = ComputeSha256(body);
            if (!string.Equals(computed, claimed, StringComparison.OrdinalIgnoreCase))
            {
                error = "积木文件校验失败: " + entry.Name + "——文本已变更，请执行 mau bricks reseal 重算校验尾";
                return false;
            }
            source = body;
            return true;
        }

        /// <summary>
        /// 提取源码中的静态类名——public static class XxxBrick
        /// </summary>
        /// <param name="source">已验证源码</param>
        /// <returns>类名（未找到为空）</returns>
        private static string ExtractClassName(string source)
        {
            string[] lines = source.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                string trimmed = lines[i].Trim();
                if (trimmed.StartsWith("public static class ", StringComparison.Ordinal))
                {
                    int nameStart = "public static class ".Length;
                    int nameEnd = trimmed.IndexOf(' ', nameStart);
                    if (nameEnd < 0)
                    {
                        nameEnd = trimmed.Length;
                    }
                    return trimmed.Substring(nameStart, nameEnd - nameStart);
                }
            }
            return "";
        }

        /// <summary>
        /// 剥离包装——文件头注释块 + using 行 + 校验尾；返回类体源码（保留缩进）
        /// </summary>
        /// <param name="source">已验证源码（无校验尾）</param>
        /// <param name="usings">using 收集器（合并去重）</param>
        /// <returns>类体源码</returns>
        private static string StripWrapper(string source, Dictionary<string, string> usings)
        {
            string[] lines = source.Split('\n');
            StringBuilder body = new StringBuilder();
            bool inHeader = false;
            bool bodyStarted = false;
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                string trimmed = line.Trim();
                if (!bodyStarted)
                {
                    if (trimmed.StartsWith("// ═══", StringComparison.Ordinal))
                    {
                        inHeader = !inHeader;
                        continue;
                    }
                    if (inHeader)
                    {
                        continue;
                    }
                    if (trimmed.StartsWith("using ", StringComparison.Ordinal))
                    {
                        usings[trimmed] = trimmed;
                        continue;
                    }
                    if (trimmed.Length == 0)
                    {
                        continue;
                    }
                    bodyStarted = true;
                }
                body.Append(line);
                body.Append('\n');
            }
            return body.ToString();
        }

        /// <summary>
        /// 全映射单词替换——闭包类名统一替换为 BRIK-ID 类名
        /// </summary>
        /// <param name="text">类体源码</param>
        /// <param name="map">旧类名 → 新类名</param>
        /// <returns>替换结果</returns>
        private static string ReplaceAllWords(string text, Dictionary<string, string> map)
        {
            string result = text;
            foreach (KeyValuePair<string, string> pair in map)
            {
                if (pair.Key.Length > 0 && !string.Equals(pair.Key, pair.Value, StringComparison.Ordinal))
                {
                    result = ReplaceWord(result, pair.Key, pair.Value);
                }
            }
            return result;
        }

        /// <summary>
        /// 单词边界替换
        /// </summary>
        /// <param name="text">原文</param>
        /// <param name="oldWord">旧词</param>
        /// <param name="newWord">新词</param>
        /// <returns>替换结果</returns>
        private static string ReplaceWord(string text, string oldWord, string newWord)
        {
            StringBuilder sb = new StringBuilder();
            int index = 0;
            while (index < text.Length)
            {
                int found = text.IndexOf(oldWord, index, StringComparison.Ordinal);
                if (found < 0)
                {
                    sb.Append(text, index, text.Length - index);
                    break;
                }
                bool leftOk = found == 0
                    || !IsWordChar(text[found - 1]);
                int end = found + oldWord.Length;
                bool rightOk = end >= text.Length
                    || !IsWordChar(text[end]);
                if (leftOk && rightOk)
                {
                    sb.Append(text, index, found - index);
                    sb.Append(newWord);
                }
                else
                {
                    sb.Append(text, index, end - index);
                }
                index = end;
            }
            return sb.ToString();
        }

        /// <summary>
        /// 单词字符判断
        /// </summary>
        /// <param name="c">字符</param>
        /// <returns>字母数字下划线</returns>
        private static bool IsWordChar(char c)
        {
            return (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z')
                || (c >= '0' && c <= '9') || c == '_';
        }

        /// <summary>
        /// 计算 SHA256——UTF-8 字节转 64 位十六进制大写（与黄金/校验尾同规）
        /// </summary>
        /// <param name="text">输入文本</param>
        /// <returns>64 位十六进制哈希（大写）</returns>
        private static string ComputeSha256(string text)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(text);
            byte[] hash = SHA256.HashData(bytes);
            StringBuilder hex = new StringBuilder();
            for (int i = 0; i < hash.Length; i++)
            {
                hex.Append(hash[i].ToString("X2"));
            }
            return hex.ToString();
        }
    }
}
