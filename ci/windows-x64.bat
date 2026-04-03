@echo off

set projname="EasyCon2.CLI"

cd ..
if not exist dist mkdir dist
cd ./src
rmdir /s /q publish 
cd %projname%
rmdir /s /q bin
rmdir /s /q obj

dotnet publish %projname%.csproj --nologo -c Release -r win-x64 -f net8.0 -p:PublishSingleFile=true --no-self-contained -o ..\publish

set projname="EasyCon2"
cd ../
cd %projname%
dotnet publish %projname%.csproj --nologo -c Release -r win-x64 -f net8.0-windows7.0 -p:PublishSingleFile=true --no-self-contained -o ..\publish

cd ..\publish
for /F %%i in ('git rev-parse --short HEAD') do ( set commitid=%%i)
ren EasyCon2.exe %projname%.%commitid%.exe
ren EasyCon2.CLI.exe ezcon.exe
ren Resources Amiibo

cd ../
xcopy ..\fw .\publish\Firmware\
xcopy ..\test\*.txt .\publish\Script\

mkdir .\publish\ImgLabel\
del .\publish\*.pdb

robocopy ./publish/ ../dist/publish/ /move /e
rmdir /s /q publish
pause