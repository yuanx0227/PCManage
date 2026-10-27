# PC控制
![界面截图](https://user-images.githubusercontent.com/50823974/217014208-3ab003ac-c30d-4557-af5b-26363fb337f0.png)  
![演示视频](https://user-images.githubusercontent.com/50823974/217262746-c5047b2f-bacd-48d0-975d-186790df3a7d.gif)

PC控制（PCManage）是一个包含远程调整 Windows 电脑显示器亮度（支持外接显示器、多显示器）、音量，设置定时关机、一键锁屏、关机、重启功能的软件。运行 PCDeviceManage.exe 后会启动本地 Web 控制台，可通过浏览器访问页面完成设备控制。项目参考开源项目 Monitorian 实现外接显示器亮度调节功能，使用 FRP 进行端口映射将本地部署的网页端口映射至公网。

PCManage is a lightweight Windows device-control tool that exposes a local web console for managing display brightness, volume, lock, shutdown, restart, and scheduled shutdown actions.

# 使用

## 访问令牌
控制台需要登录后才能使用。首次运行时会自动生成访问令牌，保存在 `%LocalAppData%\PCManage\settings.json` 的 `accessToken` 中，也可以改成自己的值（重启程序生效）。浏览器打开控制台后，在登录页输入令牌即可。

## 配置
配置文件是 `%LocalAppData%\PCManage\settings.json`：

| 字段 | 默认值 | 说明 |
|---|---|---|
| `webHost` | `127.0.0.1` | 监听地址。局域网访问可改成 `+`（需要管理员执行一次 `netsh http add urlacl`） |
| `webPort` | `9527` | 端口，frp 示例配置默认映射这个端口 |
| `accessToken` | 自动生成 | 访问令牌 |
| `frpEnabled` | `true` | `frp\frpc.exe` 和 `frp\frpc.ini` 都存在时是否启动 frp |

日志写在 `%LocalAppData%\PCManage\Log`。

脚本或快捷指令可以在请求头 `X-Access-Token` 中携带令牌；会改变状态的接口（锁屏、关机、亮度、音量等）只接受 POST，并且必须带请求头 `X-PCM-Request: 1`。

## 公网访问（frp，可选）
仓库不再包含 frp 的程序和服务器配置，需要时按以下步骤配置：

1. 从 [frp Releases](https://github.com/fatedier/frp/releases) 下载与服务器 frps 版本匹配的 Windows 版 `frpc.exe`，放到 `PCDeviceManage\frp\` 目录（当前示例配置按 0.39.x 的 ini 格式编写）。
2. 把 `frp\frpc.example.ini` 复制为 `frp\frpc.ini`，填写服务器地址和 `token`；frps 侧也要配置相同的 `authentication_method = token` 和 `token`。
3. 尽量用 HTTPS 访问（示例配置中的 `https2http` 方式），否则访问令牌会以明文在公网上传输。只是自己用的话，更推荐 Tailscale / ZeroTier 这类组网工具，完全不把端口暴露到公网。

`frpc.ini` 和 `frpc.exe` 已加入 `.gitignore`。没有这两个文件时，程序会跳过 frp，只提供本机访问。

# 开发

## 开发环境
  .NET Framework 4.6

## 相关项目
   - <a href="https://github.com/emoacht/Monitorian">Monitorian</a>:调节显示器亮度的Windows桌面工具
   - <a href="https://github.com/fatedier/frp">Frp</a>:反向代理工具
  
# 背景
本人有睡前用笔记本外接的显示器看会视频/直播助眠的习惯，由于电脑桌离床有一定距离于是我每天晚上都得用一个极其别扭的姿势慢慢将显示器的亮度调低，且我使用的一款微星显示器它的设定键与电源键紧挨在一起，经常一不小心就误触了电源键，在无数次的折磨后我决定写个软件来解放我的双手。
