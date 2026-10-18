param([switch]$Engines, [switch]$TestAssets, [ValidateSet('arm64','x64')][string]$SdkArchitecture='')
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Set-Location -LiteralPath $root
$sdk = Join-Path $root '.tools/dotnet/dotnet.exe'
if(!$SdkArchitecture){$SdkArchitecture=if([Runtime.InteropServices.RuntimeInformation]::OSArchitecture -eq 'Arm64'){'arm64'}else{'x64'}}
if (!(Test-Path -LiteralPath $sdk)) {
    New-Item -ItemType Directory -Path '.tools' -Force | Out-Null
    Invoke-WebRequest 'https://dot.net/v1/dotnet-install.ps1' -OutFile '.tools/dotnet-install.ps1'
    & './.tools/dotnet-install.ps1' -Version '10.0.402' -Architecture $SdkArchitecture -InstallDir "$root/.tools/dotnet" -NoPath
    if ($LASTEXITCODE) { throw 'SDK installation failed' }
}
if ($Engines) { & "$PSScriptRoot/fetch-engines.ps1" }
if ($TestAssets) {
    New-Item -ItemType Directory -Path 'artifacts/test-assets' -Force | Out-Null
    Invoke-WebRequest 'https://raw.githubusercontent.com/ggml-org/whisper.cpp/b5454/samples/jfk.wav' -OutFile 'artifacts/test-assets/jfk.wav'
    Invoke-WebRequest 'https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-tiny.en.bin' -OutFile 'artifacts/test-assets/ggml-tiny.en.bin'
}
& $sdk --version

