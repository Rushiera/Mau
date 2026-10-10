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
                // 结构化返回体（design-ch4-tools 附录 · A214）——首行 JSON 头（target + 专有）+ 正文摘要行 + 载荷
                System.Collections.Generic.Dictionary<string, object> fields = new System.Collections.Generic.Dictionary<string, object>();
                fields["question"] = question;
                fields["chars"] = body.Length;
                string headline = path + " | " + body.Length.ToString() + " 字";
                result = MetaHead("image-analyze", true, path, -1, fields);
                if (body.Length > 0)
                {
                    result = result + "\n" + headline + "\n" + body;
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
        /// 结构化元数据头（统一口径·A214）——恒定 ok / tool + 主来源 target + 主计数 items（负值 = 省略）+ 专有字段（插入序）
        /// 约定（design-ch4-tools 附录）：返回体 = 首行 JSON 头 + 正文摘要行 + 载荷（正文不塞进 JSON——避免转义膨胀）
        /// </summary>
        /// <param name="tool">工具名</param>
        /// <param name="ok">成败</param>
        /// <param name="target">主来源（图片路径；空串 = 省略）</param>
        /// <param name="items">主计数（负值 = 省略）</param>
        /// <param name="fields">附加字段（按插入序输出）</param>
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
// #MAU_CHECKSUM:SHA256:9C00A535014CBB1C7ABFD499AD70720C525A1C2DED0D014EEC18F8B94C36DEAE
