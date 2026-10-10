// ═══════════════════════════════════════════════════
// 积木: web.fetch
// ID:   BRIK-WEB-002
// 类别: WEB
// 作用: 网页资产下载——推入下载任务（登记即返回，非阻塞；完成 / 失败走系统注入）
// 依赖: 无
// 引用: Mau.Runtime（IWebFetchService/DataBox）
// 原理: DataBox.TryResolve<IWebFetchService> → Fetch(catId, url, fileName)；全局单一下载器（任务表全猫共用）
// 常用: web_cat.mau 认领线——'web.fetch'[@args] > @result
// 门禁: 不域限定（产物落盘、不占前文——对照 browser-* 的域内限定）
// ═══════════════════════════════════════════════════
using System;
using System.Text.Json;
using Mau.Runtime;

namespace Mau.Bricks
{
    /// <summary>
    /// 网页资产下载积木——web-fetch 工具执行面（全局单一下载器推入面）
    /// </summary>
    public static class WebFetchBrick
    {
        /// <summary>
        /// 推入下载任务
        /// </summary>
        /// <param name="argsJson">工具参数 JSON（url / file_name；catId 保留键由宿主注入）</param>
        /// <param name="result">回执或 ERR| 错误文本</param>
        /// <returns>true=执行成功</returns>
        public static bool Fetch(string argsJson, out string result)
        {
            result = "";
            string badArgs = JsonArgs.Validate(argsJson, "url file_name", "url", "", "");
            if (badArgs.Length > 0)
            {
                result = badArgs;
                return false;
            }
            string catId = JsonArgs.Get(argsJson, "catId");
            string url = JsonArgs.Get(argsJson, "url");
            string fileName = JsonArgs.Get(argsJson, "file_name");
            try
            {
                IWebFetchService? service;
                DataBox.TryResolve<IWebFetchService>(out service);
                if (service == null)
                {
                    result = "ERR|DOWNLOAD_NO_SERVICE|宿主未注入 IWebFetchService";
                    return false;
                }
                string body = service.Fetch(catId, url, fileName);
                if (body.StartsWith("ERR|", StringComparison.Ordinal))
                {
                    result = body;
                    return true;
                }
                System.Collections.Generic.Dictionary<string, object> fields = new System.Collections.Generic.Dictionary<string, object>();
                fields["chars"] = body.Length;
                result = MetaHead("web-fetch", true, url, -1, fields) + "\n" + body;
                return true;
            }
            catch (Exception ex)
            {
                result = "ERR|DOWNLOAD_BRICK|" + ex.GetType().Name + ": " + ex.Message;
                return false;
            }
        }

        /// <summary>
        /// 结构化元数据头（统一口径·A214）——恒定 ok / tool + 主来源 target + 主计数 items + 专有字段（插入序）。
        /// </summary>
        /// <param name="tool">工具名</param>
        /// <param name="ok">成败</param>
        /// <param name="target">主来源（空串 = 省略）</param>
        /// <param name="items">主计数（负值 = 省略）</param>
        /// <param name="fields">专有字段（按插入序）</param>
        /// <returns>单行 JSON</returns>
        private static string MetaHead(string tool, bool ok, string target, int items, System.Collections.Generic.Dictionary<string, object> fields)
        {
            System.Collections.Generic.Dictionary<string, object> head = new System.Collections.Generic.Dictionary<string, object>();
            head["ok"] = ok;
            head["tool"] = tool;
            if (target.Length > 0)
            {
                head["target"] = target;
            }
            if (items >= 0)
            {
                head["items"] = items;
            }
            foreach (System.Collections.Generic.KeyValuePair<string, object> kv in fields)
            {
                head[kv.Key] = kv.Value;
            }
            return JsonSerializer.Serialize(head);
        }
    }
}
// #MAU_CHECKSUM:SHA256:D3B235A7E7B7EFA08DC1F4A53E9C712A737618C1C2917DFDC44902DFF3A47B9B
