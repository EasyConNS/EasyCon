@echo off
chcp 65001 >nul
setlocal EnableDelayedExpansion

set "NATIVE=%~dp0"
set "NATIVE=%NATIVE:~0,-1%"
if "%OPENCV_ROOT%"=="" set "OPENCV_ROOT=%NATIVE%\..\..\..\Depend\opencv"
set "OPENCV=%OPENCV_ROOT%\build"
if "%ZIG%"=="" set "ZIG=zig"

if "%VCVARS%"=="" set "VCVARS=C:\Program Files\Microsoft Visual Studio\18\Insiders\VC\Auxiliary\Build\vcvars64.bat"
if not exist "%VCVARS%" (
    set "VSWHERE=%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe"
    if exist "!VSWHERE!" (
        for /f "usebackq delims=" %%i in (`"!VSWHERE!" -latest -property installationPath 2^>nul`) do (
            if exist "%%i\VC\Auxiliary\Build\vcvars64.bat" set "VCVARS=%%i\VC\Auxiliary\Build\vcvars64.bat"
        )
    )
)

set "WORK=%NATIVE%\out_static"
set "RUNTIMES=%NATIVE%\..\runtimes\win-x64\native"
set "LOG=%WORK%\build_static.log"

echo EasyCon ezcv_native Windows 静态构建
echo   native   : %NATIVE%
echo   opencv   : %OPENCV%
echo   vcvars   : %VCVARS%
echo   runtimes : %RUNTIMES%

"%ZIG%" version >nul 2>&1
if errorlevel 1 (
    echo 错误: zig 不可用 "%ZIG%", 可用 ZIG 环境变量覆盖
    if "%CI%"=="" pause
    exit /b 1
)

if not exist "%OPENCV%\x64\vc16\lib\opencv_world500.lib" (
    echo 错误: 未找到 opencv_world500.lib
    echo        %OPENCV%\x64\vc16\lib\
    echo        请用 OPENCV_ROOT 指向 OpenCV 官方 Windows 包根
    if "%CI%"=="" pause
    exit /b 1
)

if not exist "%VCVARS%" (
    echo 错误: 未找到 vcvars64.bat "%VCVARS%"
    echo        可用 VCVARS 环境变量覆盖
    if "%CI%"=="" pause
    exit /b 1
)

if exist "%WORK%" rmdir /s /q "%WORK%"
mkdir "%WORK%" 2>nul

call "%VCVARS%" >nul
if errorlevel 1 (
    echo 错误: vcvars 调用失败 "%VCVARS%"
    if "%CI%"=="" pause
    exit /b 1
)

(
  echo === zig version ===
  "%ZIG%" version

  echo === [1^/3] zig-msvc compile to .obj ===
  "%ZIG%" c++ -target x86_64-windows-msvc -std=c++17 -O2 -nostdlib++ -c "%NATIVE%\ezcv_native.cpp" -I "%OPENCV%\include" -o "%WORK%\ezcv_native.obj"
  if errorlevel 1 ( echo COMPILE_FAILED & goto :report )

  echo === [2^/3] link.exe static CRT -^> DLL ===
  link /nologo /DLL /MACHINE:X64 /SUBSYSTEM:CONSOLE ^
    /OUT:"%WORK%\ezcv_native.dll" /PDB:"%WORK%\ezcv_native.pdb" ^
    /NODEFAULTLIB ^
    "%WORK%\ezcv_native.obj" ^
    "%OPENCV%\x64\vc16\lib\opencv_world500.lib" ^
    libcmt.lib libvcruntime.lib libucrt.lib libcpmt.lib ^
    kernel32.lib user32.lib gdi32.lib advapi32.lib shell32.lib ole32.lib oleaut32.lib uuid.lib legacy_stdio_definitions.lib
  if errorlevel 1 ( echo LINK_FAILED & goto :report )
  if not exist "%WORK%\ezcv_native.dll" ( echo LINK_FAILED: 未产出 dll & goto :report )

  echo === [3^/3] copy to runtimes\win-x64\native ===
  if not exist "%RUNTIMES%" mkdir "%RUNTIMES%"
  copy /y "%WORK%\ezcv_native.dll" "%RUNTIMES%\ezcv_native.dll" >nul
  if errorlevel 1 ( echo COPY_FAILED & goto :report )

  echo === result ===
  dir "%RUNTIMES%\ezcv_native.dll"
  echo DONE
) > "%LOG%" 2>&1

:report
set "RC=%errorlevel%"
type "%LOG%"

if "%RC%"=="0" if exist "%RUNTIMES%\ezcv_native.dll" (
    echo.
    echo 构建完成: ezcv_native.dll
    if "%CI%"=="" pause
    exit /b 0
)
echo.
echo 构建失败 RC=%RC%
if "%CI%"=="" pause
exit /b 1
