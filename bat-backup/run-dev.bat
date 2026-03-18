@echo off
echo Killing processes on port 3000...
for /f "tokens=5" %%a in ('netstat -aon ^| findstr ":3000 "') do taskkill /f /pid %%a 2>nul

echo [1/4] Installing mock API packages...
pushd "%~dp0mock-api"
call npm install --silent
popd

echo [2/4] Installing mock PLC serial packages...
pushd "%~dp0mock-plc"
call npm install --silent
popd

echo [3/4] Starting Mock API server (HTTP :3000)...
start "wizMES Mock API" cmd /k "cd /d %~dp0mock-api && node server.js"

echo [4/4] Starting Serial Mock PLC server (COM6)...
start "PLC Serial Mock Server" cmd /k "cd /d %~dp0mock-plc && node serial-server.js"

echo Starting Marking System...
timeout /t 2 /nobreak > nul
pushd "%~dp0MarkingSystem"
dotnet run
popd
