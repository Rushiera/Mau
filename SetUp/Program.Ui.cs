using System;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace SetUp
{
    /// <summary>
    /// Program 分部——WinForms UI 外观层（无参启动：交互式部署面板 + P/Invoke 控制台联动）。
    /// </summary>
    public static partial class Program
    {
        /// <summary>
        /// 标准输出句柄常量。
        /// </summary>
        private const int STD_OUTPUT_HANDLE = -11;

        /// <summary>
        /// 标准错误句柄常量。
        /// </summary>
        private const int STD_ERROR_HANDLE = -12;

        /// <summary>
        /// 附加父进程控制台常量。
        /// </summary>
        private const int ATTACH_PARENT_PROCESS = -1;

        /// <summary>
        /// SetUp 主窗体——WinForms UI（无参双击进入）。
        /// 页面：状态栏 + 日志实时输出（Console 重定向）+ prepare/deploy 操作区。
        /// </summary>
        public sealed class SetUpForm : Form
        {
            /// <summary>
            /// 日志文本框。
            /// </summary>
            private readonly TextBox _logBox;

            /// <summary>
            /// 部署目标目录输入框。
            /// </summary>
            private readonly TextBox _targetBox;

            /// <summary>
            /// 状态标签。
            /// </summary>
            private readonly Label _statusLabel;

            /// <summary>
            /// prepare 按钮。
            /// </summary>
            private readonly Button _prepareButton;

            /// <summary>
            /// deploy 按钮。
            /// </summary>
            private readonly Button _deployButton;

            /// <summary>
            /// 仓库根路径。
            /// </summary>
            private string _repoRoot = "";

            /// <summary>
            /// 忙碌标志——防并发操作。
            /// </summary>
            private bool _busy = false;

            /// <summary>
            /// 构造——布局 + Console 重定向 + 后台启动检测。
            /// </summary>
            public SetUpForm()
            {
                Text = "SetUp —— CH4/Mau 一键部署工具";
                Width = 900;
                Height = 640;
                StartPosition = FormStartPosition.CenterScreen;
                Font = new Font("Microsoft YaHei UI", 9f);

                // [段1] 状态标签
                _statusLabel = new Label();
                _statusLabel.Text = "检测中…";
                _statusLabel.AutoSize = true;
                _statusLabel.Location = new Point(12, 12);
                Controls.Add(_statusLabel);

                // [段2] 日志文本框——只读多行 + 垂直滚动 + 等宽字体
                _logBox = new TextBox();
                _logBox.Multiline = true;
                _logBox.ReadOnly = true;
                _logBox.ScrollBars = ScrollBars.Vertical;
                _logBox.WordWrap = false;
                _logBox.Font = new Font("Consolas", 9f);
                _logBox.Location = new Point(12, 40);
                _logBox.Size = new Size(860, 480);
                _logBox.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
                Controls.Add(_logBox);

                // [段3] prepare 按钮
                _prepareButton = new Button();
                _prepareButton.Text = "重建发布链 (prepare)";
                _prepareButton.Enabled = false;
                _prepareButton.Location = new Point(12, 530);
                _prepareButton.Size = new Size(150, 30);
                _prepareButton.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
                _prepareButton.Click += OnPrepareClick;
                Controls.Add(_prepareButton);

                // [段4] 部署目标输入区
                Label targetLabel = new Label();
                targetLabel.Text = "部署目标:";
                targetLabel.AutoSize = true;
                targetLabel.Location = new Point(12, 572);
                targetLabel.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
                Controls.Add(targetLabel);

                _targetBox = new TextBox();
                _targetBox.Location = new Point(80, 568);
                _targetBox.Size = new Size(600, 24);
                _targetBox.Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
                Controls.Add(_targetBox);

                Button browseButton = new Button();
                browseButton.Text = "浏览…";
                browseButton.Location = new Point(692, 566);
                browseButton.Size = new Size(60, 28);
                browseButton.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
                browseButton.Click += OnBrowseClick;
                Controls.Add(browseButton);

                // [段5] deploy 按钮
                _deployButton = new Button();
                _deployButton.Text = "部署 (deploy)";
                _deployButton.Enabled = false;
                _deployButton.Location = new Point(760, 566);
                _deployButton.Size = new Size(110, 30);
                _deployButton.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
                _deployButton.Click += OnDeployClick;
                Controls.Add(_deployButton);

                // [段6] Console 重定向到日志框——全部 Console.WriteLine 自动进 UI（CLI 模式不重定向）
                Console.SetOut(new TextBoxWriter(_logBox));
                Console.SetError(new TextBoxWriter(_logBox));

                // [段7] 检测线程推迟到 Shown——窗体句柄就绪后才可触碰控件（构造函数启动会强制早建句柄 → Win32Exception 87）
                Shown += new EventHandler(OnFormShown);
            }

            /// <summary>
            /// 窗体显示完成事件——句柄全部就绪，后台启动检测（仓库根 + 环境）。
            /// </summary>
            /// <param name="sender">事件源</param>
            /// <param name="e">事件参数</param>
            private void OnFormShown(object sender, EventArgs e)
            {
                Thread t = new Thread(InitThread);
                t.IsBackground = true;
                t.Start();
            }

            /// <summary>
            /// 后台初始化线程——仓库根检测 + 环境检测 + 按钮解锁。
            /// </summary>
            private void InitThread()
            {
                AppendLog("[SetUp] 仓库根检测…\r\n");
                _repoRoot = ResolveRepoRoot(Environment.CurrentDirectory);
                if (_repoRoot.Length == 0)
                {
                    AppendLog("[SetUp] 错误：当前目录未找到 Mau.sln——SetUp.exe 必须位于 Mau 仓库根目录运行。\r\n");
                    SetStatus("未找到仓库根——操作不可用");
                    return;
                }
                AppendLog("[SetUp] 仓库根: " + _repoRoot + "\r\n");
                bool ok = CheckEnvironment();
                if (ok)
                {
                    SetStatus("环境就绪：.NET 10 Runtime + SDK + WindowsDesktop");
                }
                else
                {
                    SetStatus("环境检测未通过——操作不可用");
                }
                SetButtonsEnabled(ok);
            }

            /// <summary>
            /// prepare 点击——后台执行重建发布链。
            /// </summary>
            /// <param name="sender">事件源</param>
            /// <param name="e">事件参数</param>
            private void OnPrepareClick(object sender, EventArgs e)
            {
                if (_busy)
                {
                    return;
                }
                _busy = true;
                SetButtonsEnabled(false);
                Thread t = new Thread(RunPrepareThread);
                t.IsBackground = true;
                t.Start();
            }

            /// <summary>
            /// prepare 后台线程。
            /// </summary>
            private void RunPrepareThread()
            {
                AppendLog("\r\n========== prepare 开始 ==========\r\n");
                int code = Prepare(_repoRoot);
                AppendLog("========== prepare " + (code == 0 ? "完成 ✅" : "失败 ❌") + " ==========\r\n");
                _busy = false;
                SetButtonsEnabled(true);
            }

            /// <summary>
            /// 浏览按钮——选择部署目标文件夹。
            /// </summary>
            /// <param name="sender">事件源</param>
            /// <param name="e">事件参数</param>
            private void OnBrowseClick(object sender, EventArgs e)
            {
                FolderBrowserDialog dialog = new FolderBrowserDialog();
                dialog.Description = "选择部署目标文件夹（外部运行实例目录）";
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    _targetBox.Text = dialog.SelectedPath;
                }
            }

            /// <summary>
            /// deploy 点击——校验目标目录 + 后台部署。
            /// </summary>
            /// <param name="sender">事件源</param>
            /// <param name="e">事件参数</param>
            private void OnDeployClick(object sender, EventArgs e)
            {
                if (_busy)
                {
                    return;
                }
                string target = _targetBox.Text.Trim();
                if (target.Length == 0)
                {
                    MessageBox.Show(this, "请先选择部署目标文件夹。", "SetUp", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                _busy = true;
                SetButtonsEnabled(false);
                Thread t = new Thread(delegate ()
                {
                    RunDeployThread(target);
                });
                t.IsBackground = true;
                t.Start();
            }

            /// <summary>
            /// deploy 后台线程。
            /// </summary>
            /// <param name="target">目标目录</param>
            private void RunDeployThread(string target)
            {
                AppendLog("\r\n========== deploy 开始 ==========\r\n");
                int code = Deploy(_repoRoot, target);
                AppendLog("========== deploy " + (code == 0 ? "完成 ✅" : "失败 ❌") + " ==========\r\n");
                _busy = false;
                SetButtonsEnabled(true);
            }

            /// <summary>
            /// 日志追加——跨线程安全（Invoke 归 UI 线程）。
            /// </summary>
            /// <param name="text">日志文本</param>
            internal void AppendLog(string text)
            {
                if (_logBox.IsDisposed)
                {
                    return;
                }
                if (_logBox.InvokeRequired)
                {
                    _logBox.BeginInvoke(new Action<string>(AppendLogSafe), text);
                }
                else
                {
                    AppendLogSafe(text);
                }
            }

            /// <summary>
            /// 日志追加执行体（UI 线程内）。
            /// </summary>
            /// <param name="text">日志文本</param>
            private void AppendLogSafe(string text)
            {
                _logBox.AppendText(text);
                _logBox.SelectionStart = _logBox.TextLength;
                _logBox.ScrollToCaret();
            }

            /// <summary>
            /// 状态标签更新——跨线程安全。
            /// </summary>
            /// <param name="text">状态文本</param>
            private void SetStatus(string text)
            {
                if (_statusLabel.InvokeRequired)
                {
                    _statusLabel.BeginInvoke(new Action<string>(SetStatusSafe), text);
                }
                else
                {
                    SetStatusSafe(text);
                }
            }

            /// <summary>
            /// 状态标签执行体。
            /// </summary>
            /// <param name="text">状态文本</param>
            private void SetStatusSafe(string text)
            {
                _statusLabel.Text = text;
            }

            /// <summary>
            /// 按钮可用性——跨线程安全。
            /// </summary>
            /// <param name="enabled">可用</param>
            private void SetButtonsEnabled(bool enabled)
            {
                if (_prepareButton.InvokeRequired)
                {
                    _prepareButton.BeginInvoke(new Action<bool>(SetButtonsEnabledSafe), enabled);
                }
                else
                {
                    SetButtonsEnabledSafe(enabled);
                }
            }

            /// <summary>
            /// 按钮可用性执行体。
            /// </summary>
            /// <param name="enabled">可用</param>
            private void SetButtonsEnabledSafe(bool enabled)
            {
                _prepareButton.Enabled = enabled;
                _deployButton.Enabled = enabled;
            }
        }

        /// <summary>
        /// TextWriter 桥接——Console 输出转发到日志文本框（跨线程 BeginInvoke）。
        /// </summary>
        internal sealed class TextBoxWriter : TextWriter
        {
            /// <summary>
            /// 目标文本框。
            /// </summary>
            private readonly TextBox _box;

            /// <summary>
            /// 构造。
            /// </summary>
            /// <param name="box">日志文本框</param>
            public TextBoxWriter(TextBox box)
            {
                _box = box;
            }

            /// <summary>
            /// 编码——UTF-8。
            /// </summary>
            public override Encoding Encoding
            {
                get
                {
                    return Encoding.UTF8;
                }
            }

            /// <summary>
            /// 写入字符串——转发到文本框（经 SetUpForm.AppendLog 跨线程安全）。
            /// </summary>
            /// <param name="value">文本</param>
            public override void Write(string value)
            {
                if (value == null)
                {
                    return;
                }
                if (_box.IsDisposed)
                {
                    return;
                }
                if (_box.InvokeRequired)
                {
                    _box.BeginInvoke(new Action<string>(AppendSafe), value);
                }
                else
                {
                    AppendSafe(value);
                }
            }

            /// <summary>
            /// 追加执行体。
            /// </summary>
            /// <param name="value">文本</param>
            private void AppendSafe(string value)
            {
                _box.AppendText(value);
                _box.SelectionStart = _box.TextLength;
                _box.ScrollToCaret();
            }
        }
    }
}
