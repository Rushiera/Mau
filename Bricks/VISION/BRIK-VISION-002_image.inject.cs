// ═══════════════════════════════════════════════════
// 积木: image.inject
// ID:   BRIK-VISION-002
// 类别: VISION
// 作用: 图片插入主干——工具执行面（参数面校验 + 登记回执）；真图由宿主批后段合并注入一条 user 消息（design-ch4-chat-images §8.4-1）
// 依赖: 无
// 引用: Mau.Runtime（LogStore）
// 原理: argsJson 内解析（语料零 JSON 解析）；五参 ValidateArgs 与工具声明面逐键对齐（门禁 ToolDeclarationContract）
// 常用: vision_cat.mau 认领线——'image.inject'[@args] > @result
// ═══════════════════════════════════════════════════
using System;
using System.Text.Json;
using Mau.Runtime;

namespace Mau.Bricks
{
    /// <summary>
    /// 注入积木——image-inject 图片插入主干（LLM 工具执行面：参数整包 argsJson；宿主批后段执行实际注入）
    /// </summary>
    public static class ImageInjectBrick
    {
        /// <summary>
        /// 执行登记——参数面校验后回登记回执；图片实际注入由宿主批后段读取本工具参数完成（本积木不触前文）
        /// </summary>
        /// <param name="argsJson">工具参数 JSON（path）</param>
        /// <param name="result">登记回执或 ERR| 错误文本</param>
        /// <returns>true=执行成功</returns>
        public static bool Inject(string argsJson, out string result)
        {
            result = "";
            // [参数面] 声明面口径零容忍——未知 / 缺值一律 ERR|BAD_ARGS（catId 保留键放行）
            string badArgs = JsonArgs.Validate(argsJson, "path", "path", "", "");
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
            result = MetaHead(path);
            return true;
        }

        /// <summary>
        /// 结构化元数据头——首行单行 JSON（ok/tool/path；键序稳定 = 插入序）
        /// 约定（design-ch4-tools 附录）：返回体 = 首行 JSON 头 + 正文定界行
        /// </summary>
        /// <param name="path">图片路径</param>
        /// <returns>单行 JSON</returns>
        private static string MetaHead(string path)
        {
            System.Collections.Generic.Dictionary<string, object> head = new System.Collections.Generic.Dictionary<string, object>();
            head["ok"] = true;
            head["tool"] = "image-inject";
            head["path"] = path;
            return JsonSerializer.Serialize(head);
        }
    }
}
// #MAU_CHECKSUM:SHA256:D4A541B6B583BE64FF48A0DFC72783D72B833EBBD6C52589F069BB4BAC027374
