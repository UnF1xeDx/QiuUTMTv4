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
set "FastDevCollectedDir=%AndroidObjDir%\fastdev_collected"
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
:: The _CollectFastDevFiles target ran during step 2 (AfterTargets="_BuildApkFastDev")
:: and populated fastdev_collected\.__override__ with all assemblies.
:: We use that directory directly instead of re-running the target (which would have empty item groups).
if exist "%FastDevCollectedDir%\.__override__" (
    set "FastDevSourceDir=%FastDevCollectedDir%"
    echo Found fast dev files in: %FastDevCollectedDir%\.__override__
) else if exist "%OverrideDir%" (
    set "FastDevSourceDir=%AndroidObjDir%\android"
    echo Found fast dev files in: %OverrideDir%
) else (
    echo ERROR: No fast dev files found - neither fastdev_collected nor __override__ directory exists
    exit /b 1
)

echo [4/5] Packaging fast deploy files into APK...
:: Create assets directory for patcher
mkdir "%ExecutePath%assets" 2>nul

:: Package the fast dev files (.__override__ directory) into genouka_patcher.ext
"%ExecutePath%7z.exe" a -tzip "%ExecutePath%assets\genouka_patcher.ext" "!FastDevSourceDir!\.__override__"
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
