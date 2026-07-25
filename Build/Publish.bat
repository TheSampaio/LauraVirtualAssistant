:: Gera um executável autocontido da Laura para Windows x64.
@ECHO OFF
TITLE Publicar Laura
CLS

CD /D "%~dp0.."

ECHO Publicando a Laura (win-x64, autocontido)...
ECHO ===

dotnet publish src/Laura.App/Laura.App.csproj ^
  -c Release ^
  -r win-x64 ^
  --self-contained true ^
  -p:PublishSingleFile=true ^
  -o "_Output/Publish"

ECHO ===
ECHO Concluido. Binario em _Output\Publish\Laura.exe
PAUSE
