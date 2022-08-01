using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ZyanInterface
{
    public interface SystemInterface
    {
        void LockWindows();
        void WindowsShutdown();
        void WindowsRestart();
    }
}
