using System;
using System.Collections.Generic;
using System.IO;

namespace Mau.Runtime
{
    /// <summary>
    /// 配置存储——通用键值配置（文本资产 + 防御式读写）。
    /// 文件格式：每行 key=value；# 开头为注释；空行跳过。
    /// 缺文件 → 空配置（Get 返回默认值）；坏行 → 跳过。
    /// 写入 → 原子写（临时文件 + 改名），避免半写文件被读到。
    /// 使用方通过 DataBox.Bind&lt;ConfigStore&gt; 挂载为程序级服务。
    /// </summary>
    public sealed class ConfigStore
    {
        // [段1] 字段
        /// <summary>
        /// 配置表——键 → 值（Ordinal 比较）
        /// </summary>
        private readonly Dictionary<string, string> _values = new Dictionary<string, string>(StringComparer.Ordinal);

        /// <summary>
        /// 配置表锁——读写互斥
        /// </summary>
        private readonly object _gate = new object();

        /// <summary>
        /// 当前文件路径（空 = 未关联文件）
        /// </summary>
        private string _path = "";

        // [段2] 构造与加载
        /// <summary>
        /// 构造空配置存储
        /// </summary>
        public ConfigStore()
        {
        }

        /// <summary>
        /// 从文件加载配置——文件不存在返回空配置；坏行跳过
        /// </summary>
        /// <param name="path">配置文件路径</param>
        /// <returns>配置存储（已关联文件路径）</returns>
        public static ConfigStore Load(string path)
        {
            ConfigStore store = new ConfigStore();
            if (path == null || path.Length == 0)
            {
                return store;
            }
            store._path = path;
            if (!File.Exists(path))
            {
                return store;
            }
            string[] lines = File.ReadAllLines(path);
            for (int i = 0; i < lines.Length; i = i + 1)
            {
                string line = lines[i].Trim();
                if (line.Length == 0 || line.StartsWith("#"))
                {
                    continue;
                }
                int eq = line.IndexOf('=');
                if (eq <= 0)
                {
                    continue;
                }
                string key = line.Substring(0, eq).Trim();
                string value = line.Substring(eq + 1).Trim();
                if (key.Length == 0)
                {
                    continue;
                }
                lock (store._gate)
                {
                    store._values[key] = value;
                }
            }
            return store;
        }

        // [段3] 读取
        /// <summary>
        /// 读取配置值——不存在返回默认值
        /// </summary>
        /// <param name="key">键</param>
        /// <param name="defaultValue">默认值</param>
        /// <returns>配置值或默认值</returns>
        public string Get(string key, string defaultValue)
        {
            lock (_gate)
            {
                string? found;
                if (_values.TryGetValue(key, out found) && found != null)
                {
                    return found;
                }
                return defaultValue;
            }
        }

        /// <summary>
        /// 尝试读取配置值
        /// </summary>
        /// <param name="key">键</param>
        /// <param name="value">配置值（不存在为默认）</param>
        /// <returns>是否存在</returns>
        public bool TryGet(string key, out string value)
        {
            lock (_gate)
            {
                string? found;
                if (_values.TryGetValue(key, out found) && found != null)
                {
                    value = found;
                    return true;
                }
                value = "";
                return false;
            }
        }

        // [段4] 写入
        /// <summary>
        /// 设置配置值——覆盖已有值（内存生效，Save 落盘）
        /// </summary>
        /// <param name="key">键</param>
        /// <param name="value">值</param>
        public void Set(string key, string value)
        {
            if (key == null || key.Length == 0)
            {
                throw new ArgumentException("ConfigStore key is empty.", "key");
            }
            if (value == null)
            {
                throw new ArgumentException("ConfigStore value is null.", "value");
            }
            lock (_gate)
            {
                _values[key] = value;
            }
        }

        // [段5] 持久化
        /// <summary>
        /// 保存到当前文件路径——未关联路径时不动作
        /// </summary>
        public void Save()
        {
            if (_path.Length == 0)
            {
                return;
            }
            SaveTo(_path);
        }

        /// <summary>
        /// 保存到指定路径——原子写（临时文件 + 改名）
        /// </summary>
        /// <param name="path">目标路径</param>
        public void SaveTo(string path)
        {
            if (path == null || path.Length == 0)
            {
                throw new ArgumentException("ConfigStore path is empty.", "path");
            }
            lock (_gate)
            {
                string? dir = Path.GetDirectoryName(path);
                if (dir != null && dir.Length > 0)
                {
                    Directory.CreateDirectory(dir);
                }
                System.Text.StringBuilder sb = new System.Text.StringBuilder();
                KeyValuePair<string, string>[] pairs = All();
                for (int i = 0; i < pairs.Length; i = i + 1)
                {
                    sb.Append(pairs[i].Key);
                    sb.Append('=');
                    sb.Append(pairs[i].Value);
                    sb.AppendLine();
                }
                string tmp = path + ".tmp";
                File.WriteAllText(tmp, sb.ToString());
                File.Move(tmp, path, true);
            }
        }

        // [段6] 快照
        /// <summary>
        /// 全部键值对——快照（只读拷贝）
        /// </summary>
        /// <returns>键值对数组</returns>
        public KeyValuePair<string, string>[] All()
        {
            lock (_gate)
            {
                KeyValuePair<string, string>[] copy = new KeyValuePair<string, string>[_values.Count];
                int i = 0;
                foreach (KeyValuePair<string, string> pair in _values)
                {
                    copy[i] = pair;
                    i = i + 1;
                }
                return copy;
            }
        }
    }
}
