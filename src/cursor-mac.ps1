# Genera un cursor estilo Mac (flecha negra con borde blanco y sombra suave), original, en varios tamanos nitidos.
# Uso: . cursor-mac.ps1 ; New-MacArrowCursor "C:\ruta\mac-flecha.cur"
function New-MacArrowCursor([string]$Out) {
    Add-Type -AssemblyName System.Drawing
    $sizes = 32, 40, 48, 56, 64, 80, 96, 128
    # forma de la flecha (alto = 1)
    $shape = @(@(0, 0), @(0, 0.80), @(0.19, 0.63), @(0.32, 0.93), @(0.43, 0.88), @(0.30, 0.59), @(0.55, 0.59))
    $frames = @()
    foreach ($S in $sizes) {
        $bmp = New-Object System.Drawing.Bitmap $S, $S, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
        $g = [System.Drawing.Graphics]::FromImage($bmp)
        $g.SmoothingMode = "AntiAlias"; $g.PixelOffsetMode = "HighQuality"; $g.Clear([System.Drawing.Color]::Transparent)
        $H = $S * 0.66; $m = [Math]::Max(1.5, $S * 0.06)
        $pts = [System.Drawing.PointF[]]($shape | ForEach-Object { New-Object System.Drawing.PointF ([float]($m + $_[0] * $H)), ([float]($m + $_[1] * $H)) })
        $path = New-Object System.Drawing.Drawing2D.GraphicsPath; $path.AddPolygon($pts)
        # sombra suave
        $sh = $path.Clone(); $mx = New-Object System.Drawing.Drawing2D.Matrix; $mx.Translate([float]($S * 0.025), [float]($S * 0.04)); $sh.Transform($mx)
        foreach ($k in 3, 2, 1) {
            $pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(22, 0, 0, 0)), ([float]($H * 0.06 * $k)); $pen.LineJoin = "Round"
            $g.DrawPath($pen, $sh); $pen.Dispose()
        }
        $g.FillPath((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(70, 0, 0, 0))), $sh)
        # borde blanco y relleno negro
        $wpen = New-Object System.Drawing.Pen ([System.Drawing.Color]::White), ([float]([Math]::Max(1.6, $H * 0.085))); $wpen.LineJoin = "Round"
        $g.DrawPath($wpen, $path)
        $g.FillPath((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 18, 18, 20))), $path)
        $g.Dispose()
        # entrada DIB de 32 bits (como los cursores de Windows) + mascara vacia
        $ms = New-Object System.IO.MemoryStream; $bw = New-Object System.IO.BinaryWriter $ms
        $bw.Write([uint32]40); $bw.Write([int32]$S); $bw.Write([int32]($S * 2)); $bw.Write([uint16]1); $bw.Write([uint16]32)
        $bw.Write([uint32]0); $bw.Write([uint32]0); $bw.Write([int32]0); $bw.Write([int32]0); $bw.Write([uint32]0); $bw.Write([uint32]0)
        $data = $bmp.LockBits((New-Object System.Drawing.Rectangle 0, 0, $S, $S), "ReadOnly", "Format32bppArgb")
        $row = New-Object byte[] ($S * 4)
        for ($y = $S - 1; $y -ge 0; $y--) { [Runtime.InteropServices.Marshal]::Copy([IntPtr]($data.Scan0.ToInt64() + $y * $data.Stride), $row, 0, $row.Length); $bw.Write($row) }
        $bmp.UnlockBits($data); $bmp.Dispose()
        $maskRow = [int]([Math]::Ceiling($S / 32.0) * 4); $bw.Write((New-Object byte[] ($maskRow * $S)))
        $bw.Flush()
        $hot = [int][Math]::Round($m)
        $frames += , @($S, $ms.ToArray(), $hot)
    }
    $curMs = New-Object System.IO.MemoryStream; $w = New-Object System.IO.BinaryWriter $curMs
    $w.Write([uint16]0); $w.Write([uint16]2); $w.Write([uint16]$frames.Count)   # tipo 2 = cursor
    $offset = 6 + 16 * $frames.Count
    foreach ($f in $frames) { $s2 = $f[0]; $w.Write([byte]$s2); $w.Write([byte]$s2); $w.Write([byte]0); $w.Write([byte]0); $w.Write([uint16]$f[2]); $w.Write([uint16]$f[2]); $w.Write([uint32]$f[1].Length); $w.Write([uint32]$offset); $offset += $f[1].Length }
    foreach ($f in $frames) { $w.Write([byte[]]$f[1]) }
    $w.Flush()
    New-Item -ItemType Directory -Force (Split-Path $Out) | Out-Null
    [IO.File]::WriteAllBytes($Out, $curMs.ToArray())
}
