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
/// <summary>
/// 重置全部凭证——统一 Reset 契约（D26；宿主切换/测试隔离调用）
/// </summary>
public static void Reset()
{
    ClearAll();
}
        /// <summary>
        /// 从 AppDataConfig 加载持久化凭证（宿主启动时调用）——llm.apiKey.* 键入内存。
        /// 持久化介质：%LOCALAPPDATA%/Mau_wls/CatHome4/llm.cfg（明文——个人工具链）。
        /// </summary>
        public static void LoadPersisted()
{
            System.Collections.Generic.KeyValuePair<string, string>[] all = AppDataConfig.All();
            lock (Gate)
            {
                // 先清后载——介质中已删除的凭证不残留内存（D27 绑定先清空）
                System.Collections.Generic.List<string> stale = new System.Collections.Generic.List<string>();
                foreach (System.Collections.Generic.KeyValuePair<string, string> pair in _secrets)
                {
                    if (pair.Key != null
                        && (pair.Key == "llm.apiKey"
                            || pair.Key.StartsWith("llm.apiKey.", StringComparison.Ordinal)))
                    {
                        stale.Add(pair.Key);
                    }
                }
                for (int i = 0; i < stale.Count; i = i + 1)
                {
                    _secrets.Remove(stale[i]);
                }
                for (int i = 0; i < all.Length; i = i + 1)
                {
                    string key = all[i].Key;
                    // 单配置（llm.apiKey）+ 多档案（llm.apiKey.{id}）都加载
                    if (key != null
                        && (key == "llm.apiKey"
                            || key.StartsWith("llm.apiKey.", StringComparison.Ordinal)))
                    {
                        _secrets[key] = all[i].Value;
                    }
                }
            }
        }
        /// <summary>
        /// 保存全部凭证到 AppDataConfig（写入后调用）——内存态保持，文件是持久化介质
        /// </summary>
        public static void SavePersisted()
        {
            System.Collections.Generic.KeyValuePair<string, string>[] snapshot;
            lock (Gate)
            {
                snapshot = new System.Collections.Generic.KeyValuePair<string, string>[_secrets.Count];
                int i = 0;
                foreach (System.Collections.Generic.KeyValuePair<string, string> pair in _secrets)
                {
                    snapshot[i] = pair;
                    i = i + 1;
                }
            }
            AppDataConfig.SetMany(snapshot);
        }
    }
}
