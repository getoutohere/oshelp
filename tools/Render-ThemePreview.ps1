param([string]$RepositoryRoot = (Split-Path $PSScriptRoot -Parent))
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$theme = Get-Content -LiteralPath (Join-Path $RepositoryRoot 'design/obenseuer-dark.json') -Raw | ConvertFrom-Json
$outputDirectory = Join-Path $RepositoryRoot 'docs/assets'
$null = New-Item -ItemType Directory -Force -Path $outputDirectory
$bitmap = [Drawing.Bitmap]::new(1440, 940)
$graphics = [Drawing.Graphics]::FromImage($bitmap)
$graphics.TextRenderingHint = [Drawing.Text.TextRenderingHint]::AntiAliasGridFit
$svg = [Text.StringBuilder]::new()
$null = $svg.AppendLine('<svg xmlns="http://www.w3.org/2000/svg" width="1440" height="940" viewBox="0 0 1440 940"><title>OSHelp Obenseuer Dark theme concept</title><desc>Dark desktop planning interface with tenant requirements and a materials shopping cart. Illustrative profile, verified material bill.</desc>')
function Color([string]$Token) { [Drawing.ColorTranslator]::FromHtml($theme.colors.$Token) }
function Rect([int]$X, [int]$Y, [int]$W, [int]$H, [string]$Fill, [string]$Stroke = '') {
    $brush = [Drawing.SolidBrush]::new((Color $Fill))
    $graphics.FillRectangle($brush, $X, $Y, $W, $H); $brush.Dispose()
    $strokeAttribute = ''
    if ($Stroke) {
        $pen = [Drawing.Pen]::new((Color $Stroke), 1)
        $graphics.DrawRectangle($pen, $X, $Y, $W, $H); $pen.Dispose()
        $strokeAttribute = ' stroke="' + $theme.colors.$Stroke + '"'
    }
    $null = $svg.AppendLine(('<rect x="{0}" y="{1}" width="{2}" height="{3}" fill="{4}"{5}/>' -f $X,$Y,$W,$H,$theme.colors.$Fill,$strokeAttribute))
}
function Label([string]$Value, [int]$X, [int]$Y, [int]$Size = 15, [string]$Tone = 'text', [string]$Role = 'body') {
    $family = $theme.typography.$Role.family
    $style = if ($Role -in @('heading','label')) { [Drawing.FontStyle]::Bold } else { [Drawing.FontStyle]::Regular }
    $font = [Drawing.Font]::new($family, $Size, $style, [Drawing.GraphicsUnit]::Pixel)
    $brush = [Drawing.SolidBrush]::new((Color $Tone))
    $graphics.DrawString($Value, $font, $brush, $X, $Y); $font.Dispose(); $brush.Dispose()
    $escaped = [Security.SecurityElement]::Escape($Value)
    $weight = if ($Role -in @('heading','label')) { 'bold' } else { 'normal' }
    $null = $svg.AppendLine(('<text x="{0}" y="{1}" font-family="{2}" font-size="{3}" font-weight="{4}" fill="{5}">{6}</text>' -f $X,($Y+$Size),$family,$Size,$weight,$theme.colors.$Tone,$escaped))
}
function Button([string]$Value, [int]$X, [int]$Y, [int]$W, [string]$Fill = 'button') {
    Rect $X $Y $W 38 $Fill 'accent'; Label $Value ($X+14) ($Y+10) 14 'text' 'label'
}

Rect 0 0 1440 940 'window'
Rect 0 0 1440 36 'recess'
Label 'OSHelp' 20 8 16 'muted' 'label'
Label 'TENEMENT PLANNER' 24 55 25 'text' 'heading'
Label 'Obenseuer / Materials and rental requirements' 26 88 14 'muted'
Rect 1030 55 385 43 'recess' 'border'
Label 'PROFILE:  Example playthrough' 1045 67 14 'muted' 'label'

Button 'TENANTS' 24 123 180
Button 'UPGRADES' 214 123 180 'raised'
Button 'SHOPPING CART  5' 404 123 220 'raised'
Label 'RESOURCE COST  1.00x' 1160 136 14 'muted' 'numeric'

Rect 24 181 298 636 'panel' 'border'
Rect 40 197 266 38 'recess' 'inputBorder'
Label 'Search tenants...' 53 207 15 'muted'
Rect 40 248 266 32 'navigation' 'border'
Label 'ALL RESIDENT DEFINITIONS' 51 257 13 'text' 'label'
Label '31 definitions' 44 294 13 'muted'
$kirkaName = 'Kirka K' + [char]0x00E4 + [char]0x00E4 + 'bus'
$names = @('Aleksi Kivi','Amanda Korhonen','Agnes Takala','Einar Kaivuri','Elias Karhu','Fenella Hunt','Jarmo Pettinen',$kirkaName,'Martta Lapinjoki')
for ($i=0; $i -lt $names.Count; $i++) {
    $y = 322 + $i*48
    Rect 40 $y 266 42 $(if($i -eq 0){'selection'}else{'recess'}) $(if($i -eq 0){'accent'}else{'border'})
    if ($i -eq 0) { Rect 40 $y 3 42 'accent' }
    Label $names[$i] 54 ($y+12) 15
}

Rect 338 181 616 636 'panel' 'border'
Rect 354 197 584 64 'recess'
Label 'ALEKSI KIVI' 370 211 23 'text' 'heading'
Label 'Accepted apartment size: Small' 371 241 14 'muted'
Rect 354 277 584 42 'navigation' 'border'
Label 'EXAMPLE APARTMENT / SMALL' 370 291 14 'text' 'label'
Label 'RENTAL REQUIREMENTS' 370 343 16 'accent' 'label'
Label 'Facility' 371 381 13 'muted'
Label 'Required' 590 381 13 'muted'
Label 'Recorded state' 765 381 13 'muted'
$requirements=@(@('Bathroom','Makeshift','Needs work'),@('Walls','Makeshift','Needs work'),@('Apartment size','Small','Meets requirement'))
for($i=0;$i -lt $requirements.Count;$i++) {
    $y=406+$i*48
    Rect 354 $y 584 43 'recess'
    Label $requirements[$i][0] 371 ($y+13) 15
    Label $requirements[$i][1] 590 ($y+13) 15
    Label $requirements[$i][2] 765 ($y+13) 14 $(if($i -eq 2){'success'}else{'warning'})
}
Label 'BUILDING REQUIREMENTS' 370 575 16 'accent' 'label'
Label 'No building upgrade minimums for this resident.' 371 609 15 'muted'
Rect 354 652 584 71 'recess' 'border'
Label '2 jobs confirmed as still needed' 370 666 15 'text'
Label 'Makeshift Bathroom + Makeshift Walls' 370 693 14 'muted'
Button 'REVIEW MISSING WORK' 370 752 260

Rect 970 181 446 636 'panel' 'border'
Label 'MATERIALS TO BUY' 986 204 21 'text' 'heading'
Label '2 jobs / 1 example apartment' 988 237 14 'muted'
Rect 986 276 414 38 'navigation' 'border'
Label 'Material' 999 287 13 'text' 'label'
Label 'Need' 1189 287 13 'text' 'label'
Label 'Have' 1262 287 13 'text' 'label'
Label 'Buy' 1340 287 13 'text' 'label'
$materials=@(@('Planks','32','10','22'),@('Nails','34','0','34'),@('Scrap Metal','20','0','20'),@('Duct Tape','4','0','4'),@('Tarps','7','0','7'))
for($i=0;$i -lt $materials.Count;$i++) {
    $y=318+$i*52
    Rect 986 $y 414 48 $(if($i%2 -eq 0){'recess'}else{'panel'})
    Label $materials[$i][0] 999 ($y+16) 15
    Label $materials[$i][1] 1200 ($y+16) 16 'muted' 'numeric'
    Rect 1248 ($y+7) 63 34 'recess' 'inputBorder'
    Label $materials[$i][2] 1268 ($y+16) 16 'text' 'numeric'
    Label $materials[$i][3] 1346 ($y+16) 17 'accent' 'numeric'
}
Label 'Costs for the selected jobs' 989 607 14 'muted'
Label 'START    1,120 OC' 989 638 17 'text' 'numeric'
Label 'FINAL       95 RM' 989 669 17 'text' 'numeric'
Button 'EXPORT SHOPPING LIST' 988 750 304

Rect 24 837 1392 74 'recess' 'border'
$swatches=@('window','panel','recess','accent','navigation','text','success','danger')
for($i=0;$i -lt $swatches.Count;$i++) {
    $x=41+$i*169
    Rect $x 852 24 24 $swatches[$i] 'border'
    Label $swatches[$i] ($x+33) 850 12 'muted' 'label'
    Label $theme.colors.PSObject.Properties[$swatches[$i]].Value ($x+33) 870 13 'text' 'numeric'
}
Label 'THEME CONCEPT / Illustrative profile; material bill extracted from game assets. Not a running application.' 26 921 12 'muted'
$null=$svg.AppendLine('</svg>')
[IO.File]::WriteAllText((Join-Path $outputDirectory 'theme-preview.svg'), $svg.ToString(), [Text.UTF8Encoding]::new($false))
$bitmap.Save((Join-Path $outputDirectory 'theme-preview.png'), [Drawing.Imaging.ImageFormat]::Png)
$graphics.Dispose();$bitmap.Dispose()
'Rendered theme-preview.svg and theme-preview.png from design/obenseuer-dark.json'
