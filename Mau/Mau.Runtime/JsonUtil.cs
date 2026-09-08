using System.Text.Encodings.Web;
using System.Text.Json;

namespace Mau.Runtime
{
    /// <summary>
    /// 统一 JSON 序列化入口——中文直显（UnsafeRelaxedJsonEscaping：非 ASCII 不转义；HTML 敏感字符亦不转义——前端渲染走 textContent/escapeHtml，无注入面）。
    /// 全局替换 JsonSerializer.Serialize 裸调用（2026-09-08：cfg/json/SSE 中文 \uXXXX 转义问题——Q2 全局治本）。
    /// 特例：需要自定义 options 的调用（IncludeFields 等）保持 JsonSerializer 原样。
    /// </summary>
    public static class JsonUtil
    {
        /// <summary>共享序列化选项——中文直显（其余默认）</summary>
        private static readonly JsonSerializerOptions Options = new JsonSerializerOptions
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        /// <summary>
        /// 序列化为 JSON 字符串——中文直显
        /// </summary>
        /// <param name="value">待序列化对象</param>
        /// <returns>JSON 文本</returns>
        public static string Serialize<T>(T value)
        {
            return JsonSerializer.Serialize(value, Options);
        }
    }
}
