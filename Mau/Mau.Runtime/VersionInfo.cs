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
            string? name = entry.GetName().Name;
            if (name == null)
            {
                name = "";
            }
            return name;
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
        /// <summary>
        /// 程序集编译时刻——InformationalVersion "+" 后 yyyyMMddHHmmss（Directory.Build.targets 注入）；
        /// 元数据缺失回落文件落盘时间（LastWriteTime——旧产物无时间戳）
        /// </summary>
        /// <param name="asm">目标程序集（已加载实例——当前运行版本）</param>
        /// <returns>编译时间（"yyyy-MM-dd HH:mm:ss"），解析失败返回空串</returns>
        public static string GetAssemblyBuildTime(Assembly asm)
        {
            string buildTime = "";
            AssemblyInformationalVersionAttribute? info = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
            if (info != null && info.InformationalVersion.Length > 0)
            {
                int plus = info.InformationalVersion.IndexOf('+');
                if (plus > 0 && info.InformationalVersion.Length >= plus + 15)
                {
                    string stamp = info.InformationalVersion.Substring(plus + 1, 14);
                    DateTime parsed;
                    if (DateTime.TryParseExact(stamp, "yyyyMMddHHmmss", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out parsed))
                    {
                        buildTime = parsed.ToString("yyyy-MM-dd HH:mm:ss");
                    }
                }
            }
            if (buildTime.Length == 0)
            {
                try
                {
                    string? location = asm.Location;
                    if (location != null && location.Length > 0 && System.IO.File.Exists(location))
                    {
                        buildTime = System.IO.File.GetLastWriteTime(location).ToString("yyyy-MM-dd HH:mm:ss");
                    }
                }
                catch (Exception)
                {
                    buildTime = "";
                }
            }
            return buildTime;
        }
        /// <summary>
        /// 入口程序集编译时刻——InformationalVersion "+" 后 yyyyMMddHHmmss，缺失回落文件落盘时间
        /// </summary>
        /// <returns>编译时间（"yyyy-MM-dd HH:mm:ss"），无入口程序集返回空串</returns>
        public static string GetEntryBuildTime()
        {
            Assembly? entry = Assembly.GetEntryAssembly();
            if (entry == null)
            {
                return "";
            }
            return GetAssemblyBuildTime(entry);
        }
    }
}
