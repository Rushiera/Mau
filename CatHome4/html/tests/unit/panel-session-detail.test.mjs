// tests/unit/panel-session-detail.test.mjs —— 会话详情弹层（2026-10-01 会话状态卡详情轮）
// 覆盖：会话卡点击开弹层（cat-detail 拉取 + 上一条 user / 回复渲染）· 上一会话回落标注「上一会话」
//       · 留档按钮展开全文 · /new 确认弹层（Idle=直接可确定；非 Idle=警示 + 勾选后方可确定）· 确定投递 cat.new-session
import { readFile } from 'node:fs/promises';
import vm from 'node:vm';
import { expect, test, beforeAll } from 'vitest';
import { installDom, installMockEventSource, installMockFetch } from './mock-env.js';

// 加载无导出脚本到全局（vm.runInThisContext——var/function 声明挂 globalThis）
async function runGlobalScript(url) {
  const code = await readFile(url, 'utf-8');
  vm.runInThisContext(code, { filename: url.toString() });
}

// 一拍——等 mock fetch 的 promise 链走完
function tick() {
  return new Promise((resolve) => setTimeout(resolve, 0));
}

let calls = [];
let detail = null;

// 抓取 fetch——记录 url + body；cat-detail 回 detail，command 固定 ok
function captureFetch() {
  calls = [];
  globalThis.fetch = async (url, opt) => {
    let body = null;
    if (opt && opt.body) { body = JSON.parse(opt.body); }
    calls.push({ url: String(url), body: body });
    if (String(url).indexOf('/api/v1/cat-detail') === 0) {
      return { json: async () => detail };
    }
    if (String(url).indexOf('/api/v1/command') === 0) {
      return { json: async () => ({ ok: true, cmdId: 'cmd-1', frame: 1 }) };
    }
    return { json: async () => ({}) };
  };
}

const SESSION_ROW = {
  id: 'majordomo',
  name: 'majordomo',
  phase: 'Idle',
  round: 3,
  msgCount: 8,
  pending: 0,
  noteActive: false,
  context: 12345,
  contextCount: 42,
  lastContextChangeAt: 0
};

// 详情载荷——current 口径（本会话有消息）
function detailCurrent() {
  return {
    ok: true, cat: 'majordomo', name: 'majordomo', special: true, running: true, port: 8081,
    isIdle: true, phase: 'Idle', round: 3, msgCount: 8, pending: 0, noteActive: false,
    context: 12345, contextCount: 42,
    lastUser: { text: '你好猫猫', source: 'current', time: Date.now() - 60000, timeText: '' },
    lastReply: { text: '在的', source: 'current', time: Date.now() - 30000, timeText: '' },
    archive: { file: 'majordomo_20261001_120000.md', path: 'X:/sessions_old/majordomo_20261001_120000.md', text: '# 会话留档 — majordomo\n\n旧会话正文' }
  };
}

// 打开详情弹层——渲染一行会话卡后点击
async function openDetail(row) {
  captureFetch();
  window.renderSessions([row || SESSION_ROW]);
  document.querySelector('.session-card').dispatchEvent(new Event('click', { bubbles: true }));
  await tick();
  await tick();
}

beforeAll(async () => {
  const html = await readFile(new URL('../../index.html', import.meta.url), 'utf-8');
  installDom(html);
  installMockEventSource();
  installMockFetch();
  await runGlobalScript(new URL('../../js/ui-common.js', import.meta.url));
  await runGlobalScript(new URL('../../js/app.js', import.meta.url));
});

test('会话卡点击——拉取 cat-detail 并渲染上一条 user / 回复', async () => {
  detail = detailCurrent();
  await openDetail();
  expect(document.getElementById('catDetailModal').style.display).toBe('flex');
  expect(document.getElementById('catDetailTitle').textContent).toBe('majordomo');
  const body = document.getElementById('catDetailBody').textContent;
  expect(body).toContain('用户上一次的输入');
  expect(body).toContain('你好猫猫');
  expect(body).toContain('猫上一次的输入');
  expect(body).toContain('在的');
  // 拉取口径——cat 参数为会话 id
  expect(calls.filter((c) => c.url === '/api/v1/cat-detail?cat=majordomo').length).toBe(1);
  // 留档默认收起
  expect(body).not.toContain('旧会话正文');
});

test('上一会话回落——source=archive 标注「上一会话」', async () => {
  detail = detailCurrent();
  detail.lastUser = { text: '上一条旧输入', source: 'archive', time: 0, timeText: '10-01 12:00:00' };
  detail.lastReply = { text: '旧回复', source: 'archive', time: 0, timeText: '10-01 12:00:01' };
  await openDetail();
  const body = document.getElementById('catDetailBody').textContent;
  expect(body).toContain('上一条旧输入');
  expect(body).toContain('上一会话 · 10-01 12:00:00');
});

test('留档按钮——展开「上一个被销毁的会话内容」全文', async () => {
  detail = detailCurrent();
  await openDetail();
  document.getElementById('catDetailArchive').dispatchEvent(new Event('click'));
  const body = document.getElementById('catDetailBody').textContent;
  expect(body).toContain('上一个被销毁的会话内容');
  expect(body).toContain('旧会话正文');
  expect(body).toContain('majordomo_20261001_120000.md');
});

test('/info 按钮——展开状态字段（与 cat.info 同源）', async () => {
  detail = detailCurrent();
  await openDetail();
  document.getElementById('catDetailInfo').dispatchEvent(new Event('click'));
  const body = document.getElementById('catDetailBody').textContent;
  expect(body).toContain('/info — 状态');
  expect(body).toContain('isIdle = true');
  expect(body).toContain('port = 8081');
});

test('/new（Idle）——确认弹层无勾选项，确定即可投递 cat.new-session', async () => {
  detail = detailCurrent();
  await openDetail();
  document.getElementById('catDetailNew').dispatchEvent(new Event('click'));
  await tick();
  await tick();
  expect(document.getElementById('catNewModal').style.display).toBe('flex');
  expect(document.getElementById('catNewConfirmWrap').style.display).toBe('none');
  expect(document.getElementById('catNewGo').disabled).toBe(false);
  document.getElementById('catNewGo').dispatchEvent(new Event('click'));
  await tick();
  await tick();
  const cmds = calls.filter((c) => c.url === '/api/v1/command');
  expect(cmds.length).toBe(1);
  expect(cmds[0].body.text).toBe('cat.new-session majordomo');
});

test('/new（非 Idle）——警示 + 勾选「我确认」后方可确定', async () => {
  detail = detailCurrent();
  detail.isIdle = false;
  detail.phase = 'LlmRunning';
  await openDetail();
  document.getElementById('catDetailNew').dispatchEvent(new Event('click'));
  await tick();
  await tick();
  expect(document.getElementById('catNewText').textContent).toContain('可能还在运行');
  expect(document.getElementById('catNewConfirmWrap').style.display).toBe('flex');
  expect(document.getElementById('catNewGo').disabled).toBe(true);
  // 勾选前点击确定——不投递
  document.getElementById('catNewGo').dispatchEvent(new Event('click'));
  await tick();
  expect(calls.filter((c) => c.url === '/api/v1/command').length).toBe(0);
  // 勾选后确定——投递
  document.getElementById('catNewConfirm').checked = true;
  document.getElementById('catNewConfirm').dispatchEvent(new Event('change'));
  expect(document.getElementById('catNewGo').disabled).toBe(false);
  document.getElementById('catNewGo').dispatchEvent(new Event('click'));
  await tick();
  await tick();
  expect(calls.filter((c) => c.url === '/api/v1/command').length).toBe(1);
});

test('前文 tokens——会话卡在轮次前显示；详情状态行在运行态后显示（端口让位）', async () => {
  detail = detailCurrent();
  await openDetail();
  const status = document.querySelector('.cd-status').textContent;
  expect(status).toContain('运行中');
  expect(status).not.toContain(':8081');
  expect(status).toContain('前文 12345 tokens / 42 条');

  // 会话卡——tokens 在轮次之前
  const card = document.querySelector('.session-info').textContent;
  expect(card).toContain('前文 12345 tokens / 42 条');
  expect(card.indexOf('前文')).toBeLessThan(card.indexOf('轮次'));
});

test('端口入口——刷新左边可点直达对话窗口；静默猫隐藏', async () => {
  const opened = [];
  window.open = (url) => { opened.push(url); };
  detail = detailCurrent();
  await openDetail();
  const btn = document.getElementById('catDetailPort');
  expect(btn.style.display).toBe('');
  expect(btn.textContent).toBe(':8081');
  // 位置——紧邻刷新按钮左边
  expect(document.getElementById('catDetailReload').previousElementSibling).toBe(btn);
  btn.dispatchEvent(new Event('click'));
  expect(opened).toEqual(['http://127.0.0.1:8081/']);

  // 静默——端口入口隐藏
  detail = detailCurrent();
  detail.running = false;
  await openDetail();
  expect(document.getElementById('catDetailPort').style.display).toBe('none');
});

// ── 2026-10-01 易用性轮：按时间排序 / 停止 / 一行输入 / 遮罩关闭 ──

test('排序——用户最后输入晚于猫 → 用户块沉底', async () => {
  detail = detailCurrent();
  detail.lastUser = { text: '晚的输入', source: 'current', time: Date.now(), timeText: '' };
  detail.lastReply = { text: '早的回复', source: 'current', time: Date.now() - 10000, timeText: '' };
  await openDetail();
  const heads = document.querySelectorAll('#catDetailBody .cd-block-head');
  expect(heads.length).toBe(2);
  expect(heads[0].textContent).toContain('猫上一次的输入');
  expect(heads[1].textContent).toContain('用户上一次的输入');
});

test('排序——猫更晚时保持用户在上', async () => {
  detail = detailCurrent();
  await openDetail();
  const heads = document.querySelectorAll('#catDetailBody .cd-block-head');
  expect(heads[0].textContent).toContain('用户上一次的输入');
  expect(heads[1].textContent).toContain('猫上一次的输入');
});

test('停止按钮——/new 右边；非主干猫点击投递 cat.pause', async () => {
  detail = detailCurrent();
  detail.special = false;
  detail.cat = 'cat-a';
  await openDetail({ id: 'cat-a', name: '小猫', phase: 'Idle', round: 1, msgCount: 2, pending: 0 });
  const pause = document.getElementById('catDetailPause');
  expect(pause.style.display).not.toBe('none');
  expect(document.getElementById('catDetailNew').nextElementSibling).toBe(pause);
  calls.length = 0;
  pause.dispatchEvent(new Event('click'));
  await tick();
  await tick();
  const cmds = calls.filter((c) => c.url === '/api/v1/command');
  expect(cmds.length).toBe(1);
  expect(cmds[0].body.text).toBe('cat.pause cat-a');
  expect(document.getElementById('catDetailMsg').textContent).toBe('已投递停止本轮');
});

test('一行输入——发送投递 cat.chat，成功清空输入框', async () => {
  detail = detailCurrent();
  detail.special = false;
  await openDetail({ id: 'cat-a', name: '小猫' });
  const input = document.getElementById('catDetailInput');
  input.value = '  你好啊  ';
  calls.length = 0;
  document.getElementById('catDetailSend').dispatchEvent(new Event('click'));
  await tick();
  await tick();
  const cmds = calls.filter((c) => c.url === '/api/v1/command');
  expect(cmds.length).toBe(1);
  expect(cmds[0].body.text).toBe('cat.chat cat-a 你好啊');
  expect(input.value).toBe('');
  expect(document.getElementById('catDetailSendMsg').textContent).toBe('已发送');
});

test('一行输入——空内容不投递', async () => {
  detail = detailCurrent();
  detail.special = false;
  await openDetail({ id: 'cat-a', name: '小猫' });
  document.getElementById('catDetailInput').value = '   ';
  calls.length = 0;
  document.getElementById('catDetailSend').dispatchEvent(new Event('click'));
  await tick();
  expect(calls.filter((c) => c.url === '/api/v1/command').length).toBe(0);
  expect(document.getElementById('catDetailSendMsg').textContent).toBe('内容为空——未发送');
});

test('主干会话——停止入口隐藏 + 输入禁用', async () => {
  detail = detailCurrent();
  await openDetail();
  expect(document.getElementById('catDetailPause').style.display).toBe('none');
  expect(document.getElementById('catDetailInput').disabled).toBe(true);
  expect(document.getElementById('catDetailSend').disabled).toBe(true);
});

test('遮罩点击——点弹层外区域关闭；点内容区不关闭', async () => {
  detail = detailCurrent();
  await openDetail();
  const modal = document.getElementById('catDetailModal');
  expect(modal.style.display).toBe('flex');
  document.querySelector('#catDetailModal > div').dispatchEvent(new Event('click', { bubbles: true }));
  expect(modal.style.display).toBe('flex');
  modal.dispatchEvent(new Event('click'));
  expect(modal.style.display).toBe('none');
});
