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
set "AssetsDir=%AndroidObjDir%\android\assets"
set "ClassesDex=%ExecutePath%classes3.dex"

:: Prebuild Resources only for github actions because local build will automatically prebuild resources
call "%ExecutePath%prebuild_resources.bat"

echo [1/6] Building UndertaleModToolAvalonia...
dotnet build "%ExecutePath%..\UndertaleModToolAvalonia\UndertaleModToolAvalonia.csproj" -c Debug "-p:Platform=Any CPU"
if %ERRORLEVEL% neq 0 (
    echo ERROR: Failed to build UndertaleModToolAvalonia
    exit /b 1
)
dotnet restore "%ExecutePath%.."
echo [2/6] Building Android project with fast deploy and collecting all assemblies...
:: Restore NuGet packages for 64-bit Android ABIs (.NET 9 Android only supports arm64-v8a
:: and x86_64; 32-bit runtimes linux-bionic-arm/linux-bionic-x86 do not exist on NuGet).
dotnet restore "%AndroidProjectDir%\UndertaleModToolAvalonia.Android.csproj"
if %ERRORLEVEL% neq 0 (
    echo ERROR: Failed to restore NuGet packages for Android ABIs
    exit /b 1
)

dotnet msbuild "%AndroidProjectDir%\UndertaleModToolAvalonia.Android.csproj" /t:SignAndroidPackage /p:Configuration=Debug /p:Platform="Any CPU" /p:AndroidUseSharedRuntime=true /p:EmbedAssembliesIntoApk=false
if %ERRORLEVEL% neq 0 (
    echo ERROR: Failed to build Android project
    exit /b 1
)

echo [3/6] Locating fast deploy files...
:: Different .NET Android SDK versions produce different directory structures:
:: - Older SDK: android\.__override__\<abi>\*.dll
:: - Newer SDK: android\assets\<abi>\*.dll
:: We need .__override__ as the root name in the zip for the Android runtime.
set "CreatedJunction=0"
if exist "%OverrideDir%" (
    echo Found .__override__ directory: %OverrideDir%
) else if exist "%AssetsDir%" (
    echo Found assets directory: %AssetsDir%
    :: Create a directory junction so the zip root is .__override__ (not assets)
    mklink /J "%OverrideDir%" "%AssetsDir%"
    if %ERRORLEVEL% neq 0 (
        echo ERROR: Failed to create junction from .__override__ to assets
        exit /b 1
    )
    set "CreatedJunction=1"
    echo Created junction: %OverrideDir% -> %AssetsDir%
) else (
    echo ERROR: Neither .__override__ nor assets directory found under %AndroidObjDir%\android
    exit /b 1
)

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

echo [4/6] Packaging fast deploy files into APK...
:: Create assets directory for patcher
mkdir "%ExecutePath%assets" 2>nul

:: Package the fast dev files (.__override__ directory, including ABI subdirs) into genouka_patcher.ext
"%ExecutePath%7z.exe" a -tzip "%ExecutePath%assets\genouka_patcher.ext" "%OverrideDir%"
if %ERRORLEVEL% neq 0 (
    echo ERROR: Failed to create genouka_patcher.ext
    goto :error_exit
)

:: Add assets into the signed APK
"%ExecutePath%7z.exe" a -tzip "%SignedApk%" "%ExecutePath%assets" -aoa
if %ERRORLEVEL% neq 0 (
    echo ERROR: Failed to add assets to APK
    goto :error_exit
)

:: Clean up temp assets
del /q /f "%ExecutePath%assets\genouka_patcher.ext" 2>nul
rmdir /s /q "%ExecutePath%assets"

echo [5/6] Compiling patcher and adding classes3.dex...
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

echo [6/6] Signing APK...
del /q /f "%OutputApk%" 2>nul
"%ExecutePath%signapk.exe" sign --ks "%ExecutePath%debug.keystore" --ks-key-alias "androiddebugkey" --ks-pass "pass:android" --key-pass "pass:android" --in "%SignedApk%" --out "%OutputApk%"
if %ERRORLEVEL% neq 0 (
    echo ERROR: Failed to sign APK
    goto :error_exit
)

echo Build completed successfully: %OutputApk%

:: Clean up junction if we created it
if "!CreatedJunction!"=="1" (
    rmdir "%OverrideDir%" 2>nul
    echo Removed junction: %OverrideDir%
)

endlocal
exit /b 0

:error_exit
:: Clean up junction on error
if "!CreatedJunction!"=="1" (
    rmdir "%OverrideDir%" 2>nul
)
endlocal
exit /b 1
