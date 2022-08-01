using log4net;
using Newtonsoft.Json;
using System.Collections.Generic;
using ZyanInterface;


namespace PCDeviceManage
{
    public class DisplayManage : DisplayInterface
    {
        //日志方法调用
        private static ILog _log = LogManager.GetLogger("DisplayManage");

        public int GetDisplayBrightness(string displayName)
        {
            throw new System.NotImplementedException();
        }

        public string GetDisplayList()
        {
            try
            {
                DisplayInfoGet displayInfoGet = new DisplayInfoGet();
                var displayList = displayInfoGet.GetDisplayListAndBrightness();
                return JsonConvert.SerializeObject(displayList);
                //return displayList.tojison();

            }
            catch (System.Exception ex)
            {
                _log.Error($"获取显示器信息出错", ex);
                return string.Empty;
            }
        }

        public bool SetDisplayBrightness(int brightness,bool IsInternal)
        {
            try
            {
                _log.Info($"修改{(IsInternal?"内屏":"外屏")}亮度为：{brightness}");

                return PowerManagement.SetActiveSchemeBrightness(brightness, IsInternal);
            }
            catch (System.Exception ex)
            {
                _log.Error($"修改显示器亮度出错", ex);
                return false;
            }
        }
    }
}
