param(
    [string]$GameRoot=(Split-Path $PSScriptRoot -Parent|Split-Path -Parent),
    [string]$OutputDirectory=(Join-Path (Split-Path $PSScriptRoot -Parent) 'local/vendor-research')
)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'Inspect-Obenseuer.ps1') -FunctionsOnly -GameRoot $GameRoot -OutputDirectory $OutputDirectory
$scripts=[UnityResearchFile]::new((Join-Path $dataDirectory 'globalgamemanagers.assets'));$storageIds=@()
foreach($o in $scripts.Objects){if($o.ClassId -ne 115){continue};$r=$scripts.Reader($o);$null=[UnityResearchFile]::String($r);$null=$r.ReadInt32();$null=$r.ReadBytes(16);if([UnityResearchFile]::String($r) -eq 'Storage'){$storageIds+=,$o.Id};$r.Dispose()}
$records=[Collections.Generic.List[object]]::new()
foreach($file in Get-ChildItem -LiteralPath $dataDirectory -File|Where-Object {$_.Name -match '^level\d+$'}){
    $asset=[UnityResearchFile]::new($file.FullName)
    foreach($o in $asset.FindScripts([long[]]$storageIds)){
        $r=$asset.Reader($o);$null=$r.ReadInt32();$go=$r.ReadInt64();$r.BaseStream.Position=$o.Position+28;$null=[UnityResearchFile]::String($r)
        # Storage inherits Interactable.title as its first serialized field.
        $title=[UnityResearchFile]::String($r);if($r.BaseStream.Position -gt $o.Position+$o.Size){throw "Storage label exceeds record $($file.Name):$($o.Id)"};$r.Dispose()
        if($title){$records.Add([ordered]@{file=$file.Name;id=$o.Id;class='Storage';gameObject=$go;fields=[ordered]@{title=$title}})}
    }
    $asset=$null;[GC]::Collect()
}
Write-Json @($records.ToArray()) 'vendor-labels.json'
'Named storage labels: '+$records.Count
