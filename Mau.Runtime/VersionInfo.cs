using System;
using System.Reflection;

namespace Mau.Runtime
{
    /// <summary>
    /// 版本信息——入口程序集版本统一读取（宿主不再硬编码版本号）。
    /// 优先 AssemblyInformationalVersion（csproj &lt;Version&gt; 映射，去 +hash 后缀）；
    /// 缺失时回落 AssemblyName.Version。
    /// </summary>
    public static class VersionInfo
    {
        /// <summary>
        /// 入口程序集名
        /// </summary>
        /// <returns>程序集名，无入口程序集返回空串</returns>
        public static string GetEntryName()
        {
            Assembly? entry = Assembly.GetEntryAssembly();
            if (entry == null)
            {
                return "";
            }
            return entry.GetName().Name ?? "";
        }

        /// <summary>
        /// 入口程序集版本——InformationalVersion 去 +hash 后缀，缺失回落 AssemblyVersion
        /// </summary>
        /// <returns>版本字符串（如 "0.35"），无入口程序集返回空串</returns>
        public static string GetEntryVersion()
        {
            Assembly? entry = Assembly.GetEntryAssembly();
            if (entry == null)
            {
                return "";
            }
            AssemblyInformationalVersionAttribute? info = entry.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
            if (info != null && info.InformationalVersion.Length > 0)
            {
                string version = info.InformationalVersion;
                int plus = version.IndexOf('+');
                if (plus > 0)
                {
                    version = version.Substring(0, plus);
                }
                return version;
            }
            AssemblyName name = entry.GetName();
            if (name.Version != null)
            {
                return name.Version.ToString();
            }
            return "";
        }
}
}
