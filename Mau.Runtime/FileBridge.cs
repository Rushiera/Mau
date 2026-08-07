namespace Mau.Runtime
{
    /// <summary>
    /// 文件桥（程序级）——受控文件系统服务经 DataBox 绑定/解析（BRIK 唯一数据协议）
    /// </summary>
    public static class FileBridge
    {
        /// <summary>
        /// 配置并绑定受控文件系统——宿主启动时调用
        /// </summary>
        /// <param name="roots">允许根目录</param>
        /// <param name="recycleRoot">回收目录</param>
        public static void ConfigureRoots(string[] roots, string recycleRoot)
        {
            DataBox.Bind<FileSystemService>(new FileSystemService(roots, recycleRoot));
        }

        /// <summary>
        /// 当前受控文件系统——未绑定返回 null（BRIK 内判空）
        /// </summary>
        /// <returns>文件系统服务或 null</returns>
        public static FileSystemService? CurrentFileSystem()
        {
            FileSystemService? fs;
            DataBox.TryResolve<FileSystemService>(out fs);
            return fs;
        }
    }
}
