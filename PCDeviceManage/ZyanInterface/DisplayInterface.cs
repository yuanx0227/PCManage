using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ZyanInterface
{
    public interface DisplayInterface
    {

        string GetDisplayList();

        int GetDisplayBrightness(string displayName);

        bool SetDisplayBrightness(int brightness,bool IsInternal);

    }
}
