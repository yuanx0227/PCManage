using log4net;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace PCDeviceManage
{
    public class DisplayInfoGet
    {

		//日志方法调用
		private static ILog _log = LogManager.GetLogger("DisplayInfoGet");

		public static SafePhysicalMonitorHandle _handle;
		public class DeviceItemPlus
		{
			private readonly DisplayContext.DeviceItem _deviceItem;

			public string DeviceInstanceId => _deviceItem.DeviceInstanceId;
			public string Description => _deviceItem.Description;
			public string AlternateDescription { get; }
			public byte DisplayIndex => _deviceItem.DisplayIndex;
			public byte MonitorIndex => _deviceItem.MonitorIndex;
			public bool IsInternal { get; }

			public int Brightness { get; set; }

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

		//private static HashSet<string> _foundIds;

		public List<DeviceItemPlus> GetDisplayList() 
		{
			IDisplayItem[] displayItems = DisplayInfo.GetDisplayMonitorsAsync();
			_log.Info(" DisplayInfo.GetDisplayMonitorsAsync()获取到显示器数量："+ displayItems.Length);
			var deviceItems = DisplayContext.EnumerateMonitorDevices().ToArray();
			//_foundIds = new HashSet<string>(deviceItems.Select(x => x.DeviceInstanceId));

			_log.Info(JsonConvert.SerializeObject(deviceItems));

			IEnumerable<DeviceItemPlus> Enumerate()
			{
				foreach (var deviceItem in deviceItems)
				{
					//_log.Info(deviceItem.Description);
					//_log.Info(deviceItem.DeviceInstanceId);
					//_log.Info(deviceItem.DisplayIndex);
					//_log.Info(deviceItem.MonitorIndex);
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

		public List<DeviceItemPlus> GetDisplayListAndBrightness() 
		{
			var deviceItemPlusList = GetDisplayList();
			_log.Info("GetDisplayList()获取到显示器数量：" + deviceItemPlusList.Count());
			foreach (var item in deviceItemPlusList)
            {
                if (item.IsInternal)
                {
					item.Brightness = PowerManagement.GetActiveSchemeBrightness();
                }
                else
                {
					//var temp = new DdcMonitorItem();
					//temp.UpdateBrightness();
					item.Brightness = GetExternalDisplayBrightness(deviceItemPlusList);
				}


			}
			return deviceItemPlusList;
		}



		private int GetExternalDisplayBrightness(List<DeviceItemPlus> deviceItemPlusList) 
		{
			var handleItems = temp.GetMonitorHandles();
			foreach (var handleItem in handleItems)
			{
				foreach (var physicalItem in MonitorConfiguration.EnumeratePhysicalMonitors(handleItem.MonitorHandle))
				{
					int index = -1;
					if (physicalItem.Capability.IsBrightnessSupported )
					{
						index = deviceItemPlusList.FindIndex(x =>
							!x.IsInternal &&
							(x.DisplayIndex == handleItem.DisplayIndex) &&
							(x.MonitorIndex == physicalItem.MonitorIndex) &&
							string.Equals(x.Description, physicalItem.Description, StringComparison.OrdinalIgnoreCase));
					}
					if (index < 0)
					{
						physicalItem.Handle.Dispose();
						continue;
					}

                    if (deviceItemPlusList[index].IsInternal)
                    {
						continue;
                    }
					var deviceItem = deviceItemPlusList[index];

					MonitorCapability capability = null;
					if (physicalItem.Capability.IsBrightnessSupported)
					{
						capability = physicalItem.Capability;
					}
					//else if (_preclearedIds.Value.Contains(deviceItem.DeviceInstanceId))
					//{
					//	capability = MonitorCapability.PreclearedCapability;
					//}
					else
					{
						physicalItem.Handle.Dispose();
						continue;
					}

					var temp = physicalItem.Handle;

					_handle= physicalItem.Handle;

					DdcMonitorItem ddcMonitorItem = new DdcMonitorItem(deviceInstanceId: deviceItem.DeviceInstanceId,
						description: deviceItem.AlternateDescription,
						displayIndex: deviceItem.DisplayIndex,
						monitorIndex: deviceItem.MonitorIndex,
						monitorRect: handleItem.MonitorRect,
						handle: physicalItem.Handle,
						capability: capability);
					ddcMonitorItem.UpdateBrightness();
					return ddcMonitorItem.Brightness;

				}
			}
			return 0;
		}


	}
	
}
