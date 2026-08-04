// 本文件由 Mau Translator v0.1 自动生成 —— 请勿手改
// 流程: FileConvert
// 基座: Mau.Runtime/v0.1

using Mau.Runtime;
using System.Threading.Tasks;

namespace Mau.Generated.Flows
{
    /// <summary>
    /// FileConvert 流程——由 Mau 声明生成
    /// </summary>
    public sealed class FL_FileConvert
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
        /// 变迁 T_Convert 的动作参数——input
        /// </summary>
        private string _input = null!;

        /// <summary>
        /// 变迁 T_Convert 的动作参数——output
        /// </summary>
        private string _output = null!;

        /// <summary>
        /// 变迁 T_Convert 的时限 Cube（300 帧有限模式）
        /// </summary>
        private Cube T_Convert_Cube;

        /// <summary>
        /// 构造：初始化 Cube 与 Inbox
        /// </summary>
        public FL_FileConvert()
        {
            T_Convert_Cube = new Cube(300);
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
            // [T_Convert] 前置检查
            if (P_Input && T_Convert_Cube.IsIdle())
            {
                // 信号消费
                P_Input = false;
                T_Convert_Cube.Start();
                // 执行动作（积木调用）
                bool ok = Mau.Bricks.FileBrick.Convert(_input, _output);
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
                T_Convert_Cube.Complete();
            }

            // [T_Convert] 时限检查
            if (T_Convert_Cube.IsRunning())
            {
                T_Convert_Cube.TickFrame();
                if (T_Convert_Cube.IsExpired())
                {
                    // 超时 → 错误后置
                    P_Failed = true;
                    T_Convert_Cube.Complete();
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
// #MAU_CHECKSUM:SHA256:93A1DD1A7A3C2582D4E817BAE936AA9D3FD4B72850464717B285AD00FA118F36