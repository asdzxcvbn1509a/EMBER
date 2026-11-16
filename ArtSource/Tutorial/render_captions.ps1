# EMBER tutorial video - draws every caption of captions.json as a full-frame 1920x1080 transparent PNG.
# Blender's text and the VSE cannot shape Thai (vowel and tone marks collide) and TextMeshPro in the game cannot
# either, so captions are drawn with WPF (same approach as ArtSource/Trailer/render_thai_title.ps1).
#   powershell -STA -NoProfile -ExecutionPolicy Bypass -File ArtSource/Tutorial/render_captions.ps1 [-Only id]
# Thai text: Leelawadee UI (the font of the game's Thai credits). [KEY] = amber keycap, {MENU} = amber Futura Bold
# (the game's own font, loaded from the class package). Writes Build/Tutorial/captions/<id>.png and captions_meta.json
# (box rectangles for the overlap check). This file is ASCII only; all Thai text comes from captions.json (UTF-8).
param(
    [string]$Captions = (Join-Path $PSScriptRoot "captions.json"),
    [string]$Out = (Join-Path $PSScriptRoot "..\..\Build\Tutorial\captions"),
    [string]$Only = ""
)
$ErrorActionPreference = "Stop"
Add-Type -AssemblyName PresentationCore, PresentationFramework, WindowsBase

$W = 1920; $H = 1080
$culture = [Globalization.CultureInfo]::GetCultureInfo("th-TH")
function Col([int]$a, [int]$r, [int]$g, [int]$b) { [Windows.Media.Color]::FromArgb($a, $r, $g, $b) }
function Brush($c) { $b = New-Object Windows.Media.SolidColorBrush $c; $b.Freeze(); $b }
$Amber = Col 255 255 184 97        # UI amber #FFB861
$Pale = Col 255 230 224 214        # UI pale  #E6E0D6
$Navy = Col 204 27 33 64           # #1B2140 at 80 %
$Ink = Col 255 22 16 30
$bAmber = Brush $Amber; $bPale = Brush $Pale; $bNavy = Brush $Navy; $bInk = Brush $Ink
$bEdge = Brush (Col 90 255 184 97)

$thai = New-Object Windows.Media.FontFamily "Leelawadee UI"
$tfThai = New-Object Windows.Media.Typeface $thai, ([Windows.FontStyles]::Normal), ([Windows.FontWeights]::Normal), ([Windows.FontStretches]::Normal)
$tfThaiBold = New-Object Windows.Media.Typeface $thai, ([Windows.FontStyles]::Normal), ([Windows.FontWeights]::Bold), ([Windows.FontStretches]::Normal)
$fontDir = (Resolve-Path (Join-Path $PSScriptRoot "..\..\EMBER\Assets\Downloads\Session3\Fonts")).Path + "\"
$futuraFam = [Windows.Media.Fonts]::GetFontFamilies($fontDir) | Select-Object -First 1
if ($null -eq $futuraFam) { throw "Futura Bold.TTF not found in $fontDir" }
$tfFutura = New-Object Windows.Media.Typeface $futuraFam, ([Windows.FontStyles]::Normal), ([Windows.FontWeights]::Bold), ([Windows.FontStretches]::Normal)

function Text([string]$s, $face, [double]$size, $brush) {
    New-Object Windows.Media.FormattedText $s, $culture, ([Windows.FlowDirection]::LeftToRight), $face, $size, $brush, 1.0
}

# one line -> runs: @{ kind = text|key|menu; ft = FormattedText; w = width }
function Runs([string]$line, [double]$size, $brush, $face) {
    $runs = @()
    $rx = [regex]'\[([^\]]+)\]|\{([^}]+)\}'
    $pos = 0
    foreach ($m in $rx.Matches($line)) {
        if ($m.Index -gt $pos) {
            $t = $line.Substring($pos, $m.Index - $pos)
            $ft = Text $t $face $size $brush
            $runs += @{ kind = "text"; ft = $ft; w = $ft.WidthIncludingTrailingWhitespace }
        }
        if ($m.Groups[1].Success) {
            $ft = Text $m.Groups[1].Value $tfFutura ($size * 0.62) $bInk
            $runs += @{ kind = "key"; ft = $ft; w = $ft.Width + $size * 0.62 }
        } else {
            $ft = Text $m.Groups[2].Value $tfFutura ($size * 0.70) $bAmber
            $runs += @{ kind = "menu"; ft = $ft; w = $ft.WidthIncludingTrailingWhitespace }
        }
        $pos = $m.Index + $m.Length
    }
    if ($pos -lt $line.Length) {
        $t = $line.Substring($pos)
        $ft = Text $t $face $size $brush
        $runs += @{ kind = "text"; ft = $ft; w = $ft.WidthIncludingTrailingWhitespace }
    }
    return , $runs
}

function LineWidth($runs) { $sum = 0.0; foreach ($r in @($runs)) { $sum += $r.w }; $sum }

# draws one line of runs with its left edge at x and its baseline at y
function DrawLine($dc, $runs, [double]$x, [double]$y, [double]$size) {
    foreach ($r in @($runs)) {
        if ($r.kind -eq "key") {
            $h = $size * 1.02
            $kx = $x + $size * 0.06
            $kw = $r.w - $size * 0.12
            $mid = $y - $size * 0.34
            $rect = New-Object Windows.Rect $kx, ($mid - $h / 2), $kw, $h
            $dc.DrawRoundedRectangle($bAmber, $null, $rect, $size * 0.16, $size * 0.16)
            $tx = $kx + ($kw - $r.ft.Width) / 2
            $ty = $mid - $r.ft.Height / 2
            $dc.DrawText($r.ft, (New-Object Windows.Point $tx, $ty))
        } elseif ($r.kind -eq "menu") {
            $dc.DrawText($r.ft, (New-Object Windows.Point $x, ($y - $r.ft.Baseline)))
        } else {
            $dc.DrawText($r.ft, (New-Object Windows.Point $x, ($y - $r.ft.Baseline)))
        }
        $x += $r.w
    }
}

function Save($rtb, [string]$path) {
    $straight = New-Object Windows.Media.Imaging.FormatConvertedBitmap $rtb, ([Windows.Media.PixelFormats]::Bgra32), $null, 0
    $enc = New-Object Windows.Media.Imaging.PngBitmapEncoder
    $enc.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($straight))
    $fs = [IO.File]::Create($path)
    try { $enc.Save($fs) } finally { $fs.Dispose() }
}

function NewVisual() {
    $dv = New-Object Windows.Media.DrawingVisual
    [Windows.Media.TextOptions]::SetTextRenderingMode($dv, [Windows.Media.TextRenderingMode]::Grayscale)
    [Windows.Media.TextOptions]::SetTextFormattingMode($dv, [Windows.Media.TextFormattingMode]::Ideal)
    $dv
}

$data = [IO.File]::ReadAllText($Captions, [Text.Encoding]::UTF8) | ConvertFrom-Json
if (-not (Test-Path $Out)) { New-Item -ItemType Directory -Force $Out | Out-Null }
$Out = (Resolve-Path $Out).Path
$meta = [ordered]@{}
$warnings = 0

foreach ($c in $data.captions) {
    if ($Only -and $c.id -ne $Only) { continue }
    $kind = if ($c.kind) { $c.kind } else { "sub" }
    $pos = if ($c.pos) { $c.pos } else { "bottom" }
    $lines = $c.text -split "`n"
    $layout = @()
    $box = $null
    $shadow = NewVisual
    $dv = NewVisual

    if ($kind -eq "sub") {
        $size = 38.0; $lead = $size * 1.5
        $maxW = if ($pos -in @("br", "tr")) { 900 } else { 1340 }
        foreach ($ln in $lines) { $layout += , (Runs $ln $size $bPale $tfThai) }
        $textW = ($layout | ForEach-Object { LineWidth $_ } | Measure-Object -Maximum).Maximum
        $padL = 40; $padR = 32; $padT = 18; $padB = 22
        $bw = [Math]::Ceiling($textW + $padL + $padR); $bh = [Math]::Ceiling($lines.Count * $lead + $padT + $padB - $size * 0.25)
        if ($textW -gt $maxW) { Write-Warning "$($c.id): line width $([int]$textW) px > $maxW px"; $warnings++ }
        switch ($pos) {
            "top"    { $bx = ($W - $bw) / 2; $by = 140 }
            "br"     { $bx = $W - 48 - $bw; $by = 975 - $bh }
            "tr"     { $bx = $W - 48 - $bw; $by = 140 }
            default  { $bx = ($W - $bw) / 2; $by = 930 - $bh }
        }
        $box = New-Object Windows.Rect $bx, $by, $bw, $bh
        # box with a soft shadow, a thin amber edge and an amber accent bar
        $sdc = $shadow.RenderOpen()
        $sdc.DrawRoundedRectangle($bNavy, (New-Object Windows.Media.Pen $bEdge, 1.5), $box, 14, 14)
        $sdc.Close()
        $fx = New-Object Windows.Media.Effects.DropShadowEffect
        $fx.BlurRadius = 22; $fx.ShadowDepth = 4; $fx.Opacity = 0.55; $fx.Direction = 270; $fx.Color = (Col 255 0 0 0)
        $shadow.Effect = $fx
        $dc = $dv.RenderOpen()
        $dc.DrawRoundedRectangle($bAmber, $null, (New-Object Windows.Rect ($bx + 14), ($by + 16), 5, ($bh - 32)), 2.5, 2.5)
        for ($i = 0; $i -lt $layout.Count; $i++) {
            $lw = LineWidth $layout[$i]
            $x = $bx + $padL + ($textW - $lw) / 2
            $y = $by + $padT + $size * 1.05 + $i * $lead
            DrawLine $dc $layout[$i] $x $y $size
        }
        $dc.Close()
    }
    else {
        # title: first line large amber, the rest pale; card: on black, centred; both without a box
        $cx = if ($pos -eq "right") { 1440 } else { $W / 2 }
        $cy = if ($kind -eq "card") { 540 } else { 560 }
        $sizes = @(); $brushes = @(); $faces = @()
        for ($i = 0; $i -lt $lines.Count; $i++) {
            if ($kind -eq "title") { $s = if ($i -eq 0) { 78.0 } else { 40.0 }; $f = if ($i -eq 0) { $tfThaiBold } else { $tfThai } }
            else { $s = if ($i -eq 0) { 46.0 } else { 38.0 }; $f = if ($i -eq 0) { $tfThaiBold } else { $tfThai } }
            $sizes += $s; $faces += $f; $brushes += $(if ($i -eq 0) { $bAmber } else { $bPale })
            $layout += , (Runs $lines[$i] $s $brushes[$i] $f)
        }
        $leads = $sizes | ForEach-Object { $_ * 1.45 }
        $total = ($leads | Measure-Object -Sum).Sum
        $textW = ($layout | ForEach-Object { LineWidth $_ } | Measure-Object -Maximum).Maximum
        $top = $cy - $total / 2
        $box = New-Object Windows.Rect ($cx - $textW / 2 - 60), ($top - 30), ($textW + 120), ($total + 60)
        if ($kind -eq "title") {
            # soft dark band behind the title so it reads over the scene
            $sdc = $shadow.RenderOpen()
            $grad = New-Object Windows.Media.RadialGradientBrush
            $grad.GradientStops.Add((New-Object Windows.Media.GradientStop (Col 170 8 8 18), 0.0))
            $grad.GradientStops.Add((New-Object Windows.Media.GradientStop (Col 110 8 8 18), 0.55))
            $grad.GradientStops.Add((New-Object Windows.Media.GradientStop (Col 0 8 8 18), 1.0))
            $sdc.DrawEllipse($grad, $null, (New-Object Windows.Point $cx, $cy), ($textW / 2 + 260), ($total / 2 + 120))
            $sdc.Close()
        }
        $dc = $dv.RenderOpen()
        $y = $top
        for ($i = 0; $i -lt $layout.Count; $i++) {
            $lw = LineWidth $layout[$i]
            DrawLine $dc $layout[$i] ($cx - $lw / 2) ($y + $sizes[$i] * 1.05) $sizes[$i]
            if ($i -eq 0 -and $layout.Count -gt 1) {
                # thin amber rule under the first line, like the game's stage card
                $ry = $y + $leads[0] - $sizes[0] * 0.08
                $dc.DrawRectangle((Brush (Col 150 255 184 97)), $null, (New-Object Windows.Rect ($cx - 180), $ry, 360, 2))
                $y += $sizes[0] * 0.25
            }
            $y += $leads[$i]
        }
        $dc.Close()
    }

    $rtb = New-Object Windows.Media.Imaging.RenderTargetBitmap $W, $H, 96, 96, ([Windows.Media.PixelFormats]::Pbgra32)
    $rtb.Render($shadow)
    $rtb.Render($dv)
    Save $rtb (Join-Path $Out ($c.id + ".png"))
    $meta[$c.id] = [ordered]@{ kind = $kind; pos = $pos; rect = @([int]$box.X, [int]$box.Y, [int]($box.X + $box.Width), [int]($box.Y + $box.Height)); lines = $lines.Count }
}

$metaPath = Join-Path $Out "captions_meta.json"
if ($Only -and (Test-Path $metaPath)) {
    $old = [IO.File]::ReadAllText($metaPath, [Text.Encoding]::UTF8) | ConvertFrom-Json
    foreach ($p in $old.PSObject.Properties) { if (-not $meta.Contains($p.Name)) { $meta[$p.Name] = $p.Value } }
}
[IO.File]::WriteAllText($metaPath, ($meta | ConvertTo-Json -Depth 5), (New-Object Text.UTF8Encoding $false))
"wrote $($meta.Count) caption image(s) to $Out ($warnings warning(s))"
