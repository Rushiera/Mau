// ═══════════════════════════════════════════════════
// 积木: dog.finish
// ID:   BRIK-DOG-008
// 类别: DOG
// 作用: 取走回执并回收 Dog——Collect + Dispose + UnregisterFlow
// 依赖: 无
// 引用: Mau.Runtime
// 原理: 经 FlowRunner 找到 IDog 实例 → Collect 取走 → Dispose → 注销注册
// 常用: 发单 Cat 语料——回执读完后的收尾
// ═══════════════════════════════════════════════════
using Mau.Runtime;

namespace Mau.Bricks
{
    /// <summary>
    /// 工单载体积木——dog.finish 取走回执并回收（依赖 FlowRunner）
    /// </summary>
    public static class DogFinishBrick
    {
        /// <summary>
        /// 取走回执并回收 Dog——仅 PickingUp 可调（其他状态静默失败）
        /// </summary>
        /// <param name="dogId">Dog 注册 ID</param>
        /// <returns>true=回收成功</returns>
        public static bool Finish(long dogId)
        {
            FlowRunner? runner;
            DataBox.TryResolve<FlowRunner>(out runner);
            if (runner == null)
            {
                return false;
            }
            IFlow? flow = runner.GetFlow(dogId);
            IDog? dog = flow as IDog;
            if (dog == null)
            {
                return false;
            }
            OfficeData result;
            if (!dog.Collect(out result))
            {
                return false;
            }
            dog.Dispose();
            return runner.UnregisterFlow(dogId);
        }
    }
}
// #MAU_CHECKSUM:SHA256:71A19219DDD3035E6E033F2D2CD59ED3EF1F545C83A8B33767832BA035F2C9C7
