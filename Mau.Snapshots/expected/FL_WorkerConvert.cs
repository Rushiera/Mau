// 本文件由 Mau Translator v0.1 自动生成 —— 请勿手改
// 流程: WorkerConvert
// 基座: Mau.Runtime/v0.1

using Mau.Runtime;
using System.Threading.Tasks;

namespace Mau.Generated.Flows
{
    /// <summary>
    /// WorkerConvert 流程——由 Mau 声明生成
    /// </summary>
    public sealed class FL_WorkerConvert
    {
        /// <summary>
        /// 命题 P_Input：信号，消费即清除
        /// </summary>
        private bool P_Input;

        /// <summary>
        /// 命题 P_Done：终态事实，置位后保持
        /// </summary>
        private bool P_Done;

        /// <summary>
        /// 命题 P_Failed：终态事实，置位后保持
        /// </summary>
        private bool P_Failed;

        /// <summary>
        /// 变迁 T_WorkerConvert 的动作参数——input
        /// </summary>
        private string _input = null!;

        /// <summary>
        /// 变迁 T_WorkerConvert 的动作参数——output
        /// </summary>
        private string _output = null!;

        /// <summary>
        /// 变迁 T_WorkerConvert 的时限 Cube（300 帧有限模式）
        /// </summary>
        private Cube T_WorkerConvert_Cube;

        /// <summary>
        /// 变迁 T_WorkerConvert 的 Inbox 双缓冲——后台结果回投
        /// </summary>
        private Inbox<bool> T_WorkerConvert_Inbox;

        /// <summary>
        /// 构造：初始化 Cube 与 Inbox
        /// </summary>
        public FL_WorkerConvert()
        {
            T_WorkerConvert_Cube = new Cube(300);
            T_WorkerConvert_Inbox = new Inbox<bool>();
        }

        /// <summary>
        /// 外部投递信号：Input
        /// </summary>
        /// <param name="input">输入文件路径</param>
        /// <param name="output">输出文件路径</param>
        public void FireInput(string input, string output)
        {
            _input = input;
            _output = output;
            P_Input = true;
        }

        /// <summary>
        /// 每帧驱动——由主 Tick 调用
        /// </summary>
        public void Tick()
        {
            // [T_WorkerConvert] worker+inbox 前置检查
            if (P_Input && T_WorkerConvert_Cube.IsIdle())
            {
                // 信号消费
                P_Input = false;
                T_WorkerConvert_Cube.Start();
                // 启动后台任务——积木在 worker 线程执行，结果回投 inbox
                var inbox = T_WorkerConvert_Inbox;
                Task.Run(() =>
                {
                    bool ok = Mau.Bricks.FileBrick.Convert(_input, _output);
                    inbox.Enqueue(ok);
                });
            }

            // [T_WorkerConvert] inbox 排空
            T_WorkerConvert_Inbox.Drain(ok =>
            {
                if (!T_WorkerConvert_Cube.IsRunning())
                {
                    return;
                }
                if (ok)
                {
                    P_Done = true;
                }
                else
                {
                    P_Failed = true;
                }
                T_WorkerConvert_Cube.Complete();
            });

            // [T_WorkerConvert] 时限检查
            if (T_WorkerConvert_Cube.IsRunning())
            {
                T_WorkerConvert_Cube.TickFrame();
                if (T_WorkerConvert_Cube.IsExpired())
                {
                    // 超时 → 错误后置
                    P_Failed = true;
                    T_WorkerConvert_Cube.Complete();
                }
            }
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
        /// 查询结果：Failed
        /// </summary>
        /// <returns>Failed成立</returns>
        public bool IsFailed()
        {
            return P_Failed;
        }

    }
}
