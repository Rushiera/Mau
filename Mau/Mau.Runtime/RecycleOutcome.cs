using System;

namespace Mau.Runtime
{
    /// <summary>
    /// 软删除结果——回收站落点 + 被移动内容统计（目录=整棵子树合计；文件=单件）
    /// </summary>
    public sealed class RecycleOutcome
    {
        /// <summary>
        /// 回收站内新路径（可恢复的唯一落点）
        /// </summary>
        public string Target = "";

        /// <summary>
        /// 是否为目录（整棵子树整体迁移——结构在回收站内保留）
        /// </summary>
        public bool IsDirectory;

        /// <summary>
        /// 文件数（目录=子树合计；文件=1）
        /// </summary>
        public int FileCount;

        /// <summary>
        /// 子目录数（目录=子树合计；文件=0）
        /// </summary>
        public int DirectoryCount;

        /// <summary>
        /// 合计字节数（目录=子树合计；文件=其长度）
        /// </summary>
        public long TotalBytes;
    }
}
