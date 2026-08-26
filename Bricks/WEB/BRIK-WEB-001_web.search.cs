// ═══════════════════════════════════════════════════
// 积木: web.search
// ID:   BRIK-WEB-001
// 类别: WEB
// 作用: 联网搜索——DeepSeek Responses API + web_search 内置工具（服务端自动执行全链）；返回模型最终回答
// 依赖: 无
// 引用: Mau.Runtime（IWebSearchService/DataBox）
// 原理: DataBox.TryResolve<IWebSearchService> → Search(query)；argsJson 内解析（语料零 JSON 解析）；
//       配置 search.api_config_id 未配置 → ERR|API_NOT_CONFIGURED（先配置后才可用）
// 常用: search_cat.mau 认领线——'web.search'[@args] > @result
// ═══════════════════════════════════════════════════
using System;
using System.Text.Json;
using Mau.Runtime;

namespace Mau.Bricks
{
    /// <summary>
    /// 搜索积木——web-search 联网搜索（LLM 工具执行面：参数整包 argsJson；同步执行——R2.1 拍板走等待）
    /// </summary>
    public static class WebSearchBrick
    {
        /// <summary>
        /// 执行联网搜索——服务端自动完成"搜索→注入→生成回答"全链，返回最终回答文本
        /// </summary>
        /// <param name="argsJson">工具参数 JSON（query）</param>
        /// <param name="result">最终回答或 ERR| 错误文本</param>
        /// <returns>true=执行成功</returns>
        public static bool Search(string argsJson, out string result)
        {
            result = "";
            string query = ExtractArg(argsJson, "query");
            if (query.Length == 0)
            {
                result = "ERR|BAD_ARGS|缺少参数 query";
                return false;
            }
            try
            {
                IWebSearchService? service;
                DataBox.TryResolve<IWebSearchService>(out service);
                if (service == null)
                {
                    result = "ERR|WEB_NO_SERVICE|宿主未注入 IWebSearchService";
                    return false;
                }
                result = service.Search(query);
                return true;
            }
            catch (Exception ex)
            {
                result = "ERR|" + ex.GetType().Name + "|" + ex.Message;
                return false;
            }
        }

        /// <summary>
        /// 展平参数提取——argsJson 中取字符串值（不存在返回空串）
        /// </summary>
        /// <param name="argumentsJson">参数 JSON</param>
        /// <param name="key">参数名</param>
        /// <returns>参数值</returns>
        private static string ExtractArg(string argumentsJson, string key)
        {
            try
            {
                JsonDocument doc = JsonDocument.Parse(argumentsJson);
                try
                {
                    if (doc.RootElement.TryGetProperty(key, out JsonElement el))
                    {
                        if (el.ValueKind == JsonValueKind.String)
                        {
                            return el.GetString() ?? "";
                        }
                        return el.GetRawText();
                    }
                }
                finally
                {
                    doc.Dispose();
                }
            }
            catch (Exception)
            {
            }
            return "";
        }
    }
}
// #MAU_CHECKSUM:SHA256:1C1EF314E4C3B3A8824FF3BA8DF5788CB4F2878D5633F010FAAD8B59271018AC
