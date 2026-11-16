# EMBER trailer - renders the Thai title to a transparent PNG for the end card.
# Blender's text objects and the VSE do not shape Thai (vowel and tone marks would collide), so the title is drawn
# with WPF, which shapes Thai correctly (same approach as Tools/render_team_credits.ps1).
#   powershell -STA -NoProfile -ExecutionPolicy Bypass -File ArtSource/Trailer/render_thai_title.ps1
param(
    [string]$Out = (Join-Path $PSScriptRoot "assets\thai_title.png"),
    [double]$Size = 120,            # font size in pixels before $Scale
    [double]$Scale = 2.0,
    [string]$Font = "Leelawadee UI"
)
$ErrorActionPreference = "Stop"
Add-Type -AssemblyName PresentationCore, PresentationFramework, WindowsBase

# "ลูกไฟดวงสุดท้าย" spelled as code points, so the script file encoding cannot garble it
$codes = 0x0E25, 0x0E39, 0x0E01, 0x0E44, 0x0E1F, 0x0E14, 0x0E27, 0x0E07, 0x0E2A, 0x0E38, 0x0E14, 0x0E17, 0x0E49, 0x0E32, 0x0E22
$text = -join ($codes | ForEach-Object { [char]$_ })

$family = New-Object Windows.Media.FontFamily $Font
$face = New-Object Windows.Media.Typeface $family, ([Windows.FontStyles]::Normal), ([Windows.FontWeights]::Bold), ([Windows.FontStretches]::Normal)
$culture = [Globalization.CultureInfo]::GetCultureInfo("th-TH")
$ft = New-Object Windows.Media.FormattedText $text, $culture, ([Windows.FlowDirection]::LeftToRight), $face, ($Size * $Scale), ([Windows.Media.Brushes]::White), 1.0

$pad = [int](24 * $Scale)
$w = [int][Math]::Ceiling($ft.WidthIncludingTrailingWhitespace) + 2 * $pad
$h = [int][Math]::Ceiling($ft.Height) + 2 * $pad
$dv = New-Object Windows.Media.DrawingVisual
[Windows.Media.TextOptions]::SetTextRenderingMode($dv, [Windows.Media.TextRenderingMode]::Grayscale)
[Windows.Media.TextOptions]::SetTextFormattingMode($dv, [Windows.Media.TextFormattingMode]::Ideal)
$dc = $dv.RenderOpen()
$dc.DrawText($ft, (New-Object Windows.Point ($pad, $pad)))
$dc.Close()

$rtb = New-Object Windows.Media.Imaging.RenderTargetBitmap $w, $h, 96, 96, ([Windows.Media.PixelFormats]::Pbgra32)
$rtb.Render($dv)
$straight = New-Object Windows.Media.Imaging.FormatConvertedBitmap $rtb, ([Windows.Media.PixelFormats]::Bgra32), $null, 0
$enc = New-Object Windows.Media.Imaging.PngBitmapEncoder
$enc.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($straight))
$dir = Split-Path $Out -Parent
if (-not (Test-Path $dir)) { New-Item -ItemType Directory -Force $dir | Out-Null }
$fs = [IO.File]::Create($Out)
try { $enc.Save($fs) } finally { $fs.Dispose() }
"wrote $Out  ($w x $h px)"
