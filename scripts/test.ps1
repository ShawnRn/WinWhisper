param([switch]$Integration, [switch]$Ollama)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
Set-Location -LiteralPath $root
$testArgs=@("--root=$root")
if($Integration){$testArgs+='--integration'}
if($Ollama){$testArgs+='--ollama'}
& "$root/.tools/dotnet/dotnet.exe" run --project tests/MemoAI.Tests/MemoAI.Tests.csproj -c Release -- @testArgs
if($LASTEXITCODE){throw 'Verification failed'}
