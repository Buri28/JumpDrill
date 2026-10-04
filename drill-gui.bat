@echo off
rem ============================================================
rem  JumpDrill GUI launcher
rem
rem    drill-gui        build if needed, then start the window
rem
rem  Always runs an incremental build first. Skipping the build
rem  whenever the exe merely exists means that editing the sources
rem  silently leaves you running the previous build, which is very
rem  hard to spot from the UI. An up-to-date build costs about a
rem  second. Build output is hidden unless the build fails.
rem
rem  Takes no drill arguments, so it is safe to call from either
rem  cmd or PowerShell (see README for why drill.bat is not).
rem
rem  ASCII only on purpose: cmd.exe mis-parses batch files that
rem  contain multi-byte characters under chcp 65001.
rem ============================================================

setlocal
set "ROOT=%~dp0"
set "EXE=%ROOT%src\JumpDrill.Gui\bin\Release\net8.0-windows\JumpDrillGui.exe"
set "LOG=%TEMP%\drillgen-gui-build.log"

dotnet build "%ROOT%src\JumpDrill.Gui\JumpDrill.Gui.csproj" -c Release -v q --nologo > "%LOG%" 2>&1
if errorlevel 1 (
    type "%LOG%" 1>&2
    echo. 1>&2
    echo [drill-gui] build failed. 1>&2
    echo [drill-gui] if the GUI is already open, close it first: it locks 1>&2
    echo [drill-gui] JumpDrill.Core.dll and the copy step cannot overwrite it. 1>&2
    exit /b 1
)

if not exist "%EXE%" (
    echo [drill-gui] executable not found: "%EXE%" 1>&2
    exit /b 1
)
start "" "%EXE%"
