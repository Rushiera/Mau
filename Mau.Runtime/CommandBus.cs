using System;
using System.Collections.Generic;

namespace Mau.Runtime
{
    /// <summary>
    /// 帧驱动指令总线。注册和消费只能发生在宿主主线程；
    /// UI 或后台线程可并发写入已注册 key，下一次主线程消费时生效。
    /// </summary>
    public sealed class CommandBus : ICommandBus
    {
        /// <summary>
        /// 宿主线程归属守卫
        /// </summary>
        private readonly ThreadGuard _threadGuard;

        /// <summary>
        /// 日志写入器
        /// </summary>
        private readonly Action<string, int>? _logWriter;

        /// <summary>
        /// 所有注册、指令池和文本池共用的锁
        /// </summary>
        private readonly object _lock;

        /// <summary>
        /// 模块 ID 到邮件模板的注册表
        /// </summary>
        private readonly Dictionary<long, CommandPack> _keyDic;

        /// <summary>
        /// 全局唯一指令 key 到唯一注册者的索引
        /// </summary>
        private readonly Dictionary<string, long> _keyOwners;

        /// <summary>
        /// 当前宿主 Tick 可消费的冻结整数指令池
        /// </summary>
        private readonly Dictionary<string, int> _commandPool;

        /// <summary>
        /// 当前宿主 Tick 可消费的冻结文本指令池
        /// </summary>
        private readonly Dictionary<string, string> _textPool;

        /// <summary>
        /// 等待下一次宿主 Tick 冻结的整数输入池
        /// </summary>
        private readonly Dictionary<string, int> _pendingCommandPool;

        /// <summary>
        /// 等待下一次宿主 Tick 冻结的文本输入池
        /// </summary>
        private readonly Dictionary<string, string> _pendingTextPool;

        /// <summary>
        /// 是否接受新的外部输入
        /// </summary>
        private bool _isAcceptingInput;

        /// <summary>
        /// 停止后被拒绝的输入数量
        /// </summary>
        private long _rejectedInputCount;

        /// <summary>
        /// Command 注册和待消费集合变化版本
        /// </summary>
        private long _version;

        /// <summary>
        /// 获取整数指令池快照
        /// </summary>
        public IReadOnlyDictionary<string, int> CmdCache
        {
            get
            {
                lock (_lock)
                {
                    return new Dictionary<string, int>(_commandPool);
                }
            }
        }

        /// <summary>
        /// 创建空的指令总线
        /// </summary>
        /// <param name="threadGuard">宿主线程守卫</param>
        public CommandBus(ThreadGuard threadGuard)
        {
            if (threadGuard == null)
            {
                throw new ArgumentNullException("threadGuard");
            }
            _threadGuard = threadGuard;
            _logWriter = null;
            _lock = new object();
            _keyDic = new Dictionary<long, CommandPack>();
            _keyOwners = new Dictionary<string, long>(StringComparer.Ordinal);
            _commandPool = new Dictionary<string, int>();
            _textPool = new Dictionary<string, string>();
            _pendingCommandPool = new Dictionary<string, int>();
            _pendingTextPool = new Dictionary<string, string>();
            _isAcceptingInput = true;
            _rejectedInputCount = 0;
            _version = 0;
        }

        /// <summary>
        /// 创建带日志的指令总线
        /// </summary>
        /// <param name="threadGuard">宿主线程守卫</param>
        /// <param name="logWriter">日志写入器</param>
        public CommandBus(ThreadGuard threadGuard, Action<string, int>? logWriter)
        {
            if (threadGuard == null)
            {
                throw new ArgumentNullException("threadGuard");
            }
            _threadGuard = threadGuard;
            _logWriter = logWriter;
            _lock = new object();
            _keyDic = new Dictionary<long, CommandPack>();
            _keyOwners = new Dictionary<string, long>(StringComparer.Ordinal);
            _commandPool = new Dictionary<string, int>();
            _textPool = new Dictionary<string, string>();
            _pendingCommandPool = new Dictionary<string, int>();
            _pendingTextPool = new Dictionary<string, string>();
            _isAcceptingInput = true;
            _rejectedInputCount = 0;
            _version = 0;
        }

        /// <summary>
        /// 注册模块拥有的指令 key
        /// </summary>
        /// <param name="ownerLongId">模块全局 ID</param>
        /// <param name="cmdKeys">指令 key 数组</param>
        public void Register(long ownerLongId, string[] cmdKeys)
        {
            // [段1] 硬性要求生命周期注册发生在主线程
            _threadGuard.AssertMainThread("CommandBus.Register");
            if (cmdKeys == null)
            {
                throw new ArgumentNullException("cmdKeys");
            }
            // [段2] 先建立输入副本，随后在同一锁内全量校验并原子提交
            string[] keyCopy = new string[cmdKeys.Length];
            Array.Copy(cmdKeys, keyCopy, cmdKeys.Length);
            lock (_lock)
            {
                if (_keyDic.ContainsKey(ownerLongId))
                {
                    WriteLog("COMMAND | REGISTER | REJECT | #" + ownerLongId
                        + " 重复注册", 2);
                    if (Audit != null)
                    {
                        Audit.Record("CommandBus", "cmd.register", -1, new AuditProp[] {
                            new AuditProp("owner", ownerLongId.ToString()),
                            new AuditProp("keys", string.Join(",", keyCopy)),
                            new AuditProp("result", "rejected"),
                            new AuditProp("reason", "重复注册")
                        });
                    }
                    return;
                }
                if (!CanRegisterKeys(ownerLongId, keyCopy))
                {
                    if (Audit != null)
                    {
                        Audit.Record("CommandBus", "cmd.register", -1, new AuditProp[] {
                            new AuditProp("owner", ownerLongId.ToString()),
                            new AuditProp("keys", string.Join(",", keyCopy)),
                            new AuditProp("result", "rejected"),
                            new AuditProp("reason", "key 冲突")
                        });
                    }
                    return;
                }
                CommandPack email = CreateEmptyEmail(ownerLongId, keyCopy);
                _keyDic[ownerLongId] = email;
                for (int i = 0; i < keyCopy.Length; i = i + 1)
                {
                    _keyOwners[keyCopy[i]] = ownerLongId;
                }
                _version = _version + 1;
                if (Audit != null)
                {
                    Audit.Record("CommandBus", "cmd.register", -1, new AuditProp[] {
                        new AuditProp("owner", ownerLongId.ToString()),
                        new AuditProp("keys", string.Join(",", keyCopy)),
                        new AuditProp("result", "accepted")
                    });
                }
            }
        }

        /// <summary>
        /// 注销模块并清理它的残留指令
        /// </summary>
        /// <param name="ownerLongId">模块全局 ID</param>
        public void Unregister(long ownerLongId)
        {
            CommandPack email;

            _threadGuard.AssertMainThread("CommandBus.Unregister");
            if (!_keyDic.TryGetValue(ownerLongId, out email))
            {
                return;
            }

            // [段1] 清理本模块全部唯一 key 和待消费输入
            lock (_lock)
            {
                for (int i = 0; i < email.CmdKeys.Length; i = i + 1)
                {
                    string key = email.CmdKeys[i];
                    _keyOwners.Remove(key);
                    _commandPool.Remove(key);
                    _textPool.Remove(key);
                    _pendingCommandPool.Remove(key);
                    _pendingTextPool.Remove(key);
                }
                _keyDic.Remove(ownerLongId);
                _version = _version + 1;
                if (Audit != null)
                {
                    Audit.Record("CommandBus", "cmd.clean", -1, new AuditProp[] {
                        new AuditProp("owner", ownerLongId.ToString()),
                        new AuditProp("keys", string.Join(",", email.CmdKeys)),
                        new AuditProp("reason", "unregister")
                    });
                }
            }
        }

        /// <summary>
        /// 从任意线程写入整数指令
        /// </summary>
        /// <param name="key">已注册 key</param>
        /// <param name="value">整数值</param>
        public void Set(string key, int value)
        {
            lock (_lock)
            {
                if (!_isAcceptingInput)
                {
                    _rejectedInputCount = _rejectedInputCount + 1;
                    _version = _version + 1;
                    if (Audit != null)
                    {
                        Audit.Record("CommandBus", "cmd.set", -1, new AuditProp[] {
                            new AuditProp("key", key),
                            new AuditProp("result", "rejected"),
                            new AuditProp("reason", "未接受输入")
                        });
                    }
                    return;
                }
                if (!_keyOwners.ContainsKey(key))
                {
                    WriteLog("COMMAND | SET | REJECT | " + key + " 未注册", 2);
                    if (Audit != null)
                    {
                        Audit.Record("CommandBus", "cmd.set", -1, new AuditProp[] {
                            new AuditProp("key", key),
                            new AuditProp("result", "rejected"),
                            new AuditProp("reason", "未注册")
                        });
                    }
                    return;
                }
                _pendingCommandPool[key] = value;
                _version = _version + 1;
                if (Audit != null)
                {
                    Audit.Record("CommandBus", "cmd.set", -1, new AuditProp[] {
                        new AuditProp("key", key),
                        new AuditProp("payload", value.ToString()),
                        new AuditProp("result", "accepted")
                    });
                }
            }
        }

        /// <summary>
        /// 从任意线程写入文本指令
        /// </summary>
        /// <param name="key">已注册 key</param>
        /// <param name="text">文本值</param>
        public void SetText(string key, string text)
        {
            lock (_lock)
            {
                if (!_isAcceptingInput)
                {
                    _rejectedInputCount = _rejectedInputCount + 1;
                    _version = _version + 1;
                    if (Audit != null)
                    {
                        Audit.Record("CommandBus", "cmd.set", -1, new AuditProp[] {
                            new AuditProp("key", key),
                            new AuditProp("result", "rejected"),
                            new AuditProp("reason", "未接受输入")
                        });
                    }
                    return;
                }
                if (!_keyOwners.ContainsKey(key))
                {
                    WriteLog("COMMAND | SET_TEXT | REJECT | " + key + " 未注册", 2);
                    if (Audit != null)
                    {
                        Audit.Record("CommandBus", "cmd.set", -1, new AuditProp[] {
                            new AuditProp("key", key),
                            new AuditProp("result", "rejected"),
                            new AuditProp("reason", "未注册")
                        });
                    }
                    return;
                }
                _pendingTextPool[key] = text;
                if (Audit != null)
                {
                    Audit.Record("CommandBus", "cmd.set", -1, new AuditProp[] {
                        new AuditProp("key", key),
                        new AuditProp("payload", AuditStore.Summarize(text)),
                        new AuditProp("result", "accepted")
                    });
                }
                _version = _version + 1;
            }
        }

        /// <summary>
        /// 消费指定模块当前收到的指令
        /// </summary>
        /// <param name="ownerLongId">模块全局 ID</param>
        /// <returns>与注册 key 对齐的邮件</returns>
        public CommandPack GetCommandEmail(long ownerLongId)
        {
            CommandPack template;

            _threadGuard.AssertMainThread("CommandBus.GetCommandEmail");
            if (!_keyDic.TryGetValue(ownerLongId, out template))
            {
                return CreateEmptyEmail(0, new string[0]);
            }

            // [段1] 创建独立邮件并从双轨池中原子提取
            CommandPack result = CreateEmptyEmail(ownerLongId, template.CmdKeys);
            lock (_lock)
            {
                bool changed = false;
                List<string> takenKeys = new List<string>();
                for (int i = 0; i < result.CmdKeys.Length; i = i + 1)
                {
                    string key = result.CmdKeys[i];
                    int intValue;
                    string? textValue;
                    if (_commandPool.TryGetValue(key, out intValue))
                    {
                        result.CmdValues[i] = intValue;
                        _commandPool.Remove(key);
                        takenKeys.Add(key);
                        changed = true;
                    }
                    if (_textPool.TryGetValue(key, out textValue) && textValue != null)
                    {
                        result.CmdTexts[i] = textValue;
                        _textPool.Remove(key);
                        takenKeys.Add(key);
                        changed = true;
                    }
                }
                if (changed)
                {
                    result.HasCommands = true;
                    _version = _version + 1;
                    if (Audit != null)
                    {
                        Audit.Record("CommandBus", "cmd.consume", -1, new AuditProp[] {
                            new AuditProp("owner", ownerLongId.ToString()),
                            new AuditProp("keys", string.Join(",", takenKeys))
                        });
                    }
                }
            }
            return result;
        }

        /// <summary>
        /// 清理指定模块的待消费指令
        /// </summary>
        /// <param name="ownerLongId">模块全局 ID</param>
        public void Clean(long ownerLongId)
        {
            CommandPack email;

            _threadGuard.AssertMainThread("CommandBus.Clean");
            if (!_keyDic.TryGetValue(ownerLongId, out email))
            {
                return;
            }
            lock (_lock)
            {
                bool changed = false;
                for (int i = 0; i < email.CmdKeys.Length; i = i + 1)
                {
                    if (_commandPool.Remove(email.CmdKeys[i]))
                    {
                        changed = true;
                    }
                    if (_textPool.Remove(email.CmdKeys[i]))
                    {
                        changed = true;
                    }
                    if (_pendingCommandPool.Remove(email.CmdKeys[i]))
                    {
                        changed = true;
                    }
                    if (_pendingTextPool.Remove(email.CmdKeys[i]))
                    {
                        changed = true;
                    }
                }
                if (changed)
                {
                    _version = _version + 1;
                    if (Audit != null)
                    {
                        Audit.Record("CommandBus", "cmd.clean", -1, new AuditProp[] {
                            new AuditProp("owner", ownerLongId.ToString()),
                            new AuditProp("keys", string.Join(",", email.CmdKeys))
                        });
                    }
                }
            }
        }

        /// <summary>
        /// 返回不含 int/text 正文的 Command 域独立摘要——透明度暴露
        /// </summary>
        /// <returns>必要观察事实</returns>
        public CommandSnapshot GetSnapshot()
        {
            lock (_lock)
            {
                // [段1] 只复制身份、数量和域版本，不接触输入正文
                CommandSnapshot snapshot = CommandSnapshot.Empty();
                snapshot.Version = _version;
                snapshot.RegisteredOwnerCount = _keyDic.Count;
                snapshot.RegisteredKeyCount = _keyOwners.Count;
                HashSet<string> frozen = new HashSet<string>(
                    _commandPool.Keys, StringComparer.Ordinal);
                frozen.UnionWith(_textPool.Keys);
                HashSet<string> pending = new HashSet<string>(
                    _pendingCommandPool.Keys, StringComparer.Ordinal);
                pending.UnionWith(_pendingTextPool.Keys);
                HashSet<string> total = new HashSet<string>(frozen, StringComparer.Ordinal);
                total.UnionWith(pending);
                snapshot.FrozenKeyCount = frozen.Count;
                snapshot.PendingKeyCount = pending.Count;
                snapshot.TotalInputKeyCount = total.Count;
                snapshot.IsAcceptingInput = _isAcceptingInput;
                snapshot.RejectedInputCount = _rejectedInputCount;

                // [段2] 注册 Key 按 Ordinal 稳定发布且数组归调用方独占
                snapshot.RegisteredKeys = new string[_keyOwners.Count];
                _keyOwners.Keys.CopyTo(snapshot.RegisteredKeys, 0);
                Array.Sort(snapshot.RegisteredKeys, StringComparer.Ordinal);
                return snapshot;
            }
        }

        /// <summary>
        /// 获取注册表调试快照
        /// </summary>
        /// <returns>注册模块文本行</returns>
        public string[] GetKeyDic()
        {
            List<string> lines = new List<string>();

            _threadGuard.AssertMainThread("CommandBus.GetKeyDic");
            lines.Add("[KeyDic] (" + _keyDic.Count + "个模块)");
            foreach (KeyValuePair<long, CommandPack> pair in _keyDic)
            {
                lines.Add("  " + pair.Key + " (" + pair.Value.CmdKeys.Length + "条) "
                    + string.Join(", ", pair.Value.CmdKeys));
            }
            return lines.ToArray();
        }

        /// <summary>
        /// 获取待消费池调试快照
        /// </summary>
        /// <returns>指令池文本行</returns>
        public string[] GetCommandPool()
        {
            List<string> lines = new List<string>();

            lock (_lock)
            {
                lines.Add("[CommandPool] Frozen="
                    + (_commandPool.Count + _textPool.Count)
                    + " Pending="
                    + (_pendingCommandPool.Count + _pendingTextPool.Count));
                foreach (KeyValuePair<string, int> pair in _commandPool)
                {
                    lines.Add("  " + pair.Key + " = " + pair.Value);
                }
                foreach (KeyValuePair<string, string> pair in _textPool)
                {
                    lines.Add("  " + pair.Key + " = \"" + pair.Value + "\"");
                }
                foreach (KeyValuePair<string, int> pair in _pendingCommandPool)
                {
                    lines.Add("  [Pending] " + pair.Key + " = " + pair.Value);
                }
                foreach (KeyValuePair<string, string> pair in _pendingTextPool)
                {
                    lines.Add("  [Pending] " + pair.Key + " = \"" + pair.Value + "\"");
                }
            }
            return lines.ToArray();
        }

        /// <summary>
        /// 在宿主 Tick 开始处冻结此前到达的输入
        /// </summary>
        public void BeginTickInput()
        {
            _threadGuard.AssertMainThread("CommandBus.BeginTickInput");
            lock (_lock)
            {
                foreach (KeyValuePair<string, int> pair in _pendingCommandPool)
                {
                    _commandPool[pair.Key] = pair.Value;
                }
                foreach (KeyValuePair<string, string> pair in _pendingTextPool)
                {
                    _textPool[pair.Key] = pair.Value;
                }
                if (_pendingCommandPool.Count > 0
                    || _pendingTextPool.Count > 0)
                {
                    _pendingCommandPool.Clear();
                    _pendingTextPool.Clear();
                    _version = _version + 1;
                }
            }
        }

        /// <summary>
        /// 打开一个干净生命周期的输入面
        /// </summary>
        public void OpenInput()
        {
            _threadGuard.AssertMainThread("CommandBus.OpenInput");
            lock (_lock)
            {
                _commandPool.Clear();
                _textPool.Clear();
                _pendingCommandPool.Clear();
                _pendingTextPool.Clear();
                _isAcceptingInput = true;
                _rejectedInputCount = 0;
                _version = _version + 1;
            }
        }

        /// <summary>
        /// 关闭外部输入并清理尚未消费的正文
        /// </summary>
        public void CloseInput()
        {
            _threadGuard.AssertMainThread("CommandBus.CloseInput");
            lock (_lock)
            {
                _isAcceptingInput = false;
                _commandPool.Clear();
                _textPool.Clear();
                _pendingCommandPool.Clear();
                _pendingTextPool.Clear();
                _version = _version + 1;
            }
        }

        /// <summary>
        /// 创建已初始化数组的空邮件
        /// </summary>
        /// <param name="ownerLongId">接收者 ID</param>
        /// <param name="keys">邮件 key</param>
        /// <returns>空邮件</returns>
        private CommandPack CreateEmptyEmail(long ownerLongId, string[] keys)
        {
            CommandPack email;
            string[] keyCopy = new string[keys.Length];

            Array.Copy(keys, keyCopy, keys.Length);
            email.OwnerLongId = ownerLongId;
            email.CmdKeys = keyCopy;
            email.CmdValues = new int[keyCopy.Length];
            email.CmdTexts = new string[keyCopy.Length];
            email.HasCommands = false;
            return email;
        }

        /// <summary>
        /// 验证一组 Command key 可以作为整体注册
        /// </summary>
        /// <param name="ownerLongId">申请所有者</param>
        /// <param name="keys">待注册 key 副本</param>
        /// <returns>全部格式正确、批内唯一且未被其他 owner 占用时为 true</returns>
        private bool CanRegisterKeys(long ownerLongId, string[] keys)
        {
            HashSet<string> unique = new HashSet<string>(StringComparer.Ordinal);

            for (int i = 0; i < keys.Length; i = i + 1)
            {
                string? key = keys[i];
                if (key == null || !IsValidKey(key))
                {
                    WriteLog("COMMAND | REGISTER | REJECT | #" + ownerLongId
                        + " 非法三段式key", 2);
                    return false;
                }
                if (!unique.Add(key))
                {
                    WriteLog("COMMAND | REGISTER | REJECT | #" + ownerLongId
                        + " 批内重复key=" + key, 2);
                    return false;
                }
                if (_keyOwners.ContainsKey(key))
                {
                    WriteLog("COMMAND | REGISTER | REJECT | #" + ownerLongId
                        + " key已占用=" + key, 2);
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// 验证 Category_Module_Name 三段式 Command key
        /// </summary>
        /// <param name="key">待验证 key</param>
        /// <returns>格式是否有效</returns>
        private bool IsValidKey(string? key)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return false;
            }
            string[] segments = key.Split('_');
            if (segments.Length != 3)
            {
                return false;
            }
            for (int i = 0; i < segments.Length; i = i + 1)
            {
                if (!IsValidSegment(segments[i]))
                {
                    return false;
                }
            }
            return true;
        }
/// <summary>
/// 验证 Command key 中的一个标识段——支持 Unicode 字母（中文猫名等）；首字符必须字母，其余字母或数字
/// </summary>
///

        ///
private bool IsValidSegment(string segment)
{
            if (segment.Length == 0 || !char.IsLetter(segment[0]))
            {
                return false;
            }
            for (int i = 1; i < segment.Length; i = i + 1)
            {
                if (!char.IsLetterOrDigit(segment[i]))
                {
                    return false;
                }
            }
            return true;
        }
/// <summary>
/// 把任意标识规范化为合法 key 段——保留 Unicode 字母数字，其余字符删除；
/// 空结果回落 "cat"；数字开头补 'c' 前缀（首字符必须字母）
/// </summary>
/// <param name = "raw">原始标识（猫名/会话 Key）</param>
/// <returns>合法 key 段</returns>
public static string KeySegment(string raw)
{
    if (raw == null || raw.Length == 0)
    {
        return "cat";
    }

    System.Text.StringBuilder sb = new System.Text.StringBuilder();
    for (int i = 0; i < raw.Length; i = i + 1)
    {
        if (char.IsLetterOrDigit(raw[i]))
        {
            sb.Append(raw[i]);
        }
    }

    if (sb.Length == 0)
    {
        return "cat";
    }

    if (!char.IsLetter(sb[0]))
    {
        sb.Insert(0, 'c');
    }

    return sb.ToString();
}        /// <summary>
        /// 判断字符是否为 ASCII 英文字母
        /// </summary>
        /// <param name="character">字符</param>
        /// <returns>是否为 A-Z 或 a-z</returns>
        private bool IsAsciiLetter(char character)
        {
            return (character >= 'A' && character <= 'Z')
                || (character >= 'a' && character <= 'z');
        }

        /// <summary>
        /// 写入可选日志
        /// </summary>
        /// <param name="message">日志正文</param>
        /// <param name="level">日志等级</param>
        private void WriteLog(string message, int level)
        {
            if (_logWriter != null)
            {
                _logWriter(message, level);
            }
        }
/// <summary>
/// 审计存储——宿主注入后机制事件写入（null = 不审计）。零业务侵入：仅记录，不改流程。
/// </summary>
public AuditStore? Audit { get; set; }    }
}
