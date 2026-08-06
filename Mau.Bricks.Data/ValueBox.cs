// ═══════════════════════════════════════════════
// 积木: data.box_set / data.box_get / data.box_set_dic / data.box_get_dic
// ID:   BRIK-DATA-003 ~ 006
// 作用: 静态值盒存储——跨状态持久化的键值存储（单值 + 数据包双轨）
// 引用: Mau.Bricks.Data → Mau.Contracts（BrickRegistry）· System.Text.Json
// 依赖: 无
// 原理: 静态字典存储 + JSON 序列化——boxId 隔离作用域，深副本语义
// 常用: 跨帧状态保留 / 模块私有数据缓存 / 可序列化暂存
// ═══════════════════════════════════════════════
using System;
using System.Collections.Generic;
using System.Text.Json;
using Mau.Contracts;

namespace Mau.Bricks
{
    /// <summary>
    /// 静态值盒积木——boxId 作用域隔离的键值存储。单值（int）与命名数据包（JSON）双轨。
    /// 由 CH3 ValueBox 实例语义移植为静态存储形态（积木调用必须静态方法）。
    /// </summary>
    public static class BoxBrick
    {
        /// <summary>
        /// 单值存储——boxId → (key → value)
        /// </summary>
        private static readonly Dictionary<string, Dictionary<string, int>> _values =
            new Dictionary<string, Dictionary<string, int>>(StringComparer.Ordinal);

        /// <summary>
        /// 数据包存储——boxId → (packetKey → JSON)
        /// </summary>
        private static readonly Dictionary<string, Dictionary<string, string>> _packets =
            new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);

        /// <summary>
        /// 写入单值
        /// </summary>
        /// <param name="boxId">作用域 ID</param>
        /// <param name="key">键</param>
        /// <param name="value">值</param>
        /// <returns>true=成功</returns>
        public static bool Set(string boxId, string key, int value)
        {
            try
            {
                Dictionary<string, int> box = GetOrCreateValues(boxId);
                box[SafeKey(key, "key")] = value;
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 读取单值
        /// </summary>
        /// <param name="boxId">作用域 ID</param>
        /// <param name="key">键</param>
        /// <param name="defaultValue">默认值</param>
        /// <param name="value">读取值</param>
        /// <returns>true=成功</returns>
        public static bool Get(string boxId, string key, int defaultValue, out int value)
        {
            value = defaultValue;
            try
            {
                Dictionary<string, int>? box;
                if (_values.TryGetValue(SafeKey(boxId, "boxId"), out box) && box != null)
                {
                    int v;
                    if (box.TryGetValue(SafeKey(key, "key"), out v))
                    {
                        value = v;
                        return true;
                    }
                }
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 写入数据包（JSON 文本）
        /// </summary>
        /// <param name="boxId">作用域 ID</param>
        /// <param name="packetKey">包名</param>
        /// <param name="dataJson">JSON 对象文本</param>
        /// <returns>true=成功</returns>
        public static bool SetDic(string boxId, string packetKey, string dataJson)
        {
            try
            {
                using JsonDocument doc = JsonDocument.Parse(dataJson);
                if (doc.RootElement.ValueKind != JsonValueKind.Object)
                {
                    return false;
                }
                Dictionary<string, string> box = GetOrCreatePackets(boxId);
                box[SafeKey(packetKey, "packetKey")] = dataJson;
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 读取数据包（JSON 文本）
        /// </summary>
        /// <param name="boxId">作用域 ID</param>
        /// <param name="packetKey">包名</param>
        /// <param name="dataJson">JSON 对象文本</param>
        /// <returns>true=成功</returns>
        public static bool GetDic(string boxId, string packetKey, out string dataJson)
        {
            dataJson = "{}";
            try
            {
                Dictionary<string, string>? box;
                string? json;
                if (_packets.TryGetValue(SafeKey(boxId, "boxId"), out box) && box != null
                    && box.TryGetValue(SafeKey(packetKey, "packetKey"), out json)
                    && json != null)
                {
                    dataJson = json;
                    return true;
                }
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 清空一个作用域
        /// </summary>
        /// <param name="boxId">作用域 ID</param>
        /// <returns>true=成功</returns>
        public static bool Clear(string boxId)
        {
            try
            {
                string id = SafeKey(boxId, "boxId");
                _values.Remove(id);
                _packets.Remove(id);
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 获取或创建单值作用域
        /// </summary>
        /// <param name="boxId">作用域 ID</param>
        /// <returns>作用域字典</returns>
        private static Dictionary<string, int> GetOrCreateValues(string boxId)
        {
            string id = SafeKey(boxId, "boxId");
            Dictionary<string, int>? box;
            if (!_values.TryGetValue(id, out box))
            {
                box = new Dictionary<string, int>(StringComparer.Ordinal);
                _values[id] = box;
            }
            return box;
        }

        /// <summary>
        /// 获取或创建数据包作用域
        /// </summary>
        /// <param name="boxId">作用域 ID</param>
        /// <returns>作用域字典</returns>
        private static Dictionary<string, string> GetOrCreatePackets(string boxId)
        {
            string id = SafeKey(boxId, "boxId");
            Dictionary<string, string>? box;
            if (!_packets.TryGetValue(id, out box))
            {
                box = new Dictionary<string, string>(StringComparer.Ordinal);
                _packets[id] = box;
            }
            return box;
        }

        /// <summary>
        /// 键合法性校验
        /// </summary>
        /// <param name="value">键值</param>
        /// <param name="parameterName">参数名</param>
        /// <returns>合法键</returns>
        private static string SafeKey(string value, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length > 256)
            {
                throw new ArgumentException("ValueBox key is invalid.", parameterName);
            }
            return value;
        }
    }

    /// <summary>
    /// 值盒积木注册——进程启动时调用一次
    /// </summary>
    public static class BoxBrickRegistration
    {
        /// <summary>
        /// 注册全部值盒积木
        /// </summary>
        public static void RegisterAll()
        {
            RegisterBoxSet();
            RegisterBoxGet();
            RegisterBoxSetDic();
            RegisterBoxGetDic();
        }

        /// <summary>
        /// 注册 data.box_set
        /// </summary>
        private static void RegisterBoxSet()
        {
            BrickContract contract = new BrickContract("data.box_set", "Mau.Bricks.BoxBrick.Set");
            contract.Inputs.Add(new BrickPort("boxId", typeof(string), "作用域 ID"));
            contract.Inputs.Add(new BrickPort("key", typeof(string), "键"));
            contract.Inputs.Add(new BrickPort("value", typeof(int), "值"));
            contract.Return = BrickReturnKind.Bool;
            contract.Duration = BrickDuration.Sync;
            contract.Thread = "main";
            BrickRegistry.Register(contract);
        }

        /// <summary>
        /// 注册 data.box_get
        /// </summary>
        private static void RegisterBoxGet()
        {
            BrickContract contract = new BrickContract("data.box_get", "Mau.Bricks.BoxBrick.Get");
            contract.Inputs.Add(new BrickPort("boxId", typeof(string), "作用域 ID"));
            contract.Inputs.Add(new BrickPort("key", typeof(string), "键"));
            contract.Inputs.Add(new BrickPort("defaultValue", typeof(int), "默认值"));
            contract.Outputs.Add(new BrickPort("value", typeof(int), "读取值"));
            contract.Return = BrickReturnKind.Bool;
            contract.Duration = BrickDuration.Sync;
            contract.Thread = "main";
            BrickRegistry.Register(contract);
        }

        /// <summary>
        /// 注册 data.box_set_dic
        /// </summary>
        private static void RegisterBoxSetDic()
        {
            BrickContract contract = new BrickContract("data.box_set_dic", "Mau.Bricks.BoxBrick.SetDic");
            contract.Inputs.Add(new BrickPort("boxId", typeof(string), "作用域 ID"));
            contract.Inputs.Add(new BrickPort("packetKey", typeof(string), "包名"));
            contract.Inputs.Add(new BrickPort("dataJson", typeof(string), "JSON 对象文本"));
            contract.Return = BrickReturnKind.Bool;
            contract.Duration = BrickDuration.Sync;
            contract.Thread = "main";
            BrickRegistry.Register(contract);
        }

        /// <summary>
        /// 注册 data.box_get_dic
        /// </summary>
        private static void RegisterBoxGetDic()
        {
            BrickContract contract = new BrickContract("data.box_get_dic", "Mau.Bricks.BoxBrick.GetDic");
            contract.Inputs.Add(new BrickPort("boxId", typeof(string), "作用域 ID"));
            contract.Inputs.Add(new BrickPort("packetKey", typeof(string), "包名"));
            contract.Outputs.Add(new BrickPort("dataJson", typeof(string), "JSON 对象文本"));
            contract.Return = BrickReturnKind.Bool;
            contract.Duration = BrickDuration.Sync;
            contract.Thread = "main";
            BrickRegistry.Register(contract);
        }
    }
}
