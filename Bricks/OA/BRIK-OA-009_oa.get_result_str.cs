// ═══════════════════════════════════════════════════
// 积木: oa.get_result_str
// ID:   BRIK-OA-009
// 类别: OA
// 作用: 读取工单回执 str 值——挂单方收结果（Closed 后 Result 载荷）
// 依赖: 无
// 引用: Mau.Runtime（IOA/DataBox/Office）
// 原理: DataBox.TryResolve<IOA> → GetOffice(officeId).Result.Strs["result"]
// 常用: CH4 第一轮 tool_test_cat 语料——结果回流收集
// 注意: nullable 自声明（本积木使用 IOA?/string? 注解——拼接 BRIKGROUP 时 strip，组头统一 enable）
// ═══════════════════════════════════════════════════
#nullable enable
using Mau.Runtime;

namespace Mau.Bricks
{
    /// <summary>
    /// OA 积木——oa.get_result_str 读工单回执
    /// </summary>
    public static class OaGetResultStrBrick
    {
        /// <summary>
        /// 读取工单回执 result 键
        /// </summary>
        /// <param name="officeId">Office ID</param>
        /// <param name="value">回执文本——Key 不存在为空串</param>
        /// <returns>true=result 键存在</returns>
        public static bool GetResultStr(long officeId, out string value)
        {
            IOA? oa;
            DataBox.TryResolve<IOA>(out oa);
            if (oa == null)
            {
                value = "";
                return false;
            }
            Office office = oa.GetOffice(officeId);
            string? v;
            if (office.Result.Strs.TryGetValue("result", out v) && v != null)
            {
                value = v;
                return true;
            }
            value = "";
            return false;
        }
    }
}
// #MAU_CHECKSUM:SHA256:CC1002E9BABDC48064E28A3AFEF4268ECF9FD3D8246DD35693A8E1D1577F4BBC
