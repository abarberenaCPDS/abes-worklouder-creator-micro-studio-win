$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$assetDirectory = Join-Path $PSScriptRoot '..\src\CreatorMicroStudio.App\Resources\Images'
$sourcePath = Join-Path $assetDirectory 'studio_icon.png'
$iconPath = Join-Path $assetDirectory 'studio_icon.ico'

if (-not (Test-Path -LiteralPath $sourcePath)) {
    throw "Icon artwork not found: $sourcePath"
}

$source = [System.Drawing.Bitmap]::FromFile($sourcePath)
$frames = @()
try {
    foreach ($size in @(16, 24, 32, 48, 64, 128, 256)) {
        $bitmap = [System.Drawing.Bitmap]::new(
            $size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
        try {
            $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
            try {
                $graphics.Clear([System.Drawing.Color]::Transparent)
                $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
                $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
                $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
                $graphics.DrawImage($source, 0, 0, $size, $size)

                $stream = [System.IO.MemoryStream]::new()
                try {
                    $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
                    $frames += [pscustomobject]@{ Size = $size; Png = $stream.ToArray() }
                }
                finally { $stream.Dispose() }
            }
            finally { $graphics.Dispose() }
        }
        finally { $bitmap.Dispose() }
    }

    $stream = [System.IO.File]::Create($iconPath)
    try {
        $writer = [System.IO.BinaryWriter]::new($stream)
        $writer.Write([UInt16]0) # reserved
        $writer.Write([UInt16]1) # ICO resource
        $writer.Write([UInt16]$frames.Count)

        $offset = 6 + 16 * $frames.Count
        foreach ($frame in $frames) {
            $dimension = if ($frame.Size -eq 256) { 0 } else { $frame.Size }
            $writer.Write([Byte]$dimension)
            $writer.Write([Byte]$dimension)
            $writer.Write([Byte]0) # palette entries
            $writer.Write([Byte]0) # reserved
            $writer.Write([UInt16]1) # color planes
            $writer.Write([UInt16]32) # bits per pixel
            $writer.Write([UInt32]$frame.Png.Length)
            $writer.Write([UInt32]$offset)
            $offset += $frame.Png.Length
        }
        foreach ($frame in $frames) { $writer.Write($frame.Png) }
        $writer.Flush()
    }
    finally { $stream.Dispose() }
}
finally { $source.Dispose() }

Write-Host "Generated $iconPath (7 PNG sizes)"
