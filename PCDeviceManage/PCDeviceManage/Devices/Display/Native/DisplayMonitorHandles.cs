using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Windows;

namespace PCDeviceManage
{
	/// <summary>
	/// 枚举 HMONITOR 句柄及其所属的显示设备序号，用于打开 DDC/CI 物理显示器句柄。
	/// </summary>
	internal static class DisplayMonitorHandles
	{
		#region Win32

		[DllImport("User32.dll")]
		[return: MarshalAs(UnmanagedType.Bool)]
		private static extern bool EnumDisplayMonitors(
			IntPtr hdc,
			IntPtr lprcClip,
			MonitorEnumProc lpfnEnum,
			IntPtr dwData);

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

		[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
		private struct MONITORINFOEX
		{
			public uint cbSize;
			public RECT rcMonitor;
			public RECT rcWork;
			public uint dwFlags;

			[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
			public string szDevice;
		}

		[StructLayout(LayoutKind.Sequential)]
		private struct RECT
		{
			public int left;
			public int top;
			public int right;
			public int bottom;

			public static implicit operator Rect(RECT rect)
			{
				if ((rect.right < rect.left) || (rect.bottom < rect.top))
					return Rect.Empty;

				return new Rect(
					rect.left,
					rect.top,
					rect.right - rect.left,
					rect.bottom - rect.top);
			}
		}

		#endregion

		public class HandleItem
		{
			public int DisplayIndex { get; }
			public Rect MonitorRect { get; }
			public IntPtr MonitorHandle { get; }

			public HandleItem(
				int displayIndex,
				Rect monitorRect,
				IntPtr monitorHandle)
			{
				this.DisplayIndex = displayIndex;
				this.MonitorRect = monitorRect;
				this.MonitorHandle = monitorHandle;
			}
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
							monitorRect: monitorInfo.rcMonitor,
							monitorHandle: monitorHandle));
					}
				}
				return true;
			}
		}

		private static bool TryGetDisplayIndex(string deviceName, out byte index)
		{
			// GetMonitorInfo 返回的设备名形如 \\.\DISPLAY[从 1 开始的序号]
			var match = Regex.Match(deviceName, @"DISPLAY(?<index>\d{1,2})\s*$");
			if (match.Success)
			{
				index = byte.Parse(match.Groups["index"].Value);
				return true;
			}
			index = 0;
			return false;
		}
	}
}
