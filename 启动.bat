@echo off
cd /d "%~dp0"
title SMAP

:: Validate existing JAVA_HOME
if defined JAVA_HOME (
    if exist "%JAVA_HOME%\bin\javac.exe" goto :start
    echo [WARN] JAVA_HOME=%JAVA_HOME% is invalid, re-detecting...
    set "JAVA_HOME="
)

:detect_java
:: Ask Java itself for its real home (handles Oracle javapath shim)
for /f "tokens=2 delims==" %%i in ('java -XshowSettings:properties -version 2^>^&1 ^| findstr /c:"java.home"') do (
    set "_jh=%%i"
)
if not defined _jh (
    echo [ERROR] Java not found in PATH.
    echo Please install JDK or set JAVA_HOME manually.
    pause
    exit /b 1
)
:: Strip leading space
set "JAVA_HOME=%_jh:~1%"
if not exist "%JAVA_HOME%\bin\javac.exe" (
    echo [ERROR] Detected JAVA_HOME=%JAVA_HOME% is missing javac.exe
    echo You may have only a JRE installed. A full JDK is required.
    pause
    exit /b 1
)
echo [INFO] Auto-detected JAVA_HOME=%JAVA_HOME%

:start
echo ====================================
echo   SMAP (Sky-MusaAutoPlay)
echo ====================================
echo.
echo NOTE: If Sky game runs as Administrator,
echo       close this window, then right-click this script
echo       and select "Run as administrator".
echo.
echo [INFO] Compiling and launching (first run downloads dependencies)...
echo.

call mvnw.cmd javafx:run

if errorlevel 1 (
    echo.
    echo [ERROR] Launch failed.
    pause
)
