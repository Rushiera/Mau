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
                "{\"name\":\"temp-info\",\"description\":\"临时工具信息——返回当前 TempToolCat 全部可用临时工具 Key 组（逗号分隔；R3.1 万能接口试验场——temp.exec 的 Key 注册表枚举）\",\"parameters\":{\"type\":\"object\",\"properties\":{}}}," +
                "{\"name\":\"temp-exec\",\"description\":\"临时工具万能执行——输入 Key + content，按 Key 调度到临时工具并返回 str 结果；Key 不存在报 ERR|TEMP_KEY_NOT_FOUND（R3.1 万能接口——临时工具本体在 BRIK-TEMP-001 LLM 可改区）\",\"parameters\":{\"type\":\"object\",\"properties\":{\"key\":{\"type\":\"string\",\"description\":\"临时工具 Key（temp-info 可查当前可用 Key 组）\"},\"content\":{\"type\":\"string\",\"description\":\"输入内容\"}},\"required\":[\"key\",\"content\"]}}" +
                "]}";
        }
    }
}
// #MAU_CHECKSUM:SHA256:B129A97E5897FD24D2CF999E9D745FC551D644EBB441FD57363F2C54CDA11ABF
