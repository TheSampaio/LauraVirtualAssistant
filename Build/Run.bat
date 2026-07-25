:: Builds and runs Laura in debug mode.
@ECHO OFF
TITLE Executar Laura
CLS

CD /D "%~dp0.."

dotnet run --project src/Laura.App/Laura.App.csproj
