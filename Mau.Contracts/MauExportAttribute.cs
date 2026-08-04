using System;

namespace Mau.Contracts
{
    /// <summary>
    /// 标记口袋程序集允许由 Mau 显式发现和调用的 public static 方法。
    /// 该标记只提供导出清单，不构成安全沙箱或权限授予。
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false,
        Inherited = false)]
    public sealed class MauExportAttribute : Attribute
    {
        /// <summary>
        /// 创建导出标记
        /// </summary>
        public MauExportAttribute()
        {
        }
    }
}
