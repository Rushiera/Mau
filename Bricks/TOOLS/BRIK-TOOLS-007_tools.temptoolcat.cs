// ═══════════════════════════════════════════════════
// 积木: tools.temptoolcat
// ID:   BRIK-TOOLS-007
// 类别: TOOLS
// 作用: 返回 TempToolCat 工具组全部工具的 OpenAI 兼容定义 JSON（宿主工具池拼装原料——design-ch4-tools-pool）
// 依赖: 无
// 引用: 无（纯文本常量）
// 原理: GetToolsJson() 返回本组工具定义整包 JSON——改描述只动本文件，mau proj 重建即生效
// 常用: FL_TempToolCat.GetToolsJson() 自曝调用面（IFlow 接口）
// ═══════════════════════════════════════════════════
namespace Mau.Bricks
{
    /// <summary>
    /// 工具定义积木——TempToolCat 组工具定义 JSON（OpenAI 兼容拼装原料）
    /// </summary>
    public static class ToolsTemptoolcatBrick
    {
        /// <summary>
        /// 获取本组工具定义 JSON——{"group":"TempToolCat","tools":[{name,description,parameters}]}
        /// </summary>
        /// <returns>工具定义 JSON 字符串</returns>
        public static string GetToolsJson()
        {
            return "{\"group\":\"TempToolCat\",\"tools\":[" +
                "{\"name\":\"temp-info\",\"description\":\"临时工具信息——返回当前全部可用临时工具 Key 组（逗号分隔）\",\"parameters\":{\"type\":\"object\",\"properties\":{}}}," +
                "{\"name\":\"temp-exec\",\"description\":\"临时工具万能执行——按 Key 调度到临时工具并返回结果文本；Key 不存在报 ERR|TEMP_KEY_NOT_FOUND\",\"parameters\":{\"type\":\"object\",\"properties\":{\"key\":{\"type\":\"string\",\"description\":\"临时工具 Key（temp-info 可查当前可用 Key 组）\"},\"content\":{\"type\":\"string\",\"description\":\"输入内容\"}},\"required\":[\"key\",\"content\"]}}" +
                "]}";
        }
    }
}
// #MAU_CHECKSUM:SHA256:14DD65C412893C7FCAAA6C4B47C8B08CCAC3582A092F426D08EFBDEA94B61F67
