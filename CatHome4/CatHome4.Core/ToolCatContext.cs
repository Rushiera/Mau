using System;
using System.Collections.Generic;
using Mau.Runtime;

namespace CH4
{
    /// <summary>
    /// 猫级白名单上下文——工具执行按猫裁剪的运行时面（M4e）。
    /// 职责：当前猫标识（AsyncLocal——工具执行链读取）+ 猫级 FileSystemService 缓存表。
    /// 语义：猫启用根 = 全局池子集（cat.cfg enabledRoots）；workspace 强制常驻。
    /// 线程模型：AsyncLocal 跨异步流传递——主线程泵设置，工具执行（含后台 Task 回投）读取一致。
    /// </summary>
    public static class ToolCatContext
    {
        /// <summary>当前猫 key——AsyncLocal（工具执行链 ResolveFileSystem 读取）</summary>
        private static readonly System.Threading.AsyncLocal<string> _currentCatKey = new System.Threading.AsyncLocal<string>();

        /// <summary>猫级 FileSystemService 表——catKey → 实例（猫启用根子集；catcfg.apply 重建）</summary>
        private static readonly Dictionary<string, FileSystemService> _catFsMap = new Dictionary<string, FileSystemService>();

        /// <summary>当前猫 key——工具执行链读取（null=无猫上下文，回退全局）</summary>
        public static string CurrentCatKey
        {
            get { return _currentCatKey.Value; }
        }

        /// <summary>
        /// 设置当前猫上下文——工具执行入口（ChatSession 调 _executeTool 前）。
        /// </summary>
        /// <param name="catKey">猫 key（majordomo=默认猫；多猫=会话 ID）</param>
        public static void SetCat(string catKey)
        {
            _currentCatKey.Value = catKey;
        }

        /// <summary>
        /// 解析猫级文件系统——当前猫有缓存实例返回，否则 null（调用方回退全局 DataBox）。
        /// </summary>
        /// <returns>猫级 FileSystemService 或 null</returns>
        public static FileSystemService ResolveCatFileSystem()
        {
            string catKey = _currentCatKey.Value;
            if (catKey == null || catKey.Length == 0)
            {
                return null;
            }
            lock (_catFsMap)
            {
                FileSystemService fs;
                if (_catFsMap.TryGetValue(catKey, out fs))
                {
                    return fs;
                }
            }
            return null;
        }

        /// <summary>
        /// 更新猫级文件系统——catcfg.apply 调用（猫启用根变更后重建；null roots=移除缓存）。
        /// </summary>
        /// <param name="catKey">猫 key</param>
        /// <param name="roots">猫启用根条目（含强制 workspace）</param>
        /// <param name="recycleRoot">回收站目录</param>
        public static void UpdateCatFileSystem(string catKey, WorkspaceConfig.RootEntry[] roots, string recycleRoot)
        {
            lock (_catFsMap)
            {
                if (roots == null || roots.Length == 0)
                {
                    _catFsMap.Remove(catKey);
                    return;
                }
                _catFsMap[catKey] = new FileSystemService(roots, recycleRoot);
            }
        }

        /// <summary>
        /// 移除猫级文件系统——cat.delete 调用（缓存随猫销毁）。
        /// </summary>
        /// <param name="catKey">猫 key</param>
        public static void RemoveCatFileSystem(string catKey)
        {
            lock (_catFsMap)
            {
                _catFsMap.Remove(catKey);
            }
        }
    }
}
