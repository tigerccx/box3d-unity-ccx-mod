@echo off
setlocal
rem Auto-detect installed VS C++ tools and a compatible CMake. Explicit overrides:
rem BOX3D_SRC, CMAKE, GENERATOR, CMAKE_GENERATOR_INSTANCE, VSWHERE, BOX3D_DOUBLE.
rem Ninja requires an x64 MSVC developer command prompt.
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0build_windows_ccx.ps1" -PackageNativeDir "%~dp0."
exit /b %errorlevel%
