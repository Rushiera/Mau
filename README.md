# Mau —— 会自己长大的系统

> 埃及语"猫"（mau/mjw）。古埃及太阳神 Ra 亦被称为"伟大的猫 Mau"。
> 仓库历史一直在——Mau 这个名字是象征。

---

## 一、立意与目标

**我们在造一个 AI 用自己的语言、写自己的宿主、自己的工具、自己的家。**

不是"让 AI 帮人写代码"——是让 AI 从零搭建一套自己能持续进化的运行时：

```
LLM 写 .mau 语料 → 门禁验真（词法/解析/验证/E2xx 引用/E3xx 图论/E4xx 积木——失败拒绝生成）
  → 翻译 C# → 编译 dll → 热重载加载（宿主不重启）→ 观测（帧号可溯）→ 迭代
```

**自举循环：** 业务代码 100% 由语料表达——改语料、重编译、即生效。AI 自己闭环自己。

**哲学：** LLM 负责表达，确定性代码负责验真。语言为 LLM 输出稳定性设计（符号化 + 白名单词法）；分析门禁（图论验证）填补 LLM 的逻辑盲区——表达自由，验证严格。

**定位：** 私人方言，只服务一个消费者——从不为陌生用户建防护，不为未来产品留接口。不考虑市场与通用性，只把自己的家修好。

**双区块（同仓推进）：**

| 区块 | 是什么 | 位置 |
|:--|:--|:--|
| **Mau 基座** | 私人方言语言内核 + 数字电路运行时（Tick/Inbox/OA/CommandBus/DataBox/ALC）+ 门禁工具链 + 积木原子能力 | `Mau.Cli` / `Mau.Runtime` / `Mau.Translator` … |
| **CH4 宿主** | 自举循环宿主——agent 循环基建走 C#，业务 100% 语料化热重载 | `CH4.Entry` / `corpus/ch4/` |

**现状：** 自举循环真实运转——LLM 经宿主工具（text.* 文件操作 / mau.* 语料自查 / cs.* Roslyn 编码 / config.* 配置自改 / web-search 联网 / image-analyze 识图）完成"发现问题 → 修语料 → 门禁通过"闭环。多猫并发（每猫独立会话/端口/配置）+ 工具组 Flow 化（ToolRegistry 动态注册/热重载/退役）+ qqbot 接入（QQ Bot 池 + 双向桥接 + 强匹配指令）已落地。版本与里程碑唯一真相源：CCBP `Project/CH4/CHANGELOG.md` 与 `Project/Mau/CHANGELOG.md`。

### 当前能力（第一期工具与原型已落地）

**工具面（27 件——LLM 可调用，ToolRegistry 单一真相源）：**

| 域 | 工具 | 执行器 |
|:--|:--|:--|
| 文本 | text-read / text-write / text-append / text-replace | FileSystemService（受控根 + 回收站语义） |
| Mau 自查 | mau-verify / mau-gen / mau-proj | MauCompilerV3 进程内直调 |
| 编码 | cs-check / cs-build / cs-list / cs-read / cs-find_ref / cs-patch / cs-member / cs-comment / cs-dead | ICSharpBridge + MauRoslynBridge |
| 配置 | config-list / config-get / config-set / config-reset | ConfigStore SetChecked/ResetToDefault |
| 联网 | web-search | SearchCat Flow → IWebSearchService |
| 识图 | image-analyze | VisionCat Flow → IVisionService |
| 内置 | host-reload / time / random / info / Note | 会话内/宿主直执 |

**多猫并发（P9）：** 单进程多会话——每猫独立 ChatSession + 端口级路由 + cat.cfg 独立配置（persona / toolNames / injectList / apiConfigId / qqbotId）；默认猫 majordomo + cat.* 指令族管理（cat.new / start / stop / delete / chat）。

**工具组 Flow 化（R0.2）：** 每工具组独立 .mau Flow（TextCat / MauCat / CsCat / ConfigCat / SearchCat / VisionCat）——独立注册 / 热重载 / 退役；ToolRegistry 动态注册面（内置 / OA 双轨）。

**qqbot 接入（R2.3——附属功能组件 / 全局插件，非 toolcall）：**

- **QQ Bot 配置池**——对齐 LLM API 池（qqbot.json 明文零 secret + qqbot.cfg secrets + CRUD 端点 + 前端管理页）
- **每猫配置**——cat.cfg qqbotId / qqbotEnable（多 Cat 可绑同一 Bot，多对一）
- **QQ 管理器**——注册即建 WS 连接（CH1 协议资产：鉴权 / 心跳 ACK / Resume / 重连）；跟随宿主启动
- **输入路由**——消息广播注入所有绑定且启用的 Cat（`[来自QQ]` 前缀）；未绑定返回「无猫」/ 未启用返回「目标 Cat 未启用 qqbot 转发功能」
- **输出转发**——游标增量轮询，回复块带 `Cat名：` 前缀转发（无条件转发——前端对话回复也同步）；失败 L2 留痕
- **强匹配指令**——`/ping` `/info` 等 / 开头指令代码直执（不走 LLM），返回 QQ 管理器服务状态
- **LLM 与 Talk 无感**——输入前缀 LLM 可见；输出由管理器主动拉取，ChatSession 零改动

---

## 二、第一次部署（从空仓库）+ 常用指令

### 2.1 前置

- Windows + .NET 8 SDK（`dotnet --version` ≥ 8.0）
- 取仓库：

```
git clone git@gitee.com:wu_lisha/mau.git
cd mau
```

### 2.2 部署序（唯一次序——顺序不可跳）

```
1. dotnet build Mau.sln
2. dotnet test Mau.sln
3. dotnet publish Mau.Cli -c Debug -o Mau-public
4. Mau.Cli\bin\Debug\net8.0\mau.exe proj corpus\ch4\quick_cat.mauproj -o public\src\quick_cat --build
   Mau.Cli\bin\Debug\net8.0\mau.exe proj corpus\ch4\dev_cat.mauproj -o public\src\dev_cat --build
5. dotnet publish CH4.Entry -c Debug -o public\app
6. public\app\CH4.Entry.exe --run "session count"
```

| 步 | 做什么 | 验收 |
|:--|:--|:--|
| 1 | 构建 + NuGet 还原（唯一真相源 = git） | 0 错误 0 警告 |
| 2 | 全量测试（fixture 自给自足生成） | 188/188 |
| 3 | **基座部署区**——组构建的引用源（无此步 mau proj 报 M3245 找不到 Mau.Runtime） | `Mau-public/Mau.Runtime.dll` 存在 |
| 4 | 语料 → 组翻译 → 编译 dll | `public/app/Flows/FL_QuickCat.dll` + `FL_DevCat.dll` |
| 5 | 宿主部署 | `public/app/CH4.Entry.exe` 存在 |
| 6 | 宿主自检——CLI 全链（主线程直执 + 进程自退） | 两 Cat 注册 + `会话消息数: 1` |

> 步骤 3-6 的顺序是硬约束：改基座源码后必须重跑 3-4（`Mau-public/` 是编译/运行时同源点）。语料层随时可重建（`public/app/Flows/*.dll` 运行中热重载不锁文件）；宿主自身 publish 前先停进程。规格权威：CCBP `Project/CH4/design-ch4-deploy.md`。

### 2.3 常用指令（工作目录 = 仓库根）

**mau CLI（构筑与门禁）：**

| 指令 | 用途 |
|:--|:--|
| `mau verify <file.mau>` | 全链检查（词法→解析→验证→分析，不产出） |
| `mau gen <file.mau> -o <dir>` | 全链编译 + C# 生成物 |
| `mau proj <组.mauproj> -o <srcDir> [--build]` | 统一构筑链——组翻译落盘；--build 走 dotnet build → Flows/FL_&lt;组&gt;.dll |
| `mau build <组.mauproj>` | 统一链路由（转发 mau proj --build） |
| `mau test [--update]` | 三段门禁——[1/3] L2 翻译器+Runtime [2/3] L3 黄金哈希 [3/3] L4 积木谱 → `MAU_CHECKS_OK` |
| `mau check` | 语法谱 5/5 + 负例谱 10/10 + 关键路径报告 → `MAU_CHECK_OK` |
| `mau debug <file.mau> [--ticks N] [--step] [--pause-on S_X=Y]` | 四柱状态表 + 单步 + 状态断点 |
| `mau bricks list / index --update / index --verify` | 积木枚举 / 契约提取重算索引（唯一写入通道）/ 索引校验 |

**dotnet（标准入口）：**

| 指令 | 用途 |
|:--|:--|
| `dotnet build Mau.sln` | 全量构建——0/0 是提交底线 |
| `dotnet test Mau.sln` | 标准测试入口（三测试项目全量） |

**宿主（CH4.Entry）：**

| 指令 | 用途 |
|:--|:--|
| `CH4.Entry.exe`（无参） | 程序入口——扫描 Flows/ 加载语料 + HTTP 外观层 http://127.0.0.1:8080 |
| `CH4.Entry.exe --run "<指令>"` | CLI 全链——主线程直执指令 + 进程自退（脚本化跑测通道） |

**git：**

| 约定 | 内容 |
|:--|:--|
| 提交信息 | `vX.Y: 变更摘要`（≤30 字，动词开头；一次提交一个主题） |
| 双版本线 | 基座 `v3.x` 与业务 `v0.xx` 同仓推进；历史唯一真相 = CCBP 双 CHANGELOG |
| 黄金文件 | `Mau.Snapshots/golden-sha-v3.txt` 变更必须在 commit 信息中说明原因 |

---

## 许可证

MIT License · 详见 [LICENSE](./LICENSE)