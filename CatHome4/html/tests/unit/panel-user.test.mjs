// tests/unit/panel-user.test.mjs —— 当前用户态（侧栏项 + 双击弹层；design-ch4-user-state）
// 覆盖：侧栏首项为当前用户且无 data-tab（配置页签仍为默认）· 用户态加载回填侧栏 · 双击开弹层
//       · 候选单选回填当前用户 · 群内身份与开关回填 · 保存提交字段 + 侧栏更新 · 遮罩点击关闭
import { readFile } from 'node:fs/promises';
import vm from 'node:vm';
import { expect, test, beforeAll } from 'vitest';
import { installDom, installMockEventSource } from './mock-env.js';

// 加载无导出脚本到全局（vm.runInThisContext——var/function 声明挂 globalThis）
async function runGlobalScript(url) {
  const code = await readFile(url, 'utf-8');
  vm.runInThisContext(code, { filename: url.toString() });
}

// 异步冲刷——fetch 链（then）在微任务后落定
async function flush() {
  await new Promise((resolve) => setTimeout(resolve, 0));
}

let postBodies = [];

beforeAll(async () => {
  const html = await readFile(new URL('../../index.html', import.meta.url), 'utf-8');
  installDom(html);
  installMockEventSource();
  // fetch mock——用户态读写路由；其余端点回空响应
  globalThis.fetch = async (url, opts) => {
    if (url === '/api/v1/user-state') {
      if (opts && opts.method === 'POST') {
        const body = JSON.parse(opts.body);
        postBodies.push(body);
        return { json: async () => ({ ok: true, current: body.current, acceptNonUser: body.acceptNonUser, qqIds: body.qqIds }) };
      }
      return {
        json: async () => ({
          ok: true,
          current: '莎',
          acceptNonUser: false,
          qqIds: '',
          users: [
            { name: '莎', desc: '源实例——CatHome 4 与 CCBP 知识网络的搭建者' },
            { name: 'Rushiera', desc: '莎的朋友——本支主理（2026-09-25 起）' }
          ]
        })
      };
    }
    return { json: async () => ({}), ok: true };
  };
  await runGlobalScript(new URL('../../js/ui-common.js', import.meta.url));
  await runGlobalScript(new URL('../../js/app.js', import.meta.url));
  await runGlobalScript(new URL('../../js/panel.js', import.meta.url));
  await runGlobalScript(new URL('../../js/panel-apis.js', import.meta.url));
  await runGlobalScript(new URL('../../js/panel-catcfg.js', import.meta.url));
  await runGlobalScript(new URL('../../js/panel-packs.js', import.meta.url));
  await runGlobalScript(new URL('../../js/panel-user.js', import.meta.url));
  await flush();
});

test('侧栏首项为当前用户——无 data-tab；配置页签仍为默认', () => {
  const btns = document.querySelectorAll('#sidebar button');
  expect(btns[0].id).toBe('sidebarUser');
  expect(btns[0].getAttribute('data-tab')).toBe(null);
  const tabBtns = document.querySelectorAll('#sidebar button[data-tab]');
  expect(tabBtns[0].getAttribute('data-tab')).toBe('config');
  expect(document.getElementById('tab-config').classList.contains('active')).toBe(true);
});

test('用户态加载——侧栏项回填当前用户', () => {
  expect(document.getElementById('sidebarUser').textContent).toBe('莎');
  expect(document.getElementById('sidebarUser').title.indexOf('当前用户：莎') >= 0).toBe(true);
});

test('双击侧栏项开弹层——候选单选回填当前用户', () => {
  const btn = document.getElementById('sidebarUser');
  btn.dispatchEvent(new window.Event('dblclick'));
  expect(document.getElementById('userModal').style.display).toBe('flex');
  const radios = document.querySelectorAll('#userModalList input[name="userCurrent"]');
  expect(radios.length).toBe(2);
  expect(radios[0].checked).toBe(true);
  expect(radios[1].checked).toBe(false);
  expect(document.getElementById('userQqAccept').checked).toBe(false);
  expect(document.getElementById('userQqIds').value).toBe('');
});

test('保存——提交选中用户 + 开关 + 群内身份；侧栏随之更新', async () => {
  const radios = document.querySelectorAll('#userModalList input[name="userCurrent"]');
  radios[1].checked = true;
  document.getElementById('userQqAccept').checked = true;
  document.getElementById('userQqIds').value = '雾理莎, 1234abcd';
  document.getElementById('userModalSave').dispatchEvent(new window.Event('click'));
  await flush();
  expect(postBodies.length).toBe(1);
  expect(postBodies[0].current).toBe('Rushiera');
  expect(postBodies[0].acceptNonUser).toBe(true);
  expect(postBodies[0].qqIds).toBe('雾理莎, 1234abcd');
  expect(document.getElementById('sidebarUser').textContent).toBe('Rushiera');
  expect(document.getElementById('userModalMsg').textContent.indexOf('已保存') >= 0).toBe(true);
});

test('遮罩点击关闭弹层', () => {
  const modal = document.getElementById('userModal');
  modal.dispatchEvent(new window.Event('click'));
  expect(modal.style.display).toBe('none');
});
