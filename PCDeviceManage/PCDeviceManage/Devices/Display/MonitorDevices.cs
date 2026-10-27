using Newtonsoft.Json;
using System;
using System.Security.Cryptography;
using System.Text;

namespace PCDeviceManage
{
    /// <summary>
    /// 一台可调亮度的显示器。实现类负责自己的线程安全，Dispose 后的调用应安全地返回失败。
    /// </summary>
    public interface IMonitorDevice : IDisposable
    {
        string Id { get; }
        string Name { get; }
        bool IsInternal { get; }
        bool IsBrightnessSupported { get; }

        /// <summary>最近一次读到或设置成功的亮度，未知时为 -1。</summary>
        int Brightness { get; }

        void RefreshBrightness();
        bool TrySetBrightness(int brightness);
    }

    public sealed class MonitorInfo
    {
        [JsonProperty("id")]
        public string Id { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("isInternal")]
        public bool IsInternal { get; set; }

        [JsonProperty("brightness")]
        public int Brightness { get; set; }

        [JsonProperty("supported")]
        public bool Supported { get; set; }
    }

    public static class MonitorIds
    {
        /// <summary>
        /// DeviceInstanceId 含反斜杠和 &amp;，不适合放进 URL，取其 SHA1 的前 8 位十六进制作为短 Id。
        /// </summary>
        public static string FromDeviceInstanceId(string deviceInstanceId)
        {
            using (var sha1 = SHA1.Create())
            {
                byte[] hash = sha1.ComputeHash(Encoding.UTF8.GetBytes((deviceInstanceId ?? string.Empty).ToUpperInvariant()));
                return BitConverter.ToString(hash, 0, 4).Replace("-", string.Empty).ToLowerInvariant();
            }
        }
    }

    /// <summary>
    /// 笔记本内置屏：优先用 WMI（WmiSetBrightness），失败时退回到写电源方案。
    /// </summary>
    public sealed class InternalMonitor : IMonitorDevice
    {
        private readonly string _deviceInstanceId;
        private readonly object _locker = new object();

        public InternalMonitor(string deviceInstanceId, string name)
        {
            _deviceInstanceId = deviceInstanceId;
            Id = MonitorIds.FromDeviceInstanceId(deviceInstanceId);
            Name = name;
        }

        public string Id { get; }
        public string Name { get; }
        public bool IsInternal => true;
        public bool IsBrightnessSupported => true;
        public int Brightness { get; private set; } = -1;

        public void RefreshBrightness()
        {
            lock (_locker)
            {
                int brightness = MSMonitor.GetBrightness(_deviceInstanceId);
                Brightness = brightness >= 0 ? brightness : PowerManagement.GetActiveSchemeBrightness();
            }
        }

        public bool TrySetBrightness(int brightness)
        {
            lock (_locker)
            {
                bool succeeded = MSMonitor.SetBrightness(_deviceInstanceId, brightness) ||
                    PowerManagement.SetActiveSchemeBrightness(brightness);
                if (succeeded)
                {
                    Brightness = brightness;
                }
                return succeeded;
            }
        }

        public void Dispose()
        {
        }
    }

    /// <summary>
    /// 支持 DDC/CI 的外接显示器。持有物理显示器句柄直到被 Dispose（重新扫描或程序退出时）。
    /// </summary>
    public sealed class DdcMonitor : IMonitorDevice
    {
        private readonly DdcMonitorItem _item;
        private readonly object _locker = new object();
        private bool _isDisposed;

        public DdcMonitor(string deviceInstanceId, string name, DdcMonitorItem item)
        {
            _item = item ?? throw new ArgumentNullException(nameof(item));
            Id = MonitorIds.FromDeviceInstanceId(deviceInstanceId);
            Name = name;
        }

        public string Id { get; }
        public string Name { get; }
        public bool IsInternal => false;
        public bool IsBrightnessSupported => true;
        public int Brightness => _item.Brightness;

        public void RefreshBrightness()
        {
            lock (_locker)
            {
                if (!_isDisposed)
                {
                    _item.UpdateBrightness();
                }
            }
        }

        public bool TrySetBrightness(int brightness)
        {
            lock (_locker)
            {
                return !_isDisposed && _item.SetBrightness(brightness).Status == AccessStatus.Succeeded;
            }
        }

        public void Dispose()
        {
            lock (_locker)
            {
                if (_isDisposed)
                {
                    return;
                }
                _isDisposed = true;
                _item.Dispose();
            }
        }
    }

    /// <summary>
    /// 检测到但无法调节亮度的外接显示器（不支持 DDC/CI 或已在显示器菜单中关闭），只用于展示。
    /// </summary>
    public sealed class UnsupportedMonitor : IMonitorDevice
    {
        public UnsupportedMonitor(string deviceInstanceId, string name)
        {
            Id = MonitorIds.FromDeviceInstanceId(deviceInstanceId);
            Name = name;
        }

        public string Id { get; }
        public string Name { get; }
        public bool IsInternal => false;
        public bool IsBrightnessSupported => false;
        public int Brightness => -1;

        public void RefreshBrightness()
        {
        }

        public bool TrySetBrightness(int brightness)
        {
            return false;
        }

        public void Dispose()
        {
        }
    }
}
