﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;

namespace PCDeviceManage
{
    public class DdcMonitorItem : MonitorItem
	{
		private readonly SafePhysicalMonitorHandle _handle;
		private readonly MonitorCapability _capability;

		public override bool IsBrightnessSupported => _capability.IsBrightnessSupported;
		public override bool IsContrastSupported => _capability.IsContrastSupported;
		public override bool IsPrecleared => _capability.IsPrecleared;

		public DdcMonitorItem(
			string deviceInstanceId,
			string description,
			byte displayIndex,
			byte monitorIndex,
			Rect monitorRect,
			SafePhysicalMonitorHandle handle,
			MonitorCapability capability) : base(
				deviceInstanceId: deviceInstanceId,
				description: description,
				displayIndex: displayIndex,
				monitorIndex: monitorIndex,
				monitorRect: monitorRect,
				isReachable: true
				)
		{
			this._handle = handle ?? throw new ArgumentNullException(nameof(handle));
			this._capability = capability ?? throw new ArgumentNullException(nameof(capability));
		}

		private uint _minimumBrightness = 0; // Raw minimum brightness (not always 0)
		private uint _maximumBrightness = 100; // Raw maximum brightness (not always 100)

		public override AccessResult UpdateBrightness(int brightness = -1)
		{
			var (result, minimum, current, maximum) = MonitorConfiguration.GetBrightness(_handle, _capability.IsHighLevelBrightnessSupported);

			if ((result.Status == AccessStatus.Succeeded) && (minimum < maximum) && (minimum <= current) && (current <= maximum))
			{
				this.Brightness = (int)Math.Round((double)(current - minimum) / (maximum - minimum) * 100D, MidpointRounding.AwayFromZero);
				this._minimumBrightness = minimum;
				this._maximumBrightness = maximum;
			}
			else
			{
				this.Brightness = -1; // Default
			}
			return result;
		}

		public override AccessResult SetBrightness(int brightness)
		{
			if (brightness  < 0 || brightness > 100)
				throw new ArgumentOutOfRangeException(nameof(brightness), brightness, "The brightness must be within 0 to 100.");

			var buffer = (uint)Math.Round(brightness / 100D * (_maximumBrightness - _minimumBrightness) + _minimumBrightness, MidpointRounding.AwayFromZero);

			var result = MonitorConfiguration.SetBrightness(_handle, buffer, _capability.IsHighLevelBrightnessSupported);

			if (result.Status == AccessStatus.Succeeded)
			{
				this.Brightness = brightness;
			}
			return result;
		}

		private uint _minimumContrast = 0; // Raw minimum contrast (0)
		private uint _maximumContrast = 100; // Raw maximum contrast (not always 100)

		public override AccessResult UpdateContrast()
		{
			var (result, minimum, current, maximum) = MonitorConfiguration.GetContrast(_handle);

			if ((result.Status == AccessStatus.Succeeded) && (minimum < maximum) && (minimum <= current) && (current <= maximum))
			{
				this.Contrast = (int)Math.Round((double)(current - minimum) / (maximum - minimum) * 100D, MidpointRounding.AwayFromZero);
				this._minimumContrast = minimum;
				this._maximumContrast = maximum;
			}
			else
			{
				this.Contrast = -1; // Default
			}
			return result;
		}

		public override AccessResult SetContrast(int contrast)
		{
			if (contrast  < 0 || contrast > 100)
				throw new ArgumentOutOfRangeException(nameof(contrast), contrast, "The contrast must be within 0 to 100.");

			var buffer = (uint)Math.Round(contrast / 100D * (_maximumContrast - _minimumContrast) + _minimumContrast, MidpointRounding.AwayFromZero);

			var result = MonitorConfiguration.SetContrast(_handle, buffer);

			if (result.Status == AccessStatus.Succeeded)
			{
				this.Contrast = contrast;
			}
			return result;
		}

		#region IDisposable

		private bool _isDisposed = false;

		protected override void Dispose(bool disposing)
		{
			if (_isDisposed)
				return;

			if (disposing)
			{
				// Free any other managed objects here.
				_handle.Dispose();
			}

			// Free any unmanaged objects here.
			_isDisposed = true;

			base.Dispose(disposing);
		}

		#endregion
	}
	public class SafePhysicalMonitorHandle : SafeHandle
	{
		public SafePhysicalMonitorHandle(IntPtr handle) : base(IntPtr.Zero, true)
		{
			this.handle = handle; // IntPtr.Zero may be a valid handle.
		}

		public override bool IsInvalid => false; // The validity cannot be checked by the handle.

		protected override bool ReleaseHandle()
		{
			return MonitorConfiguration.DestroyPhysicalMonitor(handle);
		}
	}
}
