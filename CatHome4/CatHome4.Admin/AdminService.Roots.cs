using System;
using System.Collections.Generic;
using System.IO;

namespace CatHome4.Admin
{
    /// <summary>
    /// Program 管理端点面分部——受控根固定命名表与写入校验（design-ch4-workspace §三 / design-ch4-cat-admin §十）。
    /// 固定命名根（4 个）：id + note + writable 由系统固定，管理员面只能改 path；其余根自由增删改。
    /// 判定三处同源——服务读面 fixed 标记 / 写入校验 / 前端渲染。
    /// </summary>
    internal static partial class AdminService
    {
        /// <summary>
        /// 固定命名根定义——id / note / writable 三列固定（note ≤20 字）。
        /// </summary>
        internal sealed class FixedRootDef
        {
            /// <summary>根标识（小写）</summary>
            public string Id;

            /// <summary>固定认路注释（≤20 字）</summary>
            public string Note;

            /// <summary>固定读写标志</summary>
            public bool Writable;
        }

        /// <summary>
        /// 固定命名根表——单一真相源（读面标记 / 写面校验 / 前端渲染同源）。
        /// </summary>
        internal static readonly FixedRootDef[] FixedRoots = new FixedRootDef[]
        {
            new FixedRootDef() { Id = "workspace", Note = "系统工作区（必选）", Writable = true },
            new FixedRootDef() { Id = "mau", Note = "Mau 仓库（工具组仓库根）", Writable = true },
            new FixedRootDef() { Id = "mauout", Note = "运行实例部署区（重启目标）", Writable = true },
            new FixedRootDef() { Id = "data", Note = "CH4 应用数据（appdata）", Writable = false }
        };

        // [段1] 归一与判定
        /// <summary>
        /// 根 id 归一——去空白 + 大写转小写（写面统一小写；读面按小写识别）。
        /// </summary>
        /// <param name="id">原始根标识</param>
        /// <returns>归一后标识（空输入返回空串）</returns>
        internal static string NormalizeRootId(string id)
        {
            if (id == null)
            {
                return "";
            }
            return id.Trim().ToLowerInvariant();
        }

        /// <summary>
        /// 根 id 字符集校验——仅小写字母 + 数字（下划线退役；大写由归一化处理）。
        /// </summary>
        /// <param name="id">归一后的根标识</param>
        /// <returns>true=合法</returns>
        internal static bool IsValidRootIdChars(string id)
        {
            if (id == null || id.Length == 0)
            {
                return false;
            }
            for (int i = 0; i < id.Length; i = i + 1)
            {
                char ch = id[i];
                bool ok = (ch >= 'a' && ch <= 'z') || (ch >= '0' && ch <= '9');
                if (!ok)
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// 固定根定义查找——判定与校验共用。
        /// </summary>
        /// <param name="id">根标识（任意大小写）</param>
        /// <returns>定义；非固定根 null</returns>
        internal static FixedRootDef FindFixedRoot(string id)
        {
            string norm = NormalizeRootId(id);
            for (int i = 0; i < FixedRoots.Length; i = i + 1)
            {
                if (string.Equals(FixedRoots[i].Id, norm, StringComparison.Ordinal))
                {
                    return FixedRoots[i];
                }
            }
            return null;
        }

        /// <summary>
        /// 固定根判定——读面标记与写面校验同源。
        /// </summary>
        /// <param name="id">根标识（任意大小写）</param>
        /// <returns>true=固定命名根</returns>
        internal static bool IsFixedRootId(string id)
        {
            return FindFixedRoot(id) != null;
        }

        // [段2] 写入校验
        /// <summary>
        /// 受控根提交校验——id 字符集（小写字母数字）+ 重名（忽略大小写）+ path 绝对且存在 + 固定根齐备且三列一致。
        /// 语义：缺任一固定根 → 拒绝（不静默造根）；固定根 note/writable 与定义不符 → 拒绝（前端已灰化，直调 API 亦同规）。
        /// </summary>
        /// <param name="rootsIn">提交条目（Id 须已归一为小写）</param>
        /// <returns>错误文案（空 = 通过）</returns>
        private static string ValidateRootsInput(List<WorkspaceRootInput> rootsIn)
        {
            List<string> ids = new List<string>();
            for (int i = 0; i < rootsIn.Count; i = i + 1)
            {
                WorkspaceRootInput input = rootsIn[i];
                if (input.Id.Length == 0)
                {
                    return "roots[" + i.ToString() + "] id 为空";
                }
                if (!IsValidRootIdChars(input.Id))
                {
                    return "roots[" + i.ToString() + "] id 非法（仅小写字母/数字）: " + input.Id;
                }
                bool dup = false;
                for (int k = 0; k < ids.Count; k = k + 1)
                {
                    if (string.Equals(ids[k], input.Id, StringComparison.OrdinalIgnoreCase))
                    {
                        dup = true;
                        break;
                    }
                }
                if (dup)
                {
                    return "roots id 重复（大小写不敏感）: " + input.Id;
                }
                ids.Add(input.Id);
                if (input.Path.Length == 0)
                {
                    return "roots[" + i.ToString() + "] path 为空";
                }
                if (!Path.IsPathRooted(input.Path))
                {
                    return "roots[" + i.ToString() + "] path 非绝对路径: " + input.Path;
                }
                if (!Directory.Exists(input.Path))
                {
                    return "roots[" + i.ToString() + "] 目录不存在: " + input.Path;
                }
                FixedRootDef def = FindFixedRoot(input.Id);
                if (def != null)
                {
                    if (!string.Equals(input.Note, def.Note, StringComparison.Ordinal))
                    {
                        return "系统根 " + def.Id + " 的注释固定为「" + def.Note + "」，不可修改";
                    }
                    if (input.Writable != def.Writable)
                    {
                        string want = def.Writable ? "读写" : "只读";
                        return "系统根 " + def.Id + " 的读写标志固定为「" + want + "」，不可修改";
                    }
                }
            }
            for (int f = 0; f < FixedRoots.Length; f = f + 1)
            {
                bool found = false;
                for (int k = 0; k < ids.Count; k = k + 1)
                {
                    if (string.Equals(ids[k], FixedRoots[f].Id, StringComparison.OrdinalIgnoreCase))
                    {
                        found = true;
                        break;
                    }
                }
                if (!found)
                {
                    return "缺少系统根 " + FixedRoots[f].Id + "（固定命名根不可移除）";
                }
            }
            return "";
        }
    }
}
