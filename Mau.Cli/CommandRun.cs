using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using Mau.Runtime;
using Mau.Translator;

namespace Mau.Cli
{
    /// <summary>
    /// mau run 命令——六步闭环：翻译→编译→加载→fire→Tick→观测
    /// 需要环境 .NET 8 SDK
    /// </summary>
    public static class CommandRun
    {
        /// <summary>
        /// Fire 指令——一次 --fire 解析结果
        /// </summary>
        private sealed class FireCommand
        {
            public string MethodName;
            public Dictionary<string, string> Args;

            public FireCommand(string methodName)
            {
                MethodName = methodName;
                Args = new Dictionary<string, string>();
            }
        }

        /// <summary>
        /// 执行 mau run
        /// </summary>
        /// <param name="args">命令行参数——不含 "run" 本身</param>
        /// <returns>退出码</returns>
        public static int Execute(string[] args)
        {
            string? mauFile = null;
            List<FireCommand> fireCommands = new List<FireCommand>();
            int ticks = 1;
            int timeout = 30;

            // 解析参数
            FireCommand? currentFire = null;
            for (int i = 0; i < args.Length; i = i + 1)
            {
                string arg = args[i];
                if (arg == "--fire")
                {
                    if (i + 1 >= args.Length)
                    {
                        Console.Error.WriteLine("错误: --fire 需要方法名");
                        return 1;
                    }
                    i = i + 1;
                    currentFire = new FireCommand(args[i]);
                    fireCommands.Add(currentFire);
                }
                else if (arg == "--ticks")
                {
                    if (i + 1 >= args.Length || !int.TryParse(args[i + 1], out ticks))
                    {
                        Console.Error.WriteLine("错误: --ticks 需要整数");
                        return 1;
                    }
                    i = i + 1;
                }
                else if (arg == "--timeout")
                {
                    if (i + 1 >= args.Length || !int.TryParse(args[i + 1], out timeout))
                    {
                        Console.Error.WriteLine("错误: --timeout 需要整数（秒）");
                        return 1;
                    }
                    i = i + 1;
                }
                else if (arg.Contains("=") && currentFire != null)
                {
                    int eq = arg.IndexOf('=');
                    string key = arg.Substring(0, eq);
                    string val = arg.Substring(eq + 1);
                    currentFire.Args[key] = val;
                }
                else if (mauFile == null)
                {
                    mauFile = arg;
                }
            }

            if (mauFile == null)
            {
                Console.Error.WriteLine("用法: mau run <file.mau> [--fire Method key=val ...] [--ticks N] [--timeout N]");
                Console.Error.WriteLine("需要环境 .NET 8 SDK");
                return 1;
            }

            if (!File.Exists(mauFile))
            {
                Console.Error.WriteLine("错误: .mau 文件不存在: " + mauFile);
                return 1;
            }

            // [1] 解析 + 验证
            string sourceText = File.ReadAllText(mauFile, Encoding.UTF8);
            ParseResult parseResult = MauParser.Parse(sourceText);
            if (parseResult.Diagnostics.Count > 0)
            {
                PrintRunError(1, "Mau 语法错误", parseResult.Diagnostics);
                return 1;
            }

            MauDocument doc = parseResult.Document;
            List<MauDiagnostic> diags = MauValidator.Validate(doc);
            if (diags.Count > 0)
            {
                PrintRunError(1, "Mau 验证失败", diags);
                return 1;
            }

            // [2] 生成 C# 源码
            string flowName = Path.GetFileNameWithoutExtension(mauFile);
            string csSource = CodeGenerator.Generate(doc, flowName);
            string className = "FL_" + flowName;

            // [3] 临时编译
            string tempDir = Path.Combine(Path.GetTempPath(), "mau_run_" + Guid.NewGuid().ToString("N").Substring(0, 8));
            TempProjectBuilder.BuildResult buildResult = TempProjectBuilder.Build(csSource, className, tempDir);
            if (!buildResult.Success)
            {
                PrintRunError(2, "C# 编译失败", null);
                if (buildResult.BuildOutput != null)
                {
                    Console.Error.WriteLine(buildResult.BuildOutput);
                }
                return 2;
            }

            // [4] 加载 DLL
            FlowHandle? handle = null;
            try
            {
                handle = FlowHandle.Load(buildResult.DllPath!);
            }
            catch (Exception ex)
            {
                PrintRunError(3, "DLL 加载失败: " + ex.Message, null);
                return 3;
            }

            // [5] Fire + Tick
            IObservableFlow flow = handle.Flow;
            List<string> runErrors = new List<string>();

            try
            {
                // 处理 --fire 指令
                for (int f = 0; f < fireCommands.Count; f = f + 1)
                {
                    FireCommand fc = fireCommands[f];
                    try
                    {
                        InvokeFire(flow, fc);
                    }
                    catch (Exception ex)
                    {
                        runErrors.Add("Fire " + fc.MethodName + " 失败: " + ex.Message);
                    }
                }

                // Tick 循环
                for (int t = 0; t < ticks; t = t + 1)
                {
                    flow.Tick();
                }
            }
            catch (Exception ex)
            {
                runErrors.Add("运行时异常: " + ex.ToString());
            }

            // [6] 输出结果
            RuntimeStatus status = flow.GetStatus();
            MauDebug[] logs = flow.GetLogs();

            PrintRunResult(0, ticks, status, logs, runErrors);

            // 清理
            handle.TryUnload(3);
            try
            {
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, true);
                }
            }
            catch
            {
                // 临时目录清理失败不影响退出码
            }

            return runErrors.Count > 0 ? 3 : 0;
        }

        /// <summary>
        /// 反射调用 FireXxx 方法
        /// </summary>
        /// <param name="flow">生成物实例</param>
        /// <param name="fc">fire 指令</param>
        private static void InvokeFire(IObservableFlow flow, FireCommand fc)
        {
            Type type = flow.GetType();
            System.Reflection.MethodInfo? method = type.GetMethod(fc.MethodName);
            if (method == null)
            {
                throw new InvalidOperationException("未找到方法: " + fc.MethodName + "（类: " + type.Name + "）");
            }

            System.Reflection.ParameterInfo[] paramInfos = method.GetParameters();
            object?[] paramValues = new object?[paramInfos.Length];

            for (int i = 0; i < paramInfos.Length; i = i + 1)
            {
                string pname = paramInfos[i].Name!;
                if (fc.Args.TryGetValue(pname, out string? val))
                {
                    paramValues[i] = ConvertArg(val, paramInfos[i].ParameterType);
                }
                else
                {
                    // 参数未提供——使用默认值
                    if (paramInfos[i].ParameterType.IsValueType)
                    {
                        paramValues[i] = Activator.CreateInstance(paramInfos[i].ParameterType);
                    }
                    else
                    {
                        paramValues[i] = null;
                    }
                }
            }

            method.Invoke(flow, paramValues);
        }

        /// <summary>
        /// 字符串 → 目标类型转换
        /// </summary>
        /// <param name="val">字符串值</param>
        /// <param name="targetType">目标类型</param>
        /// <returns>转换后的值</returns>
        private static object? ConvertArg(string val, Type targetType)
        {
            if (targetType == typeof(string))
            {
                return val;
            }
            if (targetType == typeof(int))
            {
                return int.Parse(val);
            }
            if (targetType == typeof(long))
            {
                return long.Parse(val);
            }
            if (targetType == typeof(bool))
            {
                return bool.Parse(val);
            }
            if (targetType == typeof(double))
            {
                return double.Parse(val);
            }
            if (targetType == typeof(float))
            {
                return float.Parse(val);
            }
            return val;
        }

        /// <summary>
        /// 输出 JSON 结果到 stdout
        /// </summary>
        /// <param name="exitCode">退出码</param>
        /// <param name="ticks">实际 Tick 次数</param>
        /// <param name="status">运行时状态快照</param>
        /// <param name="logs">调试日志</param>
        /// <param name="errors">运行时错误列表</param>
        private static void PrintRunResult(int exitCode, int ticks, RuntimeStatus status, MauDebug[] logs, List<string> errors)
        {
            StringBuilder json = new StringBuilder();
            json.AppendLine("{");
            json.AppendLine("  \"exitCode\": " + exitCode.ToString() + ",");
            json.AppendLine("  \"ticks\": " + ticks.ToString() + ",");

            // status
            json.AppendLine("  \"status\": {");
            json.AppendLine("    \"frame\": " + status.Frame.ToString() + ",");
            json.Append("    \"propositions\": [");
            for (int i = 0; i < status.Propositions.Length; i = i + 1)
            {
                if (i > 0)
                {
                    json.Append(", ");
                }
                PropSnapshot p = status.Propositions[i];
                json.Append("{\"name\":\"" + JsonEscape(p.Name) + "\",\"kind\":\"" + p.Kind + "\",\"value\":" + (p.Value ? "true" : "false") + "}");
            }
            json.AppendLine("],");
            json.Append("    \"transitions\": [");
            for (int i = 0; i < status.Transitions.Length; i = i + 1)
            {
                if (i > 0)
                {
                    json.Append(", ");
                }
                TransSnapshot t = status.Transitions[i];
                json.Append("{\"name\":\"" + JsonEscape(t.Name) + "\",\"cubeState\":\"" + t.CubeState + "\",\"elapsedFrames\":" + t.ElapsedFrames.ToString() + ",\"limitFrames\":" + t.LimitFrames.ToString() + "}");
            }
            json.AppendLine("],");
            json.AppendLine("    \"resources\": []");
            json.AppendLine("  },");

            // logs
            json.AppendLine("  \"logs\": [");
            for (int i = 0; i < logs.Length; i = i + 1)
            {
                if (i > 0)
                {
                    json.AppendLine(",");
                }
                MauDebug d = logs[i];
                json.Append("    {\"frame\":" + d.Frame.ToString() + ",\"transition\":\"" + JsonEscape(d.TransitionName) + "\",\"phase\":\"" + d.Phase + "\",\"message\":\"" + JsonEscape(d.Message) + "\"}");
            }
            if (logs.Length > 0)
            {
                json.AppendLine();
            }
            json.AppendLine("  ],");

            // errors
            json.Append("  \"errors\": [");
            for (int i = 0; i < errors.Count; i = i + 1)
            {
                if (i > 0)
                {
                    json.Append(", ");
                }
                json.Append("\"" + JsonEscape(errors[i]) + "\"");
            }
            json.AppendLine("]");
            json.AppendLine("}");

            Console.WriteLine(json.ToString());
        }

        /// <summary>
        /// 输出错误结果（非运行时错误）
        /// </summary>
        /// <param name="exitCode">退出码</param>
        /// <param name="message">错误消息</param>
        /// <param name="diags">诊断列表——可选</param>
        private static void PrintRunError(int exitCode, string message, List<MauDiagnostic>? diags)
        {
            StringBuilder json = new StringBuilder();
            json.AppendLine("{");
            json.AppendLine("  \"exitCode\": " + exitCode.ToString() + ",");
            json.AppendLine("  \"ticks\": 0,");
            json.AppendLine("  \"status\": null,");
            json.AppendLine("  \"logs\": [],");
            json.Append("  \"errors\": [\"" + JsonEscape(message) + "\"");
            if (diags != null)
            {
                for (int i = 0; i < diags.Count; i = i + 1)
                {
                    json.Append(", \"" + JsonEscape(diags[i].ToString()) + "\"");
                }
            }
            json.AppendLine("]");
            json.AppendLine("}");
            Console.WriteLine(json.ToString());
        }

        /// <summary>
        /// JSON 字符串转义
        /// </summary>
        /// <param name="s">原始字符串，可为 null</param>
        /// <returns>转义后字符串</returns>
        private static string JsonEscape(string? s)
        {
            if (s == null)
            {
                return "";
            }
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t");
        }
    }
}
