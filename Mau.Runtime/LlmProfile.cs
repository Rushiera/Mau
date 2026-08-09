using System;

namespace Mau.Runtime
{
    /// <summary>
    /// LLM API 配置档案——一份不含密钥的 API 连接配置（密钥走 CredentialStore 隔离）。
    /// ProfileId 稳定身份；DisplayName 用户可见名；ApiType 决定 Provider 适配（deepseek/fake/…）；
    /// Endpoint 绝对端点；Model 默认模型。持久化经 ConfigStore（宿主注入 LlmBridge.ConfigureProfileStore）。
    /// </summary>
    public sealed class LlmProfile
    {
        /// <summary>
        /// 稳定配置身份（Guid N 格式）
        /// </summary>
        public string ProfileId = "";

        /// <summary>
        /// 用户可见名称
        /// </summary>
        public string DisplayName = "";

        /// <summary>
        /// API 类型——deepseek/fake 等（小写）
        /// </summary>
        public string ApiType = "";

        /// <summary>
        /// 绝对 API 端点
        /// </summary>
        public string Endpoint = "";

        /// <summary>
        /// 默认模型
        /// </summary>
        public string Model = "";

        /// <summary>
        /// 构造空档案
        /// </summary>
        public LlmProfile()
        {
        }
    }
}
