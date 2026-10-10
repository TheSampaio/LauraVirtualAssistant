@echo off
title Run Laura
cd /d "%~dp0.."
dotnet run --project Laura.App/Laura.App.csproj
