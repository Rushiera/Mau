using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.CodeAnalysis;

namespace Mau.Development
{
    /// <summary>
    /// MauPocketCompiler 引用集分部——TPA + 基座必需 + 显式清单的元数据引用构建（partial 分部）。
    /// </summary>
    public sealed partial class MauPocketCompiler
    {
        /// <summary>
        /// 建立 Trusted Platform Assemblies 和 Mau 程序集引用
        /// </summary>
        /// <returns>元数据引用</returns>
        private MetadataReference[] BuildReferences()
        {
            List<MetadataReference> references = new List<MetadataReference>();
            string? trusted = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string;
            if (trusted == null)
            {
                throw new InvalidOperationException(
                    "Trusted platform assemblies are unavailable.");
            }
            string[] paths = trusted.Split(Path.PathSeparator,
                StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < paths.Length; i = i + 1)
            {
                references.Add(MetadataReference.CreateFromFile(paths[i]));
            }
            AddMauReferences(references);
            return references.ToArray();
        }

        /// <summary>
        /// 附加基座必需程序集——Mau.Runtime/Mau.Contracts（宿主目录，大小写不敏感匹配文件名）
        /// </summary>
        /// <param name="baseDir">宿主目录</param>
        /// <param name="references">引用集合</param>
        private static void AddBaseRequired(string baseDir, List<MetadataReference> references)
        {
            string[] required = new string[]
            {
                "Mau.Runtime.dll",
                "Mau.Contracts.dll"
            };
            string[] dlls = Directory.GetFiles(baseDir, "*.dll", SearchOption.TopDirectoryOnly);
            for (int i = 0; i < dlls.Length; i = i + 1)
            {
                string name = Path.GetFileName(dlls[i]);
                for (int r = 0; r < required.Length; r = r + 1)
                {
                    if (string.Equals(name, required[r], StringComparison.OrdinalIgnoreCase))
                    {
                        references.Add(MetadataReference.CreateFromFile(dlls[i]));
                    }
                }
            }
        }

        /// <summary>
        /// 建立引用集（显式模式 D25）——TPA + 基座必需（Mau.Runtime/Mau.Contracts）+ 显式引用清单。
        /// 显式引用缺失时静默跳过（解析阶段已校验存在性，此处容错兜底）。
        /// </summary>
        /// <param name="extraReferences">显式引用 dll 绝对路径清单（mauproj 引用: 解析结果）</param>
        /// <returns>元数据引用</returns>
        private MetadataReference[] BuildReferences(string[] extraReferences)
        {
            List<MetadataReference> references = new List<MetadataReference>();
            string? trusted = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string;
            if (trusted == null)
            {
                throw new InvalidOperationException("Trusted platform assemblies are unavailable.");
            }
            string[] paths = trusted.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < paths.Length; i = i + 1)
            {
                references.Add(MetadataReference.CreateFromFile(paths[i]));
            }
            // 基座必需——宿主目录中 Mau.Runtime / Mau.Contracts（生成物契约依赖）
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            if (Directory.Exists(baseDir))
            {
                AddBaseRequired(baseDir, references);
            }
            // 显式引用清单——mauproj 引用: 解析结果（去重）
            for (int i = 0; i < extraReferences.Length; i = i + 1)
            {
                if (File.Exists(extraReferences[i]))
                {
                    references.Add(MetadataReference.CreateFromFile(extraReferences[i]));
                }
            }
            return references.ToArray();
        }

        /// <summary>
        /// 附加宿主输出目录的全部 dll——基座 + 契约 + 积木 + 宿主自定义（如 CH4.Contracts）
        /// 全量引用语义：生成物编译引用集 = 宿主目录全量（Learn H15 判例——发布完整性=探测路径完整性）
        /// </summary>
        /// <param name="references">引用集合</param>
        private void AddMauReferences(List<MetadataReference> references)
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            if (!Directory.Exists(baseDir))
            {
                return;
            }
            string[] dlls = Directory.GetFiles(baseDir, "*.dll",
                SearchOption.TopDirectoryOnly);
            Array.Sort(dlls, StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < dlls.Length; i = i + 1)
            {
                references.Add(MetadataReference.CreateFromFile(dlls[i]));
            }
        }
    }
}
