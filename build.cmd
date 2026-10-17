@echo off
rem Build file EXE portable cua Lam 1 Cham tren may Windows.
rem Yeu cau: .NET SDK 10 (https://dotnet.microsoft.com/download/dotnet/10.0).
setlocal
cd /d "%~dp0"

where dotnet >nul 2>nul
if errorlevel 1 (
  echo Khong tim thay lenh dotnet. Hay cai .NET SDK 10 roi chay lai.
  exit /b 1
)

echo [1/3] Build va kiem tra canh bao...
dotnet build LamMotCham.sln -c Release || goto :fail

echo [2/3] Chay test...
dotnet test LamMotCham.sln -c Release --no-build || goto :fail

echo [3/3] Dong goi EXE portable...
dotnet publish src\LamMotCham.App\LamMotCham.App.csproj -p:PublishProfile=win-x64 || goto :fail

echo.
echo Xong: artifacts\publish\win-x64\LamMotCham.exe
exit /b 0

:fail
echo.
echo Build that bai. Xem loi o tren.
exit /b 1
