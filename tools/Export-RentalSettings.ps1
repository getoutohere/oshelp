param(
    [string]$GameRoot=(Split-Path $PSScriptRoot -Parent|Split-Path -Parent),
    [string]$OutputDirectory=(Join-Path (Split-Path $PSScriptRoot -Parent) 'local/rent-research')
)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'Inspect-Obenseuer.ps1') -FunctionsOnly -GameRoot $GameRoot -OutputDirectory $OutputDirectory
$scripts=[UnityResearchFile]::new((Join-Path $dataDirectory 'globalgamemanagers.assets'));$scriptIds=@()
foreach($object in $scripts.Objects){if($object.ClassId -eq 115){$r=$scripts.Reader($object);if([UnityResearchFile]::String($r) -eq 'DifficultyController'){$scriptIds+=,$object.Id}}}
$asset=[UnityResearchFile]::new((Join-Path $dataDirectory 'level0'));$presets=@{};$payments=@{}
foreach($object in $asset.FindScripts([long[]]$scriptIds)){
    $r=$asset.Reader($object);$r.BaseStream.Position=$object.Position+28;$null=[UnityResearchFile]::String($r);$fields=@{}
    foreach($field in Get-SerializedFields ($assembly.GetType('DifficultyController'))){$fields[$field.Name]=Read-Value $r $field.FieldType}
    if($r.BaseStream.Position -ne $object.Position+$object.Size){throw 'Difficulty record byte length mismatch'}
    foreach($preset in $fields['difficulties']){$presets[$preset['difficultyName']]=[int]$preset['rentIncome'];$payments[$preset['difficultyName']]=[int]$preset['rentPayments']}
}
if($presets.Count -eq 0){throw 'No rental income presets found'}
$apartment=$assembly.GetType('TenementApartment');$staticIl=[ResearchIL]::Read($apartment.TypeInitializer);$valueIl=[ResearchIL]::Read($apartment.GetMethod('GetValue',$methodFlags))
if(($staticIl -join "`n") -notmatch 'ldc.i4.s 20\r?\n.*stsfld TenementApartment::Int32 ocDivider' -or ($staticIl -join "`n") -notmatch 'ldc.i4 200\r?\n.*stsfld TenementApartment::Int32 rmDivider' -or ($valueIl -join "`n") -notmatch 'IL_0000\s+ldc.i4 5000'){throw 'Rental value constants changed; review IL before importing'}
$inputs=@('Managed/Assembly-CSharp.dll','globalgamemanagers.assets','level0')|ForEach-Object {[ordered]@{file=$_;hash=(Get-FileHash -LiteralPath (Join-Path $dataDirectory $_) -Algorithm SHA256).Hash}}
$modes=@{};foreach($field in $assembly.GetType('RentPayments').GetFields([Reflection.BindingFlags]'Public,Static')){$modes[[string]$field.GetRawConstantValue()]=$field.Name}
Write-Json ([ordered]@{formatVersion=2;baseValue=5000;ocDivider=20;rmDivider=200;presetIncomeEnums=$presets;presetPaymentModes=$payments;paymentModeNames=$modes;inputs=@($inputs)}) 'rental-settings.json'
'Rental income presets: '+($presets.Keys -join ', ')
