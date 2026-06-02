cd /d %~dp0
@echo off

:home

frpc -c frpc.ini

goto home