param(
    [string]$GameRoot = (Split-Path $PSScriptRoot -Parent | Split-Path -Parent),
    [string]$OutputDirectory = (Join-Path (Split-Path $PSScriptRoot -Parent) 'local/planning-import')
)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'Inspect-Obenseuer.ps1') -GameRoot $GameRoot -OutputDirectory $OutputDirectory -FunctionsOnly
$firstpass=[Reflection.Assembly]::ReflectionOnlyLoadFrom((Join-Path $managedDirectory 'Assembly-CSharp-firstpass.dll'))
$scriptFile=[UnityResearchFile]::new((Join-Path $dataDirectory 'globalgamemanagers.assets'))
$scriptTypes=@{}
foreach($object in $scriptFile.Objects){
    if($object.ClassId -ne 115){continue}
    $r=$scriptFile.Reader($object)
    $name=[UnityResearchFile]::String($r);$null=$r.ReadInt32();$null=$r.ReadBytes(16)
    $className=[UnityResearchFile]::String($r);$namespace=[UnityResearchFile]::String($r);$assemblyName=[UnityResearchFile]::String($r)
    $fullName=if($namespace){$namespace+'.'+$className}else{$className}
    if($className -in @('TenementController','TenementPanelUI','DifficultyController','DialogueDatabase','TaskItem','TaskItemMap')){
        $type=if($assemblyName -like 'Assembly-CSharp-firstpass*'){$firstpass.GetType($fullName)}else{$assembly.GetType($fullName)}
        if($type){$scriptTypes[$object.Id]=$type}
    }
}
$scriptFile=$null
$records=[Collections.Generic.List[object]]::new()
function Dialogue-Field($Fields, [string]$Name) { ($Fields | Where-Object { $_['title'] -eq $Name } | Select-Object -First 1)['value'] }
foreach($fileName in @('level0','level2','resources.assets','sharedassets0.assets')){
    $asset=[UnityResearchFile]::new((Join-Path $dataDirectory $fileName))
    foreach($object in $asset.Objects){
        if($object.ClassId -ne 114){continue}
        $r=$asset.Reader($object);$r.BaseStream.Position=$object.Position+16
        $fileId=$r.ReadInt32();$scriptId=$r.ReadInt64();$type=$scriptTypes[$scriptId]
        if(-not $type){continue}
        $name=[UnityResearchFile]::String($r);$values=[ordered]@{}
        foreach($field in (Get-SerializedFields $type)){$values[$field.Name]=Read-Value $r $field.FieldType}
        if($r.BaseStream.Position -ne $object.Position+$object.Size){throw "Supplement schema mismatch $fileName $($object.Id) $($type.Name) ${name}: $($r.BaseStream.Position-$object.Position)/$($object.Size)"}
        if($type.FullName -like '*DialogueDatabase'){
            $conversations=@()
            foreach($conversation in $values['conversations']){
                $hasRent=$false
                $entries=@()
                foreach($entry in $conversation['dialogueEntries']){
                    $calls=$entry['onExecute']['m_PersistentCalls']['m_Calls']
                    $targets=@($calls|Where-Object{$_['m_MethodName'] -eq 'StartRent'}|ForEach-Object{$_['m_Target']})
                    if($targets.Count -gt 0 -or $entry['conditionsString'] -match 'CanRentApartment'){$hasRent=$true}
                    $entries+=,[ordered]@{id=$entry['id'];text=(Dialogue-Field $entry['fields'] 'Dialogue Text');actor=(Dialogue-Field $entry['fields'] 'Actor');conditions=$entry['conditionsString'];script=$entry['userScript'];links=$entry['outgoingLinks'];rentTargets=$targets}
                }
                if($hasRent){$conversations+=,[ordered]@{id=$conversation['id'];title=(Dialogue-Field $conversation['fields'] 'Title');actor=(Dialogue-Field $conversation['fields'] 'Conversant');entries=$entries}}
            }
            $values=[ordered]@{gameConversationCount=$values['conversations'].Count;conversations=$conversations;actors=$values['actors']}
        }
        $records.Add([ordered]@{sourceFile=$fileName;pathId=$object.Id;class=$type.FullName;name=$name;fields=$values;externalFiles=$asset.ExternalFiles})
    }
    $asset=$null;[GC]::Collect()
}
Write-Json @($records) 'planning.json'
$records|Group-Object {$_['class']}|ForEach-Object{'{0}: {1}' -f $_.Name,$_.Count}
