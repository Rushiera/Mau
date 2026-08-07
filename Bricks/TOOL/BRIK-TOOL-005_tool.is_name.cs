// ═══════════════════════════════════════════════════
// 积木: tool.is_name
// ID:   BRIK-TOOL-005
// 类别: TOOL
// 作用: 工具名匹配判断——查 OA 单 OfficeName 是否等于目标名
// 依赖: 无
// 引用: Mau.Runtime
// 原理: GetOffice 读 OfficeName 比较——返回=判断结果（与 is_end/is_tool 同语义）
// 常用: ToolExec 语料工具分支（CH4 P2.3）
// ═══════════════════════════════════════════════════
using Mau.Runtime;

namespace Mau.Bricks
{
    /// <summary>
    /// 工具积木——tool.is_name 工具名匹配（依赖 OaBridge）
    /// </summary>
    public static class ToolIsNameBrick
    {
        /// <summary>
        /// 工具名匹配判断——查 OA 单 OfficeName 是否等于目标名（语料分支用）
        /// </summary>
        /// <param name="officeId">Office ID</param>
        /// <param name="target">目标工具名（常量绑定）</param>
        /// <param name="matched">是否匹配</param>
        /// <returns>true=匹配</returns>
        public static bool IsName(long officeId, string target, out bool matched)
        {
            matched = false;
            IOA? oa;
            DataBox.TryResolve<IOA>(out oa);
            if (oa == null || string.IsNullOrWhiteSpace(target))
            {
                return false;
            }
            Office office = oa.GetOffice(officeId);
            matched = office.OfficeName == target;
            return matched;
        }
    }
}
// #MAU_CHECKSUM:SHA256:24368513815FDE97FB34A44AD479222998438CFB7E355C9717134503FA5D233D
