param([switch]$Integration, [switch]$Ollama, [switch]$Speech, [switch]$Benchmark, [switch]$Online, [string]$Gguf='')
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
Set-Location -LiteralPath $root
$testArgs=@("--root=$root")
if($Integration){$testArgs+='--integration'}
if($Ollama){$testArgs+='--ollama'}
if($Speech){$testArgs+='--speech'}
if($Benchmark){$testArgs+='--benchmark'}
if($Online){$testArgs+='--online'}
if($Gguf){$testArgs+="--gguf=$Gguf"}
& "$root/.tools/dotnet/dotnet.exe" run --project tests/MemoAI.Tests/MemoAI.Tests.csproj -c Release -- @testArgs
if($LASTEXITCODE){throw 'Verification failed'}
