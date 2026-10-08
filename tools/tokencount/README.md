# tokencount —— token 统计

给任意文本算 token 数：**输出一个整数**。无第三方依赖——纯 Python 标准库实现 byte-level BPE，读官方 `tokenizer.json`。

## 目录

| 文件 | 用途 |
|:--|:--|
| `tokencount.py` | 统计脚本（唯一可执行件） |
| `tokenizer.json` | **词表本体**（6.3 MB）——脚本唯一依赖，`--tokenizer` 缺省取本目录 |
| `tokenizer_config.json` | HF 侧配置——**脚本当前未读**；留着是给「对话模板套壳」用的原料（`chat_template` / special token 语义）。⚠ 其中的 `model_max_length: 16384` 是**旧版值**——现行 DeepSeek V4.1 前文窗口为 **1M**（2026-10-08 莎告），窗口判据不取该字段 |

## 用法

词表随工具放置，**不必传 `--tokenizer`**：

```
python tools\tokencount\tokencount.py <文本文件>
python tools\tokencount\tokencount.py <文本文件> --ids       # 附加 id 列表
python tools\tokencount\tokencount.py <文本文件> --pieces    # 附加 id:token 文本（看分词边界）
python tools\tokencount\tokencount.py --text "Hello, world!"
type x.txt | python tools\tokencount\tokencount.py -
```

换模型才需要传：`--tokenizer <别的 tokenizer.json>`。查找顺序：`--tokenizer` > 环境变量 `TOKENIZER_JSON` > 脚本同级 > 当前目录。

## 口径

byte-level BPE，与 HuggingFace `AutoTokenizer` 同流程：

1. 归一化——原词表 `normalizer` 为空，文本原样进入
2. 预切分——三段 `Split`（Isolated）：数字每 3 位一组 / 中日文块整体隔离 / 标点与词块（GPT-2 风格）
3. 字节映射——UTF-8 字节 → 可见 unicode 字符（`Ġ` = 空格、`Ċ` = 换行）
4. BPE——按 `merges` 列表顺序贪心合并相邻符号对
5. 查表——`vocab` + `added_tokens` 合并成统一查找表，输出 id

特殊 token（`added_tokens` 段的 `<｜User｜>` / `<｜Assistant｜>` 等）**整体匹配、不参与合并**——稳定占 1 token。

## 验证（实跑）

**英文**——`Hello, world!` = **4**，逐 id 与词表磁盘条目对上：

| token | id | 词表条目 |
|:--|:--|:--|
| `Hello` | 19923 | ✓ |
| `,` | 14 | ✓ |
| `Ġworld` | 2058 | ✓ |
| `!` | 3 | ✓ |

**中文**——`猫娘在计算 token 数量：DeepSeek 词表 128000 条。` = **20**，`--pieces` 输出（`␣` = 空格，`␊` = 换行）：

```
11440:猫 6642:娘 445:在 4339:计算 17840:␣token 223:␣ 9853:数量 768:：
53091:Deep 4374:Se 1465:ek 223:␣ 4055:词 1146:表 223:␣ 7833:128 1320:000 223:␣ 1923:条 876:。␊
```

读出来的三条事实：

- **中文能整词成 token**——`计算` / `数量` 各 1 token；`词表` 不在合并表里，退化成 2 个单字
- **数字每 3 位一组**——`128000` → `128` + `000`
- **中文前的空格永远单独占 1 token**——中文块被预切分隔离，空格接不上；英文前的空格并进词（`␣token` 是一个 token）

**免参数路径**——词表放本目录后，`--text "Hello, world!"` 不传 `--tokenizer` 同样得 4。

## 偏差（诚实标注）

- 原词表正则用 `\p{L}\p{M}\p{P}\p{S}`（Unicode 属性类），Python `re` 不支持，脚本用字符类近似。**拉丁 / 中文文本无差**；带变音符号的文字（阿拉伯、天城体等）可能偏离
- **未与官方 `transformers` / `tokenizers` 逐 token 对照过**（本机两库均未安装）。要钉死偏差率，`pip install tokenizers` 后跑一轮对照
- 耗时主要在解析 6.3 MB 的 `tokenizer.json`（128000 词条 + 12 万条 merges），单次启动秒级

## 维护提示

- **体积**：`tokenizer.json` 6.3 MB **随仓库提交**（2026-10-08 莎拍板：素材表进 git，后续做词表查询器要用）——提交后 git 历史不可消除，改主意前先想清
- 只实现 `model.type == "BPE"`——遇到别的类型报 `ERR|UNSUPPORTED_MODEL` 退出，不静默降级
- 词表缺符号时报 `ERR|TOKEN_NOT_IN_VOCAB`，不跳过——静默跳过会让数字偏小且无人知道
- 输出统一钉 UTF-8（`sys.stdout.reconfigure`）——Windows 控制台代码页会撞坏 `--pieces` 的非 ASCII 输出
- **别拿本脚本的结论去比别家模型的 token 数**——词表不同，刻度不同
