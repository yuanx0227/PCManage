using log4net;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;

namespace PCDeviceManage
{
    public interface IDisplayDeviceProvider
    {
        List<DisplayInfoGet.DeviceItemPlus> GetDisplayListAndBrightness();
    }

    public sealed class DisplayDeviceProvider : IDisplayDeviceProvider
    {
        public List<DisplayInfoGet.DeviceItemPlus> GetDisplayListAndBrightness()
        {
            return new DisplayInfoGet().GetDisplayListAndBrightness();
        }
    }

    public sealed class DisplayDeviceCache : IDisposable
    {
        private readonly IDisplayDeviceProvider _provider;
        private readonly object _locker = new object();
        private readonly object _refreshLocker = new object();
        private List<DisplayInfoGet.DeviceItemPlus> _displayDevices = new List<DisplayInfoGet.DeviceItemPlus>();
        private string _displayDevicesJson = "[]";
        private bool _isStarted;

        public DisplayDeviceCache(IDisplayDeviceProvider provider, ILog log = null)
        {
            _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        }

        public void Start()
        {
            if (_isStarted)
            {
                return;
            }

            Refresh();
            _isStarted = true;
        }

        public void Refresh()
        {
            lock (_refreshLocker)
            {
                List<DisplayInfoGet.DeviceItemPlus> devices = _provider.GetDisplayListAndBrightness();
                string json = JsonConvert.SerializeObject(devices);

                lock (_locker)
                {
                    _displayDevices = devices;
                    _displayDevicesJson = json;
                }
            }
        }

        public string GetDisplayListJson(bool refresh)
        {
            if (refresh)
            {
                Refresh();
            }

            return GetDisplayListJson();
        }

        public string GetDisplayListJson()
        {
            lock (_locker)
            {
                return _displayDevicesJson;
            }
        }

        public void UpdateCachedBrightness(int brightness, bool isInternal)
        {
            lock (_locker)
            {
                foreach (DisplayInfoGet.DeviceItemPlus device in _displayDevices)
                {
                    if (device.IsInternal == isInternal)
                    {
                        device.Brightness = brightness;
                    }
                }

                _displayDevicesJson = JsonConvert.SerializeObject(_displayDevices);
            }
        }

        public void Dispose()
        {
        }
    }
}
