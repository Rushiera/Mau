// CH4 前端测试框架——Playwright E2E 配置
// 定位：前端测试是开发流程——宿主启动时自动拉起测试工具；改前端 → 跑测试 → 刷新生效
// 宿主由开发流程启动（前台窗口，8080 单例）；E2E 复用已运行宿主（reuseExistingServer）
const { defineConfig } = require('@playwright/test');

module.exports = defineConfig({
  testDir: './e2e',
  timeout: 30000,
  fullyParallel: false,
  workers: 1,
  reporter: [['list']],
  use: {
    baseURL: 'http://127.0.0.1:8080',
    headless: true,
    trace: 'retain-on-failure',
  },
  // 宿主未运行时由测试服务拉起（tests/server.js 等待宿主就绪）；已运行则复用
  webServer: {
    command: 'node server.js --wait-host',
    url: 'http://127.0.0.1:8080',
    reuseExistingServer: true,
    timeout: 30000,
  },
});
