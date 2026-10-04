@echo off
rem ============================================================
rem  JumpDrill launcher
rem
rem    drill --seq "R:8>b, L:a>1" --interval 110 --sec 20
rem    drill --seq "R:8>b" --mirror --hands split --sec 30
rem    drill --help
rem
rem  Always runs an incremental build first. Skipping the build
rem  whenever the exe merely exists means that editing the sources
rem  silently leaves you running the previous build. An up-to-date
rem  build costs about a second, and its output is hidden unless
rem  it fails. (--rebuild is still accepted but no longer needed.)
rem
rem  The working directory is left alone, so --out stays relative
rem  to wherever you invoke this from.
rem
rem  ASCII only on purpose: cmd.exe mis-parses batch files that
rem  contain multi-byte characters under chcp 65001.
rem  See README.md for the Japanese notes.
rem ============================================================

setlocal
set "ROOT=%~dp0"
set "EXE=%ROOT%src\JumpDrill.Cli\bin\Release\net8.0\drill.exe"
set "LOG=%TEMP%\drillgen-cli-build.log"
set ARGS=

rem Drop --rebuild and rebuild the argument list by hand:
rem %* ignores shift, so it cannot be used here.
:parse
if "%~1"=="" goto build
if /i "%~1"=="--rebuild" (
    shift
    goto parse
)
set ARGS=%ARGS% %1
shift
goto parse

:build
dotnet build "%ROOT%src\JumpDrill.Cli\JumpDrill.Cli.csproj" -c Release -v q --nologo > "%LOG%" 2>&1
if errorlevel 1 (
    type "%LOG%" 1>&2
    echo. 1>&2
    echo [drill] build failed. 1>&2
    exit /b 1
)

:run
if not exist "%EXE%" (
    echo [drill] executable not found: "%EXE%" 1>&2
    exit /b 1
)
"%EXE%"%ARGS%
exit /b %ERRORLEVEL%
