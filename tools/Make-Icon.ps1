<#
.SYNOPSIS
Rebuilds src/HDGraph.App/Assets/hdgraph.ico from the SVG sources. Needs ImageMagick 7 (`magick` on PATH).

The .ico is committed, so neither the build nor CI runs this; run it after editing hdgraph.svg or hdgraph-small.svg.
Layers: 16, 20, 24 come from hdgraph-small.svg (two rings, no separators), 32 from hdgraph.svg with the white
separators stripped (they would be a third of a pixel), 40 and up from hdgraph.svg as is. ImageMagick writes every
layer as an uncompressed 32-bit bitmap, so the 256 layer is appended here by hand as PNG (Vista+ format, ~250 KB less
in the exe).
#>
$ErrorActionPreference = 'Stop'
$assets = Join-Path (Split-Path $PSScriptRoot -Parent) 'src/HDGraph.App/Assets'
$work = Join-Path ([IO.Path]::GetTempPath()) 'hdgraph-icon'
New-Item -ItemType Directory -Force $work | Out-Null

$master = Join-Path $assets 'hdgraph.svg'
$small = Join-Path $assets 'hdgraph-small.svg'
$plain = Join-Path $work 'hdgraph-plain.svg'
(Get-Content $master -Raw) -replace ' stroke="#FFFFFF" stroke-width="2" stroke-linejoin="round"', '' | Set-Content $plain -NoNewline

$layers = @(
    @{ Size = 16;  Source = $small },
    @{ Size = 20;  Source = $small },
    @{ Size = 24;  Source = $small },
    @{ Size = 32;  Source = $plain },
    @{ Size = 40;  Source = $master },
    @{ Size = 48;  Source = $master },
    @{ Size = 64;  Source = $master },
    @{ Size = 256; Source = $master }
)
$pngs = foreach ($layer in $layers) {
    $png = Join-Path $work "$($layer.Size).png"
    magick -background none -density 384 $layer.Source -resize "$($layer.Size)x$($layer.Size)" -depth 8 $png
    if ($LASTEXITCODE -ne 0) { throw "magick failed on $($layer.Source) at $($layer.Size) px" }
    $png
}
# Bitmap layers via ImageMagick, then the 256 PNG appended as one more directory entry.
$bitmapPngs = $pngs | Where-Object { $_ -notlike '*256.png' }
$bitmapIco = Join-Path $work 'bitmap.ico'
magick @bitmapPngs $bitmapIco
if ($LASTEXITCODE -ne 0) { throw 'magick failed to assemble the .ico' }

$src = [IO.File]::ReadAllBytes($bitmapIco)
$count = [BitConverter]::ToUInt16($src, 4)
$png = [IO.File]::ReadAllBytes((Join-Path $work '256.png'))
$layersOut = @()
for ($i = 0; $i -lt $count; $i++) {
    $entry = $src[(6 + 16 * $i)..(21 + 16 * $i)]
    $size = [BitConverter]::ToUInt32($entry, 8)
    $offset = [BitConverter]::ToUInt32($entry, 12)
    $layersOut += , @{ Entry = $entry[0..7]; Data = $src[$offset..($offset + $size - 1)] }
}
# ICONDIRENTRY for the PNG: width/height 0 = 256, colour count 0, reserved 0, planes 1, bit count 32.
$layersOut += , @{ Entry = [byte[]](0, 0, 0, 0, 1, 0, 32, 0); Data = $png }

$out = New-Object IO.MemoryStream
$writer = New-Object IO.BinaryWriter($out)
$writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$layersOut.Count)
$dataOffset = 6 + 16 * $layersOut.Count
foreach ($layer in $layersOut) {
    $writer.Write([byte[]]$layer.Entry)
    $writer.Write([uint32]$layer.Data.Length)
    $writer.Write([uint32]$dataOffset)
    $dataOffset += $layer.Data.Length
}
foreach ($layer in $layersOut) { $writer.Write([byte[]]$layer.Data) }
$writer.Flush()
$ico = Join-Path $assets 'hdgraph.ico'
[IO.File]::WriteAllBytes($ico, $out.ToArray())
magick identify $ico
