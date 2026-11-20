$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$iconPath = Join-Path $PSScriptRoot '..\src\KeyTrail.Desktop\Assets\KeyTrail.ico'
$null = New-Item -ItemType Directory -Force -Path (Split-Path $iconPath)
$frames = @()
foreach ($size in @(16, 24, 32, 48, 64, 128, 256)) {
    $bitmap = New-Object System.Drawing.Bitmap($size, $size)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.ScaleTransform(($size / 64.0), ($size / 64.0))
    $shape = New-Object System.Drawing.Drawing2D.GraphicsPath
    $shape.AddArc(2, 2, 20, 20, 180, 90)
    $shape.AddArc(42, 2, 20, 20, 270, 90)
    $shape.AddArc(42, 42, 20, 20, 0, 90)
    $shape.AddArc(2, 42, 20, 20, 90, 90)
    $shape.CloseFigure()
    $ground = New-Object System.Drawing.SolidBrush([System.Drawing.ColorTranslator]::FromHtml('#173D31'))
    $ink = New-Object System.Drawing.SolidBrush([System.Drawing.ColorTranslator]::FromHtml('#D5E8B6'))
    $graphics.FillPath($ground, $shape)
    $graphics.FillRectangle($ink, 13, 16, 10, 12)
    $graphics.FillRectangle($ink, 27, 16, 10, 12)
    $graphics.FillRectangle($ink, 41, 16, 10, 12)
    $graphics.FillRectangle($ink, 13, 35, 38, 12)
    $stream = New-Object System.IO.MemoryStream
    $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
    $frames += [PSCustomObject]@{ Size = $size; Bytes = $stream.ToArray() }
    $stream.Dispose(); $shape.Dispose(); $ground.Dispose(); $ink.Dispose(); $graphics.Dispose(); $bitmap.Dispose()
}
$output = [System.IO.File]::Create([System.IO.Path]::GetFullPath($iconPath))
$writer = New-Object System.IO.BinaryWriter($output)
$writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$frames.Count)
$offset = 6 + 16 * $frames.Count
foreach ($frame in $frames) {
    $dimension = if ($frame.Size -eq 256) { 0 } else { $frame.Size }
    $writer.Write([byte]$dimension); $writer.Write([byte]$dimension)
    $writer.Write([byte]0); $writer.Write([byte]0)
    $writer.Write([uint16]1); $writer.Write([uint16]32)
    $writer.Write([uint32]$frame.Bytes.Length); $writer.Write([uint32]$offset)
    $offset += $frame.Bytes.Length
}
foreach ($frame in $frames) { $writer.Write([byte[]]$frame.Bytes) }
$writer.Dispose(); $output.Dispose()
Write-Output $iconPath
