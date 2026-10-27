using Microsoft.Win32;
using System;
using System.IO;

namespace PCDeviceManage.App
{
    /// <summary>
    /// 开机自启：写 HKCU\Software\Microsoft\Windows\CurrentVersion\Run，不需要管理员权限，也不再依赖 WSH/Shell32 COM 组件。
    /// </summary>
    public sealed class Autostart
    {
        public const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        public const string DefaultValueName = "PCManage";

        private readonly RegistryKey _root;
        private readonly string _keyPath;
        private readonly string _valueName;
        private readonly string _executablePath;

        public Autostart(RegistryKey root, string keyPath, string valueName, string executablePath)
        {
            _root = root ?? throw new ArgumentNullException(nameof(root));
            _keyPath = keyPath;
            _valueName = valueName;
            _executablePath = executablePath;
        }

        public static Autostart ForCurrentUser(string executablePath)
        {
            return new Autostart(Registry.CurrentUser, RunKeyPath, DefaultValueName, executablePath);
        }

        /// <summary>注册表中存在指向当前 exe 的启动项。</summary>
        public bool IsEnabled
        {
            get
            {
                using (RegistryKey key = _root.OpenSubKey(_keyPath, false))
                {
                    string value = key?.GetValue(_valueName) as string;
                    return value != null &&
                        string.Equals(value.Trim().Trim('"'), _executablePath, StringComparison.OrdinalIgnoreCase);
                }
            }
        }

        public void Enable()
        {
            using (RegistryKey key = _root.CreateSubKey(_keyPath))
            {
                key.SetValue(_valueName, "\"" + _executablePath + "\"", RegistryValueKind.String);
            }
        }

        public void Disable()
        {
            using (RegistryKey key = _root.OpenSubKey(_keyPath, true))
            {
                key?.DeleteValue(_valueName, false);
            }
        }

        /// <summary>
        /// 旧版本每次启动都会在启动文件夹创建快捷方式、退出时删除。存在该快捷方式说明用户正在使用开机自启：
        /// 删除它并改为注册表启动项，返回是否做了迁移。
        /// </summary>
        public bool MigrateStartupShortcut(string startupFolder, string shortcutName)
        {
            string shortcut = Path.Combine(startupFolder, shortcutName);
            if (!File.Exists(shortcut))
            {
                return false;
            }

            Enable();
            File.Delete(shortcut);
            return true;
        }
    }
}
