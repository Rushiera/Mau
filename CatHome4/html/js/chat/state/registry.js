// ═══════════════════════════════════════════
// chat/state/registry.js —— state 段分发表（契约 §12.2 ①）
//
// 定位：state 段投影的**唯一映射处**——「投影件 → 消费字段 / 输出面 / 入口」一行一个；
//       `state.js` 只做整段覆盖 + 按本表分发（薄层）。
//
// 同构：与 blocks（`registry.js` 的 `BLOCK_DECL` + 两张渲染表）· fx（`fx/registry.js` 的 `FX_FEATURES`）
//       同一形态——可独立变化的东西一件 + 一张声明表；**新增字段 = 加一件 + 表加一行**。
//
// 字段面：契约 §12.2 ① = 后端 `ChatSession.BuildStateJson` 九字段——
//         `sessionId` / `runState` / `runMs` / `requests` / `note` / `delay` / `conn` / `tokens` / `meta`；
//         本表各行 `fields` 的并集**覆盖**该清单（每字段至少一件声明消费——不另立第二份字段清单；
//         覆盖性由门禁用例断言 `tests/unit/chat-state.test.mjs`）。`sessionId`（猫 key）当前无显示位
//         ——会话标识显示走 `meta.displayName`（A201）。
//
// 判据：每件只读自己声明的 `fields`、只写自己声明的 `output`——不跨面写别人的 DOM。
//
// 加载：chat.html 脚本清单段 ③b（件在前、表在段末，同 fx 段范式）。表持**入口名**（字符串）——
//       件在表前或表后加载皆可，解析发生在分发时刻；入口缺失出声（失败必须可见），不静默跳过。
//
// 🔴 新增字段改法（A194 后）：`state/<件>.js` 加一件 + 本表加一行——`state.js` 与分发器不动。
// ═══════════════════════════════════════════

/// state 投影分发表——一行一件
/// id 短标识（件名）· name 中文名 · owner 面归属（`state` 契约面投影 / `fx` 独立功能消费——A176 面界）
/// fields 消费的 state 段字段 · output 输出面 · apply 投影入口名（`function(state)` → void）
var STATE_DECL = [
    {
        id: 'status', name: '六态状态条', owner: 'state',
        fields: ['runState', 'runMs', 'requests'],
        output: '#chatStatus（六态时长 + ⏱ 总 + 🔄 Api 次数——容器唯一写者）',
        apply: 'stateRenderStatus'
    },
    {
        id: 'info', name: '顶栏信息位', owner: 'state',
        fields: ['tokens', 'meta'],
        output: '#chatInfo（前文条数 / 前文长度 / 前文字符数 / 会话级消耗与命中率——经信息位单点 chatInfoSet）',
        apply: 'stateRenderInfo'
    },
    {
        id: 'meta', name: '会话标识', owner: 'state',
        fields: ['meta'],
        output: 'document.title（浏览器标题）+ #chatTitle（左上角）——displayName',
        apply: 'stateRenderMeta'
    },
    {
        id: 'note', name: 'Note 投影', owner: 'state',
        fields: ['note'],
        output: 'Note 面板与气泡（note.js——数据源入段后为纯显示）',
        apply: 'stateRenderNote'
    },
    {
        id: 'delay', name: '定时面板', owner: 'state',
        fields: ['delay'],
        output: '定时面板列表（delay.js——纯显示，写面走指令总线）',
        apply: 'stateRenderDelay'
    },
    {
        id: 'conn', name: '连接健康', owner: 'state',
        fields: ['conn'],
        output: '#chatStatus 尾标（🔄 重启中 / 👥 N——不含断线可见性，那归本地信号）',
        apply: 'stateRenderConn'
    },
    {
        id: 'controls', name: '按钮态', owner: 'fx',
        fields: ['runState'],
        output: '动作按钮 disabled 态（fx/controls——外观层派生，契约 §12.8 登记行）',
        apply: 'fxControlsApply'
    },
    {
        id: 'pet', name: '桌宠同步', owner: 'fx',
        fields: ['runState'],
        output: '#chatPet 动画（fx/pet——外观层派生，契约 §12.8 登记行）',
        apply: 'chatPetSync'
    }
];

/// 整段分发——按表逐行调用投影入口
/// 失败可见：入口未声明出声（查脚本清单）· 单件异常隔离（一件坏不拖累其余）
/// @param {object} st state 段整段（`state.js` 覆盖后的 `appState`）
function stateProjectAll(st) {
    for (var i = 0; i < STATE_DECL.length; i = i + 1) {
        var row = STATE_DECL[i];
        var fn = (typeof window !== 'undefined' && typeof window[row.apply] === 'function') ? window[row.apply] : null;
        if (fn === null) {
            stateWarn('投影入口未声明：' + row.id + '（' + row.apply + '——查脚本清单）');
            continue;
        }
        try {
            fn(st);
        } catch (e) {
            stateWarn('投影失败：' + row.id, e);
        }
    }
}

/// 告警——失败可见（不静默吞；不抢信息位 `#chatInfo`，那是 state 段的输出口）
/// @param {string} msg 消息
/// @param {Error} [err] 异常对象
function stateWarn(msg, err) {
    if (typeof console !== 'undefined' && console.warn) {
        console.warn('[chat] ' + msg, err || '');
    }
}
