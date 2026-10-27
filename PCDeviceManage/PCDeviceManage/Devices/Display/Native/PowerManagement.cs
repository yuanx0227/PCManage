using log4net;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace PCDeviceManage.Devices.Display.Native
{
   public class PowerManagement
    {

		//日志方法调用
		private static ILog _log = LogManager.GetLogger(typeof(PowerManagement));

		#region Win32

		[DllImport("PowrProf.dll")]
		private static extern uint PowerGetActiveScheme(
			IntPtr UserRootPowerKey, // Always null
			[MarshalAs(UnmanagedType.LPStruct)] out Guid activePolicyGuid);

		[DllImport("PowrProf.dll")]
		private static extern uint PowerReadACValueIndex(
			IntPtr RootPowerKey, // Always null
			[MarshalAs(UnmanagedType.LPStruct), In] Guid SchemeGuid,
			[MarshalAs(UnmanagedType.LPStruct), In] Guid SubGroupOfPowerSettingsGuid,
			[MarshalAs(UnmanagedType.LPStruct), In] Guid PowerSettingGuid,
			out uint AcValueIndex);

		[DllImport("PowrProf.dll")]
		private static extern uint PowerReadDCValueIndex(
			IntPtr RootPowerKey, // Always null
			[MarshalAs(UnmanagedType.LPStruct), In] Guid SchemeGuid,
			[MarshalAs(UnmanagedType.LPStruct), In] Guid SubGroupOfPowerSettingsGuid,
			[MarshalAs(UnmanagedType.LPStruct), In] Guid PowerSettingGuid,
			out uint DcValueIndex);

		[DllImport("PowrProf.dll")]
		private static extern uint PowerWriteACValueIndex(
			IntPtr RootPowerKey, // Always null
			[MarshalAs(UnmanagedType.LPStruct), In] Guid SchemeGuid,
			[MarshalAs(UnmanagedType.LPStruct), In] Guid SubGroupOfPowerSettingsGuid,
			[MarshalAs(UnmanagedType.LPStruct), In] Guid PowerSettingGuid,
			uint AcValueIndex);

		[DllImport("PowrProf.dll")]
		private static extern uint PowerWriteDCValueIndex(
			IntPtr RootPowerKey, // Always null
			[MarshalAs(UnmanagedType.LPStruct), In] Guid SchemeGuid,
			[MarshalAs(UnmanagedType.LPStruct), In] Guid SubGroupOfPowerSettingsGuid,
			[MarshalAs(UnmanagedType.LPStruct), In] Guid PowerSettingGuid,
			uint DcValueIndex);

		[DllImport("PowrProf.dll")]
		private static extern uint PowerSetActiveScheme(
			IntPtr UserRootPowerKey, // Always null
			[MarshalAs(UnmanagedType.LPStruct), In] Guid SchemeGuid);

		[DllImport("Kernel32.dll", SetLastError = true)]
		[return: MarshalAs(UnmanagedType.Bool)]
		private static extern bool GetSystemPowerStatus(
			out SYSTEM_POWER_STATUS systemPowerStatus);

		[StructLayout(LayoutKind.Sequential)]
		private struct SYSTEM_POWER_STATUS
		{
			public byte ACLineStatus;
			public byte BatteryFlag;
			public byte BatteryLifePercent;
			public byte Reserved1;
			public int BatteryLifeTime;
			public int BatteryFullLifeTime;
		}

		private const uint ERROR_SUCCESS = 0;

		#endregion

		// Video settings derived from winnt.h
		private static readonly Guid VIDEO_SUBGROUP = new Guid("7516b95f-f776-4464-8c53-06167f40cc99");
		private static readonly Guid VIDEO_ADAPTIVE_DISPLAY_BRIGHTNESS = new Guid("fbd9aa66-9553-4097-ba44-ed6e9d65eab8");
		private static readonly Guid DEVICE_POWER_POLICY_VIDEO_BRIGHTNESS = new Guid("aded5e82-b909-4619-9949-f5d71dac0bcb");
		private static readonly Guid DEVICE_POWER_POLICY_VIDEO_DIM_BRIGHTNESS = new Guid("f1fbfde2-a960-4165-9f88-50667911ce96");

		// AC/DC power source derived from winnt.h
		private static readonly Guid ACDC_POWER_SOURCE = new Guid("5d3e9a59-e9d5-4b00-a6bd-ff34ff516548");

		public static Guid GetActiveScheme()
		{
			if (PowerGetActiveScheme(
				IntPtr.Zero,
				out Guid activePolicyGuid) != ERROR_SUCCESS)
			{
				//Debug.WriteLine("Failed to get active scheme.");
				return default;
			}
			return activePolicyGuid;
		}

		#region Brightness

		public static int GetActiveSchemeBrightness()
		{
			var isOnline = IsOnline();
			if (!isOnline.HasValue)
				return -1;

			var schemeGuid = GetActiveScheme();
			uint valueIndex;

			if (isOnline.Value)
			{
				if (PowerReadACValueIndex(
					IntPtr.Zero,
					schemeGuid,
					VIDEO_SUBGROUP,
					DEVICE_POWER_POLICY_VIDEO_BRIGHTNESS,
					out valueIndex) != ERROR_SUCCESS)
				{
					//Debug.WriteLine("Failed to read AC Brightness.");
					return -1;
				}
			}
			else
			{
				if (PowerReadDCValueIndex(
					IntPtr.Zero,
					schemeGuid,
					VIDEO_SUBGROUP,
					DEVICE_POWER_POLICY_VIDEO_BRIGHTNESS,
					out valueIndex) != ERROR_SUCCESS)
				{
					//Debug.WriteLine("Failed to read DC Brightness.");
					return -1;
				}
			}
			return (int)valueIndex;
		}

		public static bool SetActiveSchemeBrightness(int brightness)
		{
			if (brightness  < 0 || brightness > 100)
				throw new ArgumentOutOfRangeException(nameof(brightness), brightness, "The brightness must be within 0 to 100.");

			var isOnline = IsOnline();
			if (!isOnline.HasValue)
				return false;

			var schemeGuid = GetActiveScheme();

			if (isOnline.Value)
			{
				if (PowerWriteACValueIndex(
					IntPtr.Zero,
					schemeGuid,
					VIDEO_SUBGROUP,
					DEVICE_POWER_POLICY_VIDEO_BRIGHTNESS,
					(uint)brightness) != ERROR_SUCCESS)
				{
					_log.Warn("Failed to write AC brightness.");
					return false;
				}
			}
			else
			{
				if (PowerWriteDCValueIndex(
					IntPtr.Zero,
					schemeGuid,
					VIDEO_SUBGROUP,
					DEVICE_POWER_POLICY_VIDEO_BRIGHTNESS,
					(uint)brightness) != ERROR_SUCCESS)
				{
					_log.Warn("Failed to write DC brightness.");
					return false;
				}
			}

			if (PowerSetActiveScheme(
				IntPtr.Zero,
				schemeGuid) != ERROR_SUCCESS)
			{
				_log.Warn("Failed to set active power scheme.");
				return false;
			}
			return true;
		}

		#endregion

		#region AC/DC Status

		public static bool? IsOnline()
		{
			if (GetSystemPowerStatus(out SYSTEM_POWER_STATUS systemPowerStatus))
			{
				switch (systemPowerStatus.ACLineStatus)
				{
					case 0: return false; // Offline
					case 1: return true; // Online
				}
			}
			return null;
		}

		#endregion
	}
}
