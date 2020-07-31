echo on

del /a /f /s /q  E:\switch\PokemonTycoon\EasyCon\bin\x64\Release\*.xml
del /a /f /s /q  E:\switch\PokemonTycoon\EasyCon\bin\x64\Release\*.pdb
del /a /f /s /q  E:\switch\PokemonTycoon\EasyCon\bin\x64\Release\*.config

del /a /f /s /q  E:\switch\PokemonTycoon\EasyCon\bin\x86\Release\*.xml
del /a /f /s /q  E:\switch\PokemonTycoon\EasyCon\bin\x86\Release\*.pdb
del /a /f /s /q  E:\switch\PokemonTycoon\EasyCon\bin\x86\Release\*.config

XCOPY  E:\switch\PokemonTycoon\EasyCon\bin\x64\Release E:\switch\EasyCon\ /S/A/Y /EXCLUDE:release_exclude.txt

pause