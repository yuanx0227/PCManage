using log4net;
using Monitorian.Supplement;
using System;
using System.Configuration;
using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Text;
using System.Windows.Forms;

namespace PCDeviceManage
{
    class Program
    {

        private static ILog _log = null;
        private static LocalWebServer _webServer;
        private static DisplayDeviceCache _displayDeviceCache;

        static void Main(string[] args)
        {
            log4net.GlobalContext.Properties["pid"] = Process.GetCurrentProcess().Id;
            _log = LogManager.GetLogger("Program.Main");

            Start();
            Application.ApplicationExit += (sender, eventArgs) => Stop();

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new Form1());
        }

        /// <summary>
        /// 打开frp连接bat
        /// </summary>
        private static void StartFrp()
        {
            try
            {
                System.Diagnostics.ProcessStartInfo myStartInfo = new System.Diagnostics.ProcessStartInfo();
                myStartInfo.FileName = "frp\\frpStart.bat";
                System.Diagnostics.Process myProcess = new System.Diagnostics.Process();
                myProcess.StartInfo = myStartInfo;
                myProcess.StartInfo.WindowStyle = ProcessWindowStyle.Hidden;
                myProcess.Start();
                //myProcess.WaitForExit(); //等待程序退出
                _log.Info("frp端口映射启动成功");

            }
            catch (Exception ex)
            {
                _log.Error("frp启动出错", ex);
            }
        }
        
        /// <summary>
        /// kll Frp进程
        /// </summary>
        private static void StopFrp() 
        {
            
        }

        //打开Web服务端口
        static bool OpenWebServer()
        {
            int port = ReadIntSetting("WebPort", ReadIntSetting("Port", 9527));
            string host = ConfigurationManager.AppSettings["WebHost"];
            try
            {
                //开放端口前先判断该端口是否已存在。
                IPGlobalProperties properties = IPGlobalProperties.GetIPGlobalProperties();
                IPEndPoint[] ipendpoints = properties.GetActiveTcpListeners();
                foreach (IPEndPoint ipendpoint in ipendpoints)
                {
                    if (ipendpoint.Port == port)
                    {
                        _log.Warn($"检测到{port}端口已被使用，若网页无法正常访问请检查该端口是否被其它程序所使用并考虑更换端口号。");
                        return true;
                    }
                }

                if (_displayDeviceCache == null)
                {
                    _displayDeviceCache = new DisplayDeviceCache(new DisplayDeviceProvider(), _log);
                    _displayDeviceCache.Start();
                }

                _webServer = new LocalWebServer(new DisplayManage(_displayDeviceCache), new AudioManage(), new WindowsManage(), port, host, _log);
                return _webServer.Start();
            }
            catch (Exception ex)
            {
                _log.Error($"开放Web端口{port}失败", ex);
                return false;
            }
        }

        static int ReadIntSetting(string key, int defaultValue)
        {
            try
            {
                int value;
                return int.TryParse(ConfigurationManager.AppSettings[key], out value) ? value : defaultValue;
            }
            catch
            {
                return defaultValue;
            }
        }

        static void Stop()
        {
            try
            {
                if (_webServer != null)
                {
                    _webServer.Stop();
                    _webServer = null;
                }
                if (_displayDeviceCache != null)
                {
                    _displayDeviceCache.Dispose();
                    _displayDeviceCache = null;
                }
                StopFrp();
            }
            catch (Exception ex)
            {
                _log.Error("停止服务出错", ex);
            }
        }

        public static void Start()
        {
            //打开Web服务
            OpenWebServer();

            //打开Frp端口映射
            StartFrp();

            //To do something
        }

        public class Service
        {
            public void Start()
            {
                OpenWebServer();

                //To do something
            }
            public void Stop()
            {
                Program.Stop();
            }
        }

        /// <summary>
        /// 验证某个IP是否可ping通
        /// </summary>
        /// <param name="strIP">要验证的IP</param>
        /// <returns>是否可连通  是：true 否：false</returns>
        public static bool TestNetConnectity(string strIP)
        {
            if (!CheckIPAddr(strIP))
            {
                return false;
            }
            // Windows L2TP VPN和非Windows VPN使用ping VPN服务端的方式获取是否可以连通
            Ping pingSender = new Ping();
            PingOptions options = new PingOptions();

            // Use the default Ttl value which is 128,
            // but change the fragmentation behavior.
            options.DontFragment = true;

            // Create a buffer of 32 bytes of data to be transmitted.
            string data = "testtesttesttesttesttesttesttest";
            byte[] buffer = Encoding.ASCII.GetBytes(data);
            int timeout = 120;
            PingReply reply = pingSender.Send(strIP, timeout, buffer, options);

            return (reply.Status == IPStatus.Success);
        }

        /// <summary>
        /// 验证IP地址字符串的正确性
        /// </summary>
        /// <param name="strIP">要验证的IP地址字符串</param>
        /// <returns>格式是否正确，正确为 true 否则为 false</returns>
        public static bool CheckIPAddr(string strIP)
        {
            if (string.IsNullOrEmpty(strIP))
            {
                return false;
            }
            // 保持要返回的信息
            bool bRes = true;
            int iTmp = 0;    // 保持每个由“.”分隔的整型
            string[] ipSplit = strIP.Split('.');
            if (ipSplit.Length < 4 || string.IsNullOrEmpty(ipSplit[0]) ||
                string.IsNullOrEmpty(ipSplit[1]) ||
                string.IsNullOrEmpty(ipSplit[2]) ||
                string.IsNullOrEmpty(ipSplit[3]))
            {
                bRes = false;
            }
            else
            {
                for (int i = 0; i < ipSplit.Length; i++)
                {
                    if (!int.TryParse(ipSplit[i], out iTmp) || iTmp < 0 || iTmp > 255)
                    {
                        bRes = false;
                        break;
                    }
                }
            }

            return bRes;
        }

    }
}
