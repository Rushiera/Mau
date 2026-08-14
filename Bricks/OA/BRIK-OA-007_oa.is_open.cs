// ═══════════════════════════════════════════════════
// 积木: oa.is_open
// ID:   BRIK-OA-007
// 类别: OA
// 作用: 探测工单——某大类下指定 officeName 候选是否存在 Open 单（纯探测零副作用——主动传感器采样用）
// 依赖: 无
// 引用: Mau.Runtime（IOA/DataBox）
// 原理: DataBox.TryResolve<IOA> → ListOpen(type, names).Count > 0
// 常用: CH4 第一轮 io_test_cat/quick_cat 语料——接单轮询探测（探测与动作分离：claim 归导线动作）
// ═══════════════════════════════════════════════════
using Mau.Runtime;

namespace Mau.Bricks
{
    /// <summary>
    /// OA 积木——oa.is_open 探测 Open 单（纯探测零副作用）
    /// </summary>
    public static class OaIsOpenBrick
    {
        /// <summary>
        /// 探测工单——某大类下指定 officeName 候选是否存在 Open 单
        /// </summary>
        /// <param name="officeType">工单大类</param>
        /// <param name="officeNames">能力候选数组（逗号分隔——语料可构造）</param>
        /// <returns>true=存在 Open 单（判断语义——is_* 命名）</returns>
        public static bool IsOpen(string officeType, string officeNames)
        {
            IOA? oa;
            DataBox.TryResolve<IOA>(out oa);
            if (oa == null)
            {
                return false;
            }
            string[] names = officeNames.Split(',', ';');
            for (int i = 0; i < names.Length; i = i + 1)
            {
                names[i] = names[i].Trim();
            }
            System.Collections.Generic.List<Office> open = oa.ListOpen(officeType, names);
            return open.Count > 0;
        }
    }
}
// #MAU_CHECKSUM:SHA256:04B841C38529EC11833802F1F422F8FBCC084E5D363A1255E5957646E881C7CE
