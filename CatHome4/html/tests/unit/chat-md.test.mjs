// tests/unit/chat-md.test.mjs —— chat-md.js 手写 Markdown 解析器单元测试（F3）
// 覆盖：代码块/标题/引用/任务列表/无序有序列表/表格/水平线 + 行内 ** * ` + XSS 转义 + 段落聚合 + 未闭合容错
// 加载方式：vm.runInThisContext 加载 chat-md.js（纯函数，无 DOM 依赖——直接测全局 mdToHtml）
import { readFile } from 'node:fs/promises';
import vm from 'node:vm';
import { expect, test, beforeAll } from 'vitest';

let mdToHtml;

beforeAll(async () => {
  const code = await readFile(new URL('../../js/chat-md.js', import.meta.url), 'utf-8');
  vm.runInThisContext(code, { filename: 'chat-md.js' });
  mdToHtml = globalThis.mdToHtml;
});

// ── 标题 ──
test('标题——#~#### 渲染 h1-h4', () => {
  expect(mdToHtml('# 一级')).toBe('<h1>一级</h1>');
  expect(mdToHtml('## 二级')).toBe('<h2>二级</h2>');
  expect(mdToHtml('### 三级')).toBe('<h3>三级</h3>');
  expect(mdToHtml('#### 四级')).toBe('<h4>四级</h4>');
  expect(mdToHtml('##### 五级')).toBe('<h5>五级</h5>');
  expect(mdToHtml('###### 六级')).toBe('<h6>六级</h6>');
});

// ── 段落 ──
test('段落——多行聚合成一个 <p>（空行分隔）', () => {
  expect(mdToHtml('第一行\n第二行')).toBe('<p>第一行\n第二行</p>');
  expect(mdToHtml('第一段\n\n第二段')).toBe('<p>第一段</p>\n<p>第二段</p>');
});

// ── 代码块 ──
test('代码块——``` 包裹，保留换行与缩进，语言标记忽略（A81 外层 .md-copy 包裹）', () => {
  const md = '```csharp\nvoid Foo()\n{\n    var x = 1;\n}\n```';
  const html = mdToHtml(md);
  expect(html).toBe('<div class="md-copy"><pre><code>void Foo()\n{\n    var x = 1;\n}</code></pre></div>');
});

test('代码块——未闭合容错（EOF 强制闭合）', () => {
  expect(mdToHtml('```\nabc')).toBe('<div class="md-copy"><pre><code>abc</code></pre></div>');
});

test('代码块——```markdown 递归渲染为真 Markdown（LLM 常包表格/标题展示效果）', () => {
  const md = '```markdown\n## 标题\n\n| a | b |\n| --- | --- |\n| 1 | 2 |\n```';
  const html = mdToHtml(md);
  // 递归渲染——不出现 pre/code 黑块，而是 h2 + 真 table
  expect(html).not.toContain('<pre>');
  expect(html).toContain('<h2>标题</h2>');
  expect(html).toContain('<table>');
  expect(html).toContain('<thead>');
});

test('代码块——```csharp 保持代码块（非 markdown 语言不递归）', () => {
  expect(mdToHtml('```csharp\nint x = 1;\n```')).toBe('<div class="md-copy"><pre><code>int x = 1;</code></pre></div>');
});

// ── 引用 ──
test('引用——> 渲染 blockquote', () => {
  expect(mdToHtml('> 引文')).toBe('<blockquote>引文</blockquote>');
});

// ── 列表 ──
test('无序列表——- 前缀合并 <ul>', () => {
  expect(mdToHtml('- 甲\n- 乙')).toBe('<ul><li>甲</li><li>乙</li></ul>');
});

test('有序列表——数字. 合并 <ol>', () => {
  expect(mdToHtml('1. 甲\n2. 乙')).toBe('<ol><li>甲</li><li>乙</li></ol>');
});

test('任务列表——- [ ] / - [x] 渲染 md-task 类', () => {
  expect(mdToHtml('- [ ] 待办\n- [x] 完成')).toBe(
    '<ul><li class="md-task">待办</li><li class="md-task done">完成</li></ul>'
  );
});

test('列表类型切换——ul 与 ol 各自闭合', () => {
  expect(mdToHtml('- 甲\n1. 乙')).toBe('<ul><li>甲</li></ul>\n<ol><li>乙</li></ol>');
});

// ── 表格 ──
test('表格——表头+分隔行+数据行渲染真 <table>', () => {
  const md = '| 名称 | 数量 |\n| --- | --- |\n| 苹果 | 3 |\n| 香蕉 | 5 |';
  const html = mdToHtml(md);
  expect(html).toContain('<table>');
  expect(html).toContain('<thead><tr><td>名称</td><td>数量</td></tr></thead>');
  expect(html).toContain('<tbody><tr><td>苹果</td><td>3</td></tr><tr><td>香蕉</td><td>5</td></tr></tbody>');
  expect(html).toContain('</table>');
});

test('表格——无分隔行则全部数据行（无 thead）', () => {
  const html = mdToHtml('| a | b |\n| c | d |');
  expect(html).toContain('<tbody>');
  expect(html).not.toContain('<thead>');
});

// ── A81 复制原文——包裹层与源文本载体 ──
test('A81 代码块——外层 .md-copy 包裹，源文本走 pre code（不存 data-md）', () => {
  const html = mdToHtml('```\nabc\ndef\n```');
  expect(html).toBe('<div class="md-copy"><pre><code>abc\ndef</code></pre></div>');
  expect(html).not.toContain('data-md');
});

test('A81 表格——.md-copy 包裹 + data-md 保留源行（含分隔行，换行转 &#10;）', () => {
  const md = '| a | b |\n| --- | --- |\n| 1 | 2 |';
  const html = mdToHtml(md);
  expect(html).toContain('<div class="md-copy" data-md="| a | b |&#10;| --- | --- |&#10;| 1 | 2 |">');
  expect(html).toContain('</table></div>');
});

test('A81 表格源文本——属性转义（& < > "）不破属性边界', () => {
  const html = mdToHtml('| a & b | <x> | "q" |\n| --- | --- | --- |');
  expect(html).toContain('data-md="| a &amp; b | &lt;x&gt; | &quot;q&quot; |&#10;| --- | --- | --- |"');
  expect(html).not.toContain('data-md="| a & b');
});

test('A81 表格源文本——无分隔行时 data-md 只含数据行', () => {
  const html = mdToHtml('| a | b |\n| c | d |');
  expect(html).toContain('data-md="| a | b |&#10;| c | d |"');
});

test('A81 ```markdown 递归——内层表格同样带复制载体', () => {
  const html = mdToHtml('```markdown\n| a | b |\n| --- | --- |\n| 1 | 2 |\n```');
  expect(html).toContain('class="md-copy"');
  expect(html).toContain('data-md="| a | b |&#10;| --- | --- |&#10;| 1 | 2 |"');
  expect(html).not.toContain('<pre>');
});

// ── 行内 ──
test('行内——**粗体** / *斜体* / `代码`', () => {
  expect(mdToHtml('**粗体**')).toBe('<p><strong>粗体</strong></p>');
  expect(mdToHtml('*斜体*')).toBe('<p><em>斜体</em></p>');
  expect(mdToHtml('`代码`')).toBe('<p><code>代码</code></p>');
});

test('行内——未闭合标记原样输出（容错）', () => {
  expect(mdToHtml('**未闭合')).toBe('<p>**未闭合</p>');
});

test('行内——混合嵌套', () => {
  expect(mdToHtml('**a** 和 *b* 与 `c`')).toBe('<p><strong>a</strong> 和 <em>b</em> 与 <code>c</code></p>');
});

// ── 水平线 ──
test('水平线——--- / *** / ___', () => {
  expect(mdToHtml('---')).toBe('<hr>');
  expect(mdToHtml('***')).toBe('<hr>');
  expect(mdToHtml('___')).toBe('<hr>');
});

// ── XSS 安全 ──
test('XSS——文本全部转义，标签由解析器硬编码', () => {
  const html = mdToHtml('<script>alert(1)</script>');
  expect(html).not.toContain('<script>');
  expect(html).toContain('&lt;script&gt;');
});

test('XSS——行内代码与单元格内容同样转义', () => {
  expect(mdToHtml('`<img onerror=x>`')).toContain('&lt;img onerror=x&gt;');
  expect(mdToHtml('| <a> |')).toContain('&lt;a&gt;');
});

// ── 空输入 ──
test('空输入返回空串', () => {
  expect(mdToHtml('')).toBe('');
  expect(mdToHtml(null)).toBe('');
  expect(mdToHtml(undefined)).toBe('');
});
