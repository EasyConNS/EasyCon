@echo off

set projname="EasyCon2"

cd src
rmdir /s /q publish 
cd EasyCon2\

rmdir /s /q bin
rmdir /s /q obj

dotnet publish %projname%.csproj --nologo -c Release -r win-x64 -f net8.0-windows7.0 -p:PublishSingleFile=true --no-self-contained -o ..\publish

cd ..\publish
for /F %%i in ('git rev-parse --short HEAD') do ( set commitid=%%i)
ren EasyCon2.exe EasyCon.net8.0.%commitid%.exe

cd ../
xcopy ..\Firmware .\publish\Firmware\
xcopy ..\Script .\publish\Script\
xcopy .\EasyCon.Capture\Resources\*.traineddata .\publish\Tessdata\
xcopy .\EasyCon2\Resources\AmiiboImages .\publish\Amiibo\AmiiboImages\
mkdir .\publish\ImgLabel\
del .\publish\*.pdb
pause