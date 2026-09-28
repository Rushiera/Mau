using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using Mau.Runtime;

namespace CH4
{
    /// <summary>
    /// 宿主会话实体——图片注入分部（design-ch4-chat-images §八 · A105 图片真实插入主干）。
    /// 形态：工具（image-inject）批后段把本批登记的图片引用合并为**一条 user 注入消息**（占位 + 接力——真图由请求构造面展开为内容块）。
    /// 约束：只追加不改写（C1 缓存纪律）；注入消息落在 timeback 作用域区间内（back 回收时与查证过程一并删除，图不常驻主干）。
    /// 门禁：工具侧仅作用域内可用（ChatSession.cs 工具登记段拦截）——本分部只负责批后段的注入动作。
    /// </summary>
    internal sealed partial class ChatSession
    {
        /// <summary>
        /// 图片注入——批后段调用：收集本批 image-inject 工具的成功调用，合并为一条 user 注入消息（引用数组随消息落盘）。
        /// 无成功调用 = 零动作；同轮多次调用合并为一条（避免连续 user 噪声）。
        /// 可测面：dogs 显式传入（免起 OA 闭环）；不传 = 当前批 _dogs。
        /// </summary>
        /// <param name="dogs">本批工单列表（null=当前批）</param>
        internal void FlushImageInjections(List<ToolOrderDog> dogs = null)
        {
            List<ToolOrderDog> source = dogs;
            if (source == null)
            {
                source = _dogs;
            }
            List<string> paths = new List<string>();
            for (int i = 0; i < source.Count; i = i + 1)
            {
                ToolOrderDog dog = source[i];
                if (dog.Name != "image-inject")
                {
                    continue;
                }
                if (dog.Result == null || dog.Result.StartsWith("ERR|", StringComparison.Ordinal))
                {
                    continue;
                }
                string path = ExtractImageInjectPath(dog.ArgsJson);
                if (path.Length > 0)
                {
                    paths.Add(path);
                }
            }
            if (paths.Count == 0)
            {
                return;
            }

            // [段1] 引用数组 + 注入文本——引用形态 = 图片绝对路径（请求构造面展开为内容块）
            StringBuilder imagesJson = new StringBuilder();
            imagesJson.Append("[");
            StringBuilder notice = new StringBuilder();
            notice.Append("（系统自动 · 图片插入）以下图片已插入主干，可直接查看：");
            for (int i = 0; i < paths.Count; i = i + 1)
            {
                if (i > 0)
                {
                    imagesJson.Append(",");
                }
                imagesJson.Append(JsonUtil.Serialize(paths[i]));
                notice.Append("\n");
                notice.Append(paths[i]);
            }
            imagesJson.Append("]");
            string text = notice.ToString();

            // [段2] 注入消息——只追加不改写；视图层走 user 系统通道（source=systemauto）
            LlmMessage? injected = _context.AddUserMessage(text, imagesJson.ToString());
            if (injected == null)
            {
                return;
            }
            AppendMessage(injected.Value);
            _viewStore.OnUserMessage(LastMessage(), ViewTimestamp(), _context.GetMessageCount() - 1);
            if (_httpHost != null)
            {
                string userJson = "{\"content\":" + JsonUtil.Serialize(text) + ",\"source\":\"systemauto\"}";
                _httpHost.PushView("user", userJson, -1, 0);
            }
            LogStore.Add("CatHome4", 1, "图片注入：本批 " + paths.Count.ToString() + " 张（首张 " + paths[0] + "）", "IMAGE");
            NoteTimebackEvent();
        }

        /// <summary>
        /// 提取 image-inject 工具参数中的图片路径——参数面仅 path（design-ch4-chat-images §8.5-2）；解析失败或缺失 = 空串。
        /// </summary>
        /// <param name="argsJson">工具参数 JSON</param>
        /// <returns>图片路径（空=无）</returns>
        private static string ExtractImageInjectPath(string argsJson)
        {
            if (argsJson == null || argsJson.Length == 0)
            {
                return "";
            }
            try
            {
                using (JsonDocument doc = JsonDocument.Parse(argsJson))
                {
                    JsonElement root = doc.RootElement;
                    if (root.ValueKind != JsonValueKind.Object)
                    {
                        return "";
                    }
                    JsonElement pathEl;
                    if (!root.TryGetProperty("path", out pathEl) || pathEl.ValueKind != JsonValueKind.String)
                    {
                        return "";
                    }
                    string path = pathEl.GetString();
                    if (path == null)
                    {
                        return "";
                    }
                    return path.Trim();
                }
            }
            catch (Exception)
            {
                return "";
            }
        }
    }
}
