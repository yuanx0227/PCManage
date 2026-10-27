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
    public class FrpTunnelTests
    {
        [Fact]
        public void FrpRestartDelayBacksOff()
        {
            int[] expected = { 5, 10, 20, 40, 80, 160, 300, 300 };
            for (int i = 0; i < expected.Length; i++)
            {
                Assert.True(FrpTunnel.GetRestartDelay(i) == TimeSpan.FromSeconds(expected[i]), "Unexpected delay after " + i + " failures.");
            }
            Assert.True(FrpTunnel.GetRestartDelay(1000) == TimeSpan.FromMinutes(5), "Expected the delay to be capped.");
        }

        [Fact]
        public void FrpStatusIsParsedFromLog()
        {
            Assert.True(FrpTunnel.ParseStatus("2026/10/27 20:00:00 [I] [proxy_manager.go:144] [7a1b] proxy added: [pcmanage]") == null, "Expected unrelated lines to be ignored.");
            Assert.True(FrpTunnel.ParseStatus("2026/10/27 20:00:00 [I] [control.go:181] [7a1b] [pcmanage] start proxy success") == FrpStatus.Connected, "Expected proxy success to mean connected.");
            Assert.True(FrpTunnel.ParseStatus("2026/10/27 20:00:00 [W] [service.go:104] login to server failed: authorization failed") == FrpStatus.Reconnecting, "Expected login failure to mean reconnecting.");
            Assert.True(FrpTunnel.ParseStatus("2026/10/27 20:00:00 [I] [control.go:200] try to reconnect to server...") == FrpStatus.Reconnecting, "Expected reconnect to be detected.");
        }

        [Fact]
        public void FrpSkipsWhenNotConfiguredOrDisabled()
        {
            using (var dir = new TempDirectory())
            {
                using (var tunnel = new FrpTunnel(dir.Path, true))
                {
                    tunnel.Start();
                    Assert.True(tunnel.Status == FrpStatus.NotConfigured, "Expected missing frpc.exe/frpc.ini to skip frp.");
                }

                File.WriteAllText(dir.File("frpc.exe"), string.Empty);
                File.WriteAllText(dir.File("frpc.ini"), string.Empty);
                using (var tunnel = new FrpTunnel(dir.Path, false))
                {
                    tunnel.Start();
                    Assert.True(tunnel.Status == FrpStatus.Disabled, "Expected frpEnabled=false to skip frp.");
                }
            }
        }

        [Fact]
        public void FrpSupervisesProcess()
        {
            using (var dir = new TempDirectory())
            {
                CopyFakeFrpc(dir.Path);

                File.WriteAllText(dir.File("frpc.ini"), "stay");
                int pid;
                using (var tunnel = new FrpTunnel(dir.Path, true))
                {
                    tunnel.Start();
                    Assert.True(WaitFor(() => tunnel.Status == FrpStatus.Connected), "Expected frpc output to report connected, got " + tunnel.Status);
                    pid = tunnel.ProcessId.Value;
                }
                Assert.True(!IsRunning(pid), "Expected frpc to be killed on dispose.");

                File.WriteAllText(dir.File("frpc.ini"), "exit");
                using (var tunnel = new FrpTunnel(dir.Path, true))
                {
                    tunnel.Start();
                    Assert.True(WaitFor(() => tunnel.Status == FrpStatus.Reconnecting && tunnel.ProcessId == null), "Expected an exited frpc to be scheduled for restart.");
                    tunnel.Stop();
                    Assert.True(tunnel.Status == FrpStatus.Stopped, "Expected stop to cancel the restart.");
                }
            }
        }
    }
}
