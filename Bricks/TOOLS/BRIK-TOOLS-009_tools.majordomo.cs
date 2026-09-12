// ═══════════════════════════════════════════════════
// 积木: tools.majordomo
// ID:   BRIK-TOOLS-009
// 类别: TOOLS
// 作用: 返回 Majordomo 工具组全部工具的 OpenAI 兼容定义 JSON（宿主工具池拼装原料——design-ch4-tools-pool）
// 依赖: 无
// 引用: 无（纯文本常量）
// 原理: GetToolsJson() 返回本组工具定义整包 JSON——改描述只动本文件，mau proj 重建即生效
// 常用: FL_Majordomo.GetToolsJson() 自曝调用面（IFlow 接口）
// ═══════════════════════════════════════════════════
namespace Mau.Bricks
{
    /// <summary>
    /// 工具定义积木——Majordomo 组工具定义 JSON（OpenAI 兼容拼装原料）。
    /// 特权组：仅默认会话（catId=majordomo）可见可调（design-ch4-host-restart §二）。
    /// </summary>
    public static class ToolsMajordomoBrick
    {
        /// <summary>
        /// 获取本组工具定义 JSON——{"group":"Majordomo","tools":[{name,description,parameters}]}
        /// </summary>
        /// <returns>工具定义 JSON 字符串</returns>
        public static string GetToolsJson()
        {
            return "{\"group\":\"Majordomo\",\"tools\":[" +
                "{\"name\":\"majordomo-restart\",\"description\":\"宿主自更新：部署当前源码（prepare + 原子切换）并重启宿主，重启完成后结果回执自动注入本会话（majordomo）。小改动自测用——改语料/积木/文档后一条调用完成'改-部署-重启-看结果'闭环。调用后本轮对话将被强制中断（已完成内容与前文已落盘），新宿主起来后会带回部署结论（版本/成败/产物时间戳）。架构级变更（Runtime 机制/协议/签名）不要用本工具。\",\"parameters\":{\"type\":\"object\",\"properties\":{\"target\":{\"type\":\"string\",\"description\":\"目标运行区绝对路径（缺省=受控根 mauout）\"},\"push\":{\"type\":\"string\",\"description\":\"重启后附加说明串（随部署报告一起注入本会话，可空）\"}},\"required\":[]}}" +
                "]}";
        }
    }
}
// #MAU_CHECKSUM:SHA256:4D9B08A2014DB6DC9ADE757B12685613BC4875E3A8A00F1A80D5D2009897E931
