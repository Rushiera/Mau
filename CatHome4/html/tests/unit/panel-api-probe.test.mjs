// tests/unit/panel-api-probe.test.mjs —— LLM API 池连通性测试（2026-10-01 易用性轮）
// 覆盖：行内「测试」按钮两态（未测=测试 / 已测=内容报告）· 报告弹窗渲染三段（模型清单 / 站点信息 / 定价与分组）
//       · 公告不入报告（后端剔除，前端不渲染 announcements）· 重新测试重发请求 · 失败态留痕
import { readFile } from 'node:fs/promises';
import vm from 'node:vm';
import { expect, test, beforeAll, beforeEach } from 'vitest';
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

const API_ROW = {
  apiConfigId: '11111111-1111-1111-1111-111111111111',
  displayName: '主端点',
  apiType: 'deepseek',
  endpoint: 'https://a.example/v1',
  defaultModel: 'deepseek-v4-flash',
  isDefault: true,
  apiKey: 'sk-ab****yz',
  hasKey: true
};

// 报告样本——三段全通过（站点段不含 announcements：后端剔除，前端不渲染）
const REPORT = {
  ok: true,
  apiConfigId: API_ROW.apiConfigId,
  displayName: '主端点',
  endpoint: 'https://a.example/v1',
  defaultModel: 'deepseek-v4-flash',
  hasKey: true,
  modelsUrl: 'https://a.example/v1/models',
  siteOrigin: 'https://a.example',
  probedAt: '2026-10-01 20:00:00',
  elapsedMs: 123,
  models: {
    ok: true,
    httpStatus: 200,
    error: '',
    payload: [{ id: 'deepseek-v4-flash', ownedBy: 'openai', endpointTypes: ['openai'] }]
  },
  site: {
    ok: true,
    httpStatus: 200,
    error: '',
    payload: { system_name: '贤鱼 API', version: 'v1.0.0-rc.40', quota_per_unit: 500000 }
  },
  pricing: {
    ok: true,
    httpStatus: 200,
    error: '',
    payload: {
      models: [{ model_name: 'deepseek-v4.1-flash', model_ratio: 0.5, completion_ratio: 4, cache_ratio: 0.02, model_price: 0, enable_groups: ['default'] }],
      groupRatio: { default: 1, '福利低价国模': 0.05 },
      usableGroup: { '福利低价国模': '极低价福利分组' },
      vendors: [{ id: 1, name: 'DeepSeek' }],
      supportedEndpoint: { openai: { path: '/v1/chat/completions', method: 'POST' } },
      autoGroups: []
    }
  }
};

let calls = [];

// 抓取 fetch——记录 url + body；回执按 URL 分流
function captureFetch(report) {
  calls = [];
  globalThis.fetch = async (url, opt) => {
    let body = null;
    if (opt && opt.body) { body = JSON.parse(opt.body); }
    calls.push({ url: String(url), body: body });
    if (String(url).indexOf('/llm-apis/probe') >= 0) {
      return { json: async () => report };
    }
    return { json: async () => ({ ok: true, items: [] }) };
  };
}

function callsTo(url) {
  return calls.filter((c) => c.url === url);
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
  await tick();
});

beforeEach(() => {
  // 测试间清缓存（模块级 var 常驻——避免上条用例的「内容报告」态污染）
  window.apiProbeCache = {};
  window.apiProbeBtns = {};
  window.apiProbeOpen = null;
  document.getElementById('apiProbeModal').style.display = 'none';
  document.getElementById('apiProbeMsg').textContent = '';
});

test('测试列——未测态是「测试」按钮，行内无报告内容', () => {
  window.renderApis([API_ROW]);
  const tr = document.querySelector('#apisTable tbody tr');
  const td = tr.children[8];
  expect(td.querySelector('button').textContent).toBe('测试');
  expect(td.textContent).toBe('测试');
});

test('点击测试——发 probe 请求、按钮转「内容报告」、弹窗渲染三段', async () => {
  captureFetch(REPORT);
  window.renderApis([API_ROW]);
  const btn = document.querySelector('#apisTable tbody tr').children[8].querySelector('button');
  btn.onclick();
  await tick();
  await tick();
  // 请求形态——POST /api/v1/llm-apis/probe + apiConfigId
  const probes = callsTo('/api/v1/llm-apis/probe');
  expect(probes.length).toBe(1);
  expect(probes[0].body).toEqual({ apiConfigId: API_ROW.apiConfigId });
  // 按钮态——转「内容报告」
  expect(btn.textContent).toBe('内容报告');
  // 弹窗——可见 + 三段齐备
  const modal = document.getElementById('apiProbeModal');
  expect(modal.style.display).toBe('flex');
  const text = document.getElementById('apiProbeBody').textContent;
  expect(text).toContain('可用模型');
  expect(text).toContain('deepseek-v4-flash');
  expect(text).toContain('站点信息');
  expect(text).toContain('贤鱼 API');
  expect(text).toContain('定价与分组');
  expect(text).toContain('deepseek-v4.1-flash');
  expect(text).toContain('福利低价国模');
  // 公告不入报告
  expect(text).not.toContain('announcement');
});

test('已测态——点「内容报告」直接开弹窗，不重复请求', async () => {
  captureFetch(REPORT);
  window.apiProbeCache[API_ROW.apiConfigId] = REPORT;
  window.renderApis([API_ROW]);
  const btn = document.querySelector('#apisTable tbody tr').children[8].querySelector('button');
  expect(btn.textContent).toBe('内容报告');
  btn.onclick();
  await tick();
  expect(callsTo('/api/v1/llm-apis/probe').length).toBe(0);
  expect(document.getElementById('apiProbeModal').style.display).toBe('flex');
});

test('弹窗内重新测试——重发请求并刷新报告', async () => {
  captureFetch(REPORT);
  window.apiProbeCache[API_ROW.apiConfigId] = REPORT;
  window.apiProbeOpen = API_ROW;
  window.renderApis([API_ROW]);
  document.getElementById('apiProbeRerun').onclick();
  await tick();
  await tick();
  expect(callsTo('/api/v1/llm-apis/probe').length).toBe(1);
});

test('探测失败——分段留痕（错误文本可见），按钮回落「测试」态', async () => {
  const failed = {
    ok: false,
    apiConfigId: API_ROW.apiConfigId,
    displayName: '主端点',
    endpoint: 'https://a.example/v1',
    defaultModel: 'deepseek-v4-flash',
    hasKey: false,
    modelsUrl: 'https://a.example/v1/models',
    siteOrigin: 'https://a.example',
    probedAt: '2026-10-01 20:00:00',
    elapsedMs: 50,
    models: { ok: false, httpStatus: 0, error: 'HttpRequestException|connection refused', payload: null },
    site: { ok: false, httpStatus: 404, error: 'HTTP 404 ', payload: null },
    pricing: { ok: false, httpStatus: 404, error: 'HTTP 404 ', payload: null }
  };
  captureFetch(failed);
  window.renderApis([API_ROW]);
  const btn = document.querySelector('#apisTable tbody tr').children[8].querySelector('button');
  btn.onclick();
  await tick();
  await tick();
  const text = document.getElementById('apiProbeBody').textContent;
  expect(text).toContain('未通过');
  expect(text).toContain('connection refused');
  expect(text).toContain('HTTP 404');
});

test('接口级失败（非法 id）——不写缓存、按钮保持「测试」、提示可见', async () => {
  captureFetch({ ok: false, error: 'apiConfigId 非法' });
  window.renderApis([API_ROW]);
  const btn = document.querySelector('#apisTable tbody tr').children[8].querySelector('button');
  btn.onclick();
  await tick();
  await tick();
  expect(btn.textContent).toBe('测试');
  expect(document.getElementById('apiProbeMsg').textContent).toContain('apiConfigId 非法');
  expect(document.getElementById('apiProbeModal').style.display).not.toBe('flex');
});
