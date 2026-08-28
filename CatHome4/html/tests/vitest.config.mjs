import { defineConfig } from 'vitest/config';

export default defineConfig({
  test: {
    // node 环境 + setup.js 手动创建 JSDOM（完全控制 window/document——原生 JS 全局函数测试）
    environment: 'node',
    globals: true,
    include: ['unit/**/*.test.mjs'],
    setupFiles: ['unit/setup.js'],
  },
});
