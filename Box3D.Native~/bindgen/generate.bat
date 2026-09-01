@echo off
setlocal EnableExtensions

rem Regenerates the raw Box3D bindings + public API forwarders (Windows).
rem
rem Environment (all optional):
rem   BOX3D_SRC              Box3D checkout; auto-probed next to the repo if unset
rem   CLANG_BUILTIN_INCLUDE  Directory providing stdbool.h etc.; auto-detected from clang if unset
rem
rem Requires:
rem   .NET SDK
rem   ClangSharpPInvokeGenerator global tool
rem   Python
rem   Clang/LLVM
rem
rem Run this script from any directory. It will switch to its own directory.

cd /d "%~dp0"

rem Make .NET global tools available if installed in the default location.
if exist "%USERPROFILE%\.dotnet\tools" (
    set "PATH=%USERPROFILE%\.dotnet\tools;%PATH%"
)

rem ------------------------------------------------------------
rem Locate Box3D checkout
rem ------------------------------------------------------------
if not defined BOX3D_SRC (
    if exist "%CD%\..\..\..\box3d\include\box3d\box3d.h" (
        set "BOX3D_SRC=%CD%\..\..\..\box3d"
    ) else if exist "%CD%\..\..\..\..\..\box3d\include\box3d\box3d.h" (
        set "BOX3D_SRC=%CD%\..\..\..\..\..\box3d"
    )
)

if not defined BOX3D_SRC (
    echo error: box3d checkout not found - set BOX3D_SRC
    exit /b 1
)

if not exist "%BOX3D_SRC%\include\box3d\box3d.h" (
    echo error: "%BOX3D_SRC%\include\box3d\box3d.h" not found
    exit /b 1
)

rem Normalize BOX3D_SRC to an absolute path.
for %%I in ("%BOX3D_SRC%") do set "BOX3D_SRC=%%~fI"

rem ------------------------------------------------------------
rem Locate Clang builtin headers
rem ------------------------------------------------------------
if not defined CLANG_BUILTIN_INCLUDE (
    where clang >nul 2>nul
    if errorlevel 1 (
        echo error: clang not found in PATH
        exit /b 1
    )

    for /f "usebackq delims=" %%I in (`clang -print-resource-dir`) do (
        set "CLANG_BUILTIN_INCLUDE=%%I\include"
    )
)

if not exist "%CLANG_BUILTIN_INCLUDE%\stdbool.h" (
    echo error: no stdbool.h found in "%CLANG_BUILTIN_INCLUDE%"
    echo        Set CLANG_BUILTIN_INCLUDE manually if needed.
    exit /b 1
)

rem ------------------------------------------------------------
rem Check required tools
rem ------------------------------------------------------------
where ClangSharpPInvokeGenerator >nul 2>nul
if errorlevel 1 (
    echo error: ClangSharpPInvokeGenerator not found in PATH
    exit /b 1
)

where python >nul 2>nul
if errorlevel 1 (
    echo error: python not found in PATH
    exit /b 1
)

rem ------------------------------------------------------------
rem Clean generated output
rem ------------------------------------------------------------
if exist "generated" rmdir /s /q "generated"
mkdir "generated"
if errorlevel 1 exit /b 1

rem ------------------------------------------------------------
rem Generate raw bindings
rem
rem ClangSharpPInvokeGenerator may return non-zero for benign warnings
rem such as function-like macros, so we intentionally do not abort here.
rem We validate the generated file afterwards instead.
rem ------------------------------------------------------------
ClangSharpPInvokeGenerator @box3d.rsp ^
    --file-directory "%BOX3D_SRC%\include" ^
    --include-directory "%BOX3D_SRC%\include" ^
    --include-directory "%CLANG_BUILTIN_INCLUDE%"

if not exist "generated\UnsafeBindings.g.cs" (
    echo error: generated\UnsafeBindings.g.cs was not produced
    exit /b 1
)

for %%A in ("generated\UnsafeBindings.g.cs") do (
    if %%~zA EQU 0 (
        echo error: generated\UnsafeBindings.g.cs is empty
        exit /b 1
    )
)

rem ------------------------------------------------------------
rem Post-process and generate public API forwarders
rem ------------------------------------------------------------
python postprocess.py generated\UnsafeBindings.g.cs
if errorlevel 1 exit /b 1

python generate_api.py
if errorlevel 1 exit /b 1

rem ------------------------------------------------------------
rem Deploy into the module
rem ------------------------------------------------------------
copy /Y "generated\UnsafeBindings.g.cs" "..\..\Box3D\Bindings\UnsafeBindings.g.cs" >nul
if errorlevel 1 exit /b 1

if exist "..\..\Box3D\Generated\*.g.cs" del /Q "..\..\Box3D\Generated\*.g.cs"

if not exist "..\..\Box3D\Generated" (
    mkdir "..\..\Box3D\Generated"
    if errorlevel 1 exit /b 1
)

copy /Y "generated\api\*.g.cs" "..\..\Box3D\Generated\" >nul
if errorlevel 1 exit /b 1

echo.
echo Generated successfully:
echo   generated\UnsafeBindings.g.cs
echo   generated\api\*.g.cs
echo.
echo BOX3D_SRC=%BOX3D_SRC%
echo CLANG_BUILTIN_INCLUDE=%CLANG_BUILTIN_INCLUDE%

endlocal
exit /b 0
