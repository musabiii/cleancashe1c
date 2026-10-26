@echo off
rem Build CleanCache1C.exe (needs .NET SDK; target machine only needs .NET Framework 4.8)
cd /d "%~dp0"
dotnet build CleanCache1C.csproj -c Release || exit /b 1
if not exist dist mkdir dist
copy /y bin\Release\net48\CleanCache1C.exe dist\CleanCache1C.exe >nul
echo.
echo Done: %~dp0dist\CleanCache1C.exe
