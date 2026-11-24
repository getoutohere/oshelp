param(
    [string]$GameRoot=(Split-Path (Split-Path $PSScriptRoot -Parent) -Parent),
    [string]$OutputDirectory=(Join-Path (Split-Path $PSScriptRoot -Parent) 'local/crafting-import')
)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'Inspect-Obenseuer.ps1') -GameRoot $GameRoot -OutputDirectory $OutputDirectory -FunctionsOnly
Add-Type -Path (Join-Path $PSScriptRoot 'CraftingAssetFile.cs')
$originalFields=${function:Get-SerializedFields};$fieldCache=@{}
function Get-SerializedFields([Type]$Type){if(-not $fieldCache.ContainsKey($Type.FullName)){$fieldCache[$Type.FullName]=@(& $originalFields $Type)};$fieldCache[$Type.FullName]}
$originalRead=${function:Read-Value}
function Read-Value([IO.BinaryReader]$Reader,[Type]$Type){
    if($Type.FullName -eq 'System.Double'){return $Reader.ReadDouble()}
    if($Type.FullName -eq 'UnityEngine.Vector2Int'){return @{x=$Reader.ReadInt32();y=$Reader.ReadInt32()}}
    for($base=$Type;$base;$base=$base.BaseType){if($base.FullName -eq 'System.Delegate'){return $null}}
    if($Type.FullName -eq 'Outputs.outputCustomEvent'){return $null}
    & $originalRead $Reader $Type
}
$scripts=@{}
$scriptAsset=[UnityResearchFile]::new((Join-Path $dataDirectory 'globalgamemanagers.assets'))
foreach($object in $scriptAsset.Objects){
    if($object.ClassId -ne 115){continue}
    $r=$scriptAsset.Reader($object)
    try{$null=[UnityResearchFile]::String($r);$null=$r.ReadInt32();$null=$r.ReadBytes(16);$scripts[$object.Id]=[UnityResearchFile]::String($r)}finally{$r.Dispose()}
}
$scriptAsset=$null
$assets=@{}
function Asset([string]$file){if(-not $assets.ContainsKey($file)){$assets[$file]=[CraftingAssetFile]::new((Join-Path $dataDirectory $file))};$assets[$file]}
function Pointer([string]$file,$p){
    if($p.pathId -eq 0){return ''}
    if($p.fileId -ne 0){$file=[IO.Path]::GetFileName((Asset $file).ExternalFiles[$p.fileId-1])}
    $file+':'+$p.pathId
}
$managers=Asset 'globalgamemanagers'
$r=$managers.Reader(@($managers.Objects|Where-Object ClassId -eq 141)[0])
$scenePaths=@()
try{$sceneCount=[UnityResearchFile]::Count($r);for($index=0;$index -lt $sceneCount;$index++){$scenePaths+=,[UnityResearchFile]::String($r)}}finally{$r.Dispose()}
function ReadPrefix([string]$file,$object,[string]$class,[string]$last){
    $asset=Asset $file;$r=$asset.Reader($object)
    try{
        $go=Pointer $file @{fileId=$r.ReadInt32();pathId=$r.ReadInt64()}
        $enabled=$r.ReadBoolean();[UnityResearchFile]::Align($r);$null=$r.ReadInt32();$actualClass=$scripts[$r.ReadInt64()];$name=[UnityResearchFile]::String($r)
        if($actualClass -ne $class){throw ('Serialized type contains inconsistent script pointers: '+$file+':'+$object.Id)}
        $values=[ordered]@{}
        foreach($field in (Get-SerializedFields $assembly.GetType($class,$true))){
            try{$values[$field.Name]=Read-Value $r $field.FieldType}catch{throw ($file+':'+$object.Id+' '+$class+'.'+$field.Name+': '+$_.Exception.Message)}
            if($field.Name -eq $last){break}
        }
        if($r.BaseStream.Position -gt $object.Position+$object.Size){throw ('Crafting record overrun '+$file+':'+$object.Id)}
        [ordered]@{sourceFile=$file;pathId=$object.Id;class=$class;name=$name;gameObject=$go;enabled=$enabled;fields=$values;externalFiles=@($asset.ExternalFiles)}
    }finally{$r.Dispose()}
}
$baseType=$assembly.GetType('CraftingBase',$true)
$stationClasses=@($assembly.GetTypes()|Where-Object{$_ -eq $baseType -or $_.IsSubclassOf($baseType)}|ForEach-Object{$_.Name})
$wanted=@('Furniture','FurnitureGroup','TaskItemBlueprint','CraftingUpgrade','ItemVariantManager','FurnitureCrafting')+$stationClasses
$ids=[long[]]@($scripts.Keys|Where-Object{$scripts[$_] -in $wanted})
$records=[Collections.Generic.List[object]]::new()
foreach($file in (Get-ChildItem -LiteralPath $dataDirectory -File|Where-Object{$_.Name -match '\.assets$|^level\d+$'}|Sort-Object Name|Select-Object -ExpandProperty Name)){
    $asset=Asset $file
    foreach($object in $asset.FindScripts($ids)){
        $r=$asset.Reader($object);$r.BaseStream.Position=$object.Position+20;$class=$scripts[$r.ReadInt64()];$r.Dispose()
        $last=switch($class){
            'Furniture'{'prefab'} 'FurnitureGroup'{'furnitures'} 'TaskItemBlueprint'{'minimumSkill'}
            'CraftingUpgrade'{'AddModifiers'} 'ItemVariantManager'{'variantCategories'} 'FurnitureCrafting'{'title'}
            default{'Modifiers'}
        }
        $record=ReadPrefix $file $object $class $last
        if($stationClasses -contains $class){
            $type=$assembly.GetType($class,$true)
            $record.fields['craftingKind']=if($type.Name -eq 'Growing' -or $type.IsSubclassOf($assembly.GetType('Growing'))){0}elseif($type.Name -eq 'manufacturingProcess' -or $type.IsSubclassOf($assembly.GetType('manufacturingProcess'))){1}else{2}
            $record.fields['maxUpgrades']=0
        }
        $keep=switch($class){
            'Furniture'{@('title','prefab')}
            'FurnitureGroup'{@('title','furnitures')}
            'TaskItemBlueprint'{@('resourceItems','result','amount','processTime','skill','minimumSkill')}
            'CraftingUpgrade'{@('ID','RequiredType','RequiredRecipes','RequiredRecipesAny','AdditionalRecipes','AddModifiers','AddEquipmentQuality','MaxDistance')}
            'ItemVariantManager'{@('variantCategories')}
            'FurnitureCrafting'{@('title')}
            default{@('ManuName','EquipmentQuality','Recipes','Modifiers','maxUpgrades','craftingKind')}
        }
        $thin=[ordered]@{};foreach($key in $keep){if($record.fields.Contains($key)){$thin[$key]=$record.fields[$key]}};$record.fields=$thin
        $records.Add($record)
    }
}
# Only stations that can use a modifier-providing add-on need their connection
# limit. Avoid decoding fuel, sound and lock graphs for every other station.
$modifierAddons=@($records|Where-Object{$_.class -eq 'CraftingUpgrade' -and $_.fields.AddModifiers.Count -gt 0})
foreach($record in $records){
    if($record.class -notin $stationClasses){continue}
    $needsLimit=$false
    foreach($addon in $modifierAddons){
        if($addon.fields.RequiredType -ne $record.fields.craftingKind){continue}
        $overlap=@($addon.fields.RequiredRecipes|Where-Object{$_ -in $record.fields.Recipes})
        if(($addon.fields.RequiredRecipesAny -and $overlap.Count -gt 0) -or (-not $addon.fields.RequiredRecipesAny -and $overlap.Count -eq $addon.fields.RequiredRecipes.Count)){$needsLimit=$true;break}
    }
    if($needsLimit){$asset=Asset $record.sourceFile;$full=ReadPrefix $record.sourceFile ($asset.Object($record.pathId)) $record.class 'maxUpgrades';$record.fields.maxUpgrades=$full.fields.maxUpgrades}
}
# Resolve a workstation's furniture name by its prefab ancestry, not a name guess.
$rootNames=@{}
foreach($record in $records){if($record.class -eq 'Furniture'){$key=Pointer $record.sourceFile $record.fields.prefab;if($key){$rootNames[$key]=$record.fields.title}}}
$goCache=@{}
function GameObject([string]$key){
    if($goCache.ContainsKey($key)){return $goCache[$key]}
    $parts=$key.Split(':');$file=$parts[0];$asset=Asset $file;$object=$asset.Object([long]$parts[1]);$r=$asset.Reader($object)
    try{
        if($object.ClassId -ne 1){throw ('Not a GameObject '+$key)}
        $count=[UnityResearchFile]::Count($r);$components=@()
        for($index=0;$index -lt $count;$index++){$components+=,(Pointer $file @{fileId=$r.ReadInt32();pathId=$r.ReadInt64()})}
        $null=$r.ReadInt32();$name=[UnityResearchFile]::String($r);$parent=''
        foreach($component in $components){
            if(-not $component){continue};$p=$component.Split(':');$componentAsset=Asset $p[0];$transform=$componentAsset.Object([long]$p[1]);if($transform.ClassId -notin 4,224){continue}
            $t=$componentAsset.Reader($transform)
            try{
                $t.BaseStream.Position=$transform.Position+52;$children=[UnityResearchFile]::Count($t);$t.BaseStream.Position+=12*$children
                $parentTransform=Pointer $p[0] @{fileId=$t.ReadInt32();pathId=$t.ReadInt64()}
                if($parentTransform){$q=$parentTransform.Split(':');$pa=Asset $q[0];$pr=$pa.Reader($pa.Object([long]$q[1]));try{$parent=Pointer $q[0] @{fileId=$pr.ReadInt32();pathId=$pr.ReadInt64()}}finally{$pr.Dispose()}}
            }finally{$t.Dispose()}
            break
        }
        $result=@{name=$name;parent=$parent};$goCache[$key]=$result;return $result
    }finally{$r.Dispose()}
}
foreach($record in $records){
    if($record.class -notin ($stationClasses+@('CraftingUpgrade','FurnitureCrafting'))){continue}
    $key=$record.gameObject;$visited=@{};$title=''
    $record['furniture']= $false
    while($key -and -not $visited.ContainsKey($key)){
        $visited[$key]=$true
        if($rootNames.ContainsKey($key)){$title=$rootNames[$key];$record.furniture=$true;break}
        $go=GameObject $key;$key=$go.parent
    }
    if(-not $title){$title=$record.fields.ManuName}
    if(-not $title -and $record.gameObject){$title=(GameObject $record.gameObject).name}
    $record['title']=$title
    $record['scene']=if($record.sourceFile -match '^level(\d+)$'){[IO.Path]::GetFileNameWithoutExtension($scenePaths[[int]$Matches[1]])}else{''}
}
$enums=[ordered]@{}
foreach($name in @('RecipeType','RecipeCondition','CraftingBase+CraftingType','PlayerStats+PlayerSkillType','OS.Items.VariantCategory')){
    $values=[ordered]@{};foreach($value in $assembly.GetType($name,$true).GetFields([Reflection.BindingFlags]'Public,Static')){$values[[string]$value.GetRawConstantValue()]=$value.Name};$enums[$name]=$values
}
$result=[ordered]@{formatVersion=2;files=@($assets.Keys|Sort-Object);stationClasses=$stationClasses;enums=$enums;records=$records.ToArray()}
$result|ConvertTo-Json -Depth 60|Set-Content -LiteralPath (Join-Path $OutputDirectory 'crafting-assets.json') -Encoding UTF8
foreach($asset in $assets.Values){$asset.Dispose()}
'Exported '+$records.Count+' crafting asset records.'
