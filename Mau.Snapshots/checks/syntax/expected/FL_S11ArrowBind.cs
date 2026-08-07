// 本文件由 Mau Translator v0.1 自动生成 —— 请勿手改
// 流程: S11ArrowBind
// 基座: Mau.Runtime/v0.1

using Mau.Runtime;
using System.Threading.Tasks;

namespace Mau.Generated.Flows
{
    /// <summary>
    /// S11ArrowBind 流程——由 Mau 声明生成
    /// </summary>
    public sealed class FL_S11ArrowBind : IObservableFlow
    {
        /// <summary>
        /// 内部帧号——每 Tick 自增
        /// </summary>
        private long _frame;

        /// <summary>
        /// 环形调试日志——200 条上限
        /// </summary>
        private FlowLog _logs;

        /// <summary>
        /// 命题 P_Start：信号，消费即清除
        /// </summary>
        private bool P_Start;

        /// <summary>
        /// 命题 P_Mid：终态事实，置位后保持
        /// </summary>
        private bool P_Mid;

        /// <summary>
        /// 命题 P_Done：终态事实，置位后保持
        /// </summary>
        private bool P_Done;

        /// <summary>
        /// 命题 P_Failed：终态事实，置位后保持
        /// </summary>
        private bool P_Failed;

        /// <summary>
        /// 变迁 T_Read 的动作参数——path
        /// </summary>
        private string _path = null!;

        /// <summary>
        /// 变迁 T_Read 的输出端口——content
        /// </summary>
        private string _content = null!;
        /// <summary>
        /// 输出端口 content——宿主只读
        /// </summary>
        public string content
        {
            get { return _content; }
        }

        /// <summary>
        /// 变迁 T_Preview 的输出端口——preview
        /// </summary>
        private string _preview = null!;
        /// <summary>
        /// 输出端口 preview——宿主只读
        /// </summary>
        public string preview
        {
            get { return _preview; }
        }

        /// <summary>
        /// 变迁 T_Read 的时限 Cube（60 帧有限模式）
        /// </summary>
        private Cube T_Read_Cube;

        /// <summary>
        /// 变迁 T_Preview 的时限 Cube（60 帧有限模式）
        /// </summary>
        private Cube T_Preview_Cube;

        /// <summary>
        /// 构造：初始化日志缓冲与 Cube/Inbox
        /// </summary>
        public FL_S11ArrowBind()
        {
            _logs = new FlowLog();
            T_Read_Cube = new Cube(60);
            T_Preview_Cube = new Cube(60);
        }

        /// <summary>
        /// 外部投递信号：Start
        /// </summary>
        /// <param name="path">受控文件路径</param>
        public void FireStart(string path)
        {
            _path = path;
            P_Start = true;
        }

        /// <summary>
        /// 每帧驱动——由主 Tick 调用
        /// </summary>
        public void Tick()
        {
            _frame = _frame + 1;
            // [T_Read] 前置检查
            if (P_Start && T_Read_Cube.IsIdle())
            {
                // 信号消费
                P_Start = false;
                T_Read_Cube.Start();
                // 执行动作（积木调用）
                bool ok = Mau.Bricks.FileBrick.Read(_path, out _content);
                if (ok)
                {
                    // 正常后置注册
                    P_Mid = true;
                }
                else
                {
                    // 错误后置注册（互斥）
                    P_Failed = true;
                }

                // 同步积木当帧完成
                T_Read_Cube.Complete();
            }

            // [T_Read] 时限检查
            if (T_Read_Cube.IsRunning())
            {
                T_Read_Cube.TickFrame();
                if (T_Read_Cube.IsExpired())
                {
                    // 超时 → 错误后置
                    P_Failed = true;
                    T_Read_Cube.Complete();
                }
            }

            // [T_Preview] 前置检查
            if (P_Mid && T_Preview_Cube.IsIdle())
            {
                T_Preview_Cube.Start();
                // 执行动作（积木调用）
                bool ok = Mau.Bricks.MathBrick.ResultPreview(_content, out _preview);
                if (ok)
                {
                    // 正常后置注册
                    P_Done = true;
                }
                else
                {
                    // 错误后置注册（互斥）
                    P_Failed = true;
                }

                // 同步积木当帧完成
                T_Preview_Cube.Complete();
            }

            // [T_Preview] 时限检查
            if (T_Preview_Cube.IsRunning())
            {
                T_Preview_Cube.TickFrame();
                if (T_Preview_Cube.IsExpired())
                {
                    // 超时 → 错误后置
                    P_Failed = true;
                    T_Preview_Cube.Complete();
                }
            }
        }

        /// <summary>
        /// 获取运行时状态快照——全量截面
        /// </summary>
        /// <returns>当前帧状态</returns>
        public RuntimeStatus GetStatus()
        {
            PropSnapshot[] props = new PropSnapshot[4];
            props[0] = new PropSnapshot("P_Start", "Signal", P_Start);
            props[1] = new PropSnapshot("P_Mid", "Fact", P_Mid);
            props[2] = new PropSnapshot("P_Done", "Fact", P_Done);
            props[3] = new PropSnapshot("P_Failed", "Fact", P_Failed);
            TransSnapshot[] trans = new TransSnapshot[2];
            trans[0] = new TransSnapshot("T_Read", T_Read_Cube.State.ToString(), T_Read_Cube.ElapsedFrames, T_Read_Cube.LimitFrames);
            trans[1] = new TransSnapshot("T_Preview", T_Preview_Cube.State.ToString(), T_Preview_Cube.ElapsedFrames, T_Preview_Cube.LimitFrames);
            ResSnapshot[] res = new ResSnapshot[0];
            return new RuntimeStatus(_frame, props, trans, res);
        }

        /// <summary>
        /// 获取全量调试日志
        /// </summary>
        /// <returns>日志数组，时间顺序</returns>
        public MauDebug[] GetLogs()
        {
            return _logs.GetAll();
        }

        /// <summary>
        /// 查询结果：Mid
        /// </summary>
        /// <returns>Mid成立</returns>
        public bool IsMid()
        {
            return P_Mid;
        }

        /// <summary>
        /// 重置结果：Mid
        /// </summary>
        public void ResetMid()
        {
            P_Mid = false;
        }

        /// <summary>
        /// 查询结果：Done
        /// </summary>
        /// <returns>Done成立</returns>
        public bool IsDone()
        {
            return P_Done;
        }

        /// <summary>
        /// 重置结果：Done
        /// </summary>
        public void ResetDone()
        {
            P_Done = false;
        }

        /// <summary>
        /// 查询结果：Failed
        /// </summary>
        /// <returns>Failed成立</returns>
        public bool IsFailed()
        {
            return P_Failed;
        }

        /// <summary>
        /// 重置结果：Failed
        /// </summary>
        public void ResetFailed()
        {
            P_Failed = false;
        }

    }
}
// #MAU_CHECKSUM:SHA256:D16C37091209D4C52D4EAF2A0B5C0E7F141E3068F560B6B7BD10C3DA1665B9FB
