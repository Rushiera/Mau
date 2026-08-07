using System;
using System.Collections.Generic;

namespace Mau.Runtime
{
    /// <summary>
    /// 凭证存储（程序级）——API Key 等敏感值隔离区，不入 DataBox（Capture/快照/观察器不可枚举）
    /// 安全审查项：DataBox.Capture 全量输出曾暴露 llm.apiKey——凭证类一律走本存储
    /// </summary>
    public static class CredentialStore
    {
        /// <summary>
        /// 凭证表锁
        /// </summary>
        private static readonly object Gate = new object();

        /// <summary>
        /// 凭证表——键 → 值（内存态；不落盘不入快照不枚举）
        /// </summary>
        private static readonly Dictionary<string, string> _secrets =
            new Dictionary<string, string>(StringComparer.Ordinal);

        /// <summary>
        /// 写入凭证——覆盖已有值；空值=清除
        /// </summary>
        /// <param name="key">凭证键</param>
        /// <param name="value">值（空=清除）</param>
        public static void Set(string key, string value)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                throw new ArgumentException("Credential key is empty.", "key");
            }
            lock (Gate)
            {
                if (value == null || value.Length == 0)
                {
                    _secrets.Remove(key);
                }
                else
                {
                    _secrets[key] = value;
                }
            }
        }

        /// <summary>
        /// 读取凭证——不存在返回空串
        /// </summary>
        /// <param name="key">凭证键</param>
        /// <returns>值或空串</returns>
        public static string Get(string key)
        {
            lock (Gate)
            {
                string? value;
                if (_secrets.TryGetValue(key, out value) && value != null)
                {
                    return value;
                }
                return "";
            }
        }

        /// <summary>
        /// 是否存在凭证
        /// </summary>
        /// <param name="key">凭证键</param>
        /// <returns>存在为真</returns>
        public static bool Has(string key)
        {
            lock (Gate)
            {
                return _secrets.ContainsKey(key);
            }
        }

        /// <summary>
        /// 清空全部凭证——宿主退出/测试隔离
        /// </summary>
        public static void ClearAll()
        {
            lock (Gate)
            {
                _secrets.Clear();
            }
        }
    }
}
