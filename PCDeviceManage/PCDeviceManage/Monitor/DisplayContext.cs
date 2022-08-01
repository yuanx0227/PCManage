using log4net;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace PCDeviceManage
{
	public class DisplayContext
	{
		//日志方法调用
		private static ILog _log = LogManager.GetLogger("DisplayContext");

		[DataContract]
		public class DeviceItem
		{
			[DataMember(Order = 0)]
			public string DeviceInstanceId { get; }

			[DataMember(Order = 1)]
			public string Description { get; }

			[DataMember(Order = 2)]
			public byte DisplayIndex { get; }

			[DataMember(Order = 3)]
			public byte MonitorIndex { get; }

			public DeviceItem(
				string deviceInstanceId,
				string description,
				byte displayIndex,
				byte monitorIndex)
			{
				this.DeviceInstanceId = deviceInstanceId;
				this.Description = description;
				this.DisplayIndex = displayIndex;
				this.MonitorIndex = monitorIndex;
			}
		}


		public static IEnumerable<DeviceItem> EnumerateMonitorDevices()
		{
			foreach (var (_, displayIndex, monitor, monitorIndex) in EnumerateDevices())
			{
				var deviceInstanceId = ConvertToDeviceInstanceId(monitor.DeviceID);
				if (string.IsNullOrEmpty(deviceInstanceId))
					continue;

				//Debug.WriteLine($"DeviceId: {monitor.DeviceID}");
				//Debug.WriteLine($"DeviceInstanceId: {deviceInstanceId}");
				//Debug.WriteLine($"DeviceString: {monitor.DeviceString}");

				yield return new DeviceItem(
					deviceInstanceId: deviceInstanceId,
					description: monitor.DeviceString,
					displayIndex: displayIndex,
					monitorIndex: monitorIndex);
			}
		}

		private static IEnumerable<(DISPLAY_DEVICE display, byte displayIndex, DISPLAY_DEVICE monitor, byte monitorIndex)> EnumerateDevices()
		{
			var size = (uint)Marshal.SizeOf<DISPLAY_DEVICE>();
			var display = new DISPLAY_DEVICE { cb = size };
			var monitor = new DISPLAY_DEVICE { cb = size };

			_log.Info("EnumerateDevices() 开始获取显示器枚举信息");

			for (uint i = 0; EnumDisplayDevices(null, i, ref display, EDD_GET_DEVICE_INTERFACE_NAME); i++)
			{
				_log.Info($"EnumDisplayDevices() 获取到设备枚举信息 displayName:{display.DeviceName}");

				if (display.StateFlags.HasFlag(DISPLAY_DEVICE_FLAG.DISPLAY_DEVICE_MIRRORING_DRIVER))
					continue;

				if (!TryGetDisplayIndex(display.DeviceName, out byte displayIndex))
					continue;

				_log.Info($"TryGetDisplayIndex() 获取到设备序号 displayIndex:{displayIndex}");

				byte monitorIndex = 0;

				for (uint j = 0; EnumDisplayDevices(display.DeviceName, j, ref monitor, EDD_GET_DEVICE_INTERFACE_NAME); j++)
				{
					if (!monitor.StateFlags.HasFlag(DISPLAY_DEVICE_FLAG.DISPLAY_DEVICE_ACTIVE))
						continue;

					yield return (display, displayIndex, monitor, monitorIndex);

					monitorIndex++;
				}
			}
		}

		[DllImport("User32.dll", EntryPoint = "EnumDisplayDevicesA")]
		[return: MarshalAs(UnmanagedType.Bool)]
		private static extern bool EnumDisplayDevices(
	string lpDevice,
	uint iDevNum,
	ref DISPLAY_DEVICE lpDisplayDevice,
	uint dwFlags);

		private const uint EDD_GET_DEVICE_INTERFACE_NAME = 0x00000001;

		[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
		//[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
		private struct DISPLAY_DEVICE
		{
			public uint cb;

			[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
			public string DeviceName;

			[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
			public string DeviceString;

			public DISPLAY_DEVICE_FLAG StateFlags;

			[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
			public string DeviceID;

			[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
			public string DeviceKey;
		}

		[Flags]
		private enum DISPLAY_DEVICE_FLAG : uint
		{
			DISPLAY_DEVICE_ATTACHED_TO_DESKTOP = 0x00000001,
			DISPLAY_DEVICE_MULTI_DRIVER = 0x00000002,
			DISPLAY_DEVICE_PRIMARY_DEVICE = 0x00000004,
			DISPLAY_DEVICE_MIRRORING_DRIVER = 0x00000008,
			DISPLAY_DEVICE_VGA_COMPATIBLE = 0x00000010,
			DISPLAY_DEVICE_REMOVABLE = 0x00000020,
			DISPLAY_DEVICE_ACC_DRIVER = 0x00000040,
			DISPLAY_DEVICE_RDPUDD = 0x01000000,
			DISPLAY_DEVICE_DISCONNECT = 0x02000000,
			DISPLAY_DEVICE_REMOTE = 0x04000000,
			DISPLAY_DEVICE_MODESPRUNED = 0x08000000,

			DISPLAY_DEVICE_ACTIVE = 0x00000001,
			DISPLAY_DEVICE_ATTACHED = 0x00000002,
		}

		private static bool TryGetDisplayIndex(string deviceName, out byte index)
		{
			// The typical format of device name is as follows:
			// EnumDisplayDevices (display), GetMonitorInfo : \\.\DISPLAY[index starting at 1]
			// EnumDisplayDevices (monitor)                 : \\.\DISPLAY[index starting at 1]\Monitor[index starting at 0]

			var match = Regex.Match(deviceName, @"DISPLAY(?<index>\d{1,2})\s*$");
			if (match.Success)
			{
				index = byte.Parse(match.Groups["index"].Value);
				return true;
			}
			index = 0;
			return false;
		}


		/// <summary>
		/// Converts device path to device instance ID.
		/// </summary>
		/// <param name="devicePath">Device path</param>
		/// <returns>Device instance ID</returns>
		internal static string ConvertToDeviceInstanceId(string devicePath)
		{
			// The typical format of device path is as follows:
			// \\?\DISPLAY#<hardware-specific-ID>#<instance-specific-ID>#{e6f07b5f-ee97-4a90-b076-33f57bf4eaa7}
			// \\?\ is extended-length path prefix.
			// DISPLAY indicates display device.
			// {e6f07b5f-ee97-4a90-b076-33f57bf4eaa7} means GUID_DEVINTERFACE_MONITOR.

			int index = devicePath.IndexOf("DISPLAY", StringComparison.Ordinal);
			if (index < 0)
				return null;

			var fields = devicePath.Substring(index).Split('#');
			if (fields.Length < 3)
				return null;

			return string.Join(@"\", fields.Take(3));
		}


		[DataContract]
		public class HandleItem
		{
			[DataMember(Order = 0)]
			public int DisplayIndex { get; }

			//public Rect MonitorRect { get; }
			//[DataMember(Order = 1, Name = nameof(MonitorRect))]
			//private string _monitorRectString;

			//[OnSerializing]
			//private void OnSerializing(StreamingContext context)
			//{
			//	_monitorRectString = $"Location:{MonitorRect.Location}, Size:{MonitorRect.Size}";
			//}

			public IntPtr MonitorHandle { get; }

			public HandleItem(
				int displayIndex,
				//Rect monitorRect,
				IntPtr monitorHandle)
			{
				this.DisplayIndex = displayIndex;
				//this.MonitorRect = monitorRect;
				this.MonitorHandle = monitorHandle;
			}
		}


		//[StructLayout(LayoutKind.Sequential)]
		//public struct RECT
		//{
		//	public int left;
		//	public int top;
		//	public int right;
		//	public int bottom;

		//	public static implicit operator Rect(RECT rect)
		//	{
		//		if ((rect.right < rect.left) || (rect.bottom < rect.top))
		//			return Rect.Empty;

		//		return new Rect(
		//			rect.left,
		//			rect.top,
		//			rect.right - rect.left,
		//			rect.bottom - rect.top);
		//	}

		//	public static implicit operator RECT(Rect rect)
		//	{
		//		return new RECT
		//		{
		//			left = (int)rect.Left,
		//			top = (int)rect.Top,
		//			right = (int)rect.Right,
		//			bottom = (int)rect.Bottom
		//		};
		//	}
		//}

		[return: MarshalAs(UnmanagedType.Bool)]
		private delegate bool MonitorEnumProc(
	IntPtr hMonitor,
	IntPtr hdcMonitor,
	IntPtr lprcMonitor,
	IntPtr dwData);

		[DllImport("User32.dll", EntryPoint = "GetMonitorInfoW")]
		[return: MarshalAs(UnmanagedType.Bool)]
		private static extern bool GetMonitorInfo(
	IntPtr hMonitor,
	ref MONITORINFOEX lpmi);

		[DllImport("User32.dll")]
		[return: MarshalAs(UnmanagedType.Bool)]
		private static extern bool EnumDisplayMonitors(
	IntPtr hdc,
	IntPtr lprcClip,
	MonitorEnumProc lpfnEnum,
	IntPtr dwData);


		[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
		public struct MONITORINFOEX
		{
			public uint cbSize;
			//public RECT rcMonitor;
			//public RECT rcWork;
			public uint dwFlags;

			[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
			public string szDevice;
		}


		public static HandleItem[] GetMonitorHandles()
		{
			var handleItems = new List<HandleItem>();

			if (EnumDisplayMonitors(
				IntPtr.Zero,
				IntPtr.Zero,
				Proc,
				IntPtr.Zero))
			{
				return handleItems.ToArray();
			}
			return Array.Empty<HandleItem>();

			bool Proc(IntPtr monitorHandle, IntPtr hdcMonitor, IntPtr lprcMonitor, IntPtr dwData)
			{
				var monitorInfo = new MONITORINFOEX { cbSize = (uint)Marshal.SizeOf<MONITORINFOEX>() };

				if (GetMonitorInfo(
					monitorHandle,
					ref monitorInfo))
				{
					if (TryGetDisplayIndex(monitorInfo.szDevice, out byte displayIndex))
					{
						handleItems.Add(new HandleItem(
							displayIndex: displayIndex,
							//monitorRect: monitorInfo.rcMonitor,
							monitorHandle: monitorHandle));
					}
				}
				return true;
			}
		}



	}
}
