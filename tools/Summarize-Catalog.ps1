param(
    [string]$GameRoot = (Split-Path $PSScriptRoot -Parent | Split-Path -Parent),
    [string]$EvidenceDirectory = (Join-Path (Split-Path $PSScriptRoot -Parent) 'docs/evidence')
)
$ErrorActionPreference = 'Stop'
$catalog = Get-Content -LiteralPath (Join-Path $EvidenceDirectory 'catalog.json') -Raw | ConvertFrom-Json
$enums = Get-Content -LiteralPath (Join-Path $EvidenceDirectory 'enums.json') -Raw | ConvertFrom-Json
$items = Get-Content -LiteralPath (Join-Path $GameRoot 'Obenseuer_Data/StreamingAssets/Items.json') -Raw | ConvertFrom-Json
$itemIndex = @{}
foreach ($item in $items) {
    if ($itemIndex.ContainsKey([int]$item.ID)) { throw "Duplicate item ID $($item.ID)" }
    $itemIndex[[int]$item.ID] = $item.Title
}
$index = @{}
foreach ($record in $catalog) { $index[$record.sourceFile + ':' + $record.pathId] = $record }
function Resolve-Reference($Record, $Reference) {
    if ($Reference.pathId -eq 0) { return $null }
    $fileName = if ($Reference.fileId -eq 0) { $Record.sourceFile } else { Split-Path -Leaf $Record.externalFiles[$Reference.fileId - 1] }
    $resolved = $index[$fileName + ':' + $Reference.pathId]
    if (-not $resolved) { throw "Unresolved reference $fileName`:$($Reference.pathId)" }
    return $resolved
}
function Enum-Name([string]$Type, [int]$Value) {
    $definition = $enums.PSObject.Properties[$Type].Value
    $names = @($definition.PSObject.Properties | Where-Object { $_.Value -eq $Value })
    if ($names.Count -ne 1) { throw "Invalid enum $Type = $Value" }
    return $names[0].Name
}
$apartmentFields = [ordered]@{
    electricity = 'ApartmentElectricity'; waterMin = 'ApartmentWater'; heatingMin = 'ApartmentHeatingMethod'
    bathroomMin = 'BathroomType'; wallMin = 'ApartmentWallType'; balconyMin = 'BalconyType'; kitchenMin = 'KitchenType'
}
$buildingFields = [ordered]@{
    elevator = 'TenementElevator'; stairsMin = 'TenementStairs'; basementMin = 'TenementBasement'
    basementSaunaMin = 'BasementSauna'; outerWallsMin = 'OuterWalls'; courtyardMin = 'Courtyard'; mineMin = 'BasementMine'
}
$tenantLines = [Collections.Generic.List[string]]::new()
$tenantLines.Add('# Extracted resident requirements')
$tenantLines.Add('')
$tenantLines.Add('Generated from all 31 `TenementResident` definitions in the installed assets. These are asset definitions, including existing or special residents; this table does not classify dialogue recruitment availability. Nonzero minimums are listed below. Enum names are preserved from the game. See [catalog.json](catalog.json), [enums.json](enums.json), and [rules.il.txt](rules.il.txt) for raw fields and acceptance logic.')
$tenantLines.Add('')
$tenantLines.Add('| Resident and NPC ID | Accepted sizes | Apartment minimums | Building minimums | Other residents |')
$tenantLines.Add('|---|---|---|---|---|')
foreach ($record in ($catalog | Where-Object { $_.class -eq 'TenementResident' } | Sort-Object name)) {
    $f = $record.fields
    $sizes = @(); foreach ($size in @('small', 'medium', 'large')) { if ($f.PSObject.Properties[$size + 'Apartment'].Value) { $sizes += $size } }
    $apt = @(); foreach ($field in $apartmentFields.Keys) { if ($f.$field -ne 0) { $apt += $field + ': ' + (Enum-Name ('UpgradeType+' + $apartmentFields[$field]) $f.$field) } }
    $building = @(); foreach ($field in $buildingFields.Keys) { if ($f.$field -ne 0) { $building += $field + ': ' + (Enum-Name ('UpgradeType+' + $buildingFields[$field]) $f.$field) } }
    if ($apt.Count -eq 0) { $apt = @('None') }; if ($building.Count -eq 0) { $building = @('None') }
    $family = @($f.otherResidents | ForEach-Object { $_.name }) -join ', '
    $tenantLines.Add('| ' + $record.name + ' (' + $f.resident.ID + ') | ' + ($sizes -join ', ') + ' | ' + ($apt -join '; ') + ' | ' + ($building -join '; ') + ' | ' + $family + ' |')
}
$tenantLines | Set-Content -LiteralPath (Join-Path $EvidenceDirectory 'resident-requirements.md') -Encoding UTF8

$upgradeLines = [Collections.Generic.List[string]]::new()
$upgradeLines.Add('# Extracted upgrade material requirements')
$upgradeLines.Add('')
$upgradeLines.Add('All 131 upgrade definitions, including zero-cost starting states. Quantities, OC deposits, RM final payments, and hours are raw asset values before difficulty adjustment. A prerequisite is an explicit asset reference, not an inferred previous tier. Names in the materials column are resolved by item ID from `Items.json`. See [catalog.json](catalog.json) and [rules.il.txt](rules.il.txt).')
$upgradeLines.Add('')
$upgradeLines.Add('| Upgrade | Materials | Prerequisites | Start OC | Final RM | Base hours |')
$upgradeLines.Add('|---|---|---|---:|---:|---:|')
$referenced = @{}
foreach ($group in ($catalog | Where-Object { $_.class -eq 'TenementUpgradeGroup' })) {
    foreach ($ref in $group.fields.updateGroup) { $resolved = Resolve-Reference $group $ref; $referenced[$resolved.sourceFile + ':' + $resolved.pathId] = $true }
}
$materialLines = 0
foreach ($record in ($catalog | Where-Object { $_.class -eq 'TenementUpgrade' } | Sort-Object { $_.fields.addressableAssetPath })) {
    $f = $record.fields
    if (-not $referenced.ContainsKey($record.sourceFile + ':' + $record.pathId)) { throw "Upgrade outside all groups: $($record.name)" }
    $materials = @()
    foreach ($line in $f.requiredItems) {
        if (-not $itemIndex.ContainsKey([int]$line.Item.ID)) { throw "Unresolved item $($line.Item.ID)" }
        if ($line.itemAmount -lt 0) { throw 'Negative material amount' }
        $materials += $line.itemAmount.ToString() + ' ' + $itemIndex[[int]$line.Item.ID]
        $materialLines++
    }
    $prerequisites = @($f.requiredUpgrades | ForEach-Object { (Resolve-Reference $record $_).name })
    $upgradeLines.Add('| ' + $record.name + ' | ' + ($materials -join '; ') + ' | ' + ($prerequisites -join '; ') + ' | ' + $f.priceStartOC + ' | ' + $f.priceFinalRM + ' | ' + $f.requiredTimeHours + ' |')
}
$upgradeLines | Set-Content -LiteralPath (Join-Path $EvidenceDirectory 'upgrade-materials.md') -Encoding UTF8
[pscustomobject]@{ residents = @($catalog | Where-Object { $_.class -eq 'TenementResident' }).Count; upgrades = @($catalog | Where-Object { $_.class -eq 'TenementUpgrade' }).Count; materialLines = $materialLines; unresolvedReferences = 0; upgradesOutsideGroups = 0 } |
    ConvertTo-Json | Set-Content -LiteralPath (Join-Path $EvidenceDirectory 'validation.json') -Encoding UTF8
'Generated resident and upgrade tables; all material IDs and upgrade references resolved.'
