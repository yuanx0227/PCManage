using log4net;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace PCDeviceManage
{
    public enum FrpStatus
    {
        NotConfigured,
        Disabled,
        Starting,
        Connected,
        Reconnecting,
        Stopped
    }

    /// <summary>
    /// 托管 frpc 子进程：输出写入日志，进程退出后按指数退避重启；
    /// frpc 进程放进 Job Object，本程序退出或崩溃时由系统一并结束，不会残留。
    /// </summary>
    public sealed class FrpTunnel : IDisposable
    {
        private static readonly TimeSpan MinRestartDelay = TimeSpan.FromSeconds(5);
        private static readonly TimeSpan MaxRestartDelay = TimeSpan.FromMinutes(5);

        private readonly string _directory;
        private readonly bool _enabled;
        private readonly ILog _log;
        private readonly object _locker = new object();
        private readonly Timer _restartTimer;
        private readonly KillOnCloseJob _job;
        private Process _process;
        private int _failures;
        private bool _isStopping;
        private bool _isDisposed;
        private FrpStatus _status = FrpStatus.Stopped;

        public FrpTunnel(string directory, bool enabled, ILog log = null)
        {
            _directory = directory;
            _enabled = enabled;
            _log = log ?? LogManager.GetLogger(typeof(FrpTunnel));
            _restartTimer = new Timer(_ => StartProcess(), null, Timeout.Infinite, Timeout.Infinite);
            _job = KillOnCloseJob.TryCreate(_log);
        }

        public string ExecutablePath => Path.Combine(_directory, "frpc.exe");
        public string ConfigPath => Path.Combine(_directory, "frpc.ini");

        public FrpStatus Status
        {
            get
            {
                lock (_locker)
                {
                    return _status;
                }
            }
        }

        /// <summary>当前 frpc 进程的 Id，没有运行时为 null。</summary>
        public int? ProcessId
        {
            get
            {
                lock (_locker)
                {
                    return _process?.Id;
                }
            }
        }

        /// <summary>状态变化时触发（在后台线程上）。</summary>
        public event EventHandler StatusChanged;

        public void Start()
        {
            if (!_enabled)
            {
                _log.Info("frp is disabled in settings.");
                SetStatus(FrpStatus.Disabled);
                return;
            }
            if (!File.Exists(ExecutablePath) || !File.Exists(ConfigPath))
            {
                _log.Info("frp\\frpc.exe or frp\\frpc.ini not found, skipping frp. See frp\\frpc.example.ini.");
                SetStatus(FrpStatus.NotConfigured);
                return;
            }

            lock (_locker)
            {
                _isStopping = false;
                _failures = 0;
            }
            StartProcess();
        }

        public void Restart()
        {
            Stop();
            Start();
        }

        public void Stop()
        {
            Process process;
            lock (_locker)
            {
                _isStopping = true;
                if (!_isDisposed)
                {
                    _restartTimer.Change(Timeout.Infinite, Timeout.Infinite);
                }
                process = _process;
                _process = null;
            }

            KillQuietly(process);
            SetStatus(FrpStatus.Stopped);
        }

        private void StartProcess()
        {
            lock (_locker)
            {
                if (_isStopping || _isDisposed || _process != null)
                {
                    return;
                }

                var process = new Process
                {
                    StartInfo = new ProcessStartInfo(ExecutablePath, "-c \"" + ConfigPath + "\"")
                    {
                        WorkingDirectory = _directory,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    },
                    EnableRaisingEvents = true
                };
                process.OutputDataReceived += (s, e) => OnOutput(e.Data);
                process.ErrorDataReceived += (s, e) => OnOutput(e.Data);
                process.Exited += (s, e) => OnExited(process);

                try
                {
                    process.Start();
                }
                catch (Exception ex) when (ex is Win32Exception || ex is InvalidOperationException)
                {
                    _log.Error("Failed to start frpc.", ex);
                    process.Dispose();
                    ScheduleRestart();
                    return;
                }

                _job?.Assign(process);
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                _process = process;
                _log.Info("frpc started, pid " + process.Id + ".");
            }
            SetStatus(FrpStatus.Starting);
        }

        private void OnOutput(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                return;
            }

            _log.Info("[frpc] " + line);
            FrpStatus? status = ParseStatus(line);
            if (status == FrpStatus.Connected)
            {
                lock (_locker)
                {
                    _failures = 0;
                }
            }
            if (status.HasValue)
            {
                SetStatus(status.Value);
            }
        }

        private void OnExited(Process process)
        {
            bool restart;
            lock (_locker)
            {
                if (_process != process)
                {
                    process.Dispose();
                    return;
                }
                _process = null;
                restart = !_isStopping && !_isDisposed;
            }

            int exitCode = -1;
            try
            {
                exitCode = process.ExitCode;
            }
            catch (InvalidOperationException)
            {
            }
            process.Dispose();

            if (restart)
            {
                _log.Warn("frpc exited with code " + exitCode + ".");
                lock (_locker)
                {
                    ScheduleRestart();
                }
                SetStatus(FrpStatus.Reconnecting);
            }
        }

        // 调用方需持有 _locker。
        private void ScheduleRestart()
        {
            if (_isStopping || _isDisposed)
            {
                return;
            }
            TimeSpan delay = GetRestartDelay(_failures);
            _failures++;
            _log.Info("Restarting frpc in " + (int)delay.TotalSeconds + "s.");
            _restartTimer.Change(delay, Timeout.InfiniteTimeSpan);
        }

        private void SetStatus(FrpStatus status)
        {
            lock (_locker)
            {
                if (_status == status)
                {
                    return;
                }
                _status = status;
            }
            StatusChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>第 n 次连续失败后的重启等待时间：5s、10s、20s……，最长 5 分钟。</summary>
        public static TimeSpan GetRestartDelay(int consecutiveFailures)
        {
            double seconds = MinRestartDelay.TotalSeconds * Math.Pow(2, Math.Min(consecutiveFailures, 10));
            return TimeSpan.FromSeconds(Math.Min(seconds, MaxRestartDelay.TotalSeconds));
        }

        /// <summary>从 frpc（0.39.x）的日志行判断连接状态，无法判断时返回 null。</summary>
        public static FrpStatus? ParseStatus(string line)
        {
            if (line.IndexOf("start proxy success", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return FrpStatus.Connected;
            }
            if (line.IndexOf("login to server failed", StringComparison.OrdinalIgnoreCase) >= 0 ||
                line.IndexOf("connect to server error", StringComparison.OrdinalIgnoreCase) >= 0 ||
                line.IndexOf("try to reconnect", StringComparison.OrdinalIgnoreCase) >= 0 ||
                line.IndexOf("start error", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return FrpStatus.Reconnecting;
            }
            return null;
        }

        private void KillQuietly(Process process)
        {
            if (process == null)
            {
                return;
            }

            try
            {
                if (!process.HasExited)
                {
                    process.Kill();
                    process.WaitForExit(3000);
                }
                _log.Info("frpc stopped.");
            }
            catch (Exception ex) when (ex is Win32Exception || ex is InvalidOperationException)
            {
                _log.Warn("Failed to stop frpc.", ex);
            }
            finally
            {
                process.Dispose();
            }
        }

        public void Dispose()
        {
            Stop();
            lock (_locker)
            {
                if (_isDisposed)
                {
                    return;
                }
                _isDisposed = true;
                _restartTimer.Dispose();
            }
            _job?.Dispose();
        }

        /// <summary>
        /// 设置了 JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE 的 Job Object：最后一个句柄关闭（包括本进程崩溃）时，系统结束其中所有进程。
        /// </summary>
        private sealed class KillOnCloseJob : IDisposable
        {
            private const int JobObjectExtendedLimitInformation = 9;
            private const uint JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE = 0x2000;

            private readonly IntPtr _handle;
            private readonly ILog _log;

            private KillOnCloseJob(IntPtr handle, ILog log)
            {
                _handle = handle;
                _log = log;
            }

            public static KillOnCloseJob TryCreate(ILog log)
            {
                IntPtr handle = CreateJobObject(IntPtr.Zero, null);
                if (handle == IntPtr.Zero)
                {
                    log.Warn("CreateJobObject failed: " + Marshal.GetLastWin32Error());
                    return null;
                }

                var info = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION();
                info.BasicLimitInformation.LimitFlags = JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE;
                int length = Marshal.SizeOf(typeof(JOBOBJECT_EXTENDED_LIMIT_INFORMATION));
                IntPtr buffer = Marshal.AllocHGlobal(length);
                try
                {
                    Marshal.StructureToPtr(info, buffer, false);
                    if (!SetInformationJobObject(handle, JobObjectExtendedLimitInformation, buffer, (uint)length))
                    {
                        log.Warn("SetInformationJobObject failed: " + Marshal.GetLastWin32Error());
                        CloseHandle(handle);
                        return null;
                    }
                }
                finally
                {
                    Marshal.FreeHGlobal(buffer);
                }
                return new KillOnCloseJob(handle, log);
            }

            public void Assign(Process process)
            {
                if (!AssignProcessToJobObject(_handle, process.Handle))
                {
                    _log.Warn("AssignProcessToJobObject failed: " + Marshal.GetLastWin32Error());
                }
            }

            public void Dispose()
            {
                CloseHandle(_handle);
            }

            [StructLayout(LayoutKind.Sequential)]
            private struct JOBOBJECT_BASIC_LIMIT_INFORMATION
            {
                public long PerProcessUserTimeLimit;
                public long PerJobUserTimeLimit;
                public uint LimitFlags;
                public UIntPtr MinimumWorkingSetSize;
                public UIntPtr MaximumWorkingSetSize;
                public uint ActiveProcessLimit;
                public UIntPtr Affinity;
                public uint PriorityClass;
                public uint SchedulingClass;
            }

            [StructLayout(LayoutKind.Sequential)]
            private struct IO_COUNTERS
            {
                public ulong ReadOperationCount;
                public ulong WriteOperationCount;
                public ulong OtherOperationCount;
                public ulong ReadTransferCount;
                public ulong WriteTransferCount;
                public ulong OtherTransferCount;
            }

            [StructLayout(LayoutKind.Sequential)]
            private struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
            {
                public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
                public IO_COUNTERS IoInfo;
                public UIntPtr ProcessMemoryLimit;
                public UIntPtr JobMemoryLimit;
                public UIntPtr PeakProcessMemoryUsed;
                public UIntPtr PeakJobMemoryUsed;
            }

            [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
            private static extern IntPtr CreateJobObject(IntPtr lpJobAttributes, string lpName);

            [DllImport("kernel32.dll", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            private static extern bool SetInformationJobObject(IntPtr hJob, int infoClass, IntPtr lpJobObjectInfo, uint cbJobObjectInfoLength);

            [DllImport("kernel32.dll", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            private static extern bool AssignProcessToJobObject(IntPtr hJob, IntPtr hProcess);

            [DllImport("kernel32.dll", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            private static extern bool CloseHandle(IntPtr hObject);
        }
    }
}
