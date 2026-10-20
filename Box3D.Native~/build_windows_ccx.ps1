param([string]$PackageNativeDir = $PSScriptRoot)

$ErrorActionPreference = 'Stop'

function Invoke-CMake([string[]]$Arguments) {
    & $script:CMakeExe @Arguments
    if ($LASTEXITCODE -ne 0) { throw "CMake failed with exit code $LASTEXITCODE." }
}

try {
    # A caller can pin an installation. Otherwise include all products (Build Tools too).
    $vswhere = $env:VSWHERE
    if (-not $vswhere) {
        $command = Get-Command vswhere.exe -ErrorAction SilentlyContinue
        if ($command) { $vswhere = $command.Source }
    }
    if (-not $vswhere) {
        $candidate = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
        if (Test-Path -LiteralPath $candidate) { $vswhere = $candidate }
    }
    $instances = @()
    if ($vswhere) {
        $output = & $vswhere -all -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -format json -utf8
        if ($LASTEXITCODE -ne 0) { throw 'vswhere failed to enumerate C++ installations.' }
        $instances = @((($output -join "`n") | ConvertFrom-Json) | Sort-Object { [version]$_.installationVersion } -Descending)
    }

    $source = $env:BOX3D_SRC
    if (-not $source) {
        foreach ($candidate in @(
            'J:\Projects\Wheelman\Box3d\box3d-ccx-mod',
            (Join-Path $PackageNativeDir '..\..\box3d'),
            (Join-Path $PackageNativeDir '..\..\..\..\box3d')
        )) {
            if (Test-Path -LiteralPath (Join-Path $candidate 'include\box3d\box3d.h')) {
                $source = $candidate
                break
            }
        }
    }
    if (-not $source -or -not (Test-Path -LiteralPath (Join-Path $source 'CMakeLists.txt'))) {
        throw 'Box3D checkout not found. Set BOX3D_SRC to the checkout directory.'
    }
    $source = (Resolve-Path -LiteralPath $source).Path

    # Prefer the newest available CMake, including copies bundled with Visual Studio.
    $candidates = @()
    if ($env:CMAKE) {
        $candidates = @($env:CMAKE)
    } else {
        $command = Get-Command cmake.exe -ErrorAction SilentlyContinue
        if ($command) { $candidates += $command.Source }
        foreach ($instance in $instances) {
            $candidate = Join-Path $instance.installationPath 'Common7\IDE\CommonExtensions\Microsoft\CMake\CMake\bin\cmake.exe'
            if (Test-Path -LiteralPath $candidate) { $candidates += $candidate }
        }
    }
    $available = @(foreach ($candidate in ($candidates | Select-Object -Unique)) {
        $capabilities = & $candidate -E capabilities
        if ($LASTEXITCODE -eq 0) {
            $info = ($capabilities -join "`n") | ConvertFrom-Json
            [pscustomobject]@{ Path = $candidate; Info = $info; Version = [version]("{0}.{1}.{2}" -f $info.version.major, $info.version.minor, $info.version.patch) }
        }
    })
    $chosen = $available | Sort-Object Version -Descending | Select-Object -First 1
    if (-not $chosen) { throw 'CMake not found. Install CMake or set CMAKE to cmake.exe.' }
    $script:CMakeExe = $chosen.Path

    $generator = $env:GENERATOR
    if (-not $generator) { $generator = $env:CMAKE_GENERATOR }
    if ($generator -and $generator -ne 'Ninja' -and $generator -notlike 'Visual Studio *') {
        throw 'Supported GENERATOR values are Ninja or a Visual Studio generator.'
    }
    $instancePath = $env:CMAKE_GENERATOR_INSTANCE
    if ($generator -ne 'Ninja') {
        foreach ($instance in $instances) {
            if ($instancePath -and $instance.installationPath.TrimEnd('\', '/') -ne $instancePath.TrimEnd('\', '/')) { continue }
            $major = ([version]$instance.installationVersion).Major
            # The C17 compiler used by this project requires VS 2019 16.8 or newer.
            if ([version]$instance.installationVersion -lt [version]'16.8') { continue }
            $supported = @($chosen.Info.generators | Where-Object { $_.name -like "Visual Studio $major *" })
            if ($supported.Count -eq 0) { continue }
            if ($generator -and $generator -ne $supported[0].name) { continue }
            $generator = $supported[0].name
            $instancePath = $instance.installationPath
            break
        }
        if (-not $generator) {
            throw 'No compatible Visual Studio C++ installation found. Install VS C++ tools and vswhere, or set GENERATOR explicitly. VS 2026 requires CMake 4.2+.'
        }
    }
    if ($chosen.Info.generators.name -notcontains $generator) {
        throw "CMake $($chosen.Version) does not support generator '$generator'. Update CMake or set CMAKE to a compatible executable."
    }

    $doublePrecision = [bool]$env:BOX3D_DOUBLE
    $precision = if ($doublePrecision) { 'double' } else { 'single' }
    # Never reuse the old build/ cache pinned to a removed VS installation. Include the
    # generator, instance, source and CMake in the key, so toolchain changes get fresh caches.
    $key = "$generator|$instancePath|$source|$script:CMakeExe|$($chosen.Version)|$precision"
    $sha = [System.Security.Cryptography.SHA256]::Create()
    try { $hash = ([BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($key)))).Replace('-', '').Substring(0, 12).ToLowerInvariant() }
    finally { $sha.Dispose() }
    $buildDir = Join-Path $PackageNativeDir "build-auto-$precision-$hash"
    $outDir = Join-Path $PackageNativeDir '..\Plugins\Windows\x86_64'
    New-Item -ItemType Directory -Path $outDir -Force | Out-Null

    Write-Host "CMake: $script:CMakeExe ($($chosen.Version))"
    Write-Host "Generator: $generator"
    if ($instancePath) { Write-Host "VS instance: $instancePath" }
    Write-Host "Build directory: $buildDir"
    $configure = @('-S', $source, '-B', $buildDir, '-G', $generator,
        '-DBUILD_SHARED_LIBS=ON', '-DBOX3D_SAMPLES=OFF', '-DBOX3D_UNIT_TESTS=OFF', '-DBOX3D_BENCHMARKS=OFF',
        "-DBOX3D_DOUBLE_PRECISION=$($doublePrecision.ToString().ToUpperInvariant())")
    if ($generator -eq 'Ninja') {
        $configure += '-DCMAKE_BUILD_TYPE=Release'
    } else {
        $configure += @('-A', 'x64')
        if ($instancePath) { $configure += "-DCMAKE_GENERATOR_INSTANCE=$instancePath" }
    }
    Invoke-CMake -Arguments $configure
    Invoke-CMake -Arguments @('--build', $buildDir, '--config', 'Release')

    $dll = Join-Path $buildDir 'bin\Release\box3d.dll'
    if (-not (Test-Path -LiteralPath $dll)) { $dll = Join-Path $buildDir 'bin\box3d.dll' }
    $filename = if ($doublePrecision) { 'box3d_d.dll' } else { 'box3d.dll' }
    $destination = Join-Path $outDir $filename
    Copy-Item -LiteralPath $dll -Destination $destination -Force
    Write-Host "Done: $destination"
    exit 0
} catch {
    Write-Host "error: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}
