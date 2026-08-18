using System;
using System.Collections.Generic;

namespace Mau.Runtime
{
    /// <summary>
    /// 帧驱动指令总线。注册和消费只能发生在宿主主线程；
    /// UI 或后台线程可并发写入已注册 key，下一次主线程消费时生效。
    /// 主体监管面（Register/Unregister/Set/SetText/GetCommandEmail/Clean）在本文件；
    /// 输入冻结面分部在 CommandBus.Tick.cs；快照/校验/工具面分部在 CommandBus.Support.cs（P7b partial 拆分）。
    /// </summary>
    public sealed partial class CommandBus : ICommandBus
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
        public void Set(string key, int value, string source)
{
            lock (_lock)
            {
                if (!_isAcceptingInput)
                {
                    _rejectedInputCount = _rejectedInputCount + 1;
                    _version = _version + 1;
                    // C 类 Log——所有投递留痕（含未接受输入；level=WARN 但 Category=CMD 区分）
                    LogStore.Add("CommandBus", 2, "CMD | SET | " + key + " | " + source + " | REJECT 未接受输入", "CMD");
                    if (Audit != null)
                    {
                        Audit.Record("CommandBus", "cmd.set", -1, new AuditProp[] {
                            new AuditProp("key", key),
                            new AuditProp("source", source),
                            new AuditProp("result", "rejected"),
                            new AuditProp("reason", "未接受输入")
                        });
                    }
                    return;
                }
                if (!_keyOwners.ContainsKey(key))
                {
                    WriteLog("COMMAND | SET | REJECT | " + key + " 未注册", 2);
                    // C 类 Log——投递失败（未注册）
                    LogStore.Add("CommandBus", 2, "CMD | SET | " + key + " | " + source + " | REJECT 未注册", "CMD");
                    if (Audit != null)
                    {
                        Audit.Record("CommandBus", "cmd.set", -1, new AuditProp[] {
                            new AuditProp("key", key),
                            new AuditProp("source", source),
                            new AuditProp("result", "rejected"),
                            new AuditProp("reason", "未注册")
                        });
                    }
                    return;
                }
                _pendingCommandPool[key] = value;
                _version = _version + 1;
                // C 类 Log——投递成功（已注册）
                LogStore.Add("CommandBus", 0, "CMD | SET | " + key + " | " + source + " | ACCEPT", "CMD");
                if (Audit != null)
                {
                    Audit.Record("CommandBus", "cmd.set", -1, new AuditProp[] {
                        new AuditProp("key", key),
                        new AuditProp("source", source),
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
        public void SetText(string key, string text, string source)
{
            lock (_lock)
            {
                if (!_isAcceptingInput)
                {
                    _rejectedInputCount = _rejectedInputCount + 1;
                    _version = _version + 1;
                    // C 类 Log——所有投递留痕（含未接受输入）
                    LogStore.Add("CommandBus", 2, "CMD | SET_TEXT | " + key + " | " + source + " | REJECT 未接受输入", "CMD");
                    if (Audit != null)
                    {
                        Audit.Record("CommandBus", "cmd.set", -1, new AuditProp[] {
                            new AuditProp("key", key),
                            new AuditProp("source", source),
                            new AuditProp("result", "rejected"),
                            new AuditProp("reason", "未接受输入")
                        });
                    }
                    return;
                }
                if (!_keyOwners.ContainsKey(key))
                {
                    WriteLog("COMMAND | SET_TEXT | REJECT | " + key + " 未注册", 2);
                    // C 类 Log——投递失败（未注册）
                    LogStore.Add("CommandBus", 2, "CMD | SET_TEXT | " + key + " | " + source + " | REJECT 未注册", "CMD");
                    if (Audit != null)
                    {
                        Audit.Record("CommandBus", "cmd.set", -1, new AuditProp[] {
                            new AuditProp("key", key),
                            new AuditProp("source", source),
                            new AuditProp("result", "rejected"),
                            new AuditProp("reason", "未注册")
                        });
                    }
                    return;
                }
                _pendingTextPool[key] = text;
                // C 类 Log——投递成功（已注册）
                LogStore.Add("CommandBus", 0, "CMD | SET_TEXT | " + key + " | " + source + " | ACCEPT", "CMD");
                if (Audit != null)
                {
                    Audit.Record("CommandBus", "cmd.set", -1, new AuditProp[] {
                        new AuditProp("key", key),
                        new AuditProp("source", source),
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
    }
}