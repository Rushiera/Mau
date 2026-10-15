using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;

namespace Mau.Runtime
{
    /// <summary>
    /// Python 执行服务——py 工具组的执行面与 py 服务进程管理（design-ch4-pytools §二 / §五）。
    /// <para>
    /// 两类入口：<see cref="Run"/>（短命脚本，起-跑-退）· <see cref="Service"/> / <see cref="Invoke"/>
    /// （常驻服务，stdio 严格协议——一行 JSON 进 / 一行 JSON 出，stdout 只出协议行）。
    /// </para>
    /// <para>
    /// 解释器路径读配置 <c>py.python_path</c>（每服务可用 <c>py.service.&lt;name&gt;.python</c> 覆盖——
    /// 系统 Python 与 venv 的依赖面不同，全局单解释器跑不起来）；未配置即明示不可用，不回落探测路径。
    /// </para>
    /// <para>
    /// 进程句柄归本服务持有（不在 Flow 层——Flow 可热重载，句柄存 Flow 里一次 reload 即悬空成孤儿）。
    /// 宿主退出时统一清理（<see cref="Dispose"/> + ProcessExit 钩子），不留孤儿。
    /// </para>
    /// </summary>
    public sealed partial class PyService : IPyService, IDisposable
    {
        // [段1] 字段
        /// <summary>
        /// 可写受控根——脚本路径必须落在其一之内
        /// </summary>
        private readonly string[] _roots;

        /// <summary>
        /// 默认脚本扫描面——mau 根下的 tools 目录（List 未给 dir 时的默认面；无 mau 根即空串 → 明示不可用）
        /// </summary>
        private readonly string _defaultScanDir;

        /// <summary>
        /// 根 Id → 规范路径（可写根）——支撑 <c>&lt;根id&gt;:&lt;相对&gt;</c> 寻址（与 text-* / file-* 同源）
        /// </summary>
        private readonly Dictionary<string, string> _rootIndex = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// 服务池（服务名 → 持有进程）
        /// </summary>
        private readonly Dictionary<string, PyServer> _servers = new Dictionary<string, PyServer>(StringComparer.Ordinal);

        /// <summary>
        /// 服务池锁
        /// </summary>
        private readonly object _gate = new object();

        /// <summary>
        /// 单路输出截断上限（字符）
        /// </summary>
        private const int MaxOutputChars = 60000;

        /// <summary>
        /// 默认超时毫秒（与工具执行层统一口径）
        /// </summary>
        private const int DefaultTimeoutMs = 120000;

        /// <summary>
        /// 连续拉起失败上限——达上限置「不可用」态（防崩溃风暴；需重启恢复）
        /// </summary>
        private const int MaxLaunchFailures = 5;

        /// <summary>
        /// 释放标志
        /// </summary>
        private bool _disposed;

        // [段2] 构造
        /// <summary>
        /// 建立服务——收可写受控根（脚本路径校验面），注册宿主退出清理钩子。
        /// </summary>
        /// <param name="roots">受控根表（只收 writable=true 的根）</param>
        public PyService(WorkspaceConfig.RootEntry[] roots)
        {
            if (roots == null || roots.Length == 0)
            {
                throw new ArgumentException("At least one workspace root is required.", "roots");
            }
            List<string> writable = new List<string>();
            for (int i = 0; i < roots.Length; i = i + 1)
            {
                if (!roots[i].Writable || roots[i].Path.Length == 0)
                {
                    continue;
                }
                string normalized = Path.GetFullPath(roots[i].Path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                writable.Add(normalized);
                if (roots[i].Id != null && roots[i].Id.Length > 0)
                {
                    _rootIndex[roots[i].Id] = normalized;
                }
            }
            if (writable.Count == 0)
            {
                throw new ArgumentException("At least one writable root is required.", "roots");
            }
            _roots = writable.ToArray();
            _defaultScanDir = ResolveDefaultScanDir(roots);
            AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
        }

        /// <summary>
        /// 解析默认脚本扫描面——mau 根下的 tools 目录（py 工具线的约定面）。
        /// </summary>
        /// <param name="roots">受控根原始表</param>
        /// <returns>默认扫描面绝对路径；无 mau 根时为空串</returns>
        private static string ResolveDefaultScanDir(WorkspaceConfig.RootEntry[] roots)
        {
            for (int i = 0; i < roots.Length; i = i + 1)
            {
                if (!roots[i].Writable || roots[i].Path.Length == 0)
                {
                    continue;
                }
                if (!string.Equals(roots[i].Id, "mau", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                string root = Path.GetFullPath(roots[i].Path);
                return Path.Combine(root, "tools");
            }
            return "";
        }

        // [段5] 服务自启（宿主启动链调用——接线后做）
        /// <summary>
        /// 按配置拉起 follow=true 的服务——服务清单自配置扫描得出
        /// （键 py.service.&lt;name&gt;.script 存在即视为已声明服务）。
        /// </summary>
        /// <returns>结果 JSON（started / skipped / errors 数组）或 ERR| 前缀错误文本</returns>
        public string Autostart()
        {
            ConfigStore? cfg;
            DataBox.TryResolve<ConfigStore>(out cfg);
            if (cfg == null)
            {
                return "ERR|PY_NOT_CONFIGURED|配置存储未就绪——无法读取 py 服务声明";
            }

            const string prefix = "py.service.";
            const string suffix = ".script";
            KeyValuePair<string, string>[] all = cfg.All();
            List<string> names = new List<string>();
            foreach (KeyValuePair<string, string> item in all)
            {
                if (!item.Key.StartsWith(prefix, StringComparison.Ordinal) || !item.Key.EndsWith(suffix, StringComparison.Ordinal))
                {
                    continue;
                }
                string name = item.Key.Substring(prefix.Length, item.Key.Length - prefix.Length - suffix.Length);
                if (name.Length > 0 && !names.Contains(name))
                {
                    names.Add(name);
                }
            }

            List<string> started = new List<string>();
            List<string> skipped = new List<string>();
            List<string> errors = new List<string>();
            for (int i = 0; i < names.Count; i = i + 1)
            {
                string name = names[i];
                string follow = cfg.Get("py.service." + name + ".follow", "false");
                if (!string.Equals(follow, "true", StringComparison.OrdinalIgnoreCase))
                {
                    skipped.Add(name);
                    continue;
                }
                string result = Service(JsonUtil.Object(("name", name), ("action", "start")));
                if (result.StartsWith("ERR|", StringComparison.Ordinal))
                {
                    errors.Add(name);
                    LogStore.Add("PyService", 2, "服务自启失败（" + name + "）: " + result, "SYS");
                }
                else
                {
                    started.Add(name);
                }
            }

            Dictionary<string, object> payload = new Dictionary<string, object>();
            payload["started"] = started;
            payload["skipped"] = skipped;
            payload["errors"] = errors;
            return JsonUtil.Serialize(payload);
        }

        // [段6] 服务控制
        /// <summary>
        /// 服务控制——start（拉起并等 ready）/ stop（优雅退出 + 强杀兜底）/ status（查进程态）。
        /// </summary>
        /// <param name="argsJson">参数 JSON（name 必填；action 必填）</param>
        /// <returns>结果 JSON 或 ERR| 前缀错误文本</returns>
        public string Service(string argsJson)
        {
            string name = JsonArgs.Get(argsJson, "name");
            string action = JsonArgs.Get(argsJson, "action");
            if (name.Length == 0 || name == "§PARSE_FAIL§")
            {
                return "ERR|PY_BAD_ARGS|缺少参数 name";
            }
            if (action.Length == 0 || action == "§PARSE_FAIL§")
            {
                return "ERR|PY_BAD_ARGS|缺少参数 action（start|stop|status）";
            }

            lock (_gate)
            {
                PyServer? existing;
                _servers.TryGetValue(name, out existing);

                if (string.Equals(action, "status", StringComparison.Ordinal))
                {
                    return DescribeStatus(name, existing);
                }

                if (string.Equals(action, "stop", StringComparison.Ordinal))
                {
                    if (existing == null)
                    {
                        return ServiceJson(name, "stopped", "服务未启动", false);
                    }
                    string stopError = existing.Stop();
                    _servers.Remove(name);
                    if (stopError.Length > 0)
                    {
                        return stopError;
                    }
                    return ServiceJson(name, "stopped", "", false);
                }

                if (!string.Equals(action, "start", StringComparison.Ordinal))
                {
                    return "ERR|PY_BAD_ARGS|action 非法值: " + action + "（start|stop|status）";
                }

                // [段1] start——已存活即幂等返回
                if (existing != null && existing.IsAlive())
                {
                    return ServiceJson(name, "running", "服务已在运行", true);
                }

                string scriptError;
                string script = ResolveServiceScript(name, out scriptError);
                if (script.Length == 0)
                {
                    return scriptError;
                }
                string pythonError;
                string python = ResolvePython(name, out pythonError);
                if (python.Length == 0)
                {
                    return pythonError;
                }
                int timeoutMs = ParseServiceTimeout(name);

                PyServer server = new PyServer(name, python, script, Path.GetDirectoryName(script) ?? "", timeoutMs);
                _servers[name] = server;
                string startError = server.Start();
                if (startError.Length > 0)
                {
                    _servers.Remove(name);
                    return startError;
                }
                return ServiceJson(name, "running", "", true);
            }
        }

        // [段6] 服务调用
        /// <summary>
        /// 服务调用——按 stdio 严格协议发一行 JSON、读一行 JSON（单服务纯串行）。
        /// 未启动则按需拉起（follow 语义的按需分支）；进程崩溃则重拉并在回执标注。
        /// </summary>
        /// <param name="argsJson">参数 JSON（name 必填；payload 必填——请求体 JSON 文本）</param>
        /// <returns>服务回执 JSON 或 ERR| 前缀错误文本</returns>
        public string Invoke(string argsJson)
        {
            string name = JsonArgs.Get(argsJson, "name");
            string payload = JsonArgs.Get(argsJson, "payload");
            if (name.Length == 0 || name == "§PARSE_FAIL§")
            {
                return "ERR|PY_BAD_ARGS|缺少参数 name";
            }
            if (payload.Length == 0 || payload == "§PARSE_FAIL§")
            {
                return "ERR|PY_BAD_ARGS|缺少参数 payload（请求体 JSON 文本）";
            }

            lock (_gate)
            {
                PyServer? server;
                _servers.TryGetValue(name, out server);
                if (server == null)
                {
                    string scriptError;
                    string script = ResolveServiceScript(name, out scriptError);
                    if (script.Length == 0)
                    {
                        return scriptError;
                    }
                    string pythonError;
                    string python = ResolvePython(name, out pythonError);
                    if (python.Length == 0)
                    {
                        return pythonError;
                    }
                    server = new PyServer(name, python, script, Path.GetDirectoryName(script) ?? "", ParseServiceTimeout(name));
                    _servers[name] = server;
                }
                return server.Invoke(payload);
            }
        }

        // [段7] 释放
        /// <summary>
        /// 停止全部服务进程——宿主退出时统一清理，不留孤儿。
        /// </summary>
        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;
            lock (_gate)
            {
                foreach (KeyValuePair<string, PyServer> pair in _servers)
                {
                    try
                    {
                        pair.Value.Stop();
                    }
                    catch (Exception ex)
                    {
                        LogStore.Add("PyService", 2, "服务停止异常（" + pair.Key + "）: " + ex.Message, "SYS");
                    }
                }
                _servers.Clear();
            }
        }

        /// <summary>
        /// 宿主退出回调——统一清理服务进程。
        /// </summary>
        /// <param name="sender">事件源</param>
        /// <param name="args">事件参数</param>
        private void OnProcessExit(object? sender, EventArgs args)
        {
            try
            {
                Dispose();
            }
            catch (Exception ex)
            {
                LogStore.Add("PyService", 3, "退出清理异常: " + ex.Message, "SYS");
            }
        }

        // [段8] 配置解析
        /// <summary>
        /// 解析解释器路径——服务级覆盖优先，回退全局；未配置 / 路径不存在即明示 ERR。
        /// </summary>
        /// <param name="serviceName">服务名（空 = 全局）</param>
        /// <param name="error">错误文本（空=成功）</param>
        /// <returns>python.exe 全路径（失败返回空串）</returns>
        private static string ResolvePython(string serviceName, out string error)
        {
            error = "";
            ConfigStore? cfg;
            DataBox.TryResolve<ConfigStore>(out cfg);
            string path = "";
            if (cfg != null && serviceName.Length > 0)
            {
                path = cfg.Get("py.service." + serviceName + ".python", "");
            }
            if (path.Length == 0 && cfg != null)
            {
                path = cfg.Get("py.python_path", "");
            }
            if (path.Length == 0)
            {
                error = "ERR|PY_NOT_CONFIGURED|Python 解释器未配置——请在配置面填写 py.python_path（python.exe 全路径；获取方式 py -0p）";
                return "";
            }
            if (!File.Exists(path))
            {
                error = "ERR|PY_PYTHON_NOT_FOUND|配置的解释器路径不存在：" + path + "——请更新 py.python_path";
                return "";
            }
            return path;
        }

        /// <summary>
        /// 解析服务入口脚本——读配置 py.service.&lt;name&gt;.script（受控根内校验）。
        /// </summary>
        /// <param name="serviceName">服务名</param>
        /// <param name="error">错误文本（空=成功）</param>
        /// <returns>脚本全路径（失败返回空串）</returns>
        private static string ResolveServiceScript(string serviceName, out string error)
        {
            error = "";
            ConfigStore? cfg;
            DataBox.TryResolve<ConfigStore>(out cfg);
            string script = cfg == null ? "" : cfg.Get("py.service." + serviceName + ".script", "");
            if (script.Length == 0)
            {
                error = "ERR|PY_SERVICE_NOT_CONFIGURED|服务未配置：" + serviceName + "——请在配置面填写 py.service." + serviceName + ".script";
                return "";
            }
            return script;
        }

        /// <summary>
        /// 解析服务单次调用超时——py.service.&lt;name&gt;.timeout_ms。
        /// </summary>
        /// <param name="serviceName">服务名</param>
        /// <returns>超时毫秒</returns>
        private static int ParseServiceTimeout(string serviceName)
        {
            ConfigStore? cfg;
            DataBox.TryResolve<ConfigStore>(out cfg);
            string raw = cfg == null ? "" : cfg.Get("py.service." + serviceName + ".timeout_ms", "");
            return ParseTimeout(raw);
        }

        /// <summary>
        /// 超时解析——非法 / 空值取默认；上限与工具执行层同口径。
        /// </summary>
        /// <param name="raw">原始文本（毫秒）</param>
        /// <returns>超时毫秒</returns>
        private static int ParseTimeout(string raw)
        {
            int value;
            if (raw.Length > 0 && int.TryParse(raw, out value) && value > 0)
            {
                return value > DefaultTimeoutMs ? DefaultTimeoutMs : value;
            }
            return DefaultTimeoutMs;
        }

        // [段10] 通用工具
        /// <summary>
        /// 杀进程树——不留僵尸。
        /// </summary>
        /// <param name="proc">目标进程</param>
        private static void KillTree(Process proc)
        {
            try
            {
                if (!proc.HasExited)
                {
                    proc.Kill(true);
                }
            }
            catch (Exception ex)
            {
                LogStore.Add("PyService", 2, "进程树终止失败: " + ex.Message, "SYS");
            }
        }


        /// <summary>
        /// 追加并截断——防止超大输出拖垮内存。
        /// </summary>
        /// <param name="buffer">目标缓冲</param>
        /// <param name="line">待追加行</param>
        /// <param name="cap">上限字符数</param>
        private static void AppendCapped(StringBuilder buffer, string line, int cap)
        {
            if (buffer.Length >= cap)
            {
                return;
            }
            buffer.Append(line);
            buffer.Append('\n');
        }

        /// <summary>
        /// 行数统计——空串 0；否则 \n 计数 + 1（与前端 split 同口径）。
        /// </summary>
        /// <param name="text">源文本</param>
        /// <returns>行数</returns>
        private static int CountLines(string text)
        {
            if (text.Length == 0)
            {
                return 0;
            }
            int count = 1;
            for (int i = 0; i < text.Length; i = i + 1)
            {
                if (text[i] == '\n')
                {
                    count = count + 1;
                }
            }
            return count;
        }

        /// <summary>
        /// 组装服务态回执。
        /// </summary>
        /// <param name="name">服务名</param>
        /// <param name="state">态（running / stopped / unavailable）</param>
        /// <param name="note">备注</param>
        /// <param name="alive">进程是否存活</param>
        /// <returns>回执 JSON</returns>
        private static string ServiceJson(string name, string state, string note, bool alive)
        {
            Dictionary<string, object> payload = new Dictionary<string, object>();
            payload["name"] = name;
            payload["state"] = state;
            payload["alive"] = alive;
            payload["note"] = note;
            return JsonUtil.Serialize(payload);
        }

        /// <summary>
        /// 组装服务态描述——未启动 / 存活 / 已退出（含连续失败计数）。
        /// </summary>
        /// <param name="name">服务名</param>
        /// <param name="server">服务持有对象（可空）</param>
        /// <returns>回执 JSON</returns>
        private static string DescribeStatus(string name, PyServer? server)
        {
            if (server == null)
            {
                return ServiceJson(name, "stopped", "服务未启动", false);
            }
            if (server.IsAlive())
            {
                return ServiceJson(name, "running", "", true);
            }
            return ServiceJson(name, "stopped", "进程已退出（连续失败 " + server.FailCount() + " 次）", false);
        }

        // [段11] 服务进程持有
        /// <summary>
        /// 常驻服务进程——持句柄 + stdio 严格协议往返（单服务纯串行）。
        /// stdout 只收协议行；stderr 归日志（落 LogStore，不进协议面）。
        /// 超时报错<b>不杀进程</b>（服务端仍在跑，残留回执清空后下次调用重来）。
        /// </summary>
        private sealed class PyServer
        {
            /// <summary>
            /// 服务名
            /// </summary>
            private readonly string _name;

            /// <summary>
            /// 解释器全路径
            /// </summary>
            private readonly string _exe;

            /// <summary>
            /// 入口脚本全路径
            /// </summary>
            private readonly string _script;

            /// <summary>
            /// 工作目录
            /// </summary>
            private readonly string _workDir;

            /// <summary>
            /// 单次调用超时毫秒
            /// </summary>
            private readonly int _timeoutMs;

            /// <summary>
            /// 往返串行锁
            /// </summary>
            private readonly object _gate = new object();

            /// <summary>
            /// 协议行队列（读线程喂入）
            /// </summary>
            private readonly BlockingCollection<string> _lines = new BlockingCollection<string>();

            /// <summary>
            /// 进程句柄
            /// </summary>
            private Process? _process;

            /// <summary>
            /// 连续拉起失败计数
            /// </summary>
            private int _failCount;

            /// <summary>
            /// 建立服务持有对象。
            /// </summary>
            /// <param name="name">服务名</param>
            /// <param name="exe">解释器全路径</param>
            /// <param name="script">入口脚本全路径</param>
            /// <param name="workDir">工作目录</param>
            /// <param name="timeoutMs">单次调用超时毫秒</param>
            public PyServer(string name, string exe, string script, string workDir, int timeoutMs)
            {
                _name = name;
                _exe = exe;
                _script = script;
                _workDir = workDir;
                _timeoutMs = timeoutMs;
            }

            /// <summary>
            /// 进程是否存活。
            /// </summary>
            /// <returns>存活为 true</returns>
            public bool IsAlive()
            {
                Process? proc = _process;
                if (proc == null)
                {
                    return false;
                }
                try
                {
                    return !proc.HasExited;
                }
                catch (Exception)
                {
                    return false;
                }
            }

            /// <summary>
            /// 连续拉起失败计数。
            /// </summary>
            /// <returns>计数</returns>
            public int FailCount()
            {
                return _failCount;
            }

            /// <summary>
            /// 拉起进程并等 ready 握手。
            /// </summary>
            /// <returns>错误文本（空=成功）</returns>
            public string Start()
            {
                lock (_gate)
                {
                    return StartLocked();
                }
            }

            /// <summary>
            /// 优雅停止——发 quit，超时强杀兜底。
            /// </summary>
            /// <returns>错误文本（空=成功）</returns>
            public string Stop()
            {
                lock (_gate)
                {
                    Process? proc = _process;
                    if (proc == null)
                    {
                        return "";
                    }
                    try
                    {
                        if (!proc.HasExited && proc.StandardInput != null)
                        {
                            proc.StandardInput.WriteLine("{\"id\":\"_quit\",\"op\":\"quit\"}");
                            proc.StandardInput.Flush();
                        }
                        if (!proc.WaitForExit(5000))
                        {
                            KillTree(proc);
                        }
                    }
                    catch (Exception ex)
                    {
                        LogStore.Add("PyService", 2, "服务停止异常（" + _name + "）: " + ex.Message, "SYS");
                        KillTree(proc);
                    }
                    _process = null;
                    return "";
                }
            }

            /// <summary>
            /// 协议往返——发一行 JSON、读一行 JSON；进程不在则重拉。
            /// </summary>
            /// <param name="payload">请求体 JSON 文本</param>
            /// <returns>回执 JSON 或 ERR| 前缀错误文本</returns>
            public string Invoke(string payload)
            {
                lock (_gate)
                {
                    bool restarted = false;
                    if (!IsAlive())
                    {
                        string startError = StartLocked();
                        if (startError.Length > 0)
                        {
                            return startError;
                        }
                        restarted = true;
                    }

                    // [段1] 清残留——超时中断的上一次回执（串行语义下只可能属于上一次）
                    string? residue;
                    while (_lines.TryTake(out residue, 0))
                    {
                    }

                    Process? proc = _process;
                    if (proc == null || proc.StandardInput == null)
                    {
                        return "ERR|PY_NO_PROCESS|服务进程不可用：" + _name;
                    }

                    try
                    {
                        proc.StandardInput.WriteLine(payload);
                        proc.StandardInput.Flush();
                    }
                    catch (Exception ex)
                    {
                        _failCount = _failCount + 1;
                        return "ERR|PY_WRITE|写入服务失败（" + _name + "）: " + ex.Message;
                    }

                    string? line;
                    if (!_lines.TryTake(out line, _timeoutMs))
                    {
                        // 超时不杀——清残留（本次回执迟到即弃），下次调用重来
                        string? late;
                        while (_lines.TryTake(out late, 0))
                        {
                        }
                        _failCount = _failCount + 1;
                        return "ERR|PY_TIMEOUT|服务调用超时（" + _name + "，" + _timeoutMs.ToString() + " ms；进程未杀）";
                    }
                    _failCount = 0;
                    if (restarted)
                    {
                        return "{\"restarted\":true,\"reply\":" + JsonUtil.Str(line) + "}";
                    }
                    return line;
                }
            }

            /// <summary>
            /// 拉起实现——调用方持锁。
            /// </summary>
            /// <returns>错误文本（空=成功）</returns>
            private string StartLocked()
            {
                if (_failCount >= MaxLaunchFailures)
                {
                    return "ERR|PY_UNAVAILABLE|服务连续拉起失败达上限（" + _name + "）——需重启恢复";
                }

                ProcessStartInfo psi = new ProcessStartInfo();
                psi.FileName = _exe;
                psi.Arguments = "-u \"" + _script + "\"";
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.RedirectStandardInput = true;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                psi.StandardOutputEncoding = Encoding.UTF8;
                psi.StandardErrorEncoding = Encoding.UTF8;
                psi.StandardInputEncoding = Encoding.UTF8;
                if (_workDir.Length > 0 && Directory.Exists(_workDir))
                {
                    psi.WorkingDirectory = _workDir;
                }

                Process proc;
                try
                {
                    proc = new Process();
                    proc.StartInfo = psi;
                    proc.Start();
                }
                catch (Exception ex)
                {
                    _failCount = _failCount + 1;
                    return "ERR|PY_LAUNCH|" + ex.GetType().Name + "|" + ex.Message;
                }

                Thread reader = new Thread(delegate ()
                {
                    try
                    {
                        string? line;
                        while ((line = proc.StandardOutput.ReadLine()) != null)
                        {
                            _lines.Add(line);
                        }
                    }
                    catch (Exception ex)
                    {
                        // 进程退出或流关闭——读线程自然结束（不打断调用方）
                        LogStore.Add("PyService", 2, "协议读线程结束（" + _name + "）: " + ex.Message, "SYS");
                    }
                    _lines.Add("§EOF§");
                });
                reader.IsBackground = true;
                reader.Start();

                Thread logReader = new Thread(delegate ()
                {
                    try
                    {
                        string? line;
                        while ((line = proc.StandardError.ReadLine()) != null)
                        {
                            LogStore.Add("PyService", 1, "[" + _name + "] " + line, "SYS");
                        }
                    }
                    catch (Exception ex)
                    {
                        // 日志面异常——记一笔，不阻断协议面
                        LogStore.Add("PyService", 1, "日志读线程结束（" + _name + "）: " + ex.Message, "SYS");
                    }
                });
                logReader.IsBackground = true;
                logReader.Start();

                _process = proc;

                // [段1] ready 握手——首行必须为 ready 事件（严格协议，见 design-ch4-pytools §5.1）
                string? first;
                if (!_lines.TryTake(out first, _timeoutMs) || first == null)
                {
                    _failCount = _failCount + 1;
                    KillTree(proc);
                    _process = null;
                    return "ERR|PY_READY_TIMEOUT|服务启动未返回 ready（" + _name + "，" + _timeoutMs.ToString() + " ms）";
                }
                if (first == "§EOF§")
                {
                    _failCount = _failCount + 1;
                    _process = null;
                    return "ERR|PY_START_FAILED|服务进程启动即退出（" + _name + "）——请检查脚本与依赖";
                }
                _failCount = 0;
                return "";
            }
        }
    }
}
