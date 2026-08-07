// ═══════════════════════════════════════════════════
// 积木: dog.get_int
// ID:   BRIK-DOG-006
// 类别: DOG
// 作用: 读 Dog 回执 int（仅 PickingUp 可读——不消费，finish 才回收）
// 依赖: 无
// 引用: Mau.Runtime
// 原理: 经 FlowRunner 找到 IDog 实例 → GetResultInt
// 常用: 发单 Cat 语料——is_closed 后逐 Key 读回执
// ═══════════════════════════════════════════════════
using Mau.Runtime;

namespace Mau.Bricks
{
    /// <summary>
    /// 工单载体积木——dog.get_int 读回执 int（依赖 FlowRunner）
    /// </summary>
    public static class DogGetIntBrick
    {
        /// <summary>
        /// 读 Dog 回执 int——仅 PickingUp 可读（不消费，finish 才回收）
        /// </summary>
        /// <param name="dogId">Dog 注册 ID</param>
        /// <param name="key">Key</param>
        /// <param name="value">int 值</param>
        /// <returns>true=Key 存在</returns>
        public static bool GetInt(long dogId, string key, out int value)
        {
            FlowRunner? runner;
            DataBox.TryResolve<FlowRunner>(out runner);
            if (runner == null)
            {
                value = 0;
                return false;
            }
            IDog? dog = runner.GetFlow(dogId) as IDog;
            if (dog == null)
            {
                value = 0;
                return false;
            }
            return dog.GetResultInt(key, out value);
        }
    }
}
// #MAU_CHECKSUM:SHA256:2ADBF435D2B093C83043A46BB914D7CE242757D26B6B7A97DEE8669B60932A39
