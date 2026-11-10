# Regenerates Assets/commandhub.ico (multi-size, PNG-encoded frames).
# Run: powershell -ExecutionPolicy Bypass -File Assets/make-icon.ps1
# Drawn to pair with Stint's icon: the same dark rounded tile and amber accent,
# with a 2x2 grid of tiles (one lit amber) where Stint has its progress ring.
Add-Type -AssemblyName System.Drawing

function RoundRect([single]$x, [single]$y, [single]$w, [single]$h, [single]$r) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = 2 * $r
    $p.AddArc($x, $y, $d, $d, 180, 90)
    $p.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $p.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $p.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $p.CloseFigure()
    return $p
}

$sizes = 16, 24, 32, 48, 64, 128, 256
$frames = foreach ($s in $sizes) {
    $bmp = New-Object System.Drawing.Bitmap $s, $s
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.Clear([System.Drawing.Color]::Transparent)

    # Rounded dark tile (same as Stint)
    $g.FillPath((New-Object System.Drawing.SolidBrush ([System.Drawing.ColorTranslator]::FromHtml('#1C1F27'))),
        (RoundRect 0 0 ($s - 1) ($s - 1) ([single]($s * 0.22))))

    # 2x2 grid of small rounded tiles; top-left is amber, the rest are the track colour
    $pad = [single]($s * 0.22)
    $gap = [single]([Math]::Max(1, $s * 0.075))
    $cell = [single](($s - 2 * $pad - $gap) / 2)
    $rad = [single]([Math]::Max(1, $cell * 0.28))
    $amber = New-Object System.Drawing.SolidBrush ([System.Drawing.ColorTranslator]::FromHtml('#F5B14C'))
    $track = New-Object System.Drawing.SolidBrush ([System.Drawing.ColorTranslator]::FromHtml('#3A4150'))
    for ($i = 0; $i -lt 4; $i++) {
        $x = $pad + ($i % 2) * ($cell + $gap)
        $y = $pad + [Math]::Floor($i / 2) * ($cell + $gap)
        $g.FillPath($(if ($i -eq 0) { $amber } else { $track }), (RoundRect $x $y $cell $cell $rad))
    }
    $g.Dispose()

    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    , @($s, $ms.ToArray())
}

$out = New-Object System.IO.MemoryStream
$w = New-Object System.IO.BinaryWriter $out
$w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$frames.Count)
$offset = 6 + 16 * $frames.Count
foreach ($f in $frames) {
    $s = $f[0]; $bytes = $f[1]
    $dim = if ($s -ge 256) { 0 } else { $s }
    $w.Write([byte]$dim); $w.Write([byte]$dim); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([uint16]1); $w.Write([uint16]32)
    $w.Write([uint32]$bytes.Length); $w.Write([uint32]$offset)
    $offset += $bytes.Length
}
foreach ($f in $frames) { $w.Write([byte[]]$f[1]) }
[System.IO.File]::WriteAllBytes((Join-Path $PSScriptRoot 'commandhub.ico'), $out.ToArray())

# A large preview PNG next to it (handy for the README).
$big = New-Object System.Drawing.Bitmap 256, 256
$bg = [System.Drawing.Graphics]::FromImage($big)
$bg.DrawImage([System.Drawing.Image]::FromStream((New-Object System.IO.MemoryStream (, $frames[-1][1]))), 0, 0, 256, 256)
$bg.Dispose()
$big.Save((Join-Path $PSScriptRoot 'commandhub.png'), [System.Drawing.Imaging.ImageFormat]::Png)
"Wrote commandhub.ico ($($out.Length) bytes)"
