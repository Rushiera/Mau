// ═══════════════════════════════════════════════════
// 积木: tools.visioncat
// ID:   BRIK-TOOLS-006
// 类别: TOOLS
// 作用: 返回 VisionCat 工具组全部工具的 OpenAI 兼容定义 JSON（宿主工具池拼装原料——design-ch4-tools-pool）
// 依赖: 无
// 引用: 无（纯文本常量）
// 原理: GetToolsJson() 返回本组工具定义整包 JSON——改描述只动本文件，mau proj 重建即生效
// 常用: FL_VisionCat.GetToolsJson() 自曝调用面（IFlow 接口）
// ═══════════════════════════════════════════════════
namespace Mau.Bricks
{
    /// <summary>
    /// 工具定义积木——VisionCat 组工具定义 JSON（OpenAI 兼容拼装原料）
    /// </summary>
    public static class ToolsVisioncatBrick
    {
        /// <summary>
        /// 获取本组工具定义 JSON——{"group":"VisionCat","tools":[{name,description,parameters}]}
        /// </summary>
        /// <returns>工具定义 JSON 字符串</returns>
        public static string GetToolsJson()
        {
            return "{\"group\":\"VisionCat\",\"tools\":[" +
                "{\"name\":\"image-analyze\",\"description\":\"图像识别——读取图片（本地路径或 http(s) URL）并用视觉模型分析，返回基于提示词的描述/OCR/图表解读；视觉 API 需在配置区先配置\",\"parameters\":{\"type\":\"object\",\"properties\":{\"path\":{\"type\":\"string\",\"description\":\"图片路径（本地绝对路径或 http(s) URL）\"},\"question\":{\"type\":\"string\",\"description\":\"提示词（对图片的提问，可空=默认描述）\"}},\"required\":[\"path\"]}}" +
                "]}";
        }
    }
}
// #MAU_CHECKSUM:SHA256:B50B5B6CA5DF5BEE41511132359E943FF3D13EFDFB11ADA3037C1002352B806A
