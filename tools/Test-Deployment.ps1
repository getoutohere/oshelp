$ErrorActionPreference='Stop'
$repository=Split-Path $PSScriptRoot -Parent
$app=Join-Path $repository 'app'
$pointer=Join-Path $app 'current.json'
if(-not(Test-Path -LiteralPath $pointer)){throw 'Run Deploy.ps1 once before testing deployment.'}
$old=Get-Content -LiteralPath $pointer -Raw|ConvertFrom-Json
$oldRelease=Join-Path $app $old.directory
$launcherHash=(Get-FileHash -LiteralPath (Join-Path $app 'OSHelp.exe')).Hash
$pointerHash=(Get-FileHash -LiteralPath $pointer).Hash
$manifest=Get-Content -LiteralPath (Join-Path $oldRelease 'release.json') -Raw|ConvertFrom-Json
$data=Join-Path $repository 'local/deployment-running-data'
$null=New-Item -ItemType Directory -Force -Path $data
Copy-Item -LiteralPath (Join-Path $repository 'local/ui-smoke-data/catalog.json') -Destination (Join-Path $data 'catalog.json') -Force
$ownedProcess=$null
function Check([bool]$Condition,[string]$Message){if(-not $Condition){throw $Message}}
function Check-OldRelease {
    $ownedProcess.Refresh(); Check (-not $ownedProcess.HasExited) 'The running release exited during development/deployment.'
    foreach($file in $manifest.files){Check ((Get-FileHash -LiteralPath (Join-Path $oldRelease $file.file)).Hash -eq $file.sha256) ('A running release file was modified: '+$file.file)}
    Check ((Get-FileHash -LiteralPath (Join-Path $app 'OSHelp.exe')).Hash -eq $launcherHash) 'The canonical launcher binary changed during ordinary development/deployment.'
}
try{
    # This is an owned test window with isolated profiles, not the user's app.
    # Omitting --repository/--game also exercises the release manifest defaults.
    $ownedProcess=Start-Process -FilePath (Join-Path $oldRelease 'OSHelp.exe') -ArgumentList @('--data-dir',('"'+$data+'"')) -WindowStyle Hidden -PassThru
    & (Join-Path $PSScriptRoot 'Build.ps1') -Smoke
    Check-OldRelease
    Check ((Get-FileHash -LiteralPath $pointer).Hash -eq $pointerHash) 'A preview build changed the selected release.'
    Check (Test-Path -LiteralPath (Join-Path $data 'settings.json')) 'The live test release did not initialize with its manifest defaults.'
    & (Join-Path $PSScriptRoot 'Deploy.ps1')
    $new=Get-Content -LiteralPath $pointer -Raw|ConvertFrom-Json
    Check ($new.releaseId -ne $old.releaseId) 'Deployment did not create a separate release.'
    Check-OldRelease
    $capture=Join-Path $repository ('local/launcher-smoke-'+$new.releaseId)
    & (Join-Path $PSScriptRoot 'Test-PublishedApp.ps1') -Executable (Join-Path $app 'OSHelp.exe') -Repository (Join-Path $app $new.directory) -GameRoot (Split-Path $repository -Parent) -DataDirectory (Join-Path $repository 'local/launcher-smoke-data') -CaptureDirectory $capture -Launcher
    & (Join-Path $PSScriptRoot 'Deploy.ps1') -Rollback
    Check ((Get-Content -LiteralPath $pointer -Raw|ConvertFrom-Json).releaseId -eq $old.releaseId) 'Rollback did not restore the preceding release.'
    Check-OldRelease
    & (Join-Path $PSScriptRoot 'Deploy.ps1') -Rollback
    Check ((Get-Content -LiteralPath $pointer -Raw|ConvertFrom-Json).releaseId -eq $new.releaseId) 'The latest release was not reselected after rollback testing.'
    $result=[ordered]@{checkedAt=[DateTimeOffset]::UtcNow.ToString('O');previousRelease=$old.releaseId;currentRelease=$new.releaseId;previewDidNotChangeRelease=$true;deploymentWhileOldVersionRunning=$true;oldBinariesAndSupportFilesUnchanged=$true;stableLauncherHashUnchanged=$true;launcherDefaultsAndArgumentForwarding=$true;rollback=$true;isolatedTestProfiles=$true}
    $result|ConvertTo-Json|Set-Content -Encoding UTF8 -LiteralPath (Join-Path $repository 'local/deployment-verification.json')
    'PASS: live release survived preview builds and deployment; launcher, snapshots, isolated profiles, and rollback verified.'
}finally{
    if($ownedProcess){
        $ownedProcess.Refresh()
        if(-not $ownedProcess.HasExited){$null=$ownedProcess.CloseMainWindow();if(-not $ownedProcess.WaitForExit(3000)){$ownedProcess.Kill();$ownedProcess.WaitForExit()}}
        $ownedProcess.Dispose()
    }
}
