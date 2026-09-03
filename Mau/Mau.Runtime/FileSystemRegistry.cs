using System.Collections.Generic;

namespace Mau.Runtime
{
    /// <summary>
    /// 文件系统注册表——多猫文件面路由（P2 配置作用域单向流：猫级 FS 唯一解析出口）。
    /// 静态注册面（ConfigStoreRegistry 同模式——P9.4 per-cat 先例）：宿主 ApplyCatRoots 注册每猫实例 + 当前猫 AsyncLocal；
    /// 积木 ResolveScoped 按 catId 路由（显式参数优先）→ ResolveCurrent 回退当前猫 → 空回退全局。
    /// 线程模型：注册写 = 宿主主线程（catcfg.apply/会话构造）；读 = 积木执行（主线程 Tick 内）——锁保护保险。
    /// </summary>
    public static class FileSystemRegistry
    {
        /// <summary>当前猫 key——AsyncLocal（直执面 ChatSession.RunTool 设置；OA 面积木显式 catId 优先）</summary>
        private static readonly System.Threading.AsyncLocal<string?> _currentCatKey = new System.Threading.AsyncLocal<string?>();

        /// <summary>每猫文件系统表——catId → FileSystemService（猫启用根子集）</summary>
        private static readonly Dictionary<string, FileSystemService> _byCat = new Dictionary<string, FileSystemService>();

        /// <summary>注册表锁</summary>
        private static readonly object _gate = new object();

        /// <summary>当前猫 key——工具执行链读取（null=无猫上下文）</summary>
        public static string? CurrentCatKey
        {
            get { return _currentCatKey.Value; }
        }

        /// <summary>
        /// 设置当前猫——工具执行入口（ChatSession.RunTool 直执前设置，finally 恢复）。
        /// </summary>
        /// <param name="catKey">猫 key（majordomo=默认猫；多猫=会话 ID；null=清除上下文）</param>
        public static void SetCurrentCat(string? catKey)
        {
            _currentCatKey.Value = catKey;
        }

        /// <summary>
        /// 注册每猫文件系统——catcfg.apply / 会话构造时调用（覆盖已存在同 ID；null 实例=注销）。
        /// </summary>
        /// <param name="catId">会话 ID</param>
        /// <param name="fs">猫级文件系统（null=移除）</param>
        public static void Register(string catId, FileSystemService fs)
        {
            if (catId == null || catId.Length == 0)
            {
                return;
            }
            lock (_gate)
            {
                if (fs == null)
                {
                    _byCat.Remove(catId);
                }
                else
                {
                    _byCat[catId] = fs;
                }
            }
        }

        /// <summary>
        /// 注销每猫文件系统——cat.delete 时调用。
        /// </summary>
        /// <param name="catId">会话 ID</param>
        public static void Unregister(string catId)
        {
            if (catId == null || catId.Length == 0)
            {
                return;
            }
            lock (_gate)
            {
                _byCat.Remove(catId);
            }
        }

        /// <summary>
        /// 按 catId 解析猫级文件系统——空/未注册返回 null（调用方回退全局 DataBox）。
        /// </summary>
        /// <param name="catId">会话 ID（空=null）</param>
        /// <returns>猫级文件系统实例或 null</returns>
        public static FileSystemService? Resolve(string catId)
        {
            lock (_gate)
            {
                if (catId != null && catId.Length > 0)
                {
                    FileSystemService? fs;
                    if (_byCat.TryGetValue(catId, out fs))
                    {
                        return fs;
                    }
                }
            }
            return null;
        }

        /// <summary>
        /// 按当前猫解析——AsyncLocal 当前猫（直执面上下文）；无猫上下文返回 null。
        /// </summary>
        /// <returns>猫级文件系统实例或 null</returns>
        public static FileSystemService? ResolveCurrent()
        {
            string? catKey = _currentCatKey.Value;
            if (catKey == null || catKey.Length == 0)
            {
                return null;
            }
            return Resolve(catKey);
        }

        /// <summary>
        /// 作用域解析——显式 catId 优先，空则当前猫，再空则 null（积木统一入口）。
        /// </summary>
        /// <param name="catId">显式会话 ID（OA 面 argsJson 注入；空=当前猫）</param>
        /// <returns>猫级文件系统实例或 null</returns>
        public static FileSystemService? ResolveScoped(string catId)
        {
            if (catId != null && catId.Length > 0)
            {
                return Resolve(catId);
            }
            return ResolveCurrent();
        }
    }
}
