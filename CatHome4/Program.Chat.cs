using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using Mau.Runtime;
using CatHome4.Admin;

namespace CH4
{
    /// <summary>
    /// Program 的 ChatBridge 分部——会话中枢工具协调（P5：MajorDomoCat 工具循环 / P8 B1：工具表驱动直执）。
    /// S1 程序集拆分：会话协调静态面（注册表/默认猫配置/泵/新会话/历史视图）已迁 CatHome4.Core/ChatBridge.cs；
    /// 本文件保留入口壳面：线程守卫 + LLM 运行时 + 工具表 + 注入提示词构建（CatCfg 域委托）+ 工具参数提取。
    /// </summary>
    public static partial class Program
    {
        /// <summary>
        /// 宿主主线程 ID——Main 开头记录（HTTP 线程分流判断：Chat 指令跨线程 Tick 违规——Inbox 泵）
        /// </summary>
        private static int _mainThreadId;

        /// <summary>
        /// LLM 运行时——ChatStream 调度（Bootstrap 注入；DataBox 全局绑定——llm.* 积木消费面）
        /// </summary>
        private static ILlmRuntime _llmRuntime;

        /// <summary>
        /// 构建注入系统提示词——P8.5 会话配置化：角色 + 注入知识（workspace.json inject 清单按序读取，来源标注显式）+ 工具语义声明。
        /// 注入是宿主侧静态动作——新会话/显式 session.new 时调用一次，不随每轮携带。
        /// S1：被 ChatBridge.BuildPrompt 委托注入消费（入口壳 CatCfg 域持有——LoadCatDefaultCfg/FallbackBaseRole 本域）。
        /// </summary>
        /// <param name="workspace">工作区配置（roots + inject 清单）</param>
        /// <param name="specs">工具声明表</param>
        /// <param name="persona">角色段</param>
        /// <param name="injectList">注入清单</param>
        /// <returns>注入提示词构建结果——提示词 + 逐文件结果（问题二：前文加载明细可见性）</returns>
        private static InjectPromptResult BuildInjectPrompt(WorkspaceConfig workspace, ToolSpec[] specs, string persona, string[] injectList)
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            List<InjectFileResult> fileResults = new List<InjectFileResult>();
            // [段0] 基础角色段——全局模板 baseRole（空=无基础角色行；模板缺失回退内置文案——行为不倒退）
            AdminService.CatDefaultCfgData tpl = AdminService.LoadCatDefaultCfg();
            string baseRole = AdminService.FallbackBaseRole;
            if (tpl != null && tpl.BaseRole != null)
            {
                baseRole = tpl.BaseRole.Trim();
            }
            if (baseRole.Length > 0)
            {
                sb.Append(baseRole);
            }
            // [段0] 角色段——cat.cfg persona 非空追加（M2b：注入后追加角色段；空=仅基础角色）
            if (persona != null && persona.Trim().Length > 0)
            {
                sb.Append(System.Environment.NewLine);
                sb.Append(System.Environment.NewLine);
                sb.Append("【角色设定】");
                sb.Append(persona.Trim());
            }
            // [段1] 注入知识——按每猫 injectList 顺序读取（M2d：不再走全局 workspace.inject；寻址复用受控根 id: 命名空间；缺失跳过不阻断会话）
            // 问题二扩展——逐文件结果收集（ok/missing/error + 字符数），HandleSessionNew 生成注入报告
            if (workspace != null && injectList != null && injectList.Length > 0)
            {
                sb.Append(System.Environment.NewLine);
                sb.Append(System.Environment.NewLine);
                sb.Append("【系统前文来源】以下知识文件由本会话注入（清单 cat.cfg injectList）：");
                for (int i = 0; i < injectList.Length; i++)
                {
                    string file = injectList[i];
                    InjectFileResult fr = new InjectFileResult();
                    fr.File = file;
                    fr.Status = "error";
                    fr.Message = "";
                    fr.Chars = 0;
                    try
                    {
                        WorkspaceConfig.InjectEntry entry = new WorkspaceConfig.InjectEntry();
                        entry.File = file;
                        entry.Optional = true;
                        entry.Label = file;
                        string path = workspace.ResolveInjectFile(entry);
                        if (!File.Exists(path))
                        {
                            LogStore.Add("CatHome4", 2, "注入缺失：" + file + "（可选，已跳过）", "INJECT");
                            fr.Status = "missing";
                            fr.Message = "文件不存在（可选，已跳过）";
                            fileResults.Add(fr);
                            continue;
                        }
                        string content = File.ReadAllText(path);
                        sb.Append(System.Environment.NewLine);
                        sb.Append(System.Environment.NewLine);
                        sb.Append("===== 注入文件: ");
                        sb.Append(file);
                        sb.Append(" =====");
                        sb.Append(System.Environment.NewLine);
                        sb.Append(content);
                        fr.Status = "ok";
                        fr.Chars = content.Length;
                        fr.Message = "";
                        fileResults.Add(fr);
                    }
                    catch (Exception ex)
                    {
                        LogStore.Add("CatHome4", 2, "注入失败：" + file + "（" + ex.Message + "）", "INJECT");
                        fr.Status = "error";
                        fr.Message = ex.Message;
                        fileResults.Add(fr);
                    }
                }
            }
            // [段2] 工具声明已移除——payload["tools"] 是 LLM 唯一工具信息源（design-ch4-tools-pool；系统提示词不重复双写）
            InjectPromptResult result = new InjectPromptResult();
            result.Prompt = sb.ToString();
            result.Files = fileResults;
            return result;
        }

    }
}
