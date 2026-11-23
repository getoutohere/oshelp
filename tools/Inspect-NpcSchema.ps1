$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'Inspect-Obenseuer.ps1') -FunctionsOnly -OutputDirectory (Join-Path (Split-Path $PSScriptRoot -Parent) 'local/npc-research')
$lines=foreach($type in $assembly.GetTypes()|Where-Object {$_.FullName -match 'NPCInfo|NPCDirector|NPCSpawn|NPCDatabase|SceneName|LevelNames|NPCManager|NPCData' -or $_.Name -eq 'Character'}){
    $type.FullName
    foreach($field in $type.GetFields($fieldFlags)){try{'  '+$field.ToString()}catch{'  '+$field.Name}}
    foreach($method in $type.GetMethods($methodFlags)){
        try{'  METHOD '+$method.ToString()}catch{'  METHOD '+$method.Name}
        if($method.Name -match 'Spawn|Initial|Load|Start') {try{[ResearchIL]::Read($method)}catch{'  IL error'}}
    }
}
$lines|Set-Content -Encoding UTF8 (Join-Path (Split-Path $PSScriptRoot -Parent) 'local/npc-research/schema.txt')
