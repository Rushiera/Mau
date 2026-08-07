using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text;

namespace Mau.Runtime
{
    /// <summary>
    /// 服务快照条目——已绑定服务类型 + 实例存在标记
    /// </summary>
    public sealed class DataBoxServiceEntry
    {
        /// <summary>
        /// 服务类型全名
        /// </summary>
        public string TypeName = "";

        /// <summary>
        /// 实例引用（快照只读——断言用）
        /// </summary>
        public object? Instance;
    }

    /// <summary>
    /// 数据快照条目——scope/key + 值（浅拷贝）
    /// </summary>
    public sealed class DataBoxDataEntry
    {
        /// <summary>
        /// 作用域
        /// </summary>
        public string Scope = "";

        /// <summary>
        /// 键
        /// </summary>
        public string Key = "";

        /// <summary>
        /// 值（浅拷贝——断言用）
        /// </summary>
        public object? Value;
    }

    /// <summary>
    /// DataBox 全量快照——测试断言/运行观测/审计的统一出口（只读）
    /// </summary>
    public sealed class DataBoxSnapshot
    {
        /// <summary>
        /// 服务条目
        /// </summary>
        public DataBoxServiceEntry[] Services;

        /// <summary>
        /// 数据条目
        /// </summary>
        public DataBoxDataEntry[] Data;

        /// <summary>
        /// 构造快照
        /// </summary>
        public DataBoxSnapshot()
        {
            Services = Array.Empty<DataBoxServiceEntry>();
            Data = Array.Empty<DataBoxDataEntry>();
        }

        /// <summary>
        /// 快照文本——观测面板/审计日志用
        /// </summary>
        /// <returns>多行文本</returns>
        public override string ToString()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("DataBox { ");
            if (Services.Length > 0)
            {
                sb.Append("Services[");
                for (int i = 0; i < Services.Length; i++)
                {
                    if (i > 0)
                    {
                        sb.Append(',');
                    }
                    sb.Append(Services[i].TypeName);
                }
                sb.Append("] ");
            }
            if (Data.Length > 0)
            {
                sb.Append("Data[");
                for (int i = 0; i < Data.Length; i++)
                {
                    if (i > 0)
                    {
                        sb.Append(',');
                    }
                    sb.Append(Data[i].Scope);
                    sb.Append('.');
                    sb.Append(Data[i].Key);
                }
                sb.Append("] ");
            }
            sb.Append('}');
            return sb.ToString();
        }
    }

    /// <summary>
    /// 全局数据中台——BRIK 的唯一数据协议（增查删改固有方式）。
    /// 服务 = Bind/Resolve（类型单例）；数据 = Set/Get（scope 作用域键值）。
    /// 机制归基座——所有 BRIK 的共享态/服务经由本中台，禁止绕过直连。
    /// </summary>
    public static class DataBox
    {
        // ── 服务区 ──
        /// <summary>
        /// 服务表锁
        /// </summary>
        private static readonly object _serviceGate = new object();

        /// <summary>
        /// 服务表——类型 → 实例（Bind/Resolve 单例）
        /// </summary>
        private static readonly Dictionary<Type, object> _services =
            new Dictionary<Type, object>();

        // ── 数据区 ──
        /// <summary>
        /// 数据表——scope → (key → value)；scope 级并发安全
        /// </summary>
        private static readonly ConcurrentDictionary<string, Dictionary<string, object>> _data =
            new ConcurrentDictionary<string, Dictionary<string, object>>(StringComparer.Ordinal);

        /// <summary>
        /// 绑定服务实例——类型单例；重复绑定 = 替换（最后生效，测试隔离友好）
        /// </summary>
        /// <typeparam name="T">服务类型</typeparam>
        /// <param name="instance">实例</param>
        /// <exception cref="ArgumentException">instance 为 null</exception>
        public static void Bind<T>(T instance) where T : class
        {
            if (instance == null)
            {
                throw new ArgumentException("DataBox.Bind instance is null.", "instance");
            }
            lock (_serviceGate)
            {
                _services[typeof(T)] = instance;
            }
        }

        /// <summary>
        /// 解除绑定——测试清理/宿主切换
        /// </summary>
        /// <typeparam name="T">服务类型</typeparam>
        public static void Unbind<T>() where T : class
        {
            lock (_serviceGate)
            {
                _services.Remove(typeof(T));
            }
        }

        /// <summary>
        /// 解析服务实例——未绑定返回 false
        /// </summary>
        /// <typeparam name="T">服务类型</typeparam>
        /// <param name="instance">实例（未绑定为 null）</param>
        /// <returns>是否已绑定</returns>
        public static bool TryResolve<T>(out T instance) where T : class
        {
            lock (_serviceGate)
            {
                object? value;
                if (_services.TryGetValue(typeof(T), out value) && value != null)
                {
                    instance = (T)value;
                    return true;
                }
                instance = null!;
                return false;
            }
        }

        /// <summary>
        /// 获取或创建数据——scope 作用域键值；不存在时创建默认实例
        /// </summary>
        /// <typeparam name="T">值类型（new() 约束）</typeparam>
        /// <param name="scope">作用域（非空）</param>
        /// <param name="key">键（非空）</param>
        /// <returns>值</returns>
        /// <exception cref="ArgumentException">scope/key 为空</exception>
        public static T GetOrCreate<T>(string scope, string key) where T : new()
        {
            string safeScope = ValidateKey(scope, "scope");
            string safeKey = ValidateKey(key, "key");
            Dictionary<string, object> box = _data.GetOrAdd(safeScope,
                delegate (string s)
                {
                    return new Dictionary<string, object>(StringComparer.Ordinal);
                });
            lock (box)
            {
                object? value;
                if (box.TryGetValue(safeKey, out value) && value is T)
                {
                    return (T)value;
                }
                T created = new T();
                box[safeKey] = created;
                return created;
            }
        }

        /// <summary>
        /// 读取数据——不存在返回 false
        /// </summary>
        /// <typeparam name="T">值类型</typeparam>
        /// <param name="scope">作用域</param>
        /// <param name="key">键</param>
        /// <param name="value">值（不存在或类型不匹配为默认）</param>
        /// <returns>是否存在且类型匹配</returns>
        public static bool TryGet<T>(string scope, string key, out T value)
        {
            string safeScope = ValidateKey(scope, "scope");
            string safeKey = ValidateKey(key, "key");
            Dictionary<string, object>? box;
            if (!_data.TryGetValue(safeScope, out box) || box == null)
            {
                value = default!;
                return false;
            }
            lock (box)
            {
                object? raw;
                if (box.TryGetValue(safeKey, out raw) && raw is T)
                {
                    value = (T)raw;
                    return true;
                }
                value = default!;
                return false;
            }
        }

        /// <summary>
        /// 写入数据——覆盖已有值
        /// </summary>
        /// <typeparam name="T">值类型</typeparam>
        /// <param name="scope">作用域</param>
        /// <param name="key">键</param>
        /// <param name="value">值</param>
        public static void Set<T>(string scope, string key, T value)
        {
            string safeScope = ValidateKey(scope, "scope");
            string safeKey = ValidateKey(key, "key");
            if (value == null)
            {
                throw new ArgumentException("DataBox.Set value is null.", "value");
            }
            Dictionary<string, object> box = _data.GetOrAdd(safeScope,
                delegate (string s)
                {
                    return new Dictionary<string, object>(StringComparer.Ordinal);
                });
            lock (box)
            {
                box[safeKey] = value;
            }
        }

        /// <summary>
        /// 删除数据——不存在静默成功
        /// </summary>
        /// <param name="scope">作用域</param>
        /// <param name="key">键</param>
        /// <returns>是否删除了条目</returns>
        public static bool Remove(string scope, string key)
        {
            string safeScope = ValidateKey(scope, "scope");
            string safeKey = ValidateKey(key, "key");
            Dictionary<string, object>? box;
            if (!_data.TryGetValue(safeScope, out box) || box == null)
            {
                return false;
            }
            lock (box)
            {
                return box.Remove(safeKey);
            }
        }

        /// <summary>
        /// 清空作用域全部数据
        /// </summary>
        /// <param name="scope">作用域</param>
        public static void ClearScope(string scope)
        {
            string safeScope = ValidateKey(scope, "scope");
            _data.TryRemove(safeScope, out _);
        }

        /// <summary>
        /// 清空全部——服务 + 数据（宿主重启/全量测试隔离）
        /// </summary>
        public static void ClearAll()
        {
            lock (_serviceGate)
            {
                _services.Clear();
            }
            _data.Clear();
        }

        /// <summary>
        /// 全量快照——只读深拷贝（测试断言/观测/审计统一出口）
        /// </summary>
        /// <returns>快照</returns>
        public static DataBoxSnapshot Capture()
        {
            DataBoxSnapshot snapshot = new DataBoxSnapshot();
            lock (_serviceGate)
            {
                DataBoxServiceEntry[] services = new DataBoxServiceEntry[_services.Count];
                int si = 0;
                foreach (KeyValuePair<Type, object> pair in _services)
                {
                    DataBoxServiceEntry entry = new DataBoxServiceEntry();
                    entry.TypeName = pair.Key.FullName ?? pair.Key.Name;
                    entry.Instance = pair.Value;
                    services[si] = entry;
                    si++;
                }
                snapshot.Services = services;
            }
            List<DataBoxDataEntry> entries = new List<DataBoxDataEntry>();
            foreach (KeyValuePair<string, Dictionary<string, object>> pair in _data)
            {
                Dictionary<string, object> box = pair.Value;
                lock (box)
                {
                    foreach (KeyValuePair<string, object> kv in box)
                    {
                        DataBoxDataEntry entry = new DataBoxDataEntry();
                        entry.Scope = pair.Key;
                        entry.Key = kv.Key;
                        entry.Value = kv.Value;
                        entries.Add(entry);
                    }
                }
            }
            snapshot.Data = entries.ToArray();
            return snapshot;
        }

        /// <summary>
        /// DataBox 键校验——空/空白拒绝（严格封装铁律）
        /// </summary>
        /// <param name="value">原始值</param>
        /// <param name="paramName">参数名</param>
        /// <returns>原值（非空）</returns>
        private static string ValidateKey(string value, string paramName)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException("DataBox key/scope is empty.", paramName);
            }
            return value;
        }
    }
}
