param(
    [string]$GameRoot=(Split-Path $PSScriptRoot -Parent|Split-Path -Parent),
    [string]$OutputDirectory=(Join-Path (Split-Path $PSScriptRoot -Parent) 'local/vendor-research')
)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'Inspect-Obenseuer.ps1') -FunctionsOnly -GameRoot $GameRoot -OutputDirectory $OutputDirectory
$genericFields=${function:Get-SerializedFields};$fieldCache=@{}
function Get-SerializedFields([Type]$Type){if(-not $fieldCache.ContainsKey($Type.FullName)){$fieldCache[$Type.FullName]=@(& $genericFields $Type)};$fieldCache[$Type.FullName]}
$genericRead=${function:Read-Value}
function Read-Value([IO.BinaryReader]$Reader,[Type]$Type){if($Type.FullName -eq 'Outputs.outputCustomEvent'){return $null};& $genericRead $Reader $Type}
$firstpass=[Reflection.Assembly]::ReflectionOnlyLoadFrom((Join-Path $managedDirectory 'Assembly-CSharp-firstpass.dll'))
$scripts=[UnityResearchFile]::new((Join-Path $dataDirectory 'globalgamemanagers.assets'));$scriptTypes=@{}
foreach($o in $scripts.Objects){if($o.ClassId -ne 115){continue};$r=$scripts.Reader($o);$null=[UnityResearchFile]::String($r);$null=$r.ReadInt32();$null=$r.ReadBytes(16);$class=[UnityResearchFile]::String($r);$namespace=[UnityResearchFile]::String($r);if($class -in @('GameObjectStateSave','VendingMachine','InteractableTalk','RelayDialogue','FadeGameObject')){$scriptTypes[$o.Id]=$assembly.GetType($class)};if($class -eq 'DialogueDatabase'){$scriptTypes[$o.Id]=$firstpass.GetType($namespace+'.'+$class)}}
$managers=[UnityResearchFile]::new((Join-Path $dataDirectory 'globalgamemanagers'));$r=$managers.Reader(@($managers.Objects|Where-Object ClassId -eq 141)[0]);$count=[UnityResearchFile]::Count($r);$scenes=@();for($i=0;$i -lt $count;$i++){$scenes+=,[IO.Path]::GetFileNameWithoutExtension([UnityResearchFile]::String($r))};$r.Dispose();$managers=$null
function Hierarchy($asset,[long]$id){$names=@();$ids=@();$active=$true;for($depth=0;$depth -lt 60 -and $id -ne 0;$depth++){$o=$asset.Object($id);$r=$asset.Reader($o);$count=$r.ReadInt32();$null=$r.ReadInt32();$tr=$r.ReadInt64();$r.BaseStream.Position+=12*($count-1)+4;$names+=,[UnityResearchFile]::String($r);$null=$r.ReadUInt16();$active=$active -and $r.ReadBoolean();$ids+=,$id;$r.Dispose();$r=$asset.Reader($asset.Object($tr));$r.BaseStream.Position+=52;$count=$r.ReadInt32();$r.BaseStream.Position+=12*$count+4;$parent=$r.ReadInt64();$r.Dispose();if($parent -eq 0){break};$r=$asset.Reader($asset.Object($parent));$null=$r.ReadInt32();$id=$r.ReadInt64();$r.Dispose()};[ordered]@{hierarchy=($names -join ' -> ');ancestors=$ids;active=$active}}
$records=[Collections.Generic.List[object]]::new();$conversations=[Collections.Generic.List[object]]::new();$actors=@()
function FieldValue($fields,$name){@($fields|Where-Object {$_['title'] -eq $name}|Select-Object -First 1)[0]['value']}
foreach($file in Get-ChildItem -LiteralPath $dataDirectory -File|Where-Object {$_.Name -match '^level\d+$|^resources.assets$|^sharedassets0.assets$'}){
    $asset=[UnityResearchFile]::new($file.FullName)
    $scene='';if($file.Name -match '^level(\d+)$'){$scene=$scenes[[int]$Matches[1]]}
    foreach($o in $asset.FindScripts([long[]]@($scriptTypes.Keys))){
        $r=$asset.Reader($o);$null=$r.ReadInt32();$go=$r.ReadInt64();$enabled=$r.ReadBoolean();$r.BaseStream.Position=$o.Position+20;$type=$scriptTypes[$r.ReadInt64()];$null=[UnityResearchFile]::String($r);$values=[ordered]@{}
        foreach($f in Get-SerializedFields $type){$values[$f.Name]=Read-Value $r $f.FieldType;if(($type.Name -eq 'VendingMachine' -and $f.Name -eq 'Trader') -or ($type.Name -eq 'InteractableTalk' -and $f.Name -eq 'dialogueRelays') -or ($type.Name -eq 'FadeGameObject' -and $f.Name -eq 'hideAlways')){break}}
        if($r.BaseStream.Position -gt $o.Position+$o.Size){throw "Vendor supplemental prefix exceeded record $($file.Name):$($o.Id)"}
        if($type.Name -notin @('VendingMachine','InteractableTalk','FadeGameObject') -and $r.BaseStream.Position -ne $o.Position+$o.Size){throw "Vendor supplemental schema mismatch $($file.Name):$($o.Id) $($type.Name)"}
        if($type.Name -eq 'DialogueDatabase'){
            $actors=@($values['actors']);foreach($c in $values['conversations']){
                $entries=foreach($e in $c['dialogueEntries']){[ordered]@{id=$e['id'];actor=(FieldValue $e['fields'] 'Actor');text=(FieldValue $e['fields'] 'Dialogue Text');conditions=$e['conditionsString'];script=$e['userScript'];calls=$e['onExecute']['m_PersistentCalls']['m_Calls']}}
                $conversations.Add([ordered]@{id=$c['id'];title=(FieldValue $c['fields'] 'Title');actor=(FieldValue $c['fields'] 'Actor');conversant=(FieldValue $c['fields'] 'Conversant');entries=@($entries);sourceFile=$file.Name;pathId=$o.Id})
            }
        }elseif($scene -and $scene -notmatch '^Test|^Under Map|^Refugee'){$h=Hierarchy $asset $go;$records.Add([ordered]@{file=$file.Name;scene=$scene;id=$o.Id;class=$type.Name;gameObject=$go;enabled=$enabled;active=$h['active'];hierarchy=$h['hierarchy'];ancestors=$h['ancestors'];fields=$values;externalFiles=@($asset.ExternalFiles)})}
        $r.Dispose()
    }
    $asset=$null;[GC]::Collect()
}
Write-Json @($records.ToArray()) 'vendor-extra-records.json'
Write-Json ([ordered]@{actors=$actors;conversations=@($conversations.ToArray())}) 'vendor-dialogue.json'
'Vendor state controllers: '+$records.Count+'; dialogue conversations: '+$conversations.Count
