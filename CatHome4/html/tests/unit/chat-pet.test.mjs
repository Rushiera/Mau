// tests/unit/chat-pet.test.mjs —— 桌宠渲染单元测试（chat-pet.js）
// 覆盖：六态映射 · before-tool 前置 · before-idle 收尾 · 基础待机四选一 → 8s 直进入睡 · 移入 hook
//       · 点击 after-check 随机 · 瞬切/溶解规则 · 活跃态不响应鼠标 · 拖动改位 · 断线 · 资源清单一致
import { readFile, readdir } from 'node:fs/promises';
import { JSDOM } from 'jsdom';
import vm from 'node:vm';
import { expect, test, beforeAll, beforeEach, afterEach, vi } from 'vitest';

let petBox;
let imgA;
let imgB;

beforeAll(async () => {
  const html = await readFile(new URL('../../chat.html', import.meta.url), 'utf-8');
  const dom = new JSDOM(html, { url: 'http://127.0.0.1:8080', runScripts: 'outside-only' });
  globalThis.window = globalThis;
  globalThis.document = dom.window.document;
  globalThis.Node = dom.window.Node;
  globalThis.HTMLElement = dom.window.HTMLElement;
  globalThis.Event = dom.window.Event;
  globalThis.MouseEvent = dom.window.MouseEvent;
  globalThis.Image = dom.window.Image;
  globalThis.fetch = undefined;   // 预加载短路——jsdom 无 blob 通道，避免相对 URL 请求噪音（blob 分支由专门用例覆盖）
  const mods = ['ui-common.js', 'chat-md.js', 'chat-cmd.js', 'chat-view.js', 'chat-core.js', 'chat-note.js', 'chat-pet.js'];
  for (const m of mods) {
    const code = await readFile(new URL('../../js/' + m, import.meta.url), 'utf-8');
    vm.runInThisContext(code, { filename: m });
  }
  petBox = document.getElementById('chatPet');
  imgA = document.getElementById('chatPetImgA');
  imgB = document.getElementById('chatPetImgB');
  window.chatPetInit();
});

beforeEach(() => {
  vi.useFakeTimers();
  window.chatPetImgs = [imgA, imgB];
  window.chatPetLayer = 0;
  window.chatPetCur = '';
  window.chatPetMode = '';
  window.chatPetKey = '';
  window.chatPetKeyAt = 0;
  window.chatPetIdleMode = '';
  window.chatPetIdleIdx = -1;
  window.chatPetCheckIdx = -1;
  window.chatPetHovered = false;
  window.chatPetOffline = false;
  window.chatPetDrag = null;
  window.chatPetDragMoved = false;
  window.chatPetLastInstant = false;
  window.chatPetRes = {};
  window.chatPetManifest = {};
  window.chatPetLoading = false;
  window.chatPetBooted = true;
  window.chatPetFail = 0;
  if (window.chatPetTimer) { clearTimeout(window.chatPetTimer); }
  window.chatPetTimer = null;
  window.chatRunState = { state: '', ms: {}, requests: 0 };
  window.chatState = 'sending';
  imgA.setAttribute('src', '');
  imgB.setAttribute('src', '');
  imgA.style.opacity = '0';
  imgB.style.opacity = '0';
  petBox.style.left = '';
  petBox.style.top = '';
  petBox.style.right = '';
  petBox.style.bottom = '';
});

afterEach(() => {
  vi.useRealTimers();
  vi.restoreAllMocks();
});

function cur() {
  return window.chatPetCur;
}

const IDLE_BASE = ['loop-idle'];

function pushState(state, requests) {
  window.chatOnSessionState({ sessionId: 'majordomo', runState: state, runMs: {}, requests: requests || 0 });
}

// ── 运行态映射 ──
test('五态映射到对应循环资源', () => {
  const cases = [['link', 'loop-link'], ['wait', 'loop-wait'], ['think', 'loop-think'], ['run', 'loop-run'], ['reply', 'loop-reply']];
  for (const item of cases) {
    pushState(item[0], 1);
    expect(cur()).toBe(item[1]);
  }
});

test('wait 只有一个循环资源（loop-wait2 已退役）', () => {
  expect(window.chatPetFiles.indexOf('loop-wait2')).toBe(-1);
  pushState('wait', 1);
  vi.advanceTimersByTime(60000);
  expect(cur()).toBe('loop-wait');
});

// ── before-tool 前置 ──
test('进入 tool 直接切 loop-tool（before-tool 前置已废弃）', () => {
  pushState('think', 1);
  expect(cur()).toBe('loop-think');
  pushState('tool', 1);
  expect(cur()).toBe('loop-tool');
  expect(window.chatPetLastInstant).toBe(false);   // 态间切换走溶解
});

test('tool 内重复推送不重播', () => {
  pushState('think', 1);
  pushState('tool', 1);
  expect(cur()).toBe('loop-tool');
  pushState('tool', 1);
  pushState('tool', 1);
  expect(cur()).toBe('loop-tool');
});

// ── before-idle 收尾 ──
test('活跃态回空闲先播 before-idle，再进基础待机（接缝走溶解）', () => {
  pushState('reply', 1);
  expect(cur()).toBe('loop-reply');
  pushState('idle', 0);
  expect(cur()).toBe('before-idle');
  vi.advanceTimersByTime(1980);
  expect(window.chatPetLastInstant).toBe(false);   // loop-idle* 在溶解名单
  expect(IDLE_BASE).toContain(cur());
});

test('初载（从未进过活跃态）直接进基础待机，不播 before-idle', () => {
  pushState('idle', 0);
  expect(IDLE_BASE).toContain(cur());
  expect(window.chatPetLastInstant).toBe(false);
});

// ── 基础待机 → 入睡 ──
test('基础待机单张（素材合并后唯一项）', () => {
  pushState('idle', 0);
  expect(cur()).toBe('loop-idle');
});

test('基础待机 5s → before-idle-sleep 播 2 遍 → 入睡', () => {
  const spy = vi.spyOn(Math, 'random').mockReturnValue(0.1);
  pushState('idle', 0);
  expect(cur()).toBe('loop-idle');
  vi.advanceTimersByTime(5000);
  expect(cur()).toBe('before-idle-sleep');
  vi.advanceTimersByTime(3960);
  expect(cur()).toBe('loop-idle-sleep');
  expect(window.chatPetLastInstant).toBe(false);   // 接缝走溶解
});

test('睡眠中鼠标移入 → 唤醒进 hook', () => {
  pushState('idle', 0);
  vi.advanceTimersByTime(8960);
  expect(cur()).toBe('loop-idle-sleep');
  petBox.dispatchEvent(new Event('mouseenter'));
  expect(cur()).toBe('before-idle-hook');
});

// ── 移入 hook ──
test('鼠标移入 → before-idle-hook → loop-idle-hook（接缝走溶解）；移出回基础待机', () => {
  pushState('idle', 0);
  petBox.dispatchEvent(new Event('mouseenter'));
  expect(cur()).toBe('before-idle-hook');
  vi.advanceTimersByTime(1140);
  expect(cur()).toBe('loop-idle-hook');
  expect(window.chatPetLastInstant).toBe(false);
  petBox.dispatchEvent(new Event('mouseleave'));
  expect(IDLE_BASE).toContain(cur());
});

test('hook 期间不进入睡（移出后重新起 8s 周期）', () => {
  pushState('idle', 0);
  petBox.dispatchEvent(new Event('mouseenter'));
  vi.advanceTimersByTime(1140);
  vi.advanceTimersByTime(20000);
  expect(cur()).toBe('loop-idle-hook');
  petBox.dispatchEvent(new Event('mouseleave'));
  vi.advanceTimersByTime(5000);
  expect(cur()).toBe('before-idle-sleep');
});

// ── 点击 after-check ──
test('点击 → after-idle-check 之一（瞬切）→ 播完按悬停回 hook', () => {
  pushState('idle', 0);
  petBox.dispatchEvent(new Event('mouseenter'));
  vi.advanceTimersByTime(1140);
  expect(cur()).toBe('loop-idle-hook');
  petBox.dispatchEvent(new MouseEvent('click', { bubbles: true }));
  expect(cur()).toContain('after-idle-check');
  expect(window.chatPetLastInstant).toBe(true);
  vi.advanceTimersByTime(900);
  expect(cur()).toBe('before-idle-hook');
});

test('点击反应在鼠标已移开时回到基础待机', () => {
  pushState('idle', 0);
  petBox.dispatchEvent(new Event('mouseenter'));
  vi.advanceTimersByTime(1140);
  petBox.dispatchEvent(new MouseEvent('click', { bubbles: true }));
  const name = cur();
  petBox.dispatchEvent(new Event('mouseleave'));
  vi.advanceTimersByTime(900);
  expect(cur()).not.toBe(name);
  expect(IDLE_BASE).toContain(cur());
});

// ── 活跃态优先 ──
test('活跃态不响应移入与点击', () => {
  pushState('reply', 1);
  petBox.dispatchEvent(new Event('mouseenter'));
  petBox.dispatchEvent(new MouseEvent('click', { bubbles: true }));
  expect(cur()).toBe('loop-reply');
  vi.advanceTimersByTime(20000);
  expect(cur()).toBe('loop-reply');
});

test('活跃态打断空闲调度（陈旧定时器不改图）', () => {
  pushState('idle', 0);
  vi.advanceTimersByTime(4000);
  pushState('think', 1);
  expect(cur()).toBe('loop-think');
  vi.advanceTimersByTime(60000);
  expect(cur()).toBe('loop-think');
});

// ── 溶解 / 瞬切 ──
test('普通态切换走溶解（非瞬切）', () => {
  pushState('think', 1);
  pushState('run', 1);
  expect(cur()).toBe('loop-run');
  expect(window.chatPetLastInstant).toBe(false);
});

test('同值不切换层（防重复溶解）', () => {
  pushState('think', 1);
  const layer = window.chatPetLayer;
  const pair = imgA.getAttribute('src') + '|' + imgB.getAttribute('src');
  pushState('think', 1);
  expect(window.chatPetLayer).toBe(layer);
  expect(imgA.getAttribute('src') + '|' + imgB.getAttribute('src')).toBe(pair);
});

// ── 拖动 ──
test('拖动改变位置且不误触摸头', () => {
  pushState('idle', 0);
  petBox.dispatchEvent(new MouseEvent('mousedown', { clientX: 500, clientY: 500, bubbles: true }));
  document.dispatchEvent(new MouseEvent('mousemove', { clientX: 400, clientY: 400, bubbles: true }));
  document.dispatchEvent(new MouseEvent('mouseup', { bubbles: true }));
  expect(petBox.style.left).toBe('0px');
  expect(petBox.style.right).toBe('auto');
  petBox.dispatchEvent(new MouseEvent('click', { bubbles: true }));
  expect(cur()).not.toContain('after-idle-check');
});

test('微抖不进入拖动（点击仍算摸头）', () => {
  pushState('idle', 0);
  petBox.dispatchEvent(new MouseEvent('mousedown', { clientX: 500, clientY: 500, bubbles: true }));
  document.dispatchEvent(new MouseEvent('mousemove', { clientX: 501, clientY: 500, bubbles: true }));
  document.dispatchEvent(new MouseEvent('mouseup', { bubbles: true }));
  petBox.dispatchEvent(new MouseEvent('click', { bubbles: true }));
  expect(cur()).toContain('after-idle-check');
});

// ── 断线 ──
test('SSE 断线显错误资源，恢复后回态图', () => {
  pushState('think', 1);
  window.chatPetSetOffline(true);
  expect(cur()).toBe('loop-sseErr');
  window.chatPetSetOffline(false);
  expect(cur()).toBe('loop-think');
});

// ── 素材持有（blob 优先） ──
test('素材持有命中时用 blob URL（断线零网络依赖）', () => {
  window.chatPetRes = { 'loop-think': 'blob:mock-think', 'loop-run': 'blob:mock-run' };
  pushState('think', 1);
  expect([imgA.getAttribute('src'), imgB.getAttribute('src')]).toContain('blob:mock-think');
  pushState('run', 1);
  expect([imgA.getAttribute('src'), imgB.getAttribute('src')]).toContain('blob:mock-run');
});

test('素材未就绪时回落网络路径', () => {
  window.chatPetRes = {};
  pushState('think', 1);
  expect([imgA.getAttribute('src'), imgB.getAttribute('src')]).toContain('pet/loop-think.webp');
});

// ── 加载门禁（串行加载 + 首图启动） ──
test('加载中不调度——门禁期内状态同步不改图', () => {
  window.chatPetLoading = true;
  pushState('think', 1);
  expect(cur()).toBe('');
  expect(imgA.getAttribute('src')).toBe('');
});

test('加载序——首图 loop-sseErr 且覆盖全量清单', () => {
  const order = window.chatPetLoadOrder();
  expect(order[0]).toBe('loop-sseErr');
  expect(order.length).toBe(window.chatPetFiles.length);
  expect(order.slice().sort()).toEqual(Array.from(window.chatPetFiles).sort());
});

test('首图就位 → 桌宠显形并常显 sseErr（加载态）', () => {
  window.chatPetBooted = false;
  window.chatPetRes = { 'loop-sseErr': 'blob:mock-sse' };
  window.chatPetBoot();
  expect(cur()).toBe('loop-sseErr');
  expect(window.chatPetMode).toBe('loading');
});

test('首图缺位 → 保持不可见（不挂破图）', () => {
  window.chatPetBooted = false;
  window.chatPetRes = {};
  window.chatPetBoot();
  expect(cur()).toBe('');
  expect(window.chatPetMode).toBe('loading');
});

test('全量就绪 → 解除门禁并按当前态切图', () => {
  window.chatPetLoading = true;
  window.chatPetFail = 0;
  window.chatRunState = { state: 'think', ms: {}, requests: 1 };
  window.chatPetLoadDone();
  expect(window.chatPetLoading).toBe(false);
  expect(cur()).toBe('loop-think');
});

// ── 素材 URL 版本键（内容哈希） ──
test('素材 URL 带内容哈希——改图即换 URL，缓存自动失效', () => {
  window.chatPetManifest = { 'loop-think': { h: 'a3f9c2e1', ms: 980 } };
  expect(window.chatPetUrl('loop-think')).toBe('pet/loop-think.webp?v=a3f9c2e1');
});

test('素材时长取 manifest——免前端常量与素材漂移', () => {
  window.chatPetManifest = { 'before-tool': { h: 'aaaa1111', ms: 2640 } };
  expect(window.chatPetMs('before-tool', 999)).toBe(2640);
  expect(window.chatPetMs('before-idle', 1980)).toBe(1980);   // 缺项回落兜底
  expect(window.chatPetMs('before-idle', 0)).toBe(0);
});

test('清单缺项 → 回落无版本 URL', () => {
  window.chatPetManifest = {};
  expect(window.chatPetUrl('loop-run')).toBe('pet/loop-run.webp');
});

test('持有表未命中时切图用带版本网络 URL', () => {
  window.chatPetManifest = { 'loop-think': { h: 'beef1234', ms: 980 } };
  window.chatPetRes = {};
  pushState('think', 1);
  expect([imgA.getAttribute('src'), imgB.getAttribute('src')]).toContain('pet/loop-think.webp?v=beef1234');
});

// ── 硬保底：清理缓存按钮 ──
test('清理缓存——无缓存环境走提示且不抛错', () => {
  expect(typeof window.chatPetClearCache).toBe('function');
  window.chatPetClearCache();
  expect(document.getElementById('chatInfo').textContent).toBe('本环境不支持缓存');
});

// ── 接缝溶解名单 ──
test('接缝溶解名单——覆盖全部 before→loop 对（loop-tool 已废弃不在名单）', () => {
  expect(window.CHAT_PET_SEAM_BLEND['loop-tool']).toBeUndefined();
  expect(window.CHAT_PET_SEAM_BLEND['loop-idle-sleep']).toBe(true);
  expect(window.CHAT_PET_SEAM_BLEND['loop-idle-hook']).toBe(true);
  expect(window.CHAT_PET_SEAM_BLEND['loop-idle']).toBe(true);
});

test('名单内的 loop-idle-hook 走溶解', () => {
  pushState('idle', 0);
  petBox.dispatchEvent(new Event('mouseenter'));
  vi.advanceTimersByTime(1140);
  expect(cur()).toBe('loop-idle-hook');
  expect(window.chatPetLastInstant).toBe(false);
});

// ── 骨架与资源 ──
test('桌宠容器与双层图片元素存在', () => {
  expect(petBox).not.toBeNull();
  expect(imgA).not.toBeNull();
  expect(imgB).not.toBeNull();
});

test('预加载清单与 html/pet 目录一致', async () => {
  const files = await readdir(new URL('../../pet/', import.meta.url));
  const onDisk = files.filter(function (f) { return f.endsWith('.webp'); })
    .map(function (f) { return f.replace('.webp', ''); }).sort();
  const listed = Array.from(window.chatPetFiles).sort();
  expect(listed).toEqual(onDisk);
});
