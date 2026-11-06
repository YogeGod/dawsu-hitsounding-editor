# Builds dawsu_vst3_host.exe with a portable MSVC toolchain (no Visual Studio install needed).
#   -Tools   a folder with msvc/VC/Tools/MSVC/<ver>, "msvc/Windows Kits/10" and python-tools (cmake, ninja)
#   -Sdk     a VST 3 SDK 3.8+ checkout (base, pluginterfaces, public.sdk, cmake submodules)
#   -Out     the build folder (keep it outside the repo or in the ignored artifacts/)
param(
    [Parameter(Mandatory)][string]$Tools,
    [Parameter(Mandatory)][string]$Sdk,
    [Parameter(Mandatory)][string]$Out,
    [string]$MsvcVersion = '14.44.35207',
    [string]$WinSdkVersion = '10.0.26100.0'
)
$ErrorActionPreference = 'Stop'
# CMake reads backslashes as escapes: use forward slashes throughout.
$Tools = $Tools.Replace('\', '/'); $Sdk = $Sdk.Replace('\', '/'); $Out = $Out.Replace('\', '/')
$msvc = Join-Path $Tools "msvc/VC/Tools/MSVC/$MsvcVersion"
$kits = Join-Path $Tools 'msvc/Windows Kits/10'
$env:PATH = "$msvc/bin/Hostx64/x64;$kits/bin/$WinSdkVersion/x64;$env:PATH"
$env:INCLUDE = "$msvc/include;$kits/Include/$WinSdkVersion/ucrt;$kits/Include/$WinSdkVersion/shared;$kits/Include/$WinSdkVersion/um;$kits/Include/$WinSdkVersion/winrt"
$env:LIB = "$msvc/lib/x64;$kits/Lib/$WinSdkVersion/ucrt/x64;$kits/Lib/$WinSdkVersion/um/x64"
$env:PYTHONPATH = Join-Path $Tools 'python-tools'
$cmake = Join-Path $Tools 'python-tools/cmake/data/bin/cmake.exe'
$ninja = Join-Path $Tools 'python-tools/bin/ninja.exe'
& $cmake -S $PSScriptRoot -B $Out -G Ninja "-DCMAKE_MAKE_PROGRAM=$ninja" -DCMAKE_C_COMPILER=cl -DCMAKE_CXX_COMPILER=cl "-DVST3_SDK_ROOT=$Sdk" -DCMAKE_BUILD_TYPE=Release
if ($LASTEXITCODE) { exit $LASTEXITCODE }
& $cmake --build $Out -j 6
exit $LASTEXITCODE
