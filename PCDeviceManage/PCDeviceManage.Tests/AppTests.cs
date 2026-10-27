using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using static PCDeviceManage.Tests.TestSupport;

namespace PCDeviceManage.Tests
{
    public class AppTests
    {
        // 测试只读写 HKCU\Software\PCManageTests 下的临时键，不会改动真正的开机启动项。
        private static void WithTestRunKey(Action<Autostart, string> action)
        {
            string keyPath = @"Software\PCManageTests\" + Guid.NewGuid().ToString("N");
            try
            {
                action(new Autostart(Microsoft.Win32.Registry.CurrentUser, keyPath, "PCManage", @"C:\Apps\PC Manage\PCDeviceManage.exe"), keyPath);
            }
            finally
            {
                Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(@"Software\PCManageTests", false);
            }
        }

        [Fact]
        public void AutostartTogglesRunValue()
        {
            WithTestRunKey((autostart, keyPath) =>
            {
                Assert.True(!autostart.IsEnabled, "Expected autostart to be off without a key.");
                autostart.Enable();
                Assert.True(autostart.IsEnabled, "Expected autostart to be on after Enable.");
                using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(keyPath))
                {
                    Assert.True((string)key.GetValue("PCManage") == "\"C:\\Apps\\PC Manage\\PCDeviceManage.exe\"", "Expected a quoted executable path.");
                }

                var other = new Autostart(Microsoft.Win32.Registry.CurrentUser, keyPath, "PCManage", @"D:\Other\PCDeviceManage.exe");
                Assert.True(!other.IsEnabled, "Expected an entry for another copy not to count as enabled.");

                autostart.Disable();
                autostart.Disable();
                Assert.True(!autostart.IsEnabled, "Expected autostart to be off after Disable.");
            });
        }

        [Fact]
        public void AutostartMigratesLegacyShortcut()
        {
            WithTestRunKey((autostart, keyPath) =>
            {
                using (var dir = new TempDirectory())
                {
                    Assert.True(!autostart.MigrateStartupShortcut(dir.Path, "PCDeviceManage.lnk"), "Expected nothing to migrate without a shortcut.");
                    Assert.True(!autostart.IsEnabled, "Expected autostart to stay off.");

                    File.WriteAllText(dir.File("PCDeviceManage.lnk"), string.Empty);
                    Assert.True(autostart.MigrateStartupShortcut(dir.Path, "PCDeviceManage.lnk"), "Expected the shortcut to be migrated.");
                    Assert.True(autostart.IsEnabled && !File.Exists(dir.File("PCDeviceManage.lnk")), "Expected the shortcut to be replaced by the Run value.");
                }
            });
        }

        [Fact]
        public void TrayResourcesAndConsoleUrl()
        {
            using (Stream icon = typeof(TrayApplicationContext).Assembly.GetManifestResourceStream("PCDeviceManage.app.ico"))
            {
                Assert.True(icon != null && icon.Length > 0, "Expected the tray icon to be embedded.");
            }
            Assert.True(TrayApplicationContext.ConsoleUrl(new AppSettings { WebHost = "+", WebPort = 9527 }) == "http://127.0.0.1:9527/", "Expected wildcard host to open localhost.");
            Assert.True(TrayApplicationContext.ConsoleUrl(new AppSettings { WebHost = "192.168.1.5", WebPort = 80 }) == "http://192.168.1.5:80/", "Expected explicit host to be used.");
        }

        [Fact]
        public void SettingsGenerateTokenOnceAndReuse()
        {
            using (var dir = new TempDirectory())
            {
                bool created;
                AppSettings first = AppSettings.LoadOrCreate(dir.File("settings.json"), null, out created);
                Assert.True(created && first.AccessToken.Length >= 32, "Expected a random token to be generated.");
                Assert.True(first.WebPort == 9527 && first.WebHost == "127.0.0.1" && first.FrpEnabled, "Expected defaults.");

                AppSettings second = AppSettings.LoadOrCreate(dir.File("settings.json"), null, out created);
                Assert.True(!created && second.AccessToken == first.AccessToken, "Expected the saved token to be reused.");
            }
        }

        [Fact]
        public void SettingsMigrateLegacyTokenFile()
        {
            using (var dir = new TempDirectory())
            {
                string legacyToken = dir.File("access_token");
                File.WriteAllText(legacyToken, "token-from-file\r\n");

                bool created;
                AppSettings settings = AppSettings.LoadOrCreate(dir.File("settings.json"), legacyToken, out created);
                Assert.True(!created && settings.AccessToken == "token-from-file", "Expected the P0 token file to be migrated.");
                Assert.True(!File.Exists(legacyToken), "Expected the legacy token file to be removed.");

                File.WriteAllText(legacyToken, "stale-token");
                AppSettings again = AppSettings.LoadOrCreate(dir.File("settings.json"), legacyToken, out created);
                Assert.True(again.AccessToken == "token-from-file", "Expected an existing settings file to win over a stale token file.");
            }
        }

        [Fact]
        public void SettingsFixInvalidValues()
        {
            using (var dir = new TempDirectory())
            {
                File.WriteAllText(dir.File("settings.json"), "{ \"webPort\": 70000, \"webHost\": \" \", \"accessToken\": \"keep-me\", \"frpEnabled\": false }");
                bool created;
                AppSettings settings = AppSettings.LoadOrCreate(dir.File("settings.json"), null, out created);
                Assert.True(settings.WebPort == 9527 && settings.WebHost == "127.0.0.1", "Expected invalid port/host to fall back to defaults.");
                Assert.True(!created && settings.AccessToken == "keep-me" && !settings.FrpEnabled, "Expected valid values to be kept.");
                Assert.True(File.ReadAllText(dir.File("settings.json")).Contains("9527"), "Expected the corrected file to be saved.");
            }
        }

        [Fact]
        public void SettingsRejectCorruptFile()
        {
            using (var dir = new TempDirectory())
            {
                File.WriteAllText(dir.File("settings.json"), "{ not json");
                try
                {
                    bool created;
                    AppSettings.LoadOrCreate(dir.File("settings.json"), null, out created);
                    Assert.True(false, "Expected corrupt settings to throw.");
                }
                catch (InvalidDataException)
                {
                }
                Assert.True(File.ReadAllText(dir.File("settings.json")) == "{ not json", "Expected the corrupt file to be left untouched.");
            }
        }
    }
}
