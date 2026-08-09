using System;
using System.Collections.Generic;
using System.IO;

namespace Mau.Runtime
{
    /// <summary>
    /// AppData 配置服务（程序级）——用户级配置持久化介质（明文落盘——个人工具链定位）。
    /// 路径：%LOCALAPPDATA%/Mau_wls/CatHome4/llm.cfg（Mau 主菜单名 + wls=雾理莎 作者名——防撞车）。
    /// 用途：LLM 档案 + 密钥持久化（LlmBridge 默认绑定 + CredentialStore 落盘介质）。
    /// 安全：明文不防（个人工具）——密钥不入 DataBox/快照/枚举（CredentialStore 内存态保持）。
    /// </summary>
    public static class AppDataConfig
    {
        /// <summary>
        /// 根目录——%LOCALAPPDATA%/Mau_wls/CatHome4（测试可 ConfigureRoot 覆盖）
        /// </summary>
        private static string _rootDir = "";

        /// <summary>
        /// 存储锁
        /// </summary>
        private static readonly object Gate = new object();

        /// <summary>
        /// 懒加载存储实例
        /// </summary>
        private static ConfigStore? _store;

        /// <summary>
        /// AppData 根目录——默认 %LOCALAPPDATA%/Mau_wls/CatHome4
        /// </summary>
        public static string RootDir
        {
            get
            {
                if (_rootDir.Length == 0)
                {
                    string baseDir = Environment.GetFolderPath(
                        Environment.SpecialFolder.LocalApplicationData);
                    if (baseDir.Length == 0)
                    {
                        baseDir = Environment.GetEnvironmentVariable("LOCALAPPDATA") ?? "";
                    }
                    if (baseDir.Length == 0)
                    {
                        baseDir = Path.GetTempPath();
                    }
                    _rootDir = Path.Combine(baseDir, "Mau_wls", "CatHome4");
                }
                return _rootDir;
            }
        }

        /// <summary>
        /// 主配置文件路径——llm.cfg
        /// </summary>
        public static string ConfigPath
        {
            get { return Path.Combine(RootDir, "llm.cfg"); }
        }

        /// <summary>
        /// 配置存储——懒加载（llm.cfg；文件不存在 → 空配置）
        /// </summary>
        public static ConfigStore Store
        {
            get
            {
                lock (Gate)
                {
                    if (_store == null)
                    {
                        _store = ConfigStore.Load(ConfigPath);
                    }
                    return _store;
                }
            }
        }

        /// <summary>
        /// 覆盖根目录——测试注入临时目录（生产不调用）
        /// </summary>
        /// <param name="dir">根目录</param>
        public static void ConfigureRoot(string dir)
        {
            lock (Gate)
            {
                _rootDir = dir == null ? "" : dir;
                _store = null;
            }
        }

        /// <summary>
        /// 读取配置值——不存在返回默认值
        /// </summary>
        /// <param name="key">键</param>
        /// <param name="defaultValue">默认值</param>
        /// <returns>配置值或默认值</returns>
        public static string Get(string key, string defaultValue)
        {
            return Store.Get(key, defaultValue);
        }

        /// <summary>
        /// 写配置——内存生效 + 原子写落盘（单键）
        /// </summary>
        /// <param name="key">键</param>
        /// <param name="value">值</param>
        public static void Set(string key, string value)
        {
            lock (Gate)
            {
                Store.Set(key, value);
                Store.Save();
            }
        }

        /// <summary>
        /// 批量写配置——内存生效 + 一次原子写落盘（避免逐条 Save）
        /// </summary>
        /// <param name="pairs">键值对</param>
        public static void SetMany(KeyValuePair<string, string>[] pairs)
        {
            if (pairs == null || pairs.Length == 0)
            {
                return;
            }
            lock (Gate)
            {
                ConfigStore store = Store;
                for (int i = 0; i < pairs.Length; i = i + 1)
                {
                    store.Set(pairs[i].Key, pairs[i].Value);
                }
                store.Save();
            }
        }

        /// <summary>
        /// 全部键值对——只读快照
        /// </summary>
        /// <returns>键值对数组</returns>
        public static KeyValuePair<string, string>[] All()
        {
            return Store.All();
        }
    }
}
