param([string]$OutputDirectory = $PSScriptRoot)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$sourcePath = Join-Path $PSScriptRoot '../logo-concepts/01-find-clip.png'
$source = [System.Drawing.Bitmap]::new((Resolve-Path $sourcePath).Path)
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null

function New-Canvas([int]$Width, [int]$Height, [System.Drawing.Color]$Background) {
    $bitmap = [System.Drawing.Bitmap]::new($Width, $Height)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.Clear($Background)
    $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $graphics.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit
    return @{ Bitmap = $bitmap; Graphics = $graphics }
}

try {
    $source.Save((Join-Path $OutputDirectory 'ClipStack.png'), [System.Drawing.Imaging.ImageFormat]::Png)
    $frames = foreach ($size in @(16, 20, 24, 32, 40, 48, 64, 128, 256)) {
        $canvas = New-Canvas $size $size ([System.Drawing.Color]::Transparent)
        $stream = [System.IO.MemoryStream]::new()
        try {
            $canvas.Graphics.DrawImage($source, [System.Drawing.Rectangle]::new(0, 0, $size, $size))
            $canvas.Bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
            @{ Size = $size; Bytes = $stream.ToArray() }
        } finally {
            $stream.Dispose()
            $canvas.Graphics.Dispose()
            $canvas.Bitmap.Dispose()
        }
    }
    $iconStream = [System.IO.File]::Create((Join-Path $OutputDirectory 'ClipStack.ico'))
    $writer = [System.IO.BinaryWriter]::new($iconStream)
    try {
        $writer.Write([uint16]0)
        $writer.Write([uint16]1)
        $writer.Write([uint16]$frames.Count)
        $offset = 6 + 16 * $frames.Count
        foreach ($frame in $frames) {
            $dimension = if ($frame.Size -eq 256) { 0 } else { $frame.Size }
            $writer.Write([byte]$dimension)
            $writer.Write([byte]$dimension)
            $writer.Write([byte]0)
            $writer.Write([byte]0)
            $writer.Write([uint16]1)
            $writer.Write([uint16]32)
            $writer.Write([uint32]$frame.Bytes.Length)
            $writer.Write([uint32]$offset)
            $offset += $frame.Bytes.Length
        }
        foreach ($frame in $frames) { $writer.Write([byte[]]$frame.Bytes) }
    } finally {
        $writer.Dispose()
        $iconStream.Dispose()
    }

    $wordmark = New-Canvas 900 240 ([System.Drawing.Color]::Transparent)
    $font = [System.Drawing.Font]::new('Bahnschrift', 102, [System.Drawing.FontStyle]::Bold, [System.Drawing.GraphicsUnit]::Pixel)
    $ink = [System.Drawing.SolidBrush]::new([System.Drawing.ColorTranslator]::FromHtml('#075248'))
    try {
        $wordmark.Graphics.DrawImage($source, [System.Drawing.Rectangle]::new(0, 0, 240, 240))
        $wordmark.Graphics.DrawString('ClipStack', $font, $ink, [single]265, [single]54)
        $wordmark.Bitmap.Save((Join-Path $OutputDirectory 'ClipStack-wordmark.png'), [System.Drawing.Imaging.ImageFormat]::Png)
    } finally {
        $ink.Dispose()
        $font.Dispose()
        $wordmark.Graphics.Dispose()
        $wordmark.Bitmap.Dispose()
    }

    $small = New-Canvas 55 55 ([System.Drawing.Color]::White)
    try {
        $small.Graphics.DrawImage($source, [System.Drawing.Rectangle]::new(0, 0, 55, 55))
        $small.Bitmap.Save((Join-Path $OutputDirectory 'installer-small.bmp'), [System.Drawing.Imaging.ImageFormat]::Bmp)
    } finally {
        $small.Graphics.Dispose()
        $small.Bitmap.Dispose()
    }

    $wizard = New-Canvas 164 314 ([System.Drawing.ColorTranslator]::FromHtml('#EFF8F5'))
    $font = [System.Drawing.Font]::new('Bahnschrift', 25, [System.Drawing.FontStyle]::Bold, [System.Drawing.GraphicsUnit]::Pixel)
    $ink = [System.Drawing.SolidBrush]::new([System.Drawing.ColorTranslator]::FromHtml('#075248'))
    try {
        $wizard.Graphics.DrawImage($source, [System.Drawing.Rectangle]::new(12, 42, 140, 140))
        $wizard.Graphics.DrawString('ClipStack', $font, $ink, [single]25, [single]194)
        $wizard.Bitmap.Save((Join-Path $OutputDirectory 'installer-wizard.bmp'), [System.Drawing.Imaging.ImageFormat]::Bmp)
    } finally {
        $ink.Dispose()
        $font.Dispose()
        $wizard.Graphics.Dispose()
        $wizard.Bitmap.Dispose()
    }
} finally { $source.Dispose() }

Get-ChildItem $OutputDirectory -File | Select-Object Name, Length