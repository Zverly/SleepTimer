Add-Type -AssemblyName System.Drawing

$sourcePath = 'E:\codex_project\assets\generated\sleep-timer-icon-reference-preview-v2.png'
$outputPath = 'E:\codex_project\assets\generated\sleep-timer-icon-reference-preview-v3.png'
$source = [System.Drawing.Bitmap]::new($sourcePath)
$refined = [System.Drawing.Bitmap]::new($source.Width, $source.Height, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)

for ($y = 0; $y -lt $source.Height; $y++) {
    $vertical = $y / [double]($source.Height - 1)
    $background = [System.Drawing.Color]::FromArgb(255, [int](27 - 10 * $vertical), [int](39 - 14 * $vertical), [int](64 - 24 * $vertical))
    $foreground = [System.Drawing.Color]::FromArgb(255, [int](220 - 24 * $vertical), [int](209 - 26 * $vertical), [int](255 - 8 * $vertical))
    for ($x = 0; $x -lt $source.Width; $x++) {
        $pixel = $source.GetPixel($x, $y)
        $isForeground = $pixel.R -gt 100 -and $pixel.B -gt ($pixel.R + 10)
        $refined.SetPixel($x, $y, $(if ($isForeground) { $foreground } else { $background }))
    }
}

$refined.Save($outputPath, [System.Drawing.Imaging.ImageFormat]::Png)
$source.Dispose()
$refined.Dispose()
