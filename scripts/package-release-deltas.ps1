param(
    [string]$BaseVersion='0.1.0-preview.1',
    [string]$Version='0.1.0-preview.2',
    [string]$RawPatchRoot=''
)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
foreach($value in @($BaseVersion,$Version)){if($value -notmatch '^\d+\.\d+\.\d+(?:-[A-Za-z0-9.-]+)?$'){throw 'Invalid version'}}
$work=Join-Path $root "artifacts/release-deltas/$Version"
$release=Join-Path $root "artifacts/release/$Version"
New-Item -ItemType Directory -Path $work -Force | Out-Null
Add-Type -TypeDefinition @'
using System;
using System.IO;
using System.Text;
public static class WinWhisperDelta {
 public static void Forward(string input,string output,string name) {
  using var r=new StreamReader(input,Encoding.ASCII);
  using var w=new StreamWriter(output,false,new UTF8Encoding(false)); w.NewLine="\n";
  if (!(r.ReadLine() ?? "").StartsWith("diff --git ")) throw new Exception("Missing binary diff");
  w.WriteLine("diff --git a/"+name+" b/"+name);
  bool hunk=false; string line;
  while((line=r.ReadLine())!=null) { w.WriteLine(line); if(line.StartsWith("delta ")||line.StartsWith("literal ")) hunk=true; if(hunk&&line.Length==0) return; }
  throw new Exception("Incomplete forward binary patch");
 }
}
'@
$records=foreach($variant in 'Universal','x64','arm64'){
    $baseName="WinWhisper-$BaseVersion-$variant-Setup.exe"
    $targetName="WinWhisper-$Version-$variant-Setup.exe"
    $baseFile=Join-Path $root "artifacts/release/$BaseVersion/$baseName"
    $targetFile=Join-Path $release $targetName
    $raw=if($RawPatchRoot){Join-Path (Resolve-Path -LiteralPath $RawPatchRoot).Path "$variant-transport.patch"}else{Join-Path $work "$variant.raw"}
    if(!$RawPatchRoot){
        $process=[Diagnostics.Process]::new()
        $process.StartInfo.FileName='git'
        $process.StartInfo.UseShellExecute=$false
        $process.StartInfo.RedirectStandardOutput=$true
        foreach($argument in @('-c','core.bigFileThreshold=3g','diff','--binary','--no-index','--no-ext-diff','--',$baseFile,$targetFile)){ $process.StartInfo.ArgumentList.Add($argument) }
        $stream=[IO.File]::Create($raw)
        try { [void]$process.Start(); $process.StandardOutput.BaseStream.CopyTo($stream); $process.WaitForExit(); if($process.ExitCode -ne 1){throw 'Binary diff generation failed'} } finally { $stream.Dispose(); $process.Dispose() }
    }
    $patch=Join-Path $work "$variant.patch"
    [WinWhisperDelta]::Forward($raw,$patch,$targetName)
    [ordered]@{variant=$variant;baseName=$baseName;targetName=$targetName;patchName="$variant.patch";baseSha256=(Get-FileHash $baseFile).Hash.ToLowerInvariant();sha256=(Get-FileHash $targetFile).Hash.ToLowerInvariant();patchSha256=(Get-FileHash $patch).Hash.ToLowerInvariant();bytes=(Get-Item $targetFile).Length}
}
@{version=$Version;baseVersion=$BaseVersion;files=@($records)} | ConvertTo-Json -Depth 6 | Set-Content (Join-Path $work manifest.json) -Encoding utf8
$zip=Join-Path $release INSTALLER-TRANSPORT.zip
Compress-Archive -LiteralPath @((Join-Path $work manifest.json), (Join-Path $work Universal.patch), (Join-Path $work x64.patch), (Join-Path $work arm64.patch)) -DestinationPath $zip -CompressionLevel Fastest -Force
Get-Item $zip | Select-Object FullName,Length
