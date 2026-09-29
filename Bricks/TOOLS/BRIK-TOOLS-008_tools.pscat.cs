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
                "{\"name\":\"powershell\",\"description\":\"【单行指令·Windows PowerShell 5.1（默认解释器）】仅支持「命令 + 字面量参数」——不接受任何形式的可运行代码注入：禁类型引用 [X]、方法调用、表达式、变量 $x、花括号 {}（含 JSON 字面量）、过程语句、动态执行。禁多段（分号/与运算/换行）、管道、重定向——多步拆成多次工具调用（同轮可并发）。禁绕过工具组另起功能：文件读写走 text-*、列目录走 file-tree、起进程走宿主通道、判断归你自己。返回 JSON（exit/stdout/stderr/truncated/timeout）\",\"parameters\":{\"type\":\"object\",\"properties\":{\"command\":{\"type\":\"string\",\"description\":\"PowerShell 命令文本（单行指令：命令 + 字面量参数）\"},\"cwd\":{\"type\":\"string\",\"description\":\"工作目录（默认宿主数据根）\"},\"timeout_ms\":{\"type\":\"integer\",\"description\":\"超时毫秒（默认 30000，上限 120000）\"}},\"required\":[\"command\"]}}" +
                ",{\"name\":\"powershell7\",\"description\":\"【单行指令·PowerShell 7（pwsh）】纪律与 powershell 完全一致（同禁代码形态·多段·管道·重定向·绕过工具组另起功能）——⚠ 解释器路径由配置 ps.pwsh_path 指定，未配置或路径不存在时明示不可用（不回落默认解释器）。返回 JSON（exit/stdout/stderr/truncated/timeout）\",\"parameters\":{\"type\":\"object\",\"properties\":{\"command\":{\"type\":\"string\",\"description\":\"PowerShell 7 命令文本（单行指令：命令 + 字面量参数）\"},\"cwd\":{\"type\":\"string\",\"description\":\"工作目录（默认宿主数据根）\"},\"timeout_ms\":{\"type\":\"integer\",\"description\":\"超时毫秒（默认 30000，上限 120000）\"}},\"required\":[\"command\"]}}" +
                "]}";
        }
    }
}
// #MAU_CHECKSUM:SHA256:F4D9BF01CE2188089CF6EA42A70079BBBE84D0D0BC36E62CAC3739E45FCE4BA3
