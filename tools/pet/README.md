# pet —— 桌宠生产线

把桌宠原素材（GIF）烘成前端可直接消费的动画 WebP 与素材清单。三个脚本各管一段，**各自独立跑**。

## 目录

| 文件 | 用途 |
|:--|:--|
| `convert_pet.py` | **主脚本**——GIF → 裁剪 / 透明区外扩 / 缩放 / 羽化 → 动画 WebP + 写素材清单 |
| `match_pet_seam.py` | before→loop 接缝帧匹配（算 `convert_pet.py` 的 TRIM 截断点）——只读 |
| `verify_frontend.py` | 仓库源码区 ↔ 运行区逐文件字节核销——只读 |
| `src/<皮肤>/*.gif` | **母本素材**（进 git）——现行 `greencat` · 备用 `deepdeep`（桌宠2号，未接线） |
| `EmotelabApp对照表.txt` | 源 App 表情名 → 状态文件名对照 |

## 素材约定

- 每套皮肤 = 一组 GIF，**14 个状态名固定**：`loop-*`（循环态 10）· `before-*`（过渡 3）· `after-*`（后置反应 1）。
- 皮肤之间只换素材、不改状态映射——映射表见 `EmotelabApp对照表.txt` 与前端 `js/chat/fx/pet.js`。
- GIF 规格：500×500 · 1-bit 透明 · 20ms/帧。

## 用法

工作目录随意（脚本按自身位置定位仓库根）。

```
# 烘焙现行皮肤（greencat）全部素材
python tools/pet/convert_pet.py

# 只烘指定素材
python tools/pet/convert_pet.py loop-think before-idle

# 换皮肤烘焙（写现有平铺布局 html/pet/*.webp）
python tools/pet/convert_pet.py --skin deepdeep

# 接缝匹配（只读，对照图输出到 CatTemp）
python tools/pet/match_pet_seam.py

# 前端资源核销（只读）——运行区在仓库之外，须显式给 --run
python tools/pet/verify_frontend.py --run C:/MauTest/MauOut/html
```

## 参数

| 脚本 | 参数 | 默认 | 说明 |
|:--|:--|:--|:--|
| convert_pet | `names`（位置） | 空 | 素材名；空 = 全部 `loop-*` / `before-*` / `after-*` |
| | `--skin` | `greencat` | 皮肤名（`src` 下子目录） |
| | `--src` | `tools/pet/src` | 源素材根 |
| | `--repo` | 脚本上三级 | 仓库根 |
| match_pet_seam | `--pet` | `CatHome4/html/pet` | webp 素材目录 |
| | `--out` | `CatTemp` | 对照图输出目录 |
| verify_frontend | `--repo` | `CatHome4/html` | 仓库 html 目录 |
| | `--run` | **必填** | 运行区 html 目录 |

## 产物去向

| 产物 | 位置 | 说明 |
|:--|:--|:--|
| 动画 WebP | `CatHome4/html/pet/*.webp` | 仓库源码区（进 git） |
| 素材清单 | `CatHome4/html/js/pet-manifest.js` | 生成物（勿手改）——每张内容哈希 + 总时长 |
| 同上（副本） | `public/app/html/…` | 仓库产物区——防下次部署前出现清单 / 素材错配 |

🔴 **运行区不再直写**——前端静态资源统一由 `mau-setup mode=sync-html` 同步（源区 → 产物区 + 运行区）。
核销三区一致用 `verify_frontend.py`。

## 口径

- **画面统一**：所有帧按全局联合 bbox（`BOX`）裁到同一画布 → 切换不跳。
- **透明区外扩**：透明区取最近可见像素色 → 防缩放时黑边。
- **边缘羽化**：圆角矩形 SDF（`EDGE_PX` 羽化宽 / `CORNER_R` 圆角半径）——内部恒为 1，主体零影响。
- **帧时长**：WebP 会合并完全相同的帧（把时长累加到保留帧）——总时长 ≠ 帧数 × 每帧时长；清单如实取 ANMF 解出的真实总时长。

## 维护提示

- **母本进 git**（莎 2026-10-14 拍板）——`src/<皮肤>/*.gif` 是母本，webp 由它派生。⚠ **git 历史永久**：日后重导素材会再堆体积，只提交「采用的母本」，中间稿留 WorkSpace。
- **换素材后帧号失效**：`convert_pet.py` 的 `TRIM` 按帧号，换素材须重跑 `match_pet_seam.py` 复算。
- **多皮肤未接线**：`src` 下已备 `deepdeep`，但前端仍是单皮肤平铺布局——多皮肤切换是后续轮（样式池），本轮只迁生产线。
- **画布不同**：`BOX` 暂为全局常量；若某套素材画布不同，落地多皮肤时提为 per-skin 配置。
