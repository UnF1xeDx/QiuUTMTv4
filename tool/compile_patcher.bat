@echo off
:: Compile yuanwow.* patcher Java sources into classes3.dex
:: Usage: compile_patcher.bat
:: Output: %BASEDIR%classes3.dex
::
:: Note: .NET Android build already produces classes.dex and classes2.dex (multidex).
:: We use classes3.dex to avoid conflict. ART (API 21+) loads all classes*.dex files.

set "BASEDIR=%~dp0"
setlocal enabledelayedexpansion

set "SRCDIR=%BASEDIR%src"
set "OUTDIR=%BASEDIR%build"
set "ANDROID_JAR=%BASEDIR%android_sdk\android.jar"
set "DX_JAR=%BASEDIR%android_sdk\dx.jar"
set "OUTPUT_DEX=%BASEDIR%classes3.dex"

:: Locate javac
set "JAVAC="
if exist "C:\Program Files\Android\Android Studio\jbr\bin\javac.exe" (
    set "JAVAC=C:\Program Files\Android\Android Studio\jbr\bin\javac.exe"
) else (
    where javac >nul 2>&1
    if !ERRORLEVEL! equ 0 (
        set "JAVAC=javac"
    )
)

if "!JAVAC!"=="" (
    echo ERROR: javac not found. Install Android Studio or JDK.
    exit /b 1
)

:: Verify tools exist
if not exist "%ANDROID_JAR%" (
    echo ERROR: android.jar not found at %ANDROID_JAR%
    exit /b 1
)
if not exist "%DX_JAR%" (
    echo ERROR: dx.jar not found at %DX_JAR%
    exit /b 1
)

:: Clean output directory
if exist "%OUTDIR%" rmdir /s /q "%OUTDIR%"
mkdir "%OUTDIR%" 2>nul

echo [1/2] Compiling Java sources...
"!JAVAC!" -source 1.8 -target 1.8 -cp "%ANDROID_JAR%" -d "%OUTDIR%" "%SRCDIR%\yuanwow\YApplication.java" "%SRCDIR%\yuanwow\foxr\starter\gutmt\patcher.java" "%SRCDIR%\yuanwow\foxr\starter\ui\AssetZipUtil.java" "%SRCDIR%\yuanwow\foxr\starter\NetUtils.java"
if %ERRORLEVEL% neq 0 (
    echo ERROR: Java compilation failed
    exit /b 1
)

echo [2/2] Converting to DEX...
if exist "%OUTPUT_DEX%" del /q /f "%OUTPUT_DEX%"
java -jar "%DX_JAR%" --dex --output="%OUTPUT_DEX%" "%OUTDIR%"
if %ERRORLEVEL% neq 0 (
    echo ERROR: DEX conversion failed
    exit /b 1
)

echo.
echo Success: %OUTPUT_DEX%
exit /b 0
