# 生成应用图标（原创绘制，无第三方素材，无版权问题）
# 用法：pwsh -NoProfile -File scripts\make-icon.ps1
#
# 图形含义：蓝色圆角方块 + 两个白色分栏，直观表达"多栏文件管理器"。

Add-Type -AssemblyName System.Drawing

$outDir = Join-Path $PSScriptRoot '..\src\Files.App\Assets'
New-Item -ItemType Directory -Force -Path $outDir | Out-Null
$outPath = [System.IO.Path]::GetFullPath((Join-Path $outDir 'Files.ico'))

$sizes = @(16, 24, 32, 48, 64, 128, 256)
$pngList = New-Object System.Collections.ArrayList

foreach ($size in $sizes) {
    $s = [single]$size
    $bmp = New-Object System.Drawing.Bitmap($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.Clear([System.Drawing.Color]::Transparent)

    $pad = [single]($s * 0.04)
    $bodyW = [single]($s - 2 * $pad)
    $r = [single]($s * 0.20)

    # 背景圆角方块（用四段弧拼成）
    $bg = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = [single]($r * 2)
    $x0 = $pad
    $y0 = $pad
    $x1 = [single]($pad + $bodyW)
    $y1 = [single]($pad + $bodyW)
    $bg.AddArc($x0, $y0, $d, $d, 180, 90)
    $bg.AddArc([single]($x1 - $d), $y0, $d, $d, 270, 90)
    $bg.AddArc([single]($x1 - $d), [single]($y1 - $d), $d, $d, 0, 90)
    $bg.AddArc($x0, [single]($y1 - $d), $d, $d, 90, 90)
    $bg.CloseFigure()

    $bgBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 0, 120, 212))
    $g.FillPath($bgBrush, $bg)

    # 两个白色分栏
    $inner = [single]($s * 0.17)
    $gap = [single]($s * 0.07)
    $paneW = [single](($s - 2 * $inner - $gap) / 2)
    $paneH = [single]($s - 2 * $inner)
    $pr = [single]([Math]::Max(1.0, $s * 0.045))

    $white = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(242, 255, 255, 255))

    $leftX = $inner
    $rightX = [single]($inner + $paneW + $gap)

    foreach ($px in @($leftX, $rightX)) {
        $p = New-Object System.Drawing.Drawing2D.GraphicsPath
        $pd = [single]($pr * 2)
        $p.AddArc($px, $inner, $pd, $pd, 180, 90)
        $p.AddArc([single]($px + $paneW - $pd), $inner, $pd, $pd, 270, 90)
        $p.AddArc([single]($px + $paneW - $pd), [single]($inner + $paneH - $pd), $pd, $pd, 0, 90)
        $p.AddArc($px, [single]($inner + $paneH - $pd), $pd, $pd, 90, 90)
        $p.CloseFigure()
        $g.FillPath($white, $p)
        $p.Dispose()
    }

    $g.Dispose()
    $bgBrush.Dispose()
    $white.Dispose()
    $bg.Dispose()

    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    [void]$pngList.Add([pscustomobject]@{ Size = $size; Data = $ms.ToArray() })
    $bmp.Dispose()
    $ms.Dispose()
}

# 组装 ICO（Vista+ 支持 PNG 压缩条目）
# 直接用字节列表拼装，避免依赖 BinaryWriter/MemoryStream 的 PowerShell 绑定问题
$bytes = [System.Collections.Generic.List[byte]]::new()

$bytes.AddRange([System.BitConverter]::GetBytes([UInt16]0))                 # reserved
$bytes.AddRange([System.BitConverter]::GetBytes([UInt16]1))                 # type = icon
$bytes.AddRange([System.BitConverter]::GetBytes([UInt16]$pngList.Count))    # count

$offset = 6 + 16 * $pngList.Count
foreach ($entry in $pngList) {
    $dim = if ($entry.Size -ge 256) { [byte]0 } else { [byte]$entry.Size }
    $bytes.Add($dim)                                                        # width
    $bytes.Add($dim)                                                        # height
    $bytes.Add([byte]0)                                                     # color count
    $bytes.Add([byte]0)                                                     # reserved
    $bytes.AddRange([System.BitConverter]::GetBytes([UInt16]1))             # planes
    $bytes.AddRange([System.BitConverter]::GetBytes([UInt16]32))            # bit count
    $bytes.AddRange([System.BitConverter]::GetBytes([UInt32]$entry.Data.Length))
    $bytes.AddRange([System.BitConverter]::GetBytes([UInt32]$offset))
    $offset += $entry.Data.Length
}

foreach ($entry in $pngList) {
    $bytes.AddRange([byte[]]$entry.Data)
}

[System.IO.File]::WriteAllBytes($outPath, $bytes.ToArray())

$info = Get-Item $outPath
Write-Host ("OK {0}  {1} bytes  {2} sizes" -f $info.FullName, $info.Length, $pngList.Count)
