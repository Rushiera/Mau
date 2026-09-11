using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using Mau.Contracts;

namespace Mau.Development
{
    /// <summary>
    /// 口袋程序集编译结果。
    /// </summary>
    public sealed class MauPocketCompileResult
    {
        /// <summary>
        /// 是否编译成功
        /// </summary>
        public bool Success;

        /// <summary>
        /// 唯一输出 DLL 绝对路径
        /// </summary>
        public string AssemblyPath;

        /// <summary>
        /// Roslyn 诊断摘要
        /// </summary>
        public string[] Diagnostics;

        /// <summary>
        /// 创建空结果
        /// </summary>
        public MauPocketCompileResult()
        {
            AssemblyPath = "";
            Diagnostics = Array.Empty<string>();
        }
    }

    /// <summary>
    /// 一次口袋程序集导出调用结果。
    /// </summary>
    public sealed class MauPocketInvocationResult
    {
        /// <summary>
        /// 导出方法返回值文本
        /// </summary>
        public string Result;

        /// <summary>
        /// 卸载检查后 ALC 是否已回收
        /// </summary>
        public bool Unloaded;

        /// <summary>
        /// 创建空调用结果
        /// </summary>
        public MauPocketInvocationResult()
        {
            Result = "";
            Unloaded = false;
        }
    }

    /// <summary>
    /// 使用 Roslyn 将单文件源码编译为唯一 DLL，并在每次调用时使用独立 collectible ALC。
    /// ALC 只提供卸载边界，不隔离恶意代码；调用权必须由更外层 Approval 控制。
    /// 编译引用 = 平台 TPA + 宿主目录全部 Mau.*.dll（基座 + 积木）。
    /// </summary>
    public sealed partial class MauPocketCompiler
    {
        /// <summary>
        /// 口袋输出根
        /// </summary>
        private readonly string MauPocketCompiler_Root;
        /// <summary>
        /// 绑定口袋输出根
        /// </summary>
        /// <param name="root">输出根</param>
        public MauPocketCompiler(string root)
        {
            if (string.IsNullOrWhiteSpace(root))
            {
                throw new ArgumentException("Pocket root is empty.", "root");
            }
            MauPocketCompiler_Root = Path.GetFullPath(root);
            Directory.CreateDirectory(MauPocketCompiler_Root);
        }

        /// <summary>
        /// 编译源码到唯一构建目录
        /// </summary>
        /// <param name="source">完整 C# 源码</param>
        /// <param name="logicalName">安全逻辑名</param>
        /// <returns>编译结果</returns>
        public MauPocketCompileResult Compile(string source, string logicalName, string? mapText = null)
{
            ValidateLogicalName(logicalName);
            // 行号映射——诊断反查表（D1：生成物行 → 语料行/积木源码行）
            _mapText = mapText;
            string buildId = DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffffffZ")
                + "-" + Guid.NewGuid().ToString("N");
            string buildRoot = Path.Combine(MauPocketCompiler_Root, buildId);
            Directory.CreateDirectory(buildRoot);
            string assemblyPath = Path.Combine(buildRoot, logicalName + ".dll");
            SyntaxTree syntax = CSharpSyntaxTree.ParseText(SafeText(source),
                new CSharpParseOptions(LanguageVersion.Latest));
            MetadataReference[] references = BuildReferences();
            CSharpCompilation compilation = CSharpCompilation.Create(
                logicalName + "_" + Guid.NewGuid().ToString("N"),
                new SyntaxTree[] { syntax }, references,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
                    optimizationLevel: OptimizationLevel.Release,
                    allowUnsafe: false,
                    nullableContextOptions: NullableContextOptions.Enable));
            MauPocketCompileResult result = new MauPocketCompileResult();
            result.AssemblyPath = assemblyPath;
            using (MemoryStream assembly = new MemoryStream())
            {
                EmitResult emit = compilation.Emit(assembly);
                result.Diagnostics = FormatDiagnostics(emit.Diagnostics);
                result.Success = emit.Success;
                if (emit.Success)
                {
                    WriteAtomic(assemblyPath, assembly.ToArray());
                }
            }
            return result;
        }
        /// <summary>
        /// 列出 DLL 中所有带 MauExport 的 public static 方法
        /// </summary>
        /// <param name="assemblyPath">口袋 DLL</param>
        /// <returns>Type.Method 清单</returns>
        public string[] ListExports(string assemblyPath)
        {
            string resolved = ResolveAssembly(assemblyPath);
            MauPocketLoadContext? context = null;
            try
            {
                Assembly assembly = LoadAssembly(resolved, out context);
                List<string> exports = new List<string>();
                Type[] types = assembly.GetTypes();
                for (int i = 0; i < types.Length; i = i + 1)
                {
                    MethodInfo[] methods = types[i].GetMethods(
                        BindingFlags.Public | BindingFlags.Static);
                    for (int m = 0; m < methods.Length; m = m + 1)
                    {
                        if (methods[m].GetCustomAttribute<MauExportAttribute>() != null)
                        {
                            exports.Add(types[i].FullName + "." + methods[m].Name);
                        }
                    }
                }
                exports.Sort(StringComparer.Ordinal);
                return exports.ToArray();
            }
            finally
            {
                if (context != null)
                {
                    context.Unload();
                }
            }
        }

        /// <summary>
        /// 在独立 ALC 调用一个无参或单 string 参数导出并立即卸载
        /// </summary>
        /// <param name="assemblyPath">口袋 DLL</param>
        /// <param name="typeName">完整类型名</param>
        /// <param name="methodName">方法名</param>
        /// <param name="argument">单 string 参数</param>
        /// <returns>调用和卸载结果</returns>
        public MauPocketInvocationResult Invoke(string assemblyPath, string typeName,
            string methodName, string argument)
        {
            string resolved = ResolveAssembly(assemblyPath);
            WeakReference weak;
            string resultText;
            InvokeInContext(resolved, typeName, methodName, argument,
                out resultText, out weak);
            for (int i = 0; i < 8 && weak.IsAlive; i = i + 1)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
            }
            MauPocketInvocationResult result = new MauPocketInvocationResult();
            result.Result = resultText;
            result.Unloaded = !weak.IsAlive;
            return result;
        }

        /// <summary>
        /// 在不向调用者泄漏 ALC 强引用的方法帧中执行导出
        /// </summary>
        /// <param name="assemblyPath">DLL</param>
        /// <param name="typeName">类型</param>
        /// <param name="methodName">方法</param>
        /// <param name="argument">参数</param>
        /// <param name="resultText">结果文本</param>
        /// <param name="weak">ALC 弱引用</param>
        private void InvokeInContext(string assemblyPath, string typeName,
            string methodName, string argument, out string resultText,
            out WeakReference weak)
        {
            MauPocketLoadContext context;
            Assembly assembly = LoadAssembly(assemblyPath, out context);
            weak = new WeakReference(context, true);
            Type? type = assembly.GetType(typeName, false, false);
            if (type == null)
            {
                context.Unload();
                throw new InvalidOperationException("Pocket type was not found.");
            }
            MethodInfo? method = type.GetMethod(methodName,
                BindingFlags.Public | BindingFlags.Static);
            if (method == null
                || method.GetCustomAttribute<MauExportAttribute>() == null)
            {
                context.Unload();
                throw new UnauthorizedAccessException("Pocket method is not exported.");
            }
            ParameterInfo[] parameters = method.GetParameters();
            object? value;
            if (parameters.Length == 0)
            {
                value = method.Invoke(null, null);
            }
            else if (parameters.Length == 1
                && parameters[0].ParameterType == typeof(string))
            {
                value = method.Invoke(null, new object[] { argument });
            }
            else
            {
                context.Unload();
                throw new InvalidOperationException(
                    "Pocket export must accept zero arguments or one string.");
            }
            resultText = "";
            if (value != null)
            {
                string? converted = value.ToString();
                if (converted != null)
                {
                    resultText = converted;
                }
            }
            context.Unload();
        }

        /// <summary>
        /// 从文件流加载 DLL 避免锁住构建产物
        /// </summary>
        /// <param name="path">DLL</param>
        /// <param name="context">新 ALC</param>
        /// <returns>程序集</returns>
        private Assembly LoadAssembly(string path, out MauPocketLoadContext context)
        {
            context = new MauPocketLoadContext();
            using (FileStream stream = new FileStream(path, FileMode.Open,
                FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                return context.LoadFromStream(stream);
            }
        }

        /// <summary>
        /// 将 Roslyn 诊断转换为不含源码正文的摘要
        /// </summary>
        /// <param name="diagnostics">诊断集合</param>
        /// <returns>摘要</returns>
        private string[] FormatDiagnostics(
            System.Collections.Immutable.ImmutableArray<Diagnostic> diagnostics)
{
            string[] result = new string[diagnostics.Length];
            for (int i = 0; i < diagnostics.Length; i = i + 1)
            {
                FileLinePositionSpan span = diagnostics[i].Location.GetLineSpan();
                string filePart = span.Path.Length > 0 ? (span.Path + ":") : "";
                int genLine = span.StartLinePosition.Line + 1;
                // 行号映射反查——生成物行 → 语料行/积木源码行（D1）
                string mapped;
                string note;
                ResolveMapLine(genLine, out mapped, out note);
                result[i] = diagnostics[i].Severity.ToString() + ":"
                    + filePart + diagnostics[i].Id + ":"
                    + mapped + ":"
                    + diagnostics[i].GetMessage() + note;
            }
            return result;
        }
        /// <summary>
        /// 验证逻辑名是安全文件段
        /// </summary>
        /// <param name="logicalName">逻辑名</param>
        private void ValidateLogicalName(string logicalName)
        {
            if (string.IsNullOrWhiteSpace(logicalName)
                || logicalName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
                || logicalName.IndexOf(Path.DirectorySeparatorChar) >= 0
                || logicalName.IndexOf(Path.AltDirectorySeparatorChar) >= 0)
            {
                throw new ArgumentException("Pocket logical name is invalid.",
                    "logicalName");
            }
        }

        /// <summary>
        /// 验证 DLL 真实位于 Pocket Root
        /// </summary>
        /// <param name="assemblyPath">相对或绝对路径</param>
        /// <returns>绝对路径</returns>
        private string ResolveAssembly(string assemblyPath)
        {
            string resolved = assemblyPath;
            if (!Path.IsPathFullyQualified(resolved))
            {
                resolved = Path.Combine(MauPocketCompiler_Root, resolved);
            }
            resolved = Path.GetFullPath(resolved);
            if (!resolved.StartsWith(MauPocketCompiler_Root
                + Path.DirectorySeparatorChar.ToString(),
                StringComparison.OrdinalIgnoreCase) || !File.Exists(resolved))
            {
                throw new UnauthorizedAccessException(
                    "Pocket assembly is outside the pocket root or missing.");
            }
            return resolved;
        }

        /// <summary>
        /// 原子写入编译产物
        /// </summary>
        /// <param name="path">目标</param>
        /// <param name="bytes">字节</param>
        private void WriteAtomic(string path, byte[] bytes)
        {
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllBytes(temporary, bytes);
                File.Move(temporary, path, true);
            }
            finally
            {
                if (File.Exists(temporary))
                {
                    File.Delete(temporary);
                }
            }
        }
        /// <summary>
        /// 把可空源码规范为空字符串
        /// </summary>
        /// <param name="value">输入</param>
        /// <returns>非空文本</returns>
        private string SafeText(string? value)
        {
            if (value == null)
            {
                return "";
            }
            return value;
        }

        /// <summary>
        /// 将 Mau 程序集解析回默认 ALC 的口袋加载上下文。
        /// 生成物依赖 Mau.Runtime / Mau.Contracts / Mau.Bricks.*——全部回落默认 ALC，
        /// 保证类型身份唯一（is 检查正确、接口调用不崩）。
        /// </summary>
        private sealed class MauPocketLoadContext : AssemblyLoadContext
        {
            /// <summary>
            /// 创建 collectible 上下文
            /// </summary>
            internal MauPocketLoadContext()
                : base("Mau-Pocket-" + Guid.NewGuid().ToString("N"), true)
            {
            }

            /// <summary>
            /// Mau 程序集固定返回默认 ALC 程序集，其余依赖使用框架回落
            /// </summary>
            /// <param name="assemblyName">程序集名</param>
            /// <returns>共享程序集或 null</returns>
            protected override Assembly? Load(AssemblyName assemblyName)
            {
                string name = assemblyName.Name ?? "";
                if (!name.StartsWith("Mau.", StringComparison.Ordinal))
                {
                    return null;
                }
                Assembly? shared = AssemblyLoadContext.Default.Assemblies
                    .FirstOrDefault(a => a.GetName().Name == name);
                return shared;
            }
        }
    }
}
