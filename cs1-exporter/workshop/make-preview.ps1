# Generates cs1-exporter/workshop/PreviewImage.png (644 x 360, the size the CS1 Workshop shows) with System.Drawing.
#   powershell -ExecutionPolicy Bypass -File cs1-exporter\workshop\make-preview.ps1
# The build copies PreviewImage.png next to the DLL in the mod folder, where CS1's Content Manager and the
# Workshop uploader pick it up.
Add-Type -AssemblyName System.Drawing
$W = 644; $H = 360
$bmp = New-Object System.Drawing.Bitmap $W, $H
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.SmoothingMode = 'AntiAlias'
$g.TextRenderingHint = 'AntiAliasGridFit'

# Background: deep blue, slightly lighter at the top.
$rect = New-Object System.Drawing.Rectangle 0, 0, $W, $H
$bg = New-Object System.Drawing.Drawing2D.LinearGradientBrush $rect, ([System.Drawing.Color]::FromArgb(255, 24, 44, 78)), ([System.Drawing.Color]::FromArgb(255, 12, 22, 40)), 90
$g.FillRectangle($bg, $rect)

# A simple street grid that fades from "CS1" (left, warm) to "CS2" (right, cool).
$rnd = New-Object System.Random 81
for ($i = 0; $i -lt 14; $i++) {
  $x = 20 + $i * 46 + $rnd.Next(-6, 6)
  $t = $i / 13.0
  $c = [System.Drawing.Color]::FromArgb(60, [int](230 - 120 * $t), [int](170 + 20 * $t), [int](90 + 150 * $t))
  $pen = New-Object System.Drawing.Pen $c, ([single](2 + ($i % 3)))
  $g.DrawLine($pen, $x, 0, $x + 30, $H)
}
for ($j = 0; $j -lt 8; $j++) {
  $y = 18 + $j * 46
  $pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(45, 200, 210, 230)), ([single](2 + ($j % 2)))
  $g.DrawLine($pen, 0, $y, $W, $y + 12)
}

# Content card.
$card = New-Object System.Drawing.Rectangle 40, 78, 564, 204
$g.FillRectangle((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(215, 10, 18, 34))), $card)
$g.FillRectangle((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 86, 182, 255))), 40, 78, 6, 204)

$white = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 245, 248, 252))
$muted = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 170, 186, 210))
$accent = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 86, 182, 255))
$fTitle = New-Object System.Drawing.Font 'Segoe UI Semibold', 34
$fSub = New-Object System.Drawing.Font 'Segoe UI', 15
$fSmall = New-Object System.Drawing.Font 'Segoe UI', 11

$g.DrawString('CS1', $fTitle, $white, 64, 96)
$g.DrawString([string][char]0x2192, $fTitle, $accent, 150, 94)
$g.DrawString('CS2 Exporter', $fTitle, $white, 206, 96)
$g.DrawString('Export your Cities: Skylines city for Cities: Skylines II', $fSub, $muted, 66, 164)
$d = '  ' + [string][char]0x00B7 + '  '
$g.DrawString(('roads', 'terrain & water', 'zoning', 'districts', 'transit') -join $d, $fSmall, $muted, 66, 214)
$g.DrawString(('TM:PE', 'Node Controller', 'IMT', 'buildings & assets') -join $d, $fSmall, $muted, 66, 238)

$out = Join-Path $PSScriptRoot 'PreviewImage.png'
$bmp.Save($out, [System.Drawing.Imaging.ImageFormat]::Png)
$g.Dispose(); $bmp.Dispose()
Write-Output "wrote $out"
