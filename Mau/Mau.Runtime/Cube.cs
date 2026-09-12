namespace Mau.Runtime
{
    /// <summary>
    /// Cube 时限模式——决定超时语义
    /// </summary>
    public enum CubeMode
    {
        /// <summary>
        /// 有限——自启动起最多 N 帧，耗尽走超时
        /// </summary>
        Total,

        /// <summary>
        /// 空闲——最近一次事件后 N 帧无新事件则超时（断流检测）
        /// </summary>
        Idle,

        /// <summary>
        /// 无限——持续等待，直到主动完成
        /// </summary>
        Infinite
    }

    /// <summary>
    /// Cube 状态——运行状态机
    /// </summary>
    public enum CubeState
    {
        /// <summary>
        /// 空闲——未启动，可触发
        /// </summary>
        Idle,

        /// <summary>
        /// 运行中——已启动，帧推进中
        /// </summary>
        Running,

        /// <summary>
        /// 已超时——时限耗尽，等待复位
        /// </summary>
        Expired
    }

    /// <summary>
    /// 时限 Cube——帧步进的确定性时限原语
    /// </summary>
    public sealed class Cube
    {
        /// <summary>
        /// 时限模式——Total 总时限 / IdleTimeout 空闲超时
        /// </summary>
        private readonly CubeMode _mode;

        /// <summary>
        /// 时限帧数
        /// </summary>
        private readonly long _limitFrames;

        /// <summary>
        /// 当前状态
        /// </summary>
        private CubeState _state;

        /// <summary>
        /// 已推进帧数
        /// </summary>
        private long _elapsed;

        /// <summary>
        /// 最近事件帧号——空闲超时起点
        /// </summary>
        private long _idleSince;

        /// <summary>
        /// 创建有限 Cube——总时限模式
        /// </summary>
        /// <param name="frameLimit">总时限帧数，必须大于 0</param>
        public Cube(long frameLimit)
        {
            _mode = CubeMode.Total;
            _limitFrames = frameLimit;
            _state = CubeState.Idle;
            _elapsed = 0;
            _idleSince = 0;
        }

        /// <summary>
        /// 创建指定模式 Cube
        /// </summary>
        /// <param name="mode">时限模式</param>
        /// <param name="limitFrames">时限帧数——Total/Idle 模式必填，Infinite 传 0</param>
        public Cube(CubeMode mode, long limitFrames)
        {
            _mode = mode;
            // 先判后赋（R1-P3-02：Infinite 模式帧限归零——避免 readonly 字段二次赋值）
            if (mode == CubeMode.Infinite)
            {
                _limitFrames = 0;
            }
            else
            {
                _limitFrames = limitFrames;
            }
            _state = CubeState.Idle;
            _elapsed = 0;
            _idleSince = 0;
        }

        /// <summary>
        /// 当前状态
        /// </summary>
        public CubeState State
        {
            get { return _state; }
        }

        /// <summary>
        /// 是否空闲——未启动或已复位
        /// </summary>
        /// <returns>空闲为真</returns>
        public bool IsIdle()
        {
            return _state == CubeState.Idle;
        }

        /// <summary>
        /// 是否运行中
        /// </summary>
        /// <returns>运行为真</returns>
        public bool IsRunning()
        {
            return _state == CubeState.Running;
        }

        /// <summary>
        /// 是否已超时
        /// </summary>
        /// <returns>超时为真</returns>
        public bool IsExpired()
        {
            return _state == CubeState.Expired;
        }

        /// <summary>
        /// 已推进帧数
        /// </summary>
        public long ElapsedFrames
        {
            get { return _elapsed; }
        }

        /// <summary>
        /// 时限帧数——Infinite 模式为 0
        /// </summary>
        public long LimitFrames
        {
            get { return _limitFrames; }
        }

        /// <summary>
        /// 启动——Idle 转 Running，清零计数
        /// </summary>
        public void Start()
        {
            if (_state != CubeState.Idle)
            {
                return;
            }
            _state = CubeState.Running;
            _elapsed = 0;
            _idleSince = 0;
        }

        /// <summary>
        /// 帧推进——Running 时计数并检查超时
        /// </summary>
        public void TickFrame()
        {
            if (_state != CubeState.Running)
            {
                return;
            }
            _elapsed = _elapsed + 1;
            if (_mode == CubeMode.Total && _elapsed >= _limitFrames)
            {
                _state = CubeState.Expired;
                return;
            }
            if (_mode == CubeMode.Idle)
            {
                long idleFrames = _elapsed - _idleSince;
                if (idleFrames >= _limitFrames)
                {
                    _state = CubeState.Expired;
                    return;
                }
            }
        }

        /// <summary>
        /// 事件到达——空闲模式重置空闲计数（收到数据/分片时调用）
        /// </summary>
        public void Touch()
        {
            if (_state != CubeState.Running)
            {
                return;
            }
            _idleSince = _elapsed;
        }

        /// <summary>
        /// 主动完成——动作成功结束，回到 Idle
        /// </summary>
        public void Complete()
        {
            _state = CubeState.Idle;
            _elapsed = 0;
            _idleSince = 0;
        }

        /// <summary>
        /// 复位——任何状态回到 Idle
        /// </summary>
        public void Reset()
        {
            _state = CubeState.Idle;
            _elapsed = 0;
            _idleSince = 0;
        }
    }
}
