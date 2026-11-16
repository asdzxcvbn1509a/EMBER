# EMBER - renders the team names (Thai) to a transparent PNG used by the ending credits.
# TextMeshPro has no Thai text shaping (stacked vowels / tone marks overlap) and the class Futura font has
# no Thai glyphs, so the names are drawn here with WPF, which shapes Thai correctly, using Leelawadee UI.
#   powershell -STA -NoProfile -ExecutionPolicy Bypass -File Tools/render_team_credits.ps1
param(
    [string]$Team = (Join-Path $PSScriptRoot "team.txt"),
    [string]$Out = (Join-Path $PSScriptRoot "..\EMBER\Assets\_Ember\Art\UI\T_TeamCredits.png"),
    [double]$Scale = 2.0,          # pixels per UI unit (rendered at 2x, shown at 1x on a 1920x1080 canvas)
    [double]$Width = 1000,         # UI units
    [double]$NameSize = 34,
    [double]$IdSize = 21,
    [double]$Gap = 20
)
$ErrorActionPreference = "Stop"
Add-Type -AssemblyName PresentationCore, PresentationFramework, WindowsBase

$members = [IO.File]::ReadAllLines((Resolve-Path $Team).Path, [Text.Encoding]::UTF8) |
    Where-Object { $_.Trim() -and -not $_.TrimStart().StartsWith("#") } |
    ForEach-Object {
        $p = $_.Split("|")
        [pscustomobject]@{ Name = $p[0].Trim(); Id = $(if ($p.Count -gt 1) { $p[1].Trim() } else { "" }) }
    }
if (-not $members) { throw "no members in $Team" }

$family = New-Object Windows.Media.FontFamily "Leelawadee UI"
$bold = New-Object Windows.Media.Typeface $family, ([Windows.FontStyles]::Normal), ([Windows.FontWeights]::Bold), ([Windows.FontStretches]::Normal)
$regular = New-Object Windows.Media.Typeface $family, ([Windows.FontStyles]::Normal), ([Windows.FontWeights]::Normal), ([Windows.FontStretches]::Normal)
$culture = [Globalization.CultureInfo]::GetCultureInfo("th-TH")
$white = [Windows.Media.Brushes]::White
$dim = New-Object Windows.Media.SolidColorBrush ([Windows.Media.Color]::FromArgb(150, 255, 255, 255))

function Line([string]$text, $face, [double]$size, $brush) {
    New-Object Windows.Media.FormattedText $text, $culture, ([Windows.FlowDirection]::LeftToRight), $face, ($size * $Scale), $brush, 1.0
}

# measure
$rows = foreach ($m in $members) { [pscustomobject]@{ Name = (Line $m.Name $bold $NameSize $white); Id = $(if ($m.Id) { Line $m.Id $regular $IdSize $dim }) } }
$pad = 14 * $Scale
$w = [int][Math]::Ceiling($Width * $Scale)
$h = $pad * 2
foreach ($r in $rows) { $h += $r.Name.Height; if ($r.Id) { $h += $r.Id.Height }; $h += $Gap * $Scale }
$h = [int][Math]::Ceiling($h - $Gap * $Scale)

# draw
$dv = New-Object Windows.Media.DrawingVisual
[Windows.Media.TextOptions]::SetTextRenderingMode($dv, [Windows.Media.TextRenderingMode]::Grayscale)
[Windows.Media.TextOptions]::SetTextFormattingMode($dv, [Windows.Media.TextFormattingMode]::Ideal)
$dc = $dv.RenderOpen()
$y = $pad
foreach ($r in $rows) {
    $dc.DrawText($r.Name, (New-Object Windows.Point ((($w - $r.Name.Width) / 2), $y)))
    $y += $r.Name.Height
    if ($r.Id) {
        $dc.DrawText($r.Id, (New-Object Windows.Point ((($w - $r.Id.Width) / 2), $y)))
        $y += $r.Id.Height
    }
    $y += $Gap * $Scale
}
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
"wrote $Out  ($w x $h px, $($members.Count) members)"
