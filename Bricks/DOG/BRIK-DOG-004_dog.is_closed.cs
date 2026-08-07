// ═══════════════════════════════════════════════════
// 积木: dog.is_closed
// ID:   BRIK-DOG-004
// 类别: DOG
// 作用: 判断 Dog 工单是否已正常关闭（Closed 且可取回执）
// 依赖: 无
// 引用: Mau.Runtime
// 原理: 经 FlowRunner 找到 IDog 实例 → IsClosed（返回=判断结果）
// 常用: 发单 Cat 语料——轮询等待执行结果
// ═══════════════════════════════════════════════════
using Mau.Runtime;

namespace Mau.Bricks
{
    /// <summary>
    /// 工单载体积木——dog.is_closed 判断关闭（依赖 FlowRunner）
    /// </summary>
    public static class DogIsClosedBrick
    {
        /// <summary>
        /// 判断 Dog 工单是否已正常关闭（Closed 且可取回执）
        /// </summary>
        /// <param name="dogId">Dog 注册 ID</param>
        /// <returns>true=已关闭</returns>
        public static bool IsClosed(long dogId)
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
            return dog.IsClosed();
        }
    }
}
// #MAU_CHECKSUM:SHA256:844DE958A5BAABD38E189278DA5B023A92B4D1897CA9C07FFB0B45B0FF30AB38
