param([switch]$Verify,[switch]$Smoke)
$ErrorActionPreference='Stop'
$repository=Split-Path $PSScriptRoot -Parent
$env:DOTNET_CLI_HOME=Join-Path $repository 'local/dotnet'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE='1'
$env:DOTNET_CLI_TELEMETRY_OPTOUT='1'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE='false'
Push-Location $repository
try {
    & dotnet build OSHelp.sln -c Release --nologo
    if($LASTEXITCODE -ne 0){throw 'Solution build failed'}
    $preview=Join-Path $repository 'artifacts/preview'
    & dotnet publish src/OSHelp.App/OSHelp.App.csproj -c Release --no-self-contained -o $preview --nologo
    if($LASTEXITCODE -ne 0){throw 'Publishing failed'}
    if($Verify){
        & powershell -NoProfile -NonInteractive -ExecutionPolicy Bypass -File tools/Export-PlanningData.ps1
        if($LASTEXITCODE -ne 0){throw 'Scene/dialogue extraction failed'}
        & powershell -NoProfile -NonInteractive -ExecutionPolicy Bypass -File tools/Inspect-ApartmentUi.ps1 -OutputDirectory (Join-Path $repository 'local/planning-import')
        if($LASTEXITCODE -ne 0){throw 'Apartment rental UI extraction failed'}
        & powershell -NoProfile -NonInteractive -ExecutionPolicy Bypass -File tools/Export-TenantLocations.ps1 -OutputDirectory (Join-Path $repository 'local/planning-import')
        if($LASTEXITCODE -ne 0){throw 'Tenant location extraction failed'}
        & powershell -NoProfile -NonInteractive -ExecutionPolicy Bypass -File tools/Export-RentalSettings.ps1 -OutputDirectory (Join-Path $repository 'local/planning-import')
        if($LASTEXITCODE -ne 0){throw 'Rental settings extraction failed'}
        & powershell -NoProfile -NonInteractive -ExecutionPolicy Bypass -File tools/Export-Vendors.ps1 -OutputDirectory (Join-Path $repository 'local/vendor-research')
        if($LASTEXITCODE -ne 0){throw 'Vendor/door/dialogue extraction failed'}
        & dotnet run --project tests/OSHelp.Verify/OSHelp.Verify.csproj -c Release --no-build
        if($LASTEXITCODE -ne 0){throw 'Verification failed'}
    }
    if($Smoke){
        $capture=Join-Path $repository ('local/ui-smoke-'+(Get-Date -Format 'yyyyMMdd-HHmmss'))
        $testData=Join-Path $repository 'local/ui-smoke-data'
        & (Join-Path $PSScriptRoot 'Test-PublishedApp.ps1') -Executable (Join-Path $preview 'OSHelp.exe') -Repository $repository -GameRoot (Split-Path $repository -Parent) -DataDirectory $testData -CaptureDirectory $capture
    }
    'Preview ready: '+(Join-Path $preview 'OSHelp.exe')
    'Stable release is unchanged. Publish it with tools/Deploy.ps1.'
} finally { Pop-Location }
