# CH4 对话页前端（chat）

> 定位：对话页前端的**唯一实现处**——契约 **chat 快照协议 v2**（`design-ch4-protocol.md` §十二）的前端侧。
> 结构口径：**素材（渲染）与主干（时序）分离**——素材零全局状态、零网络；挂载 / 排序 / 跟随 / 指令归主干。
> 加载入口：`../chat.html` 的脚本清单（顺序即依赖）；本目录不自行启动。

---

## 一、目录结构

| 目录 | 内容 |
|:--|:--|
| `lib/` | 渲染共用件（8 件）——DOM 构造 `el` / 格式化 `fmtCount`·`fmtMs` / MD 渲染 / 图片包裹 / 按压判据 / 思考头行 / 工具骨架 |
| `blocks/` | 契约 11 类块型，**每类一件**（persist 8 + live 3） |
| 主干 | `registry.js`（映射表）· `persist.js`（持久区）· `live.js`（临时区）· `state.js`（状态投影）· `main.js`（收包入口）· `input.js`（用户出口） |

---

## 二、块型 ↔ 文件（契约 §12.5）

### persist 类——条目 `{ type, ts, msgIndex, round, payload }`

| type | 文件 | 渲染函数 |
|:--|:--|:--|
| `user` | `blocks/user.js` | `buildUserBlock(payload)` |
| `text` | `blocks/text.js` | `buildTextBlock(payload)` |
| `reason` | `blocks/reason.js` | `buildReasonBlock(payload)` |
| `toolcard` | `blocks/toolcard.js` | `buildToolBlock(payload)` |
| `retry` | `blocks/retry.js` | `buildRetryBlock(payload)` |
| `error` | `blocks/error.js` | `buildErrorBlock(payload)` |
| `inject_report` | `blocks/inject-report.js` | `buildInjectReportBlock(payload)` |
| `roundsum` | `blocks/roundsum.js` | `buildRoundSumBlock(payload)` |

### live 类——条目 `{ type, payload }`（按帧全量镜像）

| type | 文件 | 渲染函数 |
|:--|:--|:--|
| `stream.text` | `blocks/stream-text.js` | `buildStreamText()` + `setStreamText(h, full)` |
| `stream.reason` | `blocks/stream-reason.js` | `buildStreamReason()` + `appendStreamReason(h, text)` |
| `toolcard.pending` | `blocks/stream-toolcard.js` | `buildStreamToolCard(payload)`（`payload.result` 有值即终态） |

---

## 三、主干职责（对接契约）

| 件 | 职责 | 契约依据 |
|:--|:--|:--|
| `main.js` | 单一应用入口——一个 SSE 流收包 → `chatOnFrame` → 三段各归各位（**无事件分派 / 无键算术 / 无配对 / 无排序**）；滚动跟随；公共小件 | §12.1 / §12.3 |
| `state.js` | state 段整段投影——六态状态条 / 头部数字 / 按钮可用性 / Note 钩子 | §12.2 ① |
| `persist.js` | 持久区——`full` 清区重绘 / `append` 逐条追加；不判流式结束、不判换手 | §12.2 ② / §6.1-D |
| `live.js` | 临时区——全量镜像整体替换（不比对、不 diff） | §12.2 ③ / §6.1-G |
| `registry.js` | type → 渲染函数映射——**前端唯一扩展点**：新增块型 = 后端加 type + 本表加一行 + 一个渲染函数 | §12.5 |
| `input.js` | 用户出口——发送 / 停止 / 继续 / 新会话 / 刷新 / 临时区切换（回车发送自动切回流式态） | §6.1-F |

**帧形**（后端 `ViewBus`）：全量 `{v:2, state, persist:{mode:"full",items}, live:{items}}` · 追加 `{persist:{mode:"append",items}}` · 变化增量 `{state:{…整段…}}` / `{live:{items}}`；无事发生零字节。

---

## 四、边界

- **不含**：面板面（`../app.js` · `../panel*.js` · `../index.html`）· 两页共用件（`../ui-common.js`）
- **待转移侧翼件**（页面功能件，不属 11 类块型；清单与要点 → `design-ch4-frontend-rebuild_log.md` 阶段 3 §侧翼件清单）：滚动自持 · Note · 定时 · 桌宠 · 图片粘贴 · 插话队列 · 命令解码
- **测试**：`../tests/`（vitest；对话面用例随重构销毁，面板面用例保留）
