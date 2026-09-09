using System;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;
using CatHome4.Admin;
using Mau.Runtime;

namespace CH4
{
    /// <summary>
    /// Program 托盘与自启分部——P1：HKCU Run 自启（配置为真相，注册表为物化）+ 系统托盘独立 UI 线程。
    /// 边界：仅交互模式启用；托盘线程不触碰业务状态（ThreadGuard 域外——只经 ConfigStore 锁 / LogStore 并发写者 / 静态只读）。
    /// 退出走主循环 break → Main finally 优雅收尾（禁 Environment.Exit——跳 finally 丢观测缓冲）。
    /// </summary>
    public static partial class Program
    {
        // [段1] 常量与状态
        /// <summary>自启配置键——schema 声明（app.json 群）</summary>
        private const string AutoStartKey = "app.autostart";

        /// <summary>注册表 Run 键路径——HKCU（无管理员权限，信任面增量）</summary>
        private const string AutoStartRunPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

        /// <summary>注册表值名——按 exe 名（部署实例自引用自适应）</summary>
        private const string AutoStartRunName = "CatHome4";

        /// <summary>主循环轮询间隔帧——5 秒（FrameSleepMs=50ms × 100）</summary>
        private const int AutoStartPollFrames = 100;

        /// <summary>退出请求标志——托盘"退出"置位，主循环轮询 break（走 Main finally 优雅收尾）</summary>
        private static volatile bool _trayExitRequested;

        /// <summary>通知弹窗队列——任意线程入队/托盘线程 Timer 消费（BalloonTip 仅 STA 线程安全）</summary>
        private static readonly System.Collections.Concurrent.ConcurrentQueue<string[]> _balloonQueue = new System.Collections.Concurrent.ConcurrentQueue<string[]>();

        /// <summary>自启配置快照——主循环轮询对比（变化才同步注册表）</summary>
        private static string _lastAutoStartValue = "";

        // [段2] 自启初始化与注册表同步——配置为真相，注册表为物化
        /// <summary>
        /// 自启初始化——启动时同步一次注册表（防手动删注册表后漂移）+ 记录配置快照。
        /// 调用点：Bootstrap 完成后（配置群已挂载）。
        /// </summary>
        private static void InitAutoStart()
        {
            ConfigStore cfg = null!;
            if (DataBox.TryResolve<ConfigStore>(out cfg) && cfg != null)
            {
                _lastAutoStartValue = cfg.Get(AutoStartKey, "false");
            }
            else
            {
                _lastAutoStartValue = "false";
            }
            SyncAutoStart();
        }

        /// <summary>
        /// 注册表同步——读配置 app.autostart → 写/删 HKCU Run 值（带引号 exe 路径——路径含空格惯例）。
        /// 幂等：值一致时重复调用无副作用；失败仅 LogStore 警告不崩溃（注册表非关键路径）。
        /// </summary>
        private static void SyncAutoStart()
        {
            string value = "false";
            ConfigStore cfg = null!;
            if (DataBox.TryResolve<ConfigStore>(out cfg) && cfg != null)
            {
                value = cfg.Get(AutoStartKey, "false");
            }
            try
            {
                using (RegistryKey runKey = Registry.CurrentUser.OpenSubKey(AutoStartRunPath, true))
                {
                    if (runKey == null)
                    {
                        LogStore.Add("CatHome4", 2, "自启同步失败: Run 键不存在", "AUTOSTART");
                        return;
                    }
                    if (value == "true" || value == "1")
                    {
                        string exePath = Environment.ProcessPath;
                        if (string.IsNullOrEmpty(exePath))
                        {
                            LogStore.Add("CatHome4", 2, "自启同步失败: 无法解析当前进程路径", "AUTOSTART");
                            return;
                        }
                        runKey.SetValue(AutoStartRunName, "\"" + exePath + "\"");
                    }
                    else
                    {
                        runKey.DeleteValue(AutoStartRunName, false);
                    }
                }
                LogStore.Add("CatHome4", 1, "自启同步: " + value, "AUTOSTART");
            }
            catch (Exception ex)
            {
                LogStore.Add("CatHome4", 2, "自启同步失败: " + ex.Message, "AUTOSTART");
            }
        }

        /// <summary>
        /// 主循环轮询——每 5 秒对比自启配置值变化，变化则同步注册表（覆盖前端/config-* 写路径——零基座改动）。
        /// </summary>
        private static void CheckAutoStartPoll()
        {
            string value = "false";
            ConfigStore cfg = null!;
            if (DataBox.TryResolve<ConfigStore>(out cfg) && cfg != null)
            {
                value = cfg.Get(AutoStartKey, "false");
            }
            if (value != _lastAutoStartValue)
            {
                _lastAutoStartValue = value;
                SyncAutoStart();
            }
        }

        /// <summary>
        /// 自启是否开启——读配置（托盘勾选状态唯一源）
        /// </summary>
        /// <returns>true=开启</returns>
        private static bool IsAutoStartOn()
        {
            ConfigStore cfg = null!;
            if (DataBox.TryResolve<ConfigStore>(out cfg) && cfg != null)
            {
                string value = cfg.Get(AutoStartKey, "false");
                return value == "true" || value == "1";
            }
            return false;
        }

        // [段3] 托盘——独立 STA 线程 + Application.Run 消息泵（主循环零改动）
        /// <summary>
        /// 创建托盘图标——手绘猫脸（橙色圆脸 + 三角耳 + 眼鼻）→ PNG → ICO 容器（Q3：托盘自定义图标）。
        /// 零外部资源零部署改动：System.Drawing 运行时绘制；ICO 头手写（ICONDIR + ICONDIRENTRY + PNG——C# exp §五 ICO 手写经验）。
        /// 失败回退系统默认图标（托盘不因图标崩溃）。
        /// </summary>
        /// <returns>托盘 Icon</returns>
        private static Icon CreateTrayIcon()
        {
            try
            {
                int size = 32;
                using (Bitmap bmp = new Bitmap(size, size))
                {
                    using (Graphics g = Graphics.FromImage(bmp))
                    {
                        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                        g.Clear(Color.Transparent);
                        // 耳朵——左右三角
                        Point[] leftEar = new Point[] { new Point(4, 11), new Point(13, 3), new Point(15, 12) };
                        Point[] rightEar = new Point[] { new Point(28, 11), new Point(19, 3), new Point(17, 12) };
                        using (SolidBrush earBrush = new SolidBrush(Color.FromArgb(255, 200, 120, 40)))
                        {
                            g.FillPolygon(earBrush, leftEar);
                            g.FillPolygon(earBrush, rightEar);
                        }
                        // 脸——橙色圆
                        using (SolidBrush faceBrush = new SolidBrush(Color.FromArgb(255, 230, 140, 50)))
                        {
                            g.FillEllipse(faceBrush, 3, 6, 26, 24);
                        }
                        // 眼睛——深色椭圆
                        using (SolidBrush eyeBrush = new SolidBrush(Color.FromArgb(255, 40, 30, 20)))
                        {
                            g.FillEllipse(eyeBrush, 9, 13, 4, 5);
                            g.FillEllipse(eyeBrush, 19, 13, 4, 5);
                        }
                        // 鼻子——小三角
                        Point[] nose = new Point[] { new Point(14, 20), new Point(18, 20), new Point(16, 23) };
                        using (SolidBrush noseBrush = new SolidBrush(Color.FromArgb(255, 120, 60, 30)))
                        {
                            g.FillPolygon(noseBrush, nose);
                        }
                    }
                    using (System.IO.MemoryStream png = new System.IO.MemoryStream())
                    {
                        bmp.Save(png, System.Drawing.Imaging.ImageFormat.Png);
                        byte[] pngData = png.ToArray();
                        // ICO 容器——ICONDIR(6) + ICONDIRENTRY(16) + PNG 数据（Vista+ 支持 PNG 压缩图标）
                        using (System.IO.MemoryStream ico = new System.IO.MemoryStream())
                        {
                            System.IO.BinaryWriter w = new System.IO.BinaryWriter(ico);
                            w.Write((short)0);          // reserved
                            w.Write((short)1);          // type=icon
                            w.Write((short)1);          // count=1
                            w.Write((byte)size);        // width
                            w.Write((byte)size);        // height
                            w.Write((byte)0);           // colors
                            w.Write((byte)0);           // reserved
                            w.Write((short)1);          // planes
                            w.Write((short)32);         // bitcount
                            w.Write(pngData.Length);    // bytesInRes
                            w.Write(22);                // imageOffset
                            w.Write(pngData);
                            w.Flush();
                            ico.Position = 0;
                            return new Icon(ico);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogStore.Add("CatHome4", 2, "托盘图标生成失败（回退系统图标）: " + ex.Message, "TRAY");
                return SystemIcons.Application;
            }
        }

        /// <summary>
        /// 通知弹窗入队——本轮结束系统通知（Q：开关 app.round_notify 由 ChatSession 判定；跨线程安全——托盘 Timer 消费）。
        /// </summary>
        /// <param name="title">标题（猫 displayName）</param>
        /// <param name="text">正文（末轮回复摘要/Token 统计）</param>
        public static void NotifyBalloon(string title, string text)
        {
            _balloonQueue.Enqueue(new string[] { title, text });
        }

        /// <summary>
        /// 启动托盘线程——仅交互模式调用；后台线程（主线程退出进程即止）。
        /// </summary>
        private static void StartTray()
        {
            Thread trayThread = new Thread(delegate ()
            {
                RunTrayMessageLoop();
            });
            trayThread.SetApartmentState(ApartmentState.STA);
            trayThread.IsBackground = true;
            trayThread.Start();
        }

        /// <summary>
        /// 托盘消息循环——NotifyIcon + ContextMenuStrip（开机自启勾选 / 打开数据目录 / 退出）。
        /// 退出：置标志 + Application.Exit() → 主循环 break → Main finally 优雅收尾。
        /// </summary>
        private static void RunTrayMessageLoop()
        {
            NotifyIcon icon = new NotifyIcon();
            try
            {
                icon.Icon = CreateTrayIcon();
                icon.Text = "CatHome4";
                icon.Visible = true;
                // Q 通知消费——托盘线程 Timer 轮询队列（BalloonTip 仅 STA 线程安全；任意线程入队不越界）
                System.Windows.Forms.Timer balloonTimer = new System.Windows.Forms.Timer();
                balloonTimer.Interval = 1000;
                balloonTimer.Tick += delegate(object s2, EventArgs e2)
                {
                    string[] item;
                    while (_balloonQueue.TryDequeue(out item))
                    {
                        if (item != null && item.Length >= 2 && item[0] != null && item[1] != null)
                        {
                            icon.ShowBalloonTip(3000, item[0], item[1], ToolTipIcon.Info);
                        }
                    }
                };
                balloonTimer.Start();

                ContextMenuStrip menu = new ContextMenuStrip();

                // 开机自启——CheckOnClick 勾选；状态从配置读；点击写配置 + 立即同步注册表
                ToolStripMenuItem autoStartItem = new ToolStripMenuItem("开机自启");
                autoStartItem.CheckOnClick = true;
                autoStartItem.Checked = IsAutoStartOn();
                autoStartItem.Click += delegate (object sender, EventArgs e)
                {
                    string newValue = autoStartItem.Checked ? "true" : "false";
                    ConfigStore cfg = null!;
                    ConfigSchema schema = null!;
                    DataBox.TryResolve<ConfigStore>(out cfg);
                    DataBox.TryResolve<ConfigSchema>(out schema);
                    string error = "";
                    if (cfg == null || !cfg.SetChecked(AutoStartKey, newValue, schema, out error))
                    {
                        autoStartItem.Checked = !autoStartItem.Checked;
                        icon.ShowBalloonTip(3000, "CatHome4", "自启设置失败: " + error, ToolTipIcon.Warning);
                        return;
                    }
                    SyncAutoStart();
                    icon.ShowBalloonTip(1500, "CatHome4", "开机自启已" + (autoStartItem.Checked ? "开启" : "关闭"), ToolTipIcon.Info);
                };
                menu.Items.Add(autoStartItem);

                menu.Items.Add(new ToolStripSeparator());

                // 打开数据目录——复用既有端点逻辑（explorer.exe + LogStore；托盘线程安全）
                ToolStripMenuItem openDataItem = new ToolStripMenuItem("打开数据目录");
                openDataItem.Click += delegate (object sender, EventArgs e)
                {
                    AdminService.HandleOpenDataDir();
                };
                menu.Items.Add(openDataItem);

                menu.Items.Add(new ToolStripSeparator());

                // 退出——优雅收尾（禁 Environment.Exit：跳 finally 丢观测缓冲）
                ToolStripMenuItem exitItem = new ToolStripMenuItem("退出");
                exitItem.Click += delegate (object sender, EventArgs e)
                {
                    _trayExitRequested = true;
                    icon.Visible = false;
                    Application.Exit();
                };
                menu.Items.Add(exitItem);

                icon.ContextMenuStrip = menu;
                Application.Run();
            }
            finally
            {
                icon.Visible = false;
                icon.Dispose();
            }
        }
    }
}
