param([string]$GameRoot=(Split-Path $PSScriptRoot -Parent|Split-Path -Parent),[string]$OutputDirectory=(Join-Path (Split-Path $PSScriptRoot -Parent) 'local/ui-evidence'))
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'Inspect-Obenseuer.ps1') -GameRoot $GameRoot -FunctionsOnly -OutputDirectory $OutputDirectory
$type=$assembly.GetType('ApartmentInfoUI')
$scripts=[UnityResearchFile]::new((Join-Path $dataDirectory 'globalgamemanagers.assets'))
$scriptId=0L
foreach($object in $scripts.Objects){if($object.ClassId -eq 115){$reader=$scripts.Reader($object);if([UnityResearchFile]::String($reader) -eq 'ApartmentInfoUI'){$scriptId=$object.Id;break}}}
$asset=[UnityResearchFile]::new((Join-Path $dataDirectory 'level2'));$objects=@{}
foreach($object in $asset.Objects){$objects[$object.Id]=$object}
function Get-Ancestry([long]$GameObject){
    $names=@();$current=$GameObject
    for($depth=0;$depth -lt 12 -and $current -ne 0;$depth++){
        $go=$asset.Reader($objects[$current]);$count=$go.ReadInt32();$null=$go.ReadInt32();$transformId=$go.ReadInt64();$go.BaseStream.Position=$objects[$current].Position+4+12*$count+4
        $names+=[UnityResearchFile]::String($go)
        $transform=$objects[$transformId];$tr=$asset.Reader($transform);$tr.BaseStream.Position=$transform.Position+52;$children=$tr.ReadInt32();$tr.BaseStream.Position+=12*$children;$null=$tr.ReadInt32();$parent=$tr.ReadInt64()
        if($parent -eq 0){break};$parentReader=$asset.Reader($objects[$parent]);$null=$parentReader.ReadInt32();$current=$parentReader.ReadInt64()
    }
    $names -join ' -> '
}
$result=@()
foreach($object in $asset.Objects){
    if($object.ClassId -ne 114){continue}
    $reader=$asset.Reader($object);$reader.BaseStream.Position=$object.Position+4;$gameObject=$reader.ReadInt64();$reader.BaseStream.Position=$object.Position+20
    if($reader.ReadInt64() -ne $scriptId){continue}
    $null=[UnityResearchFile]::String($reader);$fields=[ordered]@{}
    foreach($field in Get-SerializedFields $type){$fields[$field.Name]=Read-Value $reader $field.FieldType}
    if($reader.BaseStream.Position -ne $object.Position+$object.Size){throw 'UI record byte length mismatch'}
    $go=$asset.Reader($objects[$gameObject]);$count=$go.ReadInt32();$go.BaseStream.Position+=12*$count+4
    $name=[UnityResearchFile]::String($go)
    $result+=[pscustomobject]@{object=$object.Id;gameObject=$gameObject;name=$name;hierarchy=(Get-Ancestry $gameObject);rentUI=$fields['rentUI'];label=$fields['apartmentName']}
}
$result|ConvertTo-Json -Depth 5|Set-Content -Encoding UTF8 (Join-Path $OutputDirectory 'apartment-info.json')
'Apartment rental UI records: '+$result.Count+' (Shop rental pointer is null)'
