@echo off
:: Usurper Reborn — Accessible Launcher (Windows)
:: Runs the game directly in the Windows console for screen reader compatibility.
:: NVDA, JAWS, and other screen readers can read the console output.

cd /d "%~dp0"
title Usurper Reborn
mode con: cols=80 lines=50
UsurperReborn.exe --local
