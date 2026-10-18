param([switch]$Extended, [ValidateSet('win-arm64','win-x64')][string]$Rid = 'win-arm64')
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Set-Location -LiteralPath $root
New-Item -ItemType Directory -Path 'artifacts/downloads','engines' -Force | Out-Null
$specs = @(
    @{id='whisper-cuda'; repo='ggml-org/whisper.cpp'; tag='b5454'; asset='whisper-bin-win-cuda-13.4-arm64.zip'},
    @{id='whisper-cpu'; repo='ggml-org/whisper.cpp'; tag='b5454'; asset='whisper-bin-win-cpu-arm64.zip'},
    @{id='ffmpeg'; repo='tordona/ffmpeg-win-arm64'; tag='latest-autobuild-2026.10.17.0'; asset='ffmpeg-master-latest-full-shared-win-arm64.7z'},
    @{id='yt-dlp'; repo='yt-dlp/yt-dlp'; tag='2026.08.19'; asset='yt-dlp_win_arm64.zip'}
)
if ($Rid -eq 'win-x64') {
    $specs = @(
        @{id='whisper-cuda'; repo='ggml-org/whisper.cpp'; tag='b5454'; asset='whisper-bin-win-cuda-12.4.0-x64.zip'},
        @{id='whisper-cpu'; repo='ggml-org/whisper.cpp'; tag='b5454'; asset='whisper-bin-x64.zip'},
        @{id='ffmpeg'; repo='BtbN/FFmpeg-Builds'; tag='autobuild-2026-10-17-20-51'; asset='ffmpeg-N-127617-g94cba3150c-win64-gpl-shared.zip'},
        @{id='yt-dlp'; repo='yt-dlp/yt-dlp'; tag='2026.08.19'; asset='yt-dlp_win.zip'}
    )
}
if ($Extended) {
    $specs += @{id='llama-cuda'; repo='ggml-org/llama.cpp'; tag='b11716'; asset='llama-b11716-bin-win-cuda-13.4-arm64.zip'}
    $specs += @{id='llama-cuda-runtime'; repo='ggml-org/llama.cpp'; tag='b11716'; asset='cudart-llama-bin-win-cuda-13.4-arm64.zip'}
    $specs += @{id='llama-vulkan'; repo='ggml-org/llama.cpp'; tag='b11716'; asset='llama-b11716-bin-win-vulkan-arm64.zip'}
    $specs += @{id='llama-cpu'; repo='ggml-org/llama.cpp'; tag='b11716'; asset='llama-b11716-bin-win-cpu-arm64.zip'}
    $specs += @{id='sherpa'; repo='k2-fsa/sherpa-onnx'; tag='v1.13.8'; asset='sherpa-onnx-v1.13.8-win-arm64-shared-MD-Release.tar.bz2'}
}
$denoAsset=if($Rid -eq 'win-arm64'){'deno-aarch64-pc-windows-msvc.zip'}else{'deno-x86_64-pc-windows-msvc.zip'}
$specs += @{id='deno'; repo='denoland/deno'; tag='v2.9.7'; asset=$denoAsset}
if ($Extended -and $Rid -eq 'win-x64') {
    foreach ($spec in $specs) { if ($spec.repo -eq 'ggml-org/llama.cpp') { $spec.asset = $spec.asset -replace 'arm64','x64' }; if($spec.id -eq 'sherpa') {$spec.asset = $spec.asset -replace 'win-arm64','win-x64'} }
}
$records = @()
$locked = @()
if (Test-Path 'docs/engine-lock.json') { $locked = @(Get-Content 'docs/engine-lock.json' -Raw | ConvertFrom-Json) }
foreach ($spec in $specs) {
    $source = "https://github.com/$($spec.repo)/releases/download/$($spec.tag)/$($spec.asset)"
    $prior = $locked | Where-Object {$_.id -eq "$Rid/$($spec.id)" -and $_.source -eq $source} | Select-Object -First 1
    $asset = [pscustomobject]@{browser_download_url=$source;digest=if($prior){"sha256:$($prior.sha256)"}else{$null}}
    $download = Join-Path $root "artifacts/downloads/$($spec.asset)"
    if (!(Test-Path -LiteralPath $download)) { Invoke-WebRequest $asset.browser_download_url -OutFile $download }
    $hash = (Get-FileHash -LiteralPath $download -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($asset.digest -and $asset.digest -ne "sha256:$hash") { throw "SHA-256 mismatch: $download" }
    if ($spec.asset.EndsWith('.7z')) {
        $checksumName = $spec.asset -replace '\.7z$', '.sha256'
        $sumContent = (Invoke-WebRequest "https://github.com/$($spec.repo)/releases/download/$($spec.tag)/$checksumName").Content
        if ($sumContent -is [byte[]]) { $sumContent = [Text.Encoding]::UTF8.GetString($sumContent) }
        $expected = ([regex]::Match($sumContent, '[a-fA-F0-9]{64}')).Value.ToLowerInvariant()
        if ($hash -ne $expected) { throw 'FFmpeg checksum mismatch' }
    }
    $destination = Join-Path $root "engines/$Rid/$($spec.id)"
    New-Item -ItemType Directory -Path $destination -Force | Out-Null
    if ($spec.asset.EndsWith('.zip')) { Expand-Archive -LiteralPath $download -DestinationPath $destination -Force }
    elseif ($spec.asset.EndsWith('.7z')) { & 7z x $download "-o$destination" -y | Out-Null; if($LASTEXITCODE){throw '7z extraction failed'} }
    else { & tar -xf $download -C $destination; if($LASTEXITCODE){throw 'tar extraction failed'} }
    $records += [pscustomobject]@{id="$Rid/$($spec.id)";version=$spec.tag;source=$asset.browser_download_url;sha256=$hash}
    Write-Host "Ready: $($spec.id) [$($spec.tag)]"
}
$existing = @()
if (Test-Path 'engines/manifest.json') { $existing = @(Get-Content 'engines/manifest.json' -Raw | ConvertFrom-Json) }
$ids = $records.id
@($existing | Where-Object {$_.id -notin $ids}) + $records | ConvertTo-Json -Depth 4 | Set-Content 'engines/manifest.json' -Encoding utf8
Copy-Item 'engines/manifest.json' 'docs/engine-lock.json'

