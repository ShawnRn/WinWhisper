param([ValidateSet('win-arm64','win-x64')][string]$Rid='win-arm64', [ValidateSet('Debug','Release')][string]$Configuration='Release')
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
Set-Location -LiteralPath $root
$sdk=Join-Path $root '.tools/dotnet/dotnet.exe'
if(!(Test-Path $sdk)){throw 'Run scripts/bootstrap.ps1 first'}
$env:DOTNET_CLI_TELEMETRY_OPTOUT='1'
$platform=if($Rid -eq 'win-arm64'){'ARM64'}else{'x64'}
& $sdk build 'src/MemoAI.App/MemoAI.App.csproj' -c $Configuration -r $Rid "-p:Platform=$platform" --self-contained true
if($LASTEXITCODE){throw 'App build failed'}
& $sdk publish 'src/MemoAI.Worker/MemoAI.Worker.csproj' -c $Configuration -r $Rid --self-contained true -o "artifacts/$Rid/worker"
if($LASTEXITCODE){throw 'Worker build failed'}
$appOutput="src/MemoAI.App/bin/$platform/$Configuration/net10.0-windows10.0.26100.0/$Rid"
if(!(Test-Path $appOutput)){$appOutput="src/MemoAI.App/bin/$Configuration/net10.0-windows10.0.26100.0/$Rid"}
New-Item -ItemType Directory -Path "$appOutput/worker" -Force | Out-Null
Copy-Item -Path "artifacts/$Rid/worker/*" -Destination "$appOutput/worker" -Recurse -Force
Write-Host "Application: $root/$appOutput/MemoAI.Native.exe"
