using log4net;

namespace PCDeviceManage
{
    public class DisplayManage
    {
        private static ILog _log = LogManager.GetLogger("DisplayManage");
        private readonly DisplayDeviceCache _displayDeviceCache;

        public DisplayManage(DisplayDeviceCache displayDeviceCache = null)
        {
            _displayDeviceCache = displayDeviceCache ?? new DisplayDeviceCache(new DisplayDeviceProvider(), _log);
            _displayDeviceCache.Start();
        }

        public int GetDisplayBrightness(string displayName)
        {
            throw new System.NotImplementedException();
        }

        public string GetDisplayList()
        {
            try
            {
                return _displayDeviceCache.GetDisplayListJson(true);
            }
            catch (System.Exception ex)
            {
                _log.Error("Get display list failed.", ex);
                return string.Empty;
            }
        }

        public bool SetDisplayBrightness(int brightness, bool IsInternal)
        {
            try
            {
                _log.Info("Set display brightness to " + brightness + ".");
                bool result = PowerManagement.SetActiveSchemeBrightness(brightness, IsInternal);
                if (result)
                {
                    _displayDeviceCache.UpdateCachedBrightness(brightness, IsInternal);
                }
                return result;
            }
            catch (System.Exception ex)
            {
                _log.Error("Set display brightness failed.", ex);
                return false;
            }
        }
    }
}
