using CoreAudioApi;
using log4net;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ZyanInterface;

namespace PCDeviceManage
{
    public class AudioManage : AudioInterface
    {
        static MMDevice device;

        //日志方法调用
        private static ILog _log = LogManager.GetLogger("DisplayManage");

      public  AudioManage() 
        {
            //初始化设备
            MMDeviceEnumerator devEnum = new MMDeviceEnumerator();
            device = devEnum.GetDefaultAudioEndpoint(EDataFlow.eRender, ERole.eMultimedia);
        }

        public string GetAudioList()
        {
            throw new NotImplementedException();
        }

        public int GetVolume()
        {
            try
            {
                return Convert.ToInt32(device.AudioEndpointVolume.MasterVolumeLevelScalar * 100.0f);
            }
            catch (Exception ex)
            {
                _log.Error("获取当前音量出错", ex);
                return 0;
            }
        }

        public bool SetAudioVolume(int volume, string DeviceID)
        {
            try
            {

                if (volume < 0) device.AudioEndpointVolume.MasterVolumeLevelScalar = 0.0f;
                else if (volume > 100) device.AudioEndpointVolume.MasterVolumeLevelScalar = 100.0f;
                else device.AudioEndpointVolume.MasterVolumeLevelScalar = volume / 100.0f;
                _log.Info("修改音量为：" + volume);
                return true;
            }
            catch (Exception ex)
            {
                _log.Error("修改音量出错", ex);
                return false;
            }
        }
    }
}
