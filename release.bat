echo on

del /a /f /s /q  E:\switch\PokemonTycoon\EasyCon\bin\Release\*.xml
del /a /f /s /q  E:\switch\PokemonTycoon\EasyCon\bin\Release\*.pdb
del /a /f /s /q  E:\switch\PokemonTycoon\EasyCon\bin\Release\*.config

XCOPY  E:\switch\PokemonTycoon\EasyCon\bin\Release E:\switch\EasyCon\ /S/A/Y /EXCLUDE:release_exclude.txt

pause