param([ValidateSet('win-arm64','win-x64')][string]$Rid='win-arm64')
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
Set-Location -LiteralPath $root
$vswhere='C:/Program Files (x86)/Microsoft Visual Studio/Installer/vswhere.exe'
$vs=& $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if(!$vs){throw 'Install Visual Studio Build Tools with C++ ARM64/x64 components first.'}
$msvc=Get-ChildItem "$vs/VC/Tools/MSVC" -Directory | Sort-Object Name -Descending | Select-Object -First 1
$hostArch=if($env:PROCESSOR_ARCHITECTURE -eq 'ARM64'){'Hostarm64'}else{'Hostx64'}
$tool=Join-Path $msvc.FullName "bin/$hostArch/x64"
if(!(Test-Path "$tool/dumpbin.exe")){$tool=Join-Path $msvc.FullName 'bin/Hostx64/x64'}
$cmake=(Get-ChildItem '.tools/cmake' -Filter cmake.exe -Recurse | Select-Object -First 1).FullName
if(!$cmake){$cmake=(Get-Command cmake -ErrorAction Stop).Source}
$source=Join-Path $root '.tools/whisper.cpp'
if(!(Test-Path "$source/CMakeLists.txt")){git clone --depth 1 --branch b5454 https://github.com/ggml-org/whisper.cpp.git $source; if($LASTEXITCODE){throw 'Source clone failed'}}
$sdk=Join-Path $root '.tools/VulkanSDK'
if(!(Test-Path "$sdk/Bin/glslc.exe")){throw 'Extract Bin/glslc.exe and its DLLs from LunarG Vulkan SDK into .tools/VulkanSDK.'}
$headers=(Get-ChildItem '.tools/vulkan-headers' -Directory | Select-Object -First 1).FullName
if(!$headers){throw 'Extract Vulkan-Headers vulkan-sdk-1.4.363.0 into .tools/vulkan-headers.'}
$spirvSource=Join-Path $root '.tools/SPIRV-Headers'
if(!(Test-Path "$spirvSource/CMakeLists.txt")){git clone --depth 1 --branch vulkan-sdk-1.4.363.0 https://github.com/KhronosGroup/SPIRV-Headers.git $spirvSource; if($LASTEXITCODE){throw 'SPIRV headers clone failed'}}
$prefix=Join-Path $root '.tools/spirv-install'
& $cmake -S $spirvSource -B '.tools/spirv-build' -DCMAKE_INSTALL_PREFIX="$prefix" -DSPIRV_HEADERS_SKIP_EXAMPLES=ON -DSPIRV_HEADERS_SKIP_INSTALL=OFF
if($LASTEXITCODE){throw 'SPIRV configure failed'}
& $cmake --install '.tools/spirv-build' --config Release
if($LASTEXITCODE){throw 'SPIRV install failed'}
$machine=if($Rid -eq 'win-arm64'){'ARM64'}else{'X64'}
$libDirectory=Join-Path $sdk "Lib/$Rid"; New-Item -ItemType Directory -Path $libDirectory -Force | Out-Null
$exports=& "$tool/dumpbin.exe" /nologo /exports "$env:WINDIR/System32/vulkan-1.dll"
$names=@($exports | ForEach-Object {if($_ -match '^\s*\d+\s+[0-9A-F]+\s+[0-9A-F]+\s+(vk\w+)'){$matches[1]}})
if($names.Count -lt 10){throw 'Could not read Vulkan loader exports'}
@('LIBRARY vulkan-1.dll','EXPORTS')+$names | Set-Content "$libDirectory/vulkan-1.def" -Encoding ascii
& "$tool/lib.exe" /nologo "/def:$libDirectory/vulkan-1.def" "/machine:$machine" "/out:$libDirectory/vulkan-1.lib"
if($LASTEXITCODE){throw 'Vulkan import library generation failed'}
$build=Join-Path $root ".tools/whisper-vulkan-clang-$Rid"
$target=if($Rid -eq 'win-arm64'){'arm64'}else{'x64'}
$vcvars=Join-Path $vs 'VC/Auxiliary/Build/vcvarsall.bat'
$envArchitecture=if($target -eq 'arm64'){'arm64'}else{'arm64_x64'}
if($env:PROCESSOR_ARCHITECTURE -ne 'ARM64'){$envArchitecture=if($target -eq 'arm64'){'x64_arm64'}else{'x64'}}
cmd /d /c "`"$vcvars`" $envArchitecture >nul && set" | ForEach-Object {if($_ -match '^([^=]+)=(.*)$'){[Environment]::SetEnvironmentVariable($matches[1],$matches[2],'Process')}}
$ninja=Join-Path $vs 'Common7/IDE/CommonExtensions/Microsoft/CMake/Ninja/ninja.exe'
$llvm=Join-Path $vs 'VC/Tools/Llvm/ARM64/bin'
if(!(Test-Path "$llvm/clang-cl.exe")){$llvm=Join-Path $vs 'VC/Tools/Llvm/x64/bin'}
$env:PATH="$llvm;$([IO.Path]::GetDirectoryName($ninja));$env:PATH"
$toolchain=Join-Path $source "cmake/$target-windows-llvm.cmake"
& $cmake -S $source -B $build -G 'Ninja Multi-Config' "-DCMAKE_TOOLCHAIN_FILE=$toolchain" `
    -DGGML_VULKAN=ON -DGGML_NATIVE=OFF -DGGML_OPENMP=OFF -DBUILD_SHARED_LIBS=ON `
    -DGGML_BACKEND_DL=ON -DWHISPER_SDL2=OFF -DWHISPER_BUILD_TESTS=OFF `
    "-DVulkan_INCLUDE_DIR=$headers/include" "-DVulkan_LIBRARY=$libDirectory/vulkan-1.lib" `
    "-DVulkan_GLSLC_EXECUTABLE=$sdk/Bin/glslc.exe" "-DCMAKE_PREFIX_PATH=$prefix"
if($LASTEXITCODE){throw 'Whisper Vulkan configure failed'}
& $cmake --build $build --config Release --parallel 8
if($LASTEXITCODE){throw 'Whisper Vulkan build failed'}
$destination=Join-Path $root "engines/$Rid/whisper-vulkan"
New-Item -ItemType Directory -Path $destination -Force | Out-Null
Copy-Item "$build/bin/Release/*" $destination -Recurse -Force
Copy-Item "$source/LICENSE" "$destination/LICENSE-whisper.txt"
Write-Host "Ready: $destination"
