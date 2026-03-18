@echo off
echo Killing processes on ports 3000 and 2004...
for /f "tokens=5" %%a in ('netstat -aon ^| findstr ":3000 "') do taskkill /f /pid %%a 2>nul
for /f "tokens=5" %%a in ('netstat -aon ^| findstr ":2004 "') do taskkill /f /pid %%a 2>nul

echo Starting MarkingSystem.Mock (HTTP :3000 + TCP :2004)...
start "MarkingSystem.Mock" cmd /k "cd /d %~dp0MarkingSystem.Mock && dotnet run"
