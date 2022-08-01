using log4net;
using Monitorian.Supplement;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Topshelf;
using Zyan.Communication;
using Zyan.Communication.Protocols.Tcp;
using ZyanInterface;

namespace PCDeviceManage
{
    class Program
    {

        private static ILog _log = null;

        static void Main(string[] args)
        {
            log4net.GlobalContext.Properties["pid"] = Process.GetCurrentProcess().Id;
            _log = LogManager.GetLogger("Program.Main");

            Start();

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new Form1());
            //try
            //{
            //    // 配置和运行宿主服务
            //    HostFactory.Run(x =>                                 //1
            //    {
            //        x.Service<Service>(s =>                        //2
            //        {
            //            // 指定服务类型。这里设置为 Service
            //            s.ConstructUsing(name => new Service());     //3

            //            // 当服务启动后执行什么
            //            s.WhenStarted(tc => tc.Start());              //4

            //            // 当服务停止后执行什么
            //            s.WhenStopped(tc => tc.Stop());               //5
            //        });

            //        // 服务用本地系统账号来运行
            //        x.RunAsLocalSystem();                            //6

            //        // 服务描述信息
            //        x.SetDescription("远程设备管理服务");        //7
            //        // 服务显示名称
            //        x.SetDisplayName("PCDeviceManageServer");                       //8
            //        // 服务名称
            //        x.SetServiceName("PCDeviceManageServer");                       //9 
            //    });
            //}
            //catch (Exception ex)
            //{
            //    _log.Error($"初始化服务失败。", ex);
            //    // Console.WriteLine(ex);
            //}

            //运行完后不自动退出
            //Console.ReadLine(); 
        }

        //打开Zyan服务端口
        static bool openZyan()
        {
            int Port = int.Parse(ConfigurationManager.AppSettings["Port"]);

            try
            {

                //开放端口前先判断该端口是否已存在。
                IPGlobalProperties properties = IPGlobalProperties.GetIPGlobalProperties();
                IPEndPoint[] ipendpoints = properties.GetActiveTcpListeners();
                foreach (IPEndPoint ipendpoint in ipendpoints)
                {
                    if (ipendpoint.Port == Port)
                    {
                        _log.Warn($"检测到{Port}端口已被使用，若接口无法正常调用请检查该端口是否被其它程序所使用并考虑更换端口号。");
                        return true;
                        //break;
                    }
                }
                //开放端口
                var protocol = new TcpDuplexServerProtocolSetup(Port, null, true);
                var host = new ZyanComponentHost("PCDeviceManage", protocol);
                //var host = new ZyanComponentHost("ZyanServer",Port);
                // 注册组件类型和接口主机。
                //var server = new ZyanServer();
                host.RegisterComponent<DisplayInterface, DisplayManage>(ActivationType.Singleton);
                host.RegisterComponent<AudioInterface, AudioManage>(ActivationType.Singleton);
                host.RegisterComponent<SystemInterface, WindowsManage>(ActivationType.Singleton);
                _log.Warn($"开放Zyan端口{Port}成功");
                return true;
            }
            catch (Exception ex)
            {
                _log.Error($"开放Zyan端口{Port}失败", ex);
                return false;
            }



        }
        public static void Start()
        {
            openZyan();

            //启动时需获取一次显示器信息，否则没有外接显示器的指针
            Task.Run(() => {
                DisplayInfoGet displayInfoGet = new DisplayInfoGet();
                displayInfoGet.GetDisplayListAndBrightness();
            });
            //To do something
        }
        public class Service
        {
            public void Start()
            {
                openZyan();

                //启动时需获取一次显示器信息，否则没有外接显示器指针
                Task.Run(()=> {
                    DisplayInfoGet displayInfoGet = new DisplayInfoGet();
                     displayInfoGet.GetDisplayListAndBrightness();
                });
                //To do something
            }
            public void Stop()
            {
                //To do something
            }
        }

    }
}
