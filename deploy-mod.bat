@echo off
rem Wrapper to run the PowerShell build script from the repository root
setlocal

pushd "%~dp0"
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0deploy-mod.ps1"
set rc=%ERRORLEVEL%
popd
endlocal & exit /b %rc%
