@echo off
echo [1/4] Installing mock API packages...
cd /d "%~dp0mock-api"
call npm install --silent

echo [2/4] Starting Mock API server (HTTP :3000)...
start "wizMES Mock API" cmd /k "node server.js"

echo [3/4] Starting Mock PLC server (TCP :2004)...
start "PLC Mock Server" cmd /k "cd /d %~dp0mock-plc && node server.js"

echo [4/4] Starting Marking System...
timeout /t 2 /nobreak > nul
pushd "%~dp0MarkingSystem"
dotnet run
popd
