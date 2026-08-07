// ═══════════════════════════════════════════════════
// 积木: dog.set_int
// ID:   BRIK-DOG-002
// 类别: DOG
// 作用: 写 Dog 请求载荷 int（仅 Waiting 本人可写）
// 依赖: 无
// 引用: Mau.Runtime
// 原理: 经 FlowRunner 找到 IDog 实例 → SetInt（OA.SetInt + 本地快照）
// 常用: 发单 Cat 语料——dog.create 后逐 Key 写请求参数
// ═══════════════════════════════════════════════════
using Mau.Runtime;

namespace Mau.Bricks
{
    /// <summary>
    /// 工单载体积木——dog.set_int 写请求载荷 int（依赖 FlowRunner）
    /// </summary>
    public static class DogSetIntBrick
    {
        /// <summary>
        /// 写 Dog 请求载荷 int——仅 Waiting 状态本人可写
        /// </summary>
        /// <param name="dogId">Dog 注册 ID</param>
        /// <param name="key">Key</param>
        /// <param name="value">int 值</param>
        /// <returns>true=写入成功</returns>
        public static bool SetInt(long dogId, string key, int value)
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
            return dog.SetInt(key, value);
        }
    }
}
// #MAU_CHECKSUM:SHA256:07A2D7F146F064455763DB86D34BDABC547C378C88E1CECF266B6B03561A6F0F
