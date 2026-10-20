# Steps After Modifying Box3D

## 1. Determine What Changed

- If only `.c` implementation files changed, rebuild the native DLL.
- If public headers under `include/box3d/` changed, regenerate the C# bindings first.

## 2. Regenerate Bindings When Required

Run:

```text
Box3D.Native~\bindgen\run_generate.bat
```

Types excluded in `bindgen/box3d.rsp` are maintained manually. If an excluded native struct changes, update its C# mirror under `Box3D/Types/`, preserving field order, types, size, and alignment. Test files in `Box3D/Box3D.Tests` might need modifications accordingly. 

## 3. Rebuild the Windows DLL

Close Unity, or ensure it is not using `box3d.dll`, then run:

```text
Box3D.Native~\build_windows_ccx.bat
```

The script builds the x64 Release DLL and copies it to the Unity plugin directory.

## 4. Verify in Unity

Wait for Unity to recompile scripts, then run a minimal physics scene. Check that the Console contains no:

- `DllNotFoundException`
- `EntryPointNotFoundException`
- `[Box3D] unstable`
- NaN or `IsFinite` errors

In short:

```text
Implementation only: rebuild DLL
Public API change: regenerate bindings -> check manual ABI types -> rebuild DLL -> test in Unity
```

## Windows Build Notes

- Requires Windows PowerShell 5.1+, VS C++ tools (VS 2019 16.8+) and a Windows SDK. VS 2026 requires CMake 4.2+.
- Keep `build_windows_ccx.ps1` beside the bat. The script automatically selects VS and CMake.
- If detection fails, set `BOX3D_SRC`, `CMAKE` or `VSWHERE` to the corresponding directory or executable. Use `GENERATOR` and `CMAKE_GENERATOR_INSTANCE` to select a specific toolchain.
- For Ninja, set `GENERATOR=Ninja` and run from an x64 MSVC developer command prompt with Ninja installed.
- Set `BOX3D_DOUBLE` to any non-empty value to build `box3d_d.dll`; enable `BOX3D_DOUBLE` in C# as well. Leave it unset for single precision.
- Builds use `build-auto-*` directories; the old `build/` and `build-double/` caches are not used.
