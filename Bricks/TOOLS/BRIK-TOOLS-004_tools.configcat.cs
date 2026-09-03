// ═══════════════════════════════════════════════════
// 积木: tools.configcat
// ID:   BRIK-TOOLS-004
// 类别: TOOLS
// 作用: 返回 ConfigCat 工具组全部工具的 OpenAI 兼容定义 JSON（宿主工具池拼装原料——design-ch4-tools-pool）
// 依赖: 无
// 引用: 无（纯文本常量）
// 原理: GetToolsJson() 返回本组工具定义整包 JSON——改描述只动本文件，mau proj 重建即生效
// 常用: FL_ConfigCat.GetToolsJson() 自曝调用面（IFlow 接口）
// ═══════════════════════════════════════════════════
namespace Mau.Bricks
{
    /// <summary>
    /// 工具定义积木——ConfigCat 组工具定义 JSON（OpenAI 兼容拼装原料）
    /// </summary>
    public static class ToolsConfigcatBrick
    {
        /// <summary>
        /// 获取本组工具定义 JSON——{"group":"ConfigCat","tools":[{name,description,parameters}]}
        /// </summary>
        /// <returns>工具定义 JSON 字符串</returns>
        public static string GetToolsJson()
        {
            return "{\"group\":\"ConfigCat\",\"tools\":[" +
                "{\"name\":\"config-list\",\"description\":\"配置全览——schema 全部条目（键/当前值/来源/schema 默认/敏感/可写/值域/描述）；敏感键掩码\",\"parameters\":{\"type\":\"object\",\"properties\":{}}}," +
                "{\"name\":\"config-get\",\"description\":\"配置单项查询——按 schema 键返回（含默认/敏感/可写/值域/描述）；敏感键掩码\",\"parameters\":{\"type\":\"object\",\"properties\":{\"key\":{\"type\":\"string\",\"description\":\"配置键（如 ui.chat_font_size）\"}},\"required\":[\"key\"]}}," +
                "{\"name\":\"config-set\",\"description\":\"配置写入——仅 schema 声明且 writable=true 的项（白名单+值域校验+原子写+失败回滚）；llm.* 私密环境变量只读\",\"parameters\":{\"type\":\"object\",\"properties\":{\"key\":{\"type\":\"string\",\"description\":\"配置键\"},\"value\":{\"type\":\"string\",\"description\":\"新值（掩码值拒绝）\"}},\"required\":[\"key\",\"value\"]}}," +
                "{\"name\":\"config-reset\",\"description\":\"配置还原默认——key 空=全群 writable 项还原 schema default；key 非空=单项\",\"parameters\":{\"type\":\"object\",\"properties\":{\"key\":{\"type\":\"string\",\"description\":\"配置键（空=全群）\"}},\"required\":[]}}" +
                "]}";
        }
    }
}
// #MAU_CHECKSUM:SHA256:B330838AD6BE23657A00C26768932A27CC00682FC01C58B35B6B60690FF4EA00
