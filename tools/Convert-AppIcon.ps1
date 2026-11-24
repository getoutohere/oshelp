param()
$ErrorActionPreference='Stop'
$repository=Split-Path $PSScriptRoot -Parent
Add-Type -AssemblyName System.Drawing
$source=[Drawing.Image]::FromFile((Join-Path $repository 'design/oshelp-icon.png'))
$frames=[Collections.Generic.List[byte[]]]::new()
$sizes=@(16,20,24,32,40,48,64,128,256)
try {
    foreach($size in $sizes){
        $bitmap=[Drawing.Bitmap]::new($size,$size,[Drawing.Imaging.PixelFormat]::Format32bppArgb)
        $graphics=[Drawing.Graphics]::FromImage($bitmap)
        $memory=[IO.MemoryStream]::new()
        try {
            $graphics.Clear([Drawing.Color]::Transparent)
            $graphics.CompositingMode=[Drawing.Drawing2D.CompositingMode]::SourceCopy
            $graphics.InterpolationMode=[Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
            $graphics.PixelOffsetMode=[Drawing.Drawing2D.PixelOffsetMode]::HighQuality
            # Fit the artwork without cropping, with a small transparent safety margin.
            $scale=($size*0.94)/[Math]::Max($source.Width,$source.Height)
            $width=[single]($source.Width*$scale);$height=[single]($source.Height*$scale)
            $rectangle=[Drawing.RectangleF]::new([single](($size-$width)/2),[single](($size-$height)/2),$width,$height)
            $graphics.DrawImage($source,$rectangle)
            $graphics.Flush()
            # Use native 32-bit DIB frames, including the AND mask. PNG-only
            # frames do not render reliably through all .NET/GDI icon paths.
            $frameWriter=[IO.BinaryWriter]::new($memory,[Text.Encoding]::UTF8,$true)
            try {
                $maskStride=[int]([Math]::Ceiling($size/32.0)*4)
                $frameWriter.Write([uint32]40)
                $frameWriter.Write([int32]$size);$frameWriter.Write([int32]($size*2))
                $frameWriter.Write([uint16]1);$frameWriter.Write([uint16]32)
                $frameWriter.Write([uint32]0)
                $frameWriter.Write([uint32]($size*$size*4+$maskStride*$size))
                foreach($unused in 1..4){$frameWriter.Write([uint32]0)}
                for($row=$size-1;$row -ge 0;$row--){
                    for($column=0;$column -lt $size;$column++){
                        $frameWriter.Write([int32]$bitmap.GetPixel($column,$row).ToArgb())
                    }
                }
                for($row=$size-1;$row -ge 0;$row--){
                    $mask=[byte[]]::new($maskStride)
                    for($column=0;$column -lt $size;$column++){
                        if($bitmap.GetPixel($column,$row).A -eq 0){
                            $maskIndex=[int][Math]::Floor($column/8.0)
                            $mask[$maskIndex]=$mask[$maskIndex] -bor (0x80 -shr ($column%8))
                        }
                    }
                    $frameWriter.Write($mask)
                }
            } finally {$frameWriter.Dispose()}
            $frames.Add($memory.ToArray())
        } finally {$memory.Dispose();$graphics.Dispose();$bitmap.Dispose()}
    }
} finally {$source.Dispose()}
# Each directory entry points to a complete native frame.
$output=Join-Path $repository 'design/oshelp.ico'
$writer=[IO.BinaryWriter]::new([IO.File]::Create($output))
try {
    $writer.Write([uint16]0);$writer.Write([uint16]1);$writer.Write([uint16]$sizes.Count)
    $offset=6+16*$sizes.Count
    for($index=0;$index -lt $sizes.Count;$index++){
        $dimension=if($sizes[$index] -eq 256){0}else{$sizes[$index]}
        $writer.Write([byte]$dimension);$writer.Write([byte]$dimension)
        $writer.Write([byte]0);$writer.Write([byte]0)
        $writer.Write([uint16]1);$writer.Write([uint16]32)
        $writer.Write([uint32]$frames[$index].Length);$writer.Write([uint32]$offset)
        $offset+=$frames[$index].Length
    }
    foreach($frame in $frames){$writer.Write([byte[]]$frame)}
} finally {$writer.Dispose()}
'Created design/oshelp.ico: '+($sizes -join ', ')+' pixels, 32-bit alpha.'
