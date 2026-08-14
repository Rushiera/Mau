using System.Reflection;
using System.Runtime.Loader;

namespace Mau.Runtime
{
    /// <summary>
    /// 生成物程序集加载上下文——collectible ALC，每 DLL 一个实例
    /// </summary>
    public sealed class FlowALC : AssemblyLoadContext
    {
        /// <summary>
        /// 构造 collectible ALC
        /// </summary>
        /// <param name="name">上下文名称——用于诊断</param>
        public FlowALC(string name)
            : base(name, isCollectible: true)
        {
        }

        /// <summary>
        /// 程序集解析——Mau.Runtime 回落默认 ALC，其余不解析（生成物自包含）
        /// </summary>
        /// <param name="name">程序集名</param>
        /// <returns>null=回落默认 ALC 或自身已加载</returns>
        protected override Assembly? Load(AssemblyName name)
        {
            if (name.Name == "Mau.Runtime")
            {
                return null;
            }
            return null;
        }

        /// <summary>
        /// 共享句柄加载——FileStream + FileShare.ReadWrite|Delete（不锁文件，支持热重载原子替换）
        /// </summary>
        /// <param name="assemblyPath">dll 路径</param>
        /// <returns>程序集</returns>
        public Assembly LoadShared(string assemblyPath)
        {
            using (System.IO.FileStream stream = new System.IO.FileStream(assemblyPath, System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.ReadWrite | System.IO.FileShare.Delete))
            {
                return LoadFromStream(stream);
            }
        }
    }
}
