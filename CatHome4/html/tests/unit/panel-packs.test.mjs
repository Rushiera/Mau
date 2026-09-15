// tests/unit/panel-packs.test.mjs —— 加载包池管理面 + 猫级挂载勾选（R4 加载包 2026-09-15）
// 覆盖：路径文本解析 · 池表格渲染 · 池保存载荷 · 猫配置挂载勾选渲染与收集 · 猫配置保存载荷含 packs
// 基座：与 inject-sort.test.mjs 同构（index.html DOM + 主面板脚本全量加载）
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
  await runGlobalScript(new URL('../../js/panel-packs.js', import.meta.url));
});

test('路径文本解析——去空白行、保序', () => {
  const out = window.parsePackPaths('CCBP:L2/Worker/Majordomo\n\n  CCBP:L2/Job/CCBP_OverWork  \n');
  expect(out).toEqual(['CCBP:L2/Worker/Majordomo', 'CCBP:L2/Job/CCBP_OverWork']);
});

test('池表格渲染——行数与关键字段可见', async () => {
  globalThis.fetch = async () => ({ json: async () => ({ ok: true, packs: [{ key: 'overwork', desc: '收工加载包', paths: ['a', 'b'] }] }) });
  window.loadPacks();
  await new Promise((resolve) => setTimeout(resolve, 0));
  const rows = document.querySelectorAll('#packsTbody > tr');
  expect(rows.length).toBe(1);
  expect(rows[0].textContent).toContain('overwork');
  expect(rows[0].textContent).toContain('收工加载包');
  expect(rows[0].textContent).toContain('2');
});

test('池保存载荷——key/desc/paths（一行一个）', async () => {
  let captured = null;
  globalThis.fetch = async (url, opt) => {
    if (opt && opt.body) { captured = JSON.parse(opt.body); }
    return { json: async () => ({ ok: true }) };
  };
  document.getElementById('packKey').value = 'overwork';
  document.getElementById('packDesc').value = '收工加载包';
  document.getElementById('packPaths').value = 'CCBP:L2/Worker/Majordomo\nCCBP:L2/Job/CCBP_OverWork';
  window.savePack();
  await new Promise((resolve) => setTimeout(resolve, 0));
  expect(captured).toEqual({
    key: 'overwork',
    desc: '收工加载包',
    paths: ['CCBP:L2/Worker/Majordomo', 'CCBP:L2/Job/CCBP_OverWork']
  });
});

test('池保存载荷——空 key / 空 paths 被拒（不发请求）', async () => {
  let called = false;
  globalThis.fetch = async () => { called = true; return { json: async () => ({ ok: true }) }; };
  document.getElementById('packKey').value = '';
  document.getElementById('packPaths').value = 'CCBP:L1';
  window.savePack();
  await new Promise((resolve) => setTimeout(resolve, 0));
  expect(called).toBe(false);
});

test('猫配置挂载勾选——渲染池全量 + 收集已勾选 key', () => {
  window.renderPackChecks([
    { key: 'overwork', desc: '收工加载包' },
    { key: 'distill', desc: '蒸馏加载包' }
  ], ['overwork']);
  const boxes = document.querySelectorAll('#catCfgPacks input.pack-cb');
  expect(boxes.length).toBe(2);
  expect(boxes[0].checked).toBe(true);
  expect(boxes[1].checked).toBe(false);
  expect(window.collectPackChecks()).toEqual(['overwork']);
});

test('猫配置保存载荷含 packs 字段', async () => {
  let captured = null;
  globalThis.fetch = async (url, opt) => {
    if (opt && opt.body) { captured = JSON.parse(opt.body); }
    return { json: async () => ({ ok: true }) };
  };
  window.catCfgTarget = 'majordomo';
  window.renderPackChecks([{ key: 'overwork', desc: '收工加载包' }], []);
  document.querySelector('#catCfgPacks input.pack-cb').checked = true;
  window.saveCatCfg();
  await new Promise((resolve) => setTimeout(resolve, 0));
  expect(captured).not.toBeNull();
  expect(captured.packs).toEqual(['overwork']);
});
