using System;
using System.IO;

namespace Mau.Providers
{
    /// <summary>
    /// 图片载荷构建——本地路径 / 外部 URL → `image_url` 可用串（单一实现，多处共用）。
    /// 消费方：vision 服务（image-analyze）· 对话请求构造（user 消息附件展开 · design-ch4-chat-images §8.2）。
    /// 本地：读取字节 → 32MiB 上限检查 → base64 data URL（media type 由扩展名推断，兜底 image/jpeg）。
    /// 外部：http(s) URL 直传（长度 ≤8192 校验）。
    /// </summary>
    internal static class ImagePayload
    {
        /// <summary>
        /// 单图最大字节——base64 内联 32 MiB 上限（视觉 API 通用上限）。
        /// </summary>
        internal const long MaxInlineImageBytes = 32L * 1024 * 1024;

        /// <summary>
        /// 外部图片 URL 最大长度——超长改走本地路径。
        /// </summary>
        private const int MaxUrlLength = 8192;

        /// <summary>
        /// 构建图片载荷——本地路径 base64 内联 / 外部 URL 直传（官方文档：格式由文件实际内容判断）。
        /// </summary>
        /// <param name="imagePath">图片路径（本地绝对路径或 http(s) URL）</param>
        /// <param name="error">错误文本（空=成功；失败为 ERR| 前缀——错误可见性）</param>
        /// <returns>image_url 串（data URL 或外部 URL；失败空串）</returns>
        internal static string Resolve(string imagePath, out string error)
        {
            error = "";
            if (imagePath == null || imagePath.Length == 0)
            {
                error = "ERR|BAD_ARGS|缺少图片路径";
                return "";
            }
            string trimmed = imagePath.Trim();
            if (trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                if (trimmed.Length > MaxUrlLength)
                {
                    error = "ERR|IMAGE_URL_TOO_LONG|外部图片 URL 超长（>" + MaxUrlLength.ToString() + " 字符——请改用本地路径）";
                    return "";
                }
                return trimmed;
            }
            if (!File.Exists(trimmed))
            {
                error = "ERR|IMAGE_NOT_FOUND|图片文件不存在: " + trimmed;
                return "";
            }
            long length = new FileInfo(trimmed).Length;
            if (length > MaxInlineImageBytes)
            {
                error = "ERR|IMAGE_TOO_LARGE|图片超过 32MiB 内联上限（" + length.ToString() + " 字节——请压缩或改用 Files API）";
                return "";
            }
            byte[] bytes;
            try
            {
                bytes = File.ReadAllBytes(trimmed);
            }
            catch (Exception ex)
            {
                error = "ERR|IMAGE_READ|图片读取失败: " + ex.Message;
                return "";
            }
            string mime = GuessImageMime(trimmed);
            return "data:" + mime + ";base64," + Convert.ToBase64String(bytes);
        }

        /// <summary>
        /// 图片 media type 推断——扩展名映射（官方：格式由内容判断，media type 兜底 image/jpeg）。
        /// </summary>
        /// <param name="path">图片路径</param>
        /// <returns>media type</returns>
        internal static string GuessImageMime(string path)
        {
            string ext = Path.GetExtension(path).ToLowerInvariant();
            if (ext == ".jpg" || ext == ".jpeg")
            {
                return "image/jpeg";
            }
            if (ext == ".png")
            {
                return "image/png";
            }
            if (ext == ".gif")
            {
                return "image/gif";
            }
            if (ext == ".webp")
            {
                return "image/webp";
            }
            return "image/jpeg";
        }
    }
}
