# CH4 对话页前端（chat）

> 定位：对话页前端的**唯一实现处**——契约 **chat 快照协议 v2**（`design-ch4-protocol.md` §十二）的前端侧。
> 结构口径：**素材（渲染）与主干（时序）分离**——素材零全局状态、零网络；挂载 / 排序 / 跟随 / 指令归主干。
> 加载入口：`../chat.html` 的脚本清单（顺序即依赖）；本目录不自行启动。

---

## 一、目录结构

| 目录 | 内容 |
|:--|:--|
| `lib/` | 渲染共用件（10 件）——DOM 构造 `el`/`elText` / 格式化 `fmtCount`·`fmtMs` / MD 渲染 / 图片包裹 / 按压判据 / 思考头行 / 工具骨架 / 兜底件 `fallback`（未识别 type · 渲染异常——两区共用） / **命令解码 `cmd`（PS 意图 + 未识别上报）** |
| `blocks/` | 契约 11 类块型，**每类一件**（persist 8 + live 3） |
| `fx/` | **独立功能——每功能一件 + 声明表**（A176 归位）：`pet`（桌宠）· `scroll`（滚动带）· `pending`（插话队列）· `cmd-intent`（命令解码显示）· `controls`（按钮态）；登记在 `fx/registry.js` 的 `FX_FEATURES`（名称 / 输入源 / 输出面 / 启动入口），启动由 `fxBoot()` 统一执行 |
| 主干 | `registry.js`（**块声明表 + 映射表**——A178 起不兼实现）· `persist.js`（持久区）· `live.js`（临时区）· `state.js`（状态投影）· `main.js`（收包入口 + 公共小件：信息位单点 `chatInfoSet`——**state 段专属**）· `input.js`（用户出口） |
| 面板与侧翼 | `note.js`（Note 面板——气泡 + 弹层）· `delay.js`（定时面板——列表 / 倒计时 / 改时刻）· `paste.js`（图片粘贴上传 + 待发区） |

---

## 二、块型 ↔ 文件（契约 §12.5）

**形态**列 = 该 type 的行形态（气泡 `.chat-bubble` / 朴素件 `.chat-plain`）。
🔴 **块声明的唯一处 = `registry.js` 的块声明表 `BLOCK_DECL`**（每 type 一行：`form` 形态 / `row` 行语义类 / `tick` 滚动带刻度角色 / `body` 块体语义类）——渲染函数经 `formClass(type)` 取形态、`bodyClass(type)` 取块体（形态 + 语义）、`blockRow(type)` 取行容器（含 `data-type`），滚动带经 `data-type` 查 `tickRole(type)`；均不自持类名字符串。改形态或语义 = 改表一行（A179 收口）。本表仅作可读快照。

### persist 类——条目 `{ type, ts, msgIndex, round, payload }`

| type | 文件 | 渲染函数 | 形态 |
|:--|:--|:--|:--:|
| `user` | `blocks/user.js` | `buildUserBlock(payload)` | 🫧 气泡 |
| `text` | `blocks/text.js` | `buildTextBlock(payload)` | 🫧 气泡 |
| `reason` | `blocks/reason.js` | `buildReasonBlock(payload)` | ▬ 朴素 |
| `toolcard` | `blocks/toolcard.js` | `buildToolBlock(payload)` | ▬ 朴素 |
| `retry` | `blocks/retry.js` | `buildRetryBlock(payload)` | ▬ 朴素 |
| `error` | `blocks/error.js` | `buildErrorBlock(payload)` | ▬ 朴素 |
| `inject_report` | `blocks/inject-report.js` | `buildInjectReportBlock(payload)` | ▬ 朴素 |
| `roundsum` | `blocks/roundsum.js` | `buildRoundSumBlock(payload)` | ▬ 朴素 |

### live 类——条目 `{ type, payload }`（按帧全量镜像）

| type | 文件 | 渲染函数 | 形态 |
|:--|:--|:--|:--:|
| `stream.text` | `blocks/stream-text.js` | `buildStreamText()` + `setStreamText(h, full)` | ▬ 朴素 |
| `stream.reason` | `blocks/stream-reason.js` | `buildStreamReason()` + `appendStreamReason(h, text)` | ▬ 朴素 |
| `toolcard.pending` | `blocks/stream-toolcard.js` | `buildStreamToolCard(payload)`（`payload.result` 有值即终态） | ▬ 朴素 |

**形态契约（2026-10-03 莎定）**——气泡只给「会被人当对话内容读」的两类：**用户输入（`user`）+ LLM 最终输出（`text`）**；
其余一切（过程件 / 系统件 / 临时区）一律朴素件。两种形态的样式：`.chat-bubble`（底 + 描边 + 圆角 + 内边距）· `.chat-plain`（无底无描边无圆角无内边距，只有字号与语义字色）。

---

## 三、主干职责（对接契约）

| 件 | 职责 | 契约依据 |
|:--|:--|:--|
| `main.js` | 单一应用入口——一个 SSE 流收包 → `chatOnFrame` → 三段各归各位（**无事件分派 / 无键算术 / 无配对 / 无排序**）；滚动跟随；公共小件 | §12.1 / §12.3 |
| `state.js` | state 段整段投影——六态状态条 / 头部数字 / 按钮可用性 / Note 钩子 | §12.2 ① |
| `persist.js` | 持久区——`full` 清区重绘 / `append` 逐条追加；不判流式结束、不判换手 | §12.2 ② / §6.1-D |
| `live.js` | 临时区——全量镜像整体替换（不比对、不 diff） | §12.2 ③ / §6.1-G |
| `registry.js` | type → 渲染函数映射 + **块声明表 `BLOCK_DECL`**（形态 / 行语义类 / 刻度角色 / 块体语义）+ 六态元信息 `RUN_PHASES`——**前端唯一扩展点**：新增块型 = 后端加 type + 本表加一行（渲染函数 + 声明）+ 一个渲染函数；类名一律经 `formClass(type)` / `bodyClass(type)` / `blockRow(type)` 取用 | §12.5 |
| `input.js` | 用户出口——发送 / 停止 / 继续 / 新会话 / 刷新 / 临时区切换（回车发送自动切回流式态） | §6.1-F |

**帧形**（后端 `ViewBus`）：全量 `{v:2, state, persist:{mode:"full",items}, live:{items}}` · 追加 `{persist:{mode:"append",items}}` · 变化增量 `{state:{…整段…}}` / `{live:{items}}`；无事发生零字节。

---

## 四、边界

- **不含**：面板面（`../app.js` · `../panel*.js` · `../index.html`）· 两页共用件（`../ui-common.js`）
- **独立功能归位 `fx/`**（A176——独立功能面）：桌宠 / 滚动带 / 插话队列 / 命令解码显示 / 按钮态各一件 + 声明表 `fx/registry.js`（登记 = 契约 §12「独立功能面」节）；面板与出口面（`note.js` / `delay.js` / `paste.js`）留主干层；转移清单与要点 → `design-ch4-frontend-rebuild_log.md` 阶段 3 §侧翼件清单
- **显示面归属**（契约 §12.7 · A179 收尾）：顶栏信息位 `#chatInfo` = **state 段专属**（后端必要信息读数——前文条数 / sessionId / 前文长度）；前端提示与告警 → **桌宠气泡** `chatPetSay`（通知面，TTL 自动消失），两者互不覆盖
- **测试**：`../tests/`（vitest；对话面用例随重构销毁，面板面用例保留）
