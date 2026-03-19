@echo off
setlocal

set ENV=%1
if "%ENV%"=="" (
    echo Usage: build-release.bat [local^|dev]
    echo.
    echo   local  - Mock API + Mock TCP PLC
    echo   dev    - Mock API + Mock Cnet Serial PLC
    exit /b 1
)

if not "%ENV%"=="local" if not "%ENV%"=="dev" (
    echo Error: ENV must be 'local' or 'dev'
    exit /b 1
)

set OUT=dist\%ENV%

echo.
echo [1/4] Publishing MarkingSystem (%ENV%)...
dotnet publish MarkingSystem\MarkingSystem.csproj -c Release -o %OUT% --nologo
if errorlevel 1 (
    echo Publish failed.
    exit /b 1
)

echo [2/4] Publishing MarkingSystem.Mock (%ENV%)...
dotnet publish MarkingSystem.Mock\MarkingSystem.Mock.csproj -c Release -o %OUT% --nologo
if errorlevel 1 (
    echo Mock publish failed.
    exit /b 1
)

echo [3/4] Setting AppMode to %ENV%...
powershell -Command "Set-Content -Path '%OUT%\appsettings.json' -Value '{\"AppMode\":\"%ENV%\"}' -Encoding UTF8"

echo [4/5] Creating run.bat for dist...
(
    echo @echo off
    echo start "" "MarkingSystem.Mock.exe"
    echo timeout /t 3 /nobreak ^> nul
    echo start "" "MarkingSystem.exe"
) > %OUT%\run.bat

echo [5/6] Removing unused appsettings files...
for %%e in (local dev prod) do (
    if not "%%e"=="%ENV%" (
        if exist %OUT%\appsettings.%%e.json del /f /q %OUT%\appsettings.%%e.json
    )
)

echo [6/6] Creating ZIP archive...
set ZIP=dist\MarkingSystem-%ENV%.zip
if exist %ZIP% del /f /q %ZIP%
powershell -Command "Compress-Archive -Path '%OUT%\*' -DestinationPath '%ZIP%'"
if errorlevel 1 (
    echo ZIP failed.
    exit /b 1
)

echo.
echo Done.
echo Output folder: %OUT%\
echo   MarkingSystem.exe
echo   MarkingSystem.Mock.exe
echo   appsettings.json        AppMode: %ENV%
echo   appsettings.%ENV%.json
echo.
echo ZIP archive: %ZIP%
echo.
if "%ENV%"=="local" (
    echo NOTE: Run MarkingSystem.Mock.exe first, then MarkingSystem.exe
)
if "%ENV%"=="dev" (
    echo NOTE: Run MarkingSystem.Mock.exe first (reads appsettings), then MarkingSystem.exe
)

endlocal
