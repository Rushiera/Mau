using System;
using System.Collections.Generic;
using Mau.Runtime;
using Mau.Development;

namespace CH4
{
    /// <summary>
    /// Program 的工具执行面分部——P8 工具组（一期：宿主直执，无 OA）。
    /// 归属：agent 循环基建（宿主侧）——工具声明表 + 执行器路由 + 文本/Mau 执行器。
    /// 二期：执行器不变，入口换 OA 工单（dev_cat.mau 认领）；三期：Roslyn cs.* 域落本面。
    /// </summary>
    public static partial class Program
    {
        /// <summary>
        /// 工具结果截断上限——回传 LLM 上下文防爆（text-read 大文件场景）
        /// </summary>
        private const int MaxToolResultChars = 20000;

        /// <summary>内置全量工具名清单——BuildToolSpecs 派生缓存（单一真相源；M2c 读/写比对基准）</summary>
        private static string[] _allToolNames;

        /// <summary>
        /// 内置全量工具名清单——懒加载从 BuildToolSpecs 派生（声明表是唯一真相源，清单不手工维护）。
        /// </summary>
        /// <returns>全量工具名数组</returns>
        private static string[] GetAllToolNames()
        {
            if (_allToolNames == null)
            {
                ToolSpec[] specs = ToolRegistry.BuildSpecs();
                _allToolNames = new string[specs.Length];
                for (int i = 0; i < specs.Length; i++)
                {
                    _allToolNames[i] = specs[i].Name;
                }
            }
            return _allToolNames;
        }

        /// <summary>
        /// 工具名比对——线性扫描内置清单（21 件量级，线性够用）。
        /// </summary>
        /// <param name="names">清单数组</param>
        /// <param name="name">目标名</param>
        /// <returns>true=在清单内</returns>
        private static bool ContainsToolName(string[] names, string name)
        {
            for (int i = 0; i < names.Length; i++)
            {
                if (string.Equals(names[i], name, StringComparison.Ordinal))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// 工具名单解析——读时比对（M2c 外部损坏防御：本地持久化可能被外部改坏）。
        /// 语义：空/缺省 → 全量保底；含 * → 全量；逐个比对内置清单过滤非法名；过滤后全空 → 全量保底。
        /// </summary>
        /// <param name="raw">cat.cfg toolNames 原始串（逗号/空白分隔）</param>
        /// <returns>合法工具名数组（保底全量）</returns>
        private static string[] ResolveToolNames(string raw)
        {
            string[] all = GetAllToolNames();
            if (raw == null || raw.Trim().Length == 0)
            {
                return all;
            }
            string[] parts = raw.Split(new char[] { ',', ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            List<string> names = new List<string>();
            for (int i = 0; i < parts.Length; i++)
            {
                string name = parts[i].Trim();
                if (name == "*")
                {
                    return all;
                }
                if (ContainsToolName(all, name))
                {
                    names.Add(name);
                }
            }
            if (names.Count == 0)
            {
                return all;
            }
            return names.ToArray();
        }

        /// <summary>
        /// 工具名单写时校验——序列化落盘前比对内置清单（M2c：非法名剔除，合法名逗号重拼）。
        /// 语义：空 → 空串（全量语义）；* 保留；非法名剔除；全非法 → 空串（全量保底）。
        /// </summary>
        /// <param name="raw">待写入工具名单原始串</param>
        /// <returns>过滤后逗号清单（空串=全量语义）</returns>
        private static string ValidateToolNames(string raw)
        {
            if (raw == null || raw.Trim().Length == 0)
            {
                return "";
            }
            string[] all = GetAllToolNames();
            string[] parts = raw.Split(new char[] { ',', ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            List<string> names = new List<string>();
            for (int i = 0; i < parts.Length; i++)
            {
                string name = parts[i].Trim();
                if (name == "*" || ContainsToolName(all, name))
                {
                    names.Add(name);
                }
            }
            return string.Join(",", names.ToArray());
        }

        /// <summary>
        /// 工具声明面裁剪——按名单从全量表取子集（M2c 每会话独立声明面；保序）。
        /// </summary>
        /// <param name="names">合法工具名数组（ResolveToolNames 产物）</param>
        /// <returns>裁剪后工具数组；名单空 → 全量</returns>
        private static ToolSpec[] FilterToolSpecs(string[] names)
        {
            ToolSpec[] all = ToolRegistry.BuildSpecs();
            if (names == null || names.Length == 0)
            {
                return all;
            }
            List<ToolSpec> list = new List<ToolSpec>();
            for (int i = 0; i < all.Length; i++)
            {
                if (ContainsToolName(names, all[i].Name))
                {
                    list.Add(all[i]);
                }
            }
            if (list.Count == 0)
            {
                return all;
            }
            return list.ToArray();
        }

        /// <summary>
        /// 构建工具声明表——P8 一期 7 件（文本 4 + Mau 自查 3；ask/read_file 退役——二期待 OA 恢复）
        /// </summary>
        /// <returns>工具数组</returns>
        private static ToolSpec[] BuildToolSpecs()
{
            ToolSpec[] specs = new ToolSpec[]
            {
                new ToolSpec("text-read", "读取 UTF-8 文本文件（受控根内；路径支持 id:相对路径——mau:corpus/...=仓库根 / ccbp:...=知识库 / runtime:...=数据根，或绝对路径），返回完整内容", "{\"type\":\"object\",\"properties\":{\"path\":{\"type\":\"string\",\"description\":\"要读取的文件路径（支持 mau:/ccbp: 前缀）\"}},\"required\":[\"path\"]}"),
                new ToolSpec("text-write", "覆写文件（含新建）——整文件替换为 content（路径支持 id: 前缀同 text-read）", "{\"type\":\"object\",\"properties\":{\"path\":{\"type\":\"string\",\"description\":\"目标文件路径\"},\"content\":{\"type\":\"string\",\"description\":\"完整新内容\"}},\"required\":[\"path\",\"content\"]}"),
                new ToolSpec("text-append", "追加文本到文件末尾（文件不存在则新建；路径支持 id: 前缀同 text-read）", "{\"type\":\"object\",\"properties\":{\"path\":{\"type\":\"string\",\"description\":\"目标文件路径\"},\"content\":{\"type\":\"string\",\"description\":\"要追加的文本\"}},\"required\":[\"path\",\"content\"]}"),
                new ToolSpec("text-replace", "替换文本——old 全部出现处替换为 new，返回替换数量；未找到报错（路径支持 id: 前缀同 text-read）", "{\"type\":\"object\",\"properties\":{\"path\":{\"type\":\"string\",\"description\":\"目标文件路径\"},\"old\":{\"type\":\"string\",\"description\":\"要查找的旧文本\"},\"new\":{\"type\":\"string\",\"description\":\"替换后的新文本\"}},\"required\":[\"path\",\"old\",\"new\"]}"),
                // text-* v2 扩展（design-ch4-text-tools.md C4——检索面/区间读/文件管理；编码内建+换行保真+锚点三态）
                new ToolSpec("text-read_lines", "按行号区间读取文本（start 起 / end 止，1 起；end 省略读至文件尾；编码自动探测）——大文件省 token", "{\"type\":\"object\",\"properties\":{\"path\":{\"type\":\"string\",\"description\":\"文件路径\"},\"start\":{\"type\":\"integer\",\"description\":\"起始行（1 起，默认 1）\"},\"end\":{\"type\":\"integer\",\"description\":\"结束行（默认文件尾）\"}},\"required\":[\"path\"]}"),
                new ToolSpec("text-read_between", "锚点区间读取——str1 与 str2 之间内容（str1 空=文件头 / str2 空=文件尾；锚点须全文唯一；编码自动探测）——大文件精确取段", "{\"type\":\"object\",\"properties\":{\"path\":{\"type\":\"string\",\"description\":\"文件路径\"},\"str1\":{\"type\":\"string\",\"description\":\"起始锚点（空=文件头）\"},\"str2\":{\"type\":\"string\",\"description\":\"结束锚点（空=文件尾）\"}},\"required\":[\"path\"]}"),
                new ToolSpec("text-tree", "目录树——列目录结构（depth 层级 / limit 条数上限；稳定排序）", "{\"type\":\"object\",\"properties\":{\"path\":{\"type\":\"string\",\"description\":\"目录路径\"},\"depth\":{\"type\":\"integer\",\"description\":\"递归深度（默认 2，≤10）\"},\"limit\":{\"type\":\"integer\",\"description\":\"最大条数（默认 500）\"}},\"required\":[\"path\"]}"),
                new ToolSpec("text-find", "文件名 glob 搜索——按文件名模式找文件（pattern 如 *.md / **/*.cs；recursive 默认 true）", "{\"type\":\"object\",\"properties\":{\"dir\":{\"type\":\"string\",\"description\":\"搜索根目录\"},\"pattern\":{\"type\":\"string\",\"description\":\"文件名模式（默认 *）\"},\"recursive\":{\"type\":\"boolean\",\"description\":\"是否递归（默认 true）\"},\"limit\":{\"type\":\"integer\",\"description\":\"最大结果（默认 500）\"}},\"required\":[\"dir\"]}"),
                new ToolSpec("text-grep", "内容关键词搜索——目录内递归扫文本文件，返回 相对路径:行号:上下文（前后 ≤10 字符）——定位代码/文档关键词", "{\"type\":\"object\",\"properties\":{\"dir\":{\"type\":\"string\",\"description\":\"搜索根目录\"},\"keyword\":{\"type\":\"string\",\"description\":\"搜索关键词（大小写敏感）\"},\"pattern\":{\"type\":\"string\",\"description\":\"文件名过滤（默认 *）\"},\"limit\":{\"type\":\"integer\",\"description\":\"最大结果（默认 200）\"}},\"required\":[\"dir\",\"keyword\"]}"),
                new ToolSpec("text-move", "移动/重命名——文件与目录均支持（目录=整棵子树移动）；自动创建目标父目录；目标已存在拒绝", "{\"type\":\"object\",\"properties\":{\"src\":{\"type\":\"string\",\"description\":\"源路径\"},\"dest\":{\"type\":\"string\",\"description\":\"目标路径\"}},\"required\":[\"src\",\"dest\"]}"),
                new ToolSpec("text-delete", "软删除——移入受控回收站（可恢复）；支持文件与空目录", "{\"type\":\"object\",\"properties\":{\"path\":{\"type\":\"string\",\"description\":\"要删除的文件/空目录路径\"}},\"required\":[\"path\"]}"),
                new ToolSpec("mau-verify", "Mau 语料全链检查（词法→解析→验证→分析），返回诊断（文件:行:错误码:消息）；零产出", "{\"type\":\"object\",\"properties\":{\"file\":{\"type\":\"string\",\"description\":\".mau 文件路径\"}},\"required\":[\"file\"]}"),
                new ToolSpec("mau-gen", "组翻译——.mauproj 组声明 → 中间产物（验证全组 + BRIKGROUP.cs + FL_*.cs）；不编译", "{\"type\":\"object\",\"properties\":{\"proj\":{\"type\":\"string\",\"description\":\".mauproj 文件路径\"}},\"required\":[\"proj\"]}"),
                new ToolSpec("mau-proj", "组翻译 + 编译——.mauproj → Flows/FL_<组>.dll（长耗时；产物可在宿主热重载）", "{\"type\":\"object\",\"properties\":{\"proj\":{\"type\":\"string\",\"description\":\".mauproj 文件路径\"},\"build\":{\"type\":\"boolean\",\"description\":\"true=翻译后执行 dotnet build\"}},\"required\":[\"proj\"]}"),
                new ToolSpec("host-reload", "热重载语料 dll（宿主级）——在 mau-proj 编译成功后单独调用（建议下一轮）；事务三段式：加载失败保留旧版本；cat=quick|dev", "{\"type\":\"object\",\"properties\":{\"cat\":{\"type\":\"string\",\"description\":\"quick|dev\"}},\"required\":[\"cat\"]}"),
                // P8 三期——Roslyn cs-* 编码工具域（10 件——经 ICSharpBridge / MauRoslynBridge 调度；path=受控根内 csproj 或项目目录；cs-comment_check 缺注释扫描 2026-09-02 新增）
                new ToolSpec("cs-check", "C# 语义快查——项目语法树诊断（增量/毫秒级）；full=true 含警告；实机裁决走 cs-build", "{\"type\":\"object\",\"properties\":{\"path\":{\"type\":\"string\",\"description\":\"csproj 路径或项目目录（受控根内）\"},\"full\":{\"type\":\"boolean\",\"description\":\"true=输出全部警告\"}},\"required\":[\"path\"]}"),
                new ToolSpec("cs-build", "C# 实机编译——dotnet build 子进程（唯一权威裁决；成功后引用集自动刷新）", "{\"type\":\"object\",\"properties\":{\"path\":{\"type\":\"string\",\"description\":\"csproj 路径或项目目录\"}},\"required\":[\"path\"]}"),
                new ToolSpec("cs-list", "类/成员签名清单（语法层；class 空=全项目类清单）", "{\"type\":\"object\",\"properties\":{\"path\":{\"type\":\"string\",\"description\":\"csproj 路径或项目目录\"},\"class\":{\"type\":\"string\",\"description\":\"类名（空=全项目）\"}},\"required\":[\"path\"]}"),
                new ToolSpec("cs-read", "成员源码 + 方法内行号标注（补丁锚点依据；member 空=类概览）", "{\"type\":\"object\",\"properties\":{\"path\":{\"type\":\"string\",\"description\":\"csproj 路径或项目目录\"},\"class\":{\"type\":\"string\",\"description\":\"类名\"},\"member\":{\"type\":\"string\",\"description\":\"成员名（空=类概览）\"}},\"required\":[\"path\",\"class\"]}"),
                new ToolSpec("cs-find_ref", "成员全引用（含重载全匹配；语义级）", "{\"type\":\"object\",\"properties\":{\"path\":{\"type\":\"string\",\"description\":\"csproj 路径或项目目录\"},\"class\":{\"type\":\"string\",\"description\":\"类名\"},\"member\":{\"type\":\"string\",\"description\":\"成员名\"}},\"required\":[\"path\",\"class\",\"member\"]}"),
                new ToolSpec("cs-patch", "方法体级替换（锚点=类+方法名；body 完整含大括号）——三态：OK 落盘 / ROLLED_BACK 未落盘+诊断 / ERR", "{\"type\":\"object\",\"properties\":{\"path\":{\"type\":\"string\",\"description\":\"csproj 路径或项目目录\"},\"class\":{\"type\":\"string\",\"description\":\"类名\"},\"method\":{\"type\":\"string\",\"description\":\"方法名\"},\"body\":{\"type\":\"string\",\"description\":\"新方法体（含大括号）\"}},\"required\":[\"path\",\"class\",\"method\",\"body\"]}"),
                new ToolSpec("cs-member", "成员增删改——op=insert(增)/delete(删)/rename(改名 全项目引用同步)", "{\"type\":\"object\",\"properties\":{\"path\":{\"type\":\"string\",\"description\":\"csproj 路径或项目目录\"},\"class\":{\"type\":\"string\",\"description\":\"类名\"},\"op\":{\"type\":\"string\",\"description\":\"insert|delete|rename\"},\"position\":{\"type\":\"string\",\"description\":\"insert 用：end|before|after|after_fields\"},\"anchor\":{\"type\":\"string\",\"description\":\"before/after 用：锚点成员名\"},\"code\":{\"type\":\"string\",\"description\":\"insert 用：完整成员声明源码\"},\"oldName\":{\"type\":\"string\",\"description\":\"rename 用：旧成员名\"},\"newName\":{\"type\":\"string\",\"description\":\"rename 用：新成员名\"}},\"required\":[\"path\",\"class\",\"op\"]}"),
                new ToolSpec("cs-comment", "XML 注释增改——type=summary/param/returns（param 需 param=参数名）", "{\"type\":\"object\",\"properties\":{\"path\":{\"type\":\"string\",\"description\":\"csproj 路径或项目目录\"},\"class\":{\"type\":\"string\",\"description\":\"类名\"},\"member\":{\"type\":\"string\",\"description\":\"成员名（空=类）\"},\"type\":{\"type\":\"string\",\"description\":\"summary|param|returns\"},\"text\":{\"type\":\"string\",\"description\":\"注释文本\"},\"param\":{\"type\":\"string\",\"description\":\"type=param 时的参数名\"}},\"required\":[\"path\",\"class\",\"type\",\"text\"]}"),
                new ToolSpec("cs-dead", "零引用成员扫描（private/internal；public/override 跳过）", "{\"type\":\"object\",\"properties\":{\"path\":{\"type\":\"string\",\"description\":\"csproj 路径或项目目录\"}},\"required\":[\"path\"]}"),
                new ToolSpec("cs-comment_check", "缺 summary 注释扫描（类 + 成员；交付自检链三件之一：check 编译 / comment_check 注释 / dead 零引用）", "{\"type\":\"object\",\"properties\":{\"path\":{\"type\":\"string\",\"description\":\"csproj 路径或项目目录\"}},\"required\":[\"path\"]}"),
                // P8.5d——config-* 配置自改工具组（4 件——schema 白名单写：config-set/reset 仅 writable 项；唯一校验实现 ConfigStore.SetChecked）
                new ToolSpec("config-list", "配置全览——schema 全部条目（键/当前值/来源/schema 默认/敏感/可写/值域/描述）；敏感键掩码", "{\"type\":\"object\",\"properties\":{}}"),
                new ToolSpec("config-get", "配置单项查询——按 schema 键返回（含默认/敏感/可写/值域/描述）；敏感键掩码", "{\"type\":\"object\",\"properties\":{\"key\":{\"type\":\"string\",\"description\":\"配置键（如 ui.chat_font_size）\"}},\"required\":[\"key\"]}"),
                new ToolSpec("config-set", "配置写入——仅 schema 声明且 writable=true 的项（白名单+值域校验+原子写+失败回滚）；llm.* 私密环境变量只读", "{\"type\":\"object\",\"properties\":{\"key\":{\"type\":\"string\",\"description\":\"配置键\"},\"value\":{\"type\":\"string\",\"description\":\"新值（掩码值拒绝）\"}},\"required\":[\"key\",\"value\"]}"),
                new ToolSpec("config-reset", "配置还原默认——key 空=全群 writable 项还原 schema default；key 非空=单项", "{\"type\":\"object\",\"properties\":{\"key\":{\"type\":\"string\",\"description\":\"配置键（空=全群）\"}},\"required\":[]}"),
                // M4a——Note 轻量任务追踪（Cat 内置工具——会话内直执；CH2 同款定义）
                new ToolSpec("Note", "轻量任务追踪器（内存存储，会话关闭即消失）。无参数=推进到下一条；action='set'+content='任务1\\n任务2'=写入新计划（已有未完成需force=true强制覆盖）。返回当前第X/Y条 已完成Z 待完成W 任务目标：... 最后一条时追加提示（已是最后一条需求，完成后可结束本轮）。全部完成后自动清空。剩余1条时引擎不自动拉起。", "{\"type\":\"object\",\"properties\":{\"action\":{\"type\":\"string\",\"description\":\"set=写入新计划，不传=推进\"},\"content\":{\"type\":\"string\",\"description\":\"action=set时必填，\\n分割\"},\"force\":{\"type\":\"boolean\",\"description\":\"覆盖已有未完成计划时传true\"}},\"required\":[]}"),
                // R1.1/R1.2——内置工具（会话内直执——无需 OA；R0.2 分层：内置 vs OA 双轨）
                new ToolSpec("time", "当前系统日期时间（yyyy-MM-dd HH:mm:ss）——会话内直执，无需 OA", "{\"type\":\"object\",\"properties\":{}}"),
                new ToolSpec("random", "生成 [min, max) 范围内的随机整数（min 含下限，max 不含上限，要求 min < max）——会话内直执，无需 OA", "{\"type\":\"object\",\"properties\":{\"min\":{\"type\":\"integer\",\"description\":\"随机范围下限（含）\"},\"max\":{\"type\":\"integer\",\"description\":\"随机范围上限（不含）\"}},\"required\":[\"min\",\"max\"]}"),
                new ToolSpec("info", "查看运行时工具注册表——工具清单/参数/归属工具组 Flow/内置状态（agent 的眼睛；R1.2）", "{\"type\":\"object\",\"properties\":{}}"),
                // R2.1——web-search 联网搜索（OA 工具——SearchCat 工具组 Flow 认领；服务端自动执行全链）
                new ToolSpec("web-search", "联网搜索——检索并返回基于搜索结果的回答（引用标注 [citation:x] 对应搜索结果序号）；搜索 API 需在配置区先配置", "{\"type\":\"object\",\"properties\":{\"query\":{\"type\":\"string\",\"description\":\"搜索查询\"}},\"required\":[\"query\"]}"),
                new ToolSpec("temp-info", "临时工具信息——返回当前 TempToolCat 全部可用临时工具 Key 组（逗号分隔；R3.1 万能接口试验场——temp.exec 的 Key 注册表枚举）", "{\"type\":\"object\",\"properties\":{}}"),
                new ToolSpec("temp-exec", "临时工具万能执行——输入 Key + content，按 Key 调度到临时工具并返回 str 结果；Key 不存在报 ERR|TEMP_KEY_NOT_FOUND（R3.1 万能接口——临时工具本体在 BRIK-TEMP-001 LLM 可改区）", "{\"type\":\"object\",\"properties\":{\"key\":{\"type\":\"string\",\"description\":\"临时工具 Key（temp-info 可查当前可用 Key 组）\"},\"content\":{\"type\":\"string\",\"description\":\"输入内容\"}},\"required\":[\"key\",\"content\"]}" ),
                // PsCat——powershell 执行（OA 工具——PsCat 工具组 Flow 认领；整段 EncodedCommand 免转义 + UTF-8 内建 + 写文件拦截）
                new ToolSpec("powershell", "执行 PowerShell 命令——整段命令原样执行（内部 EncodedCommand 免转义）；返回 JSON（exit/stdout/stderr/truncated/timeout）；写文件语义被拦截（走 text-* 读写工具）；git 命令豁免；禁 Start-Process/ReadKey", "{\"type\":\"object\",\"properties\":{\"command\":{\"type\":\"string\",\"description\":\"PowerShell 命令文本（完整一段脚本）\"},\"cwd\":{\"type\":\"string\",\"description\":\"工作目录（默认宿主数据根）\"},\"timeout_ms\":{\"type\":\"integer\",\"description\":\"超时毫秒（默认 30000，上限 300000）\"}},\"required\":[\"command\"]}"),
                // R2.2——image-analyze 图像识别（OA 工具——VisionCat 工具组 Flow 认领；图片读取与格式化上传在工具内部）
                new ToolSpec("image-analyze", "图像识别——读取图片（本地路径或 http(s) URL）并用视觉模型分析，返回基于提示词的描述/OCR/图表解读；视觉 API 需在配置区先配置", "{\"type\":\"object\",\"properties\":{\"path\":{\"type\":\"string\",\"description\":\"图片路径（本地绝对路径或 http(s) URL）\"},\"question\":{\"type\":\"string\",\"description\":\"提示词（对图片的提问，可空=默认描述）\"}},\"required\":[\"path\"]}")
            };
            return specs;
        }
        /// <summary>
        /// 工具执行器路由——按工具名调度（一期直执；未知工具 ERR）
        /// </summary>
        /// <param name="name">工具名</param>
        /// <param name="argsJson">参数 JSON（展平）</param>
        /// <returns>执行结果（失败 ERR| 前缀——错误可见性）</returns>
        private static string ExecuteTool(string name, string argsJson)
{
            if (name == "text-read")
            {
                return ExecTextRead(argsJson);
            }
            if (name == "text-write")
            {
                return ExecTextWrite(argsJson);
            }
            if (name == "text-append")
            {
                return ExecTextAppend(argsJson);
            }
            if (name == "text-replace")
            {
                return ExecTextReplace(argsJson);
            }
            if (name == "text-read_lines")
            {
                return ExecTextReadLines(argsJson);
            }
            if (name == "text-read_between")
            {
                return ExecTextReadBetween(argsJson);
            }
            if (name == "text-tree")
            {
                return ExecTextTree(argsJson);
            }
            if (name == "text-find")
            {
                return ExecTextFind(argsJson);
            }
            if (name == "text-grep")
            {
                return ExecTextGrep(argsJson);
            }
            if (name == "text-move")
            {
                return ExecTextMove(argsJson);
            }
            if (name == "text-delete")
            {
                return ExecTextDelete(argsJson);
            }
            if (name == "mau-verify")
            {
                return ExecMauVerify(argsJson);
            }
            if (name == "mau-gen")
            {
                return ExecMauGen(argsJson);
            }
            if (name == "mau-proj")
            {
                return ExecMauProj(argsJson);
            }
            if (name == "host-reload")
            {
                return ExecHostReload(argsJson);
            }
            if (name == "web-search")
            {
                return ExecWebSearch(argsJson);
            }
            if (name == "image-analyze")
            {
                return ExecImageAnalyze(argsJson);
            }
            if (name == "powershell")
            {
                return ExecPowerShell(argsJson);
            }
            if (name.StartsWith("cs-", StringComparison.Ordinal))
            {
                return ExecCSharpTool(name, argsJson);
            }
            return "ERR|UNKNOWN_TOOL|未知工具: " + name;
        }
        // [段3] C# 工具桥执行器——P8 三期（cs-* 9 件经 ICSharpBridge/MauRoslynBridge 调度：磁盘权威 + 三态缓存 + 回滚保护）

        /// <summary>
        /// cs-* 统一执行——桥内分派（method = 工具名去 cs- 前缀）；结果截断防爆
        /// </summary>
        /// <param name="name">工具名（cs-check 等）</param>
        /// <param name="argsJson">参数整包 JSON</param>
        /// <returns>桥结果文本（OK/ROLLED_BACK/ERR| 语义）</returns>
        private static string ExecCSharpTool(string name, string argsJson)
        {
            Mau.Runtime.ICSharpBridge bridge;
            if (!DataBox.TryResolve<Mau.Runtime.ICSharpBridge>(out bridge))
            {
                return "ERR|CSHARP_NO_BRIDGE|宿主未注入 ICSharpBridge（Bootstrap 需 Bind MauRoslynBridge）";
            }
            string method = name.Substring(3);
            string result;
            bool ok = bridge.Invoke(method, argsJson, out result);
            if (!ok || result == null || result.Length == 0)
            {
                return "ERR|BRIDGE_FAIL|cs-" + method + " 调用失败（" + (result ?? "空结果") + "）";
            }
            return TrimResult(result, MaxToolResultChars);
        }

        /// <summary>
        /// web-search——联网搜索（R2.1；FALLBACK 直执保底面——主路径 SearchCat Flow 认领）
        /// </summary>
        /// <param name="argsJson">工具参数 JSON（query）</param>
        /// <returns>搜索回答（失败 ERR| 前缀）</returns>
        private static string ExecWebSearch(string argsJson)
        {
            string query = ExtractArg(argsJson, "query");
            if (query.Length == 0)
            {
                return "ERR|BAD_ARGS|缺少参数 query";
            }
            Mau.Runtime.IWebSearchService service;
            if (!DataBox.TryResolve<Mau.Runtime.IWebSearchService>(out service))
            {
                return "ERR|WEB_NO_SERVICE|宿主未注入 IWebSearchService";
            }
            return TrimResult(service.Search(query), MaxToolResultChars);
        }

        /// <summary>
        /// image-analyze——图像识别（R2.2；FALLBACK 直执保底面——主路径 VisionCat Flow 认领）
        /// </summary>
        /// <param name="argsJson">工具参数 JSON（path/question）</param>
        /// <returns>分析结果（失败 ERR| 前缀）</returns>
        private static string ExecImageAnalyze(string argsJson)
        {
            string path = ExtractArg(argsJson, "path");
            if (path.Length == 0)
            {
                return "ERR|BAD_ARGS|缺少参数 path";
            }
            string question = ExtractArg(argsJson, "question");
            Mau.Runtime.IVisionService service;
            if (!DataBox.TryResolve<Mau.Runtime.IVisionService>(out service))
            {
                return "ERR|VISION_NO_SERVICE|宿主未注入 IVisionService";
            }
            return TrimResult(service.Analyze(path, question), MaxToolResultChars);
        }

        /// <summary>
        /// powershell——执行 PowerShell 命令（PsCat；FALLBACK 直执保底面——主路径 PsCat Flow 认领）
        /// </summary>
        /// <param name="argsJson">工具参数 JSON（command/cwd/timeout_ms）</param>
        /// <returns>结果 JSON（失败 ERR| 前缀）</returns>
        private static string ExecPowerShell(string argsJson)
        {
            Mau.Runtime.IPsService service;
            if (!DataBox.TryResolve<Mau.Runtime.IPsService>(out service))
            {
                return "ERR|PS_NO_SERVICE|宿主未注入 IPsService";
            }
            return TrimResult(service.Exec(argsJson), MaxToolResultChars);
        }
    }
}