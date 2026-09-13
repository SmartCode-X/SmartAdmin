@echo off
rem ci.bat - run the same gates CI runs, locally, before pushing
rem
rem usage:
rem   ci.bat                          backend(sqlite) + web + docs + template + audit   (no Docker)
rem   ci.bat -Stage backend -Dialect mysql,postgres,sqlserver     dialect legs (needs Docker Desktop)
rem   ci.bat -Stage all -Dialect sqlite,mysql,postgres,sqlserver  everything, before merging to main
rem   ci.bat -Stage web-e2e           Playwright against a real MinimalHost
rem   ci.bat -FullSqlServer           SqlServer full suite instead of the dialect-sensitive subset
rem
rem Exit code is 0 only when every gate passed. See scripts\ci-local.ps1 for the details.
setlocal
cd /d "%~dp0"
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\ci-local.ps1" %*
exit /b %ERRORLEVEL%
