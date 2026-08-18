using System;
using System.Collections.Generic;

namespace Mau.Runtime
{
    /// <summary>
    /// CommandBus 输入冻结面分部——宿主 Tick 输入生命周期（BeginTickInput/OpenInput/CloseInput）。
    /// P7b partial 拆分——自 CommandBus.cs 原样搬移，逻辑零改动。
    /// </summary>
    public sealed partial class CommandBus : ICommandBus
    {
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
    }
}