using System;

namespace Mau.Runtime
{
    /// <summary>
    /// 程序身份——Mau 构筑程序的注册数据（mau-app.json 内容）
    /// 进程管理协议：启动注册、退出注销、Supervisor 按 PID 存活检测
    /// </summary>
    public sealed class AppIdentity
    {
        /// <summary>
        /// 程序名（唯一标识——注册文件名）
        /// </summary>
        public string Name = null!;

        /// <summary>
        /// 程序版本
        /// </summary>
        public string Version = "";

        /// <summary>
        /// 进程 ID——存活检测依据
        /// </summary>
        public int Pid;

        /// <summary>
        /// 入口 exe 路径
        /// </summary>
        public string Exe = "";

        /// <summary>
        /// 启动时间（yyyy-MM-dd HH:mm:ss）
        /// </summary>
        public string StartedAt = "";

        /// <summary>
        /// 快照管道名——NamedPipe 协议；空=未开管道
        /// </summary>
        public string PipeName = "";

        /// <summary>
        /// 已加载模块数
        /// </summary>
        public int Modules;

        /// <summary>
        /// 状态——running/stopping/zombie（zombie 由 Supervisor 标记）
        /// </summary>
        public string State = "running";

        /// <summary>
        /// 构造默认身份
        /// </summary>
        public AppIdentity()
        {
        }
    }
}
