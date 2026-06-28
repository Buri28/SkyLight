@echo off
setlocal

set "SCRIPT_DIR=%~dp0"
set "PROJECT_FILE=%SCRIPT_DIR%SkyLight\SkyLight.csproj"
set "CONFIGURATION=%~1"

if "%CONFIGURATION%"=="" set "CONFIGURATION=Debug"

where dotnet >nul 2>nul
if errorlevel 1 (
    echo dotnet command was not found.
    exit /b 1
)

dotnet build "%PROJECT_FILE%" -c %CONFIGURATION% /property:GenerateFullPaths=true /consoleloggerparameters:NoSummary
if errorlevel 1 exit /b %ERRORLEVEL%

set "OUTPUT_DLL=%SCRIPT_DIR%SkyLight\bin\%CONFIGURATION%\net48\net48\SkyLight.dll"
set "PLUGINS_DIR=D:\BSManager\BSInstances\1.40.8\Plugins"

echo Copying to %PLUGINS_DIR%...
copy /y "%OUTPUT_DLL%" "%PLUGINS_DIR%\"
if errorlevel 1 (
    echo Failed to copy DLL.
    exit /b 1
)
echo Done.
exit /b 0
