param(
    [Parameter(Mandatory=$true)][string]$Executable,
    [Parameter(Mandatory=$true)][string]$Repository,
    [Parameter(Mandatory=$true)][string]$GameRoot,
    [Parameter(Mandatory=$true)][string]$DataDirectory,
    [Parameter(Mandatory=$true)][string]$CaptureDirectory,
    [switch]$Launcher
)
$ErrorActionPreference='Stop'
$null=New-Item -ItemType Directory -Force -Path $DataDirectory
$warm=Join-Path (Split-Path $PSScriptRoot -Parent) 'local/ui-smoke-data/catalog.json'
if(-not(Test-Path -LiteralPath (Join-Path $DataDirectory 'catalog.json')) -and (Test-Path -LiteralPath $warm)){Copy-Item -LiteralPath $warm -Destination (Join-Path $DataDirectory 'catalog.json')}
$arguments=@('--data-dir',('"'+$DataDirectory+'"'),'--smoke',('"'+$CaptureDirectory+'"'))
if($Launcher){$arguments=@('--wait')+$arguments}else{$arguments=@('--repository',('"'+$Repository+'"'),'--game',('"'+$GameRoot+'"'))+$arguments}
$process=Start-Process -FilePath $Executable -ArgumentList $arguments -WindowStyle Hidden -PassThru
$process.WaitForExit()
if($process.ExitCode -ne 0 -or (Test-Path -LiteralPath (Join-Path $CaptureDirectory 'error.txt')) -or -not(Test-Path -LiteralPath (Join-Path $CaptureDirectory 'success.txt'))){throw "GUI verification failed. Inspect $CaptureDirectory"}
Get-Content -LiteralPath (Join-Path $CaptureDirectory 'success.txt')
if(Test-Path -LiteralPath (Join-Path $CaptureDirectory 'vendor-sort.txt')){Get-Content -LiteralPath (Join-Path $CaptureDirectory 'vendor-sort.txt')}
if(Test-Path -LiteralPath (Join-Path $CaptureDirectory 'profile-actions.txt')){Get-Content -LiteralPath (Join-Path $CaptureDirectory 'profile-actions.txt')}
if(Test-Path -LiteralPath (Join-Path $CaptureDirectory 'gold-shop.txt')){Get-Content -LiteralPath (Join-Path $CaptureDirectory 'gold-shop.txt')}
if(Test-Path -LiteralPath (Join-Path $CaptureDirectory 'mobelmann.txt')){Get-Content -LiteralPath (Join-Path $CaptureDirectory 'mobelmann.txt')}
if(Test-Path -LiteralPath (Join-Path $CaptureDirectory 'tenant-filters.txt')){Get-Content -LiteralPath (Join-Path $CaptureDirectory 'tenant-filters.txt')}
if(Test-Path -LiteralPath (Join-Path $CaptureDirectory 'cart-cleanup.txt')){Get-Content -LiteralPath (Join-Path $CaptureDirectory 'cart-cleanup.txt')}
'GUI screenshots: '+$CaptureDirectory
