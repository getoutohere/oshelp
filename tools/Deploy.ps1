param([switch]$Verify,[switch]$Rollback)
$ErrorActionPreference='Stop'
if($Verify -and $Rollback){throw 'Use -Verify or -Rollback, not both.'}
$repository=Split-Path $PSScriptRoot -Parent
$gameRoot=Split-Path $repository -Parent
$app=Join-Path $repository 'app'
$releases=Join-Path $app 'releases'
$null=New-Item -ItemType Directory -Force -Path $releases
$pointer=Join-Path $app 'current.json'
$env:DOTNET_CLI_HOME=Join-Path $repository 'local/dotnet'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE='1'
$env:DOTNET_CLI_TELEMETRY_OPTOUT='1'
$deploymentLock=$null
function Install-CraftingCache([string]$DestinationDirectory){
    $source=Join-Path $repository 'local/crafting-check-data/crafting.json'
    if(-not(Test-Path -LiteralPath $source)){return}
    $null=New-Item -ItemType Directory -Force -Path $DestinationDirectory
    $destination=Join-Path $DestinationDirectory 'crafting.json'
    $temporary=Join-Path $DestinationDirectory ('crafting.'+[Guid]::NewGuid().ToString('N')+'.tmp')
    try{
        [IO.File]::Copy($source,$temporary)
        if(Test-Path -LiteralPath $destination){[IO.File]::Replace($temporary,$destination,[NullString]::Value)}else{[IO.File]::Move($temporary,$destination)}
    }finally{if(Test-Path -LiteralPath $temporary){Remove-Item -LiteralPath $temporary}}
}
function Set-CurrentRelease([string]$Content){
    $selected=$Content|ConvertFrom-Json
    $target=[IO.Path]::GetFullPath((Join-Path $app $selected.directory))
    $allowed=[IO.Path]::GetFullPath($releases).TrimEnd([IO.Path]::DirectorySeparatorChar)+[IO.Path]::DirectorySeparatorChar
    if(-not $target.StartsWith($allowed,[StringComparison]::OrdinalIgnoreCase) -or -not(Test-Path -LiteralPath (Join-Path $target 'OSHelp.exe')) -or -not(Test-Path -LiteralPath (Join-Path $target 'release.json'))){throw 'Release pointer does not select a complete published release.'}
    $temporary=Join-Path $app ('current.'+[Guid]::NewGuid().ToString('N')+'.tmp')
    try{
        [IO.File]::WriteAllText($temporary,$Content,[Text.UTF8Encoding]::new($false))
        if(Test-Path -LiteralPath $pointer){[IO.File]::Replace($temporary,$pointer,($pointer+'.bak'))}else{[IO.File]::Move($temporary,$pointer)}
    }finally{if(Test-Path -LiteralPath $temporary){Remove-Item -LiteralPath $temporary}}
}
try{
    try{$deploymentLock=[IO.File]::Open((Join-Path $app '.deploy.lock'),[IO.FileMode]::OpenOrCreate,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None)}catch{throw 'Another deployment is already running.'}
    if($Rollback){
        if(-not(Test-Path -LiteralPath ($pointer+'.bak'))){throw 'No previous deployed release is available.'}
        Set-CurrentRelease ([IO.File]::ReadAllText($pointer+'.bak'))
        'Restored previous release: '+((Get-Content -LiteralPath $pointer -Raw|ConvertFrom-Json).releaseId)
        return
    }
    Push-Location $repository
    try{
        & (Join-Path $PSScriptRoot 'Build.ps1') -Verify:$Verify
        if(-not $Verify){
            if(-not(Test-Path -LiteralPath (Join-Path $repository 'local/planning-import/planning.json'))){
                & powershell -NoProfile -NonInteractive -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'Export-PlanningData.ps1')
                if($LASTEXITCODE -ne 0){throw 'Preparing verification data failed; current release is unchanged.'}
            }
            & dotnet run --project tests/OSHelp.Verify/OSHelp.Verify.csproj -c Release --no-build
            if($LASTEXITCODE -ne 0){throw 'Regression verification failed; current release is unchanged.'}
        }
        $id=[DateTime]::UtcNow.ToString('yyyyMMddTHHmmssZ')+'-'+[Guid]::NewGuid().ToString('N').Substring(0,8)
        $release=Join-Path $releases $id
        $null=New-Item -ItemType Directory -Path $release
        & dotnet publish src/OSHelp.App/OSHelp.App.csproj -c Release --no-build --no-restore --no-self-contained -o $release --nologo
        if($LASTEXITCODE -ne 0){throw 'Release publishing failed; current release is unchanged.'}
        # Each release owns its runtime tools, theme, and verified data baseline.
        # Development edits to these files cannot change an installed release.
        foreach($directory in @('tools','design','docs')){Copy-Item -LiteralPath (Join-Path $repository $directory) -Destination (Join-Path $release $directory) -Recurse}
        $files=@(Get-ChildItem -LiteralPath $release -File -Recurse|Sort-Object FullName|ForEach-Object {[ordered]@{file=$_.FullName.Substring($release.Length+1);sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash}})
        $manifest=[ordered]@{formatVersion=1;releaseId=$id;publishedAt=[DateTimeOffset]::UtcNow.ToString('O');gameRoot=$gameRoot;dataDirectory=(Join-Path $repository 'local/userdata');files=$files}
        [IO.File]::WriteAllText((Join-Path $release 'release.json'),($manifest|ConvertTo-Json -Depth 6),[Text.UTF8Encoding]::new($false))
        $capture=Join-Path $repository ('local/deploy-smoke-'+$id)
        # Reuse the derived catalog just verified against the installed sources.
        # The app still validates its fingerprint; profiles are never copied.
        Install-CraftingCache (Join-Path $repository 'local/deploy-smoke-data')
        & (Join-Path $PSScriptRoot 'Test-PublishedApp.ps1') -Executable (Join-Path $release 'OSHelp.exe') -Repository $release -GameRoot $gameRoot -DataDirectory (Join-Path $repository 'local/deploy-smoke-data') -CaptureDirectory $capture
        $launcher=Join-Path $repository 'artifacts/launcher'
        & dotnet publish src/OSHelp.Launcher/OSHelp.Launcher.csproj -c Release --no-build --no-restore --no-self-contained -o $launcher --nologo
        if($LASTEXITCODE -ne 0){throw 'Launcher publishing failed; current release is unchanged.'}
        # Only the first conversion from the former in-place app needs it closed.
        if(-not(Test-Path -LiteralPath $pointer)){
            $legacy=Get-Process OSHelp -ErrorAction SilentlyContinue|Where-Object {$_.Path -eq (Join-Path $app 'OSHelp.exe')}
            if($legacy){throw "The validated release is ready at $release. Close the legacy OSHelp once, then rerun Deploy.ps1 to install the versioned launcher."}
        }
        foreach($name in @('OSHelp.Launcher.dll','OSHelp.Launcher.deps.json','OSHelp.Launcher.runtimeconfig.json','OSHelp.Launcher.exe')){
            $source=Join-Path $launcher $name;$destination=Join-Path $app $(if($name -eq 'OSHelp.Launcher.exe'){'OSHelp.exe'}else{$name})
            if((Test-Path -LiteralPath $destination) -and (Get-FileHash -LiteralPath $destination).Hash -eq (Get-FileHash -LiteralPath $source).Hash){continue}
            Copy-Item -LiteralPath $source -Destination $destination -Force
        }
        Set-CurrentRelease (([ordered]@{formatVersion=1;releaseId=$id;directory=('releases/'+$id)})|ConvertTo-Json)
        try{Install-CraftingCache (Join-Path $repository 'local/userdata')}catch [IO.IOException]{'Crafting cache will be rebuilt on first search.'}
        'Deployed release: '+$id
        'Stable launcher: '+(Join-Path $app 'OSHelp.exe')
        'Running releases keep working. New launches use this release.'
    }finally{Pop-Location}
}finally{if($deploymentLock){$deploymentLock.Dispose()}}
