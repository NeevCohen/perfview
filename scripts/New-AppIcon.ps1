# Regenerate the shared executable, installer, and tray artwork on Windows.
# The checked-in ICO lets normal builds run without regenerating assets.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$output = Join-Path $PSScriptRoot '..\assets\Perfview.ico'
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $output) | Out-Null
$sizes = @(16, 20, 24, 32, 48, 64, 128, 256)
$frames = foreach ($size in $sizes) {
    $bitmap = [Drawing.Bitmap]::new($size, $size)
    $graphics = [Drawing.Graphics]::FromImage($bitmap)
    $path = [Drawing.Drawing2D.GraphicsPath]::new()
    $brush = [Drawing.SolidBrush]::new([Drawing.Color]::FromArgb(25, 34, 43))
    $pen = [Drawing.Pen]::new([Drawing.Color]::FromArgb(75, 221, 180), 2.5)
    $png = [IO.MemoryStream]::new()
    try {
        $graphics.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $graphics.ScaleTransform($size / 32.0, $size / 32.0)
        $path.AddArc(0, 0, 16, 16, 180, 90)
        $path.AddArc(16, 0, 16, 16, 270, 90)
        $path.AddArc(16, 16, 16, 16, 0, 90)
        $path.AddArc(0, 16, 16, 16, 90, 90)
        $path.CloseFigure()
        $graphics.FillPath($brush, $path)
        $points = [Drawing.PointF[]]@(
            [Drawing.PointF]::new(5, 22), [Drawing.PointF]::new(10, 22),
            [Drawing.PointF]::new(14, 10), [Drawing.PointF]::new(18, 25),
            [Drawing.PointF]::new(23, 15), [Drawing.PointF]::new(27, 15)
        )
        $graphics.DrawLines($pen, $points)
        $bitmap.Save($png, [Drawing.Imaging.ImageFormat]::Png)
        ,$png.ToArray()
    }
    finally {
        $png.Dispose()
        $pen.Dispose()
        $brush.Dispose()
        $path.Dispose()
        $graphics.Dispose()
        $bitmap.Dispose()
    }
}
$writer = [IO.BinaryWriter]::new([IO.File]::Create($output))
try {
    $writer.Write([uint16]0)
    $writer.Write([uint16]1) # ICO
    $writer.Write([uint16]$sizes.Count)
    $offset = 6 + 16 * $sizes.Count
    for ($i = 0; $i -lt $sizes.Count; $i++) {
        # An ICO dimension byte of zero represents 256 pixels.
        $dimension = [byte]($sizes[$i] % 256)
        $writer.Write($dimension)
        $writer.Write($dimension)
        $writer.Write([byte]0)
        $writer.Write([byte]0)
        $writer.Write([uint16]1)
        $writer.Write([uint16]32)
        $writer.Write([uint32]$frames[$i].Length)
        $writer.Write([uint32]$offset)
        $offset += $frames[$i].Length
    }
    foreach ($frame in $frames) { $writer.Write([byte[]]$frame) }
}
finally { $writer.Dispose() }
Write-Output "Generated $output"
