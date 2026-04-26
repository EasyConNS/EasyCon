@echo off
chcp 65001 >nul
setlocal enabledelayedexpansion

:: 设置项目根目录
set "ROOT_DIR=%~dp0.."
set "PUBLISH_DIR=%ROOT_DIR%\publish"
set "DIST_DIR=%ROOT_DIR%\dist"

:: 从 Directory.Build.props 自动探测目标框架版本
set "TFM=net10.0"
echo 检测到目标框架: %TFM%

:: 创建输出目录
if not exist "%DIST_DIR%" mkdir "%DIST_DIR%"

:: 清理发布目录
if exist "%PUBLISH_DIR%" rmdir /s /q "%PUBLISH_DIR%"
mkdir "%PUBLISH_DIR%"

:: ========== 编译 EasyCon2.CLI ==========
set "PROJ_NAME=EasyCon2.CLI"
set "PROJ_DIR=%ROOT_DIR%\src\%PROJ_NAME%"

echo 正在编译 "%PROJ_NAME%"...
if exist "%PROJ_DIR%\bin" rmdir /s /q "%PROJ_DIR%\bin"
if exist "%PROJ_DIR%\obj" rmdir /s /q "%PROJ_DIR%\obj"

dotnet publish "%PROJ_DIR%\%PROJ_NAME%.csproj" --nologo -c Release -r win-x64 -f %TFM% -p:PublishSingleFile=true --self-contained false -o "%PUBLISH_DIR%"
if errorlevel 1 (
    echo 错误: 编译 "%PROJ_NAME%" 失败
    pause
    exit /b 1
)

:: ========== 编译 EasyCon2 ==========
set "PROJ_NAME=EasyCon2"
set "PROJ_DIR=%ROOT_DIR%\src\%PROJ_NAME%"

echo 正在编译 "%PROJ_NAME%"...
if exist "%PROJ_DIR%\bin" rmdir /s /q "%PROJ_DIR%\bin"
if exist "%PROJ_DIR%\obj" rmdir /s /q "%PROJ_DIR%\obj"

dotnet publish "%PROJ_DIR%\%PROJ_NAME%.csproj" --nologo -c Release -r win-x64 -f %TFM%-windows7.0 -p:PublishSingleFile=true --self-contained false -o "%PUBLISH_DIR%"
if errorlevel 1 (
    echo 错误: 编译 "%PROJ_NAME%" 失败
    pause
    exit /b 1
)

:: ========== 重命名文件 ==========
echo 正在重命名文件...

:: 获取Git提交ID
for /f %%i in ('git -C "%ROOT_DIR%" rev-parse --short HEAD') do set "COMMIT_ID=%%i"

if exist "%PUBLISH_DIR%\EasyCon2.exe" ren "%PUBLISH_DIR%\EasyCon2.exe" "EasyCon2.!COMMIT_ID!.exe"
if exist "%PUBLISH_DIR%\EasyCon2.CLI.exe" ren "%PUBLISH_DIR%\EasyCon2.CLI.exe" "ezcon.exe"

:: ========== 复制额外文件 ==========
echo 正在复制额外文件...

:: 复制固件文件
if exist "%ROOT_DIR%\fw" xcopy /e /y /q "%ROOT_DIR%\fw\*" "%PUBLISH_DIR%\Firmware\"
if not exist "%PUBLISH_DIR%\Firmware" mkdir "%PUBLISH_DIR%\Firmware"

:: 删除调试文件
if exist "%PUBLISH_DIR%\*.pdb" del /q "%PUBLISH_DIR%\*.pdb"

:: ========== 移动到dist目录 ==========
echo 正在移动到dist文件夹...

robocopy "%PUBLISH_DIR%" "%DIST_DIR%\publish" /move /e
if exist "%PUBLISH_DIR%" rmdir /s /q "%PUBLISH_DIR%"

echo 构建完成!
echo 输出目录: "%DIST_DIR%\publish"
if "%CI%"=="" pause