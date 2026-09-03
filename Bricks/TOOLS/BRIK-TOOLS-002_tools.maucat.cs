// ═══════════════════════════════════════════════════
// 积木: tools.maucat
// ID:   BRIK-TOOLS-002
// 类别: TOOLS
// 作用: 返回 MauCat 工具组全部工具的 OpenAI 兼容定义 JSON（宿主工具池拼装原料——design-ch4-tools-pool）
// 依赖: 无
// 引用: 无（纯文本常量）
// 原理: GetToolsJson() 返回本组工具定义整包 JSON——改描述只动本文件，mau proj 重建即生效
// 常用: FL_MauCat.GetToolsJson() 自曝调用面（IFlow 接口）
// ═══════════════════════════════════════════════════
namespace Mau.Bricks
{
    /// <summary>
    /// 工具定义积木——MauCat 组工具定义 JSON（OpenAI 兼容拼装原料）
    /// </summary>
    public static class ToolsMaucatBrick
    {
        /// <summary>
        /// 获取本组工具定义 JSON——{"group":"MauCat","tools":[{name,description,parameters}]}
        /// </summary>
        /// <returns>工具定义 JSON 字符串</returns>
        public static string GetToolsJson()
        {
            return "{\"group\":\"MauCat\",\"tools\":[" +
                "{\"name\":\"mau-verify\",\"description\":\"Mau 语料全链检查（词法→解析→验证→分析），返回诊断（文件:行:错误码:消息）；零产出\",\"parameters\":{\"type\":\"object\",\"properties\":{\"file\":{\"type\":\"string\",\"description\":\".mau 文件路径\"}},\"required\":[\"file\"]}}," +
                "{\"name\":\"mau-gen\",\"description\":\"组翻译——.mauproj 组声明 → 中间产物（验证全组 + BRIKGROUP.cs + FL_*.cs）；不编译\",\"parameters\":{\"type\":\"object\",\"properties\":{\"proj\":{\"type\":\"string\",\"description\":\".mauproj 文件路径\"}},\"required\":[\"proj\"]}}," +
                "{\"name\":\"mau-proj\",\"description\":\"组翻译 + 编译——.mauproj → Flows/FL_<组>.dll（长耗时；产物可在宿主热重载）\",\"parameters\":{\"type\":\"object\",\"properties\":{\"proj\":{\"type\":\"string\",\"description\":\".mauproj 文件路径\"},\"build\":{\"type\":\"boolean\",\"description\":\"true=翻译后执行 dotnet build\"}},\"required\":[\"proj\"]}}" +
                "]}";
        }
    }
}
// #MAU_CHECKSUM:SHA256:5F07510A8965DF20079488E501A5E245270206AAEDAF39D1CDFF6F96984C922B
