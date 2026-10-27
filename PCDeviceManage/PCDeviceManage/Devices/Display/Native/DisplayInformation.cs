using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace PCDeviceManage.Devices.Display.Native
{
	/// <summary>
	/// 通过 Win32 QueryDisplayConfig 获取显示器的友好名称和连接方式（是否内置屏）。
	/// 取代原来基于 WinRT DisplayMonitor 的实现，不再需要 Windows SDK 投影库。
	/// </summary>
	public static class DisplayInformation
	{
		public class DisplayItem
		{
			/// <summary>Device ID (Not device interface ID)</summary>
			public string DeviceInstanceId { get; }

			public string DisplayName { get; }

			/// <summary>Whether the display is connected internally</summary>
			public bool IsInternal { get; }

			public string ConnectionDescription { get; }

			internal DisplayItem(string deviceInstanceId, string displayName, bool isInternal, string connectionDescription)
			{
				this.DeviceInstanceId = deviceInstanceId;
				this.DisplayName = displayName;
				this.IsInternal = isInternal;
				this.ConnectionDescription = connectionDescription;
			}
		}

		#region Win32

		[DllImport("User32.dll")]
		private static extern int GetDisplayConfigBufferSizes(
			uint flags,
			out uint numPathArrayElements,
			out uint numModeInfoArrayElements);

		[DllImport("User32.dll")]
		private static extern int QueryDisplayConfig(
			uint flags,
			ref uint numPathArrayElements,
			[Out] DISPLAYCONFIG_PATH_INFO[] pathArray,
			ref uint numModeInfoArrayElements,
			[Out] DISPLAYCONFIG_MODE_INFO[] modeInfoArray,
			IntPtr currentTopologyId);

		[DllImport("User32.dll")]
		private static extern int DisplayConfigGetDeviceInfo(
			ref DISPLAYCONFIG_TARGET_DEVICE_NAME requestPacket);

		[StructLayout(LayoutKind.Sequential)]
		private struct LUID
		{
			public uint LowPart;
			public int HighPart;
		}

		[StructLayout(LayoutKind.Sequential)]
		private struct DISPLAYCONFIG_PATH_SOURCE_INFO
		{
			public LUID adapterId;
			public uint id;
			public uint modeInfoIdx;
			public uint statusFlags;
		}

		[StructLayout(LayoutKind.Sequential)]
		private struct DISPLAYCONFIG_PATH_TARGET_INFO
		{
			public LUID adapterId;
			public uint id;
			public uint modeInfoIdx;
			public DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY outputTechnology;
			public uint rotation;
			public uint scaling;
			public uint refreshRateNumerator;
			public uint refreshRateDenominator;
			public uint scanLineOrdering;
			public int targetAvailable;
			public uint statusFlags;
		}

		[StructLayout(LayoutKind.Sequential)]
		private struct DISPLAYCONFIG_PATH_INFO
		{
			public DISPLAYCONFIG_PATH_SOURCE_INFO sourceInfo;
			public DISPLAYCONFIG_PATH_TARGET_INFO targetInfo;
			public uint flags;
		}

		// 模式信息的内容用不到，只需要大小正确（64 字节：头部 16 字节 + 48 字节的联合体）。
		[StructLayout(LayoutKind.Sequential)]
		private struct DISPLAYCONFIG_MODE_INFO
		{
			public uint infoType;
			public uint id;
			public LUID adapterId;
			private ulong union0, union1, union2, union3, union4, union5;
		}

		[StructLayout(LayoutKind.Sequential)]
		private struct DISPLAYCONFIG_DEVICE_INFO_HEADER
		{
			public uint type;
			public uint size;
			public LUID adapterId;
			public uint id;
		}

		[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
		private struct DISPLAYCONFIG_TARGET_DEVICE_NAME
		{
			public DISPLAYCONFIG_DEVICE_INFO_HEADER header;
			public uint flags;
			public DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY outputTechnology;
			public ushort edidManufactureId;
			public ushort edidProductCodeId;
			public uint connectorInstance;

			[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
			public string monitorFriendlyDeviceName;

			[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
			public string monitorDevicePath;
		}

		private enum DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY : uint
		{
			OTHER = 0xFFFFFFFF,
			HD15 = 0,
			SVIDEO = 1,
			COMPOSITE_VIDEO = 2,
			COMPONENT_VIDEO = 3,
			DVI = 4,
			HDMI = 5,
			LVDS = 6,
			D_JPN = 8,
			SDI = 9,
			DISPLAYPORT_EXTERNAL = 10,
			DISPLAYPORT_EMBEDDED = 11,
			UDI_EXTERNAL = 12,
			UDI_EMBEDDED = 13,
			SDTVDONGLE = 14,
			MIRACAST = 15,
			INDIRECT_WIRED = 16,
			INDIRECT_VIRTUAL = 17,
			INTERNAL = 0x80000000
		}

		private const uint QDC_ONLY_ACTIVE_PATHS = 0x00000002;
		private const uint DISPLAYCONFIG_DEVICE_INFO_GET_TARGET_NAME = 2;
		private const int ERROR_SUCCESS = 0;
		private const int ERROR_INSUFFICIENT_BUFFER = 122;

		#endregion

		/// <summary>
		/// 枚举当前处于活动状态的显示器。
		/// </summary>
		public static DisplayItem[] GetDisplayMonitors()
		{
			DISPLAYCONFIG_PATH_INFO[] paths = QueryActivePaths();
			var items = new List<DisplayItem>(paths.Length);
			var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

			foreach (DISPLAYCONFIG_PATH_INFO path in paths)
			{
				var deviceName = new DISPLAYCONFIG_TARGET_DEVICE_NAME();
				deviceName.header.type = DISPLAYCONFIG_DEVICE_INFO_GET_TARGET_NAME;
				deviceName.header.size = (uint)Marshal.SizeOf<DISPLAYCONFIG_TARGET_DEVICE_NAME>();
				deviceName.header.adapterId = path.targetInfo.adapterId;
				deviceName.header.id = path.targetInfo.id;
				if (DisplayConfigGetDeviceInfo(ref deviceName) != ERROR_SUCCESS)
					continue;

				string deviceInstanceId = DisplayContext.ConvertToDeviceInstanceId(deviceName.monitorDevicePath ?? string.Empty);
				// 复制模式下同一台显示器会出现在多条路径里。
				if (string.IsNullOrEmpty(deviceInstanceId) || !seen.Add(deviceInstanceId))
					continue;

				DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY technology = deviceName.outputTechnology;
				items.Add(new DisplayItem(
					deviceInstanceId: deviceInstanceId,
					displayName: deviceName.monitorFriendlyDeviceName,
					isInternal: IsInternal(technology),
					connectionDescription: GetConnectionDescription(technology)));
			}
			return items.ToArray();
		}

		private static DISPLAYCONFIG_PATH_INFO[] QueryActivePaths()
		{
			// 两次调用之间显示配置可能变化，缓冲区不够时重试。
			for (int attempt = 0; attempt < 5; attempt++)
			{
				int result = GetDisplayConfigBufferSizes(QDC_ONLY_ACTIVE_PATHS, out uint pathCount, out uint modeCount);
				if (result != ERROR_SUCCESS)
					throw new Win32Exception(result);

				var paths = new DISPLAYCONFIG_PATH_INFO[pathCount];
				var modes = new DISPLAYCONFIG_MODE_INFO[modeCount];
				result = QueryDisplayConfig(QDC_ONLY_ACTIVE_PATHS, ref pathCount, paths, ref modeCount, modes, IntPtr.Zero);
				if (result == ERROR_SUCCESS)
				{
					Array.Resize(ref paths, (int)pathCount);
					return paths;
				}
				if (result != ERROR_INSUFFICIENT_BUFFER)
					throw new Win32Exception(result);
			}
			throw new Win32Exception(ERROR_INSUFFICIENT_BUFFER);
		}

		private static bool IsInternal(DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY technology)
		{
			switch (technology)
			{
				case DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY.INTERNAL:
				case DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY.DISPLAYPORT_EMBEDDED:
				case DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY.UDI_EMBEDDED:
				case DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY.LVDS:
					return true;
				default:
					return false;
			}
		}

		private static string GetConnectionDescription(DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY technology)
		{
			switch (technology)
			{
				case DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY.INTERNAL:
				case DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY.DISPLAYPORT_EMBEDDED:
				case DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY.UDI_EMBEDDED:
					return "Internal";
				case DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY.HD15:
					return "VGA";
				case DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY.DVI:
					return "DVI";
				case DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY.HDMI:
					return "HDMI";
				case DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY.LVDS:
					return "LVDS";
				case DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY.SDI:
					return "SDI";
				case DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY.DISPLAYPORT_EXTERNAL:
					return "DisplayPort";
				case DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY.MIRACAST:
					return "Wireless";
				case DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY.INDIRECT_VIRTUAL:
					return "Virtual";
				default:
					return null;
			}
		}
	}
}
