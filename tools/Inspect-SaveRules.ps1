param([string]$OutputDirectory=(Join-Path (Split-Path $PSScriptRoot -Parent) 'local/save-research'))
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'Inspect-Obenseuer.ps1') -FunctionsOnly -OutputDirectory $OutputDirectory
$types=$assembly.GetTypes()|Where-Object {$_.FullName -match 'Save|Tenement|Difficulty|Inventory|TaskItem|Player' }
$lines=foreach($type in $types){
    $type.FullName
    foreach($field in $type.GetFields($fieldFlags)){try{'  FIELD '+$field.ToString()}catch{'  FIELD '+$field.Name}}
    foreach($method in $type.GetMethods($methodFlags)){
        try{'  METHOD '+$method.ToString()}catch{'  METHOD '+$method.Name}
        if(($type.FullName -match 'Tenement|Difficulty|Inventory|TaskItem|Save') -and ($method.Name -match 'Save|Saving|Load|Capture|Restore|StartRent|CanRent|Upgrade|Available|Read|Write|GetData|SetData|CheckIfPlayer|PlayerHasTask|RefreshResource|GetResource|ChangeDifficulty|GetMultiplier|FurnitureClear|Contains|PlayerHasRequiredItem|GetItemAmount')){
            try{foreach($line in [ResearchIL]::Read($method)){'    '+$line}}catch{'    DECODE ERROR '+$_.Exception.Message}
        }
    }
}
$lines|Set-Content -LiteralPath (Join-Path $OutputDirectory 'save-rules.il.txt') -Encoding UTF8
'Saved reflection/IL report to '+$OutputDirectory
