using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace Mau.Runtime
{
    /// <summary>
    /// 程序注册表——Mau 构筑程序的进程管理协议（mau-app.json）
    /// 注册目录：%LOCALAPPDATA%/Mau/apps/——用户级，跨程序共享，Supervisor 统一扫描
    /// </summary>
    public static class AppRegistry
    {
        /// <summary>
        /// 注册目录——用户级统一
        /// </summary>
        public static string AppsDir
        {
            get
            {
                string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                return Path.Combine(local, "Mau", "apps");
            }
        }

        /// <summary>
        /// 注册文件路径
        /// </summary>
        /// <param name="name">程序名</param>
        /// <returns>json 文件路径</returns>
        public static string FilePath(string name)
        {
            return Path.Combine(AppsDir, name + ".json");
        }

        /// <summary>
        /// 注册——原子写（临时文件 + 改名，避免半写文件被读到）
        /// </summary>
        /// <param name="identity">程序身份</param>
        public static void Register(AppIdentity identity)
{
            if (identity == null)
            {
                throw new ArgumentNullException("identity");
            }
            if (string.IsNullOrEmpty(identity.Name))
            {
                throw new ArgumentException("程序名不能为空", "identity");
            }
            Directory.CreateDirectory(AppsDir);
            string json = JsonSerializer.Serialize(identity, JsonOptions);
            // 原子写——统一实现 ConfigStore.AtomicWrite（审查修复轮 2026-08-11 决策3）
            ConfigStore.AtomicWrite(FilePath(identity.Name), json);
        }
        /// <summary>
        /// 注销——删除注册文件
        /// </summary>
        /// <param name="name">程序名</param>
        public static void Unregister(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return;
            }
            string path = FilePath(name);
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }

        /// <summary>
        /// 按名读取注册信息
        /// </summary>
        /// <param name="name">程序名</param>
        /// <returns>身份信息，未注册返回 null</returns>
        public static AppIdentity? Get(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return null;
            }
            string path = FilePath(name);
            if (!File.Exists(path))
            {
                return null;
            }
            try
            {
                string json = File.ReadAllText(path);
                return JsonSerializer.Deserialize<AppIdentity>(json, JsonOptions);
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// JSON 选项——序列化 public 字段（AppIdentity 为字段模型）
        /// </summary>
        private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

        /// <summary>
        /// 构造 JSON 选项
        /// </summary>
        /// <returns>字段序列化选项</returns>
        private static JsonSerializerOptions CreateJsonOptions()
        {
            JsonSerializerOptions options = new JsonSerializerOptions();
            options.IncludeFields = true;
            return options;
        }

        /// <summary>
        /// 枚举全部注册程序
        /// </summary>
        /// <returns>身份数组（按名排序）</returns>
        public static AppIdentity[] List()
        {
            if (!Directory.Exists(AppsDir))
            {
                return new AppIdentity[0];
            }
            List<AppIdentity> list = new List<AppIdentity>();
            string[] files = Directory.GetFiles(AppsDir, "*.json", SearchOption.TopDirectoryOnly);
            for (int i = 0; i < files.Length; i++)
            {
                try
                {
                    string json = File.ReadAllText(files[i]);
                    AppIdentity? identity = JsonSerializer.Deserialize<AppIdentity>(json, JsonOptions);
                    if (identity != null && !string.IsNullOrEmpty(identity.Name))
                    {
                        list.Add(identity);
                    }
                }
                catch (Exception)
                {
                    // 损坏的注册文件跳过——可由 --clean 清理
                }
            }
            list.Sort(delegate (AppIdentity a, AppIdentity b)
            {
                return string.CompareOrdinal(a.Name, b.Name);
            });
            return list.ToArray();
        }

        /// <summary>
        /// PID 存活检测——进程是否存在
        /// </summary>
        /// <param name="identity">程序身份</param>
        /// <returns>true=进程存活</returns>
        public static bool IsAlive(AppIdentity identity)
        {
            if (identity == null || identity.Pid <= 0)
            {
                return false;
            }
            try
            {
                using (Process? process = Process.GetProcessById(identity.Pid))
                {
                    return !process.HasExited;
                }
            }
            catch (ArgumentException)
            {
                return false;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }

        /// <summary>
        /// 按 PID 查进程名——存活校验辅助
        /// </summary>
        /// <param name="pid">进程 ID</param>
        /// <returns>进程名，不存在返回空串</returns>
        public static string ProcessName(int pid)
        {
            try
            {
                using (Process? process = Process.GetProcessById(pid))
                {
                    return process.ProcessName;
                }
            }
            catch (Exception)
            {
                return "";
            }
        }
    }
}
