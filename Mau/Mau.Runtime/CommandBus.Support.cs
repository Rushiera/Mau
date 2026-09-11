using System;
using System.Collections.Generic;

namespace Mau.Runtime
{
    /// <summary>
    /// CommandBus 快照/校验/工具面分部——调试快照、key 校验、空邮件创建、日志与审计出口。
    /// P7b partial 拆分——自 CommandBus.cs 原样搬移，逻辑零改动。
    /// </summary>
    public sealed partial class CommandBus : ICommandBus
    {
        /// <summary>
        /// 返回不含 int/text 正文的 Command 域独立摘要——透明度暴露。线程安全：锁内快照，任意线程可调（管道/外部线程查询首选）
        /// </summary>
        ///
        
                ///
        
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
        /// 获取注册表调试快照——线程安全：锁内快照，任意线程可调（GetSnapshot 同规）
        /// </summary>
        public string[] GetKeyDic()
        {
            List<string> lines = new List<string>();
            // 线程安全快照——_lock 内遍历（与 GetSnapshot 同规；任意线程可调——管道/外部线程查询）
            lock (_lock)
            {
                lines.Add("[KeyDic] (" + _keyDic.Count + "个模块)");
                foreach (KeyValuePair<long, CommandPack> pair in _keyDic)
                {
                    lines.Add("  " + pair.Key + " (" + pair.Value.CmdKeys.Length + "条) "
                        + string.Join(", ", pair.Value.CmdKeys));
                }
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
                    WriteLog("指令注册被拒 #" + ownerLongId + " 非法三段式 key", 2);
                    return false;
                }
                if (!unique.Add(key))
                {
                    WriteLog("指令注册被拒 #" + ownerLongId + " 批内重复 key=" + key, 2);
                    return false;
                }
                if (_keyOwners.ContainsKey(key))
                {
                    WriteLog("指令注册被拒 #" + ownerLongId + " key 已占用=" + key, 2);
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
        /// 验证 Command key 中的一个标识段——支持 Unicode 字母；首字符必须字母，其余字母或数字
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
        public AuditStore? Audit
        {
            get;
            set;
        }
    }
}