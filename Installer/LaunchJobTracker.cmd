@echo off
:: Launch JobTracker server and open browser
:: This script is installed alongside JobTracker.exe by the MSI installer.

cd /d "%~dp0"

:: Use HTTP — no dev cert needed on installed machines
set ASPNETCORE_URLS=http://localhost:7046

:: Start the server minimised so it runs in the background
start "" /min JobTracker.exe

:: Give the server a moment to start, then open the browser
timeout /t 4 /nobreak >nul
start "" "http://localhost:7046"
