using log4net;
using System;
using System.Configuration;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Threading;
using System.Windows.Forms;

namespace PCDeviceManage
{
    static class Program
    {
        private const string MutexName = @"Local\PCManage.PCDeviceManage";
        // 旧版本在启动文件夹中创建的快捷方式名（Application.ProductName + ".lnk"）。
        private const string LegacyStartupShortcut = "PCDeviceManage.lnk";

        private static ILog _log;
        private static AppSettings _settings;
        private static WebServer _webServer;
        private static MonitorRegistry _monitorRegistry;
        private static ShutdownScheduler _shutdownScheduler;
        private static FrpTunnel _frpTunnel;

        [STAThread]
        static void Main()
        {
            log4net.GlobalContext.Properties["pid"] = Process.GetCurrentProcess().Id;
            _log = LogManager.GetLogger(typeof(Program));

            bool createdNew;
            using (var mutex = new Mutex(true, MutexName, out createdNew))
            {
                if (!createdNew)
                {
                    MessageBox.Show("PC控制已经在运行，可以在任务栏通知区域找到它的图标。", "PC控制", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);

                bool tokenCreated;
                try
                {
                    _settings = AppSettings.LoadOrCreate(AppSettings.DefaultPath, ConfigurationManager.AppSettings, AppSettings.LegacyTokenPath, out tokenCreated);
                    if (tokenCreated)
                    {
                        _log.Warn("已生成访问令牌并保存到 " + AppSettings.DefaultPath + "，打开控制台时在登录页输入该令牌。");
                    }
                }
                catch (Exception ex)
                {
                    _log.Error("读取配置失败", ex);
                    MessageBox.Show("读取配置失败：" + ex.Message, "PC控制", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                Autostart autostart = Autostart.ForCurrentUser(Application.ExecutablePath);
                try
                {
                    if (autostart.MigrateStartupShortcut(Environment.GetFolderPath(Environment.SpecialFolder.Startup), LegacyStartupShortcut))
                    {
                        _log.Info("已将启动文件夹中的快捷方式迁移为注册表开机自启。");
                    }
                }
                catch (Exception ex)
                {
                    _log.Warn("迁移旧的开机自启快捷方式失败", ex);
                }

                try
                {
                    bool webStarted = Start();
                    using (var tray = new TrayApplicationContext(_settings, _shutdownScheduler, _frpTunnel, autostart, tokenCreated, webStarted))
                    {
                        Application.Run(tray);
                    }
                }
                finally
                {
                    Stop();
                }
            }
        }

        private static bool Start()
        {
            var powerService = new PowerService();
            _shutdownScheduler = new ShutdownScheduler(powerService.Shutdown);

            _monitorRegistry = new MonitorRegistry(new SystemMonitorEnumerator(), _log);
            _monitorRegistry.Start();

            bool webStarted = StartWebServer(powerService);

            _frpTunnel = new FrpTunnel(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "frp"), _settings.FrpEnabled, _log);
            _frpTunnel.Start();
            return webStarted;
        }

        private static bool StartWebServer(PowerService powerService)
        {
            int port = _settings.WebPort;
            try
            {
                if (IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners().Any(x => x.Port == port))
                {
                    _log.Error($"端口 {port} 已被其它程序占用，请在 {AppSettings.DefaultPath} 中更换 webPort。");
                    return false;
                }

                _webServer = new WebServer(_monitorRegistry, new AudioService(), powerService, _shutdownScheduler,
                    new TokenAuthenticator(_settings.AccessToken), port, _settings.WebHost, _log);
                return _webServer.Start();
            }
            catch (Exception ex)
            {
                _log.Error($"开放Web端口{port}失败", ex);
                return false;
            }
        }

        private static void Stop()
        {
            try
            {
                if (_webServer != null)
                {
                    _webServer.Stop();
                    _webServer = null;
                }
                if (_frpTunnel != null)
                {
                    _frpTunnel.Dispose();
                    _frpTunnel = null;
                }
                if (_monitorRegistry != null)
                {
                    _monitorRegistry.Dispose();
                    _monitorRegistry = null;
                }
                if (_shutdownScheduler != null)
                {
                    _shutdownScheduler.Dispose();
                    _shutdownScheduler = null;
                }
            }
            catch (Exception ex)
            {
                _log.Error("停止服务出错", ex);
            }
        }
    }
}
