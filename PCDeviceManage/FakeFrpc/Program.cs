using System;
using System.IO;
using System.Threading;

// 用法与 frpc 相同：FakeFrpc -c <配置文件>。配置文件内容为 stay 时输出连接成功并保持运行，否则输出登录失败并退出。
string mode = File.ReadAllText(args[1]).Trim();
if (mode == "stay")
{
    Console.WriteLine("2026/10/27 20:00:00 [I] [control.go:181] [test] [pcmanage] start proxy success");
    Thread.Sleep(TimeSpan.FromMinutes(1));
    return 0;
}

Console.WriteLine("2026/10/27 20:00:00 [W] [service.go:104] login to server failed: test");
return 1;
