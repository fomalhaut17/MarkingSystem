@echo off
echo [1/3] Installing mock API packages...
pushd "%~dp0mock-api"
call npm install --silent
popd

echo [2/3] Starting Mock API server (HTTP :3000)...
start "wizMES Mock API" cmd /k "cd /d %~dp0mock-api && node server.js"

echo [3/3] Starting Mock PLC server (TCP :2004)...
start "PLC Mock Server" cmd /k "cd /d %~dp0mock-plc && node server.js"
