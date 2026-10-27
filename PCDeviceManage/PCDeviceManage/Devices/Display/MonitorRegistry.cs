using log4net;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace PCDeviceManage.Devices.Display
{
    public interface IMonitorEnumerator
    {
        /// <summary>枚举当前连接的显示器。返回的对象归调用方所有，需要由调用方 Dispose。</summary>
        List<IMonitorDevice> Enumerate();
    }

    public enum BrightnessWriteStatus
    {
        Applied,
        /// <summary>同一台显示器正在写入，该值已交给正在进行的写入（之后会被写入，或被更新的值覆盖）。</summary>
        Queued,
        NotFound,
        NotSupported,
        Failed
    }

    /// <summary>
    /// 持有当前所有显示器对象（及其物理句柄），按 Id 调节亮度。
    /// 显示器变化时整体重新扫描并替换快照，旧对象随即释放。
    /// </summary>
    public sealed class MonitorRegistry : IDisposable
    {
        private static readonly TimeSpan RescanDelay = TimeSpan.FromSeconds(1.5);

        private readonly IMonitorEnumerator _enumerator;
        private readonly ILog _log;
        private readonly object _rescanLocker = new object();
        private readonly object _writersLocker = new object();
        private readonly Dictionary<string, LatestValueWriter> _writers = new Dictionary<string, LatestValueWriter>();
        private readonly Timer _rescanTimer;
        private volatile IReadOnlyList<IMonitorDevice> _devices = new IMonitorDevice[0];
        private bool _subscribed;
        private bool _isDisposed;

        public MonitorRegistry(IMonitorEnumerator enumerator, ILog log = null)
        {
            _enumerator = enumerator ?? throw new ArgumentNullException(nameof(enumerator));
            _log = log ?? LogManager.GetLogger(typeof(MonitorRegistry));
            _rescanTimer = new Timer(_ => RescanSafely(), null, Timeout.Infinite, Timeout.Infinite);
        }

        /// <summary>
        /// 首次扫描并监听显示器插拔。扫描失败只记录日志，之后可通过 Rescan 重试。
        /// </summary>
        public void Start()
        {
            RescanSafely();
            if (!_subscribed)
            {
                SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
                _subscribed = true;
            }
        }

        public void Rescan()
        {
            lock (_rescanLocker)
            {
                if (_isDisposed)
                {
                    return;
                }

                List<IMonitorDevice> fresh = _enumerator.Enumerate();
                foreach (IMonitorDevice device in fresh)
                {
                    RefreshSafely(device);
                }

                IReadOnlyList<IMonitorDevice> old = _devices;
                _devices = fresh;
                foreach (IMonitorDevice device in old)
                {
                    if (!fresh.Contains(device))
                    {
                        device.Dispose();
                    }
                }

                _log.Info("Monitor scan found " + fresh.Count + " display(s): " +
                    string.Join(", ", fresh.Select(x => x.Name + (x.IsBrightnessSupported ? string.Empty : " (unsupported)"))));
            }
        }

        public IReadOnlyList<MonitorInfo> GetMonitors(bool refresh)
        {
            IReadOnlyList<IMonitorDevice> devices = _devices;
            if (refresh)
            {
                foreach (IMonitorDevice device in devices)
                {
                    RefreshSafely(device);
                }
            }

            return devices.Select(x => new MonitorInfo
            {
                Id = x.Id,
                Name = x.Name,
                IsInternal = x.IsInternal,
                Brightness = x.Brightness,
                Supported = x.IsBrightnessSupported
            }).ToList();
        }

        public BrightnessWriteStatus SetBrightness(string id, int brightness)
        {
            if (brightness < 0 || brightness > 100)
            {
                throw new ArgumentOutOfRangeException(nameof(brightness), brightness, "The brightness must be within 0 to 100.");
            }

            LatestValueWriter writer;
            lock (_writersLocker)
            {
                if (!_writers.TryGetValue(id, out writer))
                {
                    writer = new LatestValueWriter();
                    _writers[id] = writer;
                }
            }

            return writer.Submit(brightness, value => WriteBrightness(id, value));
        }

        private BrightnessWriteStatus WriteBrightness(string id, int brightness)
        {
            BrightnessWriteStatus status = TryWrite(id, brightness);
            if (status != BrightnessWriteStatus.Failed)
            {
                return status;
            }

            // 句柄可能已经失效（显示器断开、休眠唤醒、切换输入源），重新扫描后重试一次。
            _log.Warn("Set brightness of " + id + " failed, rescanning monitors and retrying.");
            RescanSafely();
            status = TryWrite(id, brightness);
            if (status == BrightnessWriteStatus.Applied)
            {
                _log.Info("Set brightness of " + id + " to " + brightness + " after rescan.");
            }
            return status;
        }

        private BrightnessWriteStatus TryWrite(string id, int brightness)
        {
            IMonitorDevice device = _devices.FirstOrDefault(x => x.Id == id);
            if (device == null)
            {
                return BrightnessWriteStatus.NotFound;
            }
            if (!device.IsBrightnessSupported)
            {
                return BrightnessWriteStatus.NotSupported;
            }

            try
            {
                return device.TrySetBrightness(brightness) ? BrightnessWriteStatus.Applied : BrightnessWriteStatus.Failed;
            }
            catch (Exception ex)
            {
                _log.Error("Set brightness of " + device.Name + " threw.", ex);
                return BrightnessWriteStatus.Failed;
            }
        }

        private void OnDisplaySettingsChanged(object sender, EventArgs e)
        {
            // 插拔时会连续触发多次，合并成一次扫描。
            try
            {
                _rescanTimer.Change(RescanDelay, Timeout.InfiniteTimeSpan);
            }
            catch (ObjectDisposedException)
            {
            }
        }

        private void RescanSafely()
        {
            try
            {
                Rescan();
            }
            catch (Exception ex)
            {
                _log.Error("Monitor scan failed.", ex);
            }
        }

        private void RefreshSafely(IMonitorDevice device)
        {
            try
            {
                device.RefreshBrightness();
            }
            catch (Exception ex)
            {
                _log.Error("Read brightness of " + device.Name + " failed.", ex);
            }
        }

        public void Dispose()
        {
            if (_subscribed)
            {
                SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
                _subscribed = false;
            }
            _rescanTimer.Dispose();

            lock (_rescanLocker)
            {
                if (_isDisposed)
                {
                    return;
                }
                _isDisposed = true;

                foreach (IMonitorDevice device in _devices)
                {
                    device.Dispose();
                }
                _devices = new IMonitorDevice[0];
            }
        }
    }

    /// <summary>
    /// 合并同一目标的连续写入：写入进行中时到达的新值只保留最后一个，当前写入完成后再写入它。
    /// 拖动滑块时不会堆积大量缓慢的 DDC/CI 调用。
    /// </summary>
    public sealed class LatestValueWriter
    {
        private readonly object _locker = new object();
        private bool _isWriting;
        private bool _hasPending;
        private int _pending;

        public BrightnessWriteStatus Submit(int value, Func<int, BrightnessWriteStatus> write)
        {
            lock (_locker)
            {
                _pending = value;
                _hasPending = true;
                if (_isWriting)
                {
                    return BrightnessWriteStatus.Queued;
                }
                _isWriting = true;
            }

            BrightnessWriteStatus status = BrightnessWriteStatus.Queued;
            try
            {
                while (true)
                {
                    int next;
                    lock (_locker)
                    {
                        if (!_hasPending)
                        {
                            _isWriting = false;
                            return status;
                        }
                        next = _pending;
                        _hasPending = false;
                    }
                    status = write(next);
                }
            }
            catch
            {
                lock (_locker)
                {
                    _isWriting = false;
                    _hasPending = false;
                }
                throw;
            }
        }
    }
}
