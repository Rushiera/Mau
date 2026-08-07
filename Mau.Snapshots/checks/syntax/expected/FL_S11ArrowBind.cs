// 本文件由 Mau Translator v0.1 自动生成 —— 请勿手改
// 流程: S11ArrowBind
// 基座: Mau.Runtime/v0.1

using Mau.Runtime;
using System.Threading.Tasks;
using System;
using System.Globalization;

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
        /// <param name="path">参数 path</param>
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
                bool ok = Mau.Bricks.BRIK_FILE_002.Read(_path, out _content);
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
                bool ok = Mau.Bricks.BRIK_MATH_003.ResultPreview(_content, out _preview);
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

    // #BRICK:BRIK-FILE-002 BEGIN
namespace Mau.Bricks
{
    /// <summary>
    /// 文件积木——file.read 读取文本（依赖 FileBridge）
    /// </summary>
    public static class BRIK_FILE_002
    {
        /// <summary>
        /// 读取 UTF-8 文本
        /// </summary>
        /// <param name="path">受控路径</param>
        /// <param name="content">完整文本——成功时填充</param>
        /// <returns>true=成功</returns>
        public static bool Read(string path, out string content)
        {
            try
            {
                content = FileBridge.CurrentFileSystem().ReadText(path);
                return true;
            }
            catch (Exception ex)
            {
                content = "ERR|" + ex.GetType().Name + "|" + ex.Message;
                return false;
            }
        }
    }
}

    // #BRICK:BRIK-FILE-002 END
    // #BRICK:BRIK-MATH-002 BEGIN
namespace Mau.Bricks
{
    /// <summary>
    /// 数学积木——math.format_size 按十进制 B/K/M 阈值格式化数量
    /// </summary>
    public static class BRIK_MATH_002
    {
        /// <summary>
        /// 按十进制 B/K/M 阈值格式化数量
        /// </summary>
        /// <param name="bytes">数量</param>
        /// <param name="formatted">格式文本</param>
        /// <returns>true=成功</returns>
        public static bool FormatSize(long bytes, out string formatted)
        {
            if (bytes >= 1000000)
            {
                formatted = (bytes / 1000000.0).ToString("F2", CultureInfo.InvariantCulture) + " M";
                return true;
            }
            if (bytes >= 1000)
            {
                formatted = (bytes / 1000.0).ToString("F2", CultureInfo.InvariantCulture) + " K";
                return true;
            }
            formatted = bytes.ToString(CultureInfo.InvariantCulture) + " B";
            return true;
        }
    }
}

    // #BRICK:BRIK-MATH-002 END
    // #BRICK:BRIK-MATH-003 BEGIN
namespace Mau.Bricks
{
    /// <summary>
    /// 数学积木——math.result_preview 生成单行结果预览（依赖 math.format_size）
    /// </summary>
    public static class BRIK_MATH_003
    {
        /// <summary>
        /// 生成单行、最多十五 UTF-16 字符头部的结果预览
        /// </summary>
        /// <param name="result">完整工具结果</param>
        /// <param name="preview">预览文本——OK 或空结果返回空字符串，否则返回预览</param>
        /// <returns>true=成功</returns>
        public static bool ResultPreview(string? result, out string preview)
        {
            if (string.IsNullOrEmpty(result))
            {
                preview = "";
                return true;
            }
            if (result == "OK" || result.StartsWith("OK\n", StringComparison.Ordinal)
                || result.StartsWith("OK\r", StringComparison.Ordinal))
            {
                preview = "";
                return true;
            }
            int headLength = result.Length;
            if (headLength > 15)
            {
                headLength = 15;
                if (headLength < result.Length && headLength > 0
                    && char.IsHighSurrogate(result[headLength - 1])
                    && char.IsLowSurrogate(result[headLength]))
                {
                    headLength = headLength + 1;
                }
            }
            string head = result.Substring(0, headLength).Replace('\r', ' ').Replace('\n', ' ');
            string sizeText = "";
            BRIK_MATH_002.FormatSize(result.Length, out sizeText);
            preview = "（" + head + "…总" + sizeText + "）";
            return true;
        }
    }
}

    // #BRICK:BRIK-MATH-003 END
// #MAU_CHECKSUM:SHA256:2A340650FCF7A29E0ACEFFCB9BC9D911557AEFA233F6679648C2CFACAF2868CC
