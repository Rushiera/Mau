using System.Collections.Generic;

namespace Mau.Runtime
{
    /// <summary>
    /// OA 系统——工单撮合平台。宿主唯一实例，和 CommandBus 平级。
    /// 挂单方 Post 工单，执行方查单/锁单/回执。双方互不可见，OA 是唯一交汇点。
    /// </summary>
    public interface IOA
    {
        /// <summary>
        /// 上架一个工单。返回 OfficeId。
        /// </summary>
        /// <param name="dogId">所有者 LongId</param>
        /// <param name="officeType">工单大类</param>
        /// <param name="officeName">固定词汇——执行方据此判断能不能干</param>
        /// <param name="texts">文本参数数组，可为 null</param>
        /// <param name="paths">文件路径参数数组，可为 null</param>
        /// <param name="timeoutTicks">超时帧数，由挂单方自定义</param>
        /// <returns>新 Office 的 ID</returns>
        long Post(long dogId, string officeType, string officeName, string[] texts, string[] paths, long timeoutTicks);

        /// <summary>
        /// 取消自己挂的单。仅 Open 状态 + 本人可操作。
        /// </summary>
        /// <param name="officeId">Office ID</param>
        /// <param name="dogId">请求者 LongId</param>
        /// <returns>true=取消成功</returns>
        bool Cancel(long officeId, long dogId);

        /// <summary>
        /// 返回某大类下所有 Open 单。
        /// </summary>
        /// <param name="officeType">工单大类</param>
        /// <returns>Open 状态的 Office 列表，无则空列表</returns>
        List<Office> ListOpen(string officeType);

        /// <summary>
        /// 返回某大类下、OfficeName 在候选列表中的 Open 单。
        /// </summary>
        /// <param name="officeType">工单大类</param>
        /// <param name="officeNames">执行方能干的 OfficeName 候选数组</param>
        /// <returns>匹配的 Open 状态 Office 列表</returns>
        List<Office> ListOpen(string officeType, string[] officeNames);

        /// <summary>
        /// 逐个尝试锁单——已被别人取走的跳过。返回锁成功的名单。
        /// 执行方拿到后自行维护已认领列表，完成后逐个 Complete。
        /// </summary>
        /// <param name="catId">认领者 LongId</param>
        /// <param name="officeIds">待锁的 OfficeId 数组</param>
        /// <returns>锁成功的 Office 列表</returns>
        List<Office> ClaimBatch(long catId, long[] officeIds);

        /// <summary>
        /// 完成一个 Office——写结果 → 状态变 Closed。
        /// </summary>
        /// <param name="officeId">Office ID</param>
        /// <param name="catId">认领者 LongId</param>
        /// <param name="resultTexts">回执文本数组，可为 null</param>
        /// <param name="resultPaths">回执路径数组，可为 null</param>
        void Complete(long officeId, long catId, string[] resultTexts, string[] resultPaths);

        /// <summary>
        /// 干不了/失败了——把单重新变回 Open 让别人试试。
        /// 仅 Work 状态 + 本人可操作。
        /// </summary>
        /// <param name="officeId">Office ID</param>
        /// <param name="catId">请求者 LongId</param>
        void Relist(long officeId, long catId);

        /// <summary>
        /// 获取 Office 当前状态。
        /// </summary>
        /// <param name="officeId">Office ID</param>
        /// <returns>当前状态</returns>
        OfficeState GetStatus(long officeId);

        /// <summary>
        /// 获取 Office 完整信息（含结果）。
        /// </summary>
        /// <param name="officeId">Office ID</param>
        /// <returns>Office 结构体副本</returns>
        Office GetOffice(long officeId);

        /// <summary>
        /// 超时检查——Open/Work 超过 TimeoutFrames 后进入 TimeOut；每帧由宿主调用。
        /// </summary>
        void Tick();

        /// <summary>
        /// 获取 OA 域独立摘要——透明度暴露
        /// </summary>
        /// <returns>OA 数量与版本快照</returns>
        OAView GetSnapshot();

        /// <summary>
        /// 获取 OA 内部状态文本，供调试面板。
        /// </summary>
        /// <returns>调试信息文本行数组</returns>
        string[] GetDebugInfo();
    }
}
