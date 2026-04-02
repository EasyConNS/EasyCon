# publish contained execute

projname="EasyCon2.CLI"
net_sdk="net10.0"

cd ../src
rm -fr publish 
cd ${projname}
rm -fr bin
rm -fr obj

dotnet publish ${projname}.csproj -c Release -r linux-x64 -f ${net_sdk} -p:PublishSingleFile=true --self-contained -o ../publish

cd ../publish
# for /F %%i in ('git rev-parse --short HEAD') do ( set commitid=%%i)
# ren EasyCon2.exe %projname%.%commitid%.exe
mv EasyCon2.CLI ezcon
# ren Resources Amiibo

# cd ../
# xcopy ..\fw .\publish\Firmware\
# xcopy ..\test\*.txt .\publish\Script\

# mkdir .\publish\ImgLabel\
# del .\publish\*.pdb
read
