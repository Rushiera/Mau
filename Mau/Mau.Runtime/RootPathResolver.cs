using System;
using System.IO;

namespace Mau.Runtime
{
    /// <summary>
    /// 根寻址解析结果（A257 公共服务）——成功时 FullPath / RootId / RootPath 有效，失败时 ErrorCode / Message 有效。
    /// </summary>
    public sealed class RootResolveResult
    {
        /// <summary>
        /// 是否解析成功
        /// </summary>
        public bool Ok;

        /// <summary>
        /// 规范绝对路径（成功时有效；空输入时为空串）
        /// </summary>
        public string FullPath = "";

        /// <summary>
        /// 归属受控根 id（ResolveOwned 且已定归属时有效；普通路径未定归属时为空）
        /// </summary>
        public string RootId = "";

        /// <summary>
        /// 归属受控根规范路径（与 RootId 同步有效）
        /// </summary>
        public string RootPath = "";

        /// <summary>
        /// 错误码（失败时有效——ROOT_UNKNOWN_ID / ROOT_OUT_OF_SCOPE / ROOT_BAD_PATH）
        /// </summary>
        public string ErrorCode = "";

        /// <summary>
        /// 错误说明（失败时有效）
        /// </summary>
        public string Message = "";
    }

    /// <summary>
    /// 根寻址公共解析器（A257）——受控根寻址的统一解析点：&lt;根id&gt;:&lt;相对&gt; · 裸根名 · 普通路径。
    /// <para>
    /// 纯字符串层：只做形态判定 / 根 id 映射 / 绝对化 / 归属根前缀匹配；不碰 writable、不碰重解析点、不碰存在性——
    /// 权限与安全判定归调用方（工具侧），两层不合并。
    /// </para>
    /// <para>
    /// 两点可用：<see cref="Expand"/>（只展开不判归属——注入清单等「不强制根内」场景）·
    /// <see cref="ResolveOwned"/>（展开 + 归属判定——工具路径等「必须根内」场景）。
    /// </para>
    /// </summary>
    public static class RootPathResolver
    {
        /// <summary>
        /// 错误码——根寻址形态但根 id 未命中（Windows 盘符 `C:\` 不作根寻址，不在此列）
        /// </summary>
        public const string ErrUnknownId = "ROOT_UNKNOWN_ID";

        /// <summary>
        /// 错误码——解析结果不落在任何受控根内
        /// </summary>
        public const string ErrOutOfScope = "ROOT_OUT_OF_SCOPE";

        /// <summary>
        /// 错误码——路径字面量非法（GetFullPath 失败）
        /// </summary>
        public const string ErrBadPath = "ROOT_BAD_PATH";

        /// <summary>
        /// 展开（点一）——形态判定 + 根 id 映射 + 绝对化；不判归属、不判权限。
        /// 非根寻址形态按 basePath 基准绝对化（basePath 缺省 = 根表首根）。
        /// </summary>
        /// <param name="path">输入路径（根寻址形态 / 裸根名 / 普通路径）</param>
        /// <param name="roots">受控根表（全局表；空表时相对与裸根形态按普通路径处理）</param>
        /// <returns>解析结果（Ok / FullPath / RootId / RootPath / ErrorCode / Message）</returns>
        public static RootResolveResult Expand(string path, WorkspaceConfig.RootEntry[] roots)
        {
            return Expand(path, roots, null);
        }

        /// <summary>
        /// 展开（点一）——形态判定 + 根 id 映射 + 绝对化；不判归属、不判权限。
        /// </summary>
        /// <param name="path">输入路径（根寻址形态 / 裸根名 / 普通路径）</param>
        /// <param name="roots">受控根表（全局表；空表时相对与裸根形态按普通路径处理）</param>
        /// <param name="basePath">相对路径基准（空 = 根表首根；要 CWD 语义显式传 Environment.CurrentDirectory）</param>
        /// <returns>解析结果（Ok / FullPath / RootId / RootPath / ErrorCode / Message）</returns>
        public static RootResolveResult Expand(string path, WorkspaceConfig.RootEntry[] roots, string basePath)
        {
            RootResolveResult result = new RootResolveResult();
            if (path == null || path.Length == 0)
            {
                result.Ok = true;
                result.FullPath = "";
                return result;
            }
            WorkspaceConfig.RootEntry[] table = roots == null ? new WorkspaceConfig.RootEntry[0] : roots;

            // [段1] 根寻址 / 裸根名形态
            int sep = path.IndexOf(':');
            if (sep > 0)
            {
                string id = path.Substring(0, sep);
                WorkspaceConfig.RootEntry hit = FindById(table, id);
                if (hit != null)
                {
                    return CombineWithRoot(hit, path.Substring(sep + 1), result);
                }
                if (IsRootAddress(path))
                {
                    result.Ok = false;
                    result.ErrorCode = ErrUnknownId;
                    result.Message = "未知受控根 id: " + id;
                    return result;
                }
            }
            else if (sep <= 0 && table.Length > 0)
            {
                // 裸根名——整串即某根 id（无冒号）→ 该根根目录
                WorkspaceConfig.RootEntry bare = FindById(table, path);
                if (bare != null)
                {
                    string barePath = NormalizeRootPath(bare.Path);
                    result.Ok = true;
                    result.FullPath = barePath;
                    result.RootId = bare.Id;
                    result.RootPath = barePath;
                    return result;
                }
            }

            // [段2] 普通路径——按基准绝对化
            return Absolutize(path, table, basePath, result);
        }

        /// <summary>
        /// 解析 + 归属判定（点二）——先展开，再判结果落在哪个受控根内（最长前缀匹配，带分隔符边界）。
        /// 越界不属于异常语义——返回 ErrOutOfScope 错误码（失败可见由调用方按业务处置）。
        /// </summary>
        /// <param name="path">输入路径（根寻址形态 / 裸根名 / 普通路径）</param>
        /// <param name="roots">受控根表（全局表）</param>
        /// <returns>解析结果（归属成功时 RootId / RootPath 已填）</returns>
        public static RootResolveResult ResolveOwned(string path, WorkspaceConfig.RootEntry[] roots)
        {
            return ResolveOwned(path, roots, null);
        }

        /// <summary>
        /// 解析 + 归属判定（点二）——先展开，再判结果落在哪个受控根内（最长前缀匹配，带分隔符边界）。
        /// </summary>
        /// <param name="path">输入路径（根寻址形态 / 裸根名 / 普通路径）</param>
        /// <param name="roots">受控根表（全局表）</param>
        /// <param name="basePath">相对路径基准（空 = 根表首根）</param>
        /// <returns>解析结果（归属成功时 RootId / RootPath 已填）</returns>
        public static RootResolveResult ResolveOwned(string path, WorkspaceConfig.RootEntry[] roots, string basePath)
        {
            RootResolveResult result = Expand(path, roots, basePath);
            if (!result.Ok)
            {
                return result;
            }
            WorkspaceConfig.RootEntry[] table = roots == null ? new WorkspaceConfig.RootEntry[0] : roots;
            if (result.FullPath.Length == 0)
            {
                return result;
            }
            WorkspaceConfig.RootEntry owner = FindOwningRoot(table, result.FullPath);
            if (owner == null)
            {
                RootResolveResult fail = new RootResolveResult();
                fail.Ok = false;
                fail.FullPath = result.FullPath;
                fail.ErrorCode = ErrOutOfScope;
                fail.Message = "路径超出受控根范围：" + result.FullPath;
                return fail;
            }
            result.RootId = owner.Id;
            result.RootPath = NormalizeRootPath(owner.Path);
            return result;
        }

        /// <summary>
        /// 根寻址形态判定——首个冒号出现在首个路径分隔符之前（Windows 盘符 `C:\` 不算）。
        /// 前端镜像 images.js imgIsRootAddress 为同判据 JS 实现（JS 不能引用程序集——A257 收口锁定一致）。
        /// </summary>
        /// <param name="path">输入路径</param>
        /// <returns>true=按受控根寻址解析</returns>
        public static bool IsRootAddress(string path)
        {
            if (path == null || path.Length == 0)
            {
                return false;
            }
            int colon = path.IndexOf(':');
            if (colon <= 0)
            {
                return false;
            }
            int slash = path.IndexOf('/');
            int back = path.IndexOf('\\');
            int sep = -1;
            if (slash >= 0 && back >= 0)
            {
                sep = slash < back ? slash : back;
            }
            else if (slash >= 0)
            {
                sep = slash;
            }
            else
            {
                sep = back;
            }
            if (sep >= 0 && sep < colon)
            {
                return false;
            }
            // 单字母盘符 + 紧随分隔符（C:\…）——按本地绝对路径处理，不作根寻址
            if (colon == 1 && sep == 2)
            {
                return false;
            }
            return true;
        }

        /// <summary>
        /// 按 id 查根条目（大小写不敏感）
        /// </summary>
        /// <param name="table">根表</param>
        /// <param name="id">根 id</param>
        /// <returns>命中条目；未命中返回 null</returns>
        private static WorkspaceConfig.RootEntry FindById(WorkspaceConfig.RootEntry[] table, string id)
        {
            for (int i = 0; i < table.Length; i = i + 1)
            {
                WorkspaceConfig.RootEntry entry = table[i];
                if (entry == null || entry.Id == null || entry.Id.Length == 0)
                {
                    continue;
                }
                if (string.Equals(entry.Id, id, StringComparison.OrdinalIgnoreCase))
                {
                    return entry;
                }
            }
            return null;
        }

        /// <summary>
        /// 归属根匹配——最长前缀（带分隔符边界，防兄弟目录误伤）
        /// </summary>
        /// <param name="table">根表</param>
        /// <param name="full">规范绝对路径</param>
        /// <returns>归属条目；不在任何根内返回 null</returns>
        private static WorkspaceConfig.RootEntry FindOwningRoot(WorkspaceConfig.RootEntry[] table, string full)
        {
            WorkspaceConfig.RootEntry best = null;
            int bestLength = -1;
            string separator = Path.DirectorySeparatorChar.ToString();
            for (int i = 0; i < table.Length; i = i + 1)
            {
                WorkspaceConfig.RootEntry entry = table[i];
                if (entry == null)
                {
                    continue;
                }
                string root = NormalizeRootPath(entry.Path);
                if (root.Length == 0)
                {
                    continue;
                }
                bool inside = string.Equals(full, root, StringComparison.OrdinalIgnoreCase)
                    || full.StartsWith(root + separator, StringComparison.OrdinalIgnoreCase);
                if (inside && root.Length > bestLength)
                {
                    best = entry;
                    bestLength = root.Length;
                }
            }
            return best;
        }

        /// <summary>
        /// 根寻址展开——组合根路径与相对段并绝对化
        /// </summary>
        /// <param name="root">命中根条目</param>
        /// <param name="rel">相对段</param>
        /// <param name="result">结果载体</param>
        /// <returns>解析结果</returns>
        private static RootResolveResult CombineWithRoot(WorkspaceConfig.RootEntry root, string rel, RootResolveResult result)
        {
            string rootPath = NormalizeRootPath(root.Path);
            try
            {
                result.FullPath = Path.GetFullPath(Path.Combine(rootPath, rel));
                result.Ok = true;
                result.RootId = root.Id;
                result.RootPath = rootPath;
                return result;
            }
            catch (Exception ex)
            {
                result.Ok = false;
                result.ErrorCode = ErrBadPath;
                result.Message = ex.Message;
                return result;
            }
        }

        /// <summary>
        /// 普通路径绝对化——全限定直用，相对则拼基准（基准空 = 根表首根）
        /// </summary>
        /// <param name="path">输入路径</param>
        /// <param name="table">根表</param>
        /// <param name="basePath">相对基准</param>
        /// <param name="result">结果载体</param>
        /// <returns>解析结果</returns>
        private static RootResolveResult Absolutize(string path, WorkspaceConfig.RootEntry[] table, string basePath, RootResolveResult result)
        {
            string basis = basePath;
            if (basis == null || basis.Length == 0)
            {
                basis = table.Length > 0 ? NormalizeRootPath(table[0].Path) : "";
            }
            try
            {
                if (Path.IsPathFullyQualified(path) || basis.Length == 0)
                {
                    result.FullPath = Path.GetFullPath(path);
                }
                else
                {
                    result.FullPath = Path.GetFullPath(Path.Combine(basis, path));
                }
                result.Ok = true;
                return result;
            }
            catch (Exception ex)
            {
                result.Ok = false;
                result.ErrorCode = ErrBadPath;
                result.Message = ex.Message;
                return result;
            }
        }

        /// <summary>
        /// 根路径规范化——GetFullPath + 去尾分隔符（保卷根）；与 PathBoundary.NormalizeRoot 同规
        /// </summary>
        /// <param name="root">原始根路径</param>
        /// <returns>规范根路径（空输入返回空串）</returns>
        private static string NormalizeRootPath(string root)
        {
            if (root == null || root.Length == 0)
            {
                return "";
            }
            string full = Path.GetFullPath(root);
            string volume = Path.GetPathRoot(full);
            if (volume != null && string.Equals(full, volume, StringComparison.OrdinalIgnoreCase))
            {
                return full;
            }
            return full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
    }
}
