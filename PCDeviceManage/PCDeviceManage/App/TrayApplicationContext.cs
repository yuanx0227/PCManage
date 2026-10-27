using log4net;
using PCDeviceManage.Devices.Power;
using PCDeviceManage.Tunnel;
using System;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

namespace PCDeviceManage.App
{
    /// <summary>
    /// 托盘图标和菜单。程序没有主窗口，生命周期由这里控制：选择“退出”后 Application.Run 返回。
    /// </summary>
    public sealed class TrayApplicationContext : ApplicationContext
    {
        private static readonly ILog _log = LogManager.GetLogger(typeof(TrayApplicationContext));

        private readonly AppSettings _settings;
        private readonly ShutdownScheduler _scheduler;
        private readonly FrpTunnel _frpTunnel;
        private readonly Autostart _autostart;

        // 用于把计时器线程上的事件切回 UI 线程。
        private readonly Control _ui;
        private readonly NotifyIcon _icon;
        private readonly ContextMenuStrip _menu;
        private readonly ToolStripMenuItem _scheduleStatus;
        private readonly ToolStripMenuItem _cancelSchedule;
        private readonly ToolStripMenuItem _frpStatus;
        private readonly ToolStripMenuItem _restartFrp;
        private readonly ToolStripMenuItem _autostartItem;
        private bool _balloonCancelsShutdown;

        public TrayApplicationContext(AppSettings settings, ShutdownScheduler scheduler, FrpTunnel frpTunnel, Autostart autostart, bool tokenCreated, bool webStarted)
        {
            _settings = settings;
            _scheduler = scheduler;
            _frpTunnel = frpTunnel;
            _autostart = autostart;

            _ui = new Control();
            _ = _ui.Handle;

            _menu = new ContextMenuStrip();
            var openConsole = new ToolStripMenuItem("打开控制台", null, (s, e) => OpenConsole());
            openConsole.Font = new Font(openConsole.Font, FontStyle.Bold);
            _scheduleStatus = new ToolStripMenuItem { Enabled = false };
            _cancelSchedule = new ToolStripMenuItem("取消定时关机", null, (s, e) => _scheduler.Cancel());
            _frpStatus = new ToolStripMenuItem { Enabled = false };
            _restartFrp = new ToolStripMenuItem("重启 frp", null, (s, e) => _frpTunnel.Restart());
            _autostartItem = new ToolStripMenuItem("开机自启", null, (s, e) => ToggleAutostart());

            _menu.Items.AddRange(new ToolStripItem[]
            {
                openConsole,
                new ToolStripMenuItem("复制访问令牌", null, (s, e) => CopyToken()),
                new ToolStripSeparator(),
                _scheduleStatus,
                _cancelSchedule,
                new ToolStripSeparator(),
                _frpStatus,
                _restartFrp,
                new ToolStripSeparator(),
                _autostartItem,
                new ToolStripMenuItem("打开日志目录", null, (s, e) => OpenFolder(System.IO.Path.Combine(AppSettings.DataDirectory, "Log"))),
                new ToolStripMenuItem("打开配置文件", null, (s, e) => OpenFile(AppSettings.DefaultPath)),
                new ToolStripSeparator(),
                new ToolStripMenuItem("退出", null, (s, e) => ExitThread())
            });
            _menu.Opening += (s, e) => RefreshMenu();

            _icon = new NotifyIcon
            {
                Icon = LoadIcon(),
                Text = "PC控制",
                ContextMenuStrip = _menu,
                Visible = true
            };
            _icon.DoubleClick += (s, e) => OpenConsole();
            _icon.BalloonTipClicked += OnBalloonClicked;
            _icon.BalloonTipClosed += (s, e) => _balloonCancelsShutdown = false;

            _scheduler.Warning += OnShutdownWarning;

            if (!webStarted)
            {
                ShowBalloon("控制台启动失败", "Web 服务没有启动，详细原因见日志。", ToolTipIcon.Error);
            }
            else if (tokenCreated)
            {
                ShowBalloon("PC控制已启动", "已生成访问令牌。右键托盘图标选择“复制访问令牌”，在登录页粘贴即可。", ToolTipIcon.Info);
            }
        }

        private void RefreshMenu()
        {
            DateTime? deadline = _scheduler.Deadline;
            _scheduleStatus.Text = deadline.HasValue
                ? "定时关机：" + deadline.Value.ToLocalTime().ToString("HH:mm") + " 关机"
                : "定时关机：未设置";
            _cancelSchedule.Enabled = deadline.HasValue;

            FrpStatus frp = _frpTunnel.Status;
            _frpStatus.Text = "frp：" + Describe(frp);
            _restartFrp.Enabled = frp != FrpStatus.NotConfigured && frp != FrpStatus.Disabled;

            _autostartItem.Checked = _autostart.IsEnabled;
        }

        private static string Describe(FrpStatus status)
        {
            switch (status)
            {
                case FrpStatus.NotConfigured: return "未配置";
                case FrpStatus.Disabled: return "已关闭";
                case FrpStatus.Starting: return "正在连接";
                case FrpStatus.Connected: return "已连接";
                case FrpStatus.Reconnecting: return "连接断开，等待重连";
                default: return "已停止";
            }
        }

        private void OnShutdownWarning(object sender, ShutdownWarningEventArgs e)
        {
            _ui.BeginInvoke((Action)(() =>
            {
                ShowBalloon("即将关机",
                    "电脑将在 " + e.Deadline.ToLocalTime().ToString("HH:mm:ss") + " 关机，点击此通知取消。",
                    ToolTipIcon.Warning);
                _balloonCancelsShutdown = true;
            }));
        }

        private void OnBalloonClicked(object sender, EventArgs e)
        {
            if (_balloonCancelsShutdown)
            {
                _balloonCancelsShutdown = false;
                _scheduler.Cancel();
                ShowBalloon("已取消定时关机", "可以在控制台重新设置。", ToolTipIcon.Info);
            }
        }

        private void ShowBalloon(string title, string text, ToolTipIcon icon)
        {
            _balloonCancelsShutdown = false;
            _icon.ShowBalloonTip(10000, title, text, icon);
        }

        private void ToggleAutostart()
        {
            try
            {
                if (_autostart.IsEnabled)
                {
                    _autostart.Disable();
                }
                else
                {
                    _autostart.Enable();
                }
            }
            catch (Exception ex)
            {
                _log.Error("修改开机自启失败", ex);
                MessageBox.Show("修改开机自启失败：" + ex.Message, "PC控制", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void OpenConsole()
        {
            Launch(ConsoleUrl(_settings));
        }

        private void CopyToken()
        {
            try
            {
                Clipboard.SetText(_settings.AccessToken);
                ShowBalloon("已复制访问令牌", "在控制台登录页粘贴即可。", ToolTipIcon.Info);
            }
            catch (Exception ex)
            {
                _log.Error("复制访问令牌失败", ex);
            }
        }

        /// <summary>浏览器访问本机控制台的地址；监听所有地址时用 127.0.0.1。</summary>
        public static string ConsoleUrl(AppSettings settings)
        {
            string host = settings.WebHost;
            if (host == "+" || host == "*" || host == "0.0.0.0")
            {
                host = "127.0.0.1";
            }
            return "http://" + host + ":" + settings.WebPort + "/";
        }

        private static void OpenFolder(string path)
        {
            System.IO.Directory.CreateDirectory(path);
            Launch(path);
        }

        private static void OpenFile(string path)
        {
            Launch("notepad.exe", "\"" + path + "\"");
        }

        private static void Launch(string target, string arguments = null)
        {
            try
            {
                Process.Start(new ProcessStartInfo(target, arguments ?? string.Empty) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                _log.Error("打开 " + target + " 失败", ex);
            }
        }

        private static Icon LoadIcon()
        {
            using (var stream = typeof(TrayApplicationContext).Assembly.GetManifestResourceStream("PCDeviceManage.app.ico"))
            {
                return stream != null ? new Icon(stream) : SystemIcons.Application;
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _scheduler.Warning -= OnShutdownWarning;
                _icon.Visible = false;
                _icon.Dispose();
                _menu.Dispose();
                _ui.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
