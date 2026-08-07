// ═══════════════════════════════════════════════════
// 积木: dog.set_str
// ID:   BRIK-DOG-003
// 类别: DOG
// 作用: 写 Dog 请求载荷 str（仅 Waiting 本人可写）
// 依赖: 无
// 引用: Mau.Runtime
// 原理: 经 FlowRunner 找到 IDog 实例 → SetStr（OA.SetStr + 本地快照）
// 常用: 发单 Cat 语料——dog.create 后逐 Key 写请求参数
// ═══════════════════════════════════════════════════
using Mau.Runtime;

namespace Mau.Bricks
{
    /// <summary>
    /// 工单载体积木——dog.set_str 写请求载荷 str（依赖 FlowRunner）
    /// </summary>
    public static class DogSetStrBrick
    {
        /// <summary>
        /// 写 Dog 请求载荷 str——仅 Waiting 状态本人可写
        /// </summary>
        /// <param name="dogId">Dog 注册 ID</param>
        /// <param name="key">Key</param>
        /// <param name="value">str 值</param>
        /// <returns>true=写入成功</returns>
        public static bool SetStr(long dogId, string key, string value)
        {
            FlowRunner? runner;
            DataBox.TryResolve<FlowRunner>(out runner);
            if (runner == null)
            {
                return false;
            }
            IDog? dog = runner.GetFlow(dogId) as IDog;
            if (dog == null)
            {
                return false;
            }
            return dog.SetStr(key, value);
        }
    }
}
// #MAU_CHECKSUM:SHA256:93A480F169852ABEFF13566D5459581CB6BF1BB0D15CC3C1AB8AB65B1701F4F4
