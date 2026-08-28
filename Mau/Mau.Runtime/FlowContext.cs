namespace Mau.Runtime
{
    /// <summary>
    /// 当前 Flow 执行上下文——对象唯一 ID 的运行时通道。
    /// FlowRunner 驱动实体帧前 SetFlowId、finally Clear；积木静态方法与生成物
    /// 代码经 CurrentFlowId 取得"当前是哪个实例在调我"——语料面零感知 ID。
    /// ThreadStatic：主线程驱动语义（par 后台线程不传播——后台积木不依赖 OA 身份，
    /// 跨线程身份随 BrickCaptureV3 载荷回投主线程后应用）。
    /// </summary>
    public static class FlowContext
    {
        /// <summary>
        /// 当前驱动中的 Flow 全局 ID——0=上下文外（未驱动）
        /// </summary>
        [System.ThreadStatic]
        private static long _currentFlowId;

        /// <summary>
        /// 当前驱动中的 Flow 全局 ID——0=上下文外
        /// </summary>
        public static long CurrentFlowId
        {
            get { return _currentFlowId; }
        }

        /// <summary>
        /// 设置当前 Flow ID（FlowRunner 驱动入口调用）
        /// </summary>
        /// <param name="flowId">Flow 全局 ID</param>
        public static void SetFlowId(long flowId)
        {
            _currentFlowId = flowId;
        }

        /// <summary>
        /// 清除上下文（FlowRunner 驱动出口调用）
        /// </summary>
        public static void Clear()
        {
            _currentFlowId = 0;
        }
    }
}
