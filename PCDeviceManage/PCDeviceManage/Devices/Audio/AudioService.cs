using log4net;
using NAudio.CoreAudioApi;
using System;

namespace PCDeviceManage
{
    /// <summary>
    /// 默认播放设备的音量。每次调用都重新获取默认设备：插拔耳机后默认设备会变，
    /// 而且 COM 对象不跨线程保存，避免套间切换问题。
    /// </summary>
    public sealed class AudioService
    {
        private static readonly ILog _log = LogManager.GetLogger(typeof(AudioService));

        public int GetVolume()
        {
            using (var enumerator = new MMDeviceEnumerator())
            using (MMDevice device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia))
            {
                return (int)Math.Round(device.AudioEndpointVolume.MasterVolumeLevelScalar * 100f, MidpointRounding.AwayFromZero);
            }
        }

        public void SetVolume(int volume)
        {
            int clamped = Math.Max(0, Math.Min(100, volume));
            using (var enumerator = new MMDeviceEnumerator())
            using (MMDevice device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia))
            {
                device.AudioEndpointVolume.MasterVolumeLevelScalar = clamped / 100f;
            }
            _log.Info("Set volume to " + clamped + ".");
        }
    }
}
