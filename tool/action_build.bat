@echo off
:: This Script is by Genouka
:: Licensed under MPL2.0
:: Build fast deployment APK with all assemblies (including resx-generated DLLs)

set "ExecutePath=%~dp0"
setlocal enabledelayedexpansion

set "AndroidProjectDir=%ExecutePath%..\UndertaleModToolAvalonia.Android"
set "AndroidBinDir=%AndroidProjectDir%\bin\Any CPU\Debug\net9.0-android"
set "AndroidObjDir=%AndroidProjectDir%\obj\Any CPU\Debug\net9.0-android"
set "SignedApk=%AndroidBinDir%\com.genouka.qiuutmtv4-Signed.apk"
set "OutputApk=%AndroidBinDir%\output.merged.apk"
set "OverrideDir=%AndroidObjDir%\android\.__override__"
set "ClassesDex=%ExecutePath%classes.dex"

:: Prebuild Resources only for github actions because local build will automatically prebuild resources
call "%ExecutePath%prebuild_resources.bat"

echo [1/5] Building UndertaleModToolAvalonia...
msbuild "%ExecutePath%..\UndertaleModToolAvalonia\UndertaleModToolAvalonia.csproj" /p:Configuration=Debug /p:Platform="Any CPU"
if %ERRORLEVEL% neq 0 (
    echo ERROR: Failed to build UndertaleModToolAvalonia
    exit /b 1
)

echo [2/5] Building Android project with fast deploy and collecting all assemblies...
msbuild "%AndroidProjectDir%\UndertaleModToolAvalonia.Android.csproj" ^
    /t:SignAndroidPackage ^
    /p:Configuration=Debug ^
    /p:Platform="Any CPU" ^
    /p:AndroidUseSharedRuntime=true ^
    /p:EmbedAssembliesIntoApk=false
if %ERRORLEVEL% neq 0 (
    echo ERROR: Failed to build Android project
    exit /b 1
)

echo [3/5] Locating fast deploy files...
:: Use the android\.__override__ directory directly - it has the correct ABI subdirectory
:: structure (e.g. .__override__\arm64-v8a\*.dll) and contains ALL assemblies
:: (both framework DLLs and app assemblies like QiuLibCore.dll, UndertaleModToolAvalonia.dll, etc.)
if not exist "%OverrideDir%" (
    echo ERROR: __override__ directory not found at: %OverrideDir%
    exit /b 1
)
echo Found fast dev files in: %OverrideDir%

:: Copy satellite assemblies (*.resources.dll) from app's bin directory into each ABI subdirectory.
:: The MAUI fast-deploy mechanism sometimes misses app satellite assemblies
:: (e.g. Strings.ja.resx -> ja\UndertaleModToolAvalonia.resources.dll).
set "AppBinDir=%ExecutePath%..\UndertaleModToolAvalonia\bin\Any CPU\Debug\net9.0"
if exist "%AppBinDir%" (
    for /d %%A in ("%OverrideDir%\*") do (
        for /d %%C in ("%AppBinDir%\*") do (
            if exist "%%C\*.resources.dll" (
                if not exist "%%A\%%~nxC" mkdir "%%A\%%~nxC" 2>nul
                copy /y "%%C\*.resources.dll" "%%A\%%~nxC\" >nul 2>&1
            )
        )
    )
    echo Satellite assemblies copied from app bin to __override__
)

echo [4/5] Packaging fast deploy files into APK...
:: Create assets directory for patcher
mkdir "%ExecutePath%assets" 2>nul

:: Package the fast dev files (.__override__ directory, including ABI subdirs) into genouka_patcher.ext
"%ExecutePath%7z.exe" a -tzip "%ExecutePath%assets\genouka_patcher.ext" "%OverrideDir%"
if %ERRORLEVEL% neq 0 (
    echo ERROR: Failed to create genouka_patcher.ext
    exit /b 1
)

:: Add assets into the signed APK
"%ExecutePath%7z.exe" a -tzip "%SignedApk%" "%ExecutePath%assets" -aoa
if %ERRORLEVEL% neq 0 (
    echo ERROR: Failed to add assets to APK
    exit /b 1
)

:: Clean up temp assets
del /q /f "%ExecutePath%assets\genouka_patcher.ext" 2>nul
rmdir /s /q "%ExecutePath%assets"

:: Add classes.dex
"%ExecutePath%7z.exe" a -tzip "%SignedApk%" "%ClassesDex%" -aoa
if %ERRORLEVEL% neq 0 (
    echo ERROR: Failed to add classes.dex to APK
    exit /b 1
)

echo [5/5] Signing APK...
del /q /f "%OutputApk%" 2>nul
"%ExecutePath%signapk.exe" sign --ks "%ExecutePath%debug.keystore" --ks-key-alias "androiddebugkey" --ks-pass "pass:android" --key-pass "pass:android" --in "%SignedApk%" --out "%OutputApk%"
if %ERRORLEVEL% neq 0 (
    echo ERROR: Failed to sign APK
    exit /b 1
)

echo Build completed successfully: %OutputApk%
endlocal
