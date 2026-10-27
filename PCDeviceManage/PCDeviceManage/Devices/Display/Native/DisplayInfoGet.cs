using log4net;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace PCDeviceManage
{
    public class DisplayInfoGet
    {

		//日志方法调用
		private static ILog _log = LogManager.GetLogger(typeof(DisplayInfoGet));

		public class DeviceItemPlus
		{
			private readonly DisplayContext.DeviceItem _deviceItem;

			public string DeviceInstanceId => _deviceItem.DeviceInstanceId;
			public string Description => _deviceItem.Description;
			public string AlternateDescription { get; }
			public byte DisplayIndex => _deviceItem.DisplayIndex;
			public byte MonitorIndex => _deviceItem.MonitorIndex;
			public bool IsInternal { get; }

			public DeviceItemPlus(
				DisplayContext.DeviceItem deviceItem,
				string alternateDescription = null,
				bool isInternal = true)
			{
				this._deviceItem = deviceItem ?? throw new ArgumentNullException(nameof(deviceItem));
				this.AlternateDescription = alternateDescription ?? deviceItem.Description;
				this.IsInternal = isInternal;
			}
		}

		public List<DeviceItemPlus> GetDisplayList() 
		{
			IDisplayItem[] displayItems = DisplayInfo.GetDisplayMonitorsAsync();
			_log.Info(" DisplayInfo.GetDisplayMonitorsAsync()获取到显示器数量："+ displayItems.Length);
			var deviceItems = DisplayContext.EnumerateMonitorDevices().ToArray();

			_log.Info("DisplayContext.EnumerateMonitorDevices()获取到设备数量：" + deviceItems.Length);

			IEnumerable<DeviceItemPlus> Enumerate()
			{
				foreach (var deviceItem in deviceItems)
				{
					var displayItem = displayItems.FirstOrDefault(x => string.Equals(deviceItem.DeviceInstanceId, x.DeviceInstanceId, StringComparison.OrdinalIgnoreCase));
					if (displayItem is null)
					{
						yield return new DeviceItemPlus(deviceItem);
					}
					else if (!string.IsNullOrWhiteSpace(displayItem.DisplayName))
					{
						yield return new DeviceItemPlus(deviceItem, displayItem.DisplayName, displayItem.IsInternal);
					}
					else if (Regex.IsMatch(deviceItem.Description, "^Generic (?:PnP|Non-PnP) Monitor$", RegexOptions.IgnoreCase)
						&& !string.IsNullOrWhiteSpace(displayItem.ConnectionDescription))
					{
						yield return new DeviceItemPlus(deviceItem, $"{deviceItem.Description} ({displayItem.ConnectionDescription})", displayItem.IsInternal);
					}
					else
					{
						yield return new DeviceItemPlus(deviceItem, null, displayItem.IsInternal);
					}
				}
			}

			return Enumerate().Where(x => !string.IsNullOrWhiteSpace(x.AlternateDescription)).ToList();
		}
	}
	
}
