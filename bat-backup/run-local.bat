@echo off
echo Killing processes on ports 3000 and 2004...
for /f "tokens=5" %%a in ('netstat -aon ^| findstr ":3000 "') do taskkill /f /pid %%a 2>nul
for /f "tokens=5" %%a in ('netstat -aon ^| findstr ":2004 "') do taskkill /f /pid %%a 2>nul

echo [1/4] Installing mock API packages...
pushd "%~dp0mock-api"
call npm install --silent
popd

echo [2/4] Starting Mock API server (HTTP :3000)...
start "wizMES Mock API" cmd /k "cd /d %~dp0mock-api && node server.js"

echo [3/4] Starting Mock PLC server (TCP :2004)...
start "PLC Mock Server" cmd /k "cd /d %~dp0mock-plc && node server.js"

echo [4/4] Starting Marking System...
timeout /t 2 /nobreak > nul
pushd "%~dp0MarkingSystem"
dotnet run
popd
