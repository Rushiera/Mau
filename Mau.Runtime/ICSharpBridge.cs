namespace Mau.Runtime
{
    /// <summary>
    /// C# 工具桥接口——Mau 积木的 Roslyn 能力入口（纯接口，零依赖）。
    /// 实现位于 Mau.Development（MauRoslynBridge——多树隔离 + 编译与提交分离）。
    /// PACK 类：单方法调度入口 Invoke(method, argsJson) + 特化方法（实现内部使用）。
    /// 契约：Bricks/PACK/BRIK-PACK-003_csharp.bridge.cs `方法:` 字段（V11 校验）。
    /// </summary>
    public interface ICSharpBridge
    {
        /// <summary>
        /// PACK 单方法调度——method 白名单 + argsJson 展平参数（积木统一入口）
        /// </summary>
        /// <param name="method">操作名（init/info/list/read/compile/body_replace/...）</param>
        /// <param name="argsJson">展平参数 JSON</param>
        /// <param name="result">结果文本</param>
        /// <returns>true=调用成功（业务错误码进 result）</returns>
        bool Invoke(string method, string argsJson, out string result);

        /// <summary>
        /// 绑定 csproj 项目——建立类索引 + 初始编译诊断
        /// </summary>
        /// <param name="csprojPath">.csproj 路径（相对或绝对）</param>
        /// <returns>JSON：project/documents/classes/errors/warnings</returns>
        string Init(string csprojPath);

        /// <summary>
        /// 查询绑定状态
        /// </summary>
        /// <returns>JSON：project/csproj/documents/classes</returns>
        string GetInfo();

        /// <summary>
        /// 列出类成员——class 空=全项目类名；指定类=成员签名+注释摘要
        /// </summary>
        /// <param name="className">类名（空=全项目）</param>
        /// <returns>清单文本</returns>
        string ListMembers(string className);

        /// <summary>
        /// 读取成员源码——含 XML 注释 + 方法内行号标注（// LN）
        /// </summary>
        /// <param name="className">类名</param>
        /// <param name="memberName">成员名（空=类概览）</param>
        /// <returns>源码文本</returns>
        string ReadMember(string className, string memberName);

        /// <summary>
        /// 编译项目——full=true 返回完整诊断
        /// </summary>
        /// <param name="full">true=完整诊断列表</param>
        /// <returns>JSON：errors/warnings[/diagnostics]</returns>
        string GetDiagnostics(bool full);

        /// <summary>
        /// 替换整个方法体——签名+注释不动，只换 { } 内部
        /// </summary>
        /// <param name="className">类名</param>
        /// <param name="methodName">方法名</param>
        /// <param name="newBody">新方法体（含大括号）</param>
        /// <returns>JSON：ok/label/errors 变化量</returns>
        string ReplaceMethodBody(string className, string methodName, string newBody);

        /// <summary>
        /// 替换方法内指定行范围（1-based，与 read 的 LN 对齐）
        /// </summary>
        /// <param name="className">类名</param>
        /// <param name="methodName">方法名</param>
        /// <param name="startLine">起始行（方法内）</param>
        /// <param name="endLine">结束行（方法内）</param>
        /// <param name="newText">替换文本</param>
        /// <returns>JSON：ok/replaced/errors 变化量</returns>
        string LinePatch(string className, string methodName, int startLine, int endLine, string newText);

        /// <summary>
        /// 在方法内指定行后插入——afterLine=0=body 头
        /// </summary>
        /// <param name="className">类名</param>
        /// <param name="methodName">方法名</param>
        /// <param name="afterLine">插入位置（方法内行号，0=body头）</param>
        /// <param name="newText">插入文本</param>
        /// <returns>JSON：ok/inserted_after_line/errors 变化量</returns>
        string LineInsert(string className, string methodName, int afterLine, string newText);

        /// <summary>
        /// 在类中插入新成员
        /// </summary>
        /// <param name="className">类名</param>
        /// <param name="position">after/before/end/after_fields</param>
        /// <param name="anchor">锚点成员名（after/before 必填）</param>
        /// <param name="code">新成员完整源码</param>
        /// <returns>JSON：ok/errors 变化量</returns>
        string InsertMember(string className, string position, string anchor, string code);

        /// <summary>
        /// 删除指定成员（含注释）
        /// </summary>
        /// <param name="className">类名</param>
        /// <param name="memberName">成员名</param>
        /// <returns>JSON：ok/errors 变化量</returns>
        string DeleteMember(string className, string memberName);

        /// <summary>
        /// 设置 XML 注释——type: summary/param/returns；member 空=类
        /// </summary>
        /// <param name="className">类名</param>
        /// <param name="memberName">成员名（空=类）</param>
        /// <param name="commentType">summary/param/returns</param>
        /// <param name="text">注释文本</param>
        /// <param name="paramName">type=param 时必填</param>
        /// <returns>JSON：ok/errors 变化量</returns>
        string SetComment(string className, string memberName, string commentType, string text, string paramName);

        /// <summary>
        /// 扫描全项目缺 summary 的类/方法/字段/属性
        /// </summary>
        /// <returns>清单+统计</returns>
        string CommentCheck();

        /// <summary>
        /// 重命名成员——全项目引用同步更新
        /// </summary>
        /// <param name="className">类名</param>
        /// <param name="oldName">旧成员名</param>
        /// <param name="newName">新成员名</param>
        /// <returns>JSON：ok/errors 变化量</returns>
        string RenameMember(string className, string oldName, string newName);

        /// <summary>
        /// 扫描全项目零引用 private/internal 成员
        /// </summary>
        /// <returns>清单+统计</returns>
        string DeadCode();

        /// <summary>
        /// 查找成员的所有引用位置
        /// </summary>
        /// <param name="className">类名</param>
        /// <param name="memberName">成员名</param>
        /// <returns>JSON：references 数组</returns>
        string FindReferences(string className, string memberName);

        /// <summary>
        /// 释放全部工作区——宿主 Shutdown 时调用
        /// </summary>
        void Shutdown();
    }
}

