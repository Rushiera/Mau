// ═══════════════════════════════════════════════════
// 积木: dog.get_str
// ID:   BRIK-DOG-007
// 类别: DOG
// 作用: 读 Dog 回执 str（仅 PickingUp 可读——不消费，finish 才回收）
// 依赖: 无
// 引用: Mau.Runtime
// 原理: 经 FlowRunner 找到 IDog 实例 → GetResultStr
// 常用: 发单 Cat 语料——is_closed 后逐 Key 读回执
// ═══════════════════════════════════════════════════
using Mau.Runtime;

namespace Mau.Bricks
{
    /// <summary>
    /// 工单载体积木——dog.get_str 读回执 str（依赖 FlowRunner）
    /// </summary>
    public static class DogGetStrBrick
    {
        /// <summary>
        /// 读 Dog 回执 str——仅 PickingUp 可读（不消费，finish 才回收）
        /// </summary>
        /// <param name="dogId">Dog 注册 ID</param>
        /// <param name="key">Key</param>
        /// <param name="value">str 值</param>
        /// <returns>true=Key 存在</returns>
        public static bool GetStr(long dogId, string key, out string value)
        {
            FlowRunner? runner;
            DataBox.TryResolve<FlowRunner>(out runner);
            if (runner == null)
            {
                value = "";
                return false;
            }
            IDog? dog = runner.GetFlow(dogId) as IDog;
            if (dog == null)
            {
                value = "";
                return false;
            }
            return dog.GetResultStr(key, out value);
        }
    }
}
// #MAU_CHECKSUM:SHA256:D1D528CEF7D31548AB38A4FC8398E800EFE6AB987048FC582BAB95C4C3CCAA66
