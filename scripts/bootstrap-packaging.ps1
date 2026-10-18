$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
Set-Location -LiteralPath $root
$archive=Join-Path $root '.tools/nsis-3.13-portable.zip'
$hash='ba63dffc4410ee89193e1cb5a41989991bd77c61068da17e3156d136b7b0b3d8'
if(!(Test-Path -LiteralPath $archive)){
    $source='https://downloads.sourceforge.net/project/nsis/NSIS%203/3.13/nsis-3.13.zip'
    Invoke-WebRequest $source -OutFile $archive
    # SourceForge sometimes returns its normal download landing page first.
    $prefix=[IO.File]::ReadAllBytes($archive)
    if($prefix[0] -ne 0x50 -or $prefix[1] -ne 0x4b){
        $html=Get-Content -LiteralPath $archive -Raw
        $link=[regex]::Match($html,'https://downloads\.sourceforge\.net/project/nsis/NSIS%203/3\.13/nsis-3\.13\.zip\?[^"<> ]+')
        if(!$link.Success){throw 'No official SourceForge download link returned'}
        Invoke-WebRequest ([Net.WebUtility]::HtmlDecode($link.Value)) -OutFile $archive
    }
}
if((Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant() -ne $hash){throw 'NSIS archive SHA-256 mismatch'}
if(!(Test-Path '.tools/NSIS/nsis-3.13/makensis.exe')){Expand-Archive -LiteralPath $archive -DestinationPath '.tools/NSIS' -Force}
& '.tools/NSIS/nsis-3.13/makensis.exe' /VERSION
if($LASTEXITCODE){throw 'NSIS compiler could not run'}
$seven=Join-Path $root '.tools/7zip-26.04'
New-Item -ItemType Directory -Path $seven -Force | Out-Null
$assets=@(
    @{file='7zr.exe';sha='256feca8e274e5da655e2a284fabafd9f554365eb164862089dacd4e8276d282'},
    @{file='7z2604-extra.7z';sha='dc4b11d3399db18b063630137145f5585d8f7ac847bf3639bd1185d7d1f7cee0'},
    @{file='7z2604-src.tar.xz';sha='9691944c0fe0d01bb49373a704fb983fd33bc98b1738695179dfbf99ac1734f6'}
)
if([Runtime.InteropServices.RuntimeInformation]::OSArchitecture -eq 'Arm64'){$assets+=@{file='7z2604-arm64.exe';sha='d4117b95495b0e925334ae7b5bddf2853513274603de15dee6acf03999fbc09c'}}
foreach($asset in $assets){
    $out=Join-Path $seven $asset.file
    if(!(Test-Path -LiteralPath $out)){Invoke-WebRequest "https://github.com/ip7z/7zip/releases/download/26.04/$($asset.file)" -OutFile $out}
    if((Get-FileHash -LiteralPath $out).Hash.ToLowerInvariant() -ne $asset.sha){throw '7-Zip asset hash mismatch'}
}
if(!(Test-Path "$seven/extra/x64/7za.exe")){
    & "$seven/7zr.exe" x "$seven/7z2604-extra.7z" "-o$seven/extra" -y -bsp0 -bso0
    if($LASTEXITCODE){throw 'Portable x64 encoder extraction failed'}
}
if([Runtime.InteropServices.RuntimeInformation]::OSArchitecture -eq 'Arm64' -and !(Test-Path "$seven/arm64/7z.exe")){
    # Extract the official SFX payload only; do not install it or change the system.
    & "$seven/7zr.exe" x "$seven/7z2604-arm64.exe" "-o$seven/arm64" -y -bsp0 -bso0
    if($LASTEXITCODE){throw 'Portable ARM64 encoder extraction failed'}
}
