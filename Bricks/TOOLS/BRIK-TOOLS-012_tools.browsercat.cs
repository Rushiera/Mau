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
                "{\"name\":\"browser-open\",\"description\":\"打开网页并等待加载完成——返回标题 / 最终 URL / 耗时；不做点击与输入（翻页靠改 URL 或取页面里的链接）；仅 timeback 作用域内可用（域外调用被拒——网页内容属非主干信息，用完回收）；支持任意 http(s) 地址\",\"parameters\":{\"type\":\"object\",\"properties\":{\"url\":{\"type\":\"string\",\"description\":\"目标地址（http 或 https）\"}},\"required\":[\"url\"]}}," +
                "{\"name\":\"browser-read\",\"description\":\"读取当前页面内容并落盘，返回摘要与文件路径——mode=text（正文文本，最省）/ ax（无障碍树精简：控件与结构）/ links（可导航链接清单——据此自己决定下一个打开哪个 URL）/ 缺省 text；仅 timeback 作用域内可用（域外调用被拒）\",\"parameters\":{\"type\":\"object\",\"properties\":{\"mode\":{\"type\":\"string\",\"description\":\"观察面：text 正文 / ax 无障碍树 / links 链接清单（缺省 text）\"}},\"required\":[]}}," +
                "{\"name\":\"browser-eval\",\"description\":\"在当前页面执行 JS 表达式并取回结果——万能取值口（如取 JS 跳转的目标地址、页面内嵌数据）；小结果直返、大结果落盘；仅 timeback 作用域内可用（域外调用被拒）\",\"parameters\":{\"type\":\"object\",\"properties\":{\"expression\":{\"type\":\"string\",\"description\":\"JS 表达式（求值后按值返回，须自带 return 或为表达式）\"}},\"required\":[\"expression\"]}}," +
                "{\"name\":\"browser-shot\",\"description\":\"截图当前页面为 PNG 并返回绝对路径（可交给 image-inject 直接查看原图）；full=true 截整页，缺省截当前视口；仅 timeback 作用域内可用（域外调用被拒）\",\"parameters\":{\"type\":\"object\",\"properties\":{\"full\":{\"type\":\"boolean\",\"description\":\"true=整页截图（长文场景）；缺省=false 截当前视口\"}},\"required\":[]}}," +
                "{\"name\":\"browser-tabs\",\"description\":\"页签管理——list（列出当前实例全部页签，* 标当前）/ new（新建页签，可带 url）/ select（切到指定页签）/ close（关闭指定页签）；多页签让 URL 寻路能「开着不关」——保留来过哪一页，可回退与对比；仅 timeback 作用域内可用（域外调用被拒）\",\"parameters\":{\"type\":\"object\",\"properties\":{\"action\":{\"type\":\"string\",\"description\":\"动作：list / new / select / close（缺省 list）\"},\"target\":{\"type\":\"string\",\"description\":\"页签 id（select / close 必填——先用 list 取）\"},\"url\":{\"type\":\"string\",\"description\":\"新建页签的地址（仅 new 用；空=空白页）\"}},\"required\":[]}}," +
                "{\"name\":\"browser-headful\",\"description\":\"有头登录实例——open：关现有实例并用本猫 profile 起有头浏览器（窗口弹出供人工登录）；close：优雅关闭（profile 落盘——登录态保留）。仅主干可用（域外专属，与其余 browser-* 域内专属相反）；登录态存 profile，之后无头工具自动复用\",\"parameters\":{\"type\":\"object\",\"properties\":{\"action\":{\"type\":\"string\",\"description\":\"动作：open 起有头 / close 关有头（缺省 open）\"},\"url\":{\"type\":\"string\",\"description\":\"open 时可选导航地址（http/https；空=停在空白页）\"}},\"required\":[]}}" +
                "]}";
        }
    }
}
// #MAU_CHECKSUM:SHA256:A13C734D20FE405B6D94C29093FBE81827D71C666F89ECBBDA499193CA7BD3FF
