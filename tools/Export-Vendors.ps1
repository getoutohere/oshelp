param(
    [string]$GameRoot=(Split-Path $PSScriptRoot -Parent|Split-Path -Parent),
    [string]$OutputDirectory=(Join-Path (Split-Path $PSScriptRoot -Parent) 'local/vendor-research')
)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'Inspect-Obenseuer.ps1') -FunctionsOnly -GameRoot $GameRoot -OutputDirectory $OutputDirectory

# Outputs.outputCustomEvent was added after these scenes were serialized.
# Use the legacy data layout, and validate every complete record's byte boundary.
$genericRead=${function:Read-Value}
$genericFields=${function:Get-SerializedFields};$fieldCache=@{}
function Get-SerializedFields([Type]$Type){if(-not $fieldCache.ContainsKey($Type.FullName)){$fieldCache[$Type.FullName]=@(& $genericFields $Type)};$fieldCache[$Type.FullName]}
function Read-Value([IO.BinaryReader]$Reader,[Type]$Type) {
    if($Type.FullName -eq 'UnityEngine.Vector3Int'){return [ordered]@{x=$Reader.ReadInt32();y=$Reader.ReadInt32();z=$Reader.ReadInt32()}}
    for($base=$Type;$base;$base=$base.BaseType){if($base.FullName -eq 'System.Delegate'){return $null}}
    if($Type.FullName -eq 'Outputs.outputCustomEvent') {
        return $null
    }
    & $genericRead $Reader $Type
}
$scriptMap=@{};$fieldsByClass=@{};$types=@{}
$names=@('Trade','TradeSpecial','FurnitureShop','TrainShop','MarketShop','DoorChangelevel','Door','DoorSchedule','Logic_openingTimes','RelayWeekdays','Relay','Character','NPCInfo','info_NPCSpawn')
foreach($name in $names){$types[$name]=$assembly.GetType($name);$fieldsByClass[$name]=@(Get-SerializedFields $types[$name])}
$scripts=[UnityResearchFile]::new((Join-Path $dataDirectory 'globalgamemanagers.assets'))
foreach($o in $scripts.Objects){if($o.ClassId -ne 115){continue};$r=$scripts.Reader($o);$null=[UnityResearchFile]::String($r);$null=$r.ReadInt32();$null=$r.ReadBytes(16);$class=[UnityResearchFile]::String($r);$r.Dispose();if($class -in $names){$scriptMap[$o.Id]=$class}}
$managers=[UnityResearchFile]::new((Join-Path $dataDirectory 'globalgamemanagers'));$r=$managers.Reader(@($managers.Objects|Where-Object ClassId -eq 141)[0]);$sceneCount=[UnityResearchFile]::Count($r);$scenes=@()
for($i=0;$i -lt $sceneCount;$i++){$scenes+=,[UnityResearchFile]::String($r)};$r.Dispose();$managers=$null;$scripts=$null
$records=[Collections.Generic.List[object]]::new();$scanned=[Collections.Generic.List[object]]::new()
$stop=@{Character='LastName';NPCInfo='character';FurnitureShop='savedTimeAndDay';TrainShop='savedTimeAndDay';MarketShop='owner';DoorChangelevel='DontSyncLocks';Door='BypassLock'}
function GameObjectInfo($asset,[long]$id) {
    $r=$asset.Reader($asset.Object($id));$count=[UnityResearchFile]::Count($r);$components=@()
    for($i=0;$i -lt $count;$i++){$null=$r.ReadInt32();$components+=,$r.ReadInt64()};$layer=$r.ReadInt32();$name=[UnityResearchFile]::String($r);$tag=$r.ReadUInt16();$active=$r.ReadBoolean();$r.Dispose()
    $tr=$asset.Reader($asset.Object($components[0]));$tr.BaseStream.Position+=52;$count=[UnityResearchFile]::Count($tr);$tr.BaseStream.Position+=12*$count;$null=$tr.ReadInt32();$parent=$tr.ReadInt64();$tr.Dispose()
    $parentGo=0L;if($parent -ne 0){$p=$asset.Reader($asset.Object($parent));$null=$p.ReadInt32();$parentGo=$p.ReadInt64();$p.Dispose()}
    [ordered]@{id=$id;name=$name;active=$active;parent=$parentGo;components=$components}
}
$files=@(Get-ChildItem -LiteralPath $dataDirectory -File|Where-Object {$_.Name -match '\.assets$|^level\d+$'})
foreach($file in $files) {
    $asset=[UnityResearchFile]::new($file.FullName);$scanned.Add([ordered]@{file=$file.Name;objects=$asset.Objects.Count})
    $scene='';if($file.Name -match '^level(\d+)$'){$scene=[IO.Path]::GetFileNameWithoutExtension($scenes[[int]$Matches[1]])}
    $gos=@{}
    foreach($o in $asset.FindScripts([long[]]@($scriptMap.Keys))) {
        $r=$asset.Reader($o);$null=$r.ReadInt32();$go=$r.ReadInt64();$enabled=$r.ReadBoolean();$r.BaseStream.Position=$o.Position+20;$class=$scriptMap[$r.ReadInt64()];$r.Dispose()
        if($class -ne 'Character' -and (-not $scene -or $scene -match '^Test|^Under Map|^Refugee')){continue}
        $stream=[IO.MemoryStream]::new($asset.Data,[int]$o.Position,[int]$o.Size,$false);$r=[IO.BinaryReader]::new($stream);$r.BaseStream.Position=28;$name=[UnityResearchFile]::String($r);$fields=[ordered]@{}
        foreach($f in $fieldsByClass[$class]) {
            if($class -eq 'Relay' -and $r.BaseStream.Position -eq $o.Size -and $f.Name -in @('requiredTaskItems','requiredObjectives','requiredDialogueVariables','disableSaving','preventNPCSceneChange')){break}
            try{$fields[$f.Name]=Read-Value $r $f.FieldType}catch{throw "$($file.Name):$($o.Id) $class field $($f.Name), byte $($r.BaseStream.Position)/$($o.Size): $_"}
            if($stop[$class] -eq $f.Name){break}
        }
        if(-not $stop.ContainsKey($class) -and $r.BaseStream.Position -ne $o.Size){throw "Byte boundary mismatch: $($file.Name):$($o.Id) $class read $($r.BaseStream.Position), expected $($o.Size)"};$r.Dispose()
        $hierarchy=@();$goIds=@();$active=$true;$current=$go
        for($depth=0;$depth -lt 60 -and $current -ne 0;$depth++){if(-not $gos.ContainsKey($current)){$gos[$current]=GameObjectInfo $asset $current};$g=$gos[$current];$hierarchy+=,$g['name'];$goIds+=,$current;$active=$active -and $g['active'];$current=$g['parent']}
        $records.Add([ordered]@{file=$file.Name;scene=$scene;id=$o.Id;class=$class;gameObject=$go;enabled=$enabled;active=$active;hierarchy=($hierarchy -join ' -> ');ancestors=$goIds;fields=$fields;externalFiles=@($asset.ExternalFiles)})
    }
    $asset=$null;[GC]::Collect()
}
Write-Json @($records.ToArray()) 'vendor-records.json'
Write-Json @($scanned.ToArray()) 'vendor-scan.json'
Write-Json $scenes 'vendor-scenes.json'
$records|Group-Object {$_['class']}|ForEach-Object { $_.Name+': '+$_.Count }
& powershell -NoProfile -NonInteractive -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'Export-VendorExtras.ps1') -GameRoot $GameRoot -OutputDirectory $OutputDirectory
if($LASTEXITCODE -ne 0){throw 'Vendor supplemental extraction failed'}
& powershell -NoProfile -NonInteractive -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'Export-VendorLabels.ps1') -GameRoot $GameRoot -OutputDirectory $OutputDirectory
if($LASTEXITCODE -ne 0){throw 'Vendor label extraction failed'}
$sourceFiles=@('Managed/Assembly-CSharp.dll','Managed/Assembly-CSharp-firstpass.dll','globalgamemanagers')+@($files.Name)
$hashes=@($sourceFiles|Select-Object -Unique|ForEach-Object {[ordered]@{file=$_;hash=(Get-FileHash -LiteralPath (Join-Path $dataDirectory $_) -Algorithm SHA256).Hash}})
Write-Json $hashes 'vendor-inputs.json'
