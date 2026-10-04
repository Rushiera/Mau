// ═══════════════════════════════════════════
// lib/dom.js —— DOM 构造小件
// 定位：素材层唯一 DOM 工具入口——只做「造节点 / 塞文本」，不含任何布局知识
// 纪律：文本一律 textContent（零 innerHTML）；不碰全局容器（挂载由调用方决定）
// ═══════════════════════════════════════════

function el(tag, cls) {
    // 造元素——可选类名
    var n = document.createElement(tag);
    if (cls) { n.className = cls; }
    return n;
}

function elText(tag, cls, text) {
    // 造元素 + 纯文本——高频形态单出口
    var n = el(tag, cls);
    n.textContent = (text === undefined || text === null) ? '' : String(text);
    return n;
}
