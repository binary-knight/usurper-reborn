@echo off
echo Testing BBS Door Mode - Telnet Socket (will fail due to no real socket, but shows verbose output)
echo.
cd /d "%~dp0"
"..\publish\local\UsurperReborn.exe" --door32 "door32-telnet.sys" --verbose 2>&1
pause
