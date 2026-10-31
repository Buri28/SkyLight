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

rem Usage: build.bat [Debug^|Release] [Plugins folder to copy to]
rem If omitted, copies to Plugins of both 1.40.8 and 1.44.1 under %BS_INSTANCES% (default D:\BSManager\BSInstances)
set "OUTPUT_DLL=%SCRIPT_DIR%SkyLight\bin\%CONFIGURATION%\net48\SkyLight.dll"
if "%BS_INSTANCES%"=="" set "BS_INSTANCES=D:\BSManager\BSInstances"

if not "%~2"=="" (
    call :copy_dll "%~2"
    if errorlevel 1 exit /b 1
    goto :done
)

call :copy_dll "%BS_INSTANCES%\1.40.8\Plugins"
if errorlevel 1 exit /b 1
call :copy_dll "%BS_INSTANCES%\1.44.1\Plugins"
if errorlevel 1 exit /b 1

:done
echo Done.
exit /b 0

:copy_dll
echo Copying to %~1...
copy /y "%OUTPUT_DLL%" "%~1\"
if errorlevel 1 (
    echo Failed to copy DLL.
    exit /b 1
)
exit /b 0
