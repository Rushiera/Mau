// ═══════════════════════════════════════════════════
// 积木: tools.llmsimplerequest
// ID:   BRIK-TOOLS-014
// 类别: TOOLS
// 作用: 返回 LLMSimpleRequest 组工具定义 JSON——空组（本组不向 LLM 暴露任何工具）
// 依赖: 无
// 引用: 无（纯文本常量）
// 原理: GetToolsJson() 返回空组 JSON——LLMSimpleRequest 是 OA 服务工单消费者（一次性 LLM 请求服务），
//       不向 LLM 暴露工具；本积木 = 非工具组的显式空组声明标准解法
//       （约定名 tools.<flowName 小写>——有认领面就要求定义面，空组也是定义；判据统一、不设豁免分支）
// 常用: FL_LLMSimpleRequest.GetToolsJson() 自曝调用面（IFlow 接口）
// ═══════════════════════════════════════════════════
namespace Mau.Bricks
{
    /// <summary>
    /// 工具定义积木——LLMSimpleRequest 组（空组声明：本组无 LLM 工具，仅消费 OA 服务工单）。
    /// </summary>
    public static class ToolsLlmsimplerequestBrick
    {
        /// <summary>
        /// 获取本组工具定义 JSON——{"group":"LLMSimpleRequest","tools":[]}
        /// </summary>
        /// <returns>工具定义 JSON 字符串</returns>
        public static string GetToolsJson()
        {
            return "{\"group\":\"LLMSimpleRequest\",\"tools\":[]}";
        }
    }
}
// #MAU_CHECKSUM:SHA256:027BFA2ED15B401709557A9B3862286226DB5713C7C061044D90E9612F062AA0
