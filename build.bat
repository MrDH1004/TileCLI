@echo off
chcp 949 >nul
setlocal
echo [TileCLI] 단일 포터블 exe 빌드 시작...
echo.
dotnet publish "%~dp0src\TileCLI\TileCLI.csproj" -c Release
if errorlevel 1 (
  echo.
  echo *** 빌드 실패 ***
  pause
  exit /b 1
)
echo.
echo 빌드 완료. 산출물:
echo   %~dp0src\TileCLI\bin\Release\net9.0-windows\win-x64\publish\TileCLI.exe
echo.
endlocal
pause
