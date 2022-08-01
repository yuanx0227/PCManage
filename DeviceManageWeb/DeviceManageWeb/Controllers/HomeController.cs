using log4net;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Web;
using System.Web.Mvc;
using Zyan.Communication;
using Zyan.Communication.Protocols.Tcp;
using ZyanInterface;

namespace DeviceManageWeb.Controllers
{
    public class HomeController : Controller
    {

        static int shutdownTimingVal = 0;

        static ZyanConnection _conn;
        static DisplayInterface _displayProxy;
        static AudioInterface _audioInterface;
        static SystemInterface _systemInterface;
        static bool _haveConnected = false;

        //日志方法调用
        private static ILog _log = LogManager.GetLogger("HomeController");

        public ActionResult Index()
        {
            return View();
        }

        public ActionResult About()
        {
            ViewBag.Message = "Your application description page.";

            return View();
        }

        public ActionResult Contact()
        {
            ViewBag.Message = "Your contact page.";

            return View();
        }

        public ActionResult GetDisplayDeviceInfo() 
        {
            try
            {
                if (!_haveConnected || !_conn.IsSessionValid)
                {
                    if (!InitProxy("127.0.0.1:15092"))
                    {
                        return Content("error");
                    }
                }

                //var temp =_proxy.GetDisplayList();

                //获取接口返回对象
                return Content(_displayProxy.GetDisplayList().ToString());
            }
            catch (Exception ex)
            {
                _haveConnected = false;
                return Content(ex.Message);
            }
        }

        public ActionResult SetBrightness(int brightness,bool IsInternal)
        {
            try
            {
                if (!_haveConnected || !_conn.IsSessionValid)
                {
                    if (!InitProxy("127.0.0.1:15092"))
                    {
                        return Content("error");
                    }
                }
                //_log.Info(brightness);
                //var temp =_proxy.GetDisplayList();

                //获取接口返回对象
                return Content(_displayProxy.SetDisplayBrightness(brightness, IsInternal).ToString());
            }
            catch (Exception ex)
            {
                _haveConnected = false;
                return Content(ex.Message);
            }
        }

        public ActionResult GetVolume()
        {
            try
            {
                if (!_haveConnected || !_conn.IsSessionValid)
                {
                    if (!InitProxy("127.0.0.1:15092"))
                    {
                        return Content("0");
                    }
                }

                //var temp =_proxy.GetDisplayList();

                //获取接口返回对象
                return Content(_audioInterface.GetVolume().ToString());
            }
            catch (Exception ex)
            {
                _haveConnected = false;
                return Content("0");
            }
        }

        public ActionResult SetVolume(int volume)
        {
            try
            {
                if (!_haveConnected || !_conn.IsSessionValid)
                {
                    if (!InitProxy("127.0.0.1:15092"))
                    {
                        return Content("0");
                    }
                }

                //var temp =_proxy.GetDisplayList();

                //获取接口返回对象
                return Content(_audioInterface.SetAudioVolume(volume, "").ToString());
            }
            catch (Exception ex)
            {
                _haveConnected = false;
                return Content("0");
            }
        }

        public ActionResult LockWindows() 
        {
            try
            {
                if (!_haveConnected || !_conn.IsSessionValid)
                {
                    if (!InitProxy("127.0.0.1:15092"))
                    {
                        return Content("0");
                    }
                }
                var temp= Request;
                _systemInterface.LockWindows();
                var ip = GetIp();
                _log.Info($"锁屏指令发送成功，客户端IP：{ip} Mac：{GetMac(ip)}");
                return Content("true");
            }
            catch (Exception ex)
            {
                _log.Error("请求锁屏出错",ex);
                _haveConnected = false;
                return Content("0");
            }
        }

        public ActionResult WindowsShutdown()
        {
            try
            {
                if (!_haveConnected || !_conn.IsSessionValid)
                {
                    if (!InitProxy("127.0.0.1:15092"))
                    {
                        return Content("0");
                    }
                }

                _systemInterface.WindowsShutdown();
                return Content("true");
            }
            catch (Exception ex)
            {
                _haveConnected = false;
                return Content("0");
            }
        }

        public ActionResult WindowsRestart()
        {
            try
            {
                if (!_haveConnected || !_conn.IsSessionValid)
                {
                    if (!InitProxy("127.0.0.1:15092"))
                    {
                        return Content("0");
                    }
                }

                _systemInterface.WindowsRestart();
                return Content("true");
            }
            catch (Exception ex)
            {
                _haveConnected = false;
                return Content("0");
            }
        }

        //定义Timer类
        static System.Timers.Timer timer;
        public ActionResult TimingShutdown(int minute)
        {
            try
            {
                if (timer == null)
                {
                    timer = new System.Timers.Timer();
                    //设置定时间隔(毫秒为单位)
                    int interval = 1000;
                    timer = new System.Timers.Timer(interval);
                    //设置执行一次（false）还是一直执行(true)
                    timer.AutoReset = true;
                    //设置是否执行System.Timers.Timer.Elapsed事件
                    timer.Enabled = true;
                    //绑定Elapsed事件
                    timer.Elapsed += new System.Timers.ElapsedEventHandler(TimerUp);
                }
                    HourCount = 0;
                if (minute == 0)
                {
                    timer.Stop();
                    shutdownTimingVal = 0;

                }
                else
                {
                    shutdownTimingVal = minute;
                    timer.Start();
                }

                return Content("true");
            }
            catch (Exception ex)
            {
                timer?.Stop();
                return Content("0");
            }
        }

       static int HourCount = 0;
        /// <summary>
        /// Timer类执行定时到点事件
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void TimerUp(object sender, System.Timers.ElapsedEventArgs e)
        {
            try
            {
                HourCount++;
                if (HourCount >= shutdownTimingVal && shutdownTimingVal > 0)
                {
                    WindowsShutdown();
                }
            }
            catch (Exception ex)
            {
            }
        }

        public ActionResult GetShutdownTimingVal()
        {
            return Content((shutdownTimingVal == 0 ? shutdownTimingVal : shutdownTimingVal - HourCount) + "");
        }

        private bool InitProxy(string linkPath)
        {
            var protocol = new TcpDuplexClientProtocolSetup(true);
            try
            {
                _conn = new ZyanConnection($"tcpex://{linkPath}/PCDeviceManage", protocol);
                //创建Service代理
                _displayProxy = _conn.CreateProxy<DisplayInterface>();
                _audioInterface= _conn.CreateProxy<AudioInterface>();
                _systemInterface= _conn.CreateProxy<SystemInterface>();
                _haveConnected = true;
            }
            catch (Exception ex)
            {
                _haveConnected = false;
                //MessageBox.Show(ex.Message);
            }
            return _haveConnected;
        }



        public string GetIp()
        {
            return ExcuteAjaxService<object>(() =>
            {
                string userIP = "未获取用户IP";
                string customerIp = string.Empty;

                //CDN加速后取到的IP 
                customerIp = Request.Headers["Cdn-Src-Ip"];
                if (!string.IsNullOrWhiteSpace(customerIp))
                {
                    return customerIp;
                }

                customerIp = Request.ServerVariables["HTTP_X_FORWARDED_FOR"];

                if (!string.IsNullOrWhiteSpace(customerIp))
                {
                    return customerIp;
                }

                if (Request.ServerVariables["HTTP_VIA"] != null)
                {
                    customerIp = Request.ServerVariables["HTTP_X_FORWARDED_FOR"];
                    if (customerIp == null)
                    {
                        customerIp = Request.ServerVariables["REMOTE_ADDR"];
                    }
                }
                else
                {
                    customerIp = Request.ServerVariables["REMOTE_ADDR"];
                }

                if (string.Compare(customerIp, "unknown", true) == 0)
                {
                    return Request.UserHostAddress;
                }

                return string.IsNullOrWhiteSpace(customerIp) ? userIP : customerIp;
            });
        }


        [DllImport("Iphlpapi.dll")]
        static extern int SendARP(Int32 destIp, Int32 srcIp, ref Int64 macAddr, ref Int32 phyAddrLen);
        [DllImport("Ws2_32.dll")]
        static extern Int32 inet_addr(string ipaddr);

        public string GetMac(string remoteIp)
        {
            return ExcuteAjaxService<object>(() =>
            {
                var macAddress = new StringBuilder();
                try
                {
                    Int32 remote = inet_addr(remoteIp);
                    Int64 macInfo = new Int64();
                    Int32 length = 6;
                    SendARP(remote, 0, ref macInfo, ref length);
                    string temp = Convert.ToString(macInfo, 16).PadLeft(12, '0').ToUpper();
                    int x = 12;
                    for (int i = 0; i < 6; i++)
                    {
                        if (i == 5)
                        {
                            macAddress.Append(temp.Substring(x - 2, 2));
                        }
                        else
                        {
                            macAddress.Append(temp.Substring(x - 2, 2) + "-");
                        }
                        x -= 2;
                    }
                    return macAddress.ToString();
                }
                catch
                {
                    return macAddress.ToString();
                }
            });
        }


        private string ExcuteAjaxService<T>(Func<T> action)
        {
            try
            {
                var msg = action.Invoke();
                return JsonConvert.SerializeObject(new { IsSuccess = true, Message = msg });
            }
            catch (Exception ex)
            {
                return JsonConvert.SerializeObject(new { IsSuccess = false, Message = ex.Message, Exception = ex });
            }
        }

    }
}