param(
    [string]$GameRoot=(Split-Path $PSScriptRoot -Parent|Split-Path -Parent),
    [string]$OutputDirectory=(Join-Path (Split-Path $PSScriptRoot -Parent) 'local/bazaar-store')
)
# Research harness for the documented installed build, not a general vendor importer.
# Relay custom-event tails are unsupported; only the first UnityEvent prefix is evidence.
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'Inspect-Obenseuer.ps1') -FunctionsOnly -GameRoot $GameRoot -OutputDirectory $OutputDirectory
$inputs=@('globalgamemanagers','globalgamemanagers.assets','level77','level112','Managed/Assembly-CSharp.dll')|ForEach-Object {[ordered]@{file=$_;hash=(Get-FileHash -LiteralPath (Join-Path $dataDirectory $_) -Algorithm SHA256).Hash}}
Write-Json @($inputs) 'inputs.json'
$names=@('DoorSchedule','Logic_openingTimes','DoorChangelevel','Trade');$scripts=[UnityResearchFile]::new((Join-Path $dataDirectory 'globalgamemanagers.assets'));$ids=@{};$allIds=@{}
foreach($o in $scripts.Objects){if($o.ClassId -eq 115){$r=$scripts.Reader($o);$null=[UnityResearchFile]::String($r);$null=$r.ReadInt32();$null=$r.ReadBytes(16);$name=[UnityResearchFile]::String($r);$allIds[$o.Id]=$name;if($name -in $names){$ids[$o.Id]=$name}}}
function Ancestry($a,[long]$id){$parts=@();for($i=0;$i -lt 20 -and $id -ne 0;$i++){$go=$a.Reader($a.Object($id));$count=$go.ReadInt32();$null=$go.ReadInt32();$trId=$go.ReadInt64();$go.BaseStream.Position+=12*($count-1)+4;$parts+=[UnityResearchFile]::String($go);$tr=$a.Reader($a.Object($trId));$tr.BaseStream.Position+=52;$count=$tr.ReadInt32();$tr.BaseStream.Position+=12*$count+4;$parent=$tr.ReadInt64();if($parent -eq 0){break};$p=$a.Reader($a.Object($parent));$null=$p.ReadInt32();$id=$p.ReadInt64()};$parts -join ' -> '}
$records=@()
foreach($file in @('level77','level112')){
    $asset=[UnityResearchFile]::new((Join-Path $dataDirectory $file))
    foreach($o in $asset.FindScripts([long[]]@($ids.Keys))){$r=$asset.Reader($o);$null=$r.ReadInt32();$go=$r.ReadInt64();$r.BaseStream.Position=$o.Position+20;$class=$ids[$r.ReadInt64()];$null=[UnityResearchFile]::String($r);$fields=[ordered]@{}
        foreach($f in Get-SerializedFields ($assembly.GetType($class))){try{$fields[$f.Name]=Read-Value $r $f.FieldType}catch{throw "$file $class $($o.Id) field $($f.Name) at $($r.BaseStream.Position-$o.Position): $_"};if(($class -eq 'Trade' -and $f.Name -eq 'openingTimes') -or ($class -eq 'DoorChangelevel' -and $f.Name -eq 'OtherEntrypoint')){break}}
        if($class -in @('DoorSchedule','Logic_openingTimes') -and $r.BaseStream.Position -ne $o.Position+$o.Size){throw "Schema mismatch: $class $($o.Id)"}
        $records+=,[ordered]@{file=$file;object=$o.Id;class=$class;gameObject=$go;hierarchy=(Ancestry $asset $go);fields=$fields}
    }
}
Write-Json $records 'records.json'
$targets=@{}
function Walk([long]$transformId){$t=$asset.Reader($asset.Object($transformId));$null=$t.ReadInt32();$go=$t.ReadInt64();$targets[$go]=$true;$t.BaseStream.Position=$asset.Object($transformId).Position+52;$count=$t.ReadInt32();$children=@();for($j=0;$j -lt $count;$j++){$null=$t.ReadInt32();$children+=,$t.ReadInt64()};foreach($child in $children){Walk $child}}
$goId=76663L
for($n=0;$n -lt 2;$n++){$r=$asset.Reader($asset.Object($goId));$null=$r.ReadInt32();$null=$r.ReadInt32();$tr=$r.ReadInt64();$r=$asset.Reader($asset.Object($tr));$r.BaseStream.Position=$asset.Object($tr).Position+52;$cnt=$r.ReadInt32();$r.BaseStream.Position+=12*$cnt+4;$parent=$r.ReadInt64();$r=$asset.Reader($asset.Object($parent));$null=$r.ReadInt32();$goId=$r.ReadInt64()}
$r=$asset.Reader($asset.Object($goId));$null=$r.ReadInt32();$null=$r.ReadInt32();Walk $r.ReadInt64()
$logic=@()
foreach($o in $asset.Objects){if($o.ClassId -ne 114){continue};$r=$asset.Reader($o);$null=$r.ReadInt32();$go=$r.ReadInt64();if(-not $targets.ContainsKey($go)){continue};$r.BaseStream.Position=$o.Position+20;$class=$allIds[$r.ReadInt64()];$null=[UnityResearchFile]::String($r);$fields=[ordered]@{};$type=$assembly.GetType($class)
    if($type -and $class -match 'Logic|Time|Trigger|Schedule|Relay|Clock') {try{foreach($f in Get-SerializedFields $type){$fields[$f.Name]=Read-Value $r $f.FieldType}}catch{$fields['decodeError']=$_.Exception.Message}}
    if($class -eq 'RelayWeekdays' -and $r.BaseStream.Position -ne $o.Position+$o.Size){throw 'RelayWeekdays byte length mismatch'}
    $logic+=,[ordered]@{object=$o.Id;class=$class;gameObject=$go;hierarchy=(Ancestry $asset $go);fields=$fields}
}
Write-Json $logic 'shop-logic.json'
foreach($typeName in @('RelayWeekdays','OpeningTimes')){$type=$assembly.GetType($typeName);$type.FullName;foreach($method in $type.GetMethods($methodFlags)){'METHOD '+$method.ToString();[ResearchIL]::Read($method)}}
$type=$assembly.GetType('Trade');foreach($method in $type.GetMethods($methodFlags)|Where-Object {$_.Name -match 'Open|Time|Delta|Interact|Use'}){'TRADE METHOD '+$method.ToString();[ResearchIL]::Read($method)}
