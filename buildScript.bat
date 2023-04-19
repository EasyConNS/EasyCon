@echo off

set projname="EasyScript"

rmdir /s /q publish 

cd %projname%
rmdir /s /q bin
rmdir /s /q obj

dotnet publish %projname%.csproj --nologo -c Release -r win-x64 -f net7.0 --no-self-contained -o ..\publish

pause