@echo off
setlocal

rem ============================================================
rem Set your local Box3D source repo here
rem ============================================================
set "BOX3D_SRC=J:\Projects\Wheelman\Box3d\box3d-ccx-mod"
set "PYTHONUTF8=1"

rem Optional: uncomment this only if you want to override auto-detection
rem set "CLANG_BUILTIN_INCLUDE=D:\Programs\LLVM\lib\clang\12.0.0\include"

echo BOX3D_SRC=%BOX3D_SRC%
echo.

rem Call generate.bat in the same directory as this script
call "%~dp0generate.bat"

if errorlevel 1 (
    echo.
    echo ==============================
    echo ERROR: generation failed
    echo ==============================
    pause
    exit /b 1
)

echo.
echo ==============================
echo Generation completed successfully
echo ==============================
pause

endlocal
exit /b 0
