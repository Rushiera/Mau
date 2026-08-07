// ═══════════════════════════════════════════════════
// 积木: dog.is_timeout
// ID:   BRIK-DOG-005
// 类别: DOG
// 作用: 判断 Dog 工单是否已超时（TimeOut 且可取回执）
// 依赖: 无
// 引用: Mau.Runtime
// 原理: 经 FlowRunner 找到 IDog 实例 → IsTimeout（返回=判断结果）
// 常用: 发单 Cat 语料——超时分支结算
// ═══════════════════════════════════════════════════
using Mau.Runtime;

namespace Mau.Bricks
{
    /// <summary>
    /// 工单载体积木——dog.is_timeout 判断超时（依赖 FlowRunner）
    /// </summary>
    public static class DogIsTimeoutBrick
    {
        /// <summary>
        /// 判断 Dog 工单是否已超时（TimeOut 且可取回执）
        /// </summary>
        /// <param name="dogId">Dog 注册 ID</param>
        /// <returns>true=已超时</returns>
        public static bool IsTimeout(long dogId)
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
            return dog.IsTimeout();
        }
    }
}
// #MAU_CHECKSUM:SHA256:DF2683DC087B084BE2762570181D88D0805FB0B1EE26ED25CE1C14F8C2A87106
