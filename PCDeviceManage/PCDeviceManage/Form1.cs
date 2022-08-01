using log4net;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace PCDeviceManage
{
    public partial class Form1 : Form
    {
        //日志方法调用
        private static ILog _log = LogManager.GetLogger("Form1");

        [DllImport("user32")]
        private static extern void LockWorkStation();

        public Form1()
        {
            InitializeComponent();
            //添加开机自启
            CreateStartupFolderShortcut();
            //添加开机自启
            //aotuStartup();
            //RegistryKey key = Registry.LocalMachine.OpenSubKey("SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Run", true);//打开注册表子项
            //key.SetValue("PCDeviceManage", this.GetType().Assembly.Location);

            //WshShell shell = new WshShell();
            //IWshShortcut shortcut = (IWshShortcut)shell.CreateShortcut(shortcutPath);
        }

        private void exitButton_Click(object sender, EventArgs e)
        {
            //删除开机自启
            DeleteStartupFolderShortcuts(Path.GetFileName(Application.ExecutablePath));
            System.Environment.Exit(0);
        }

        protected override CreateParams CreateParams
        {
            get
            {
                Hide();
                return base.CreateParams;
            }
        }

        private void aotuStartup()
        {
            // 要设置软件名称，有唯一性要求，最好起特别一些
            string SoftWare = "PCDeviceManage";


            Console.WriteLine("设置开机自启动，需要修改注册表", "提示");
            string path = Application.ExecutablePath;
            RegistryKey rk = Registry.CurrentUser; //
                                                   // 添加到 当前登陆用户的 注册表启动项     
            try
            {
                //  
                //SetValue:存储值的名称   
                RegistryKey rk2 = rk.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");

                // 检测是否之前有设置自启动了，如果设置了，就看值是否一样
                string old_path = (string)rk2.GetValue(SoftWare);
                Console.WriteLine("\r\n注册表值: {0}", old_path);

                if (old_path == null || !path.Equals(old_path))
                {
                    rk2.SetValue(SoftWare, path);
                    Console.WriteLine("添加开启启动成功");
                }

                rk2.Close();
                rk.Close();

            }
            catch (Exception ee)
            {
                Console.WriteLine("开机自启动设置失败");

            }
        }

        #region 设置启动快捷方式
        /// <summary>
        /// 在启动栏添加快捷方式
        /// </summary>
        public void CreateStartupFolderShortcut()
        {
            try
            {
                IWshRuntimeLibrary.WshShellClass wshShell = new IWshRuntimeLibrary.WshShellClass();
                IWshRuntimeLibrary.IWshShortcut shortcut;
                string startUpFolderPath = Environment.GetFolderPath(Environment.SpecialFolder.Startup);

                // Create the shortcut
                shortcut = (IWshRuntimeLibrary.IWshShortcut)wshShell.CreateShortcut(startUpFolderPath + "\\" + Application.ProductName + ".lnk");

                shortcut.TargetPath = Application.ExecutablePath;
                shortcut.WorkingDirectory = Application.StartupPath;
                shortcut.Description = "Launch My Application";
                //      shortcut.IconLocation = Application.StartupPath + @"\App.ico";
                shortcut.Save();
            }
            catch (Exception ex)
            {
                _log.Error("设置系统开机自启失败，请关闭杀毒软件且用管理员身份运行系统", ex);
            }
        }
        #endregion


        #region 删除启动快捷方式
        /// <summary>
        /// 删除启动栏快捷方式
        /// </summary>
        public void DeleteStartupFolderShortcuts(string targetExeName)
        {
            try
            {
                string startUpFolderPath = Environment.GetFolderPath(Environment.SpecialFolder.Startup);

                DirectoryInfo di = new DirectoryInfo(startUpFolderPath);
                FileInfo[] files = di.GetFiles("*.lnk");

                foreach (FileInfo fi in files)
                {
                    string shortcutTargetFile = GetShortcutTargetFile(fi.FullName);
                    Console.WriteLine("{0} -> {1}", fi.Name, shortcutTargetFile);

                    if (shortcutTargetFile.EndsWith(targetExeName, StringComparison.InvariantCultureIgnoreCase))
                    {
                        System.IO.File.Delete(fi.FullName);
                    }
                }
            }
            catch (Exception ex)
            {
                _log.Error("取消系统开机自启失败，请关闭杀毒软件且用管理员身份运行系统", ex);
            }
        }

        public string GetShortcutTargetFile(string shortcutFilename)
        {
            string pathOnly = Path.GetDirectoryName(shortcutFilename);
            string filenameOnly = Path.GetFileName(shortcutFilename);

            Shell32.Shell shell = new Shell32.ShellClass();
            Shell32.Folder folder = shell.NameSpace(pathOnly);
            Shell32.FolderItem folderItem = folder.ParseName(filenameOnly);
            if (folderItem != null)
            {
                Shell32.ShellLinkObject link = (Shell32.ShellLinkObject)folderItem.GetLink;
                return link.Path;
            }

            return String.Empty; // Not found
        }
        #endregion
    }
}
