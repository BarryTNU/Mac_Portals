@echo off
echo.
echo Publishing Portals...
echo.

cd "G:\Portals"

dotnet publish Portals.csproj -f net10.0-windows10.0.19041.0 -c Release

echo.
echo Publishing complete.
pause