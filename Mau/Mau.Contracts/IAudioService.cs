namespace Mau.Contracts
{
    /// <summary>
    /// 音频播放服务——per-Key 串行 + 组间并发（投递即返回，只进不出；design-mau-audio §四）。
    /// 契约面只用 BCL 类型（string / float / int）——实现（NAudio 等）不泄漏到此。
    /// </summary>
    public interface IAudioService
    {
        /// <summary>
        /// 入队——某 Key 无在播则立即起播，否则按 Key 排队（先来后到），立即返回。
        /// </summary>
        /// <param name="key">串行分组键（空 = 默认 Key——所有空 Key 音频自动串行）</param>
        /// <param name="path">WAV 文件路径</param>
        /// <param name="volume">会话音量（&lt; 0 = 用全局配置 audio.volume；≥ 0 = 覆盖；design-mau-audio §五）</param>
        /// <returns>已受理返回空串；失败返回 "ERR|CODE|说明"（可见，不静默）</returns>
        string Enqueue(string key, string path, float volume);

        /// <summary>
        /// 活跃 Key 数——诊断哨兵（当前有音频在播的 Key 数）。
        /// </summary>
        /// <returns>活跃 Key 数</returns>
        int ActiveKeyCount();
    }
}
