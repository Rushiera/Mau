namespace Mau.Runtime
{
    /// <summary>
    /// 图像识别服务接口——image-analyze 工具的执行面（R2.2 工具组）。
    /// 宿主注入实现（Mau.Providers.DeepSeekVisionService——deepseek-v4-flash-vision-exp 视觉模型，chat completions 带图 content 块）；
    /// 语料经 vision.analyze 积木（VISION 类别）触达——DataBox.TryResolve 面。
    /// 配置：vision.api_config_id（引用 LLM 池配置——未配置=不可用）+ vision.endpoint/model/timeout_ms/detail（可选覆盖）。
    /// </summary>
    public interface IVisionService
    {
        /// <summary>
        /// 执行一次图像识别——读取图片（本地路径 base64 内联 / 外部 URL 直传）→ 视觉模型分析 → 返回文本。
        /// 同步执行（阻塞调用线程；R2.2 拍板——与 web-search 同构走等待）。
        /// </summary>
        /// <param name="imagePath">图片路径（本地绝对路径或 http(s) URL）</param>
        /// <param name="question">提示词（对图片的提问/指令）</param>
        /// <returns>分析结果文本（失败 ERR| 前缀——错误可见性）</returns>
        string Analyze(string imagePath, string question);
    }
}
