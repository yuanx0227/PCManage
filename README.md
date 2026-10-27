# PC控制
![界面截图](https://user-images.githubusercontent.com/50823974/217014208-3ab003ac-c30d-4557-af5b-26363fb337f0.png)  
![演示视频](https://user-images.githubusercontent.com/50823974/217262746-c5047b2f-bacd-48d0-975d-186790df3a7d.gif)

PC控制（PCManage）是一个包含远程调整 Windows 电脑显示器亮度（支持外接显示器、多显示器）、音量，设置定时关机、一键锁屏、关机、重启功能的软件。运行 PCDeviceManage.exe 后会启动本地 Web 控制台，可通过浏览器访问页面完成设备控制。项目参考开源项目 Monitorian 实现外接显示器亮度调节功能，使用 FRP 进行端口映射将本地部署的网页端口映射至公网。

PCManage is a lightweight Windows device-control tool that exposes a local web console for managing display brightness, volume, lock, shutdown, restart, and scheduled shutdown actions.

# 使用

## 访问令牌
控制台需要登录后才能使用。首次运行时会自动生成访问令牌，保存在程序目录（`PCDeviceManage.exe` 所在目录）下的 `access_token` 文件中，也可以改成自己的值（重启程序生效）。浏览器打开控制台后，在登录页输入令牌即可，也可以在托盘菜单中选择“复制访问令牌”。程序目录需要有写权限。

## 配置
配置文件是程序目录下的 `settings.json`（首次运行时生成）：

| 字段 | 默认值 | 说明 |
|---|---|---|
| `webHost` | `127.0.0.1` | 监听地址：IP 地址、`localhost`，或 `+` 表示所有地址（局域网访问用，首次启动时 Windows 防火墙会询问是否放行） |
| `webPort` | `9527` | 端口，frp 示例配置默认映射这个端口 |
| `frpEnabled` | `true` | `frp\frpc.exe` 和 `frp\frpc.ini` 都存在时是否启动 frp |

日志写在程序目录下的 `Log\`。配置、令牌和日志都放在程序目录，旧版本放在 `%LocalAppData%\PCManage` 下的配置会在首次启动时迁移过来，然后删除该目录。

## 接口
脚本或快捷指令可以在请求头 `X-Access-Token` 中携带令牌。除 GET 外的请求都必须带请求头 `X-PCM-Request: 1`，参数用表单（`application/x-www-form-urlencoded`）传递。返回值统一为 `{"ok": true, "data": ...}` 或 `{"ok": false, "error": "..."}`。

| 方法 | 地址 | 参数 | 说明 |
|---|---|---|---|
| GET | `/api/displays` | `refresh=true` 时重新读取硬件 | 显示器列表 `[{id, name, isInternal, brightness, supported}]` |
| POST | `/api/displays/{id}/brightness` | `value` 0–100 | 设置某台显示器的亮度 |
| POST | `/api/displays/rescan` | | 重新检测显示器 |
| GET / POST | `/api/audio/volume` | POST：`value` 0–100 | 读取 / 设置音量 |
| POST | `/api/power/lock`、`/api/power/shutdown`、`/api/power/restart` | | 锁屏 / 关机 / 重启 |
| GET | `/api/power/schedule` | | 定时关机状态 `{remainingSeconds, deadline}` |
| POST | `/api/power/schedule` | `seconds` | 在指定秒数后关机 |
| DELETE | `/api/power/schedule` | | 取消定时关机 |

## 公网访问（frp，可选）
仓库不再包含 frp 的程序和服务器配置，需要时按以下步骤配置：

1. 从 [frp Releases](https://github.com/fatedier/frp/releases) 下载与服务器 frps 版本匹配的 Windows 版 `frpc.exe`，放到 `PCDeviceManage\frp\` 目录（当前示例配置按 0.39.x 的 ini 格式编写）。
2. 把 `frp\frpc.example.ini` 复制为 `frp\frpc.ini`，填写服务器地址和 `token`；frps 侧也要配置相同的 `authentication_method = token` 和 `token`。
3. 尽量用 HTTPS 访问（示例配置中的 `https2http` 方式），否则访问令牌会以明文在公网上传输。只是自己用的话，更推荐 Tailscale / ZeroTier 这类组网工具，完全不把端口暴露到公网。

`frpc.ini` 和 `frpc.exe` 已加入 `.gitignore`。没有这两个文件（或 `settings.json` 中 `frpEnabled` 为 `false`）时，程序会跳过 frp，只提供本机访问。

frpc 由程序直接作为子进程启动：输出写入日志，意外退出后自动重启（间隔从 5 秒逐步加长到 5 分钟），程序退出或崩溃时 frpc 会一起结束。

# 开发

## 开发环境
- .NET 8 SDK 或更高版本，目标框架为 `net8.0-windows`，运行需要 Windows 10 及以上。
- 主要依赖：ASP.NET Core（Kestrel，Web 控制台）、WinForms（托盘）、NAudio.Wasapi（音量）、System.Management（WMI 亮度）、log4net。
- .NET 8 的官方支持在 2026-11-10 结束。升级到 .NET 10 时，把 `PCDeviceManage`、`PCDeviceManage.Tests`、`FakeFrpc` 三个项目的 `TargetFramework` 中的 `net8.0` 改成 `net10.0`，再安装 .NET 10 SDK 即可。

## 构建与测试
```
dotnet build PCDeviceManage\PCDeviceManage.sln
dotnet test PCDeviceManage\PCDeviceManage.sln
```

测试不会真的锁屏、关机或修改开机启动项：关机动作被替换为计数，开机自启只读写 `HKCU\Software\PCManageTests` 下的临时键。`HardwareSmokeTests` 会在本机真实硬件上只读地枚举显示器、读取音量。`FakeFrpc` 项目是测试用的假 frpc。

## 发布
```
./publish.ps1
```

默认发布为自包含的 win-x64 单文件（约 80 MB），目标机器不需要安装 .NET；加 `-FrameworkDependent` 则只有约 1.5 MB，但目标机器需要安装 .NET 8 桌面运行时和 ASP.NET Core 运行时。输出在 `publish\`，`log4net.config` 和 `frp\` 目录放在 exe 旁边。

## 相关项目
   - <a href="https://github.com/emoacht/Monitorian">Monitorian</a>:调节显示器亮度的Windows桌面工具
   - <a href="https://github.com/fatedier/frp">Frp</a>:反向代理工具
  
# 背景
本人有睡前用笔记本外接的显示器看会视频/直播助眠的习惯，由于电脑桌离床有一定距离于是我每天晚上都得用一个极其别扭的姿势慢慢将显示器的亮度调低，且我使用的一款微星显示器它的设定键与电源键紧挨在一起，经常一不小心就误触了电源键，在无数次的折磨后我决定写个软件来解放我的双手。
