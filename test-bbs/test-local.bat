@echo off
echo Testing BBS Door Mode - Local (DOOR32.SYS with CommType=0)
echo.
cd /d "%~dp0"
"..\publish\local\UsurperReborn.exe" --door32 "door32.sys"
pause
