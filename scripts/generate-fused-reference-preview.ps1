Add-Type -AssemblyName System.Drawing

$size = 1024
$bitmap = [System.Drawing.Bitmap]::new($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$graphics = [System.Drawing.Graphics]::FromImage($bitmap)
$graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$graphics.Clear([System.Drawing.Color]::FromArgb(255, 20, 31, 52))

$lavender = [System.Drawing.Color]::FromArgb(255, 204, 191, 255)
$background = [System.Drawing.Color]::FromArgb(255, 20, 31, 52)
$cutoutBrush = [System.Drawing.SolidBrush]::new($background)
$clockPen = [System.Drawing.Pen]::new($lavender, 30)
$clockPen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
$clockPen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
$moonArc = [System.Drawing.RectangleF]::new(88, 116, 790, 790)
$moonArcPen = [System.Drawing.Pen]::new($lavender, 112)
$moonArcPen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
$moonArcPen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
$graphics.DrawArc($moonArcPen, $moonArc, 132, 236)
$clock = [System.Drawing.RectangleF]::new(272, 174, 650, 650)
$graphics.DrawEllipse($clockPen, $clock)

$crownBrush = [System.Drawing.SolidBrush]::new($lavender)
$graphics.FillRectangle($crownBrush, 874, 410, 74, 156)
$graphics.FillRectangle($cutoutBrush, 892, 432, 38, 112)

$center = [System.Drawing.PointF]::new(571, 489)
foreach ($tick in @(30, 60, 90, 120, 150, 210, 240, 270, 300, 330)) {
    $angle = ($tick - 90) * [Math]::PI / 180
    $outer = [System.Drawing.PointF]::new($center.X + [Math]::Cos($angle) * 272, $center.Y + [Math]::Sin($angle) * 272)
    $inner = [System.Drawing.PointF]::new($center.X + [Math]::Cos($angle) * 228, $center.Y + [Math]::Sin($angle) * 228)
    $graphics.DrawLine($clockPen, $inner, $outer)
}
$graphics.DrawLine($clockPen, $center, [System.Drawing.PointF]::new(571, 292))
$graphics.DrawLine($clockPen, $center, [System.Drawing.PointF]::new(690, 406))
$graphics.FillEllipse($crownBrush, [System.Drawing.RectangleF]::new(558, 476, 26, 26))

$output = 'E:\codex_project\assets\generated\sleep-timer-icon-reference-preview.png'
$bitmap.Save($output, [System.Drawing.Imaging.ImageFormat]::Png)
$graphics.Dispose()
$moonArcPen.Dispose()
$cutoutBrush.Dispose()
$crownBrush.Dispose()
$clockPen.Dispose()
$bitmap.Dispose()
