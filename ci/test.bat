@echo off
chcp 65001 >nul
setlocal enabledelayedexpansion

set "ROOT_DIR=%~dp0.."
set "TFM=net10.0"

echo Running tests...
dotnet test "%ROOT_DIR%\EasyCon2.slnx" --nologo -c Release -f %TFM%
if errorlevel 1 (
    echo ERROR: Tests failed
    pause
    exit /b 1
)

echo All tests passed!
pause
