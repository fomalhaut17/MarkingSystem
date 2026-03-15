@echo off
echo [1/3] Installing mock API packages...
cd /d "%~dp0mock-api"
call npm install --silent

echo [2/3] Starting Mock API server...
start "wizMES Mock API" cmd /k "node server.js"

echo [3/3] Starting Marking System...
timeout /t 2 /nobreak > nul
cd /d "%~dp0MarkingSystem"
dotnet run
