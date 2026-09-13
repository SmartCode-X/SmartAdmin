@echo off
rem dev-start.bat - one-click start backend + frontend dev env
rem usage: double-click, or run  dev-start.bat  in repo root
rem backend http://localhost:5100 (MinimalHost)
rem   web (Vue)   http://localhost:5173 (Vite)
setlocal
cd /d "%~dp0"

echo [api] starting backend http://localhost:5100 ...
start "smart-api" cmd /k "cd /d %~dp0backend && dotnet run --project samples/MinimalHost"

echo [web] starting Vue frontend http://localhost:5173 ...
start "smart-web" cmd /k "cd /d %~dp0web && npm install && npm run dev"

echo.
echo All started in separate windows. Close a window to stop that service.
