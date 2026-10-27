using log4net;
using System;
using System.Threading;

namespace PCDeviceManage.Devices.Power
{
    public sealed class ShutdownWarningEventArgs : EventArgs
    {
        public ShutdownWarningEventArgs(DateTime deadline)
        {
            Deadline = deadline;
        }

        /// <summary>预定的关机时间（UTC）。</summary>
        public DateTime Deadline { get; }
    }

    /// <summary>
    /// 定时关机。记录截止时间而不是逐秒计数：电脑睡眠期间计时器不走，醒来后按墙上时间判断是否到点。
    /// 到点前 WarningLead 触发一次 Warning，可用于提醒用户取消。
    /// </summary>
    public sealed class ShutdownScheduler : IDisposable
    {
        // 计时器最长等待间隔，保证睡眠唤醒、调整系统时间后能及时按墙上时间重新判断。
        private static readonly TimeSpan MaxTimerInterval = TimeSpan.FromSeconds(30);
        private static readonly ILog _log = LogManager.GetLogger(typeof(ShutdownScheduler));

        private readonly Action _shutdown;
        private readonly Func<DateTime> _clock;
        private readonly object _locker = new object();
        private readonly Timer _timer;
        private DateTime? _deadline;
        private bool _warned;
        private bool _isDisposed;

        public ShutdownScheduler(Action shutdown, Func<DateTime> clock = null, TimeSpan? warningLead = null)
        {
            _shutdown = shutdown ?? throw new ArgumentNullException(nameof(shutdown));
            _clock = clock ?? (() => DateTime.UtcNow);
            WarningLead = warningLead ?? TimeSpan.FromMinutes(1);
            _timer = new Timer(_ => Poll(), null, Timeout.Infinite, Timeout.Infinite);
        }

        public TimeSpan WarningLead { get; }

        /// <summary>即将关机时触发（在计时器线程上）。</summary>
        public event EventHandler<ShutdownWarningEventArgs> Warning;

        /// <summary>安排、取消或执行关机后触发（在调用方线程或计时器线程上）。</summary>
        public event EventHandler Changed;

        /// <summary>预定的关机时间（UTC），未安排时为 null。</summary>
        public DateTime? Deadline
        {
            get
            {
                lock (_locker)
                {
                    return _deadline;
                }
            }
        }

        public int RemainingSeconds
        {
            get
            {
                lock (_locker)
                {
                    if (!_deadline.HasValue)
                    {
                        return 0;
                    }
                    return Math.Max(0, (int)Math.Ceiling((_deadline.Value - _clock()).TotalSeconds));
                }
            }
        }

        /// <summary>安排在 delay 之后关机；delay 不大于 0 时取消。</summary>
        public void Schedule(TimeSpan delay)
        {
            if (delay <= TimeSpan.Zero)
            {
                Cancel();
                return;
            }

            lock (_locker)
            {
                ThrowIfDisposed();
                _deadline = _clock() + delay;
                _warned = delay <= WarningLead;
                ArmTimer();
            }
            _log.Info("Shutdown scheduled in " + (int)delay.TotalSeconds + "s.");
            Changed?.Invoke(this, EventArgs.Empty);
        }

        public void Cancel()
        {
            bool hadDeadline;
            lock (_locker)
            {
                hadDeadline = _deadline.HasValue;
                _deadline = null;
                _warned = false;
                if (!_isDisposed)
                {
                    _timer.Change(Timeout.Infinite, Timeout.Infinite);
                }
            }

            if (hadDeadline)
            {
                _log.Info("Scheduled shutdown cancelled.");
                Changed?.Invoke(this, EventArgs.Empty);
            }
        }

        /// <summary>
        /// 按当前时间检查是否需要提醒或关机。内部计时器会调用它，测试也可以直接调用。
        /// </summary>
        public void Poll()
        {
            bool shutdown = false;
            DateTime? warnFor = null;

            lock (_locker)
            {
                if (_isDisposed || !_deadline.HasValue)
                {
                    return;
                }

                DateTime now = _clock();
                if (now >= _deadline.Value)
                {
                    _deadline = null;
                    _warned = false;
                    shutdown = true;
                }
                else
                {
                    if (!_warned && _deadline.Value - now <= WarningLead)
                    {
                        _warned = true;
                        warnFor = _deadline.Value;
                    }
                    ArmTimer();
                }
            }

            if (warnFor.HasValue)
            {
                Warning?.Invoke(this, new ShutdownWarningEventArgs(warnFor.Value));
            }

            if (shutdown)
            {
                _log.Info("Scheduled shutdown time reached.");
                Changed?.Invoke(this, EventArgs.Empty);
                _shutdown();
            }
        }

        // 调用方需持有 _locker。
        private void ArmTimer()
        {
            DateTime now = _clock();
            DateTime next = _warned ? _deadline.Value : _deadline.Value - WarningLead;
            TimeSpan due = next - now;
            if (due < TimeSpan.Zero)
            {
                due = TimeSpan.Zero;
            }
            if (due > MaxTimerInterval)
            {
                due = MaxTimerInterval;
            }
            _timer.Change(due, Timeout.InfiniteTimeSpan);
        }

        private void ThrowIfDisposed()
        {
            if (_isDisposed)
            {
                throw new ObjectDisposedException(nameof(ShutdownScheduler));
            }
        }

        public void Dispose()
        {
            lock (_locker)
            {
                if (_isDisposed)
                {
                    return;
                }
                _isDisposed = true;
                _deadline = null;
                _timer.Dispose();
            }
        }
    }
}
