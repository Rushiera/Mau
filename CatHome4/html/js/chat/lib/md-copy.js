// ═══════════════════════════════════════════
// lib/md-copy.js —— MD 块复制按钮（代码块 / 表格右上角）
// 来源：chat-view.js 第 646-712 行（A81 MD 块复制原文）
// 分层：md.js 出结构（.md-copy 包裹 + data-md 源文本）→ 本层插按钮与绑定事件
// 取舍：保留整节；失败告警由 uiWarn（全局弹层）改为 console.warn（素材不引全局 UI 面）
// ═══════════════════════════════════════════

function mdCopySource(wrap) {
    // 源文本单一出口——data-md 优先（表格：含分隔行原文）；否则取代码块正文（pre code 的 textContent 天然等于原文）
    var src = wrap.getAttribute('data-md');
    if (src !== null && src !== undefined) { return src; }
    var code = wrap.querySelector('pre code');
    if (code) { return code.textContent; }
    return '';
}

function mdWriteClipboard(text) {
    // 剪贴板写入——navigator.clipboard 单一通道（127.0.0.1 / localhost 属安全上下文）
    if (typeof navigator === 'undefined' || !navigator.clipboard || typeof navigator.clipboard.writeText !== 'function') {
        return Promise.reject(new Error('clipboard 不可用'));
    }
    return navigator.clipboard.writeText(text);
}

function mdCopyIcon() {
    // 线条复制图标——双框（Feather 风格内联 SVG：零依赖、零字体差异、随 currentColor 取灰）
    var ns = 'http://www.w3.org/2000/svg';
    var svg = document.createElementNS(ns, 'svg');
    svg.setAttribute('viewBox', '0 0 24 24');
    svg.setAttribute('width', '12');
    svg.setAttribute('height', '12');
    svg.setAttribute('fill', 'none');
    svg.setAttribute('stroke', 'currentColor');
    svg.setAttribute('stroke-width', '2');
    svg.setAttribute('stroke-linecap', 'round');
    svg.setAttribute('stroke-linejoin', 'round');
    var back = document.createElementNS(ns, 'rect');
    back.setAttribute('x', '9');
    back.setAttribute('y', '9');
    back.setAttribute('width', '13');
    back.setAttribute('height', '13');
    back.setAttribute('rx', '2');
    var front = document.createElementNS(ns, 'path');
    front.setAttribute('d', 'M5 15H4a2 2 0 0 1-2-2V4a2 2 0 0 1 2-2h9a2 2 0 0 1 2 2v1');
    svg.appendChild(back);
    svg.appendChild(front);
    return svg;
}

function mdAppendCopyBtn(wrap) {
    // 复制按钮——线条图标（内联 SVG；零文本节点：不污染 textContent 断言与朗读）；成功切 done 类 1.2s 后复原
    var btn = el('button', 'md-copy-btn');
    btn.type = 'button';
    btn.title = '复制原文';
    btn.setAttribute('aria-label', '复制原文');
    btn.appendChild(mdCopyIcon());
    btn.addEventListener('click', function () {
        mdWriteClipboard(mdCopySource(wrap)).then(function () {
            btn.classList.add('done');
            window.setTimeout(function () { btn.classList.remove('done'); }, 1200);
        }).catch(function (e) { console.warn('复制原文失败', e); });
    });
    wrap.insertBefore(btn, wrap.firstChild);
}

function mdBindCopy(root) {
    // 整块渲染后一次性挂载——遍历 .md-copy 包裹
    var wraps = root.querySelectorAll('.md-copy');
    for (var i = 0; i < wraps.length; i = i + 1) {
        mdAppendCopyBtn(wraps[i]);
    }
}
