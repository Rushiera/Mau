// ═══════════════════════════════════════════════════
// 积木: vision.analyze
// ID:   BRIK-VISION-001
// 类别: VISION
// 作用: 图像识别——读取图片（本地路径 base64 内联 / 外部 URL 直传）→ 视觉模型分析 → 返回基于提示词的文本
// 依赖: 无
// 引用: Mau.Runtime（IVisionService/DataBox）
// 原理: DataBox.TryResolve<IVisionService> → Analyze(path, question)；argsJson 内解析（语料零 JSON 解析）；
//       配置 vision.api_config_id 未配置 → ERR|API_NOT_CONFIGURED（先配置后才可用）
// 常用: vision_cat.mau 认领线——'vision.analyze'[@args] > @result
// ═══════════════════════════════════════════════════
using System;
using System.Text.Json;
using Mau.Runtime;

namespace Mau.Bricks
{
    /// <summary>
    /// 识别积木——image-analyze 图像识别（LLM 工具执行面：参数整包 argsJson；同步执行——R2.2 拍板走等待）
    /// </summary>
    public static class VisionAnalyzeBrick
    {
        /// <summary>
        /// 执行图像识别——读取图片 + 视觉模型分析，返回基于提示词的文本
        /// </summary>
        /// <param name="argsJson">工具参数 JSON（path/question）</param>
        /// <param name="result">分析结果或 ERR| 错误文本</param>
        /// <returns>true=执行成功</returns>
        public static bool Analyze(string argsJson, out string result)
        {
            result = "";
            string path = ExtractArg(argsJson, "path");
            if (path.Length == 0)
            {
                result = "ERR|BAD_ARGS|缺少参数 path";
                return false;
            }
            string question = ExtractArg(argsJson, "question");
            try
            {
                IVisionService? service;
                DataBox.TryResolve<IVisionService>(out service);
                if (service == null)
                {
                    result = "ERR|VISION_NO_SERVICE|宿主未注入 IVisionService";
                    return false;
                }
                result = service.Analyze(path, question);
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
// #MAU_CHECKSUM:SHA256:E007DD8B2BBF2DAEF8FB1D590AB8CDBFE1BC6513FEFFEB36C36E60601B88E3BB
