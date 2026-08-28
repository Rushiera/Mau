// tests/server.js —— 前端测试服务（宿主启动时自动拉起；持久化访问入口）
// 定位：前端测试框架之于前端 = Mau.exe 之于 CH4——CH4 通过 HTTP 端点随时触发前端测试
//       （自举闭环：改前端 → 跑测试 → 刷新生效）
// 端点：
//   GET /health          → 健康检查
//   GET /api/test/unit   → 运行 Vitest 单元测试（无需宿主）
//   GET /api/test/e2e    → 运行 Playwright E2E（需宿主在 127.0.0.1:8080）
//   GET /api/test        → 运行全部（unit + e2e）
// 端口：环境变量 FE_TEST_PORT（默认 8099）
const http = require('http');
const { spawn } = require('child_process');
const fs = require('fs');
const path = require('path');

// 部署标记清理——自动部署链末端（npm install 完成后 server.js 启动），删除 .fe-deploying 标记
try { fs.unlinkSync(path.join(__dirname, '.fe-deploying')); } catch (e) { /* 标记不存在忽略 */ }

const PORT = parseInt(process.env.FE_TEST_PORT || '8099', 10);
const TEST_DIR = __dirname;
const NPX = process.platform === 'win32' ? 'npx.cmd' : 'npx';

function runTest(args, res) {
  // Windows 下 npx.cmd 需 shell:true（spawn .cmd 直接执行失败——ENOENT）
  const child = spawn(NPX, args, {
    cwd: TEST_DIR,
    env: { ...process.env, CI: '1' },
    shell: process.platform === 'win32',
  });
  let stdout = '';
  let stderr = '';
  child.stdout.on('data', (d) => { stdout += d.toString(); });
  child.stderr.on('data', (d) => { stderr += d.toString(); });
  child.on('close', (code) => {
    res.writeHead(code === 0 ? 200 : 500, { 'Content-Type': 'application/json' });
    res.end(JSON.stringify({ ok: code === 0, code, stdout: stdout.slice(-6000), stderr: stderr.slice(-2000) }));
  });
}

const server = http.createServer((req, res) => {
  if (req.url === '/health') {
    res.writeHead(200, { 'Content-Type': 'application/json' });
    res.end(JSON.stringify({ ok: true, service: 'ch4-frontend-test', port: PORT }));
    return;
  }
  if (req.url === '/api/test/unit') {
    runTest(['vitest', 'run'], res);
    return;
  }
  if (req.url === '/api/test/e2e') {
    runTest(['playwright', 'test'], res);
    return;
  }
  if (req.url === '/api/test') {
    runTest(['vitest', 'run'], res);
    return;
  }
  res.writeHead(404, { 'Content-Type': 'text/plain' });
  res.end('not found: ' + req.url);
});

server.listen(PORT, '127.0.0.1', () => {
  console.log('[fe-test] 前端测试服务就绪: http://127.0.0.1:' + PORT);
});
