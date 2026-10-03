// ═══════════════════════════════════════════
// chat/fx/registry.js —— 独立功能声明表（A176）
//
// 定位：`fx/` 层的**唯一声明处**——每个独立功能登记一行（名称 / 输入源 / 输出面 / 启动入口）。
// 判据（设计 §1.2）：独立功能必须在此登记，否则不成立——未登记的自由实现 = 第五代残留发源地。
// 准入（须同时满足）：① 不承载业务效果（算错只错画面）② 不含指令输入 ③ 输入只读原始信息
// 消费：① `fxBoot()` 按表启动（启动时机单点）② 契约 §12「独立功能面」节的代码侧对应物（人读 + 门禁可查）
// 加载：chat.html 脚本清单 fx 段末件（须在各 fx 件之后——表持有各件的入口名）
// ═══════════════════════════════════════════

/// 独立功能声明表——每件一行
/// id 短标识 · name 中文名 · inputs 输入源（只读）· outputs 输出面 · init 启动入口（无则 null）
var FX_FEATURES = [
    {
        id: 'pet', name: '桌宠',
        inputs: ['state.runState', 'SSE 连接态（onopen / onerror）'],
        outputs: ['#chatPet 动画（双层交叉溶解）'],
        init: 'chatPetInit'
    },
    {
        id: 'scroll', name: '滚动带',
        inputs: ['对话区 DOM 变化', '滚轮事件'],
        outputs: ['#chatScrollBand（指示块 + 刻度）'],
        init: 'chatScrollInit'
    },
    {
        id: 'pending', name: '插话队列',
        inputs: ['发送时刻（input.js）', 'persist user 块到达（persist.js）'],
        outputs: ['#chatPendingPanel'],
        init: null
    },
    {
        id: 'cmdIntent', name: '命令解码显示',
        inputs: ['toolcard payload.name / arguments'],
        outputs: ['工具卡折叠行文案 · .cmd-intent 展开块'],
        init: null
    },
    {
        id: 'controls', name: '按钮态',
        inputs: ['state.runState'],
        outputs: ['动作按钮 disabled 态（停止 / 继续 / 开始 Note / 发送）'],
        init: null
    }
];

/// 启动——遍历表执行 init（无 init 的件零动作）；查空出声、异常隔离（一件坏不拖累其余）
function fxBoot() {
    for (var i = 0; i < FX_FEATURES.length; i = i + 1) {
        var f = FX_FEATURES[i];
        if (!f.init) {
            continue;
        }
        var fn = (typeof window !== 'undefined' && typeof window[f.init] === 'function') ? window[f.init] : null;
        if (fn === null) {
            fxWarn('独立功能未启动：' + f.id + '（入口 ' + f.init + ' 未声明——查脚本清单）');
            continue;
        }
        try {
            fn();
        } catch (e) {
            fxWarn('独立功能启动失败：' + f.id, e);
        }
    }
}

/// 告警——复用 console（不抢顶部状态位：启动期状态位尚未投影）
function fxWarn(msg, err) {
    if (typeof console !== 'undefined' && console.warn) {
        console.warn('[chat] ' + msg, err || '');
    }
}
