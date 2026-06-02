using System;
using System.Collections.Generic;

namespace PCDeviceManage.Tests
{
    internal static class Program
    {
        private static int Main()
        {
            try
            {
                ForcedReadRefreshesDisplayList();
                CachedReadCanReturnLastDisplayList();
                DisplayManageReadRefreshesDisplayList();
                Console.WriteLine("All display refresh tests passed.");
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex.Message);
                return 1;
            }
        }

        private static void ForcedReadRefreshesDisplayList()
        {
            var provider = new FakeDisplayDeviceProvider();
            provider.SetBrightness(40);
            var cache = new DisplayDeviceCache(provider);

            string first = cache.GetDisplayListJson(true);
            provider.SetBrightness(80);
            string second = cache.GetDisplayListJson(true);

            Assert(provider.CallCount == 2, "Expected forced reads to call provider each time.");
            Assert(first.Contains("\"Brightness\":40"), "Expected first brightness to be serialized.");
            Assert(second.Contains("\"Brightness\":80"), "Expected second brightness to be refreshed.");
        }

        private static void CachedReadCanReturnLastDisplayList()
        {
            var provider = new FakeDisplayDeviceProvider();
            var cache = new DisplayDeviceCache(provider);

            provider.SetBrightness(25);
            cache.GetDisplayListJson(true);
            provider.SetBrightness(75);
            string cached = cache.GetDisplayListJson(false);

            Assert(provider.CallCount == 1, "Expected cached read to avoid provider call.");
            Assert(cached.Contains("\"Brightness\":25"), "Expected cached brightness to remain available.");
            Assert(!cached.Contains("\"Brightness\":75"), "Expected cached read not to refresh state.");
        }

        private static void DisplayManageReadRefreshesDisplayList()
        {
            var provider = new FakeDisplayDeviceProvider();
            provider.SetBrightness(10);
            var cache = new DisplayDeviceCache(provider);
            var manager = new DisplayManage(cache);

            provider.SetBrightness(90);
            string refreshed = manager.GetDisplayList();

            Assert(provider.CallCount == 2, "Expected DisplayManage.GetDisplayList to refresh provider state.");
            Assert(refreshed.Contains("\"Brightness\":90"), "Expected DisplayManage.GetDisplayList to return refreshed brightness.");
        }

        private static void Assert(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidOperationException(message);
            }
        }

        private sealed class FakeDisplayDeviceProvider : IDisplayDeviceProvider
        {
            private int _brightness;

            public int CallCount { get; private set; }

            public void SetBrightness(int brightness)
            {
                _brightness = brightness;
            }

            public List<DisplayInfoGet.DeviceItemPlus> GetDisplayListAndBrightness()
            {
                CallCount++;
                return new List<DisplayInfoGet.DeviceItemPlus>
                {
                    new DisplayInfoGet.DeviceItemPlus(
                        new DisplayContext.DeviceItem("DISPLAY\\TEST", "Test Panel", 1, 0),
                        "Test Panel",
                        true)
                    {
                        Brightness = _brightness
                    }
                };
            }
        }
    }
}
