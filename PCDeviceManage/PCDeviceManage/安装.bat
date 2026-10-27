cd /d %~dp0
%cd%\PCDeviceManage.exe install
Net Start PCDeviceManageServer
sc config PCDeviceManageServer start= auto

pause 20