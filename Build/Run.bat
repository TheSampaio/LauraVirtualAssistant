:: Compila e executa a Laura em modo de depuração.
@ECHO OFF
TITLE Executar Laura
CLS

CD /D "%~dp0.."

dotnet run --project src/Laura.App/Laura.App.csproj
