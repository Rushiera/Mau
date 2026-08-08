namespace Mau.Runtime
{
    /// <summary>
    /// PACK 接口基形——外部包能力经单方法调度（方法白名单 + argsJson 展平参数）。
    /// 与工具载荷 args.* 同构；低频调用可接受 JSON 序列化损失。
    /// 契约声明在 Bricks/PACK 接口积木文件头 `方法:` 字段（V11 校验）。
    /// </summary>
    public interface IPackBridge
    {
        /// <summary>
        /// 单方法调度——method 白名单 + argsJson 展平参数
        /// </summary>
        /// <param name="method">操作名（read/write/init 等）</param>
        /// <param name="argsJson">展平参数 JSON（{ "path": "...", "sheet": "..." }）</param>
        /// <param name="result">结果文本（错误时含 ERR| 前缀）</param>
        /// <returns>true=调用成功（业务错误码进 result）</returns>
        bool Invoke(string method, string argsJson, out string result);

        /// <summary>
        /// 释放资源——宿主 Shutdown 时调用
        /// </summary>
        void Shutdown();
    }
}
