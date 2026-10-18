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
