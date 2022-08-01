using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ZyanInterface
{
    public interface AudioInterface
    {
        string GetAudioList();

        bool SetAudioVolume(int volume, string DeviceID);

        int GetVolume();

    }
}
