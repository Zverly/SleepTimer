Add-Type -AssemblyName System.Drawing

$canvasSize = 1024
$bitmap = [System.Drawing.Bitmap]::new($canvasSize, $canvasSize, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$graphics = [System.Drawing.Graphics]::FromImage($bitmap)
$graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
$graphics.Clear([System.Drawing.Color]::Transparent)

$background = [System.Drawing.RectangleF]::new(80, 80, 864, 864)
$path = [System.Drawing.Drawing2D.GraphicsPath]::new()
$path.AddArc($background.X, $background.Y, 220, 220, 180, 90)
$path.AddArc($background.Right - 220, $background.Y, 220, 220, 270, 90)
$path.AddArc($background.Right - 220, $background.Bottom - 220, 220, 220, 0, 90)
$path.AddArc($background.X, $background.Bottom - 220, 220, 220, 90, 90)
$path.CloseFigure()
$backgroundBrush = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(255, 17, 26, 47))
$graphics.FillPath($backgroundBrush, $path)

$moonBrush = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(255, 207, 196, 255))
$moon = [System.Drawing.RectangleF]::new(260, 220, 420, 420)
$graphics.FillEllipse($moonBrush, $moon)
$cutoutBrush = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(255, 17, 26, 47))
$graphics.FillEllipse($cutoutBrush, [System.Drawing.RectangleF]::new(380, 160, 420, 420))

$clockPen = [System.Drawing.Pen]::new([System.Drawing.Color]::FromArgb(255, 207, 196, 255), 30)
$clockPen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
$clockPen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
$clock = [System.Drawing.RectangleF]::new(470, 500, 300, 300)
$graphics.DrawEllipse($clockPen, $clock)
$center = [System.Drawing.PointF]::new(620, 650)
$graphics.DrawLine($clockPen, $center, [System.Drawing.PointF]::new(620, 570))
$graphics.DrawLine($clockPen, $center, [System.Drawing.PointF]::new(690, 690))
$graphics.FillEllipse($moonBrush, [System.Drawing.RectangleF]::new(605, 635, 30, 30))

$outputDirectory = 'E:\codex_project\assets\generated'
$appDirectory = 'E:\codex_project\src\SleepTimer.App\Assets'
New-Item -ItemType Directory -Force -Path $outputDirectory, $appDirectory | Out-Null
foreach ($size in @(1024, 256, 64, 32)) {
    $target = [System.Drawing.Bitmap]::new($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $targetGraphics = [System.Drawing.Graphics]::FromImage($target)
    $targetGraphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
    $targetGraphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $targetGraphics.DrawImage($bitmap, 0, 0, $size, $size)
    $target.Save((Join-Path $outputDirectory "sleep-timer-icon-v2-$size.png"), [System.Drawing.Imaging.ImageFormat]::Png)
    if ($size -eq 256) { $target.Save((Join-Path $appDirectory 'sleep-timer-icon-v2.png'), [System.Drawing.Imaging.ImageFormat]::Png) }
    $targetGraphics.Dispose()
    $target.Dispose()
}
$pngPath = Join-Path $appDirectory 'sleep-timer-icon-v2.png'
$pngBytes = [System.IO.File]::ReadAllBytes($pngPath)
$icoPath = Join-Path $appDirectory 'sleep-timer-icon-v2.ico'
$stream = [System.IO.MemoryStream]::new()
$writer = [System.IO.BinaryWriter]::new($stream)
$writer.Write([uint16]0)
$writer.Write([uint16]1)
$writer.Write([uint16]1)
$writer.Write([byte]0)
$writer.Write([byte]0)
$writer.Write([byte]0)
$writer.Write([byte]0)
$writer.Write([uint16]1)
$writer.Write([uint16]32)
$writer.Write([uint32]$pngBytes.Length)
$writer.Write([uint32]22)
$writer.Write($pngBytes)
$writer.Flush()
[System.IO.File]::WriteAllBytes($icoPath, $stream.ToArray())
$writer.Dispose()
$stream.Dispose()
$graphics.Dispose()
$backgroundBrush.Dispose()
$moonBrush.Dispose()
$cutoutBrush.Dispose()
$clockPen.Dispose()
$path.Dispose()
$bitmap.Dispose()
