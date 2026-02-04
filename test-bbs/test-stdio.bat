@echo off
echo Testing BBS Door Mode - Standard I/O Mode (--stdio)
echo.
cd /d "%~dp0"
"..\publish\local\UsurperReborn.exe" --door32 "door32.sys" --stdio
pause
