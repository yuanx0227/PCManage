# PC控制
![界面截图](https://user-images.githubusercontent.com/50823974/217014208-3ab003ac-c30d-4557-af5b-26363fb337f0.png)  
![演示视频](https://user-images.githubusercontent.com/50823974/217262746-c5047b2f-bacd-48d0-975d-186790df3a7d.gif)

PC控制（PCManage）是一个包含远程调整 Windows 电脑显示器亮度（支持外接显示器、多显示器）、音量，设置定时关机、一键锁屏、关机、重启功能的软件。运行 PCDeviceManage.exe 后会启动本地 Web 控制台，可通过浏览器访问页面完成设备控制。项目参考开源项目 Monitorian 实现外接显示器亮度调节功能，使用 FRP 进行端口映射将本地部署的网页端口映射至公网。

PCManage is a lightweight Windows device-control tool that exposes a local web console for managing display brightness, volume, lock, shutdown, restart, and scheduled shutdown actions.

# 开发

## 开发环境
  .NET Framework 4.6

## 相关项目
   - <a href="https://github.com/emoacht/Monitorian">Monitorian</a>:调节显示器亮度的Windows桌面工具
   - <a href="https://github.com/fatedier/frp">Frp</a>:反向代理工具
  
# 背景
本人有睡前用笔记本外接的显示器看会视频/直播助眠的习惯，由于电脑桌离床有一定距离于是我每天晚上都得用一个极其别扭的姿势慢慢将显示器的亮度调低，且我使用的一款微星显示器它的设定键与电源键紧挨在一起，经常一不小心就误触了电源键，在无数次的折磨后我决定写个软件来解放我的双手。
