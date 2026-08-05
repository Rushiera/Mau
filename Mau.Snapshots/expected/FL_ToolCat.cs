// 本文件由 Mau Translator v0.1 自动生成 —— 请勿手改
// 流程: ToolCat
// 基座: Mau.Runtime/v0.1

using Mau.Runtime;
using System.Threading.Tasks;

namespace Mau.Generated.Flows
{
    /// <summary>
    /// ToolCat 流程——由 Mau 声明生成
    /// </summary>
    public sealed class FL_ToolCat : IObservableFlow, CH4.Contracts.ICat
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
        /// 命题 P_Read：信号，消费即清除
        /// </summary>
        private bool P_Read;

        /// <summary>
        /// 命题 P_Write：信号，消费即清除
        /// </summary>
        private bool P_Write;

        /// <summary>
        /// 命题 P_ReadDone：终态事实，置位后保持
        /// </summary>
        private bool P_ReadDone;

        /// <summary>
        /// 命题 P_ReadFailed：终态事实，置位后保持
        /// </summary>
        private bool P_ReadFailed;

        /// <summary>
        /// 命题 P_WriteDone：终态事实，置位后保持
        /// </summary>
        private bool P_WriteDone;

        /// <summary>
        /// 命题 P_WriteFailed：终态事实，置位后保持
        /// </summary>
        private bool P_WriteFailed;

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
        /// 构造：初始化日志缓冲
        /// </summary>
        public FL_ToolCat()
        {
            _logs = new FlowLog();
        }

        /// <summary>
        /// 外部投递信号：Read
        /// </summary>
        /// <param name="path">受控文件路径</param>
        public void FireRead(string path)
        {
            _path = path;
            P_Read = true;
        }

        /// <summary>
        /// 外部投递信号：Write
        /// </summary>
        /// <param name="path">受控文件路径</param>
        /// <param name="content">完整正文</param>
        public void FireWrite(string path, string content)
        {
            _path = path;
            _content = content;
            P_Write = true;
        }

        /// <summary>
        /// 每帧驱动——由主 Tick 调用
        /// </summary>
        public void Tick()
        {
            _frame = _frame + 1;
            // [T_Read] 前置检查
            if (P_Read)
            {
                // 信号消费
                P_Read = false;
                // 执行动作（积木调用）
                bool ok = Mau.Bricks.FileBrick.Read(_path, out _content);
                if (ok)
                {
                    // 正常后置注册
                    P_ReadDone = true;
                }
                else
                {
                    // 错误后置注册（互斥）
                    P_ReadFailed = true;
                }
            }

            // [T_Write] 前置检查
            if (P_Write)
            {
                // 信号消费
                P_Write = false;
                // 执行动作（积木调用）
                bool ok = Mau.Bricks.FileBrick.Write(_path, _content);
                if (ok)
                {
                    // 正常后置注册
                    P_WriteDone = true;
                }
                else
                {
                    // 错误后置注册（互斥）
                    P_WriteFailed = true;
                }
            }
        }

        /// <summary>
        /// 获取运行时状态快照——全量截面
        /// </summary>
        /// <returns>当前帧状态</returns>
        public RuntimeStatus GetStatus()
        {
            PropSnapshot[] props = new PropSnapshot[6];
            props[0] = new PropSnapshot("P_Read", "Signal", P_Read);
            props[1] = new PropSnapshot("P_Write", "Signal", P_Write);
            props[2] = new PropSnapshot("P_ReadDone", "Fact", P_ReadDone);
            props[3] = new PropSnapshot("P_ReadFailed", "Fact", P_ReadFailed);
            props[4] = new PropSnapshot("P_WriteDone", "Fact", P_WriteDone);
            props[5] = new PropSnapshot("P_WriteFailed", "Fact", P_WriteFailed);
            TransSnapshot[] trans = new TransSnapshot[2];
            trans[0] = new TransSnapshot("T_Read", "Idle", 0, 0);
            trans[1] = new TransSnapshot("T_Write", "Idle", 0, 0);
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
        /// 查询结果：ReadDone
        /// </summary>
        /// <returns>ReadDone成立</returns>
        public bool IsReadDone()
        {
            return P_ReadDone;
        }

        /// <summary>
        /// 重置结果：ReadDone
        /// </summary>
        public void ResetReadDone()
        {
            P_ReadDone = false;
        }

        /// <summary>
        /// 查询结果：ReadFailed
        /// </summary>
        /// <returns>ReadFailed成立</returns>
        public bool IsReadFailed()
        {
            return P_ReadFailed;
        }

        /// <summary>
        /// 重置结果：ReadFailed
        /// </summary>
        public void ResetReadFailed()
        {
            P_ReadFailed = false;
        }

        /// <summary>
        /// 查询结果：WriteDone
        /// </summary>
        /// <returns>WriteDone成立</returns>
        public bool IsWriteDone()
        {
            return P_WriteDone;
        }

        /// <summary>
        /// 重置结果：WriteDone
        /// </summary>
        public void ResetWriteDone()
        {
            P_WriteDone = false;
        }

        /// <summary>
        /// 查询结果：WriteFailed
        /// </summary>
        /// <returns>WriteFailed成立</returns>
        public bool IsWriteFailed()
        {
            return P_WriteFailed;
        }

        /// <summary>
        /// 重置结果：WriteFailed
        /// </summary>
        public void ResetWriteFailed()
        {
            P_WriteFailed = false;
        }

    }
}
// #MAU_CHECKSUM:SHA256:9235118F980D64A5A5DFA58ED0D263C84185F5B31F8BCE104413D5964AEDCFCA