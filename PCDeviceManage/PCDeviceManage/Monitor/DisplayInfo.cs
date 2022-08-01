using log4net;
using Monitorian.Supplement;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;

namespace PCDeviceManage
{

	internal interface IDisplayItem
	{
		 string DeviceInstanceId { get; }
		 string DisplayName { get; }
		 bool IsInternal { get; }
		 string ConnectionDescription { get; }
	}


	public class DisplayInfo
	{
		//日志方法调用
		private static ILog _log = LogManager.GetLogger("DisplayInfo");

		[DataContract]
		public class DisplayItem : IDisplayItem
		{
			[DataMember(Order = 0)]
			public string DeviceInstanceId { get; }

			[DataMember(Order = 1)]
			public string DisplayName { get; }

			[DataMember(Order = 2)]
			public bool IsInternal { get; }

			[DataMember(Order = 3)]
			public string ConnectionDescription { get; }

			[DataMember(Order = 4)]
			public float PhysicalSize { get; }

			public DisplayItem(DisplayInformation.DisplayItem item)
			{
				this.DeviceInstanceId = item.DeviceInstanceId;
				this.DisplayName = item.DisplayName;
				this.IsInternal = item.IsInternal;
				this.ConnectionDescription = item.ConnectionDescription;
				this.PhysicalSize = item.PhysicalSize;
			}
		}

		public static DisplayItem[] GetDisplayMonitorsAsync()
		{
			var temp= DisplayInformation.GetDisplayMonitorsAsync()
				.ContinueWith(task => task.Result.Select(x => new DisplayItem(x)).ToArray()).Result;

			_log.Info("DisplayInformation.GetDisplayMonitorsAsync() 获取到设备数量："+ temp.Length);

			_log.Info(JsonConvert.SerializeObject(temp));

			return temp;
		}

	}
}
