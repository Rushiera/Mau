namespace Mau.Runtime
{
    /// <summary>
    /// Dog 生命周期阶段——工单载体四态
    /// </summary>
    public enum DogPhase
    {
        /// <summary>
        /// 已创建未 Post
        /// </summary>
        Created,

        /// <summary>
        /// 已 Post 等待执行结果
        /// </summary>
        Waiting,

        /// <summary>
        /// Closed/TimeOut——待挂单方取回执
        /// </summary>
        PickingUp,

        /// <summary>
        /// 已取回执
        /// </summary>
        Done
    }

    /// <summary>
    /// 工单载体接口——OA 系统的附属（长程 OA 单的生命周期与持久化载体）。
    /// 通用 Dog：创建者 Post 建单 → 写请求载荷 → 轮询等待 → Closed/TimeOut → 取回执 → 回收。
    /// 实现 IFlow——注册进 FlowRunner 由全局 Tick 驱动（全部走全局 Tick，不做 Tick 分发）。
    /// </summary>
    public interface IDog : IFlow
    {
        /// <summary>
        /// Flow 注册 ID
        /// </summary>
        long DogId { get; }

        /// <summary>
        /// Dog 名字
        /// </summary>
        string DogName { get; }

        /// <summary>
        /// 工单大类
        /// </summary>
        string OfficeType { get; }

        /// <summary>
        /// 工单固定词汇——执行方据此判断能不能干
        /// </summary>
        string OfficeName { get; }

        /// <summary>
        /// OA 单 ID（未 Post 为 0）
        /// </summary>
        long OfficeId { get; }

        /// <summary>
        /// 当前阶段
        /// </summary>
        DogPhase Phase { get; }

        /// <summary>
        /// 绑定注册 ID——注册后由宿主/工厂调用
        /// </summary>
        /// <param name="dogId">Flow 注册 ID</param>
        void BindId(long dogId);

        /// <summary>
        /// 建 OA 单并进入等待
        /// </summary>
        /// <param name="officeType">工单大类</param>
        /// <param name="officeName">工单固定词汇</param>
        /// <param name="timeoutTicks">超时帧数</param>
        /// <returns>true=建单成功</returns>
        bool Post(string officeType, string officeName, long timeoutTicks);

        /// <summary>
        /// 写请求载荷 int
        /// </summary>
        /// <param name="key">Key</param>
        /// <param name="value">int 值</param>
        /// <returns>true=写入成功</returns>
        bool SetInt(string key, int value);

        /// <summary>
        /// 写请求载荷 str
        /// </summary>
        /// <param name="key">Key</param>
        /// <param name="value">str 值</param>
        /// <returns>true=写入成功</returns>
        bool SetStr(string key, string value);

        /// <summary>
        /// 单是否已正常关闭（Closed 且可取回执）
        /// </summary>
        /// <returns>true=Closed</returns>
        bool IsClosed();

        /// <summary>
        /// 单是否已超时（TimeOut 且可取回执）
        /// </summary>
        /// <returns>true=TimeOut</returns>
        bool IsTimeout();

        /// <summary>
        /// 取回执双字典并进入 Done——仅 PickingUp 可调
        /// </summary>
        /// <param name="result">回执双字典（超时为空）</param>
        /// <returns>true=取回执成功</returns>
        bool Collect(out OfficeData result);

        /// <summary>
        /// 读回执 int——仅 PickingUp 可调（不消费，finish 才回收）
        /// </summary>
        /// <param name="key">Key</param>
        /// <param name="value">int 值</param>
        /// <returns>true=Key 存在</returns>
        bool GetResultInt(string key, out int value);

        /// <summary>
        /// 读回执 str——仅 PickingUp 可调（不消费，finish 才回收）
        /// </summary>
        /// <param name="key">Key</param>
        /// <param name="value">str 值</param>
        /// <returns>true=Key 存在</returns>
        bool GetResultStr(string key, out string value);

        /// <summary>
        /// 持久化落盘——原子写（临时文件 + 改名）
        /// </summary>
        /// <param name="path">目标路径</param>
        /// <returns>true=保存成功</returns>
        bool Save(string path);

        /// <summary>
        /// 从磁盘恢复——重建 Dog + OA 重建单（载荷写回）；注册由调用方负责
        /// </summary>
        /// <param name="path">存档路径</param>
        /// <param name="oa">OA 引用</param>
        /// <returns>Dog 实例，失败返回 null</returns>
        IDog? TryLoad(string path, IOA oa);

        /// <summary>
        /// 清内部状态进入 Done（注册回收由调用方执行 FlowRunner.UnregisterFlow）
        /// </summary>
        void Dispose();
    }
}
