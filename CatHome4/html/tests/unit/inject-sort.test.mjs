// tests/unit/inject-sort.test.mjs —— 前文注入 List 编辑面（2026-09-14：目录条目 + 拖拽排序；2026-10-01：手动路径输入撤除，改选择式弹层）
// 覆盖：拖拽改序（最简原生实现）· 保存载荷顺序与列表一致 · 前文注入弹层（池=新猫默认模板默认前文；池外条目保留可移除）
//       · 猫配置弹层「选择前文」入口与保存回填
// 基座：与 smoke.test.mjs 同构（index.html DOM + 主面板五域脚本）
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

beforeAll(async () => {
  const html = await readFile(new URL('../../index.html', import.meta.url), 'utf-8');
  installDom(html);
  installMockEventSource();
  installMockFetch();
  await runGlobalScript(new URL('../../js/ui-common.js', import.meta.url));
  await runGlobalScript(new URL('../../js/app.js', import.meta.url));
  await runGlobalScript(new URL('../../js/panel.js', import.meta.url));
  await runGlobalScript(new URL('../../js/panel-apis.js', import.meta.url));
  await runGlobalScript(new URL('../../js/panel-catcfg.js', import.meta.url));
});

// 辅助——渲染清单并取行元素
function renderRows(list) {
  window.renderInjectList(list);
  return document.querySelectorAll('#catCfgInject > div');
}

test('拖拽排序——dragstart + drop 改序并重渲染', () => {
  const rows = renderRows(['ccbp:A.md', 'ccbp:B.md', 'ccbp:C.md']);
  expect(rows.length).toBe(3);
  expect(rows[0].draggable).toBe(true);
  // 第三行拖到第一行位置
  rows[2].dispatchEvent(new window.Event('dragstart', { bubbles: true }));
  rows[0].dispatchEvent(new window.Event('dragover', { bubbles: true }));
  rows[0].dispatchEvent(new window.Event('drop', { bubbles: true }));
  const after = document.querySelectorAll('#catCfgInject > div');
  expect(after.length).toBe(3);
  expect(after[0].textContent).toContain('ccbp:C.md');
  expect(after[1].textContent).toContain('ccbp:A.md');
  expect(after[2].textContent).toContain('ccbp:B.md');
});

test('保存载荷顺序与列表一致（目录条目原样透传）', async () => {
  let captured = null;
  globalThis.fetch = async (url, opt) => {
    if (opt && opt.body) { captured = JSON.parse(opt.body); }
    return { json: async () => ({ ok: true }) };
  };
  window.catCfgTarget = 'majordomo';
  renderRows(['ccbp:L2/Worker/Majordomo', 'ccbp:SOUL.md']);
  window.saveCatCfg();
  await new Promise((resolve) => setTimeout(resolve, 0));
  expect(captured).not.toBeNull();
  expect(captured.injectList).toEqual(['ccbp:L2/Worker/Majordomo', 'ccbp:SOUL.md']);
});

// 前文注入弹层 mock——池（新猫默认模板默认前文）+ 现值（该猫 injectList）；POST 一律回 ok
function mockInjectFetch(pool, current) {
  globalThis.fetch = async (url, opt) => {
    if (opt && opt.body) { return { json: async () => ({ ok: true }) }; }
    if (String(url).indexOf('/api/v1/cat-default') === 0) { return { json: async () => ({ ok: true, defaultInjectList: pool }) }; }
    if (String(url).indexOf('/api/v1/cat-config') === 0) { return { json: async () => ({ ok: true, injectList: current }) }; }
    return { json: async () => ({ ok: true }) };
  };
}

test('前文注入弹层——候选来自新猫默认模板池；手动路径输入已撤', async () => {
  // 手动输入行已撤（2026-10-01 易用性轮——改选择式）
  expect(document.getElementById('catCfgInjectAdd')).toBeNull();
  expect(document.getElementById('catCfgInjectAddBtn')).toBeNull();
  mockInjectFetch(['ccbp:L1/Tree.md', 'ccbp:L1/Wisdom.md', 'ccbp:SOUL.md'], ['ccbp:L1/Wisdom.md']);
  window.openCatInject('majordomo', '管家');
  await tick();
  expect(document.getElementById('catInjectModal').style.display).toBe('flex');
  const rows = document.querySelectorAll('#catInjectPool > label');
  expect(rows.length).toBe(3);
  expect(rows[0].querySelector('span').textContent).toBe('ccbp:L1/Tree.md');
  expect(rows[1].querySelector('input').checked).toBe(true);
  expect(rows[0].querySelector('input').checked).toBe(false);
  // 无池外条目——保留区不出现
  expect(document.getElementById('catInjectExtraWrap').style.display).toBe('none');
});

test('前文注入弹层——保存载荷 = 池内勾选（池序）+ 池外条目（原序）', async () => {
  let captured = null;
  globalThis.fetch = async (url, opt) => {
    if (opt && opt.body) { captured = JSON.parse(opt.body); return { json: async () => ({ ok: true }) }; }
    if (String(url).indexOf('/api/v1/cat-default') === 0) {
      return { json: async () => ({ ok: true, defaultInjectList: ['ccbp:A.md', 'ccbp:B.md', 'ccbp:C.md'] }) };
    }
    return { json: async () => ({ ok: true, injectList: ['ccbp:C.md', 'ccbp:A.md', 'ccbp:Z.md'] }) };
  };
  window.openCatInject('majordomo', '管家');
  await tick();
  const rows = document.querySelectorAll('#catInjectPool > label');
  rows[1].querySelector('input').checked = true;
  rows[1].querySelector('input').dispatchEvent(new Event('change'));
  window.saveCatInject();
  await tick();
  expect(captured).not.toBeNull();
  expect(captured.cat).toBe('majordomo');
  // 池内按池序（A / B / C）+ 池外 Z 保留在尾
  expect(captured.injectList).toEqual(['ccbp:A.md', 'ccbp:B.md', 'ccbp:C.md', 'ccbp:Z.md']);
});

test('前文注入弹层——池外条目单列保留（不静默丢）且可移除', async () => {
  mockInjectFetch(['ccbp:A.md'], ['ccbp:A.md', 'ccbp:Legacy.md']);
  window.openCatInject('majordomo', '管家');
  await tick();
  const wrap = document.getElementById('catInjectExtraWrap');
  expect(wrap.style.display).toBe('block');
  const ex = document.querySelectorAll('#catInjectExtra > div');
  expect(ex.length).toBe(1);
  expect(ex[0].textContent).toContain('ccbp:Legacy.md');
  ex[0].querySelector('button').click();
  expect(document.querySelectorAll('#catInjectExtra > div').length).toBe(0);
  expect(wrap.style.display).toBe('none');
});

test('前文注入弹层——池为空时出声（不静默空面）', async () => {
  mockInjectFetch([], []);
  window.openCatInject('majordomo', '管家');
  await tick();
  expect(document.getElementById('catInjectPool').textContent).toContain('默认前文池为空');
});

test('猫配置弹层——「选择前文」按钮开同一弹层，保存后回填本面清单', async () => {
  mockInjectFetch(['ccbp:A.md', 'ccbp:B.md'], []);
  window.catCfgTarget = 'majordomo';
  window.catCfgName = '管家';
  window.renderInjectList([]);
  document.getElementById('catCfgInjectPick').click();
  await tick();
  expect(document.getElementById('catInjectModal').style.display).toBe('flex');
  const rows = document.querySelectorAll('#catInjectPool > label');
  rows[1].querySelector('input').checked = true;
  rows[1].querySelector('input').dispatchEvent(new Event('change'));
  window.saveCatInject();
  await tick();
  expect(window.catCfgInjectList).toEqual(['ccbp:B.md']);
  expect(document.querySelectorAll('#catCfgInject > div').length).toBe(1);
});
