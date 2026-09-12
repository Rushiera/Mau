// ═══════════════════════════════════════════════════
// 积木: tools.quickcat
// ID:   BRIK-TOOLS-010
// 类别: TOOLS
// 作用: 返回 QuickCat 组工具定义 JSON——空组（本组不向 LLM 暴露任何工具）
// 依赖: 无
// 引用: 无（纯文本常量）
// 原理: GetToolsJson() 返回空组 JSON——QuickCat 是 OA 组级工单消费者（工单名 = 组名；双参载荷 +
//       llm.stream 流式），不向 LLM 暴露工具；本积木 = 非工具组的显式空组声明标准解法
//       （约定名 tools.<flowName 小写>——有认领面就要求定义面，空组也是定义；判据统一、不设豁免分支）
// 常用: FL_QuickCat.GetToolsJson() 自曝调用面（IFlow 接口）
// ═══════════════════════════════════════════════════
namespace Mau.Bricks
{
    /// <summary>
    /// 工具定义积木——QuickCat 组（空组声明：本组无 LLM 工具，仅消费 OA 组级工单）。
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
// #MAU_CHECKSUM:SHA256:37DC8A7F2673678ED0E1513C73D04EDCD777B895E30D9E481731F7FC5BC93CE7
