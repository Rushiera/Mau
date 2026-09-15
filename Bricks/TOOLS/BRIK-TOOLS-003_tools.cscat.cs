// ═══════════════════════════════════════════════════
// 积木: tools.cscat
// ID:   BRIK-TOOLS-003
// 类别: TOOLS
// 作用: 返回 CsCat 工具组全部工具的 OpenAI 兼容定义 JSON（宿主工具池拼装原料——design-ch4-tools-pool）
// 依赖: 无
// 引用: 无（纯文本常量）
// 原理: GetToolsJson() 返回本组工具定义整包 JSON——改描述只动本文件，mau proj 重建即生效
// 常用: FL_CsCat.GetToolsJson() 自曝调用面（IFlow 接口）
// ═══════════════════════════════════════════════════
namespace Mau.Bricks
{
    /// <summary>
    /// 工具定义积木——CsCat 组工具定义 JSON（OpenAI 兼容拼装原料）
    /// </summary>
    public static class ToolsCscatBrick
    {
        /// <summary>
        /// 获取本组工具定义 JSON——{"group":"CsCat","tools":[{name,description,parameters}]}
        /// </summary>
        /// <returns>工具定义 JSON 字符串</returns>
        public static string GetToolsJson()
        {
            return "{\"group\":\"CsCat\",\"tools\":[" +
                "{\"name\":\"cs-check\",\"description\":\"C# 语法层验证——写完代码后的第一轮全量语法检查（逐文件语法诊断，不解析类型/引用）；path 支持 csproj / .sln / 目录（聚合分组输出）；full=true 含语法警告；程序集引用与编译裁决以 cs-build 为唯一权威\",\"parameters\":{\"type\":\"object\",\"properties\":{\"path\":{\"type\":\"string\",\"description\":\"csproj / .sln / 目录（受控根内）\"},\"full\":{\"type\":\"boolean\",\"description\":\"true=输出全部语法警告\"}},\"required\":[\"path\"]}}," +
                "{\"name\":\"cs-build\",\"description\":\"C# 实机编译——dotnet build 子进程（唯一权威裁决；成功后引用集自动刷新）；path 支持 csproj / .sln / 目录（多项目逐个执行）\",\"parameters\":{\"type\":\"object\",\"properties\":{\"path\":{\"type\":\"string\",\"description\":\"csproj / .sln / 目录（受控根内）\"}},\"required\":[\"path\"]}}," +
                "{\"name\":\"cs-list\",\"description\":\"类/成员签名清单（语法层；class 空=全项目类清单）；path 支持 csproj / .sln / 目录（聚合分组输出）\",\"parameters\":{\"type\":\"object\",\"properties\":{\"path\":{\"type\":\"string\",\"description\":\"csproj / .sln / 目录（受控根内）\"},\"class\":{\"type\":\"string\",\"description\":\"类名（空=全项目）\"}},\"required\":[\"path\"]}}," +
                "{\"name\":\"cs-read\",\"description\":\"成员源码 + 文件行号标注（统一文件坐标系；member 空=类概览）\",\"parameters\":{\"type\":\"object\",\"properties\":{\"path\":{\"type\":\"string\",\"description\":\"csproj 路径或项目目录\"},\"class\":{\"type\":\"string\",\"description\":\"类名\"},\"member\":{\"type\":\"string\",\"description\":\"成员名（空=类概览；支持签名后缀如 SubmitChoice(int) 区分重载；.ctor/类名=构造函数）\"}},\"required\":[\"path\",\"class\"]}}," +
                "{\"name\":\"cs-find_ref\",\"description\":\"成员全引用（含重载全匹配；语义级）\",\"parameters\":{\"type\":\"object\",\"properties\":{\"path\":{\"type\":\"string\",\"description\":\"csproj 路径或项目目录\"},\"class\":{\"type\":\"string\",\"description\":\"类名\"},\"member\":{\"type\":\"string\",\"description\":\"成员名\"}},\"required\":[\"path\",\"class\",\"member\"]}}," +
                "{\"name\":\"cs-patch\",\"description\":\"方法体级替换（锚点=类+方法名；body 完整含大括号）——三态：OK 落盘 / ROLLED_BACK 未落盘+诊断 / ERR\",\"parameters\":{\"type\":\"object\",\"properties\":{\"path\":{\"type\":\"string\",\"description\":\"csproj 路径或项目目录\"},\"class\":{\"type\":\"string\",\"description\":\"类名\"},\"method\":{\"type\":\"string\",\"description\":\"方法名\"},\"body\":{\"type\":\"string\",\"description\":\"新方法体（含大括号）\"}},\"required\":[\"path\",\"class\",\"method\",\"body\"]}}," +
                "{\"name\":\"cs-member\",\"description\":\"成员增删改——op=insert(增 返回落盘行号区间)/delete(删 含注释)/rename(改名 全项目引用同步)\",\"parameters\":{\"type\":\"object\",\"properties\":{\"path\":{\"type\":\"string\",\"description\":\"csproj 路径或项目目录\"},\"class\":{\"type\":\"string\",\"description\":\"类名\"},\"op\":{\"type\":\"string\",\"description\":\"insert|delete|rename\"},\"member\":{\"type\":\"string\",\"description\":\"delete 用：待删除成员名（支持签名后缀如 SubmitChoice(int) 区分重载；.ctor/类名=构造函数）\"},\"position\":{\"type\":\"string\",\"description\":\"insert 用：end|before|after|after_fields\"},\"anchor\":{\"type\":\"string\",\"description\":\"before/after 用：锚点成员名\"},\"code\":{\"type\":\"string\",\"description\":\"insert 用：完整成员声明源码\"},\"oldName\":{\"type\":\"string\",\"description\":\"rename 用：旧成员名\"},\"newName\":{\"type\":\"string\",\"description\":\"rename 用：新成员名\"}},\"required\":[\"path\",\"class\",\"op\"]}}," +
                "{\"name\":\"cs-comment\",\"description\":\"XML 注释增改——type=summary/param/returns（param 需 param=参数名）\",\"parameters\":{\"type\":\"object\",\"properties\":{\"path\":{\"type\":\"string\",\"description\":\"csproj 路径或项目目录\"},\"class\":{\"type\":\"string\",\"description\":\"类名\"},\"member\":{\"type\":\"string\",\"description\":\"成员名（空=类；支持签名后缀如 SubmitChoice(int) 区分重载；.ctor/类名=构造函数）\"},\"type\":{\"type\":\"string\",\"description\":\"summary|param|returns\"},\"text\":{\"type\":\"string\",\"description\":\"注释文本\"},\"param\":{\"type\":\"string\",\"description\":\"type=param 时的参数名\"}},\"required\":[\"path\",\"class\",\"type\",\"text\"]}}," +
                "{\"name\":\"cs-dead\",\"description\":\"零引用成员扫描（private/internal；public/override 跳过）；path 支持 csproj / .sln / 目录（聚合分组输出）\",\"parameters\":{\"type\":\"object\",\"properties\":{\"path\":{\"type\":\"string\",\"description\":\"csproj / .sln / 目录（受控根内）\"}},\"required\":[\"path\"]}}," +
                "{\"name\":\"cs-comment_check\",\"description\":\"缺 summary 注释扫描（类 + 成员；交付自检链三件之一：check 编译 / comment_check 注释 / dead 零引用）；path 支持 csproj / .sln / 目录（聚合分组输出）\",\"parameters\":{\"type\":\"object\",\"properties\":{\"path\":{\"type\":\"string\",\"description\":\"csproj / .sln / 目录（受控根内）\"}},\"required\":[\"path\"]}}," +
                "{\"name\":\"cs-format\",\"description\":\"空白/缩进规整（Roslyn Formatter；零语义变更——token 流 + directive 双校验）——mode=check 干跑差异清单（零写入）/ apply 写盘（保真 BOM 与换行）\",\"parameters\":{\"type\":\"object\",\"properties\":{\"path\":{\"type\":\"string\",\"description\":\".cs 文件 / 目录 / csproj（受控根内）\"},\"mode\":{\"type\":\"string\",\"description\":\"check（默认，零写入）| apply（写盘）\"}},\"required\":[\"path\"]}}" +
                "]}";
        }
    }
}
// #MAU_CHECKSUM:SHA256:6010602FE1680702E2EABEDB025FDAE1ECE4B7B50874DD863159F4CE16161E04
