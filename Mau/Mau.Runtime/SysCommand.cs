using System;
using System.Collections.Generic;
using System.Text;

namespace Mau.Runtime
{
    /// <summary>
    /// sys.* 指令解析器——审计查询的统一指令入口（v3 纯化：sys.llm 随 LLM 组件退役）。
    /// 查询分层：SysCommand = 唯一入口（指令解析）→ AuditQuery = 审计查询内核 / DataBox = 实时态 / LogStore = 类别日志。
    /// 线程契约：Execute 须宿主主线程调用（实时态段访问守卫组件）；非主线程经 FlowRunner.InvokeOnMain 投递。
    /// 入口/路由面（Execute/Usage）在本文件；查询指令实现分部在 SysCommand.Query.cs；静态工具分部在 SysCommand.Helpers.cs（P7b partial 拆分）。
    /// </summary>
    public sealed partial class SysCommand
    {
        /// <summary>
        /// 审计查询内核
        /// </summary>
        private readonly AuditQuery _audit;

        /// <summary>
        /// 统一查询通道——sys.query/sys.summary 出口（null = 未绑定）
        /// </summary>
        private readonly QueryBus? _queryBus;

        /// <summary>
        /// 构造指令解析器——绑定审计查询与统一查询通道
        /// </summary>
        /// <param name="audit">审计查询</param>
        /// <param name="queryBus">统一查询通道（null = 不启用）</param>
        public SysCommand(AuditQuery audit, QueryBus? queryBus = null)
        {
            if (audit == null)
            {
                throw new ArgumentNullException("audit");
            }
            _audit = audit;
            _queryBus = queryBus;
            if (_queryBus != null)
            {
                _queryBus.Register("summary", QueryDomain.Main, BuildSummary);
            }
        }

        /// <summary>
        /// 执行指令——首词为指令名，其余 key=value 参数（裸词 = 布尔开关 true）
        /// </summary>
        /// <param name="line">指令行（如 "sys.audit category=cmd.set tail=5"）</param>
        /// <returns>MD 键值输出；未知指令返回用法提示</returns>
        public string Execute(string line)
        {
            if (line == null || line.Trim().Length == 0)
            {
                return Usage();
            }
            string[] words = line.Split(' ');
            string cmd = words[0];
            Dictionary<string, string> args = new Dictionary<string, string>(StringComparer.Ordinal);
            for (int i = 1; i < words.Length; i = i + 1)
            {
                if (words[i].Length == 0)
                {
                    continue;
                }
                int eq = words[i].IndexOf('=');
                if (eq > 0)
                {
                    args[words[i].Substring(0, eq)] = words[i].Substring(eq + 1);
                }
                else
                {
                    args[words[i]] = "true";
                }
            }
            if (cmd == "sys.audit")
            {
                return SysAudit(args);
            }
            if (cmd == "sys.cmd")
            {
                return SysCmd(args);
            }
            if (cmd == "sys.keys")
            {
                return SysKeys(args);
            }
            if (cmd == "sys.oa")
            {
                return SysOa(args);
            }
            if (cmd == "sys.trace")
            {
                return SysTrace(args);
            }
            if (cmd == "sys.logs")
            {
                return SysLogs(args);
            }
            if (cmd == "sys.cmdlog")
            {
                return SysCmdLog(args);
            }
            if (cmd == "sys.olog")
            {
                return SysOLog(args);
            }
            if (cmd == "sys.box")
            {
                return SysBox(args);
            }
            if (cmd == "sys.conf")
            {
                return SysConf(args);
            }
            if (cmd == "sys.flow")
            {
                return SysFlow(args);
            }
            if (cmd == "sys.query")
            {
                return SysQuery(args);
            }
            if (cmd == "sys.summary")
            {
                return SysSummary(args);
            }
            return "未知指令: " + cmd + "\n" + Usage();
        }

        /// <summary>
        /// 用法提示
        /// </summary>
        /// <returns>指令清单文本</returns>
        public static string Usage()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("## SYS USAGE");
            sb.AppendLine("- sys.audit [category=] [source=] [tail=N] [from=] [to=]");
            sb.AppendLine("- sys.cmd key=<CommandKey> [tail=N]");
            sb.AppendLine("- sys.keys [owner=<flowId>]");
            sb.AppendLine("- sys.oa id=<单ID> | list=true [status=]");
            sb.AppendLine("- sys.trace flow=<实体名> [tail=N]");
            sb.AppendLine("- sys.logs [tail=N]");
            sb.AppendLine("- sys.cmdlog [tail=N]（C 类——Command 总线投递留痕）");
            sb.AppendLine("- sys.olog [tail=N]（O 类——OA 工单生命周期）");
            sb.AppendLine("- sys.box [scope=] [key=]");
            sb.AppendLine("- sys.conf [key=] [tail=N]");
            sb.AppendLine("- sys.flow [tail=N]");
            sb.AppendLine("- sys.query name=<处理器名> [key=value...]（统一查询通道出口）");
            sb.AppendLine("- sys.summary（快照聚合视图——帧号/实体/Command/OA/DataBox/审计）");
            sb.AppendLine();
            sb.AppendLine("## 数据分层与落盘");
            sb.AppendLine("- 实时态: DataBox 当前状态（sys.keys/oa/flow 实时段）");
            sb.AppendLine("- 历史: 内存环形缓冲（10000 条满覆盖——查询唯一数据源）");
            sb.AppendLine("- 落盘: Data/audit/{session}/{date}.md MD 留痕（不参与查询——人类/AI 阅读用）");
            return sb.ToString();
        }
    }
}