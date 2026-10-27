using PCDeviceManage.Devices.Display;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using static PCDeviceManage.Tests.TestSupport;

namespace PCDeviceManage.Tests
{
    public class MonitorRegistryTests
    {
        [Fact]
        public void MonitorIdIsStableAndUrlSafe()
        {
            string id = MonitorIds.FromDeviceInstanceId(@"DISPLAY\DELA0A1\5&2B2E1F0&0&UID4353");
            Assert.True(id == MonitorIds.FromDeviceInstanceId(@"display\dela0a1\5&2b2e1f0&0&uid4353"), "Expected id to ignore case.");
            Assert.True(id.Length == 8 && id.All(c => "0123456789abcdef".IndexOf(c) >= 0), "Expected an 8-char hex id.");
            Assert.True(id != MonitorIds.FromDeviceInstanceId(@"DISPLAY\DELA0A1\5&2B2E1F0&0&UID4354"), "Expected different monitors to get different ids.");
        }

        [Fact]
        public void RegistryRoutesBrightnessById()
        {
            var a = new FakeMonitor("aaaa0001", "External A");
            var b = new FakeMonitor("bbbb0002", "External B");
            using (var registry = CreateRegistry(a, b))
            {
                Assert.True(registry.SetBrightness("bbbb0002", 30) == BrightnessWriteStatus.Applied, "Expected write to B to apply.");
                Assert.True(registry.SetBrightness("aaaa0001", 70) == BrightnessWriteStatus.Applied, "Expected write to A to apply.");

                Assert.True(a.Writes.SequenceEqual(new[] { 70 }), "Expected A to receive only its own value.");
                Assert.True(b.Writes.SequenceEqual(new[] { 30 }), "Expected B to receive only its own value.");

                var infos = registry.GetMonitors(false);
                Assert.True(infos.Single(x => x.Id == "aaaa0001").Brightness == 70, "Expected A brightness to be reported.");
                Assert.True(infos.Single(x => x.Id == "bbbb0002").Brightness == 30, "Expected B brightness to be reported.");
            }
        }

        [Fact]
        public void RegistryReadsHardwareOnlyWhenAsked()
        {
            var monitor = new FakeMonitor("aaaa0001", "External A");
            using (var registry = CreateRegistry(monitor))
            {
                int afterScan = monitor.RefreshCount;
                Assert.True(afterScan == 1, "Expected rescan to read brightness once.");

                registry.GetMonitors(false);
                Assert.True(monitor.RefreshCount == afterScan, "Expected cached read not to touch hardware.");

                monitor.HardwareBrightness = 55;
                var refreshed = registry.GetMonitors(true);
                Assert.True(monitor.RefreshCount == afterScan + 1 && refreshed[0].Brightness == 55, "Expected refresh to read hardware.");
            }
        }

        [Fact]
        public void RescanDisposesReplacedMonitors()
        {
            var first = new FakeMonitor("aaaa0001", "External A");
            var second = new FakeMonitor("aaaa0001", "External A");
            var enumerator = new FakeEnumerator(new IMonitorDevice[] { first }, new IMonitorDevice[] { second });
            var registry = new MonitorRegistry(enumerator);
            registry.Rescan();
            registry.Rescan();

            Assert.True(first.IsDisposed && !second.IsDisposed, "Expected the replaced monitor to be disposed.");
            Assert.True(registry.SetBrightness("aaaa0001", 40) == BrightnessWriteStatus.Applied && second.Writes.SequenceEqual(new[] { 40 }), "Expected writes to go to the new monitor.");

            registry.Dispose();
            Assert.True(second.IsDisposed, "Expected registry dispose to release monitors.");
        }

        [Fact]
        public void FailedWriteRescansAndRetries()
        {
            var stale = new FakeMonitor("aaaa0001", "External A") { OnSet = value => false };
            var fresh = new FakeMonitor("aaaa0001", "External A");
            var enumerator = new FakeEnumerator(new IMonitorDevice[] { stale }, new IMonitorDevice[] { fresh });
            using (var registry = new MonitorRegistry(enumerator))
            {
                registry.Rescan();
                Assert.True(registry.SetBrightness("aaaa0001", 25) == BrightnessWriteStatus.Applied, "Expected write to succeed after rescan.");
                Assert.True(enumerator.Calls == 2, "Expected a failed write to trigger exactly one rescan.");
                Assert.True(stale.IsDisposed && fresh.Writes.SequenceEqual(new[] { 25 }), "Expected the retry to use the rescanned monitor.");
            }
        }

        [Fact]
        public void UnknownAndUnsupportedMonitorsAreRejected()
        {
            var unsupported = new FakeMonitor("cccc0003", "Old Monitor") { Supported = false };
            using (var registry = CreateRegistry(unsupported))
            {
                Assert.True(registry.SetBrightness("ffffffff", 50) == BrightnessWriteStatus.NotFound, "Expected unknown id to be reported.");
                Assert.True(registry.SetBrightness("cccc0003", 50) == BrightnessWriteStatus.NotSupported, "Expected unsupported monitor to be reported.");
                Assert.True(unsupported.Writes.Count == 0, "Expected no write to an unsupported monitor.");
                Assert.True(!registry.GetMonitors(false)[0].Supported, "Expected unsupported flag in the listing.");
            }
        }

        [Fact]
        public async Task ConcurrentWritesKeepLatestValue()
        {
            var entered = new ManualResetEventSlim();
            var release = new ManualResetEventSlim();
            var monitor = new FakeMonitor("aaaa0001", "External A")
            {
                OnSet = value =>
                {
                    if (value == 10)
                    {
                        entered.Set();
                        release.Wait();
                    }
                    return true;
                }
            };

            using (var registry = CreateRegistry(monitor))
            {
                Task<BrightnessWriteStatus> first = Task.Run(() => registry.SetBrightness("aaaa0001", 10));
                Assert.True(entered.Wait(5000), "Expected first write to start.");

                Assert.True(registry.SetBrightness("aaaa0001", 20) == BrightnessWriteStatus.Queued, "Expected second write to be queued.");
                Assert.True(registry.SetBrightness("aaaa0001", 30) == BrightnessWriteStatus.Queued, "Expected third write to be queued.");
                release.Set();

                Assert.True(await Task.WhenAny(first, Task.Delay(5000)) == first, "Expected the writer to finish.");
                Assert.Equal(BrightnessWriteStatus.Applied, await first);
                Assert.True(monitor.Writes.SequenceEqual(new[] { 10, 30 }), "Expected intermediate value to be skipped, got " + string.Join(",", monitor.Writes));
            }
        }
    }
}
