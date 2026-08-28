using System;
using System.Collections.Generic;

namespace Mau.Runtime
{
    /// <summary>
    /// 统一查询通道（③ 线程聚合 D11-D13）——组件只声明查询 + 线程域标注，不写守卫样板。
    /// 通道内建线程契约：Main 域自动投递（GAP.2 InvokeOnMain 原语吸收为内部机制）/ Any 域直行（GAP.1 锁内化契约）。
    /// 标准响应 = MD 文本；处理器异常不扩散（结果含错误块）。
    /// 设计：design-mau-rebuild.md §2.2。
    /// </summary>
    public sealed class QueryBus
    {
        /// <summary>
        /// 查询条目——线程域 + 处理器
        /// </summary>
        private sealed class QueryEntry
        {
            /// <summary>
            /// 线程域标注
            /// </summary>
            public QueryDomain Domain;

            /// <summary>
            /// 查询处理器——参数 key=value，返回 MD 文本
            /// </summary>
            public Func<Dictionary<string, string>, string> Handler = null!;
        }

        /// <summary>
        /// 注册表锁——注册/注销/枚举同步
        /// </summary>
        private readonly object _gate = new object();

        /// <summary>
        /// 查询注册表——处理器名 → 条目
        /// </summary>
        private readonly Dictionary<string, QueryEntry> _entries = new Dictionary<string, QueryEntry>(StringComparer.Ordinal);

        /// <summary>
        /// 主线程投递器——FlowRunner.InvokeOnMain 形态（Action + 超时毫秒 → 是否完成）；null = 未绑定
        /// </summary>
        private Func<Action, int, bool>? _dispatcher;

        /// <summary>
        /// Main 域投递超时毫秒
        /// </summary>
        private readonly int _mainTimeoutMs;

        /// <summary>
        /// 构造统一查询通道
        /// </summary>
        /// <param name="mainTimeoutMs">Main 域投递超时毫秒（默认 5000；D31 无超时规范落地前保留兜底）</param>
        public QueryBus(int mainTimeoutMs = 5000)
        {
            _mainTimeoutMs = mainTimeoutMs;
        }

        /// <summary>
        /// 绑定主线程投递器——宿主注入 FlowRunner.InvokeOnMain（GAP.2 原语作为通道内部机制）
        /// </summary>
        /// <param name="dispatcher">投递器——Action + 超时毫秒 → 是否完成；null = 解绑</param>
        public void BindMainDispatcher(Func<Action, int, bool>? dispatcher)
        {
            _dispatcher = dispatcher;
        }

        /// <summary>
        /// 重置——清空全部注册 + 解绑投递器（D26 统一 Reset 契约；宿主切换/测试隔离调用）
        /// </summary>
        public void Reset()
        {
            lock (_gate)
            {
                _entries.Clear();
            }
            _dispatcher = null;
        }

        /// <summary>
        /// 注册查询处理器——同名覆盖（最后生效，测试隔离友好）
        /// </summary>
        /// <param name="name">处理器名（如 "flow.status" / "oa.snapshot" / "summary"）</param>
        /// <param name="domain">线程域标注</param>
        /// <param name="handler">处理器——参数 key=value，返回 MD 文本</param>
        public void Register(string name, QueryDomain domain, Func<Dictionary<string, string>, string> handler)
        {
            if (name == null || name.Trim().Length == 0)
            {
                throw new ArgumentException("查询处理器名不能为空", "name");
            }
            if (handler == null)
            {
                throw new ArgumentNullException("handler");
            }
            QueryEntry entry = new QueryEntry();
            entry.Domain = domain;
            entry.Handler = handler;
            lock (_gate)
            {
                _entries[name] = entry;
            }
        }

        /// <summary>
        /// 注销查询处理器——不存在静默成功
        /// </summary>
        /// <param name="name">处理器名</param>
        /// <returns>true=已移除；false=不存在</returns>
        public bool Unregister(string name)
        {
            lock (_gate)
            {
                return _entries.Remove(name);
            }
        }

        /// <summary>
        /// 执行查询——按线程域自动路由（Any 直行 / Main 投递）；处理器异常不扩散
        /// </summary>
        /// <param name="name">处理器名</param>
        /// <param name="args">查询参数（key=value）</param>
        /// <param name="result">MD 结果文本——失败时为错误说明</param>
        /// <returns>true=查询已执行完成（处理器内部失败时结果含错误块）；false=路由失败（未注册/未绑定/超时）</returns>
        public bool TryExecute(string name, Dictionary<string, string> args, out string result)
        {
            result = "";
            // [段1] 取条目——锁内快照，执行不持锁（注册/注销不阻塞长查询）
            QueryEntry? entry;
            lock (_gate)
            {
                if (!_entries.TryGetValue(name, out entry) || entry == null)
                {
                    result = "错误: 未注册查询处理器: " + name;
                    return false;
                }
            }
            // [段2] Any 域——直接执行（组件自身线程安全）
            if (entry.Domain == QueryDomain.Any)
            {
                try
                {
                    result = entry.Handler(args);
                }
                catch (Exception ex)
                {
                    result = "错误: 查询处理器异常: " + ex.Message;
                }
                return true;
            }
            // [段3] Main 域——经投递器执行（GAP.2 InvokeOnMain 内部机制）
            Func<Action, int, bool>? dispatcher = _dispatcher;
            if (dispatcher == null)
            {
                result = "错误: 主线程投递器未绑定（BindMainDispatcher）——Main 域查询无法执行: " + name;
                return false;
            }
            string captured = "";
            Exception? handlerError = null;
            bool executed = false;
            try
            {
                executed = dispatcher(delegate ()
                {
                    try
                    {
                        captured = entry.Handler(args);
                    }
                    catch (Exception ex)
                    {
                        handlerError = ex;
                    }
                }, _mainTimeoutMs);
            }
            catch (Exception ex)
            {
                result = "错误: 主线程投递失败: " + ex.Message;
                return false;
            }
            if (!executed)
            {
                result = "错误: 主线程投递超时（" + _mainTimeoutMs + "ms）——主线程未驱动？: " + name;
                return false;
            }
            if (handlerError != null)
            {
                result = "错误: 查询处理器异常: " + handlerError.Message;
                return true;
            }
            result = captured;
            return true;
        }

        /// <summary>
        /// 已注册处理器名清单
        /// </summary>
        /// <returns>名称数组</returns>
        public string[] Names()
        {
            lock (_gate)
            {
                string[] names = new string[_entries.Count];
                _entries.Keys.CopyTo(names, 0);
                return names;
            }
        }

        /// <summary>
        /// 已注册处理器数量
        /// </summary>
        /// <returns>数量</returns>
        public int Count
        {
            get
            {
                lock (_gate)
                {
                    return _entries.Count;
                }
            }
        }
    }
}
