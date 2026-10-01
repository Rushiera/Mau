// tests/unit/panel-cats.test.mjs —— 主面板多猫行（2026-10-01 主面板轮）
// 覆盖：列结构（ID 列撤除）· 名称行内编辑（cat.cfg.set displayName；未改动 / 清空不提交）
//       · LLM API / QQ Bot 下拉 · 目录白名单无权目录展示 + 专用弹层入口 · 工具清单单按钮
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

const CAT_ROW = {
  id: '33333333-3333-3333-3333-333333333333',
  name: '测试猫',
  running: false,
  port: -1,
  apiConfigId: '',
  qqbotId: '',
  enabledRoots: ['ccbp']
};

const ROOTS = [
  { id: 'workspace', path: 'D:\\Mau\\WorkSpace', writable: true },
  { id: 'ccbp', path: 'D:\\Mau\\CatCatBigParty', writable: true },
  { id: 'mau', path: 'D:\\Mau\\mau', writable: true }
];

let calls = [];

// 抓取 fetch——记录 url + body；回执固定 ok
function captureFetch() {
  calls = [];
  globalThis.fetch = async (url, opt) => {
    let body = null;
    if (opt && opt.body) { body = JSON.parse(opt.body); }
    calls.push({ url: url, body: body });
    return { json: async () => ({ ok: true }) };
  };
}

function callsTo(url) {
  return calls.filter((c) => c.url === url);
}

// 渲染单行——清空表体后调 renderCatRow（池数据由用例预设）
function renderRow(cat) {
  window.catApiOptions = [{ apiConfigId: 'aaaaaaaa-0000-0000-0000-000000000000', displayName: '主端点', isDefault: true }];
  window.catQqBotOptions = [{ qqBotId: 'bbbbbbbb-0000-0000-0000-000000000000', displayName: '露雪娜' }];
  window.catAllRoots = ROOTS;
  document.querySelector('#catsTable tbody').textContent = '';
  window.renderCatRow(cat);
  return document.querySelector('#catsTable tbody tr');
}

beforeAll(async () => {
  const html = await readFile(new URL('../../index.html', import.meta.url), 'utf-8');
  installDom(html);
  installMockEventSource();
  installMockFetch();
  await runGlobalScript(new URL('../../js/ui-common.js', import.meta.url));
  await runGlobalScript(new URL('../../js/app.js', import.meta.url));
  await runGlobalScript(new URL('../../js/panel.js', import.meta.url));
  await runGlobalScript(new URL('../../js/panel-catcfg.js', import.meta.url));
  await tick();
});

test('主面板行——8 列（ID 列已撤）；名称可编辑、API/QQBot 是下拉、工具清单是按钮', () => {
  const tr = renderRow(CAT_ROW);
  expect(tr.children.length).toBe(8);
  expect(tr.children[0].querySelector('input').value).toBe('测试猫');
  expect(tr.children[1].querySelector('select')).not.toBeNull();
  expect(tr.children[2].querySelector('select')).not.toBeNull();
  expect(tr.children[4].querySelector('button').textContent).toBe('工具清单');
  // ID 不再出现在任何单元格
  const texts = [];
  for (let i = 0; i < tr.children.length; i = i + 1) { texts.push(tr.children[i].textContent); }
  expect(texts.join('|').indexOf(CAT_ROW.id)).toBe(-1);
});

test('目录白名单列——显示无权目录（池 ∖ 启用根），点「设置」开专用弹层', async () => {
  const tr = renderRow(CAT_ROW);   // enabledRoots = ['ccbp'] → 无权仅 mau（workspace 为系统根不计）
  const td = tr.children[3];
  expect(td.textContent).toContain('无权: mau');
  expect(td.textContent.indexOf('ccbp')).toBe(-1);
  td.querySelector('button').onclick();
  await tick();
  expect(document.getElementById('catRootsModal').style.display).toBe('flex');
  document.getElementById('catRootsModal').style.display = 'none';
});

test('目录白名单列——启用根为空 = 空白名单（非系统根全部无权）', () => {
  const tr = renderRow(Object.assign({}, CAT_ROW, { enabledRoots: [] }));
  const td = tr.children[3];
  expect(td.textContent).toContain('无权: ccbp, mau');
});

test('名称行内编辑——改动提交 cat.cfg.set displayName；未改动 / 清空不提交', async () => {
  captureFetch();
  const tr = renderRow(CAT_ROW);
  const input = tr.children[0].querySelector('input');

  input.dispatchEvent(new Event('change'));
  await tick();
  expect(callsTo('/api/v1/command').length).toBe(0);

  input.value = '  ';
  input.dispatchEvent(new Event('change'));
  expect(input.value).toBe('测试猫');
  await tick();
  expect(callsTo('/api/v1/command').length).toBe(0);

  input.value = '新名字';
  input.dispatchEvent(new Event('change'));
  await tick();
  const cmds = callsTo('/api/v1/command');
  expect(cmds.length).toBe(1);
  expect(cmds[0].body.text).toBe('cat.cfg.set ' + CAT_ROW.id + ' displayName 新名字');
});
