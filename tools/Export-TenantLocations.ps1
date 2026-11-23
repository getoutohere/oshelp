param(
    [string]$GameRoot=(Split-Path $PSScriptRoot -Parent|Split-Path -Parent),
    [string]$OutputDirectory=(Join-Path (Split-Path $PSScriptRoot -Parent) 'local/npc-research')
)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'Inspect-Obenseuer.ps1') -FunctionsOnly -GameRoot $GameRoot -OutputDirectory $OutputDirectory
$residentCatalog=Get-Content -LiteralPath (Join-Path (Split-Path $PSScriptRoot -Parent) 'docs/evidence/catalog.json') -Raw|ConvertFrom-Json
$tenantIds=@($residentCatalog|Where-Object class -eq 'TenementResident'|ForEach-Object {$_.fields.resident.ID})
$scripts=[UnityResearchFile]::new((Join-Path $dataDirectory 'globalgamemanagers.assets')); $scriptNames=@{}
foreach($o in $scripts.Objects){if($o.ClassId -eq 115){$r=$scripts.Reader($o);$name=[UnityResearchFile]::String($r);if($name -in @('Character','NPCInfo','info_NPCSpawn')){$scriptNames[$o.Id]=$name}}}
$managers=[UnityResearchFile]::new((Join-Path $dataDirectory 'globalgamemanagers'))
$build=@($managers.Objects|Where-Object ClassId -eq 141)[0];$r=$managers.Reader($build);$sceneCount=[UnityResearchFile]::Count($r);$sceneNames=@()
for($i=0;$i -lt $sceneCount;$i++){$sceneNames+=,[UnityResearchFile]::String($r)}
$sceneNames|ConvertTo-Json|Set-Content -Encoding UTF8 (Join-Path $OutputDirectory 'scenes.json')
$scripts=$null;$managers=$null
$characters=@{};$candidates=[Collections.Generic.List[object]]::new()
function Hierarchy([UnityResearchFile]$Asset,[long]$GameObject){
    $names=@();$current=$GameObject
    for($depth=0;$depth -lt 20 -and $current -ne 0;$depth++){
        $go=$Asset.Reader($Asset.Object($current));$count=$go.ReadInt32();$null=$go.ReadInt32();$transformId=$go.ReadInt64();$go.BaseStream.Position+=12*($count-1)+4;$names+=[UnityResearchFile]::String($go)
        $transform=$Asset.Object($transformId);$tr=$Asset.Reader($transform);$tr.BaseStream.Position=$transform.Position+52;$children=[UnityResearchFile]::Count($tr);$tr.BaseStream.Position+=12*$children;$null=$tr.ReadInt32();$parent=$tr.ReadInt64()
        if($parent -eq 0){break};$p=$Asset.Reader($Asset.Object($parent));$null=$p.ReadInt32();$current=$p.ReadInt64()
    }
    $names -join ' -> '
}
$files=Get-ChildItem -LiteralPath $dataDirectory -File|Where-Object {$_.Name -match '\.assets$|^level\d+$'}
foreach($file in $files){
    $asset=[UnityResearchFile]::new($file.FullName)
    foreach($o in $asset.FindScripts([long[]]@($scriptNames.Keys))){
        $r=$asset.Reader($o);$null=$r.ReadInt32();$go=$r.ReadInt64();$r.BaseStream.Position=$o.Position+20;$scriptId=$r.ReadInt64();$class=$scriptNames[$scriptId];$name=[UnityResearchFile]::String($r);$values=@{}
        foreach($field in Get-SerializedFields ($assembly.GetType($class))){$values[$field.Name]=Read-Value $r $field.FieldType;if(($class -eq 'Character' -and $field.Name -eq 'ID') -or ($class -eq 'NPCInfo' -and $field.Name -eq 'character')){break}}
        if($r.BaseStream.Position -gt $o.Position+$o.Size){throw 'NPC field read exceeded object boundary'}
        if($class -eq 'Character'){$characters[$file.Name+':'+$o.Id]=[int]$values['ID'];continue}
        if($file.Name -notmatch '^level(\d+)$'){continue};$index=[int]$Matches[1];if($index -ge $sceneNames.Count){throw 'Scene index outside BuildSettings'}
        if($class -eq 'info_NPCSpawn' -and $r.BaseStream.Position -ne $o.Position+$o.Size){throw 'NPC spawn schema byte mismatch'}
        $id=if($class -eq 'info_NPCSpawn'){[int]$values['_NPC']['ID']}else{$null}
        if($class -eq 'info_NPCSpawn' -and $id -notin $tenantIds){continue}
        $scene=[IO.Path]::GetFileNameWithoutExtension($sceneNames[$index]);if($scene -match '^Test|^Under Map|^Refugee'){continue}
        $candidates.Add([ordered]@{npcId=$id;scene=$scene;scenePath=$sceneNames[$index];sourceFile=$file.Name;pathId=$o.Id;class=$class;name=$name;hierarchy=(Hierarchy $asset $go);character=$values['character'];externalFiles=$asset.ExternalFiles})
    }
    $asset=$null;[GC]::Collect()
}
$result=foreach($entry in $candidates){
    if($entry['class'] -eq 'NPCInfo'){
        $pointer=$entry['character'];if($pointer['pathId'] -eq 0){continue}
        $file=if($pointer['fileId'] -eq 0){$entry['sourceFile']}else{[IO.Path]::GetFileName($entry['externalFiles'][$pointer['fileId']-1])}
        $entry['npcId']=$characters[$file+':'+$pointer['pathId']];$entry['characterSourceFile']=$file
    }
    if($entry['npcId'] -in $tenantIds){$entry.Remove('character');$entry.Remove('externalFiles');$entry}
}
Write-Json @($result) 'tenant-locations.json'
$sourceFiles=@('Managed/Assembly-CSharp.dll','globalgamemanagers','globalgamemanagers.assets')+@($result|ForEach-Object {$_['sourceFile'];if($_['characterSourceFile']){$_['characterSourceFile']}})
$sourceHashes=@($sourceFiles|Select-Object -Unique|ForEach-Object {[ordered]@{file=$_;hash=(Get-FileHash -LiteralPath (Join-Path $dataDirectory $_) -Algorithm SHA256).Hash}})
Write-Json $sourceHashes 'tenant-location-inputs.json'
$result|Group-Object {$_['npcId']}|ForEach-Object {($_.Name+': '+(($_.Group|ForEach-Object {$_['scene']+' ['+$_['class']+'] '+$_['hierarchy']}) -join ' | '))}
