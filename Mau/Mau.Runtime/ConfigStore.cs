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
        /// <summary>
        /// 多文件槽——键前缀段 → 文件路径（ui. → ui.cfg；无前缀段命中 → 主文件 _path）。P8.5d 配置群多文件化
        /// </summary>
        private readonly Dictionary<string, string> _fileByPrefix = new Dictionary<string, string>(StringComparer.Ordinal);
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
        /// <summary>
        /// 追加配置文件槽——键前缀段路由（如 "ui" → ui.cfg）；装载该文件全部键值并注册前缀映射。
        /// P8.5d 配置群多文件化：单一 ConfigStore 承载多配置文件，Set/Save 按键前缀段分组落盘。
        /// </summary>
        /// <param name="prefix">键前缀段（键 "ui.chat_font_size" 的前缀 "ui"）</param>
        /// <param name="path">配置文件路径</param>
        public void AddFile(string prefix, string path)
        {
            if (prefix == null || prefix.Length == 0)
            {
                throw new ArgumentException("ConfigStore file prefix is empty.", "prefix");
            }

            if (path == null || path.Length == 0)
            {
                throw new ArgumentException("ConfigStore file path is empty.", "path");
            }

            lock (_gate)
            {
                if (File.Exists(path))
                {
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

                        _values[key] = value;
                    }
                }

                _fileByPrefix[prefix] = path;
            }
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
                if (Audit != null)
                {
                    Audit.Record("ConfigStore", "cfg.change", -1, new AuditProp[] {
                        new AuditProp("key", key),
                        new AuditProp("value", AuditStore.Summarize(value))
                    });
                }
            }
        }
        /// <summary>
        /// 提取键前缀段——"ui.chat_font_size" → "ui"；无句点或空前缀返回空串（归主文件）
        /// </summary>
        /// <param name = "key">配置键</param>
        /// <returns>前缀段</returns>
        private static string KeyPrefix(string key)
        {
            int dot = key.IndexOf('.');
            if (dot <= 0)
            {
                return "";
            }

            return key.Substring(0, dot);
        }
        // [段5] 持久化
        /// <summary>
        /// 保存到当前文件路径——未关联路径时不动作
        /// </summary>
        public void Save()
        {
            lock (_gate)
            {
                // [段1] 按键前缀段分组——_fileByPrefix 命中 → 对应文件；否则主文件 _path
                Dictionary<string, System.Text.StringBuilder> builders = new Dictionary<string, System.Text.StringBuilder>(StringComparer.Ordinal);
                KeyValuePair<string, string>[] pairs = All();
                for (int i = 0; i < pairs.Length; i = i + 1)
                {
                    string key = pairs[i].Key;
                    string prefix = KeyPrefix(key);
                    string target = "";
                    string? mapped = "";
                    if (prefix.Length > 0 && _fileByPrefix.TryGetValue(prefix, out mapped) && mapped != null)
                    {
                        target = mapped;
                    }
                    else if (_path.Length > 0)
                    {
                        target = _path;
                    }
                    if (target.Length == 0)
                    {
                        continue;
                    }
                    System.Text.StringBuilder? sb;
                    if (!builders.TryGetValue(target, out sb))
                    {
                        sb = new System.Text.StringBuilder();
                        builders[target] = sb;
                    }
                    sb.Append(key);
                    sb.Append('=');
                    sb.Append(pairs[i].Value);
                    sb.AppendLine();
                }
                // [段2] 逐文件原子写（临时文件 + 改名）
                foreach (KeyValuePair<string, System.Text.StringBuilder> pair in builders)
                {
                    AtomicWrite(pair.Key, pair.Value.ToString());
                }
            }
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
        /// <summary>
        /// 审计存储——宿主注入后配置变更事件写入（null = 不审计）。零业务侵入：仅记录，不改流程。
        /// </summary>
        /// <summary>
        /// 审计存储——宿主注入后配置变更事件写入（null = 不审计）。零业务侵入：仅记录，不改流程。
        /// </summary>
        public AuditStore? Audit
        {
            get;
            set;
        }

        /// <summary>
        /// 原子写文件——临时文件 + 改名（防半写文件被读到；统一实现——AppRegistry/DogBase/FileSystemService 原四处独立，审查修复轮 2026-08-11 决策3 收拢）
        /// </summary>
        /// <param name="path">目标路径</param>
        /// <param name="content">内容</param>
        /// <param name="encoding">编码（默认 UTF-8 无 BOM）</param>
        public static void AtomicWrite(string path, string content, System.Text.Encoding? encoding = null)
        {
            string? dir = Path.GetDirectoryName(path);
            if (dir != null && dir.Length > 0)
            {
                Directory.CreateDirectory(dir);
            }
            string tmp = path + ".tmp";
            System.Text.Encoding effectiveEncoding;
            if (encoding == null)
            {
                effectiveEncoding = new System.Text.UTF8Encoding(false);
            }
            else
            {
                effectiveEncoding = encoding;
            }
            System.IO.File.WriteAllText(tmp, content, effectiveEncoding);
            System.IO.File.Move(tmp, path, true);
        }
        /// <summary>
        /// 受控配置写入——P8.5d 自改通道唯一实现（serve POST / config 积木 / 宿主直执共用）。
        /// 校验链：schema 声明 → writable 白名单 → 值域校验 → 掩码回写拒绝 → 旧值快照 → 写入落盘 → 失败回滚。
        /// </summary>
        /// <param name = "key">配置键（带文件前缀：ui.chat_font_size）</param>
        /// <param name = "value">新值</param>
        /// <param name = "schema">配置 schema（可 null——null 时仅基本校验）</param>
        /// <param name = "error">失败原因（成功为空串）</param>
        /// <returns>是否成功</returns>
        public bool SetChecked(string key, string value, ConfigSchema? schema, out string error)
        {
            error = "";
            if (key == null || key.Length == 0)
            {
                error = "key 为空";
                return false;
            }

            if (value == null)
            {
                error = "value 为空";
                return false;
            }

            if (schema != null)
            {
                ConfigSchema.Item? item = schema.Find(key);
                if (item == null)
                {
                    error = "配置键未在 schema 声明: " + key;
                    return false;
                }

                if (!item.Writable)
                {
                    error = "只读配置项: " + key;
                    return false;
                }

                if (!schema.Validate(key, value, out error))
                {
                    return false;
                }
            }

            // [段2] 掩码回写拒绝——防掩码值覆盖真实值
            if (value.IndexOf("****", StringComparison.Ordinal) >= 0)
            {
                error = "value 含掩码标记——请输入真实值";
                return false;
            }

            // [段3] 旧值快照 + 写入 + 失败回滚（写入异常时内存与磁盘一致恢复）
            bool hadOld;
            string? oldValue = "";
            lock (_gate)
            {
                hadOld = _values.TryGetValue(key, out oldValue);
            }

            try
            {
                Set(key, value);
                Save();
                return true;
            }
            catch (Exception ex)
            {
                try
                {
                    lock (_gate)
                    {
                        if (hadOld && oldValue != null)
                        {
                            _values[key] = oldValue;
                        }
                        else
                        {
                            _values.Remove(key);
                        }
                    }

                    Save();
                }
                catch (Exception)
                {
                    // 回滚再失败——内存保持一致，磁盘差异交由审计提示
                }

                error = "写入失败: " + ex.Message;
                return false;
            }
        }
        /// <summary>
        /// 配置还原默认——P8.5d：key 空 = 全群（仅 writable 项）；key 非空 = 单项。
        /// 语义：将值写为 schema default（落盘）；未声明/只读项拒绝。
        /// </summary>
        /// <param name = "key">配置键（空 = 全群）</param>
        /// <param name = "schema">配置 schema</param>
        /// <param name = "error">失败原因（成功为空串）</param>
        /// <returns>是否成功</returns>
        public bool ResetToDefault(string key, ConfigSchema? schema, out string error)
        {
            error = "";
            if (schema == null)
            {
                error = "schema 未绑定";
                return false;
            }
            // [段1] 目标集合——单项或全群 writable
            List<ConfigSchema.Item> targets = new List<ConfigSchema.Item>();
            if (key == null || key.Length == 0)
            {
                ConfigSchema.Item[] all = schema.All();
                for (int i = 0; i < all.Length; i = i + 1)
                {
                    if (all[i].Writable)
                    {
                        targets.Add(all[i]);
                    }
                }
            }
            else
            {
                ConfigSchema.Item? item = schema.Find(key);
                if (item == null)
                {
                    error = "配置键未在 schema 声明: " + key;
                    return false;
                }
                if (!item.Writable)
                {
                    error = "只读配置项: " + key;
                    return false;
                }
                targets.Add(item);
            }
            // [段2] 快照 + 逐项设默认 + 落盘（失败回滚）
            bool[] hadOld = new bool[targets.Count];
            string?[] oldValues = new string?[targets.Count];
            for (int i = 0; i < targets.Count; i = i + 1)
            {
                lock (_gate)
                {
                    hadOld[i] = _values.TryGetValue(targets[i].Key, out oldValues[i]);
                }
            }
            try
            {
                for (int i = 0; i < targets.Count; i = i + 1)
                {
                    Set(targets[i].Key, targets[i].Default);
                }
                Save();
                return true;
            }
            catch (Exception ex)
            {
                try
                {
                    for (int i = 0; i < targets.Count; i = i + 1)
                    {
                        lock (_gate)
                        {
                            string? oldVal = hadOld[i] ? oldValues[i] : null;
                            if (oldVal != null)
                            {
                                _values[targets[i].Key] = oldVal;
                            }
                            else
                            {
                                _values.Remove(targets[i].Key);
                            }
                        }
                    }
                    Save();
                }
                catch (Exception)
                {
                    // 回滚再失败——内存保持一致，磁盘差异交由审计提示
                }
                error = "还原失败: " + ex.Message;
                return false;
            }
        }
    }
}
