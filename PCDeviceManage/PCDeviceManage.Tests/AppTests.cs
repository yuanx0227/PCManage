using PCDeviceManage.App;
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
                AppSettings first = AppSettings.LoadOrCreate(dir.File("settings.json"), dir.File("app\\access_token"), null, out created);
                Assert.True(created && first.AccessToken.Length >= 32, "Expected a random token to be generated.");
                Assert.True(first.WebPort == 9527 && first.WebHost == "127.0.0.1" && first.FrpEnabled, "Expected defaults.");
                Assert.Equal(first.AccessToken, File.ReadAllText(dir.File("app\\access_token")));
                Assert.DoesNotContain("accessToken", File.ReadAllText(dir.File("settings.json")));

                AppSettings second = AppSettings.LoadOrCreate(dir.File("settings.json"), dir.File("app\\access_token"), null, out created);
                Assert.True(!created && second.AccessToken == first.AccessToken, "Expected the saved token to be reused.");
            }
        }

        [Fact]
        public void SettingsTokenFileCanBeEdited()
        {
            using (var dir = new TempDirectory())
            {
                File.WriteAllText(dir.File("access_token"), "  my-own-token\r\n");
                bool created;
                AppSettings settings = AppSettings.LoadOrCreate(dir.File("settings.json"), dir.File("access_token"), null, out created);
                Assert.True(!created && settings.AccessToken == "my-own-token", "Expected the token file to be used as-is (trimmed).");
            }
        }

        [Fact]
        public void SettingsMigrateTokenFromSettingsJson()
        {
            using (var dir = new TempDirectory())
            {
                File.WriteAllText(dir.File("settings.json"), "{ \"webPort\": 9600, \"accessToken\": \"token-from-settings\" }");
                bool created;
                AppSettings settings = AppSettings.LoadOrCreate(dir.File("settings.json"), dir.File("access_token"), null, out created);
                Assert.True(!created && settings.AccessToken == "token-from-settings", "Expected the P1/P2 token to be migrated.");
                Assert.Equal("token-from-settings", File.ReadAllText(dir.File("access_token")));
                string json = File.ReadAllText(dir.File("settings.json"));
                Assert.DoesNotContain("accessToken", json);
                Assert.Contains("9600", json);
            }
        }

        [Fact]
        public void SettingsMigrateLegacyTokenFile()
        {
            using (var dir = new TempDirectory())
            {
                string legacy = dir.File("legacy");
                Directory.CreateDirectory(legacy);
                File.WriteAllText(Path.Combine(legacy, "access_token"), "token-from-file\r\n");

                bool created;
                AppSettings settings = AppSettings.LoadOrCreate(dir.File("settings.json"), dir.File("access_token"), legacy, out created);
                Assert.True(!created && settings.AccessToken == "token-from-file", "Expected the P0 token file to be migrated.");
                Assert.Equal("token-from-file", File.ReadAllText(dir.File("access_token")));

                File.WriteAllText(Path.Combine(legacy, "access_token"), "stale-token");
                AppSettings again = AppSettings.LoadOrCreate(dir.File("settings.json"), dir.File("access_token"), legacy, out created);
                Assert.True(again.AccessToken == "token-from-file", "Expected the program-directory token to win over a stale legacy file.");
            }
        }

        [Fact]
        public void SettingsMigrateLegacyDirectoryThenDeleteIt()
        {
            using (var dir = new TempDirectory())
            {
                string legacy = dir.File("legacy");
                Directory.CreateDirectory(Path.Combine(legacy, "Log"));
                File.WriteAllText(Path.Combine(legacy, "Log", "20261027.Log"), "old log");
                File.WriteAllText(Path.Combine(legacy, "settings.json"), "{ \"webHost\": \"+\", \"webPort\": 9600, \"frpEnabled\": false, \"accessToken\": \"old-token\" }");

                bool created;
                AppSettings settings = AppSettings.LoadOrCreate(dir.File("app\\settings.json"), dir.File("app\\access_token"), legacy, out created);
                Assert.True(!created && settings.AccessToken == "old-token", "Expected the legacy token to be migrated.");
                Assert.True(settings.WebHost == "+" && settings.WebPort == 9600 && !settings.FrpEnabled, "Expected legacy settings to be migrated.");
                Assert.Contains("9600", File.ReadAllText(dir.File("app\\settings.json")));
                Assert.Equal("old-token", File.ReadAllText(dir.File("app\\access_token")));

                Assert.True(AppSettings.DeleteLegacyDirectory(legacy), "Expected the legacy directory to be deleted.");
                Assert.False(Directory.Exists(legacy));
                Assert.False(AppSettings.DeleteLegacyDirectory(legacy), "Expected a second delete to be a no-op.");

                // 新位置已有配置时，不再读取旧目录。
                Directory.CreateDirectory(legacy);
                File.WriteAllText(Path.Combine(legacy, "settings.json"), "{ \"webPort\": 1234 }");
                AppSettings again = AppSettings.LoadOrCreate(dir.File("app\\settings.json"), dir.File("app\\access_token"), legacy, out created);
                Assert.Equal(9600, again.WebPort);
            }
        }

        [Fact]
        public void SettingsFixInvalidValues()
        {
            using (var dir = new TempDirectory())
            {
                File.WriteAllText(dir.File("settings.json"), "{ \"webPort\": 70000, \"webHost\": \" \", \"frpEnabled\": false }");
                File.WriteAllText(dir.File("access_token"), "keep-me");
                bool created;
                AppSettings settings = AppSettings.LoadOrCreate(dir.File("settings.json"), dir.File("access_token"), null, out created);
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
                bool created;
                Assert.Throws<InvalidDataException>(() => AppSettings.LoadOrCreate(dir.File("settings.json"), dir.File("access_token"), null, out created));
                Assert.True(File.ReadAllText(dir.File("settings.json")) == "{ not json", "Expected the corrupt file to be left untouched.");
            }
        }
    }
}
