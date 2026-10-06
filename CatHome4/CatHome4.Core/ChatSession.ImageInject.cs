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
            StringBuilder notice = new StringBuilder();
            notice.Append("（系统自动 · 图片插入）以下图片已插入主干，可直接查看：");
            for (int i = 0; i < paths.Count; i = i + 1)
            {
                notice.Append("\n");
                notice.Append(paths[i]);
            }
            string imagesJson = JsonUtil.Array(paths.ToArray());
            string text = notice.ToString();

            // [段2] 注入消息——经系统注入口入队（等价于系统输入一行；附件随条目携带，批后段同帧消费）
            string posted = PostSystemMessage(SysKindSystemAuto, text, imagesJson);
            if (posted.StartsWith("ERR|", StringComparison.Ordinal))
            {
                LogStore.Add("CatHome4", 2, "图片注入未受理: " + posted, "IMAGE");
                return;
            }
            LogStore.Add("CatHome4", 1, "图片注入：本批 " + paths.Count.ToString() + " 张（首张 " + paths[0] + "）", "IMAGE");
            NoteTimebackEvent();
        }

        /// <summary>
        /// 提取 image-inject 工具参数中的图片路径——参数面仅 path（design-ch4-chat-images §8.5-2）；解析失败或缺失 = 空串。
        /// 提取后经根寻址规范化（A108）——`&lt;rootId&gt;:&lt;rel&gt;` → 绝对路径：引用数组落绝对路径，
        /// 展开面（请求构造）/ 渲染面 / 归档面全链同源。
        /// </summary>
        /// <param name="argsJson">工具参数 JSON</param>
        /// <returns>图片路径（空=无）</returns>
        private string ExtractImageInjectPath(string argsJson)
        {
            if (argsJson == null || argsJson.Length == 0)
            {
                return "";
            }
            try
            {
                using (JsonDocument doc = JsonUtil.ParseStrict(argsJson))
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
                    return NormalizeImagePath(path.Trim());
                }
            }
            catch (Exception)
            {
                return "";
            }
        }

        /// <summary>
        /// 根寻址规范化（A108 · 2026-09-29）——`&lt;rootId&gt;:&lt;rel&gt;` 形态解析为绝对路径：
        /// 复用 `Mau.Runtime.FileSystemService.Resolve`（受控根 id 映射 + 越界拒绝 + 猫级白名单），
        /// 与 text-* / cs-* 寻址同一实现（积木侧「通用解码接口」同源）。
        /// 非根寻址（无冒号 / http(s) URL）与解析失败（越界 / 未知根 id / 无可用根表）一律原样返回——
        /// 失败照旧在下游可见（展开面报 `ERR|IMAGE_NOT_FOUND`，不静默改写成别的路径）。
        /// </summary>
        /// <param name="path">工具参数中的路径</param>
        /// <returns>绝对路径或原串</returns>
        private string NormalizeImagePath(string path)
        {
            if (path.Length == 0)
            {
                return path;
            }
            // 外部 URL 直传——不经根解析
            if (path.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || path.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                return path;
            }
            // 冒号位置 > 0 才可能是命名空间寻址（盘符 `C:\…` 由 Resolve 内部按「无匹配 id」原样回落）
            if (path.IndexOf(':') <= 0)
            {
                return path;
            }
            FileSystemService fs = ToolCatContext.ResolveCatFileSystem(_catKey);
            if (fs == null)
            {
                DataBox.TryResolve<FileSystemService>(out fs);
            }
            if (fs == null)
            {
                return path;
            }
            try
            {
                string full = fs.Resolve(path, false);
                if (full != null && full.Length > 0)
                {
                    return full;
                }
            }
            catch (Exception ex)
            {
                // 越界 / 未知根 id / 解析异常——原样透传（失败可见性归下游）
                LogStore.Add("ChatSession", 2, "图片路径解析失败，原样透传: " + ex.Message, "SYS");
            }
            return path;
        }
    }
}
