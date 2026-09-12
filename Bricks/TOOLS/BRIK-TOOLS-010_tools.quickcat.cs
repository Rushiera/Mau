// ═══════════════════════════════════════════════════
// 积木: tools.quickcat
// ID:   BRIK-TOOLS-010
// 类别: TOOLS
// 作用: 返回 QuickCat 组工具定义 JSON（本组不提供 LLM 工具——显式空组声明）
// 依赖: 无
// 引用: 无（纯文本常量）
// 原理: GetToolsJson() 返回空工具组 JSON——QuickCat 是 OA 工单消费组（双参消费者 / llm.stream 流式），
//       不向 LLM 暴露任何工具；本积木是 E409 约定面的显式声明（认领 TOOL 域 ≠ 提供 LLM 工具定义）
// 常用: FL_QuickCat.GetToolsJson() 自曝调用面（IFlow 接口）
// ═══════════════════════════════════════════════════
namespace Mau.Bricks
{
    /// <summary>
    /// 工具定义积木——QuickCat 组（空组：本组无 LLM 工具，仅消费 OA 工单）。
    /// </summary>
    public static class ToolsQuickcatBrick
    {
        /// <summary>
        /// 获取本组工具定义 JSON——{"group":"QuickCat","tools":[]}
        /// </summary>
        /// <returns>工具定义 JSON 字符串</returns>
        public static string GetToolsJson()
        {
            return "{\"group\":\"QuickCat\",\"tools\":[]}";
        }
    }
}
// #MAU_CHECKSUM:SHA256:3DA78595E55C817023308692899EC260F54D92072E18BAA67B3CAEEF0763FE2A
