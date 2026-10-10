<#
.SYNOPSIS
  Regenerate the package icons in src\PomodoroDock\Assets (a tomato on a transparent background).

.DESCRIPTION
  Needs only Windows PowerShell / PowerShell 7 on Windows (System.Drawing). Everything is drawn at
  1024 px and downsampled with high-quality bicubic scaling, so small sizes stay smooth.
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$assets = Join-Path $PSScriptRoot '..\src\PomodoroDock\Assets'
New-Item -ItemType Directory -Force $assets | Out-Null
$assets = (Resolve-Path $assets).Path
$base = 1024

function New-Tomato {
  $bmp = New-Object System.Drawing.Bitmap $base, $base, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $g.SmoothingMode = 'AntiAlias'
  $g.Clear([System.Drawing.Color]::Transparent)

  # Body: a slightly flattened red ellipse with a radial-looking highlight.
  $body = New-Object System.Drawing.RectangleF 110, 230, 804, 720
  $path = New-Object System.Drawing.Drawing2D.GraphicsPath
  $path.AddEllipse($body)
  $grad = New-Object System.Drawing.Drawing2D.PathGradientBrush $path
  $grad.CenterPoint = New-Object System.Drawing.PointF 380, 440
  $grad.CenterColor = [System.Drawing.Color]::FromArgb(255, 255, 99, 71)
  $grad.SurroundColors = @([System.Drawing.Color]::FromArgb(255, 200, 30, 30))
  $g.FillPath($grad, $path)

  # Leaves: a five-pointed green star on top.
  $green = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 46, 139, 60))
  $cx = 512; $cy = 290
  $points = @()
  for ($i = 0; $i -lt 10; $i++) {
    $r = if ($i % 2 -eq 0) { 230 } else { 80 }
    $a = [Math]::PI * ($i * 36 - 90) / 180
    $points += New-Object System.Drawing.PointF ($cx + $r * [Math]::Cos($a) * 1.0), ($cy + $r * [Math]::Sin($a) * 0.55)
  }
  $g.FillPolygon($green, [System.Drawing.PointF[]]$points)

  # Stem.
  $stem = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(255, 30, 96, 40)), 44
  $stem.StartCap = 'Round'; $stem.EndCap = 'Round'
  $g.DrawLine($stem, 512, 280, 540, 130)

  $g.Dispose()
  return $bmp
}

function Save-Resized($src, [int]$w, [int]$h, [int]$mark, [string]$name) {
  $out = New-Object System.Drawing.Bitmap $w, $h, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
  $g = [System.Drawing.Graphics]::FromImage($out)
  $g.Clear([System.Drawing.Color]::Transparent)
  $g.InterpolationMode = 'HighQualityBicubic'
  $g.SmoothingMode = 'HighQuality'
  $g.PixelOffsetMode = 'HighQuality'
  $g.DrawImage($src, [int](($w - $mark) / 2), [int](($h - $mark) / 2), $mark, $mark)
  $g.Dispose()
  $out.Save((Join-Path $assets $name), [System.Drawing.Imaging.ImageFormat]::Png)
  $out.Dispose()
}

$tomato = New-Tomato
$squares = [ordered]@{
  'StoreLogo.png'                                       = 50
  'LockScreenLogo.scale-200.png'                        = 48
  'Square44x44Logo.scale-200.png'                       = 88
  'Square44x44Logo.targetsize-24_altform-unplated.png'  = 24
  'Square150x150Logo.scale-200.png'                     = 300
}
foreach ($name in $squares.Keys) { Save-Resized $tomato $squares[$name] $squares[$name] $squares[$name] $name }
Save-Resized $tomato 620 300 200 'Wide310x150Logo.scale-200.png'
Save-Resized $tomato 1240 600 360 'SplashScreen.scale-200.png'
$tomato.Dispose()
Write-Host "Wrote $($squares.Count + 2) icons to $assets"
