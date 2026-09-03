// ═══════════════════════════════════════════════════
// 积木: tools.pscat
// ID:   BRIK-TOOLS-008
// 类别: TOOLS
// 作用: 返回 PsCat 工具组全部工具的 OpenAI 兼容定义 JSON（宿主工具池拼装原料——design-ch4-tools-pool）
// 依赖: 无
// 引用: 无（纯文本常量）
// 原理: GetToolsJson() 返回本组工具定义整包 JSON——改描述只动本文件，mau proj 重建即生效
// 常用: FL_PsCat.GetToolsJson() 自曝调用面（IFlow 接口）
// ═══════════════════════════════════════════════════
namespace Mau.Bricks
{
    /// <summary>
    /// 工具定义积木——PsCat 组工具定义 JSON（OpenAI 兼容拼装原料）
    /// </summary>
    public static class ToolsPscatBrick
    {
        /// <summary>
        /// 获取本组工具定义 JSON——{"group":"PsCat","tools":[{name,description,parameters}]}
        /// </summary>
        /// <returns>工具定义 JSON 字符串</returns>
        public static string GetToolsJson()
        {
            return "{\"group\":\"PsCat\",\"tools\":[" +
                "{\"name\":\"powershell\",\"description\":\"执行 PowerShell 命令——整段命令原样执行（内部 EncodedCommand 免转义）；返回 JSON（exit/stdout/stderr/truncated/timeout）；写文件语义被拦截（走 text-* 读写工具）；git 命令豁免；禁 Start-Process/ReadKey\",\"parameters\":{\"type\":\"object\",\"properties\":{\"command\":{\"type\":\"string\",\"description\":\"PowerShell 命令文本（完整一段脚本）\"},\"cwd\":{\"type\":\"string\",\"description\":\"工作目录（默认宿主数据根）\"},\"timeout_ms\":{\"type\":\"integer\",\"description\":\"超时毫秒（默认 30000，上限 300000）\"}},\"required\":[\"command\"]}}" +
                "]}";
        }
    }
}
// #MAU_CHECKSUM:SHA256:EFB80826768D19B57D0ACAEFD92E8060378220A8B0744F9FE463921405621799
