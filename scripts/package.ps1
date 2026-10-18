param(
    [ValidateSet('Universal','x64','arm64','All')][string]$Architecture='All',
    [string]$Version='0.1.0-preview.1',
    [string]$AppOutputRoot='artifacts/ui-v2',
    [string]$ExistingStage=''
)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
Set-Location -LiteralPath $root
if($Version -notmatch '^\d+\.\d+\.\d+(?:-[A-Za-z0-9.-]+)?$'){throw 'Invalid release version'}
$compiler=Join-Path $root '.tools/NSIS/nsis-3.13/makensis.exe'
if(!(Test-Path -LiteralPath $compiler)){throw 'Run scripts/bootstrap-packaging.ps1 to obtain the pinned portable NSIS compiler'}
$release=Join-Path $root "artifacts/release/$Version"
New-Item -ItemType Directory -Path $release -Force | Out-Null
# Staging is always new; reruns never mix files from an older package.
$stage=if($ExistingStage){(Resolve-Path -LiteralPath $ExistingStage).Path}else{Join-Path $release ('stage-'+[guid]::NewGuid().ToString('N'))}
if(!$stage.StartsWith($release+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw 'Stage must be inside this release directory'}
Get-Process | ForEach-Object {
    try {$path=$_.MainModule.FileName}catch{$path=$null}
    if($path -and $path.StartsWith($stage+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw 'A process is running from the package stage; close it before packaging'}
}
function Copy-Tree([string]$Source,[string]$Destination){
    if(!(Test-Path -LiteralPath $Source -PathType Container)){throw "Missing payload directory: $Source"}
    foreach($file in Get-ChildItem -LiteralPath $Source -File -Recurse){
        if($file.Attributes -band [IO.FileAttributes]::ReparsePoint){throw "Refuse reparse-point payload: $($file.FullName)"}
        if($file.Extension -in '.pdb','.log','.db','.pfx','.key'){continue}
        if($file.Extension -in '.onnx','.gguf' -or $file.Name -like 'ggml-*.bin'){throw 'Model weights must not be bundled in an installer'}
        $relative=[IO.Path]::GetRelativePath($Source,$file.FullName)
        $target=Join-Path $Destination $relative
        New-Item -ItemType Directory -Path (Split-Path $target) -Force | Out-Null
        Copy-Item -LiteralPath $file.FullName -Destination $target -Force
    }
}
function Assert-Architecture([string]$Directory,[string]$Rid){
    $expected=if($Rid -eq 'win-arm64'){0xaa64}else{0x8664}
    foreach($file in Get-ChildItem -LiteralPath $Directory -File -Recurse | Where-Object Extension -In '.exe','.dll'){
        $relative=[IO.Path]::GetRelativePath($Directory,$file.FullName)
        $machineExpected=if($relative.StartsWith('worker-x64'+[IO.Path]::DirectorySeparatorChar)){0x8664}else{$expected}
        $stream=[IO.File]::OpenRead($file.FullName)
        try{
            $pe=[Reflection.PortableExecutable.PEReader]::new($stream)
            try{
                $header=$pe.PEHeaders
                $anyCpu=$null -ne $header.CorHeader -and ([int]$header.CorHeader.Flags -band 1) -ne 0 -and ([int]$header.CorHeader.Flags -band 2) -eq 0 -and [int]$header.CoffHeader.Machine -eq 0x14c
                # The pinned Windows App SDK AI x64 payload intentionally contains
                # this ARM64EC companion. Accept only its exact upstream bytes.
                $sdkCompanion=$Rid -eq 'win-x64' -and $relative -eq 'Microsoft.Windows.Workloads.Resources_ec.dll' -and
                    (Get-FileHash -LiteralPath $file.FullName).Hash -eq '639F7DD3130D22C0929E5A86FB722B754BE6457CBF65593303E257178D5AEBE7'
                if(!$anyCpu -and !$sdkCompanion -and [int]$header.CoffHeader.Machine -ne $machineExpected){throw "Wrong PE architecture: $relative [$($header.CoffHeader.Machine)]"}
            }finally{$pe.Dispose()}
        }finally{$stream.Dispose()}
    }
}
if(!$ExistingStage){
    foreach($rid in 'win-arm64','win-x64'){
        $platform=if($rid -eq 'win-arm64'){'ARM64'}else{'x64'}
        $app=Join-Path $root "$AppOutputRoot/$platform/Release/net10.0-windows10.0.26100.0/$rid"
        $destination=Join-Path $stage $rid
        if(!(Test-Path "$app/WinWhisper.exe")){throw "Build the $rid application first"}
        if(!(Get-Item "$app/WinWhisper.exe").VersionInfo.ProductVersion.StartsWith($Version)){throw "Application version does not match $Version"}
        Copy-Tree $app $destination
        Copy-Tree (Join-Path $root "artifacts/$rid/worker") (Join-Path $destination 'worker')
        if($rid -eq 'win-arm64'){Copy-Tree (Join-Path $root 'artifacts/win-x64/worker') (Join-Path $destination 'worker-x64')}
        foreach($engine in 'whisper-cpu','whisper-cuda','whisper-vulkan','ffmpeg','deno','yt-dlp','llama-cpu','llama-cuda','llama-cuda-runtime','llama-vulkan','sherpa-directml'){
            Copy-Tree (Join-Path $root "engines/$rid/$engine") (Join-Path $destination "engines/$rid/$engine")
        }
        foreach($name in 'README.md','CREDITS.md','LICENSE'){Copy-Item -LiteralPath (Join-Path $root $name) -Destination $destination -Force}
        Copy-Tree (Join-Path $root 'docs') (Join-Path $destination 'docs')
        Copy-Tree (Join-Path $root 'distribution/licenses') (Join-Path $destination 'licenses')
        # NuGet's package notices are not consistently copied to build output.
        # Keep the original files for the exact restored package versions.
        $locks=@('src/MemoAI.App/packages.lock.json','src/MemoAI.Core/packages.lock.json')
        foreach($lock in $locks){
            $dependencies=(Get-Content -LiteralPath $lock -Raw | ConvertFrom-Json).dependencies
            foreach($framework in $dependencies.PSObject.Properties){
                foreach($package in $framework.Value.PSObject.Properties){
                    $packageVersion=$package.Value.resolved
                    if(!$packageVersion){continue}
                    $packageRoot=Join-Path $env:USERPROFILE ".nuget/packages/$($package.Name.ToLowerInvariant())/$packageVersion"
                    if(!(Test-Path -LiteralPath $packageRoot)){continue}
                    foreach($notice in Get-ChildItem -LiteralPath $packageRoot -File -Recurse | Where-Object Name -Match '^(LICENSE|LICENCE|COPYING|NOTICE|THIRD.PARTY)'){
                        $relative=[IO.Path]::GetRelativePath($packageRoot,$notice.FullName)
                        $noticeTarget=Join-Path $destination "licenses/nuget/$($package.Name)/$packageVersion/$relative"
                        New-Item -ItemType Directory -Path (Split-Path $noticeTarget) -Force | Out-Null
                        Copy-Item -LiteralPath $notice.FullName -Destination $noticeTarget -Force
                    }
                }
            }
        }
        # Preserve notices for static dependencies of the custom sherpa build.
        $deps=Join-Path $root ".tools/sherpa-directml-$rid/_deps"
        foreach($dependency in Get-ChildItem -LiteralPath $deps -Directory -Filter '*-src'){
            foreach($notice in Get-ChildItem -LiteralPath $dependency.FullName -File | Where-Object Name -Match '^(LICENSE|LICENCE|COPYING|NOTICE|AUTHORS)'){
                $target=Join-Path $destination "engines/$rid/sherpa-directml/licenses/$($dependency.Name)/$($notice.Name)"
                New-Item -ItemType Directory -Path (Split-Path $target) -Force | Out-Null
                Copy-Item -LiteralPath $notice.FullName -Destination $target -Force
            }
        }
        Assert-Architecture $destination $rid
    }
}
# Use app-local VC runtime files; per-user installation must not require a
# machine-wide redistributable installer. Pin both versions and file hashes.
$crtLock=Get-Content (Join-Path $root 'docs/msvc-runtime-lock.json') -Raw | ConvertFrom-Json
$vs=& 'C:/Program Files (x86)/Microsoft Visual Studio/Installer/vswhere.exe' -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
foreach($rid in 'win-arm64','win-x64'){
    $payload=Join-Path $stage $rid
    $destinations=@($payload,(Join-Path $payload 'worker'))
    $destinations+=@(Get-ChildItem -LiteralPath (Join-Path $payload 'engines') -File -Recurse -Filter '*.exe' | ForEach-Object DirectoryName | Sort-Object -Unique)
    foreach($directory in $destinations){
        foreach($entry in $crtLock.files | Where-Object rid -EQ $rid){
            $source=Join-Path $vs "VC/Redist/MSVC/$($crtLock.version)/$($entry.path)"
            if((Get-FileHash -LiteralPath $source).Hash.ToLowerInvariant() -ne $entry.sha256){throw 'VC runtime hash mismatch'}
            Copy-Item -LiteralPath $source -Destination $directory -Force
        }
    }
    if($rid -eq 'win-arm64'){
        foreach($entry in $crtLock.files | Where-Object rid -EQ 'win-x64'){
            Copy-Item -LiteralPath (Join-Path $vs "VC/Redist/MSVC/$($crtLock.version)/$($entry.path)") -Destination (Join-Path $payload 'worker-x64') -Force
        }
    }
    Assert-Architecture $payload $rid
}
$variants=if($Architecture -eq 'All'){@('Universal','x64','arm64')}else{@($Architecture)}
foreach($variant in $variants){
    $rids=if($variant -eq 'Universal'){@('win-arm64','win-x64')}elseif($variant -eq 'arm64'){@('win-arm64')}else{@('win-x64')}
    $files=@{}; $directories=@{}
    foreach($rid in $rids){
        $payload=Join-Path $stage $rid
        foreach($file in Get-ChildItem -LiteralPath $payload -File -Recurse){$files[[IO.Path]::GetRelativePath($payload,$file.FullName)]=$true}
        foreach($dir in Get-ChildItem -LiteralPath $payload -Directory -Recurse){$directories[[IO.Path]::GetRelativePath($payload,$dir.FullName)]=$true}
    }
    $lines=@('; Generated from package-owned files. No recursive deletion.')
    $lines+=@($files.Keys | Sort-Object | ForEach-Object {'Delete "$INSTDIR\'+$_.Replace('$','$$')+'"'})
    $lines+=@($directories.Keys | Sort-Object Length -Descending | ForEach-Object {'RMDir "$INSTDIR\'+$_.Replace('$','$$')+'"'})
    [IO.File]::WriteAllLines((Join-Path $stage "uninstall-$variant.nsh"),$lines,[Text.UTF8Encoding]::new($false))
    $output=Join-Path $release "WinWhisper-$Version-$variant-Setup.exe"
    & $compiler /V3 /INPUTCHARSET UTF8 "/DVERSION=$Version" "/DARCH=$variant" "/DROOT=$root" "/DSTAGE=$stage" "/DOUTPUT=$output" (Join-Path $root 'installer/WinWhisper.nsi')
    if($LASTEXITCODE){throw "Installer compilation failed: $variant"}
    Write-Host "Installer: $output"
}
$checksums=Get-ChildItem -LiteralPath $release -Filter 'WinWhisper-*-Setup.exe' -File | Sort-Object Name | ForEach-Object {"$((Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant())  $($_.Name)"}
[IO.File]::WriteAllLines((Join-Path $release 'SHA256SUMS.txt'),$checksums,[Text.UTF8Encoding]::new($false))
@{version=$Version;stage=$stage;sourceCommit=(& git rev-parse HEAD);builtAt=[DateTime]::UtcNow.ToString('o')} | ConvertTo-Json | Set-Content (Join-Path $release 'package-build.json') -Encoding utf8
Write-Host "Stage: $stage"
