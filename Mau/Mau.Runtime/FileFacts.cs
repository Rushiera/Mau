using System;

namespace Mau.Runtime
{
    /// <summary>
    /// 文件条目事实——相对路径 + 目录标志（A228 档 2 · B3：服务只给事实，展示形态归积木层）。
    /// </summary>
    public sealed class FsEntry
    {
        /// <summary>
        /// 相对根路径（不含目录尾标——尾标属展示约定）
        /// </summary>
        public string RelativePath = "";

        /// <summary>
        /// 是否目录
        /// </summary>
        public bool IsDir;
    }

    /// <summary>
    /// git 目录特征事实——相对路径 + HEAD 摘要（展示文案归积木层）。
    /// </summary>
    public sealed class FsGitFact
    {
        /// <summary>
        /// 相对根路径（含 .git 段）
        /// </summary>
        public string RelativePath = "";

        /// <summary>
        /// HEAD 摘要（分支 → 短 commit / detached 短 commit；不可读时为空串）
        /// </summary>
        public string Head = "";
    }

    /// <summary>
    /// 目录树事实——条目 + git 特征 + 忽略与总量计数（limit 只影响 Entries 条数；格式化归积木层）。
    /// </summary>
    public sealed class FsTreeFacts
    {
        /// <summary>
        /// 条目（≤ limit；目录项 IsDir = true）
        /// </summary>
        public FsEntry[] Entries = new FsEntry[0];

        /// <summary>
        /// git 特征事实（忽略目录中的 .git——全量返回，配比策略归积木）
        /// </summary>
        public FsGitFact[] GitFacts = new FsGitFact[0];

        /// <summary>
        /// 被忽略的目录数（.git/bin/obj 等——不展开但计数可见）
        /// </summary>
        public int IgnoredDirs;

        /// <summary>
        /// 实际条目总数（未截断口径——与 Entries.Length 之差即截断量）
        /// </summary>
        public int Total;
    }

    /// <summary>
    /// 文件名搜索结果事实——相对路径列表 + 忽略与命中计数。
    /// </summary>
    public sealed class FsFindFacts
    {
        /// <summary>
        /// 命中相对路径（≤ limit，稳定排序）
        /// </summary>
        public string[] RelativePaths = new string[0];

        /// <summary>
        /// 被忽略的目录数
        /// </summary>
        public int IgnoredDirs;

        /// <summary>
        /// 实际命中总数（未截断口径）
        /// </summary>
        public int Matched;
    }

    /// <summary>
    /// 内容命中事实——相对路径 + 行号 + 上下文（前后 ≤10 字符）。
    /// </summary>
    public sealed class FsGrepHit
    {
        /// <summary>
        /// 相对搜索根路径
        /// </summary>
        public string RelativePath = "";

        /// <summary>
        /// 行号（1 起）
        /// </summary>
        public int Line;

        /// <summary>
        /// 命中上下文（前后 ≤10 字符）
        /// </summary>
        public string Context = "";
    }

    /// <summary>
    /// 内容搜索跳过事实——相对路径 + 原因（异常类型名）。
    /// </summary>
    public sealed class FsGrepSkip
    {
        /// <summary>
        /// 相对搜索根路径
        /// </summary>
        public string RelativePath = "";

        /// <summary>
        /// 跳过原因（异常类型名——失败可见，不静默）
        /// </summary>
        public string Reason = "";
    }

    /// <summary>
    /// 内容搜索事实——命中 + 跳过 + 忽略与命中总数。
    /// </summary>
    public sealed class FsGrepFacts
    {
        /// <summary>
        /// 命中条目（≤ limit）
        /// </summary>
        public FsGrepHit[] Hits = new FsGrepHit[0];

        /// <summary>
        /// 跳过条目（不可读文件——具名告警随结果返回）
        /// </summary>
        public FsGrepSkip[] Skipped = new FsGrepSkip[0];

        /// <summary>
        /// 被忽略的目录数
        /// </summary>
        public int IgnoredDirs;

        /// <summary>
        /// 实际命中总数（未截断口径）
        /// </summary>
        public int HitTotal;
    }

    /// <summary>
    /// PE 版本事实——版本三件 + 元数据（展示文案与回落注记归积木层）。
    /// </summary>
    public sealed class FsVersionFacts
    {
        /// <summary>
        /// 解析后的绝对路径
        /// </summary>
        public string ResolvedPath = "";

        /// <summary>
        /// 版本（ProductVersion 优先，空则回落 FileVersion）
        /// </summary>
        public string Version = "";

        /// <summary>
        /// 是否发生了回落（ProductVersion 为空 → 用 FileVersion）
        /// </summary>
        public bool FallbackUsed;

        /// <summary>
        /// FileVersion（可能为空串——「(无)」形态归积木）
        /// </summary>
        public string FileVersion = "";

        /// <summary>
        /// ProductVersion（可能为空串——「(无)」形态归积木）
        /// </summary>
        public string ProductVersion = "";

        /// <summary>
        /// 最后修改时间
        /// </summary>
        public DateTime LastWrite;

        /// <summary>
        /// 文件大小（字节）
        /// </summary>
        public long SizeBytes;
    }
}
