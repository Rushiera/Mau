// ═══════════════════════════════════════════════════
// 积木: web.fetch.jobs
// ID:   BRIK-WEB-003
// 类别: WEB
// 作用: 下载任务管理——list（全部任务）/ status（单任务详情）/ cancel（取消进行中任务）
// 依赖: 无
// 引用: Mau.Runtime（IWebFetchService/DataBox）
// 原理: DataBox.TryResolve<IWebFetchService> → Jobs(action, id)；全局任务表查询与取消
// 常用: web_cat.mau 认领线——'web.fetch.jobs'[@args] > @result
// 门禁: 不域限定（同 web.fetch）
// ═══════════════════════════════════════════════════
using System;
using System.Text.Json;
using Mau.Runtime;

namespace Mau.Bricks
{
    /// <summary>
    /// 下载任务管理积木——web-fetch-jobs 工具执行面（全局单一下载器管理面）
    /// </summary>
    public static class WebFetchJobsBrick
    {
        /// <summary>
        /// 任务管理——list / status / cancel
        /// </summary>
        /// <param name="argsJson">工具参数 JSON（action / id；catId 保留键由宿主注入）</param>
        /// <param name="result">清单 / 详情 / 动作结果或 ERR| 错误文本</param>
        /// <returns>true=执行成功</returns>
        public static bool Jobs(string argsJson, out string result)
        {
            result = "";
            string badArgs = JsonArgs.Validate(argsJson, "action id", "", "action", "list|status|cancel");
            if (badArgs.Length > 0)
            {
                result = badArgs;
                return false;
            }
            string action = JsonArgs.Get(argsJson, "action");
            string id = JsonArgs.Get(argsJson, "id");
            try
            {
                IWebFetchService? service;
                DataBox.TryResolve<IWebFetchService>(out service);
                if (service == null)
                {
                    result = "ERR|DOWNLOAD_NO_SERVICE|宿主未注入 IWebFetchService";
                    return false;
                }
                string body = service.Jobs(action, id);
                if (body.StartsWith("ERR|", StringComparison.Ordinal))
                {
                    result = body;
                    return true;
                }
                System.Collections.Generic.Dictionary<string, object> fields = new System.Collections.Generic.Dictionary<string, object>();
                fields["chars"] = body.Length;
                result = MetaHead("web-fetch-jobs", true, action, -1, fields) + "\n" + body;
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
// #MAU_CHECKSUM:SHA256:1C63EE255B829C25C490DE19A9AB0CB2BB6B1061673B329E19442AD0114BB6C6
