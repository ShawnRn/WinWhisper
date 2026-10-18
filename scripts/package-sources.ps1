param([string]$Version='0.1.0-preview.2')
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
Set-Location -LiteralPath $root
if($Version -notmatch '^\d+\.\d+\.\d+(?:-[A-Za-z0-9.-]+)?$'){throw 'Invalid version'}
$release=Join-Path $root "artifacts/release/$Version"
$sourceRoot=Join-Path $release 'third-party-sources'
New-Item -ItemType Directory -Path $sourceRoot -Force | Out-Null
$downloads=@(
    @{file='ffmpeg-arm64-e9b3c55.tar.gz';url='https://github.com/FFmpeg/FFmpeg/archive/e9b3c55fe0321915a70f44f30b276ef2afdc3a0c.tar.gz';sha='d518b185fcd3f214a6cf28d50b0f94cf9bcd572136dbe2280e44a259268085bd'},
    @{file='ffmpeg-x64-94cba31.tar.gz';url='https://github.com/FFmpeg/FFmpeg/archive/94cba3150c6b83057ed48b833689adc806715cc0.tar.gz';sha='8d4d5864279641a4a50ad9fe572fef5f3e96d735f575f49f7125a16273de7fe7'},
    @{file='ffmpeg-arm64-recipes-a6db219.tar.gz';url='https://github.com/tordona/ffmpeg-win-arm64/archive/a6db219ea76a2d9461b7f79706e575e22ccf0d6c.tar.gz';sha='10aeae4cc9677e19dd0e6f89d0cd07f988606c7df19955bda95851255c2613e4'},
    @{file='ffmpeg-x64-recipes-63ac326.tar.gz';url='https://github.com/BtbN/FFmpeg-Builds/archive/63ac3265b0789ea16c7b1f8351cd2ad195a28722.tar.gz';sha='4fd8856d5b207fce5c870a690aefc9a72b9a1943cfc79df8d5d327ec4612c9d2'}
    @{file='7z2604-src.tar.xz';url='https://github.com/ip7z/7zip/releases/download/26.04/7z2604-src.tar.xz';sha='9691944c0fe0d01bb49373a704fb983fd33bc98b1738695179dfbf99ac1734f6'}
)
foreach($item in $downloads){
    $out=Join-Path $sourceRoot $item.file
    if(!(Test-Path -LiteralPath $out)){Invoke-WebRequest $item.url -OutFile $out}
    if((Get-FileHash -LiteralPath $out).Hash.ToLowerInvariant() -ne $item.sha){throw "Source hash mismatch: $($item.file)"}
}
$sherpa=Join-Path $sourceRoot 'sherpa-onnx-1.13.8-and-static-dependencies.tar.gz'
if(!(Test-Path -LiteralPath $sherpa)){
    $revision=(& git -C '.tools/sherpa-onnx' rev-parse HEAD)
    if($revision -ne '11afbd009a7f8c08f4bcf2fc1b265d0df4670fbf'){throw 'Unexpected sherpa source revision'}
    $deps=(Resolve-Path '.tools/sherpa-directml-win-arm64/_deps').Path
    $names=@(Get-ChildItem -LiteralPath $deps -Directory -Filter '*-src' | Select-Object -ExpandProperty Name)
    & tar -czf $sherpa --exclude=.git -C (Resolve-Path '.tools').Path sherpa-onnx -C $deps @names
    if($LASTEXITCODE){throw 'sherpa source archive failed'}
}
Copy-Item -LiteralPath 'scripts/build-sherpa-directml.ps1' -Destination $sourceRoot -Force
Copy-Item -LiteralPath 'docs/THIRD-PARTY-SOURCES.md' -Destination "$sourceRoot/README.md" -Force
$downloads | ConvertTo-Json | Set-Content "$sourceRoot/upstream-downloads.json" -Encoding utf8
Get-ChildItem -LiteralPath $sourceRoot -File | Where-Object Name -Ne 'SHA256SUMS.txt' | Sort-Object Name | ForEach-Object {
    "$((Get-FileHash -LiteralPath $_.FullName).Hash.ToLowerInvariant())  $($_.Name)"
} | Set-Content "$sourceRoot/SHA256SUMS.txt" -Encoding utf8
$archive=Join-Path $release "WinWhisper-$Version-ThirdPartySources.tar.gz"
& tar -czf $archive -C $release third-party-sources
if($LASTEXITCODE){throw 'Combined source archive failed'}
Write-Host "Source archive: $archive"
