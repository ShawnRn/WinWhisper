param([ValidateSet('win-arm64','win-x64')][string]$Rid='win-arm64', [switch]$NoBuild, [string]$AppPath='', [string]$PlaybackVideo='', [switch]$Recording)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
if(!$NoBuild -and !$AppPath){ & "$PSScriptRoot/build.ps1" -Rid $Rid }
$platform=if($Rid -eq 'win-arm64'){'ARM64'}else{'x64'}
$app=Join-Path $root "src/MemoAI.App/bin/$platform/Release/net10.0-windows10.0.26100.0/$Rid/WinWhisper.exe"
if($AppPath){$app=(Resolve-Path -LiteralPath $AppPath).Path}
if(!(Test-Path -LiteralPath $app)){throw 'Build the application first.'}
$testRoot=Join-Path $root ("artifacts/ui-tests/$Rid/"+(Get-Date -Format 'yyyyMMdd-HHmmss')+'-'+[Guid]::NewGuid().ToString('N').Substring(0,8))
New-Item -ItemType Directory -Path $testRoot -Force | Out-Null
$arguments=@(('"--data-root='+$testRoot+'"'),'--ui-self-test','--exit-after-test')
if($PlaybackVideo){$arguments+=('"--ui-test-video='+(Resolve-Path -LiteralPath $PlaybackVideo).Path+'"')}
if($Recording){$arguments+='--ui-test-recording'}
$process=Start-Process -FilePath $app -ArgumentList $arguments -PassThru -WindowStyle Hidden
if(!$process.WaitForExit(60000)){
    Stop-Process -Id $process.Id -ErrorAction SilentlyContinue
    throw "UI regression timed out. Isolated test data: $testRoot"
}
$report=Join-Path $testRoot 'ui-test-results.json'
if(!(Test-Path -LiteralPath $report)){throw "No UI regression report. Check $testRoot"}
$result=Get-Content -LiteralPath $report -Raw | ConvertFrom-Json
$result | ConvertTo-Json
if(!$result.passed){throw "UI regression failed: $report"}
if($process.ExitCode -ne 0){throw "UI process exited abnormally ($($process.ExitCode)): $testRoot"}
Write-Host "PASS: $report"
