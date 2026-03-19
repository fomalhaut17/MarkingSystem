@echo off
echo Killing processes on ports 47300 and 47200...
for /f "tokens=5" %%a in ('netstat -aon ^| findstr ":47300 "') do taskkill /f /pid %%a 2>nul
for /f "tokens=5" %%a in ('netstat -aon ^| findstr ":47200 "') do taskkill /f /pid %%a 2>nul

echo Starting MarkingSystem.Mock (HTTP :47300 + TCP :47200)...
start "MarkingSystem.Mock" cmd /k "cd /d %~dp0MarkingSystem.Mock && dotnet run"
