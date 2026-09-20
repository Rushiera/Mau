using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace CH4
{
    /// <summary>
    /// 对话图片包裹条目——编号（前文条数-批内序号）与图片绝对路径。
    /// 编号供渲染角标与模型指代（「图片92-1」）；路径供 Agent 侧 image-analyze 直读。
    /// </summary>
    internal sealed class ChatImageItem
    {
        /// <summary>编号文本（如 92-1）——前文条数-批内序号</summary>
        internal string Ref;

        /// <summary>图片绝对路径（视觉服务直读——不解析受控根寻址）</summary>
        internal string Path;
    }

    /// <summary>
    /// 对话图片包裹——组装（后端在消息落前文时刻拼 [image-open]…[image-end]）与解析（前端按严格门判定渲染）。
    /// 规格：Project/CH4/design-ch4-chat-images.md §四（格式唯一权威）——本类为组装与解析的单一真相源（同一格式定义两处共用）。
    /// 纯函数——零状态零 IO；严格门：段头 + ≥1 条目 + 段尾齐备，内部无杂行，缺任一条整段按普通文本。
    /// </summary>
    internal static class ChatImageEnvelope
    {
        /// <summary>段头——独占一行，大小写固定</summary>
        internal const string OpenTag = "[image-open]";

        /// <summary>段尾——独占一行，大小写固定</summary>
        internal const string EndTag = "[image-end]";

        /// <summary>指令前缀——Chat 指令行的固定头部（组装时插在正文之前、前缀之后）</summary>
        private const string ChatPrefix = "Chat ";

        /// <summary>条目行正则——图片&lt;前文条数&gt;-&lt;批内序号&gt;：&lt;绝对路径&gt;（全角冒号；路径取到行尾，可含空格）</summary>
        private static readonly Regex _itemRegex = new Regex("^图片(\\d+)-(\\d+)：(.+)$", RegexOptions.Compiled);

        /// <summary>
        /// 组装包裹 + 正文——无有效路径时原样返回正文（零回归）。
        /// </summary>
        /// <param name="images">图片绝对路径列表（可空）</param>
        /// <param name="contextCount">组装时刻的前文条数（编号前缀）</param>
        /// <param name="body">用户正文（可空）</param>
        /// <returns>完整消息文本（包裹 + 空行 + 正文；无图片 = 原正文）</returns>
        internal static string Build(IList<string> images, int contextCount, string body)
        {
            List<string> paths = Normalize(images);
            if (paths.Count == 0)
            {
                if (body == null)
                {
                    return "";
                }
                return body;
            }
            // [段1] 包裹本体——段头 + 条目 + 段尾（每行独占）
            StringBuilder sb = new StringBuilder();
            sb.Append(OpenTag);
            for (int i = 0; i < paths.Count; i = i + 1)
            {
                sb.Append("\n");
                sb.Append(BuildItemLine(contextCount, i + 1, paths[i]));
            }
            sb.Append("\n");
            sb.Append(EndTag);
            // [段2] 正文——空行分隔；无正文不留多余空行
            if (body != null && body.Length > 0)
            {
                sb.Append("\n\n");
                sb.Append(body);
            }
            return sb.ToString();
        }

        /// <summary>
        /// 指令行套用——`Chat &lt;正文&gt;` → `Chat &lt;包裹+正文&gt;`；非 Chat 行 / 无图片原样返回（零回归）。
        /// 单一出口：Chat 前缀约定只在本函数处理，调用面（宿主）不感知。
        /// </summary>
        /// <param name="line">指令行（如 "Chat 看这个"）</param>
        /// <param name="images">图片绝对路径列表（可空）</param>
        /// <param name="contextCount">组装时刻的前文条数</param>
        /// <returns>套用后的指令行（不成立时原样）</returns>
        internal static string ApplyToLine(string line, IList<string> images, int contextCount)
        {
            if (line == null || line.Length == 0)
            {
                return line;
            }
            if (images == null || images.Count == 0)
            {
                return line;
            }
            if (!line.StartsWith(ChatPrefix, StringComparison.Ordinal))
            {
                return line;
            }
            string body = line.Substring(ChatPrefix.Length);
            return ChatPrefix + Build(images, contextCount, body);
        }

        /// <summary>
        /// 解析包裹——严格门：段头 + ≥1 条目 + 段尾齐备，内部无杂行。
        /// 不成立时：items 空列表、body = 原文（调用方按普通文本处理）。
        /// </summary>
        /// <param name="text">消息文本（可空）</param>
        /// <param name="items">出参——条目列表（按出现顺序）</param>
        /// <param name="body">出参——去掉包裹后的正文（无包裹 = 原文 Trim）</param>
        /// <returns>true=命中合法包裹</returns>
        internal static bool TryParse(string text, out List<ChatImageItem> items, out string body)
        {
            items = new List<ChatImageItem>();
            if (text == null || text.Length == 0)
            {
                body = "";
                return false;
            }
            body = text.Trim();
            string[] lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            // [段1] 段头定位——跳过前导空行（容错读旧数据；组装侧不留空行）
            int i = 0;
            while (i < lines.Length && lines[i].Trim().Length == 0)
            {
                i = i + 1;
            }
            if (i >= lines.Length || lines[i].Trim() != OpenTag)
            {
                return false;
            }
            i = i + 1;
            // [段2] 条目行——≥1；段尾之前不得出现空行或非条目行（严格门）
            // 局部收集——任一判定失败即整段判不成立，不留下半截条目（调用方 items 恒空）
            List<ChatImageItem> found = new List<ChatImageItem>();
            while (i < lines.Length)
            {
                string t = lines[i].Trim();
                if (t == EndTag)
                {
                    break;
                }
                if (t.Length == 0)
                {
                    return false;
                }
                Match m = _itemRegex.Match(t);
                if (!m.Success)
                {
                    return false;
                }
                ChatImageItem item = new ChatImageItem();
                item.Ref = m.Groups[1].Value + "-" + m.Groups[2].Value;
                item.Path = m.Groups[3].Value.Trim();
                found.Add(item);
                i = i + 1;
            }
            if (found.Count == 0 || i >= lines.Length)
            {
                return false;
            }
            i = i + 1;
            // [段3] 正文——段尾之后全部内容（前导空行分隔被 Trim 吸收）
            StringBuilder sb = new StringBuilder();
            while (i < lines.Length)
            {
                if (sb.Length > 0)
                {
                    sb.Append("\n");
                }
                sb.Append(lines[i]);
                i = i + 1;
            }
            items = found;
            body = sb.ToString().Trim();
            return true;
        }

        /// <summary>
        /// 条目行构造——图片&lt;前文条数&gt;-&lt;序号&gt;：&lt;路径&gt;（组装与解析共用的格式唯一产出点）。
        /// </summary>
        /// <param name="contextCount">前文条数</param>
        /// <param name="index">批内序号（1 起）</param>
        /// <param name="path">图片绝对路径</param>
        /// <returns>条目行文本</returns>
        internal static string BuildItemLine(int contextCount, int index, string path)
        {
            return "图片" + contextCount.ToString() + "-" + index.ToString() + "：" + path;
        }

        /// <summary>
        /// 路径列表归一——Trim + 丢空项（顺序保持）。
        /// </summary>
        /// <param name="images">原始列表（可空）</param>
        /// <returns>归一后的路径列表（可直接入包裹）</returns>
        private static List<string> Normalize(IList<string> images)
        {
            List<string> paths = new List<string>();
            if (images == null)
            {
                return paths;
            }
            for (int i = 0; i < images.Count; i = i + 1)
            {
                string p = images[i];
                if (p == null)
                {
                    continue;
                }
                p = p.Trim();
                if (p.Length == 0)
                {
                    continue;
                }
                paths.Add(p);
            }
            return paths;
        }
    }
}
