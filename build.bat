@echo off

set projname="EasyCon2"

rmdir /s /q publish 

cd %projname%
rmdir /s /q bin
rmdir /s /q obj

dotnet publish %projname%.csproj --nologo -c Release -r win-x64 -f net6.0-windows -p:PublishSingleFile=true --no-self-contained -o ..\publish

cd ../publish
for /F %%i in ('git rev-parse --short HEAD') do ( set commitid=%%i)
ren EasyCon2.exe EasyCon.net6.0.%commitid%.exe
cd ../
xcopy .\Firmware .\publish\Firmware\
xcopy .\Firmware .\publish\Amiibo\

pause