param(
    [Parameter(Mandatory)][string]$Installer,
    [ValidateSet('CurrentUser','AllUsers')][string]$Scope='CurrentUser',
    [string]$WorkRoot='',
    [string]$PlaybackVideo='',
    [switch]$SkipAppLaunch
)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$setup=(Resolve-Path -LiteralPath $Installer).Path
if($Scope -eq 'AllUsers'){
    $principal=[Security.Principal.WindowsPrincipal]::new([Security.Principal.WindowsIdentity]::GetCurrent())
    if(!$principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)){throw 'AllUsers QA requires an already elevated environment; this script never approves UAC'}
}
if(!$WorkRoot){$WorkRoot=Join-Path $root 'artifacts/installer-tests'}
$parent=[IO.Path]::GetFullPath($WorkRoot)
$run=Join-Path $parent ((Get-Date -Format 'yyyyMMdd-HHmmss')+'-'+[guid]::NewGuid().ToString('N').Substring(0,8))
$target=Join-Path $run 'application'
if(!$target.StartsWith($parent+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw 'Unsafe installer test target'}
New-Item -ItemType Directory -Path $target -Force | Out-Null
'Owned installer test directory' | Set-Content -LiteralPath "$target/.winwhisper-installer-test"
$registry=if($Scope -eq 'AllUsers'){'HKLM:'}else{'HKCU:'}
$key="$registry/Software/Microsoft/Windows/CurrentVersion/Uninstall/WinWhisper.InstallerTest"
if(Test-Path $key){throw 'An existing installer-test registration must be reviewed before running another test'}
$desktop=[Environment]::GetFolderPath($(if($Scope -eq 'AllUsers'){'CommonDesktopDirectory'}else{'Desktop'}))
$programs=[Environment]::GetFolderPath($(if($Scope -eq 'AllUsers'){'CommonPrograms'}else{'Programs'}))
$desktopLink=Join-Path $desktop 'WinWhisper Installer Test.lnk'
$menuLink=Join-Path $programs 'WinWhisper Installer Test.lnk'
if((Test-Path -LiteralPath $desktopLink) -or (Test-Path -LiteralPath $menuLink)){throw 'Existing test shortcut; refusing to overwrite it'}
$checks=[ordered]@{installer=[IO.Path]::GetFileName($setup);sha256=(Get-FileHash -LiteralPath $setup -Algorithm SHA256).Hash.ToLowerInvariant();scope=$Scope;uiSkipped=[bool]$SkipAppLaunch}
function Require([bool]$Condition,[string]$Message){if(!$Condition){throw $Message}}
function Run-Setup {
    # NSIS /D= is deliberately the final, unquoted command-line tail.
    $process=Start-Process -FilePath $setup -ArgumentList "/S /$Scope /TestInstall /D=$target" -WindowStyle Hidden -Wait -PassThru
    Require ($process.ExitCode -eq 0) "Installer exited with $($process.ExitCode)"
}
function Read-Machine([string]$Path){
    $stream=[IO.File]::OpenRead($Path)
    try{$pe=[Reflection.PortableExecutable.PEReader]::new($stream);try{return [int]$pe.PEHeaders.CoffHeader.Machine}finally{$pe.Dispose()}}finally{$stream.Dispose()}
}
try{
    Run-Setup
    Require (Test-Path -LiteralPath "$target/WinWhisper.exe") 'Application was not installed to the isolated target'
    $actual=(Get-ItemProperty -LiteralPath $key).InstallLocation
    Require ($actual -eq $target) 'Registry install location differs from the isolated target'
    $scopeRecord=Get-Content -LiteralPath "$target/install-scope.ini" -Raw
    Require ($scopeRecord -match "(?m)^Scope=$Scope`r?$") 'Installation scope was not recorded correctly'
    $machine=Read-Machine "$target/WinWhisper.exe"
    $workerMachine=Read-Machine "$target/worker/MemoAI.Worker.exe"
    Require ($machine -eq $workerMachine) 'Application and default Worker architectures differ'
    if($setup -match '-Universal-'){
        $expected=if([Runtime.InteropServices.RuntimeInformation]::OSArchitecture -eq 'Arm64'){0xaa64}else{0x8664}
        Require ($machine -eq $expected) 'Universal installer selected the wrong payload'
    }
    $checks.applicationMachine=$machine; $checks.workerMachine=$workerMachine
    Require (!(Test-Path -LiteralPath $desktopLink)) 'Desktop shortcut was created by default'
    Require (Test-Path -LiteralPath $menuLink) 'Start menu shortcut is missing'
    $checks.desktopUnchecked=$true; $checks.startMenuCreated=$true
    'Preserve this user-owned file across upgrade and uninstall' | Set-Content -LiteralPath "$target/unowned-user-file.txt"
    $data=Join-Path $run 'user-data'; New-Item -ItemType Directory -Path $data -Force | Out-Null
    'Preserve application user data' | Set-Content -LiteralPath "$data/keep.txt"
    Run-Setup
    Require (Test-Path -LiteralPath "$target/unowned-user-file.txt") 'Upgrade deleted an unrelated user file'
    Require (Test-Path -LiteralPath "$data/keep.txt") 'Upgrade deleted application data'
    $checks.upgradePreservesData=$true
    if(!$SkipAppLaunch){
        $arguments=@(('"--data-root='+$data+'"'),'--ui-self-test','--exit-after-test')
        if($PlaybackVideo){$arguments+=('"--ui-test-video='+(Resolve-Path -LiteralPath $PlaybackVideo).Path+'"')}
        $app=Start-Process -FilePath "$target/WinWhisper.exe" -ArgumentList $arguments -WindowStyle Hidden -PassThru
        if(!$app.WaitForExit(60000)){Stop-Process -Id $app.Id -ErrorAction SilentlyContinue;throw 'Installed native UI self-test timed out'}
        Require ($app.ExitCode -eq 0) "Installed application exited abnormally: $($app.ExitCode)"
        $report=Get-Content -LiteralPath "$data/ui-test-results.json" -Raw | ConvertFrom-Json
        Require $report.passed 'Installed native UI regression failed'
        $checks.installedUiPassed=$true
    }
    $uninstaller=Start-Process -FilePath "$target/Uninstall.exe" -ArgumentList '/S' -WindowStyle Hidden -PassThru -Wait
    Require ($uninstaller.ExitCode -eq 0) "Uninstaller exited with $($uninstaller.ExitCode)"
    # NSIS's temporary uninstaller child can outlive the launcher briefly.
    for($attempt=0;$attempt -lt 40 -and (Test-Path -LiteralPath "$target/WinWhisper.exe");$attempt++){Start-Sleep -Milliseconds 250}
    Require (!(Test-Path -LiteralPath "$target/WinWhisper.exe")) 'Uninstaller retained the package executable'
    Require (!(Test-Path $key)) 'Uninstaller retained its registry entry'
    Require (!(Test-Path -LiteralPath $menuLink)) 'Uninstaller retained its Start menu shortcut'
    Require (Test-Path -LiteralPath "$target/unowned-user-file.txt") 'Uninstaller deleted an unrelated user file'
    Require (Test-Path -LiteralPath "$data/keep.txt") 'Uninstaller deleted application data'
    $checks.uninstallPreservesData=$true; $checks.registrationRemoved=$true; $checks.passed=$true
}catch{
    $checks.passed=$false; $checks.error=$_.Exception.Message
    throw
}finally{
    $checks | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath "$run/installer-test-results.json" -Encoding utf8
    Write-Host "Installer report: $run/installer-test-results.json"
}
