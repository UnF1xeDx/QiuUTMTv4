@echo off
:: This Script is by Genouka
:: Licensed under MPL2.0
:: Build a normal debug APK with assemblies embedded (no fast deploy).
:: All ABIs (arm64-v8a, x86_64) are embedded into the APK directly.

set "ExecutePath=%~dp0"
setlocal enabledelayedexpansion

set "AndroidProjectDir=%ExecutePath%..\UndertaleModToolAvalonia.Android"
set "AndroidBinDir=%AndroidProjectDir%\bin\Any CPU\Debug\net9.0-android"
set "SignedApk=%AndroidBinDir%\com.genouka.qiuutmtv4-Signed.apk"
set "OutputApk=%AndroidBinDir%\output.merged.apk"
set "ClassesDex=%ExecutePath%classes3.dex"

:: Prebuild Resources only for github actions because local build will automatically prebuild resources
call "%ExecutePath%prebuild_resources.bat"

echo [1/5] Building UndertaleModToolAvalonia...
msbuild "%ExecutePath%..\UndertaleModToolAvalonia\UndertaleModToolAvalonia.csproj" /p:Configuration=Debug /p:Platform="Any CPU"
if %ERRORLEVEL% neq 0 (
    echo ERROR: Failed to build UndertaleModToolAvalonia
    exit /b 1
)

echo [2/5] Building Android project (normal debug, assemblies embedded)...
:: Restore NuGet packages for 64-bit Android ABIs (.NET 9 Android only supports arm64-v8a
:: and x86_64; 32-bit runtimes linux-bionic-arm/linux-bionic-x86 do not exist on NuGet).
msbuild "%AndroidProjectDir%\UndertaleModToolAvalonia.Android.csproj" ^
    /t:Restore ^
    /p:RuntimeIdentifiers="android-arm64;android-x64" ^
    /p:AndroidSupportedAbis="arm64-v8a;x86_64"
if %ERRORLEVEL% neq 0 (
    echo ERROR: Failed to restore NuGet packages for Android ABIs
    exit /b 1
)

:: Normal debug build: EmbedAssembliesIntoApk=true (default) and AndroidUseSharedRuntime=false.
:: This embeds all assemblies and native libraries for all ABIs directly into the APK,
:: avoiding the FastDev _GetPrimaryCpuAbi override that restricts .__override__ to a single ABI.
msbuild "%AndroidProjectDir%\UndertaleModToolAvalonia.Android.csproj" ^
    /t:SignAndroidPackage ^
    /p:Configuration=Debug ^
    /p:Platform="Any CPU" ^
    /p:AndroidUseSharedRuntime=false ^
    /p:EmbedAssembliesIntoApk=true ^
    /p:AndroidSupportedAbis="arm64-v8a;x86_64"
if %ERRORLEVEL% neq 0 (
    echo ERROR: Failed to build Android project
    exit /b 1
)

echo [3/5] Compiling patcher and adding classes3.dex...
:: Compile yuanwow.* patcher classes (Application injection + genouka_patcher.ext auto-extraction)
:: Uses classes3.dex because .NET Android build already produces classes.dex and classes2.dex (multidex)
call "%ExecutePath%compile_patcher.bat"
if %ERRORLEVEL% neq 0 (
    echo ERROR: Failed to compile patcher
    goto :error_exit
)

:: Add classes3.dex (yuanwow.* classes) alongside the existing classes.dex and classes2.dex
:: ART (API 21+) natively loads all classes*.dex files from the APK
"%ExecutePath%7z.exe" a -tzip "%SignedApk%" "%ClassesDex%" -aoa
if %ERRORLEVEL% neq 0 (
    echo ERROR: Failed to add classes3.dex to APK
    goto :error_exit
)

echo [4/5] Signing APK...
del /q /f "%OutputApk%" 2>nul
"%ExecutePath%signapk.exe" sign --ks "%ExecutePath%debug.keystore" --ks-key-alias "androiddebugkey" --ks-pass "pass:android" --key-pass "pass:android" --in "%SignedApk%" --out "%OutputApk%"
if %ERRORLEVEL% neq 0 (
    echo ERROR: Failed to sign APK
    goto :error_exit
)

echo [5/5] Build completed successfully: %OutputApk%
endlocal
exit /b 0

:error_exit
endlocal
exit /b 1
