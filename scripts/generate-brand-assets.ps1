Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName System.Drawing

$Root = Split-Path -Parent $PSScriptRoot
$Assets = Join-Path $Root 'assets'
New-Item -ItemType Directory -Force -Path $Assets | Out-Null

function New-RoundedPath {
    param([float]$X,[float]$Y,[float]$Width,[float]$Height,[float]$Radius)
    $path = [System.Drawing.Drawing2D.GraphicsPath]::new()
    $diameter = $Radius * 2
    $path.AddArc($X, $Y, $diameter, $diameter, 180, 90)
    $path.AddArc($X + $Width - $diameter, $Y, $diameter, $diameter, 270, 90)
    $path.AddArc($X + $Width - $diameter, $Y + $Height - $diameter, $diameter, $diameter, 0, 90)
    $path.AddArc($X, $Y + $Height - $diameter, $diameter, $diameter, 90, 90)
    $path.CloseFigure()
    return $path
}

function Add-Line {
    param($Graphics,[System.Drawing.Color]$Color,[float]$Width,[float[]]$Points)
    $pen = [System.Drawing.Pen]::new($Color, $Width)
    $pen.StartCap = $pen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $pen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
    try {
        for ($i = 0; $i -lt $Points.Count - 2; $i += 2) {
            $Graphics.DrawLine($pen, $Points[$i], $Points[$i+1], $Points[$i+2], $Points[$i+3])
        }
    } finally { $pen.Dispose() }
}

function New-LogoBitmap {
    param([int]$Size)
    $bitmap = [System.Drawing.Bitmap]::new($Size, $Size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bitmap)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $s = $Size / 1024.0

    $tile = New-RoundedPath 0 0 ($Size-1) ($Size-1) (194*$s)
    $g.FillPath([System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(10,17,29)), $tile)

    $g.FillPolygon([System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(14,24,40)), @(
        [System.Drawing.PointF]::new(0,716*$s), [System.Drawing.PointF]::new(737*$s,0),
        [System.Drawing.PointF]::new($Size,0), [System.Drawing.PointF]::new($Size,287*$s),
        [System.Drawing.PointF]::new(287*$s,$Size)
    ))

    $gridPen = [System.Drawing.Pen]::new([System.Drawing.Color]::FromArgb(55,28,41,60), 2*$s)
    for ($k = -$Size; $k -lt $Size*2; $k += [int](92*$s)) {
        $g.DrawLine($gridPen, $k, 0, $k-$Size, $Size)
    }
    $gridPen.Dispose()

    $shadow = @(
        [System.Drawing.PointF]::new(282*$s,314*$s), [System.Drawing.PointF]::new(400*$s,270*$s),
        [System.Drawing.PointF]::new(624*$s,270*$s), [System.Drawing.PointF]::new(742*$s,314*$s),
        [System.Drawing.PointF]::new(724*$s,618*$s), [System.Drawing.PointF]::new(647*$s,759*$s),
        [System.Drawing.PointF]::new(512*$s,852*$s), [System.Drawing.PointF]::new(377*$s,759*$s),
        [System.Drawing.PointF]::new(300*$s,618*$s)
    )
    $g.FillPolygon([System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(150,0,0,0)), $shadow)

    $shield = @(
        [System.Drawing.PointF]::new(267*$s,286*$s), [System.Drawing.PointF]::new(389*$s,241*$s),
        [System.Drawing.PointF]::new(635*$s,241*$s), [System.Drawing.PointF]::new(757*$s,286*$s),
        [System.Drawing.PointF]::new(737*$s,600*$s), [System.Drawing.PointF]::new(656*$s,747*$s),
        [System.Drawing.PointF]::new(512*$s,846*$s), [System.Drawing.PointF]::new(368*$s,747*$s),
        [System.Drawing.PointF]::new(287*$s,600*$s)
    )
    $g.FillPolygon([System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(25,38,57)), $shield)
    $g.DrawPolygon([System.Drawing.Pen]::new([System.Drawing.Color]::FromArgb(221,174,77), 22*$s), $shield)

    $inner = @(
        [System.Drawing.PointF]::new(330*$s,330*$s), [System.Drawing.PointF]::new(418*$s,297*$s),
        [System.Drawing.PointF]::new(606*$s,297*$s), [System.Drawing.PointF]::new(694*$s,330*$s),
        [System.Drawing.PointF]::new(681*$s,576*$s), [System.Drawing.PointF]::new(617*$s,693*$s),
        [System.Drawing.PointF]::new(512*$s,766*$s), [System.Drawing.PointF]::new(407*$s,693*$s),
        [System.Drawing.PointF]::new(343*$s,576*$s)
    )
    $g.DrawPolygon([System.Drawing.Pen]::new([System.Drawing.Color]::FromArgb(250,218,146), 5*$s), $inner)

    $red = @(
        [System.Drawing.PointF]::new(315*$s,409*$s), [System.Drawing.PointF]::new(625*$s,279*$s),
        [System.Drawing.PointF]::new(720*$s,443*$s), [System.Drawing.PointF]::new(420*$s,682*$s)
    )
    $g.FillPolygon([System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(123,31,44)), $red)

    Add-Line $g ([System.Drawing.Color]::FromArgb(250,218,146)) (16*$s) @(365*$s,363*$s,365*$s,292*$s,425*$s,332*$s,475*$s,251*$s,525*$s,332*$s,585*$s,251*$s,646*$s,332*$s,646*$s,363*$s)
    Add-Line $g ([System.Drawing.Color]::FromArgb(221,174,77)) (14*$s) @(365*$s,374*$s,646*$s,374*$s)

    $cyan = [System.Drawing.Color]::FromArgb(75,201,219)
    Add-Line $g $cyan (10*$s) @(389*$s,524*$s,512*$s,620*$s,635*$s,524*$s)
    Add-Line $g $cyan (10*$s) @(389*$s,524*$s,635*$s,524*$s)
    Add-Line $g $cyan (10*$s) @(512*$s,620*$s,512*$s,724*$s)

    foreach ($node in @(@(389,524,30),@(635,524,30),@(512,620,36),@(512,724,24))) {
        $x=$node[0]*$s; $y=$node[1]*$s; $r=$node[2]*$s
        $g.FillEllipse([System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(246,248,250)), $x-$r, $y-$r, $r*2, $r*2)
        $g.DrawEllipse([System.Drawing.Pen]::new($cyan, 7*$s), $x-$r, $y-$r, $r*2, $r*2)
    }

    $g.Dispose()
    $tile.Dispose()
    return $bitmap
}

function Save-PngIconAsIco {
    param([System.Drawing.Bitmap]$Bitmap,[string]$Path)
    $small = [System.Drawing.Bitmap]::new(256,256,[System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($small)
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.DrawImage($Bitmap,0,0,256,256)
    $g.Dispose()
    $stream = [System.IO.MemoryStream]::new()
    $small.Save($stream,[System.Drawing.Imaging.ImageFormat]::Png)
    $small.Dispose()
    $png = $stream.ToArray(); $stream.Dispose()
    $file = [System.IO.File]::Create($Path)
    $writer = [System.IO.BinaryWriter]::new($file)
    $writer.Write([UInt16]0); $writer.Write([UInt16]1); $writer.Write([UInt16]1)
    $writer.Write([byte]0); $writer.Write([byte]0); $writer.Write([byte]0); $writer.Write([byte]0)
    $writer.Write([UInt16]1); $writer.Write([UInt16]32); $writer.Write([UInt32]$png.Length); $writer.Write([UInt32]22)
    $writer.Write($png); $writer.Dispose(); $file.Dispose()
}

$logo = New-LogoBitmap 1024
$logo.Save((Join-Path $Assets 'app_icon.png'), [System.Drawing.Imaging.ImageFormat]::Png)
Save-PngIconAsIco $logo (Join-Path $Assets 'app.ico')

$banner = [System.Drawing.Bitmap]::new(1280,640,[System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$g = [System.Drawing.Graphics]::FromImage($banner)
$g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
$g.Clear([System.Drawing.Color]::FromArgb(10,17,29))
$g.FillPolygon([System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(14,24,40)), @(
    [System.Drawing.PointF]::new(0,640),[System.Drawing.PointF]::new(690,0),
    [System.Drawing.PointF]::new(940,0),[System.Drawing.PointF]::new(250,640)
))
$g.FillPolygon([System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(65,123,31,44)), @(
    [System.Drawing.PointF]::new(0,545),[System.Drawing.PointF]::new(515,0),
    [System.Drawing.PointF]::new(650,0),[System.Drawing.PointF]::new(135,640),[System.Drawing.PointF]::new(0,640)
))
$grid = [System.Drawing.Pen]::new([System.Drawing.Color]::FromArgb(22,255,255,255),1)
for ($x=0;$x -le 1280;$x+=80){$g.DrawLine($grid,$x,0,$x,640)}
for ($y=0;$y -le 640;$y+=80){$g.DrawLine($grid,0,$y,1280,$y)}
$grid.Dispose()
$g.DrawImage($logo,62,105,420,420)

$cyanBrush=[System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(75,201,219))
$goldBrush=[System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(250,218,146))
$whiteBrush=[System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(246,248,250))
$mutedBrush=[System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(174,188,205))
$eyebrow=[System.Drawing.Font]::new('Segoe UI Semibold',24)
$title=[System.Drawing.Font]::new('Georgia',96,[System.Drawing.FontStyle]::Bold)
$subtitle=[System.Drawing.Font]::new('Segoe UI Semibold',34)
$body=[System.Drawing.Font]::new('Segoe UI',23)
$chip=[System.Drawing.Font]::new('Segoe UI Semibold',18)
$footer=[System.Drawing.Font]::new('Segoe UI',18)
$g.DrawString('WINDOWS UTILITY',$eyebrow,$cyanBrush,550,108)
$g.FillRectangle([System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(221,174,77)),550,151,122,6)
$g.DrawString('CK3MPS',$title,$whiteBrush,540,166)
$g.DrawString('Crusader Kings III',$subtitle,$goldBrush,550,301)
$g.DrawString('Multiplayer Stabilizer',$subtitle,$whiteBrush,550,344)
$g.DrawString('Cleaner profiles. Safer settings. Better multiplayer readiness.',$body,$mutedBrush,550,414)
$chipX=550
foreach($label in @('OOS prevention','Network diagnostics','Safe cleanup')){
    $measure=$g.MeasureString($label,$chip); $width=[int]$measure.Width+32
    $path=New-RoundedPath $chipX 478 $width 42 20
    $g.FillPath([System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(31,46,67)),$path)
    $g.DrawPath([System.Drawing.Pen]::new([System.Drawing.Color]::FromArgb(78,101,126),1),$path)
    $g.DrawString($label,$chip,$whiteBrush,$chipX+16,486)
    $chipX += $width+12; $path.Dispose()
}
$g.DrawString('github.com/Danissemo/CK3MPS',$footer,$mutedBrush,550,562)
$g.Dispose()
$banner.Save((Join-Path $Assets 'social-preview.png'),[System.Drawing.Imaging.ImageFormat]::Png)
$banner.Dispose(); $logo.Dispose()

Write-Host 'Generated assets/app.ico, assets/app_icon.png and assets/social-preview.png'
