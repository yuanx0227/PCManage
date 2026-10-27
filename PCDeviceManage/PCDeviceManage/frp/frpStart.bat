@echo off
cd /d %~dp0

if not exist frpc.exe (
    echo frpc.exe not found, see frpc.example.ini
    exit /b 1
)
if not exist frpc.ini (
    echo frpc.ini not found, copy frpc.example.ini to frpc.ini and edit it
    exit /b 1
)

:home
frpc -c frpc.ini
rem Wait before reconnecting so a bad config does not spin in a tight loop.
timeout /t 5 /nobreak >nul
goto home
