// tests/e2e/main.spec.js —— 主面板 E2E（Playwright）
// 前置：宿主运行于 127.0.0.1:8080（开发流程前台窗口；webServer reuseExistingServer 复用）
// 注意：对话区发送会触发真实 LLM 会话（慢且副作用）——发送类测试只验证前端即时行为（SSE user 事件），
//       不等待 LLM 回复；Note 新增任务依赖宿主处理（会话忙时排队）——等待放宽。
const { test, expect } = require('@playwright/test');

test('主面板加载——标题 + 侧栏页签 + 状态区骨架', async ({ page }) => {
  await page.goto('/');
  await expect(page).toHaveTitle(/CH4/);
  // 侧栏六个页签
  await expect(page.locator('#sidebar button[data-tab="state"]')).toBeVisible();
  await expect(page.locator('#sidebar button[data-tab="log"]')).toBeVisible();
  await expect(page.locator('#sidebar button[data-tab="raw"]')).toBeVisible();
  await expect(page.locator('#sidebar button[data-tab="config"]')).toBeVisible();
  await expect(page.locator('#sidebar button[data-tab="chat"]')).toBeVisible();
  await expect(page.locator('#sidebar button[data-tab="cats"]')).toBeVisible();
  // 状态区骨架
  await expect(page.locator('#meta')).toBeVisible();
  await expect(page.locator('#cats')).toBeVisible();
  await expect(page.locator('#boxes')).toBeVisible();
});

test('页签切换——日志/原始/配置/多猫', async ({ page }) => {
  await page.goto('/');
  await page.locator('#sidebar button[data-tab="log"]').click();
  await expect(page.locator('#tab-log')).toBeVisible();
  await expect(page.locator('#logevents')).toBeVisible();

  await page.locator('#sidebar button[data-tab="raw"]').click();
  await expect(page.locator('#tab-raw')).toBeVisible();
  await expect(page.locator('#rawJson')).toBeVisible();

  await page.locator('#sidebar button[data-tab="config"]').click();
  await expect(page.locator('#tab-config')).toBeVisible();
  await expect(page.locator('#configTable')).toBeVisible();

  await page.locator('#sidebar button[data-tab="cats"]').click();
  await expect(page.locator('#tab-cats')).toBeVisible();
  await expect(page.locator('#catsTable')).toBeVisible();
});

test('状态区渲染——SSE 快照到达后 meta 显示 pid/frame', async ({ page }) => {
  await page.goto('/');
  // 等待 SSE 快照（宿主推送 snapshot 事件 → meta 更新）
  await expect(page.locator('#meta')).toContainText('pid=', { timeout: 10000 });
  await expect(page.locator('#meta')).toContainText('frame=');
});

test('对话区——发送消息渲染用户气泡 + 输入清空', async ({ page }) => {
  await page.goto('/');
  await page.locator('#sidebar button[data-tab="chat"]').click();
  const input = page.locator('#chatSendInput');
  await input.fill('你好');
  await page.locator('#chatSendBtn').click();
  // 用户气泡出现（SSE user 事件渲染——单向数据流；.last() 避免历史消息干扰）
  await expect(page.locator('.chat-row.user .chat-bubble').last()).toContainText('你好', { timeout: 10000 });
  // 输入框已清空
  await expect(input).toHaveValue('');
});

test('Note 面板——折叠/展开（纯前端）', async ({ page }) => {
  await page.goto('/');
  await page.locator('#sidebar button[data-tab="chat"]').click();
  // Note 面板初始折叠
  await expect(page.locator('#notePanel')).toHaveClass(/collapsed/);
  // 展开
  await page.locator('#noteHead').click();
  await expect(page.locator('#notePanel')).not.toHaveClass(/collapsed/);
  // 再折叠
  await page.locator('#noteHead').click();
  await expect(page.locator('#notePanel')).toHaveClass(/collapsed/);
});

test('Note 面板——新增任务（依赖宿主；会话忙时排队）', async ({ page }) => {
  await page.goto('/');
  await page.locator('#sidebar button[data-tab="chat"]').click();
  // Note 面板初始折叠——先展开（输入框才可见）
  await page.locator('#noteHead').click();
  await page.locator('#noteAddInput').fill('测试任务');
  await page.locator('#noteAddRow button').first().click();
  // 等待 SSE note 事件回绘（会话忙时排队——超时放宽到 30s）
  await expect(page.locator('#noteList')).toContainText('测试任务', { timeout: 30000 });
});
