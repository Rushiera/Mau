// tests/unit/chat-images.test.mjs —— 对话图片附件（A65）：包裹解析 / 渲染单一出口 / 粘贴上传 / 发送载荷
// 覆盖：严格门（段头+条目+段尾齐备）· 路径→取图 URL · user 泡与 assistant 泡渲染 · 取图失败回落
//       · 纯文本粘贴不拦截 · 上传成功入待发区 · 发送带 images 列表
// 规格：Project/CH4/design-ch4-chat-images.md §四 / §六
import { readFile } from 'node:fs/promises';
import { JSDOM } from 'jsdom';
import vm from 'node:vm';
import { expect, test, beforeAll, beforeEach } from 'vitest';

let chatMsgs;
let chatImagesBox;
let domWindow;

beforeAll(async () => {
  const html = await readFile(new URL('../../chat.html', import.meta.url), 'utf-8');
  const chatDom = new JSDOM(html, { url: 'http://127.0.0.1:8080', runScripts: 'outside-only' });
  domWindow = chatDom.window;
  globalThis.window = globalThis;
  globalThis.document = chatDom.window.document;
  globalThis.Node = chatDom.window.Node;
  globalThis.HTMLElement = chatDom.window.HTMLElement;
  const common = await readFile(new URL('../../js/ui-common.js', import.meta.url), 'utf-8');
  const md = await readFile(new URL('../../js/chat-md.js', import.meta.url), 'utf-8');
  const cmd = await readFile(new URL('../../js/chat-cmd.js', import.meta.url), 'utf-8');
  const tools = await readFile(new URL('../../js/chat-tools.js', import.meta.url), 'utf-8');
  const view = await readFile(new URL('../../js/chat-view.js', import.meta.url), 'utf-8');
  const core = await readFile(new URL('../../js/chat-core.js', import.meta.url), 'utf-8');
  const note = await readFile(new URL('../../js/chat-note.js', import.meta.url), 'utf-8');
  vm.runInThisContext(common, { filename: 'ui-common.js' });
  vm.runInThisContext(md, { filename: 'chat-md.js' });
  vm.runInThisContext(cmd, { filename: 'chat-cmd.js' });
  vm.runInThisContext(tools, { filename: 'chat-tools.js' });
  vm.runInThisContext(view, { filename: 'chat-view.js' });
  vm.runInThisContext(core, { filename: 'chat-core.js' });
  vm.runInThisContext(note, { filename: 'chat-note.js' });
  chatMsgs = document.getElementById('chatMsgs');
  chatImagesBox = document.getElementById('chatImages');
});

beforeEach(() => {
  window.chatState = 'idle';
  window.viewContainers = {};
  window.chatPending = [];
  window.chatImages = [];
  window.chatImageUploading = 0;
  window.chatInput.value = '';
  chatMsgs.textContent = '';
  chatImagesBox.textContent = '';
});

const ENVELOPE = '[image-open]\n图片92-1：C:\\Data\\chat-images\\a1b2.png\n图片92-2：C:\\Data\\chat-images\\c3d4.jpg\n[image-end]\n\n看这个';

test('chatImgSplit 合法包裹——条目与正文分离', () => {
  const r = window.chatImgSplit(ENVELOPE);
  expect(r.items.length).toBe(2);
  expect(r.items[0].ref).toBe('92-1');
  expect(r.items[0].path).toBe('C:\\Data\\chat-images\\a1b2.png');
  expect(r.items[1].ref).toBe('92-2');
  expect(r.body).toBe('看这个');
});

test('chatImgSplit 缺段尾——整段按普通文本', () => {
  const text = '[image-open]\n图片92-1：C:\\a.png\n看这个';
  const r = window.chatImgSplit(text);
  expect(r.items.length).toBe(0);
  expect(r.body).toBe(text);
});

test('chatImgSplit 缺段头 / 零条目——不命中', () => {
  expect(window.chatImgSplit('图片92-1：C:\\a.png\n[image-end]').items.length).toBe(0);
  expect(window.chatImgSplit('[image-open]\n[image-end]\n正文').items.length).toBe(0);
});

test('chatImgSplit 包裹内杂行或空行——不命中（严格门）', () => {
  expect(window.chatImgSplit('[image-open]\n说明一行\n图片1-1：C:\\a.png\n[image-end]').items.length).toBe(0);
  expect(window.chatImgSplit('[image-open]\n图片1-1：C:\\a.png\n\n[image-end]').items.length).toBe(0);
});

test('chatImgUrl 本地路径——绝对路径整串进端点 / 裸文件名走缓存分支', () => {
  expect(window.chatImgUrl('C:\\Data\\chat-images\\a1b2.png')).toBe('/api/v1/cache-image/C:/Data/chat-images/a1b2.png');
  expect(window.chatImgUrl('C:\\Users\\ASUS\\桌面\\图 1.png')).toBe('/api/v1/cache-image/C:/Users/ASUS/' + encodeURIComponent('桌面') + '/' + encodeURIComponent('图 1.png'));
  expect(window.chatImgUrl('a1b2.png')).toBe('/api/v1/cache-image/a1b2.png');
  expect(window.chatImgUrl('')).toBe('');
});

test('chatImgIsAbsolutePath——盘符 / UNC / 正斜杠根 判定', () => {
  expect(window.chatImgIsAbsolutePath('C:\\x\\a.png')).toBe(true);
  expect(window.chatImgIsAbsolutePath('C:/x/a.png')).toBe(true);
  expect(window.chatImgIsAbsolutePath('\\\\host\\share\\a.png')).toBe(true);
  expect(window.chatImgIsAbsolutePath('/x/a.png')).toBe(true);
  expect(window.chatImgIsAbsolutePath('mau:CatTemp/a.png')).toBe(false);
  expect(window.chatImgIsAbsolutePath('a1b2.png')).toBe(false);
});

test('chatUserFill 命中包裹——缩略图组 + 正文文本', () => {
  const bubble = document.createElement('div');
  window.chatUserFill(bubble, ENVELOPE);
  expect(bubble.querySelectorAll('.chat-imgs .chat-img').length).toBe(2);
  expect(bubble.querySelector('.chat-user-text').textContent).toBe('看这个');
  expect(bubble.querySelectorAll('.chat-img figcaption')[0].textContent).toBe('图片92-1');
});

test('chatUserFill 无包裹——与旧行为同构（纯文本，零回归）', () => {
  const bubble = document.createElement('div');
  window.chatUserFill(bubble, '普通消息');
  expect(bubble.textContent).toBe('普通消息');
  expect(bubble.querySelector('.chat-imgs')).toBe(null);
});

test('chatMdFill 命中包裹——图组 + MD 正文块；无包裹——单 md-block', () => {
  const withImg = document.createElement('div');
  window.chatMdFill(withImg, ENVELOPE);
  expect(withImg.querySelectorAll('.chat-imgs .chat-img').length).toBe(2);
  expect(withImg.querySelector('.md-block').textContent).toContain('看这个');
  const plain = document.createElement('div');
  window.chatMdFill(plain, '**加粗**');
  expect(plain.querySelectorAll('.md-block').length).toBe(1);
  expect(plain.querySelector('.chat-imgs')).toBe(null);
});

test('取图失败回落——原位显示编号与路径（不空白）', () => {
  const group = window.chatImageGroup([{ ref: '7-1', path: 'C:\\nope\\gone.png' }]);
  document.body.appendChild(group);
  const img = group.querySelector('img');
  img.dispatchEvent(new domWindow.Event('error'));
  const fig = group.querySelector('.chat-img');
  expect(fig.className).toContain('missing');
  expect(fig.textContent).toContain('图片7-1');
  expect(fig.textContent).toContain('C:\\nope\\gone.png');
});

test('chatOnPaste 纯文本——不拦截（不 preventDefault）', () => {
  let prevented = false;
  window.chatOnPaste({ clipboardData: { items: [{ kind: 'string', type: 'text/plain' }] }, preventDefault: function () { prevented = true; } });
  expect(prevented).toBe(false);
});

test('chatOnPaste 图片——拦截并上传入待发区', async () => {
  let prevented = false;
  let fetchCalls = 0;
  globalThis.fetch = async function () {
    fetchCalls = fetchCalls + 1;
    return { json: async function () { return { ok: true, name: 'a1b2.png', path: 'C:\\Data\\chat-images\\a1b2.png' }; } };
  };
  const file = new domWindow.File([1, 2, 3], 'a.png', { type: 'image/png' });
  const ev = {
    clipboardData: { items: [{ kind: 'file', type: 'image/png', getAsFile: function () { return file; } }] },
    preventDefault: function () { prevented = true; }
  };
  window.chatOnPaste(ev);
  expect(prevented).toBe(true);
  await new Promise(function (r) { setTimeout(r, 20); });
  expect(fetchCalls).toBe(1);
  expect(window.chatImages.length).toBe(1);
  expect(window.chatImages[0].path).toBe('C:\\Data\\chat-images\\a1b2.png');
  expect(chatImagesBox.querySelectorAll('.chat-img').length).toBe(1);
});

test('chatOnPaste 上传失败——不入待发区且提示可见', async () => {
  globalThis.fetch = async function () {
    return { json: async function () { return { ok: false, error: '不支持的图片类型：image/svg+xml' }; } };
  };
  const file = new domWindow.File([1], 'a.svg', { type: 'image/svg+xml' });
  window.chatOnPaste({
    clipboardData: { items: [{ kind: 'file', type: 'image/svg+xml', getAsFile: function () { return file; } }] },
    preventDefault: function () { }
  });
  await new Promise(function (r) { setTimeout(r, 0); });
  expect(window.chatImages.length).toBe(0);
  expect(chatImagesBox.querySelector('.chat-img-err')).not.toBe(null);
});

test('chatSend 带图片——载荷含 images 列表，待发区清空', async () => {
  let sent = null;
  globalThis.fetch = async function (url, opts) {
    sent = JSON.parse(opts.body);
    return { json: async function () { return { ok: true }; } };
  };
  window.chatImages = [{ path: 'C:\\Data\\chat-images\\a1b2.png', url: '' }];
  window.chatInput.value = '看这个';
  window.chatSend();
  expect(sent.text).toBe('Chat 看这个');
  expect(sent.images).toEqual(['C:\\Data\\chat-images\\a1b2.png']);
  expect(window.chatImages.length).toBe(0);
});

test('chatSend 仅图片无正文——仍可发送（包裹由后端组装）', async () => {
  let sent = null;
  globalThis.fetch = async function (url, opts) {
    sent = JSON.parse(opts.body);
    return { json: async function () { return { ok: true }; } };
  };
  window.chatImages = [{ path: 'C:\\Data\\chat-images\\a1b2.png', url: '' }];
  window.chatInput.value = '';
  window.chatSend();
  expect(sent.text).toBe('Chat ');
  expect(sent.images.length).toBe(1);
});

test('chatSend 上传中——不发送', async () => {
  let called = false;
  globalThis.fetch = async function () { called = true; return { json: async function () { return { ok: true }; } }; };
  window.chatImageUploading = 1;
  window.chatInput.value = '看这个';
  window.chatSend();
  expect(called).toBe(false);
});

test('工具卡 image-analyze——输入图片段（本地路径走取图端点单一出口）', () => {
  const body = window.chatToolBody({
    name: 'image-analyze',
    arguments: JSON.stringify({ path: 'C:\\Data\\chat-images\\a1b2.png', question: '这是什么' }),
    result: '一张测试图'
  });
  expect(body).not.toBe(null);
  let imgSeg = null;
  for (let i = 0; i < body.segs.length; i++) {
    if (body.segs[i].className.indexOf('seg-img') >= 0) { imgSeg = body.segs[i]; }
  }
  expect(imgSeg).not.toBe(null);
  expect(imgSeg.querySelector('summary').textContent).toContain('输入图片 · 1 张');
  expect(imgSeg.querySelector('img').getAttribute('src')).toBe('/api/v1/cache-image/C:/Data/chat-images/a1b2.png');
  expect(imgSeg.querySelector('figcaption').textContent).toBe('a1b2.png');
});

test('工具卡 image-analyze——http(s) 图片直通（不走取图端点）', () => {
  const body = window.chatToolBody({
    name: 'image-analyze',
    arguments: JSON.stringify({ path: 'https://x.test/a.png?v=2' }),
    result: 'ok'
  });
  let imgSeg = null;
  for (let i = 0; i < body.segs.length; i++) {
    if (body.segs[i].className.indexOf('seg-img') >= 0) { imgSeg = body.segs[i]; }
  }
  expect(imgSeg.querySelector('img').getAttribute('src')).toBe('https://x.test/a.png?v=2');
  expect(imgSeg.querySelector('figcaption').textContent).toBe('a.png');
});

test('工具卡 image-analyze——无 path 不产图片段（零回归）', () => {
  const body = window.chatToolBody({
    name: 'image-analyze',
    arguments: JSON.stringify({ question: '没有路径' }),
    result: 'ERR|BAD_ARGS|缺少参数 path'
  });
  for (let i = 0; i < body.segs.length; i++) {
    expect(body.segs[i].className.indexOf('seg-img')).toBe(-1);
  }
});

test('工具卡图片渲染不影响其他工具（覆盖表未声明 inputImages）', () => {
  const body = window.chatToolBody({
    name: 'text-read',
    arguments: JSON.stringify({ path: 'ccbp:L1/Tree.md' }),
    result: '正文'
  });
  for (let i = 0; i < body.segs.length; i++) {
    expect(body.segs[i].className.indexOf('seg-img')).toBe(-1);
  }
});

test('chatImgName / chatImgUrl——http(s) 直通与查询串剥离', () => {
  expect(window.chatImgUrl('https://x.test/a.png?v=2')).toBe('https://x.test/a.png?v=2');
  expect(window.chatImgUrl('http://x.test/b.jpg')).toBe('http://x.test/b.jpg');
  expect(window.chatImgName('https://x.test/a.png?v=2')).toBe('a.png');
  expect(window.chatImgName('C:\\Data\\chat-images\\c3d4.png')).toBe('c3d4.png');
});

test('chatImgUrl 受控根寻址——保留分隔符 + 逐段编码', () => {
  expect(window.chatImgUrl('mau:CatTemp/shot.png')).toBe('/api/v1/cache-image/mau:CatTemp/shot.png');
  expect(window.chatImgUrl('WorkSpace:out/shot 图.png')).toBe('/api/v1/cache-image/WorkSpace:out/shot%20%E5%9B%BE.png');
  expect(window.chatImgUrl('mau:CatTemp\\shot.png')).toBe('/api/v1/cache-image/mau:CatTemp/shot.png');
  expect(window.chatImgUrl('mau:a.png')).toBe('/api/v1/cache-image/mau:a.png');
});

test('chatImgIsRootAddress——盘符绝对路径不算根寻址', () => {
  expect(window.chatImgIsRootAddress('mau:CatTemp/a.png')).toBe(true);
  expect(window.chatImgIsRootAddress('mau:a.png')).toBe(true);
  expect(window.chatImgIsRootAddress('C:\\x\\a.png')).toBe(false);
  expect(window.chatImgIsRootAddress('C:/x/a.png')).toBe(false);
  expect(window.chatImgIsRootAddress('a1b2.png')).toBe(false);
});

test('工具卡 image-analyze——受控根路径走根寻址 URL（跑测产物可直接看）', () => {
  const body = window.chatToolBody({
    name: 'image-analyze',
    arguments: JSON.stringify({ path: 'WorkSpace:CatTemp/shot.png' }),
    result: 'ok'
  });
  let imgSeg = null;
  for (let i = 0; i < body.segs.length; i++) {
    if (body.segs[i].className.indexOf('seg-img') >= 0) { imgSeg = body.segs[i]; }
  }
  expect(imgSeg.querySelector('img').getAttribute('src')).toBe('/api/v1/cache-image/WorkSpace:CatTemp/shot.png');
  expect(imgSeg.querySelector('figcaption').textContent).toBe('shot.png');
});
