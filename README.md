# PC控制
![界面截图](https://user-images.githubusercontent.com/50823974/217014208-3ab003ac-c30d-4557-af5b-26363fb337f0.png)  
PC控制（PCManage）是一个可以远程调整windows电脑显示器亮度（支持外接显示器、多显示器）、音量，设置定时关机、一键锁屏、关机、重启功能。项目分为客户端（Web）和服务端（Winform），使用开源项目Zyan作为前后端通讯接口，参考开源项目Monitorian实现外接显示器亮度调节功能，使用FRP进行端口映射将本地部署的网页端口映射至公网。

# 开发

## 开发环境
  

## 相关项目
   - <a href="https://github.com/zyanfx/Zyan">Zyan</a>：RPC接口通讯框架  
   - <a href="https://github.com/emoacht/Monitorian">Monitorian</a>:调节显示器亮度的Windows桌面工具
   - <a href="https://github.com/fatedier/frp">Frp</a>:反向代理工具
  
# 背景
本人有睡前用笔记本外接的显示器看会视频/直播助眠的习惯，由于电脑桌离床有一定距离于是我每天晚上都得用一个极其别扭的姿势慢慢将显示器的亮度调低，且我使用的一款微星显示器它的设定键与电源键紧挨在一起，经常一不小心就误触了电源键。在无数次的折磨后我决定写个软件来解放我的双手。
