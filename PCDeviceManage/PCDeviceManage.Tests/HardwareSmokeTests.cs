using PCDeviceManage.Devices.Audio;
using PCDeviceManage.Devices.Display;
using System;
using System.Runtime.InteropServices;
using Xunit;
using Xunit.Abstractions;

namespace PCDeviceManage.Tests
{
    /// <summary>
    /// 在本机真实硬件上做只读检查（不调亮度、不改音量），确认 WinRT、DDC/CI、WMI 和 Core Audio 互操作可用。
    /// 没有显示器或声卡的机器上只要求不抛异常。
    /// </summary>
    public class HardwareSmokeTests
    {
        private readonly ITestOutputHelper _output;

        public HardwareSmokeTests(ITestOutputHelper output)
        {
            _output = output;
        }

        [Fact]
        public void SystemEnumeratorReadsRealMonitors()
        {
            using (var registry = new MonitorRegistry(new SystemMonitorEnumerator()))
            {
                registry.Rescan();
                foreach (MonitorInfo monitor in registry.GetMonitors(true))
                {
                    _output.WriteLine("{0} {1} internal={2} supported={3} brightness={4}",
                        monitor.Id, monitor.Name, monitor.IsInternal, monitor.Supported, monitor.Brightness);
                    Assert.True(monitor.Id.Length == 8, "Expected a short id.");
                    Assert.True(monitor.Brightness >= -1 && monitor.Brightness <= 100, "Expected brightness within -1..100.");
                }
            }
        }

        [Fact]
        public void AudioServiceReadsVolume()
        {
            try
            {
                int volume = new AudioService().GetVolume();
                _output.WriteLine("volume=" + volume);
                Assert.InRange(volume, 0, 100);
            }
            catch (COMException ex)
            {
                // 没有播放设备时 GetDefaultAudioEndpoint 返回 E_NOTFOUND。
                _output.WriteLine("No audio endpoint: " + ex.Message);
            }
        }
    }
}
