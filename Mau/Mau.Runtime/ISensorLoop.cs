namespace Mau.Runtime
{
    /// <summary>
    /// 传感器壳循环接口——生成物主动传感器执行面（宿主协程列表驱动）。
    /// 主动传感器 = 独立起点：宿主 Tick 统一驱动壳方法（主线程帧驱动——时序绝对安全）；
    /// 壳内探测 → 捕获落盒 → 分支动作（只读世界，零状态转移）。
    /// </summary>
    public interface ISensorLoop
    {
        /// <summary>
        /// 驱动主动传感器壳——每帧由宿主协程列表调用（与 Flow.Tick 分离）
        /// </summary>
        /// <param name="frame">宿主帧号</param>
        void TickSensors(int frame);
    }
}
