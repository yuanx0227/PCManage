using PCDeviceManage.Devices.Power;
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
    public class ShutdownSchedulerTests
    {
        [Fact]
        public void SchedulerWarnsOnceThenShutsDown()
        {
            var now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            int shutdowns = 0;
            int warnings = 0;
            using (var scheduler = new ShutdownScheduler(() => shutdowns++, () => now, TimeSpan.FromMinutes(1)))
            {
                scheduler.Warning += (s, e) => warnings++;
                scheduler.Schedule(TimeSpan.FromMinutes(5));
                Assert.True(scheduler.RemainingSeconds == 300 && scheduler.Deadline == now.AddMinutes(5), "Expected deadline five minutes ahead.");

                now = now.AddMinutes(3);
                scheduler.Poll();
                Assert.True(warnings == 0 && shutdowns == 0, "Expected nothing before the warning window.");

                now = now.AddSeconds(61);
                scheduler.Poll();
                scheduler.Poll();
                Assert.True(warnings == 1 && shutdowns == 0, "Expected exactly one warning inside the last minute.");
                Assert.True(scheduler.RemainingSeconds == 59, "Expected 59 seconds remaining.");

                now = now.AddSeconds(59);
                scheduler.Poll();
                scheduler.Poll();
                Assert.True(shutdowns == 1, "Expected exactly one shutdown at the deadline.");
                Assert.True(scheduler.Deadline == null && scheduler.RemainingSeconds == 0, "Expected schedule to clear after shutdown.");
            }
        }

        [Fact]
        public void SchedulerCancelPreventsShutdown()
        {
            var now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            int shutdowns = 0;
            int changes = 0;
            using (var scheduler = new ShutdownScheduler(() => shutdowns++, () => now))
            {
                scheduler.Changed += (s, e) => changes++;
                scheduler.Schedule(TimeSpan.FromMinutes(10));
                scheduler.Schedule(TimeSpan.Zero);
                Assert.True(scheduler.Deadline == null && changes == 2, "Expected zero delay to cancel.");

                scheduler.Schedule(TimeSpan.FromMinutes(10));
                scheduler.Cancel();
                scheduler.Cancel();
                Assert.True(changes == 4, "Expected a second cancel to be a no-op.");

                now = now.AddHours(1);
                scheduler.Poll();
                Assert.True(shutdowns == 0, "Expected cancelled schedule not to shut down.");
            }
        }

        [Fact]
        public void SchedulerUsesWallClockAfterSleep()
        {
            var now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            int shutdowns = 0;
            using (var scheduler = new ShutdownScheduler(() => shutdowns++, () => now))
            {
                scheduler.Schedule(TimeSpan.FromMinutes(10));
                now = now.AddMinutes(30); // 电脑睡眠了 30 分钟，计时器期间没有触发
                scheduler.Poll();
                Assert.True(shutdowns == 1, "Expected overdue schedule to shut down on wake.");
            }
        }
    }
}
