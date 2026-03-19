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

git describe --tags --abbrev=0 > %TEMP%\ms_gittag.txt 2>nul
set /p TAG=<%TEMP%\ms_gittag.txt
del /q %TEMP%\ms_gittag.txt
if "%TAG%"=="" set TAG=unknown
powershell -NoProfile -Command "Set-Content -Path '%TEMP%\ms_buildtime.txt' -Value (Get-Date -Format 'yyyyMMdd-HHmm') -Encoding ASCII -NoNewline"
set /p BUILDTIME=<%TEMP%\ms_buildtime.txt
del /q %TEMP%\ms_buildtime.txt

echo.
echo [1/6] Publishing MarkingSystem.Mock (%ENV%)...
dotnet publish MarkingSystem.Mock\MarkingSystem.Mock.csproj -c Release -o %OUT% --nologo
if errorlevel 1 (
    echo Mock publish failed.
    exit /b 1
)

echo [2/6] Publishing MarkingSystem (%ENV%)...
dotnet publish MarkingSystem\MarkingSystem.csproj -c Release -o %OUT% --nologo
if errorlevel 1 (
    echo Publish failed.
    exit /b 1
)

echo [3/6] Setting AppMode to %ENV%...
powershell -NoProfile -Command "Set-Content -Path '%OUT%\appsettings.json' -Value '{\"AppMode\":\"%ENV%\"}' -Encoding UTF8"

echo [4/6] Removing unused appsettings files...
for %%e in (local dev prod) do (
    if not "%%e"=="%ENV%" (
        if exist %OUT%\appsettings.%%e.json del /f /q %OUT%\appsettings.%%e.json
    )
)

echo [5/6] Creating run.bat and README.txt for dist...
powershell -NoProfile -Command "'@echo off','start MarkingSystem.Mock.exe','timeout /t 3 /nobreak > nul','start MarkingSystem.exe' | Set-Content '%OUT%\run.bat' -Encoding ASCII"
powershell -NoProfile -Command "(Get-Content 'dist-readme-%ENV%.txt') -replace 'TAG_PLACEHOLDER','%TAG%' -replace 'BUILDTIME_PLACEHOLDER','%BUILDTIME%' | Set-Content '%OUT%\README.txt' -Encoding UTF8"

echo [6/6] Creating ZIP archive...
set ZIP=dist\MarkingSystem-%ENV%-%BUILDTIME%.zip
if exist %ZIP% del /f /q %ZIP%
powershell -NoProfile -Command "Compress-Archive -Path '%OUT%\*' -DestinationPath '%ZIP%'"
if errorlevel 1 (
    echo ZIP failed.
    exit /b 1
)

echo.
echo Done.
echo   Tag:           %TAG%
echo   Build time:    %BUILDTIME%
echo   Output folder: %OUT%\
echo   ZIP archive:   %ZIP%
echo.

endlocal
