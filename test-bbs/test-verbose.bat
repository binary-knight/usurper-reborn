@echo off
echo Testing BBS Door Mode - Verbose Debug Output
echo.
cd /d "%~dp0"
"..\publish\local\UsurperReborn.exe" --door32 "door32.sys" --verbose
pause
