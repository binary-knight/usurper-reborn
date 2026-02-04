@echo off
echo Testing BBS Door Mode - DOOR.SYS Format (Legacy)
echo.
cd /d "%~dp0"
"..\publish\local\UsurperReborn.exe" --doorsys "door.sys" --verbose
pause
