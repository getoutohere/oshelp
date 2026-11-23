param([string]$OutputDirectory=(Join-Path (Split-Path $PSScriptRoot -Parent) 'local/rent-research'))
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'Inspect-Obenseuer.ps1') -FunctionsOnly -OutputDirectory $OutputDirectory
$lines=foreach($name in @('TenementResident','TenementApartment','TenementController','DifficultyInfo','money')){
    $type=$assembly.GetTypes()|Where-Object {$_.Name -ceq $name}|Select-Object -First 1
    if(-not $type){continue};$type.FullName
    foreach($field in $type.GetFields($fieldFlags -bor [Reflection.BindingFlags]::Static)){'  FIELD '+$field.ToString()}
    foreach($method in @($type.GetMethods($methodFlags))+@($type.GetConstructors($methodFlags))){
        '  METHOD '+$method.ToString()
        if($method.Name -match 'Rent|Income|Value|Payment|Paid|Money|op_|ctor|Multiplier|AdjacentDifficulty'){
            try{foreach($line in [ResearchIL]::Read($method)){'    '+$line}}catch{'    ERROR '+$_.Exception.Message}
        }
    }
}
$lines|Set-Content -Encoding UTF8 (Join-Path $OutputDirectory 'rent-rules.il.txt')
$engine=[Reflection.Assembly]::ReflectionOnlyLoadFrom((Join-Path $managedDirectory 'UnityEngine.CoreModule.dll'))
'UnityEngine.Mathf::RoundToInt(Single)'|Add-Content -Encoding UTF8 (Join-Path $OutputDirectory 'rent-rules.il.txt')
[ResearchIL]::Read($engine.GetType('UnityEngine.Mathf').GetMethod('RoundToInt',[Reflection.BindingFlags]'Public,Static'))|Add-Content -Encoding UTF8 (Join-Path $OutputDirectory 'rent-rules.il.txt')
'Rental income rules: '+$OutputDirectory
