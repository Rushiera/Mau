// tests/global-setup.mjs —— Vitest 全局启动钩子（整个测试进程启动前只执行一次）
// 定位：前端版本自动化——每次 Vitest 运行，frontend-version.json 版本号 +1 并写入当前时间戳（前端版本唯一真相源）
// 为什么用 globalSetup 而非 setupFiles：setupFiles 每个测试文件执行一次（3 个文件会递增 3 次）；globalSetup 进程级只跑一次 ✅
// 消费面：chat.html / index.html 的 data-version 组件 fetch 本文件显示「UI vN | 时间戳」——前端一更新测试一跑，浏览器即见新版本
// 文件位置：html/js/frontend-version.json（宿主 /js/{file} 静态路由可 fetch；仓库内可直读）
import { readFileSync, writeFileSync } from 'node:fs';

export default function () {
  const file = new URL('../js/frontend-version.json', import.meta.url);
  let version = 0;
  try {
    const cur = JSON.parse(readFileSync(file, 'utf-8'));
    version = (typeof cur.version === 'number' ? cur.version : 0) + 1;
  } catch {
    version = 1;   // 文件缺失/损坏——从 1 起算
  }
  const now = new Date();
  const pad = (n) => String(n).padStart(2, '0');
  const time = now.getFullYear() + '-' + pad(now.getMonth() + 1) + '-' + pad(now.getDate())
    + ' ' + pad(now.getHours()) + ':' + pad(now.getMinutes()) + ':' + pad(now.getSeconds());
  writeFileSync(file, JSON.stringify({ version, time }, null, 2) + '\n', 'utf-8');
}
