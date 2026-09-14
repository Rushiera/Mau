// tests/unit/inject-sort.test.mjs —— 前文注入 List 编辑面（2026-09-14：目录条目 + 拖拽排序）
// 覆盖：拖拽改序（最简原生实现）· 保存载荷顺序与列表一致 · 目录路径形态可录入
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

test('目录路径形态可录入（前端校验接受完整路径）', () => {
  renderRows([]);
  document.getElementById('catCfgInjectAdd').value = 'ccbp:L2/Worker/Majordomo';
  document.getElementById('catCfgInjectAddBtn').click();
  const rows = document.querySelectorAll('#catCfgInject > div');
  expect(rows.length).toBe(1);
  expect(rows[0].textContent).toContain('ccbp:L2/Worker/Majordomo');
  // 非法形态仍被拒（相对路径）
  document.getElementById('catCfgInjectAdd').value = 'L2/Worker';
  document.getElementById('catCfgInjectAddBtn').click();
  expect(document.querySelectorAll('#catCfgInject > div').length).toBe(1);
});
