param()
$ErrorActionPreference='Stop'
$repository=Split-Path $PSScriptRoot -Parent
Push-Location $repository
try {
    # Audit tracked files and files eligible for the initial source commit.
    $files=@(& git -c core.quotepath=false ls-files --cached --others --exclude-standard | Sort-Object -Unique)
    if($LASTEXITCODE -ne 0){throw 'Cannot enumerate Git source files.'}
    if($files.Count -eq 0){throw 'No source files found.'}
    $failures=[Collections.Generic.List[string]]::new()
    $utf8=[Text.UTF8Encoding]::new($false,$true)
    $machinePath='(?i)(?<![a-z0-9])[a-z]:[\\/]|/(?:Users|home)/[^\s/]+|\\\\[a-z0-9_.-]+\\(?:Users|home|share)\\'
    $privateKey='-----BEGIN (?:RSA |EC |OPENSSH )?PRIVATE KEY-----'
    $userName=[Environment]::UserName
    foreach($file in $files){
        if($file -match '(^|/)(?:bin|obj|app|artifacts|local|exports|decomp|Obenseuer_Data|\.vs)/' -or $file -match '\.(?:dll|exe|pdb|zip|7z|tar|gz|nupkg|tnmt|dialogue|png|jpg|jpeg|bak|log)$' -or $file -match '(^|/)oshelp-profile[^/]*\.json$' -or $file -eq 'docs/evidence/dotnet-info.txt'){
            $failures.Add($file+': excluded artifact or private data');continue
        }
        try{$content=$utf8.GetString([IO.File]::ReadAllBytes((Join-Path $repository $file)))}catch{$failures.Add($file+': non-text content');continue}
        if($content.Contains([char]0)){$failures.Add($file+': binary content');continue}
        if($content -match $machinePath){$failures.Add($file+': machine-specific absolute path')}
        if($content -match $privateKey){$failures.Add($file+': private key material')}
        if($userName.Length -ge 3 -and $content -match ('(?i)(?<![a-z0-9_])'+[Regex]::Escape($userName)+'(?![a-z0-9_])')){$failures.Add($file+': local account name')}
    }
    if($failures.Count -gt 0){throw ("Source release audit failed:`n"+($failures -join "`n"))}
    'PASS: '+$files.Count+' source files; no compiled artifacts, personal saves, binary captures, machine paths, local account names, or private keys.'
} finally {Pop-Location}
