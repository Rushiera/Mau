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
    public sealed class MauPocketCompiler
    {
        /// <summary>
        /// 口袋输出根
        /// </summary>
        private readonly string MauPocketCompiler_Root;
/// <summary>
/// 是否保留源码中间产物——true 时编译把 .cs 写入编译根（构建现场留档）
/// </summary>
private bool _keepSources; 

/// <summary>
/// 行号映射文本——当前编译的诊断反查表（D1：生成物行 → 语料行/积木源码行）
/// </summary>
private string? _mapText;
/// <summary>
/// 设置是否保留源码中间产物——构建命令在构造后设置
/// </summary>
/// <param name = "keep">true=编译时把源码写入编译根</param>
 public  void  SetKeepSources ( bool  keep ) { _keepSources  =  keep ;  }

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
                    allowUnsafe: false));
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
                    KeepArtifacts(source, logicalName, buildRoot);
                    WriteProjectFile(buildRoot, logicalName, new string[] { logicalName + ".cs" });
                }
            }
            return result;
        }
        /// <summary>
        /// 多源码一次编译——组模式（多个生成物 → 一个 DLL）
        /// </summary>
        /// <param name="sources">完整 C# 源码数组（与 classNames 一一对应）</param>
        /// <param name="classNames">逻辑类名数组（仅用于校验，实际类名在源码内）</param>
        /// <param name="assemblyName">输出程序集名（安全逻辑名）</param>
        /// <returns>编译结果</returns>
        public MauPocketCompileResult CompileMany(string[] sources,
            string[] classNames, string assemblyName)
{
            ValidateLogicalName(assemblyName);
            string buildId = DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffffffZ")
                + "-" + Guid.NewGuid().ToString("N");
            string buildRoot = Path.Combine(MauPocketCompiler_Root, buildId);
            Directory.CreateDirectory(buildRoot);
            string assemblyPath = Path.Combine(buildRoot, assemblyName + ".dll");
            List<SyntaxTree> trees = new List<SyntaxTree>();
            for (int i = 0; i < sources.Length; i = i + 1)
            {
                if (i < classNames.Length)
                {
                    ValidateLogicalName(classNames[i]);
                }
                string sourcePath = i < classNames.Length ? (classNames[i] + ".cs") : ("Part" + (i + 1).ToString() + ".cs");
                trees.Add(CSharpSyntaxTree.ParseText(SafeText(sources[i]),
                    new CSharpParseOptions(LanguageVersion.Latest), path: sourcePath));
            }
            MetadataReference[] references = BuildReferences();
            CSharpCompilation compilation = CSharpCompilation.Create(
                assemblyName + "_" + Guid.NewGuid().ToString("N"),
                trees, references,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
                    optimizationLevel: OptimizationLevel.Release,
                    allowUnsafe: false));
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
                    List<string> sourceNames = new List<string>();
                    for (int s = 0; s < sources.Length; s = s + 1)
                    {
                        string logical = s < classNames.Length ? classNames[s] : ("Part" + (s + 1).ToString());
                        KeepArtifacts(sources[s], logical, buildRoot);
                        sourceNames.Add(logical + ".cs");
                    }
                    WriteProjectFile(buildRoot, assemblyName, sourceNames.ToArray());
                }
            }
            return result;
        }
/// <summary>
/// 多源码一次编译（显式引用集 D25）——组模式 mauproj 引用: 确定性：TPA + 基座必需（Mau.Runtime/Mau.Contracts）+ 显式引用清单。
/// 禁止隐式宿主目录全量引用——生成物只引用声明过的程序集，缺引用即编译失败（确定性）。
/// </summary>
/// <param name = "sources">完整 C# 源码数组（与 classNames 一一对应）</param>
/// <param name = "classNames">逻辑类名数组（仅用于校验，实际类名在源码内）</param>
/// <param name = "assemblyName">输出程序集名（安全逻辑名）</param>
/// <param name = "extraReferences">显式引用 dll 绝对路径清单（mauproj 引用: 解析结果）</param>
/// <returns>编译结果</returns>
public MauPocketCompileResult CompileManyWithRefs(string[] sources, string[] classNames, string assemblyName, string[] extraReferences)
{
    ValidateLogicalName(assemblyName);
    string buildId = DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffffffZ") + "-" + Guid.NewGuid().ToString("N");
    string buildRoot = Path.Combine(MauPocketCompiler_Root, buildId);
    Directory.CreateDirectory(buildRoot);
    string assemblyPath = Path.Combine(buildRoot, assemblyName + ".dll");
    List<SyntaxTree> trees = new List<SyntaxTree>();
    for (int i = 0; i < sources.Length; i = i + 1)
    {
        if (i < classNames.Length)
        {
            ValidateLogicalName(classNames[i]);
        }

        string sourcePath = i < classNames.Length ? (classNames[i] + ".cs") : ("Part" + (i + 1).ToString() + ".cs");
        trees.Add(CSharpSyntaxTree.ParseText(SafeText(sources[i]), new CSharpParseOptions(LanguageVersion.Latest), path: sourcePath));
    }

    MetadataReference[] references = BuildReferences(extraReferences);
    CSharpCompilation compilation = CSharpCompilation.Create(assemblyName + "_" + Guid.NewGuid().ToString("N"), trees, references, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, optimizationLevel: OptimizationLevel.Release, allowUnsafe: false));
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
            List<string> sourceNames = new List<string>();
            for (int s = 0; s < sources.Length; s = s + 1)
            {
                string logical = s < classNames.Length ? classNames[s] : ("Part" + (s + 1).ToString());
                KeepArtifacts(sources[s], logical, buildRoot);
                sourceNames.Add(logical + ".cs");
            }

            WriteProjectFile(buildRoot, assemblyName, sourceNames.ToArray());
        }
    }

    return result;
}        /// <summary>
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
/// <param name = "baseDir">宿主目录</param>
/// <param name = "references">引用集合</param>
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
}        /// <summary>
/// 建立引用集（显式模式 D25）——TPA + 基座必需（Mau.Runtime/Mau.Contracts）+ 显式引用清单。
/// 显式引用缺失时静默跳过（解析阶段已校验存在性，此处容错兜底）。
/// </summary>
/// <param name = "extraReferences">显式引用 dll 绝对路径清单（mauproj 引用: 解析结果）</param>
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
}/// <summary>
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
/// 生成中间产物项目文件——构建目录下写 .csproj（引用宿主输出目录全部 dll）
/// 中间项目可独立打开/构建（IDE 友好）；生成物路径由构建命令决定（不在此写死）
/// </summary>
/// <param name = "buildRoot">构建目录</param>
/// <param name = "assemblyName">程序集名</param>
/// <param name = "sourceNames">源码文件数组</param>
private void WriteProjectFile(string buildRoot, string assemblyName, string[] sourceNames)
{
    if (!_keepSources || sourceNames.Length == 0)
    {
        return;
    }

    System.Text.StringBuilder sb = new System.Text.StringBuilder();
    sb.AppendLine("<Project Sdk=\"Microsoft.NET.Sdk\">");
    sb.AppendLine("  <PropertyGroup>");
    sb.AppendLine("    <TargetFramework>net8.0</TargetFramework>");
    sb.AppendLine("    <OutputType>Library</OutputType>");
    sb.AppendLine("    <Nullable>enable</Nullable>");
    sb.AppendLine("    <ImplicitUsings>disable</ImplicitUsings>");
    sb.AppendLine("    <AssemblyName>" + assemblyName + "</AssemblyName>");
    sb.AppendLine("    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>");
    sb.AppendLine("  </PropertyGroup>");
    sb.AppendLine("  <ItemGroup>");
    for (int i = 0; i < sourceNames.Length; i = i + 1)
    {
        sb.AppendLine("    <Compile Include=\"" + sourceNames[i] + "\" />");
    }

    sb.AppendLine("  </ItemGroup>");
    sb.AppendLine("  <ItemGroup>");
    // 引用宿主输出目录全部 dll——编译引用集 = 宿主目录全量（与 AddMauReferences 同语义）
    string baseDir = AppDomain.CurrentDomain.BaseDirectory;
    if (Directory.Exists(baseDir))
    {
        string[] dlls = Directory.GetFiles(baseDir, "*.dll", SearchOption.TopDirectoryOnly);
        Array.Sort(dlls, StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < dlls.Length; i = i + 1)
        {
            string fileName = Path.GetFileName(dlls[i]);
            string name = Path.GetFileNameWithoutExtension(dlls[i]);
            sb.AppendLine("    <Reference Include=\"" + name + "\">");
            sb.AppendLine("      <HintPath>" + Path.GetFullPath(dlls[i]) + "</HintPath>");
            sb.AppendLine("    </Reference>");
        }
    }

    sb.AppendLine("  </ItemGroup>");
    sb.AppendLine("</Project>");
    string projectPath = Path.Combine(buildRoot, assemblyName + ".csproj");
    WriteAtomic(projectPath, Encoding.UTF8.GetBytes(sb.ToString()));
}/// <summary>
/// 保留构建中间产物——编译成功后把源码 .cs 写入构建目录（构建现场留档）
/// </summary>
/// <param name = "source">完整 C# 源码</param>
/// <param name = "logicalName">逻辑名</param>
/// <param name = "buildRoot">构建目录</param>
private void KeepArtifacts(string source, string logicalName, string buildRoot)
{
    if (!_keepSources || string.IsNullOrWhiteSpace(source))
    {
        return;
    }

    string sourcePath = Path.Combine(buildRoot, logicalName + ".cs");
    WriteAtomic(sourcePath, Encoding.UTF8.GetBytes(source));
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

        /// <summary>
        /// 行号映射段——生成物行区间 → 语料/积木源码定位（D1）
        /// </summary>
        private sealed class MapSegment
        {
            /// <summary>
            /// 段名——变迁名或积木 ID
            /// </summary>
            internal string Name = "";

            /// <summary>
            /// 段起始生成物行（1-based）
            /// </summary>
            internal int Start;

            /// <summary>
            /// 段结束生成物行（1-based 含）——仅 brick 段有效
            /// </summary>
            internal int End;

            /// <summary>
            /// 映射源——transition: 语料行号; brick: 剥离行数
            /// </summary>
            internal int Source;

            /// <summary>
            /// 是否 brick 段——false = transition 段
            /// </summary>
            internal bool IsBrick;
        }

        /// <summary>
        /// 行号映射反查——生成物行号 → 语料行号/积木源码行号（D1 调试基建）
        /// </summary>
        /// <param name="generatedLine">生成物行号（1-based）</param>
        /// <param name="mapped">映射后行号——未命中返回原行号</param>
        /// <param name="note">映射说明——追加到诊断消息尾部（未命中为空）</param>
        private void ResolveMapLine(int generatedLine, out string mapped, out string note)
        {
            mapped = generatedLine.ToString();
            note = "";
            if (string.IsNullOrEmpty(_mapText))
            {
                return;
            }
            // [段1] 解析全部段——transition（语料行号）与 brick（剥离行数）
            List<MapSegment> segments = new List<MapSegment>();
            string[] lines = _mapText.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal))
                {
                    continue;
                }
                if (line.StartsWith("transition ", StringComparison.Ordinal))
                {
                    string[] parts = line.Split(' ');
                    if (parts.Length >= 6 && parts[2] == "generated" && parts[4] == "source")
                    {
                        MapSegment seg = new MapSegment();
                        seg.Name = parts[1].TrimEnd(':');
                        seg.Start = int.Parse(parts[3]);
                        seg.Source = int.Parse(parts[5]);
                        seg.IsBrick = false;
                        segments.Add(seg);
                    }
                }
                else if (line.StartsWith("brick ", StringComparison.Ordinal))
                {
                    int colon = line.IndexOf(' ');
                    string rest = line.Substring(colon + 1);
                    int genPos = rest.IndexOf(" generated ", StringComparison.Ordinal);
                    int stripPos = rest.IndexOf(" stripped ", StringComparison.Ordinal);
                    if (genPos > 0 && stripPos > genPos)
                    {
                        MapSegment seg = new MapSegment();
                        seg.Name = rest.Substring(0, genPos).TrimEnd(':');
                        string range = rest.Substring(genPos + 10, stripPos - genPos - 10).Trim();
                        int dash = range.IndexOf('-');
                        seg.Start = int.Parse(range.Substring(0, dash));
                        seg.End = int.Parse(range.Substring(dash + 1)) + 1;
                        seg.Source = int.Parse(rest.Substring(stripPos + 9).Trim());
                        seg.IsBrick = true;
                        segments.Add(seg);
                    }
                }
            }
            // [段2] 定位——段 i 的结束 = 段 i+1 的起始（transition 段不吞后续 brick 段）
            for (int i = 0; i < segments.Count; i++)
            {
                int segEnd = i + 1 < segments.Count ? segments[i + 1].Start : int.MaxValue;
                if (segments[i].IsBrick && segments[i].End < segEnd)
                {
                    segEnd = segments[i].End;
                }
                if (generatedLine >= segments[i].Start && generatedLine < segEnd)
                {
                    if (segments[i].IsBrick)
                    {
                        int srcLine = generatedLine - segments[i].Start + segments[i].Source;
                        mapped = srcLine.ToString();
                        note = " [生成物行" + generatedLine.ToString() + " → 积木 " + segments[i].Name + " 源码行" + srcLine.ToString() + "]";
                    }
                    else
                    {
                        mapped = segments[i].Source.ToString();
                        note = " [生成物行" + generatedLine.ToString() + " → 语料 " + segments[i].Name + " 行" + segments[i].Source.ToString() + "]";
                    }
                    return;
                }
            }
        }
    }
}
