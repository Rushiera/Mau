// ═══════════════════════════════════════════════════
// 积木: tools.browsercat
// ID:   BRIK-TOOLS-012
// 类别: TOOLS
// 作用: 返回 BrowserCat 工具组全部工具的 OpenAI 兼容定义 JSON（宿主工具池拼装原料——design-ch4-tools-pool）
// 依赖: 无
// 引用: 无（纯文本常量）
// 原理: GetToolsJson() 返回本组工具定义整包 JSON——改描述只动本文件，mau proj 重建即生效
// 常用: FL_BrowserCat.GetToolsJson() 自曝调用面（IFlow 接口）
// ═══════════════════════════════════════════════════
namespace Mau.Bricks
{
    /// <summary>
    /// 工具定义积木——BrowserCat 组工具定义 JSON（OpenAI 兼容拼装原料）
    /// </summary>
    public static class ToolsBrowsercatBrick
    {
        /// <summary>
        /// 获取本组工具定义 JSON——{"group":"BrowserCat","tools":[{name,description,parameters}]}
        /// </summary>
        /// <returns>工具定义 JSON 字符串</returns>
        public static string GetToolsJson()
        {
            return "{\"group\":\"BrowserCat\",\"tools\":[" +
                "{\"name\":\"browser-open\",\"description\":\"打开 URL 等加载，返回标题 / 最终 URL / 耗时。浏览器流程入口——先 timeback-start type=browser_vision 开域，完事 timeback-back（域内专用）。只导航，无点击输入；翻页改 URL，或用 browser-read 的 links。\",\"parameters\":{\"type\":\"object\",\"properties\":{\"url\":{\"type\":\"string\",\"description\":\"目标地址（http 或 https）\"}},\"required\":[\"url\"]}}," +
                "{\"name\":\"browser-read\",\"description\":\"读当前页并落盘，返回摘要 + 路径。mode：text 正文（默认）/ ax 控件结构 / links 链接清单（挑下一跳用）。域内专用。\",\"parameters\":{\"type\":\"object\",\"properties\":{\"mode\":{\"type\":\"string\",\"description\":\"观察面：text 正文 / ax 无障碍树 / links 链接清单（缺省 text）\"}},\"required\":[]}}," +
                "{\"name\":\"browser-eval\",\"description\":\"在当前页执行 JS 取值（页面数据 / JS 跳转目标 / 内嵌 JSON）。小结果直返，大结果落盘。域内专用。\",\"parameters\":{\"type\":\"object\",\"properties\":{\"expression\":{\"type\":\"string\",\"description\":\"JS 表达式（求值后按值返回，须自带 return 或为表达式）\"}},\"required\":[\"expression\"]}}," +
                "{\"name\":\"browser-shot\",\"description\":\"截当前页为 PNG，返回绝对路径（可交 image-inject 直读原图）。full=true 截整页，默认当前视口。域内专用。\",\"parameters\":{\"type\":\"object\",\"properties\":{\"full\":{\"type\":\"boolean\",\"description\":\"true=整页截图（长文场景）；缺省=false 截当前视口\"}},\"required\":[]}}," +
                "{\"name\":\"browser-tabs\",\"description\":\"页签管理：list / new（可带 url）/ select / close。多页签 = 寻路时开着不关，可回退与对比。域内专用。\",\"parameters\":{\"type\":\"object\",\"properties\":{\"action\":{\"type\":\"string\",\"description\":\"动作：list / new / select / close（缺省 list）\"},\"target\":{\"type\":\"string\",\"description\":\"页签 id（select / close 必填——先用 list 取）\"},\"url\":{\"type\":\"string\",\"description\":\"新建页签的地址（仅 new 用；空=空白页）\"}},\"required\":[]}}," +
                "{\"name\":\"browser-headful\",\"description\":\"有头窗口供人工登录，登录态落 profile、之后无头自动复用。open 起 / close 关（等 profile 落盘）。域外专用——与其余 browser-* 方向相反，域内被拒。\",\"parameters\":{\"type\":\"object\",\"properties\":{\"action\":{\"type\":\"string\",\"description\":\"动作：open 起有头 / close 关有头（缺省 open）\"},\"url\":{\"type\":\"string\",\"description\":\"open 时可选导航地址（http/https；空=停在空白页）\"}},\"required\":[]}}" +
                "]}";
        }
    }
}
// #MAU_CHECKSUM:SHA256:4BB4092965674784B4103409B9C8301FE58FDA45E80464A86EAA53132D724BC7
