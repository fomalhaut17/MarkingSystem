@echo off
setlocal

set ENV=%1
if "%ENV%"=="" (
    echo Usage: build-release.bat [local^|dev]
    echo.
    echo   local  - Mock API + Mock PLC
    echo   dev    - Mock API + Real PLC via Serial
    exit /b 1
)

if not "%ENV%"=="local" if not "%ENV%"=="dev" (
    echo Error: ENV must be 'local' or 'dev'
    exit /b 1
)

set OUT=dist\%ENV%

echo.
echo [1/3] Publishing %ENV% build...
dotnet publish MarkingSystem\MarkingSystem.csproj -c Release -o %OUT% --nologo
if errorlevel 1 (
    echo Publish failed.
    exit /b 1
)

echo [2/3] Setting AppMode to %ENV%...
powershell -Command "Set-Content -Path '%OUT%\appsettings.json' -Value '{\"AppMode\":\"%ENV%\"}' -Encoding UTF8"

echo [3/3] Removing unused appsettings files...
for %%e in (local dev prod) do (
    if not "%%e"=="%ENV%" (
        if exist %OUT%\appsettings.%%e.json del /f /q %OUT%\appsettings.%%e.json
    )
)

echo.
echo Done.
echo Output folder: %OUT%\
echo   MarkingSystem.exe
echo   appsettings.json        AppMode: %ENV%
echo   appsettings.%ENV%.json
if "%ENV%"=="local" (
    echo.
    echo NOTE: Tester must also run mock servers before launching the app.
    echo       Copy mock-api\ and mock-plc\ folders and run run-local-mock.bat
)

endlocal
