@echo off
echo Killing processes on ports 47300 and 47200...
for /f "tokens=5" %%a in ('netstat -aon ^| findstr ":47300 "') do taskkill /f /pid %%a 2>nul
for /f "tokens=5" %%a in ('netstat -aon ^| findstr ":47200 "') do taskkill /f /pid %%a 2>nul

echo [1/2] Starting MarkingSystem.Mock (HTTP :47300 + PLC from appsettings)...
start "MarkingSystem.Mock" cmd /k "cd /d %~dp0MarkingSystem.Mock && dotnet run"

echo [2/2] Starting Marking System...
timeout /t 3 /nobreak > nul
pushd "%~dp0MarkingSystem"
dotnet run
popd
