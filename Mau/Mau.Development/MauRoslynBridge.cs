using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using Mau.Runtime;

namespace Mau.Development
{
#pragma warning disable CS8600 // 环境 SDK 对 ConcurrentDictionary/Dictionary TryXXX(out TValue) 的 MaybeNullWhen 分析异常误报——out 实参非空变量也报警（最小复现验证）
    /// <summary>
    /// C# 工具桥实现——Roslyn 编码工具域缓存桥（design-ch4-cs.md D1-D3）。
    /// 磁盘权威 + 快照监管（mtime+size 前缀对账）+ 项目键隔离常驻池（LRU 4）+ 树/编译/语义三态无感。
    /// 引用集 = 目标项目已 build 的 bin 产物 + TPA + 共享框架探测（AspNetCore/WindowsDesktop——莎拍板 A 方案）——
    /// bin 缺失 → 引导先 cs.build（check 快、build 权威的闭环）。
    /// 工具面 9 件：check / build / list / read / find_ref / patch / member / comment / dead。
    /// </summary>
    public sealed partial class MauRoslynBridge : ICSharpBridge
    {
        /// <summary>
        /// 池上限——LRU 淘汰只丢内存缓存（磁盘权威无损失）
        /// </summary>
        private const int PoolLimit = 4;

        /// <summary>
        /// 单结果截断上限——回传 LLM 上下文防爆
        /// </summary>
        private const int MaxResultChars = 20000;

        /// <summary>
        /// 受控根列表——csproj 绝对路径必须落在某根内（同 mau.* 工具的仓库根校验语义）
        /// </summary>
        private readonly string[] _roots;

        /// <summary>
        /// 受控根 id 列表——与 _roots 对齐（id: 命名空间寻址——runtime:/mau:/ccbp:；对齐 FileSystemService）
        /// </summary>
        private readonly string[] _rootIds;

        /// <summary>
        /// 项目缓存池——Key = csproj 绝对路径（GetFullPath 归一化；OrdinalIgnoreCase）
        /// </summary>
        private readonly ConcurrentDictionary<string, ProjectCache> _pool;

        /// <summary>
        /// 建条目串行锁——首次加载（Parse 全树）不并发
        /// </summary>
        private readonly object _poolGate;

        /// <summary>
        /// LRU 淘汰锁
        /// </summary>
        private readonly object _lruGate;

        /// <summary>
        /// 创建工具桥——受控根用于项目路径越界校验（id: 命名空间寻址对齐 FileSystemService）
        /// </summary>
        /// <param name="rootEntries">受控根条目（id + 绝对路径；csproj 必须在其内）</param>
        public MauRoslynBridge(WorkspaceConfig.RootEntry[] rootEntries)
        {
            if (rootEntries == null || rootEntries.Length == 0)
            {
                throw new ArgumentException("MauRoslynBridge 需要至少一个受控根。", "rootEntries");
            }
            List<string> normalized = new List<string>();
            List<string> ids = new List<string>();
            for (int i = 0; i < rootEntries.Length; i = i + 1)
            {
                WorkspaceConfig.RootEntry entry = rootEntries[i];
                if (entry == null || string.IsNullOrWhiteSpace(entry.Path))
                {
                    throw new ArgumentException("受控根路径为空。", "rootEntries");
                }
                normalized.Add(Path.GetFullPath(entry.Path));
                string entryId = entry.Id;
                if (entryId == null || entryId.Length == 0)
                {
                    entryId = "root" + i.ToString();
                }
                ids.Add(entryId);
            }
            _roots = normalized.ToArray();
            _rootIds = ids.ToArray();
            _pool = new ConcurrentDictionary<string, ProjectCache>(StringComparer.OrdinalIgnoreCase);
            _poolGate = new object();
            _lruGate = new object();
        }

        /// <summary>
        /// 单方法调度入口——method 白名单 9 件分派；异常不外泄（ERR|EX）
        /// </summary>
        /// <param name="method">操作名</param>
        /// <param name="argsJson">展平参数 JSON</param>
        /// <param name="result">结果文本</param>
        /// <returns>true=调用完成（语义成功与否看 result 前缀）</returns>
        public bool Invoke(string method, string argsJson, out string result)
        {
            try
            {
                JsonDocument args;
                try
                {
                    args = JsonDocument.Parse(argsJson);
                }
                catch (Exception ex)
                {
                    result = "ERR|BAD_ARGS|参数 JSON 解析失败: " + ex.Message;
                    return false;
                }
                using (args)
                {
                    JsonElement root = args.RootElement;
                    if (method == "check")
                    {
                        return ToolCheck(root, out result);
                    }
                    if (method == "build")
                    {
                        return ToolBuild(root, out result);
                    }
                    if (method == "list")
                    {
                        return ToolList(root, out result);
                    }
                    if (method == "read")
                    {
                        return ToolRead(root, out result);
                    }
                    if (method == "find_ref")
                    {
                        return ToolFindRef(root, out result);
                    }
                    if (method == "patch")
                    {
                        return ToolPatch(root, out result);
                    }
                    if (method == "member")
                    {
                        return ToolMember(root, out result);
                    }
                    if (method == "comment")
                    {
                        return ToolComment(root, out result);
                    }
                    if (method == "dead")
                    {
                        return ToolDead(root, out result);
                    }
                    if (method == "comment_check")
                    {
                        return ToolCommentCheck(root, out result);
                    }
                    result = "ERR|UNKNOWN_METHOD|未知方法: " + method;
                    return false;
                }
            }
            catch (Exception ex)
            {
                result = "ERR|EX|" + ex.GetType().Name + ": " + ex.Message;
                return false;
            }
        }

        /// <summary>
        /// 受控根校验 + csproj 规范化——目录参数自动补 *.csproj
        /// </summary>
        /// <param name="pathParam">路径参数</param>
        /// <returns>csproj 绝对路径（越界/缺失返回空串）</returns>
        private string ResolveProject(string pathParam)
        {
            if (string.IsNullOrWhiteSpace(pathParam))
            {
                return "";
            }
            // P1 修复：id: 命名空间寻址（runtime:/mau:/ccbp:）——对齐 FileSystemService.Resolve（P8.5b）
            // LLM 习惯传 runtime:s1press/csproj_press，此前被当作字面路径导致 BAD_PATH 试错循环
            string path = pathParam;
            int nsSep = path.IndexOf(':');
            if (nsSep > 0)
            {
                string nsId = path.Substring(0, nsSep);
                string nsRel = path.Substring(nsSep + 1);
                for (int i = 0; i < _roots.Length; i = i + 1)
                {
                    if (string.Equals(_rootIds[i], nsId, StringComparison.Ordinal))
                    {
                        path = Path.Combine(_roots[i], nsRel);
                        break;
                    }
                }
            }
            string full;
            if (Path.IsPathFullyQualified(path))
            {
                full = Path.GetFullPath(path);
            }
            else
            {
                full = Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, path));
            }
            bool inside = false;
            for (int i = 0; i < _roots.Length; i = i + 1)
            {
                if (full.StartsWith(_roots[i] + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(full, _roots[i], StringComparison.OrdinalIgnoreCase))
                {
                    inside = true;
                    break;
                }
            }
            if (!inside)
            {
                return "";
            }
            if (Directory.Exists(full))
            {
                string[] projects = Directory.GetFiles(full, "*.csproj", SearchOption.TopDirectoryOnly);
                if (projects.Length == 1)
                {
                    return Path.GetFullPath(projects[0]);
                }
                if (projects.Length == 0)
                {
                    return "";
                }
                return "";
            }
            if (File.Exists(full) && full.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
            {
                return Path.GetFullPath(full);
            }
            return "";
        }

        /// <summary>
        /// LRU 淘汰——池超限时淘汰 LastAccess 最旧条目（跳过当前正在用的 key）
        /// </summary>
        /// <param name="currentKey">当前操作项目键（不淘汰）</param>
        private void EvictIfNeeded(string currentKey)
        {
            lock (_lruGate)
            {
                if (_pool.Count <= PoolLimit)
                {
                    return;
                }
                string? oldestKey = null;
                long oldest = long.MaxValue;
                foreach (KeyValuePair<string, ProjectCache> pair in _pool)
                {
                    if (string.Equals(pair.Key, currentKey, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                    if (pair.Value.LastAccess < oldest)
                    {
                        oldest = pair.Value.LastAccess;
                        oldestKey = pair.Key;
                    }
                }
                if (oldestKey != null)
                {
                    ProjectCache removed;
                    _pool.TryRemove(oldestKey, out removed);
                }
            }
        }

        /// <summary>
        /// 文本截断——超长保留头部 + 截断提示
        /// </summary>
        /// <param name="text">原文</param>
        /// <param name="max">上限</param>
        /// <returns>截断文本</returns>
        private static string TrimResult(string text, int max)
        {
            if (text == null || text.Length == 0)
            {
                return "";
            }
            if (text.Length <= max)
            {
                return text;
            }
            return text.Substring(0, max) + System.Environment.NewLine + "…[截断: 共 " + text.Length.ToString() + " 字符，仅保留前 " + max.ToString() + "]";
        }

        /// <summary>
        /// 相对 csproj 目录路径——诊断输出可读性
        /// </summary>
        private static string RelativeToProject(ProjectCache cache, string filePath)
        {
            if (filePath.StartsWith(cache.ProjectDir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                return filePath.Substring(cache.ProjectDir.Length + 1);
            }
            return filePath;
        }

        /// <summary>
        /// 从 argsJson 提取字段（缺省空串）
        /// </summary>
        private static string Arg(JsonElement root, string name)
        {
            if (root.ValueKind != JsonValueKind.Object)
            {
                return "";
            }
            JsonElement value;
            if (!root.TryGetProperty(name, out value))
            {
                return "";
            }
            if (value.ValueKind == JsonValueKind.String)
            {
                return value.GetString() ?? "";
            }
            if (value.ValueKind == JsonValueKind.True)
            {
                return "true";
            }
            if (value.ValueKind == JsonValueKind.False)
            {
                return "false";
            }
            return value.ToString();
        }
    }
}