// ═══════════════════════════════════════════════════
// 积木: vision.analyze
// ID:   BRIK-VISION-001
// 类别: VISION
// 作用: 图像识别——读取图片（本地路径 base64 内联 / 外部 URL 直传）→ 视觉模型分析 → 返回基于提示词的文本；返回体 = 首行 JSON 头 + 正文
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
            // [参数面] 声明面口径零容忍——未知 / 缺值一律 ERR|BAD_ARGS（catId 保留键放行）
            string badArgs = JsonArgs.Validate(argsJson, "path question", "path", "", "");
            if (badArgs.Length > 0)
            {
                result = badArgs;
                return false;
            }
            string path = JsonArgs.Get(argsJson, "path");
            if (path.Length == 0)
            {
                result = "ERR|BAD_ARGS|缺少参数 path";
                return false;
            }
            string question = JsonArgs.Get(argsJson, "question");
            try
            {
                IVisionService? service;
                DataBox.TryResolve<IVisionService>(out service);
                if (service == null)
                {
                    result = "ERR|VISION_NO_SERVICE|宿主未注入 IVisionService";
                    return false;
                }
                string body = service.Analyze(path, question);
                if (body.StartsWith("ERR|", StringComparison.Ordinal))
                {
                    result = body;
                    return true;
                }
                // 结构化返回体（design-ch4-tools 附录）——首行 JSON 元数据头 + 正文定界
                result = MetaHead(path, question, body);
                if (body.Length > 0)
                {
                    result = result + "\n" + body;
                }
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
        /// <summary>
        /// 结构化元数据头——首行单行 JSON（ok/tool/path/question/chars；键序稳定 = 插入序）
        /// 约定（design-ch4-tools 附录）：返回体 = 首行 JSON 头 + 正文定界行（正文不塞进 JSON——避免转义膨胀）
        /// </summary>
        /// <param name="path">图片路径</param>
        /// <param name="question">提示词</param>
        /// <param name="body">分析正文</param>
        /// <returns>单行 JSON</returns>
        private static string MetaHead(string path, string question, string body)
        {
            System.Collections.Generic.Dictionary<string, object> head = new System.Collections.Generic.Dictionary<string, object>();
            head["ok"] = true;
            head["tool"] = "image-analyze";
            head["path"] = path;
            head["question"] = question;
            head["chars"] = body.Length;
            return JsonSerializer.Serialize(head);
        }
    }
}
// #MAU_CHECKSUM:SHA256:5A3C2D739A77D344F844DBABB184A4C68AF9370CD4DCB82738548F78350AD3D1
