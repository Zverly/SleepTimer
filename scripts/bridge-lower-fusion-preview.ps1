Add-Type -AssemblyName System.Drawing

$sourcePath = 'E:\codex_project\assets\generated\sleep-timer-icon-reference-preview-v3.png'
$outputPath = 'E:\codex_project\assets\generated\sleep-timer-icon-reference-preview-v5.png'
$bitmap = [System.Drawing.Bitmap]::new($sourcePath)
$graphics = [System.Drawing.Graphics]::FromImage($bitmap)
$graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$lavender = [System.Drawing.Color]::FromArgb(255, 204, 191, 255)
$joinPen = [System.Drawing.Pen]::new($lavender, 24)
$joinPen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
$joinPen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
$joinPath = [System.Drawing.Drawing2D.GraphicsPath]::new()
$joinPath.AddBezier(645, 742, 663, 744, 691, 724, 718, 700)
$graphics.DrawPath($joinPen, $joinPath)
$bitmap.Save($outputPath, [System.Drawing.Imaging.ImageFormat]::Png)
$joinPath.Dispose()
$joinPen.Dispose()
$graphics.Dispose()
$bitmap.Dispose()
