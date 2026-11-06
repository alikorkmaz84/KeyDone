@echo off
setlocal
title KeyDone
rem Bu dosya nereden acilirsa acilsin KeyDone klasorunde calisir.
cd /d "%~dp0"
set "DOTNET=%ProgramFiles%\dotnet\dotnet.exe"
set "DOTNET_NOLOGO=1"
set "DOTNET_CLI_TELEMETRY_OPTOUT=1"
set "EXE=src\KeyDone\bin\Release\net10.0-windows\KeyDone.exe"

if not exist "%DOTNET%" (
  echo .NET SDK bulunamadi. Lutfen dotnet.microsoft.com adresinden .NET 10 SDK kurun.
  goto hata
)

echo KeyDone hazirlaniyor. Ilk acilis bir iki dakika surebilir, sonraki acilislar birkac saniye surer.
"%DOTNET%" build "src\KeyDone\KeyDone.csproj" -c Release -v quiet -nologo
if errorlevel 1 goto hata

start "" "%EXE%"
exit /b 0

:hata
echo.
echo KeyDone baslatilamadi. Yukaridaki hata mesajini Claude'a iletebilirsiniz.
pause
exit /b 1
