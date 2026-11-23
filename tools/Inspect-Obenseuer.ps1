param(
    [string]$GameRoot = (Split-Path $PSScriptRoot -Parent | Split-Path -Parent),
    [string]$OutputDirectory = (Join-Path (Split-Path $PSScriptRoot -Parent) 'docs/evidence'),
    [switch]$ScanAllAssets,
    [switch]$RulesOnly,
    [switch]$FunctionsOnly
)

$ErrorActionPreference = 'Stop'
$GameRoot = (Resolve-Path -LiteralPath $GameRoot).Path
$dataDirectory = Join-Path $GameRoot 'Obenseuer_Data'
$managedDirectory = Join-Path $dataDirectory 'Managed'
$null = New-Item -ItemType Directory -Force -Path $OutputDirectory
Add-Type -Path (Join-Path $PSScriptRoot 'UnitySerializedFile.cs')

# Reflection-only inspection: never loads the game for execution or starts Unity.
$resolveHandler = [System.ResolveEventHandler] {
    param($sender, $eventArgs)
    $dependencyName = ([Reflection.AssemblyName]::new($eventArgs.Name)).Name + '.dll'
    $dependencyPath = Join-Path $managedDirectory $dependencyName
    if (Test-Path -LiteralPath $dependencyPath) { [Reflection.Assembly]::ReflectionOnlyLoadFrom($dependencyPath) }
}
[AppDomain]::CurrentDomain.add_ReflectionOnlyAssemblyResolve($resolveHandler)
$assembly = [Reflection.Assembly]::ReflectionOnlyLoadFrom((Join-Path $managedDirectory 'Assembly-CSharp.dll'))
$fieldFlags = [Reflection.BindingFlags]'Public,NonPublic,Instance,DeclaredOnly'
$methodFlags = [Reflection.BindingFlags]'Public,NonPublic,Instance,Static,DeclaredOnly'

function Get-SerializedFields([Type]$Type) {
    if ($Type.BaseType -and $Type.BaseType.FullName -notin @('UnityEngine.ScriptableObject', 'UnityEngine.MonoBehaviour', 'System.Object', 'System.ValueType')) {
        Get-SerializedFields $Type.BaseType
    }
    $Type.GetFields($fieldFlags) | Where-Object {
        -not $_.IsStatic -and -not $_.IsInitOnly -and -not $_.IsNotSerialized -and
        -not ($_.FieldType.IsGenericType -and $_.FieldType.GetGenericTypeDefinition().FullName -ne 'System.Collections.Generic.List`1') -and
        ($_.IsPublic -or @($_.GetCustomAttributesData() | Where-Object { $_.AttributeType.Name -eq 'SerializeField' }).Count -gt 0)
    } | Sort-Object MetadataToken
}

function Read-Value([IO.BinaryReader]$Reader, [Type]$Type) {
    if ($Type.FullName -eq 'UnityEngine.Rect') {
        return [ordered]@{ x=$Reader.ReadSingle(); y=$Reader.ReadSingle(); width=$Reader.ReadSingle(); height=$Reader.ReadSingle() }
    }
    if ($Type.IsEnum -or $Type.FullName -eq 'System.Int32') { return $Reader.ReadInt32() }
    if ($Type.FullName -eq 'System.Int64') { return $Reader.ReadInt64() }
    if ($Type.FullName -eq 'System.Single') { return $Reader.ReadSingle() }
    if ($Type.FullName -eq 'System.Boolean') { $value = $Reader.ReadBoolean(); [UnityResearchFile]::Align($Reader); return $value }
    if ($Type.FullName -eq 'System.String') { return [UnityResearchFile]::String($Reader) }
    for ($base = $Type; $base; $base = $base.BaseType) {
        if ($base.FullName -eq 'UnityEngine.Object') {
            return [ordered]@{ fileId = $Reader.ReadInt32(); pathId = $Reader.ReadInt64() }
        }
    }
    if ($Type.IsArray -or ($Type.IsGenericType -and $Type.GetGenericTypeDefinition().FullName -eq 'System.Collections.Generic.List`1')) {
        $elementType = if ($Type.IsArray) { $Type.GetElementType() } else { $Type.GetGenericArguments()[0] }
        $count = [UnityResearchFile]::Count($Reader)
        $values = @()
        for ($i = 0; $i -lt $count; $i++) { $values += ,(Read-Value $Reader $elementType) }
        return ,$values
    }
    $fields = @(Get-SerializedFields $Type)
    if ($fields.Count -eq 0) { throw "Unsupported serialized type $($Type.FullName)" }
    $result = [ordered]@{}
    foreach ($field in $fields) { $result[$field.Name] = Read-Value $Reader $field.FieldType }
    return $result
}

function Write-Json($Value, [string]$Name) {
    $Value | ConvertTo-Json -Depth 60 | Set-Content -LiteralPath (Join-Path $OutputDirectory $Name) -Encoding UTF8
}

if ($FunctionsOnly) { return }

$scriptFile = [UnityResearchFile]::new((Join-Path $dataDirectory 'globalgamemanagers.assets'))
$scripts = @{}
foreach ($object in $scriptFile.Objects) {
    if ($object.ClassId -eq 115) {
        $reader = $scriptFile.Reader($object)
        $scripts[$object.Id] = [UnityResearchFile]::String($reader)
        $reader.Dispose()
    }
}
$scriptFile = $null
$wanted = @('TenementResident', 'TenementUpgrade', 'TenementUpgradeGroup', 'TenementApartmentType', 'TenementContractor')
$records = [Collections.Generic.List[object]]::new()
$files = if ($RulesOnly) {
    @()
} elseif ($ScanAllAssets) {
    Get-ChildItem -LiteralPath $dataDirectory -File | Where-Object { $_.Name -match '\.assets$|^level\d+$' }
} else {
    Get-Item -LiteralPath (Join-Path $dataDirectory 'resources.assets'), (Join-Path $dataDirectory 'sharedassets0.assets')
}
$scanned = @()
foreach ($file in $files) {
    $asset = [UnityResearchFile]::new($file.FullName)
    $scanned += [ordered]@{ name = $file.Name; unityVersion = $asset.UnityVersion; objectCount = $asset.Objects.Count }
    foreach ($object in $asset.Objects) {
        if ($object.ClassId -ne 114) { continue }
        $reader = $asset.Reader($object)
        try {
            $reader.BaseStream.Position = $object.Position + 16
            $scriptFileId = $reader.ReadInt32(); $scriptPathId = $reader.ReadInt64()
            if ($scriptFileId -le 0 -or $scriptFileId -gt $asset.ExternalFiles.Count) { continue }
            if ($asset.ExternalFiles[$scriptFileId - 1] -notmatch 'globalgamemanagers\.assets$') { continue }
            $class = $scripts[$scriptPathId]
            if ($class -notin $wanted) { continue }
            $name = [UnityResearchFile]::String($reader)
            $bodyOffset = $reader.BaseStream.Position
            $fields = [ordered]@{}
            foreach ($field in (Get-SerializedFields $assembly.GetType($class, $true))) {
                $fields[$field.Name] = Read-Value $reader $field.FieldType
            }
            if ($reader.BaseStream.Position -ne $object.Position + $object.Size) {
                throw "Field schema did not consume object exactly: $($file.Name):$($object.Id) $class $name; $($reader.BaseStream.Position - $object.Position) / $($object.Size) bytes"
            }
            $records.Add([ordered]@{
                sourceFile = $file.Name; pathId = $object.Id; class = $class; name = $name
                objectOffset = $object.Position; bodyOffset = $bodyOffset; byteLength = $object.Size
                fields = $fields; externalFiles = $asset.ExternalFiles
            })
        } finally { $reader.Dispose() }
    }
    $asset = $null
    [GC]::Collect()
}
if (-not $RulesOnly) { Write-Json @($records) 'catalog.json' }

$selectedMethods = [ordered]@{
    'TenementResident' = @('CheckRequirements', 'CantUpgradeApartment', 'UpgradeMeetsSizeRequirement', 'UpgradeMeetsRequirements', 'GetMonthlyRent', 'StartRent')
    'TenementUpgrade' = @('CanUpgrade', 'IsUpgraded', 'GetRequiredTimeHours', 'GetPriceStartOC', 'GetPriceFinalRM', 'TakeRequiredItemsAndMoney', 'GetTier')
    'TenementContractor' = @('CanUpgrade')
    'TenementApartment' = @('IsApartmentAvailable', 'GetScaledValue')
    'TenementController' = @('CanRentApartment', 'AddResident', 'RentApartment', 'MarkUpgrade', 'RemoveSupersededMarkedUpgrades', 'RemoveRequiredItems')
    'DifficultyInfo' = @('GetRenovationResourceCost', 'GetRenovationCost', 'GetRenovationSpeed', 'ApplyMultiplier', 'GetMultiplierValue')
    'ApartmentRentUI' = @('UpdateCantRentButton', 'OnRentButtonPressed', 'ShowRentButton')
    'TenementPanelUI' = @('PlayerCanRentApartment', 'PlayerHasRequiredItem')
    'UpgradePanel' = @('IsLocked')
    'CharacterRentDatabase' = @('Start', 'ConstructCharacterRentDatabase')
}
$il = [Collections.Generic.List[string]]::new()
foreach ($typeName in $selectedMethods.Keys) {
    $type = $assembly.GetType($typeName, $true)
    foreach ($method in ($type.GetMethods($methodFlags) | Where-Object { $_.Name -in $selectedMethods[$typeName] } | Sort-Object MetadataToken)) {
        $il.Add(''); $il.Add($typeName + '::' + $method.ToString() + ' [0x' + $method.MetadataToken.ToString('X8') + ']')
        foreach ($line in [ResearchIL]::Read($method)) { $il.Add($line) }
    }
}
$il | Set-Content -LiteralPath (Join-Path $OutputDirectory 'rules.il.txt') -Encoding UTF8
$enums = [ordered]@{}
foreach ($type in $assembly.GetTypes()) {
    if ($type.IsEnum -and ($type.FullName -like 'UpgradeType+*' -or $type.FullName -in @('TenementResident+CantRentReason', 'DefaultMultiplier'))) {
        $values = [ordered]@{}
        foreach ($field in $type.GetFields([Reflection.BindingFlags]'Public,Static')) { $values[$field.Name] = $field.GetRawConstantValue() }
        $enums[$type.FullName] = $values
    }
}
Write-Json $enums 'enums.json'
if (-not $RulesOnly) {
$manifest = [ordered]@{
    investigatedOn = '2026-11-23'; scanAllAssets = [bool]$ScanAllAssets; filesScanned = $scanned
    sourceHashes = @((@('Managed/Assembly-CSharp.dll', 'StreamingAssets/CharactersRent.json', 'StreamingAssets/Items.json', 'globalgamemanagers.assets') + @($files.Name)) | Select-Object -Unique | ForEach-Object {
        $path = Join-Path $dataDirectory $_
        [ordered]@{ file = $_; sha256 = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash; bytes = (Get-Item -LiteralPath $path).Length }
    })
    counts = @($records | Group-Object { $_['class'] } | ForEach-Object { [ordered]@{ class = $_.Name; count = $_.Count } })
}
Write-Json $manifest 'manifest.json'
$manifest.counts | ForEach-Object { '{0}: {1}' -f $_.class, $_.count }
}
