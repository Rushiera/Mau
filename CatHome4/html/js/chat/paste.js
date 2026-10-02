// ═══════════════════════════════════════════
// chat/paste.js —— 图片粘贴上传与待发区（侧翼件 A172 · 规格 design-ch4-chat-images.md §五）
//
// 链路：粘贴（仅 image/*）→ 逐张上传（POST /api/v1/chat-images，原始字节）→ 待发区回显（本地 blob 预览）
//      → 发送时投递正文 + images 路径列表（**编号与包裹由后端在落前文时刻组装**——前端零路径知识）
// 边界：本件只管「上传 + 显示 + 路径列表」——包裹解析与缩略图渲染归 lib/images.js；发送归 input.js
// 失败面：上传失败不入待发区（防死路径进包裹）+ 待发区尾部可见提示（3s 自动消失）
// ═══════════════════════════════════════════

/// 待发图片顺序表：[{ path, url }]——path 供投递，url 供本地预览
var pendImgs = [];
/// 上传中计数——占位显示 + 发送时拒发（防漏图）
var pendUploading = 0;

/// 本地预览地址——blob URL；环境不支持时回落空串（预览降级，上传与投递不受影响）
function pendPreviewUrl(file) {
    try {
        if (typeof URL !== 'undefined' && typeof URL.createObjectURL === 'function') {
            return URL.createObjectURL(file);
        }
    } catch (e) {
        return '';
    }
    return '';
}

/// 预览地址释放——环境不支持时静默跳过
function pendRevokeUrl(url) {
    if (url && typeof URL !== 'undefined' && typeof URL.revokeObjectURL === 'function') {
        URL.revokeObjectURL(url);
    }
}

/// 粘贴拦截——仅图片项；纯文本粘贴不拦截（原样入框）
function pendOnPaste(e) {
    var dt = e.clipboardData;
    if (!dt) {
        return;
    }
    var files = [];
    var items = dt.items || [];
    for (var i = 0; i < items.length; i = i + 1) {
        if (items[i].kind === 'file' && items[i].type && items[i].type.indexOf('image/') === 0) {
            var f = items[i].getAsFile();
            if (f) {
                files.push(f);
            }
        }
    }
    if (files.length === 0) {
        return;
    }
    e.preventDefault();
    for (var k = 0; k < files.length; k = k + 1) {
        pendUpload(files[k]);
    }
}

/// 逐张上传——成功后入待发区；失败给可见提示且不入列（防死路径进包裹）
function pendUpload(file) {
    pendUploading = pendUploading + 1;
    pendRender();
    fetch('/api/v1/chat-images', {
        method: 'POST',
        headers: { 'Content-Type': file.type || 'image/png' },
        body: file
    })
        .then(function (r) {
            return r.json();
        })
        .then(function (d) {
            pendUploading = pendUploading - 1;
            if (!d || !d.ok || !d.path) {
                // 先重绘（清空区）再挂提示，避免提示被重绘冲掉
                pendRender();
                pendNotice((d && d.error) ? d.error : '图片上传失败');
                return;
            }
            pendImgs.push({ path: d.path, url: pendPreviewUrl(file) });
            pendRender();
        })
        .catch(function (err) {
            pendUploading = pendUploading - 1;
            pendRender();
            pendNotice('图片上传失败: ' + err);
        });
}

/// 失败可见化——待发区尾部提示（3 秒自动消失；不阻断）
function pendNotice(msg) {
    var box = document.getElementById('chatImages');
    if (!box) {
        return;
    }
    var d = document.createElement('span');
    d.className = 'chat-img-err';
    d.textContent = msg;
    box.appendChild(d);
    setTimeout(function () {
        if (d.parentNode) {
            d.parentNode.removeChild(d);
        }
    }, 3000);
}

/// 待发区渲染——本地 blob 预览（零服务端往返）+ 移除按钮 + 上传中占位
function pendRender() {
    var box = document.getElementById('chatImages');
    if (!box) {
        return;
    }
    box.textContent = '';
    for (var i = 0; i < pendImgs.length; i = i + 1) {
        box.appendChild(pendItem(pendImgs[i], i));
    }
    for (var k = 0; k < pendUploading; k = k + 1) {
        var w = document.createElement('span');
        w.className = 'chat-img-loading';
        w.textContent = '上传中…';
        box.appendChild(w);
    }
}

/// 待发项——缩略图 + 移除按钮
function pendItem(item, index) {
    var fig = document.createElement('figure');
    fig.className = 'chat-img';
    var img = document.createElement('img');
    img.src = item.url;
    img.alt = '待发图片';
    var del = document.createElement('button');
    del.type = 'button';
    del.className = 'chat-img-del';
    del.title = '移除这张图片（不发送）';
    del.textContent = '×';
    del.addEventListener('click', function () {
        pendRemove(index);
    });
    fig.appendChild(img);
    fig.appendChild(del);
    return fig;
}

/// 移除单张——释放预览地址
function pendRemove(index) {
    if (index < 0 || index >= pendImgs.length) {
        return;
    }
    pendRevokeUrl(pendImgs[index].url);
    pendImgs.splice(index, 1);
    pendRender();
}

/// 清空待发区——发送后调用（释放全部预览地址）
function pendClear() {
    for (var i = 0; i < pendImgs.length; i = i + 1) {
        pendRevokeUrl(pendImgs[i].url);
    }
    pendImgs = [];
    pendRender();
}

/// 路径列表——投递载荷（编号与包裹由后端组装）
function pendPaths() {
    var out = [];
    for (var i = 0; i < pendImgs.length; i = i + 1) {
        out.push(pendImgs[i].path);
    }
    return out;
}

/// 上传中判据——发送面用（上传未完成不发，防漏图）
function pendBusy() {
    return pendUploading > 0;
}

/// 接线——输入框粘贴拦截（本件加载于页面尾部，DOM 已就绪）
function pendInit() {
    var input = document.getElementById('chatSendInput');
    if (input) {
        input.addEventListener('paste', pendOnPaste);
    }
}

pendInit();
