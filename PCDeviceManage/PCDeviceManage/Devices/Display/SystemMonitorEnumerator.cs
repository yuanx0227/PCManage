using PCDeviceManage.Devices.Display.Native;
using System;
using System.Collections.Generic;
using System.Linq;

namespace PCDeviceManage.Devices.Display
{
    /// <summary>
    /// 枚举本机显示器：内置屏走 WMI/电源方案，外接显示器通过 DDC/CI 打开物理显示器句柄。
    /// </summary>
    public sealed class SystemMonitorEnumerator : IMonitorEnumerator
    {
        public List<IMonitorDevice> Enumerate()
        {
            List<DisplayInfoGet.DeviceItemPlus> deviceItems = new DisplayInfoGet().GetDisplayList();
            var ddcMonitors = new Dictionary<DisplayInfoGet.DeviceItemPlus, DdcMonitor>();

            try
            {
                if (deviceItems.Any(x => !x.IsInternal))
                {
                    OpenDdcMonitors(deviceItems, ddcMonitors);
                }

                var devices = new List<IMonitorDevice>();
                foreach (DisplayInfoGet.DeviceItemPlus deviceItem in deviceItems)
                {
                    DdcMonitor ddcMonitor;
                    if (deviceItem.IsInternal)
                    {
                        devices.Add(new InternalMonitor(deviceItem.DeviceInstanceId, deviceItem.AlternateDescription));
                    }
                    else if (ddcMonitors.TryGetValue(deviceItem, out ddcMonitor))
                    {
                        devices.Add(ddcMonitor);
                    }
                    else
                    {
                        devices.Add(new UnsupportedMonitor(deviceItem.DeviceInstanceId, deviceItem.AlternateDescription));
                    }
                }
                return devices;
            }
            catch
            {
                foreach (DdcMonitor ddcMonitor in ddcMonitors.Values)
                {
                    ddcMonitor.Dispose();
                }
                throw;
            }
        }

        private static void OpenDdcMonitors(
            List<DisplayInfoGet.DeviceItemPlus> deviceItems,
            Dictionary<DisplayInfoGet.DeviceItemPlus, DdcMonitor> ddcMonitors)
        {
            foreach (DisplayMonitorHandles.HandleItem handleItem in DisplayMonitorHandles.GetMonitorHandles())
            {
                foreach (MonitorConfiguration.PhysicalItem physicalItem in MonitorConfiguration.EnumeratePhysicalMonitors(handleItem.MonitorHandle))
                {
                    DisplayInfoGet.DeviceItemPlus deviceItem = null;
                    if (physicalItem.Capability.IsBrightnessSupported)
                    {
                        deviceItem = deviceItems.FirstOrDefault(x =>
                            !x.IsInternal &&
                            !ddcMonitors.ContainsKey(x) &&
                            x.DisplayIndex == handleItem.DisplayIndex &&
                            x.MonitorIndex == physicalItem.MonitorIndex &&
                            string.Equals(x.Description, physicalItem.Description, StringComparison.OrdinalIgnoreCase));
                    }

                    if (deviceItem == null)
                    {
                        physicalItem.Handle.Dispose();
                        continue;
                    }

                    var item = new DdcMonitorItem(
                        deviceInstanceId: deviceItem.DeviceInstanceId,
                        description: deviceItem.AlternateDescription,
                        displayIndex: deviceItem.DisplayIndex,
                        monitorIndex: deviceItem.MonitorIndex,
                        monitorRect: handleItem.MonitorRect,
                        handle: physicalItem.Handle,
                        capability: physicalItem.Capability);
                    ddcMonitors[deviceItem] = new DdcMonitor(deviceItem.DeviceInstanceId, deviceItem.AlternateDescription, item);
                }
            }
        }
    }
}
