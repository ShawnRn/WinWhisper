param(
    [ValidateSet('win-arm64','win-x64')][string]$Rid='win-arm64',
    [string]$Audio='artifacts/test-assets/jfk.wav',
    [string]$Model=(Join-Path $env:LOCALAPPDATA 'MemoAI.Native/models/ggml-large-v3-turbo.bin'),
    [int[]]$Threads=@(4,8,12,16),
    [ValidateRange(1,5)][int]$Rounds=2
)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
Set-Location -LiteralPath $root
$audioPath=(Resolve-Path -LiteralPath $Audio).Path
$modelPath=(Resolve-Path -LiteralPath $Model).Path
$engine=Get-ChildItem -LiteralPath "engines/$Rid/whisper-cuda" -Filter whisper-cli.exe -Recurse -File | Select-Object -First 1 -ExpandProperty FullName
if(!$engine){throw 'Missing pinned Whisper CUDA engine'}
if($Threads | Where-Object {$_ -lt 1 -or $_ -gt 64}){throw 'Threads must be in [1,64]'}
$run=Join-Path $root ('artifacts/whisper-performance/'+(Get-Date -Format yyyyMMdd-HHmmss)+'-'+[guid]::NewGuid().ToString('N').Substring(0,8))
New-Item -ItemType Directory -Path $run -Force | Out-Null
$records=@()
foreach($round in 1..$Rounds){
    # Reverse the order in alternate rounds to reduce warm-cache / thermal bias.
    $order=@($Threads); if($round % 2 -eq 0){[array]::Reverse($order)}
    foreach($count in $order){
        if(Get-Process -Name whisper-cli -ErrorAction SilentlyContinue){throw 'Another Whisper transcription is running; benchmark deferred'}
        $prefix=Join-Path $run "round-$round-threads-$count"
        $watch=[Diagnostics.Stopwatch]::StartNew()
        & $engine -m $modelPath -f $audioPath -l en -t $count -bs 5 -tp 0 -fa -ojf -of $prefix 1>"$prefix.output.log" 2>"$prefix.engine.log"
        $exit=$LASTEXITCODE; $watch.Stop()
        if($exit){throw "Whisper exited with $exit"}
        $log=Get-Content -LiteralPath "$prefix.engine.log" -Raw
        if($log -notmatch 'using CUDA\d* backend' -or $log -notmatch 'flash attn = 1'){throw 'CUDA / flash attention was not initialized'}
        $timings=@{}
        foreach($match in [regex]::Matches($log,'whisper_print_timings:\s+(\w+) time\s*=\s*([\d.]+) ms')){$timings[$match.Groups[1].Value]=[double]::Parse($match.Groups[2].Value,[Globalization.CultureInfo]::InvariantCulture)}
        $json=Get-Content -LiteralPath "$prefix.json" -Raw | ConvertFrom-Json
        $text=($json.transcription | ForEach-Object text) -join ' '
        if($text -notmatch 'country'){throw 'Recognized content failed the JFK speech sanity check'}
        $records+=@{round=$round;threads=$count;wallSeconds=$watch.Elapsed.TotalSeconds;timingsMs=$timings;transcript=$text;engineLog="$prefix.engine.log"}
        Write-Host "Round $round / $count threads: $($watch.Elapsed.TotalSeconds.ToString('0.00')) s"
    }
}
$summary=@($records | Group-Object threads | ForEach-Object {
    $ordered=@($_.Group.wallSeconds | Sort-Object)
    $median=if($ordered.Count % 2){$ordered[[int][math]::Floor($ordered.Count/2)]}else{($ordered[$ordered.Count/2-1]+$ordered[$ordered.Count/2])/2}
    @{threads=[int]$_.Name;medianWallSeconds=$median;normalizedTranscriptsAgree=@($_.Group.transcript | ForEach-Object {($_.ToLowerInvariant() -replace '[^\p{L}\p{N}]','')} | Sort-Object -Unique).Count -eq 1}
})
@{rid=$Rid;engine=$engine;model=$modelPath;audio=$audioPath;beamSize=5;flashAttention=$true;vad=$false;rounds=$Rounds;records=$records;summary=$summary;note='Short speech benchmark; utilization percentage alone is not a performance target. Thread count is not changed automatically.'} |
    ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $run 'results.json') -Encoding utf8
$summary | Sort-Object medianWallSeconds | Format-Table
Write-Host "Report: $run/results.json"
