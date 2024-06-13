@echo off

set projname="ILViewer"

cd %projname%
rmdir /s /q bin
rmdir /s /q obj

dotnet publish %projname%.csproj --nologo -c Release -r win-x64 -f net8.0-windows7.0 -p:PublishSingleFile=true --no-self-contained -o ..\publish

pause