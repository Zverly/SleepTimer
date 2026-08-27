Add-Type -AssemblyName System.Drawing

$previewPath = 'E:\codex_project\assets\generated\sleep-timer-icon-reference-preview-v3.png'
$appDirectory = 'E:\codex_project\src\SleepTimer.App\Assets'
$squarePath = Join-Path $appDirectory 'sleep-timer-icon-approved.png'
$circlePath = Join-Path $appDirectory 'sleep-timer-icon-approved-circle.png'
$icoPath = Join-Path $appDirectory 'sleep-timer-icon-approved.ico'
Copy-Item $previewPath $squarePath -Force

$source = [System.Drawing.Bitmap]::new($previewPath)
$circleSize = 512
$sourceCircle = [System.Drawing.Rectangle]::new(162, 165, 720, 720)
$circle = [System.Drawing.Bitmap]::new($circleSize, $circleSize, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$circleGraphics = [System.Drawing.Graphics]::FromImage($circle)
$circleGraphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$circleGraphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
$circleGraphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
$circleGraphics.Clear([System.Drawing.Color]::Transparent)
$circlePathGeometry = [System.Drawing.Drawing2D.GraphicsPath]::new()
$circlePathGeometry.AddEllipse(0, 0, $circleSize - 1, $circleSize - 1)
$circleGraphics.SetClip($circlePathGeometry, [System.Drawing.Drawing2D.CombineMode]::Replace)
$circleGraphics.DrawImage($source, [System.Drawing.Rectangle]::new(0, 0, $circleSize, $circleSize), $sourceCircle, [System.Drawing.GraphicsUnit]::Pixel)
$circle.Save($circlePath, [System.Drawing.Imaging.ImageFormat]::Png)

$pngBytes = [System.IO.File]::ReadAllBytes($squarePath)
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
$circlePathGeometry.Dispose()
$circleGraphics.Dispose()
$circle.Dispose()
$source.Dispose()
