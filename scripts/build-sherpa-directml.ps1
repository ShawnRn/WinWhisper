param([ValidateSet('win-arm64','win-x64')][string]$Rid='win-arm64')
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
Set-Location -LiteralPath $root
$source=Join-Path $root '.tools/sherpa-onnx'
$revision='11afbd009a7f8c08f4bcf2fc1b265d0df4670fbf' # v1.13.8
if(!(Test-Path "$source/CMakeLists.txt")){git clone --depth 1 --branch v1.13.8 https://github.com/k2-fsa/sherpa-onnx.git $source; if($LASTEXITCODE){throw 'sherpa clone failed'}}
if((& git -C $source rev-parse HEAD) -ne $revision){throw 'Unexpected sherpa source revision'}
foreach($package in @(
    @{id='microsoft.ml.onnxruntime.directml';version='1.24.4';folder='onnxruntime-directml-1.24.4';sha='57e9f11b73437bef7a309496135d4c1f96b1a8e9ddba60013fa27bfc1d788681'},
    @{id='microsoft.ai.directml';version='1.15.4';folder='directml-1.15.4';sha='4e7cb7ddce8cf837a7a75dc029209b520ca0101470fcdf275c1f49736a3615b9'}
)){
    $archive=Join-Path $root ".tools/$($package.folder).nupkg"
    if(!(Test-Path -LiteralPath $archive)){Invoke-WebRequest "https://api.nuget.org/v3-flatcontainer/$($package.id)/$($package.version)/$($package.id).$($package.version).nupkg" -OutFile $archive}
    if((Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant() -ne $package.sha){throw 'NuGet archive hash mismatch'}
    if(!(Test-Path ".tools/$($package.folder)/LICENSE")){Expand-Archive -LiteralPath $archive -DestinationPath ".tools/$($package.folder)" -Force}
}
# Upstream's preinstalled DirectML branch assumes x64 and downloads an older DML.
# Supply the pinned ARM64/x64 NuGet import library instead.
$cmakeFile=Join-Path $source 'cmake/onnxruntime.cmake'
$original=(& git -C $source show 'HEAD:cmake/onnxruntime.cmake') -join "`n"
$needle='include(onnxruntime-win-x64-directml)'
$replacement='set(location_onnxruntime_lib2 "$ENV{SHERPA_ONNXRUNTIME_LIB_DIR}/onnxruntime.lib")' + "`n" + '          set(location_onnxruntime_lib "$ENV{SHERPA_ONNXRUNTIME_LIB_DIR}/onnxruntime.dll")'
$offset=$original.IndexOf('if(SHERPA_ONNX_USE_PRE_INSTALLED_ONNXRUNTIME_IF_AVAILABLE)')
if($offset -lt 0){throw 'Upstream CMake layout changed'}
$patched=$original.Substring(0,$offset)+$original.Substring($offset).Replace($needle,$replacement)
[IO.File]::WriteAllText($cmakeFile,$patched,[Text.UTF8Encoding]::new($false))
# ORT 1.24 names profile files with second resolution. Multiple model sessions
# created together otherwise overwrite each other's evidence. This changes
# profiling names only, not model computation or graph options.
$sessionFile=Join-Path $source 'sherpa-onnx/csrc/session.cc'
$session=(& git -C $source show 'HEAD:sherpa-onnx/csrc/session.cc') -join "`n"
$include='#include "sherpa-onnx/csrc/session.h"'
$anchor='if (config.find("ProfilingFilePrefix") != config.end()) {'
if(!$session.Contains($include) -or !$session.Contains($anchor)){throw 'Upstream profiling layout changed'}
$session=$session.Replace($include,$include+"`n#include <atomic>")
$session=$session.Replace($anchor,$anchor+"`n    static std::atomic<unsigned long long> profile_sequence{0};`n"+'    config["ProfilingFilePrefix"] += "-" + std::to_string(profile_sequence.fetch_add(1));')
[IO.File]::WriteAllText($sessionFile,$session,[Text.UTF8Encoding]::new($false))
$vswhere='C:/Program Files (x86)/Microsoft Visual Studio/Installer/vswhere.exe'
$vs=& $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if(!$vs){throw 'Install Visual Studio Build Tools with C++ ARM64/x64 components first'}
$platform=if($Rid -eq 'win-arm64'){'ARM64'}else{'x64'}
$vcTarget=if($env:PROCESSOR_ARCHITECTURE -eq 'ARM64'){if($Rid -eq 'win-arm64'){'arm64'}else{'arm64_x64'}}else{if($Rid -eq 'win-arm64'){'x64_arm64'}else{'x64'}}
$vcvars=Join-Path $vs 'VC/Auxiliary/Build/vcvarsall.bat'
cmd /d /c "`"$vcvars`" $vcTarget >nul && set" | ForEach-Object {if($_ -match '^([^=]+)=(.*)$'){[Environment]::SetEnvironmentVariable($matches[1],$matches[2],'Process')}}
$cmake=(Get-ChildItem '.tools/cmake' -Filter cmake.exe -Recurse | Select-Object -First 1).FullName
$ninja=Join-Path $vs 'Common7/IDE/CommonExtensions/Microsoft/CMake/Ninja/ninja.exe'
$env:PATH="$([IO.Path]::GetDirectoryName($ninja));$env:PATH"
$env:SHERPA_ONNXRUNTIME_INCLUDE_DIR=Join-Path $root '.tools/onnxruntime-directml-1.24.4/build/native/include'
$env:SHERPA_ONNXRUNTIME_LIB_DIR=Join-Path $root ".tools/onnxruntime-directml-1.24.4/runtimes/$Rid/native"
$build=Join-Path $root ".tools/sherpa-directml-$Rid"
& $cmake -S $source -B $build -G Ninja -DCMAKE_BUILD_TYPE=Release "-DCMAKE_VS_PLATFORM_NAME=$platform" '-DCMAKE_CXX_FLAGS=/DWIN32 /D_WINDOWS /GR /EHsc /utf-8' '-DCMAKE_C_FLAGS=/DWIN32 /D_WINDOWS /utf-8' -DBUILD_SHARED_LIBS=ON -DSHERPA_ONNX_ENABLE_DIRECTML=ON -DSHERPA_ONNX_ENABLE_BINARY=OFF -DSHERPA_ONNX_ENABLE_PORTAUDIO=OFF -DSHERPA_ONNX_ENABLE_WEBSOCKET=OFF -DSHERPA_ONNX_ENABLE_TESTS=OFF
if($LASTEXITCODE){throw 'DirectML configure failed'}
& $cmake --build $build --target sherpa-onnx-c-api --parallel 8
if($LASTEXITCODE){throw 'DirectML build failed'}
$destination=Join-Path $root "engines/$Rid/sherpa-directml"
New-Item -ItemType Directory -Path $destination -Force | Out-Null
Get-ChildItem "$build/bin" -Filter '*.dll' -Recurse | Copy-Item -Destination $destination -Force
Copy-Item "$env:SHERPA_ONNXRUNTIME_LIB_DIR/*.dll" $destination -Force
$dmlArch=if($Rid -eq 'win-arm64'){'arm64-win'}else{'x64-win'}
Copy-Item ".tools/directml-1.15.4/bin/$dmlArch/DirectML.dll" $destination -Force
Copy-Item "$source/LICENSE" "$destination/LICENSE-sherpa.txt" -Force
Copy-Item '.tools/onnxruntime-directml-1.24.4/LICENSE' "$destination/LICENSE-onnxruntime.txt" -Force
Copy-Item '.tools/onnxruntime-directml-1.24.4/ThirdPartyNotices.txt' $destination -Force
Copy-Item '.tools/directml-1.15.4/LICENSE*' $destination -Force
Write-Host "Ready: $destination"
