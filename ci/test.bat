@echo off
chcp 65001 >nul
setlocal enabledelayedexpansion

set "ROOT_DIR=%~dp0.."
set "CONFIG=%1"
if "%CONFIG%"=="" set "CONFIG=Release"

echo Running tests (%CONFIG%)...
dotnet test "%ROOT_DIR%\EasyCon2.slnx" --nologo -c %CONFIG%
if errorlevel 1 (
    echo ERROR: Tests failed
    pause
    exit /b 1
)

echo All tests passed!
if "%CI%"=="" pause
