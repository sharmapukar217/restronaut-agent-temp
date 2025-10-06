@echo off
setlocal

if "%~1"=="" exit /b

echo Updating...

set "PROGRAM=%~1"
set "BASE_DIR={{BASE_DIR}}"
set "EXTRACT_DIR=%BASE_DIR%\extracted"

for %%A in ("%PROGRAM%") do taskkill /F /IM "%%~nxA" >nul 2>&1

for %%F in (RestronautService*.exe) do if /I not "%%F"=="%VERSION%" del /f /q "%%F"
copy "%PROGRAM%" "RestronautService.{{VERSION}}.exe" >nul 2>&1
xcopy "%EXTRACT_DIR%\*" "%CD%\" /E /Y /I >nul

start "" "RestronautService.exe"
rmdir /s /q "%BASE_DIR%"
exit