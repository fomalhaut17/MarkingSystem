@echo off
for /f "tokens=5" %%a in ('netstat -aon ^| findstr ":47300 "') do taskkill /f /pid %%a 2>nul

echo Starting MarkingSystem.Mock API only (HTTP :47300, AppMode=local)...
start "MarkingSystem.Mock [API]" cmd /k "cd /d %~dp0MarkingSystem.Mock && dotnet run -- --api-only"
