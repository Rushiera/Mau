// ═══════════════════════════════════════════════════
// 积木: dog.create
// ID:   BRIK-DOG-001
// 类别: DOG
// 作用: 创建通用 Dog 并上架 OA 单——双字典工单载体（长程单生命周期载体）
// 依赖: 无
// 引用: Mau.Runtime
// 原理: 经 FlowRunner 注册 DogBase（全局 Tick 轮询）+ IOA.Post 建单（DataBox 服务）
// 常用: 发单 Cat 语料——dog.create → set_* → 等 is_closed → get_* → finish
// ═══════════════════════════════════════════════════
using Mau.Runtime;

namespace Mau.Bricks
{
    /// <summary>
    /// 工单载体积木——dog.create 创建通用 Dog 并上架 OA 单（依赖 FlowRunner + IOA）
    /// </summary>
    public static class DogCreateBrick
    {
        /// <summary>
        /// 创建通用 Dog 并上架 OA 单——注册进 FlowRunner 由全局 Tick 轮询
        /// </summary>
        /// <param name="name">Dog 名字</param>
        /// <param name="officeType">工单大类</param>
        /// <param name="officeName">工单固定词汇</param>
        /// <param name="timeoutTicks">超时帧数</param>
        /// <param name="dogId">Flow 注册 ID</param>
        /// <param name="officeId">OA 单 ID</param>
        /// <returns>true=创建并建单成功</returns>
        public static bool Create(string name, string officeType, string officeName, long timeoutTicks, out long dogId, out long officeId)
        {
            IOA? oa;
            FlowRunner? runner;
            DataBox.TryResolve<IOA>(out oa);
            DataBox.TryResolve<FlowRunner>(out runner);
            if (oa == null || runner == null)
            {
                dogId = 0;
                officeId = 0;
                return false;
            }
            DogBase dog = new DogBase(oa, name);
            dogId = runner.RegisterFlow(dog, name);
            dog.BindId(dogId);
            if (!dog.Post(officeType, officeName, timeoutTicks))
            {
                officeId = 0;
                return false;
            }
            officeId = dog.OfficeId;
            return true;
        }
    }
}
// #MAU_CHECKSUM:SHA256:37CF5E75496FD1E76F6B3FD3EDED0BF3D7B467BF18940C33579DCFAC4E21F599
